using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// Resizing notes with phonemes pinned, through the real document and the phonemizer
    /// running on its own thread as in the app.
    /// </summary>
    public class KeepPhonemeTimingTest {
        // Puts a consonant before each note, the vowel on it and a tail before its end. The tail moves
        // with the note end, and the consonant gets shorter after a note right before it.
        class TailPhonemizer : Phonemizer {
            public override void SetSinger(USinger singer) { }
            public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
                int duration = notes.Sum(note => note.duration);
                int consonant = prevNeighbour == null ? 30 : prevNeighbour.Value.duration / 8;
                return new Result {
                    phonemes = new[] {
                        new Phoneme { index = 0, phoneme = "k", position = -consonant },
                        new Phoneme { index = 1, phoneme = notes[0].lyric, position = 0 },
                        new Phoneme { index = 2, phoneme = "t", position = duration - 120 },
                    },
                };
            }
        }

        class Singer : USinger {
            public Singer() {
                found = true;
            }
            public override string Id => "keep-phoneme-timing";
            public override string Name => "Keep Phoneme Timing";
            public override USingerType SingerType => USingerType.Classic;
            public override IList<string> SearchTerms { get; } = new List<string>();
        }

        // Runs the test on a part with notes at the given positions and lengths, phonemized.
        static void WithPart(int[][] notes, Action<UProject, UVoicePart, UNote[]> test) => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            var document = DocManager.Inst;
            // The headless app shares DocManager across tests: start from an empty project.
            document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            HeadlessUi.Errors.Clear();
            var project = document.Project;
            var track = project.tracks[0];
            var (oldSinger, oldPhonemizer) = (track.Singer, track.Phonemizer);
            try {
                track.Singer = new Singer();
                track.Phonemizer = new TailPhonemizer();
                var part = new UVoicePart { trackNo = 0, position = 0, Duration = 1920 * 2 };
                foreach (var n in notes) {
                    var note = project.CreateNote(60, n[0], n[1]);
                    note.lyric = "a";
                    part.notes.Add(note);
                }
                document.StartUndoGroup();
                document.ExecuteCmd(new AddPartCommand(project, part));
                document.EndUndoGroup();
                WaitForPhonemes(part);
                Assert.Equal(3 * notes.Length, part.phonemes.Count);
                test(project, part, part.notes.ToArray());
                var errors = HeadlessUi.Errors.Snapshot();
                Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
            } finally {
                document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
                (track.Singer, track.Phonemizer) = (oldSinger, oldPhonemizer);
            }
        });

        // Lets the phonemizer thread catch up with the notes.
        static void WaitForPhonemes(UVoicePart part) {
            DocManager.Inst.FlushValidation();
            var stopwatch = Stopwatch.StartNew();
            while (!part.PhonemesUpToDate) {
                Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "The phonemizer did not finish");
                Thread.Sleep(5);
                Dispatcher.UIThread.RunJobs();
            }
            HeadlessUi.Flush();
        }

        static int PositionOf(UVoicePart part, UNote note, int index) =>
            part.phonemes.Single(phoneme => phoneme.Parent == note && phoneme.index == index).position;

        static Dictionary<(UNote, int), int> Positions(UVoicePart part) =>
            part.phonemes.ToDictionary(phoneme => (phoneme.Parent, phoneme.index), phoneme => phoneme.position);

        // Runs edits in one undo group with the given notes pinned, as dragging a note edge does.
        static void PinnedEdit(UVoicePart part, IEnumerable<UNote> pinned, Action edit, Action whilePending) {
            var document = DocManager.Inst;
            document.StartUndoGroup("command.note.edit");
            var pin = new PinPhonemesCommand(part, pinned);
            document.ExecuteCmd(pin);
            edit();
            document.FlushValidation();
            // The phonemizer hasn't caught up yet, and the phonemes already stay put.
            whilePending();
            document.EndUndoGroup();
            pin.Release();
            WaitForPhonemes(part);
        }

        [Fact]
        public void LongerNoteKeepsItsTail() => WithPart(new[] { new[] { 0, 480 }, new[] { 960, 480 } }, (project, part, notes) => {
            var document = DocManager.Inst;
            var a = notes[0];
            var before = Positions(part);
            Assert.Equal(360, before[(a, 2)]);

            PinnedEdit(part, new[] { a, notes[1] },
                () => document.ExecuteCmd(new ResizeNoteCommand(part, a, 240)),
                () => Assert.Equal(before, Positions(part)));
            Assert.Equal(720, a.duration);
            Assert.Equal(before, Positions(part));
            // The pin went into the tail's offset, against the phonemizer's new place for it.
            Assert.Equal(360 - 600, a.GetPhonemeOverride(2).offset);

            // The pins are gone: a later edit moves the tail again.
            document.StartUndoGroup();
            document.ExecuteCmd(new ResizeNoteCommand(part, a, 120));
            document.EndUndoGroup();
            WaitForPhonemes(part);
            Assert.Equal(360 + 120, PositionOf(part, a, 2));
            document.Undo();
            WaitForPhonemes(part);
            Assert.Equal(before, Positions(part));

            // One undo takes back the resize and the offsets it wrote; redo puts both back.
            document.Undo();
            WaitForPhonemes(part);
            Assert.Equal(480, a.duration);
            Assert.Null(a.phonemeOverrides.FirstOrDefault(o => o.index == 2)?.offset);
            Assert.Equal(before, Positions(part));
            document.Redo();
            WaitForPhonemes(part);
            Assert.Equal(720, a.duration);
            Assert.Equal(before, Positions(part));
        });

        [Fact]
        public void ShorterNoteKeepsTheNextConsonant() => WithPart(new[] { new[] { 0, 480 }, new[] { 480, 480 } }, (project, part, notes) => {
            var document = DocManager.Inst;
            var (a, b) = (notes[0], notes[1]);
            Assert.Equal(480 - 60, PositionOf(part, b, 0));

            PinnedEdit(part, new[] { a, b },
                () => document.ExecuteCmd(new ResizeNoteCommand(part, a, -240)),
                () => Assert.Equal(480 - 60, PositionOf(part, b, 0)));
            // Without the pin the consonant would be 30 ticks long now that the notes don't touch.
            Assert.Equal(480 - 60, PositionOf(part, b, 0));
            // The tail can't stay past the new end of its note, so it goes with the end.
            Assert.Equal(240 - 120, PositionOf(part, a, 2));
        });

        [Fact]
        public void LaterStartKeepsEveryPhoneme() => WithPart(new[] { new[] { 0, 480 }, new[] { 480, 480 } }, (project, part, notes) => {
            var document = DocManager.Inst;
            var (a, b) = (notes[0], notes[1]);
            var before = Positions(part);

            PinnedEdit(part, new[] { a, b },
                () => {
                    document.ExecuteCmd(new MoveNoteCommand(part, b, 120, 0));
                    document.ExecuteCmd(new ResizeNoteCommand(part, b, -120));
                },
                () => Assert.Equal(before, Positions(part)));
            Assert.Equal(600, b.position);
            Assert.Equal(before, Positions(part));
        });
    }
}
