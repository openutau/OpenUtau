using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NAudio.Wave;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Audio;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.UiTest {
    public class SingerPortraitTest {
        private sealed class Singer : USinger {
            public Singer(string location, Dictionary<string, string> paths, float opacity = 1) {
                Location = location;
                VoiceColorPortraits = paths;
                PortraitOpacity = opacity;
                found = loaded = true;
            }
            public override string Id => Location;
            public override string Name => "Portrait test";
            public override USingerType SingerType => USingerType.Classic;
            public override string Location { get; }
            public override string Portrait => Path.Combine(Location, "default.png");
            public override float PortraitOpacity { get; }
            public override IReadOnlyDictionary<string, string> VoiceColorPortraits { get; }
            public override IList<USubbank> Subbanks => new[] { "", "Power", "Soft", "Whisper" }
                .Select(color => new USubbank(new Subbank { Color = color })).ToList();
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = UOto.OfDummy(phoneme);
                return true;
            }
            public override byte[] LoadPortrait() => File.ReadAllBytes(Portrait);
        }

        // Drive the production playback clock at known sample positions without a sound device.
        private sealed class Output : IAudioOutput {
            public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
            public long Position { get; set; }
            public int DeviceNumber => 0;
            public void Pause() => PlaybackState = PlaybackState.Paused;
            public void Play() => PlaybackState = PlaybackState.Playing;
            public void Stop() => PlaybackState = PlaybackState.Stopped;
            public long GetPosition() => Position;
            public List<AudioOutputDevice> GetOutputDevices() => new();
            public void Init(ISampleProvider provider) { }
            public void SelectDevice(Guid guid, int deviceNumber) { }
        }

        [Fact]
        public void PlaybackSelectsEachPhonemePortrait() => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            var doc = DocManager.Inst;
            var previous = doc.Project;
            var playback = PlaybackManager.Inst;
            var device = playback.AudioOutput;
            var show = Preferences.Default.ShowPortrait;
            var reduce = Preferences.Default.ReduceAnimations;
            var preRender = Preferences.Default.PreRender;
            int threshold = Preferences.Default.PortraitSilenceMs;
            Preferences.Default.PortraitSilenceMs = 600;
            var vm = new NotesViewModel();
            try {
                Preferences.Default.PreRender = false;
                Preferences.Default.ReduceAnimations = false;
                Preferences.Default.ShowPortrait = true;
                var project = Core.Format.Ustx.Create();
                doc.ExecuteCmd(new LoadProjectNotification(project));
                var output = new Output();
                playback.AudioOutput = output;
                var paths = new Dictionary<string, string> { ["Power"] = "red.png", ["Soft"] = "blue.png", ["Whisper"] = "green.png" };
                var singer = new Singer("first", paths);
                var track = project.tracks[0];
                track.Singer = singer;
                track.Validate(new ValidateOptions(), project);
                var part = new UVoicePart { position = 960, duration = 2880, name = "take" };
                project.parts.Add(part);
                var note = project.CreateNote(60, 96, 720);
                note.ExtendedDuration = note.duration;
                part.notes.Add(note);
                var colors = new[] { "Power", "Soft", "Whisper" };
                for (int i = 0; i < 3; i++) {
                    var ph = new UPhoneme { index = i, Parent = note, position = 96 + 240 * i, phoneme = new[] { "k", "a", "i" }[i] };
                    ph.SetExpression(project, track, Core.Format.Ustx.CLR, Array.IndexOf(track.VoiceColorExp.options, colors[i]));
                    part.phonemes.Add(ph);
                }
                for (int i = 0; i < 3; i++) {
                    var ph = part.phonemes[i];
                    if (i < 2) { ph.Next = part.phonemes[i + 1]; }
                    ph.Validate(new ValidateOptions(), project, track, part, note);
                    Assert.False(ph.Error);
                }
                foreach (int start in new[] { 1464, 2448, 2688 }) {
                    var later = project.CreateNote(60, start, 192);
                    later.ExtendedDuration = later.duration;
                    part.notes.Add(later);
                    var next = new UPhoneme { Parent = later, position = start, phoneme = "a" };
                    part.phonemes.Add(next);
                    next.Validate(new ValidateOptions(), project, track, part, later);
                    Assert.False(next.Error);
                }
                vm.OnNext(new LoadPartNotification(part, project, 0), false);
                Assert.Null(vm.Portrait!.ColorPath);
                void Tick(int tick) {
                    double elapsedMs = project.timeAxis.TickPosToMsPos(part.position + tick) - project.timeAxis.TickPosToMsPos(part.position);
                    output.Position = sizeof(float) * (long)Math.Round(elapsedMs * 44100 / 1000);
                    playback.UpdatePlayPos();
                }
                playback.Play(project, part.position);
                Assert.True(SpinWait.SpinUntil(() => {
                    HeadlessUi.Flush();
                    return !playback.StartingToPlay && output.PlaybackState == PlaybackState.Playing;
                }, 5000), "Playback did not start.");
                // Seek before any playback update, as when opening the part with the playhead in a gap.
                Preferences.Default.PortraitSilenceMs = 225;
                foreach (var (tick, expected) in new[] { (1031, 1.0), (1032, 1.0), (1104, 0.75), (1176, 0.5) }) {
                    vm.OnNext(new LoadPartNotification(part, project, 0), false);
                    Tick(tick);
                    Assert.Equal("green.png", vm.Portrait!.ColorPath);
                    Assert.Equal(expected, vm.Portrait.OpacityFactor, 5);
                }
                Tick(1608); // The later, uncolored phoneme is fully restored.
                Assert.Null(vm.Portrait.ColorPath);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Tick(1104); // Backward into the previous gap.
                Assert.Equal("green.png", vm.Portrait.ColorPath);
                Assert.Equal(0.75, vm.Portrait.OpacityFactor, 5);
                Tick(336); // Directly on the Soft phoneme.
                Assert.Equal("blue.png", vm.Portrait.ColorPath);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Tick(1176); // Forward across an entire phoneme into silence.
                Assert.Equal("green.png", vm.Portrait.ColorPath);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                Tick(1464);
                Assert.Null(vm.Portrait.ColorPath);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                Tick(1536);
                Assert.Equal(0.75, vm.Portrait.OpacityFactor, 5);
                Tick(1608);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Preferences.Default.PortraitSilenceMs = 600;
                Tick(95);
                Assert.Null(vm.Portrait.ColorPath);
                Tick(96);
                Assert.Equal("red.png", vm.Portrait.ColorPath);
                Tick(335);
                Assert.Equal("red.png", vm.Portrait.ColorPath);
                Tick(336);
                Assert.Equal("blue.png", vm.Portrait.ColorPath);
                vm.OnNext(new SetPlayPosTickNotification(part.position + 576, waitingRendering: true), false);
                Assert.Equal("blue.png", vm.Portrait.ColorPath);
                Tick(576);
                Assert.Equal("green.png", vm.Portrait.ColorPath);
                Tick(816);
                Assert.Equal("green.png", vm.Portrait.ColorPath);
                // At 120 BPM, 576 ticks = 600 ms and 144 ticks = 150 ms.
                var prefs = Preferences.Default;
                try {
                    prefs.AdvancedPortraitSettings = false;
                    Tick(900);
                    Assert.Null(vm.Portrait.ColorPath);
                    Assert.Equal(1, vm.Portrait.OpacityFactor);
                    Assert.Equal(0, vm.Portrait.FadeMs);
                    Tick(576);
                    Assert.Null(vm.Portrait.ColorPath);
                    prefs.AdvancedPortraitSettings = true;
                    Tick(577);
                    Assert.Equal("green.png", vm.Portrait.ColorPath);
                    prefs.UseVoiceColorPortraits = false;
                    Tick(580);
                    Assert.Null(vm.Portrait.ColorPath);
                    prefs.UseVoiceColorPortraits = true;
                    prefs.PortraitSilenceMs = 200;
                    prefs.PortraitFadeMs = 100;
                    Tick(1056);
                    Assert.Equal(0.75, vm.Portrait.OpacityFactor, 5);
                    Assert.Equal("green.png", vm.Portrait.ColorPath);
                    prefs.DimPortraitDuringSilence = false;
                    Tick(1200);
                    Assert.Equal(1, vm.Portrait.OpacityFactor);
                    prefs.DimPortraitDuringSilence = true;
                    prefs.PortraitFadeMs = 0;
                    Tick(1210);
                    Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                } finally {
                    prefs.AdvancedPortraitSettings = prefs.DimPortraitDuringSilence = prefs.UseVoiceColorPortraits = true;
                    prefs.PortraitFadeMs = 150;
                    prefs.PortraitSilenceMs = 600;
                }
                Tick(1391);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Tick(1392);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Tick(1428);
                Assert.Equal(0.875, vm.Portrait.OpacityFactor, 5);
                Tick(1464);
                Assert.Null(vm.Portrait.ColorPath);
                Assert.Equal(0.75, vm.Portrait.OpacityFactor, 5);
                Tick(1536);
                Assert.Equal(0.875, vm.Portrait.OpacityFactor, 5);
                Tick(1608);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                // Skip all UI updates during this entire silence.
                Tick(2376);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                playback.PausePlayback();
                vm.OnNext(new SetPlayPosTickNotification(0, pause: true), false);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                playback.Play(project, part.position);
                Tick(2448);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                Tick(2520);
                Assert.Equal(0.75, vm.Portrait.OpacityFactor, 5);
                Tick(2592);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Tick(2687);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Tick(2688);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Tick(576);
                Assert.Equal("green.png", vm.Portrait.ColorPath);
                playback.PausePlayback();
                vm.OnNext(new SetPlayPosTickNotification(0, pause: true), false);
                Assert.Equal("green.png", vm.Portrait.ColorPath);
                playback.Play(project, part.position + 336);
                Tick(336);
                Assert.Equal("blue.png", vm.Portrait.ColorPath);
                paths.Remove("Power");
                Tick(96);
                Assert.Null(vm.Portrait.ColorPath);
                Tick(2376);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                playback.StopPlayback();
                vm.OnNext(new SetPlayPosTickNotification(0), false);
                Assert.Null(vm.Portrait.ColorPath);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
                var change = new TrackChangeSingerCommand(project, track, new Singer("next", new()));
                change.Execute();
                vm.OnNext(change, false);
                Assert.Equal("next", vm.Portrait.Singer.Id);
                Assert.Equal(1, vm.Portrait.OpacityFactor);
                Assert.Null(vm.Portrait.ColorPath);
                // The new singer has just a default portrait. It still follows silence.
                playback.Play(project, part.position);
                Assert.True(SpinWait.SpinUntil(() => {
                    HeadlessUi.Flush();
                    return !playback.StartingToPlay && output.PlaybackState == PlaybackState.Playing;
                }, 5000));
                Tick(96);
                Tick(2376);
                Assert.Null(vm.Portrait.ColorPath);
                Assert.Equal(0.5, vm.Portrait.OpacityFactor);
            } finally {
                playback.StopPlayback();
                playback.AudioOutput = device;
                Preferences.Default.ShowPortrait = show;
                Preferences.Default.PreRender = preRender;
                Preferences.Default.PortraitSilenceMs = threshold;
                Preferences.Default.ReduceAnimations = reduce;
                doc.RemoveSubscriber(vm);
                doc.ExecuteCmd(new LoadProjectNotification(previous));
            }
        });

        [Theory]
        [InlineData(1f)]
        [InlineData(0.6f)]
        [InlineData(0.8f)]
        public void UsesEachSingersConfiguredOpacity(float opacity) => HeadlessUi.Run(() => {
            using var yaml = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"portrait: default.png\nportrait_opacity: {opacity}\n")));
            var config = VoicebankConfig.Load(yaml);
            var bank = new Voicebank();
            VoicebankLoader.ApplyConfig(bank, config);
            var singer = new Singer("portrait test", new(), bank.PortraitOpacity);
            var control = new SingerPortrait();
            var window = new Window { Content = control };
            try {
                window.Show();
                control.Selection = new PortraitSelection(singer, 1);
                Assert.Equal((double)opacity, control.Opacity, 6);
                control.Selection = control.Selection with { OpacityFactor = 0.5 };
                Assert.Equal(opacity * 0.5, control.Opacity, 6);
                control.Selection = control.Selection with { OpacityFactor = 1 };
                Assert.Equal((double)opacity, control.Opacity, 6);
                control.Selection = new PortraitSelection(new Singer("second", new(), 0.42f), 2);
                Assert.Equal(0.42, control.Opacity, 6);
            } finally {
                window.Close();
            }
        });

        private static void WriteColor(string path, int bgra) => WritePortrait(path, _ => bgra);

        private static void WritePortrait(string path, Func<int, int> premultipliedBgra) {
            using var image = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var pixels = image.Lock()) {
                for (int y = 0; y < 16; y++) {
                    for (int x = 0; x < 16; x++) {
                        Marshal.WriteInt32(pixels.Address, y * pixels.RowBytes + x * 4, premultipliedBgra(x));
                    }
                }
            }
            image.Save(path, new PngBitmapEncoderOptions());
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(0.6f)]
        public void CrossfadePreservesBrightnessAndPngAlpha(float opacity) => HeadlessUi.Run(() => {
            var folder = Directory.CreateTempSubdirectory();
            var singer = new Singer(folder.FullName, new(), opacity);
            var control = new SingerPortrait();
            const int background = 48;
            var window = new Window { Content = control, Width = 240, Height = 80,
                Background = new SolidColorBrush(Color.FromRgb(background, background, background)) };
            try {
                int Stripe(int x, bool second) => x switch {
                    < 3 => -1,                           // Both opaque white.
                    < 6 => unchecked((int)0x80808080),   // Both half-transparent white.
                    < 9 => 0,                            // Both transparent.
                    < 12 => second ? 0 : -1,             // Opaque white fading to clear.
                    _ => second ? unchecked((int)0xff0000ff) : unchecked((int)0xffff0000),
                };
                WritePortrait(singer.Portrait, x => Stripe(x, false));
                WritePortrait(Path.Combine(folder.FullName, "next.png"), x => Stripe(x, true));
                window.Show();
                control.Selection = new PortraitSelection(singer, 1) { FadeMs = 1000 };
                int solid = (int)Math.Round(background + (255 - background) * opacity);
                int half = (int)Math.Round(background + (255 - background) * opacity * 128 / 255);
                int redLeft = (int)Math.Round(background * (1 - opacity));

                // The marker changes from red to blue, giving each captured frame its own clock.
                double Sample(bool check) {
                    HeadlessUi.Flush();
                    using var frame = window.CaptureRenderedFrame()!;
                    using var pixels = frame.Lock();
                    int Red(int x) {
                        int offset = ((int)control.Bounds.Width - 116 + x) * 4 + 8 * pixels.RowBytes;
                        return Marshal.ReadByte(pixels.Address, offset + (pixels.Format == PixelFormat.Rgba8888 ? 0 : 2));
                    }
                    int marker = Red(13);
                    double remaining = (marker - redLeft) / (255.0 * opacity);
                    if (check) {
                        Assert.InRange(Red(1), solid - 2, solid + 2);
                        Assert.InRange(Red(4), half - 2, half + 2);
                        Assert.Equal(background, Red(7));
                        double edge = background + (solid - background) * remaining;
                        Assert.InRange((double)Red(10), edge - 3, edge + 3);
                    }
                    return remaining;
                }
                Assert.True(SpinWait.SpinUntil(() => Sample(false) > 0.99, 5000));
                Sample(true);
                control.Selection = control.Selection with { ColorPath = "next.png" };
                bool early = false, middle = false, late = false;
                Assert.True(SpinWait.SpinUntil(() => {
                    double remaining = Sample(true);
                    early |= remaining is > 0.65 and < 0.95;
                    middle |= remaining is > 0.35 and < 0.65;
                    late |= remaining is > 0.05 and < 0.35;
                    return remaining < 0.01;
                }, 5000));
                Assert.True(early && middle && late, "Expected samples early, midway and late in the fade.");
            } finally {
                window.Close();
                folder.Delete(true);
            }
        });

        [Fact]
        public void CrossfadesCachesAndFallsBackInTheRenderedWindow() => HeadlessUi.Run(() => {
            var folder = Directory.CreateTempSubdirectory();
            var singer = new Singer(folder.FullName, new());
            var control = new SingerPortrait();
            var window = new Window { Content = control, Width = 240, Height = 80, Background = Brushes.Black };
            var reduce = Preferences.Default.ReduceAnimations;
            try {
                Preferences.Default.ReduceAnimations = false;
                const int red = 0xff0000, blue = 0x0000ff, green = 0x00ff00;
                WriteColor(singer.Portrait, red | unchecked((int)0xff000000));
                WriteColor(Path.Combine(folder.FullName, "soft.png"), blue | unchecked((int)0xff000000));
                WriteColor(Path.Combine(folder.FullName, "whisper.png"), green | unchecked((int)0xff000000));
                File.WriteAllText(Path.Combine(folder.FullName, "bad.png"), "invalid image");
                window.Show();
                int Pixel() {
                    HeadlessUi.Flush();
                    using var frame = window.CaptureRenderedFrame()!;
                    using var pixels = frame.Lock();
                    int x = (int)control.Bounds.Width - 108;
                    int rgb = Marshal.ReadInt32(pixels.Address, 8 * pixels.RowBytes + x * 4) & 0xffffff;
                    return pixels.Format == PixelFormat.Rgba8888
                        ? ((rgb & 0xff) << 16) | (rgb & 0xff00) | (rgb >> 16) : rgb;
                }
                void WaitColor(int color) => Assert.True(SpinWait.SpinUntil(() => Pixel() == color, 5000), $"Expected pixel {color:X6}, got {Pixel():X6}");
                void Select(string? path) => control.Selection = new PortraitSelection(singer, 1, path);
                Select(null); WaitColor(red);
                Select("soft.png");
                Assert.True(SpinWait.SpinUntil(() => { var p = Pixel(); return (p & red) != 0 && (p & blue) != 0; }, 5000));
                Select("whisper.png"); WaitColor(green);
                File.Delete(Path.Combine(folder.FullName, "soft.png"));
                Select("soft.png"); WaitColor(blue);
                Select("missing.png"); WaitColor(red);
                Select("whisper.png"); WaitColor(green);
                Select("bad.png"); WaitColor(red);
                control.Selection = null;
                WaitColor(0);
            } finally {
                window.Close();
                Preferences.Default.ReduceAnimations = reduce;
                folder.Delete(true);
            }
        });
    }
}
