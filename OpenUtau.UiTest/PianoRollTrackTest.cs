using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.Api;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// The piano roll over the whole track of the open part, with the other parts on the track shown
    /// and editable from it. At the default 480 ticks per beat in 4/4.
    /// </summary>
    public class PianoRollTrackTest {
        // Two parts on one track, a bar apart, each with notes as position, duration and tone.
        static readonly (int position, int[] notes)[] Parts = {
            (0, new[] { 0, 480, 60, 960, 480, 62 }),
            (3840, new[] { 0, 480, 64, 960, 480, 65 }),
        };

        // One phoneme on each note, so notes are drawn as with a working singer rather than as errors.
        class OnePhonemePhonemizer : Phonemizer {
            public override void SetSinger(USinger singer) { }
            public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) =>
                new Result { phonemes = new[] { new Phoneme { phoneme = notes[0].lyric } } };
        }

        // Has every phoneme, so phonemes and notes aren't errors.
        class Singer : USinger {
            public Singer() {
                found = true;
                loaded = true;
            }
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = UOto.OfDummy(phoneme);
                return true;
            }
            public override string Id => nameof(PianoRollTrackTest);
            public override string Name => nameof(PianoRollTrackTest);
            public override USingerType SingerType => USingerType.Classic;
            public override IList<string> SearchTerms { get; } = new List<string>();
            public override IList<USubbank> Subbanks { get; } = new List<USubbank>();
        }

        // Opens the piano roll on the first part, as double-clicking it in the main window does.
        static void WithPianoRoll(Action<Window, NotesViewModel, Control, UVoicePart[]> test,
                [CallerMemberName] string name = "") => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            var document = DocManager.Inst;
            document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            HeadlessUi.Errors.Clear();
            // The test singer has no audio to render.
            var preRender = Core.Util.Preferences.Default.PreRender;
            Core.Util.Preferences.Default.PreRender = false;
            var project = document.Project;
            project.tracks[0].Singer = new Singer();
            project.tracks[0].Phonemizer = new OnePhonemePhonemizer();
            var model = new PianoRollViewModel();
            var pianoRoll = new PianoRoll(model);
            model.PlaybackViewModel = new PlaybackViewModel();
            var window = new Window { Width = 1280, Height = 800, Content = pianoRoll };
            try {
                window.Show();
                pianoRoll.InitializePianoRollWindowAsync();
                document.StartUndoGroup();
                var parts = Parts.Select(p => {
                    var part = new UVoicePart { trackNo = 0, position = p.position, Duration = 1920 };
                    for (int i = 0; i + 2 < p.notes.Length; i += 3) {
                        part.notes.Add(project.CreateNote(p.notes[i + 2], p.notes[i], p.notes[i + 1]));
                    }
                    document.ExecuteCmd(new AddPartCommand(project, part));
                    return part;
                }).ToArray();
                document.EndUndoGroup();
                WaitForPhonemes(parts);
                document.ExecuteCmd(new LoadPartNotification(parts[0], project, 0));
                pianoRoll.AttachExpressions();
                var notesVm = model.NotesViewModel;
                // Show the notes' tones in the middle.
                notesVm.TrackOffset = notesVm.TrackCount - 1 - 70;
                HeadlessUi.Flush();
                var canvas = pianoRoll.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "NotesCanvas");
                // Only what the test does counts, not opening the piano roll outside the main window.
                HeadlessUi.Errors.Clear();
                test(window, notesVm, canvas, parts);
                var errors = HeadlessUi.Errors.Snapshot();
                Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
            } finally {
                HeadlessUi.Flush();
                HeadlessUi.SaveScreenshot(window, $"{nameof(PianoRollTrackTest)}-{name}");
                window.Close();
                // Leave no unsaved edits behind for later tests, which share DocManager.
                document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
                Core.Util.Preferences.Default.PreRender = preRender;
            }
        });

        // Lets the phonemizer thread catch up with the parts' notes.
        static void WaitForPhonemes(params UVoicePart[] parts) {
            DocManager.Inst.FlushValidation();
            var stopwatch = Stopwatch.StartNew();
            while (!parts.All(part => part.PhonemesUpToDate && part.phonemes.Count == part.notes.Count)) {
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "The phonemizer did not finish");
                Thread.Sleep(5);
                Dispatcher.UIThread.RunJobs();
            }
            HeadlessUi.Flush();
        }

        // The window point in the middle of a note of a part, open or not.
        static Point NoteCenter(Window window, NotesViewModel notesVm, Control canvas, UVoicePart part, UNote note) {
            int tick = part.position + note.position + note.duration / 2 - notesVm.Part!.position;
            var inCanvas = notesVm.TickToneToPoint(tick, note.tone) + new Point(0, notesVm.TrackHeight / 2);
            return canvas.TranslatePoint(inCanvas, window)!.Value;
        }

        static Color PixelAt(Window window, Point point) {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            using var buffer = frame.Lock();
            int x = (int)(point.X * window.RenderScaling);
            int y = (int)(point.Y * window.RenderScaling);
            return Color.FromUInt32((uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4));
        }

        static void Click(Window window, Point point) {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessUi.Flush();
        }

        [Fact]
        public void ScrollsOverTheTrackAndShowsTheOtherParts() => WithPianoRoll((window, notesVm, canvas, parts) => {
            var (open, other) = (parts[0], parts[1]);
            Assert.Same(open, notesVm.Part);
            // The view goes past the open part, up to the end of the last part on the track.
            Assert.Equal(other.End - notesVm.ViewportTicks, notesVm.HScrollBarMax, 3);
            notesVm.TickOffset = notesVm.ClampTickOffset(other.position - 240);
            HeadlessUi.Flush();
            Assert.Equal(other.position - 240, notesVm.TickOffset, 3);

            // The notes of the other part show, fainter than notes of the open part.
            var otherNote = other.notes.First();
            int Brightness(Color color) => color.R + color.G + color.B;
            int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            var center = NoteCenter(window, notesVm, canvas, other, otherNote);
            var empty = PixelAt(window, center + new Point(0, notesVm.TrackHeight * 3));
            var faint = PixelAt(window, center);
            Assert.True(Distance(faint, empty) > 40, $"The other part's note ({faint}) doesn't show over the background ({empty})");

            // Outside the open part is shaded: the same row is darker past the open part's end.
            double y = NoteCenter(window, notesVm, canvas, open, open.notes.First()).Y;
            notesVm.TickOffset = notesVm.ClampTickOffset(open.End - 960);
            HeadlessUi.Flush();
            var edge = canvas.TranslatePoint(notesVm.TickToneToPoint(open.Duration, 0), window)!.Value;
            // Averaged along the row, over the dotted grid lines.
            double Average(double from) => Enumerable.Range(0, 40)
                .Average(i => Brightness(PixelAt(window, new Point(from + i, y))));
            double before = Average(edge.X - 50);
            double after = Average(edge.X + 10);
            Assert.True(after < before - 5, $"Past the open part ({after}) isn't darker than in it ({before})");
            // A note of the open part is more solid than the other part's.
            var solid = PixelAt(window, NoteCenter(window, notesVm, canvas, open, open.notes.Last()));
            Assert.True(Distance(solid, faint) > 40, $"The other part's note ({faint}) looks as solid as the open part's ({solid})");
        });

        [Fact]
        public void ClickingAnotherPartsNoteOpensItWhereTheViewIs() => WithPianoRoll((window, notesVm, canvas, parts) => {
            var (first, second) = (parts[0], parts[1]);
            // Both parts in view.
            notesVm.TickWidth = 0.2;
            notesVm.TickOffset = notesVm.ClampTickOffset(480);
            HeadlessUi.Flush();
            double viewStart = notesVm.Part!.position + notesVm.TickOffset;

            var note = second.notes.First();
            Click(window, NoteCenter(window, notesVm, canvas, second, note));
            Assert.Same(second, notesVm.Part);
            Assert.Equal(viewStart, notesVm.Part.position + notesVm.TickOffset, 3);
            Assert.Contains(note, notesVm.Selection);

            // Dragging a note of the part now not open opens it and moves the note, in one gesture.
            var firstNote = first.notes.Last();
            var from = NoteCenter(window, notesVm, canvas, first, firstNote);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from - new Point(0, notesVm.TrackHeight), RawInputModifiers.LeftMouseButton);
            window.MouseUp(from - new Point(0, notesVm.TrackHeight), MouseButton.Left);
            HeadlessUi.Flush();
            Assert.Same(first, notesVm.Part);
            Assert.Equal(63, firstNote.tone);
            Assert.Equal(viewStart, notesVm.Part.position + notesVm.TickOffset, 3);
        });

        [Fact]
        public void ClickingAnotherPartsPhonemeOpensIt() => WithPianoRoll((window, notesVm, canvas, parts) => {
            var second = parts[1];
            notesVm.TickWidth = 0.2;
            notesVm.TickOffset = notesVm.ClampTickOffset(480);
            HeadlessUi.Flush();
            Assert.True(notesVm.ShowPhoneme);
            var phonemeCanvas = window.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "PhonemeCanvas");
            // Inside the phoneme's bar, away from its handles.
            var phoneme = second.phonemes.First();
            int tick = second.position + phoneme.position + 120 - notesVm.Part!.position;
            var at = phonemeCanvas.TranslatePoint(new Point(notesVm.TickToneToPoint(tick, 0).X, 45), window)!.Value;
            Click(window, at);
            Assert.Same(second, notesVm.Part);
        });

        [Fact]
        public void NotesCannotBeDrawnBeforeThePartStart() => WithPianoRoll((window, notesVm, canvas, parts) => {
            var second = parts[1];
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(second, DocManager.Inst.Project, second.position));
            notesVm.TickOffset = notesVm.ClampTickOffset(second.position - 2400 - second.position);
            HeadlessUi.Flush();
            int notes = second.notes.Count;
            Assert.Null(notesVm.MaybeAddNote(notesVm.TickToneToPoint(-480, 60), false));
            Assert.Equal(notes, second.notes.Count);
        });

        [Fact]
        public void PlaybackPastTheOpenPartKeepsItOpen() => WithPianoRoll((window, notesVm, canvas, parts) => {
            var (first, second) = (parts[0], parts[1]);
            DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(second.position + 240));
            HeadlessUi.Flush();
            Assert.Same(first, notesVm.Part);
        });
    }
}
