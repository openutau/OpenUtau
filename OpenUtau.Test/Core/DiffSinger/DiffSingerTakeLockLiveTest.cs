using System;
using System.IO;
using System.Linq;
using System.Threading;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// Live check of the take lock against a real DiffSinger voicebank: a re-roll
    /// must change only the requested frames, and a protected range must survive it
    /// untouched. Opt-in, because it needs a voicebank on disk:
    /// <code>
    /// OPENUTAU_TEST_SINGERS=/path/to/Singers dotnet run --project OpenUtau.Test -- -class OpenUtau.Core.DiffSingerTakeLockLiveTest
    /// </code>
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class DiffSingerTakeLockLiveTest {
        [Fact]
        public void RerollChangesOnlyTheRequestedFramesAndProtectWins() {
            string? singersRoot = Environment.GetEnvironmentVariable("OPENUTAU_TEST_SINGERS");
            if (string.IsNullOrEmpty(singersRoot) || !Directory.Exists(singersRoot)) {
                Assert.Skip("Set OPENUTAU_TEST_SINGERS to a directory containing a DiffSinger voicebank to run this live test.");
            }

            string? previousSingerPath = Preferences.Default.AdditionalSingerPath;
            string? previousRunner = Preferences.Default.OnnxRunner;
            bool previousTakeLock = Preferences.Default.DiffSingerTakeLock;
            string? temporaryCharacterTxt = null;
            try {
                // The app registers the legacy code pages at startup; the test host
                // has to do it before the voicebank loader asks for shift_jis.
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                // The loader only discovers banks through character.txt; test banks
                // shipped without one get a temporary stand-in.
                temporaryCharacterTxt = EnsureCharacterTxt(singersRoot!);
                Preferences.Default.AdditionalSingerPath = singersRoot!;
                Preferences.Default.OnnxRunner = "CPU";
                Preferences.Default.DiffSingerTakeLock = true;
                SingerManager.Inst.SearchAllSingers();
                var singer = SingerManager.Inst.Singers.Values
                    .OfType<DiffSingerSinger>()
                    .FirstOrDefault();
                string probe = "ok";
                if (singer == null && temporaryCharacterTxt != null) {
                    var bank = new OpenUtau.Classic.Voicebank();
                    try {
                        OpenUtau.Classic.VoicebankLoader.LoadInfo(bank, temporaryCharacterTxt, singersRoot!);
                        probe = $"id={bank.Id} type={bank.SingerType} name={bank.Name} file={bank.File}";
                    } catch (Exception e) {
                        probe = e.ToString();
                    }
                }
                Assert.True(singer != null,
                    $"No DiffSinger voicebank found under {singersRoot}. " +
                    $"character.txt={temporaryCharacterTxt ?? "<none>"} " +
                    $"roots=[{string.Join(", ", PathManager.Inst.SingersPaths)}] " +
                    $"singers=[{string.Join(", ", SingerManager.Inst.Singers.Values.Select(s => $"{s.GetType().Name}:{s.Id}"))}] " +
                    $"probe={probe}");
                singer!.EnsureLoaded();
                Assert.True(singer.Loaded, $"{singer.Id} failed to load: {string.Join("; ", singer.Errors)}");

                Directory.CreateDirectory(PathManager.Inst.CachePath);

                var renderer = new DiffSingerRenderer();
                var (project, track, part) = BuildPart(singer, renderer);
                var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));

                var first = renderer.Render(phrase, new Progress(1), 0, new CancellationTokenSource(), true).Result;
                Assert.NotNull(first.samples);

                int steps = Preferences.Default.DiffSingerSteps;
                // Mirrors DiffSingerRenderer.Render: fixed depth models ignore the preference.
                double depth = singer.dsConfig.useVariableDepth
                    ? Math.Min(Preferences.Default.DiffSingerDepth, singer.dsConfig.maxDepth)
                    : 1.0;
                ulong key = DiffSingerTakeStore.ComputeKey(
                    phrase, singer.Id, singer.dsConfig.frameMs(), singer.dsConfig.num_mel_bins,
                    depth, steps, phrase.isXsyVariant);
                var take1 = DiffSingerTakeStore.TryLoad(key);
                Assert.NotNull(take1);

                // Re-roll ticks [200, 250), which land around frames 29..34 of a
                // phrase whose frame 0 is the head padding.
                DiffSingerTakeReroll.Request(part.trackNo, new[] { (200, 250) });
                int probeStart = project.timeAxis.MsPosToTickPos(phrase.positionMs);
                int probeEnd = project.timeAxis.MsPosToTickPos(phrase.endMs);
                int probeNonce = DiffSingerTakeReroll.Consume(part.trackNo, probeStart, probeEnd, out var probeRanges);
                Assert.True(probeNonce > 0,
                    $"reroll request did not match: track={part.trackNo} start={probeStart} end={probeEnd} " +
                    $"frames={take1.TotalFrames} bins={take1.Bins}");
                Assert.NotNull(probeRanges);
                var second = renderer.Render(phrase, new Progress(1), 0, new CancellationTokenSource(), true).Result;
                Assert.NotNull(second.samples);

                var take2 = DiffSingerTakeStore.TryLoad(key);
                Assert.NotNull(take2);
                Assert.Equal(take1!.Mel.Length, take2!.Mel.Length);

                int bins = take1.Bins;
                // Frames far from the edit keep the old take bit for bit.
                AssertFrameEquals(take1, take2, 0, bins);
                AssertFrameEquals(take1, take2, 1, bins);
                AssertFrameEquals(take1, take2, take1.TotalFrames - 1, bins);
                // The requested window itself is a new sample.
                Assert.True(AnyFrameDiffers(take1, take2, 29, 34, bins),
                    "The re-rolled frames should have been re-sampled.");

                // Now protect a window wide enough to contain the re-roll and its whole
                // cross-fade, then ask for it again: the take must be reused as is,
                // without running the acoustic model.
                phrase.preservedRanges = new[] { (48, 432) };
                DiffSingerTakeReroll.Request(part.trackNo, new[] { (200, 250) });
                var third = renderer.Render(phrase, new Progress(1), 0, new CancellationTokenSource(), true).Result;
                Assert.NotNull(third.samples);

                var take3 = DiffSingerTakeStore.TryLoad(key);
                Assert.NotNull(take3);
                for (int frame = 0; frame < take2.TotalFrames; frame++) {
                    AssertFrameEquals(take2, take3!, frame, bins);
                }

                CleanupPhraseCache(phrase);
            } finally {
                Preferences.Default.AdditionalSingerPath = previousSingerPath;
                Preferences.Default.OnnxRunner = previousRunner;
                Preferences.Default.DiffSingerTakeLock = previousTakeLock;
                if (temporaryCharacterTxt != null) {
                    try {
                        File.Delete(temporaryCharacterTxt);
                    } catch {
                        // A leftover file only affects the next live run's discovery.
                    }
                }
            }
        }

        static string? EnsureCharacterTxt(string singersRoot) {
            foreach (var dir in Directory.GetDirectories(singersRoot)) {
                if (File.Exists(Path.Combine(dir, "character.txt"))) {
                    continue;
                }
                if (!File.Exists(Path.Combine(dir, "character.yaml")) ||
                    !File.Exists(Path.Combine(dir, "dsconfig.yaml"))) {
                    continue;
                }
                string path = Path.Combine(dir, "character.txt");
                File.WriteAllText(path, "name=" + Path.GetFileName(dir) + "\n");
                return path;
            }
            return null;
        }

        static void CleanupPhraseCache(RenderPhrase phrase) {
            try {
                phrase.DeleteCacheFiles();
            } catch {
                // Cache cleanup failures must not mask the assertion result.
            }
        }

        static void AssertFrameEquals(DiffSingerTake a, DiffSingerTake b, int frame, int bins) {
            for (int c = 0; c < bins; c++) {
                Assert.Equal(a.Mel[frame * bins + c], b.Mel[frame * bins + c]);
            }
        }

        static bool AnyFrameDiffers(DiffSingerTake a, DiffSingerTake b, int start, int end, int bins) {
            for (int frame = start; frame < end && frame < Math.Min(a.TotalFrames, b.TotalFrames); frame++) {
                for (int c = 0; c < bins; c++) {
                    if (a.Mel[frame * bins + c] != b.Mel[frame * bins + c]) {
                        return true;
                    }
                }
            }
            return false;
        }

        static (UProject, UTrack, UVoicePart) BuildPart(USinger singer, IRenderer renderer) {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("engine", "eng", 0, 100, 0) {
                options = new[] { "" },
            });
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("velocity", "vel", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("modulation", "mod", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("direct", "dir", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("shift", "shft", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("attack", "atk", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("decay", "dec", 0, 100, 100));
            var track = project.tracks[0];
            track.Singer = singer;
            track.RendererSettings.Renderer = renderer;

            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);

            var note = UNote.Create();
            note.position = 0;
            note.duration = 480;
            note.tone = 60;
            note.lyric = "a";
            note.ExtendedDuration = 480;
            part.notes.Add(note);

            var phoneme = new UPhoneme { position = 0, phoneme = "a", Parent = note };
            part.phonemes.Add(phoneme);
            var options = new ValidateOptions();
            phoneme.Validate(options, project, track, part, note);
            Assert.False(phoneme.Error, $"Phoneme errored: {phoneme.ErrorException?.Message}");
            return (project, track, part);
        }
    }
}

