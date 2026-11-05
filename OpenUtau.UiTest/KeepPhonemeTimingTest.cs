using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.App.ViewModels;
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

        // Puts the vowel on the note and a tail just after the start of its last extender note, so the
        // tail lies on the extender and moves when the extender is merged into the note.
        // A phoneme for each letter of the lyric, 60 ticks apart and starting 30 ticks later for each
        // letter after the first, and a tail that ends the note earlier the longer the next note's
        // lyric is: changing a lyric moves all the note's phonemes and the tail of the note before it.
        class LetterPhonemizer : Phonemizer {
            public override void SetSinger(USinger singer) { }
            public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
                int duration = notes.Sum(note => note.duration);
                var lyric = notes[0].lyric;
                int start = 30 * (lyric.Length - 1);
                var phonemes = lyric.Select((letter, i) => new Phoneme { index = i, phoneme = letter.ToString(), position = start + i * 60 }).ToList();
                phonemes.Add(new Phoneme { index = 100, phoneme = "t", position = duration - 60 * (nextNeighbour?.lyric.Length ?? 1) });
                return new Result { phonemes = phonemes.ToArray() };
            }
        }

        class SlurPhonemizer : Phonemizer {
            public override void SetSinger(USinger singer) { }
            public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) =>
                new Result {
                    phonemes = new[] {
                        new Phoneme { index = 0, phoneme = notes[0].lyric, position = 0 },
                        new Phoneme { index = 1, phoneme = "t", position = notes[^1].position - notes[0].position + 120 },
                    },
                };
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
        static void WithPart(int[][] notes, Action<UProject, UVoicePart, UNote[]> test,
                Phonemizer? phonemizer = null, string[]? lyrics = null) => HeadlessUi.Run(() => {
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
                track.Phonemizer = phonemizer ?? new TailPhonemizer();
                var part = new UVoicePart { trackNo = 0, position = 0, Duration = 1920 * 2 };
                for (int i = 0; i < notes.Length; i++) {
                    var note = project.CreateNote(60, notes[i][0], notes[i][1]);
                    note.lyric = lyrics?[i] ?? "a";
                    part.notes.Add(note);
                }
                document.StartUndoGroup();
                document.ExecuteCmd(new AddPartCommand(project, part));
                document.EndUndoGroup();
                WaitForPhonemes(part);
                Assert.NotEmpty(part.phonemes);
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

        // Opens the part in a piano roll's view model, with the toggle as given, and puts the toggle
        // back as it was after the test.
        static void InPianoRoll(UProject project, UVoicePart part, bool keepPhonemeTiming, Action<NotesViewModel> test) {
            var notesVm = new PianoRollViewModel().NotesViewModel;
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(part, project, 0));
            bool old = notesVm.KeepPhonemeTiming;
            notesVm.KeepPhonemeTiming = keepPhonemeTiming;
            try {
                test(notesVm);
            } finally {
                notesVm.KeepPhonemeTiming = old;
            }
        }

        [Fact]
        public void CuttingANoteKeepsTheNextConsonant() => WithPart(new[] { new[] { 0, 480 }, new[] { 480, 480 }, new[] { 960, 480 } },
                (project, part, notes) => {
            var (b, c) = (notes[1], notes[2]);
            // The consonant of c is shorter after b, a note right before it.
            Assert.Equal(960 - 60, PositionOf(part, c, 0));
            void Delete(bool keep) => InPianoRoll(project, part, keep, notesVm => {
                notesVm.SelectNote(b);
                notesVm.CutNotes();
                WaitForPhonemes(part);
            });

            Delete(keep: true);
            Assert.DoesNotContain(b, part.notes);
            Assert.Equal(960 - 60, PositionOf(part, c, 0));

            // Without the toggle, the consonant gets the length it has with no note right before it.
            DocManager.Inst.Undo();
            WaitForPhonemes(part);
            Delete(keep: false);
            Assert.Equal(960 - 30, PositionOf(part, c, 0));
        });

        [Fact]
        public void MergingKeepsThePhonemesOnTheExtender() => WithPart(new[] { new[] { 0, 480 }, new[] { 480, 480 } },
                (project, part, notes) => {
            var (a, extender) = (notes[0], notes[1]);
            Assert.Same(a, extender.Extends);
            Assert.Equal(480 + 120, PositionOf(part, a, 1));
            void Merge(bool keep) => InPianoRoll(project, part, keep, notesVm => {
                notesVm.SelectNote(a);
                notesVm.SelectNote(extender, false);
                notesVm.MergeSelectedNotes();
                WaitForPhonemes(part);
            });

            Merge(keep: true);
            Assert.Equal(a, Assert.Single(part.notes));
            Assert.Equal(960, a.duration);
            Assert.Equal(480 + 120, PositionOf(part, a, 1));

            // Without the toggle, the tail moves to after the start of the merged note.
            DocManager.Inst.Undo();
            WaitForPhonemes(part);
            Merge(keep: false);
            Assert.Equal(120, PositionOf(part, a, 1));
        }, new SlurPhonemizer(), new[] { "a", "+" });

        [Fact]
        public void MovedNotesTakeTheirPhonemesAlong() => WithPart(new[] { new[] { 0, 480 }, new[] { 960, 480 }, new[] { 1440, 480 } },
                (project, part, notes) => {
            var (b, c) = (notes[1], notes[2]);
            var before = Positions(part);
            Assert.Equal(1440 - 60, before[(c, 0)]);
            InPianoRoll(project, part, true, notesVm => {
                notesVm.SelectNote(b);
                notesVm.MoveSelectedNotes(-240);
                WaitForPhonemes(part);
            });
            Assert.Equal(720, b.position);
            // The phonemes of b moved with it, and the consonant of c, no longer after a note right
            // before it, stayed.
            Assert.All(Enumerable.Range(0, 3), i => Assert.Equal(before[(b, i)] - 240, PositionOf(part, b, i)));
            Assert.Equal(1440 - 60, PositionOf(part, c, 0));
        });

        // Runs lyric changes in one undo group with the toggle as given, as the lyric box does.
        static void ChangeLyrics(UVoicePart part, bool keepPhonemeTiming, params (UNote note, string lyric)[] changes) {
            var preferences = Core.Util.Preferences.Default;
            bool old = preferences.KeepPhonemeTiming;
            preferences.KeepPhonemeTiming = keepPhonemeTiming;
            try {
                DocManager.Inst.StartUndoGroup("command.note.lyric");
                foreach (var (note, lyric) in changes) {
                    DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(part, note, lyric));
                }
                DocManager.Inst.EndUndoGroup();
                WaitForPhonemes(part);
            } finally {
                preferences.KeepPhonemeTiming = old;
            }
        }

        [Fact]
        public void ChangingALyricKeepsTheOtherPhonemes() => WithPart(new[] { new[] { 0, 480 }, new[] { 480, 480 }, new[] { 960, 480 } },
                (project, part, notes) => {
            var (a, b, c) = (notes[0], notes[1], notes[2]);
            var before = Positions(part);
            Assert.Equal(480 - 60, before[(a, 100)]);

            ChangeLyrics(part, true, (b, "bbb"));
            // The changed note gets its new phonemes, and the tail of the note before it stays.
            Assert.Equal(new[] { 540, 600, 660 }, Enumerable.Range(0, 3).Select(i => PositionOf(part, b, i)));
            Assert.Equal(480 - 60, PositionOf(part, a, 100));
            Assert.Equal(before[(c, 0)], PositionOf(part, c, 0));
            // One undo takes the lyric back, with the phonemes as they were.
            DocManager.Inst.Undo();
            WaitForPhonemes(part);
            Assert.Equal(before, Positions(part));

            // Without the toggle, the phonemizer moves the tail for the longer lyric after it.
            ChangeLyrics(part, false, (b, "bbb"));
            Assert.Equal(480 - 180, PositionOf(part, a, 100));
        }, new LetterPhonemizer(), new[] { "a", "b", "c" });

        [Fact]
        public void ChangingLyricsInABatchLetsEachChangedNoteGo() => WithPart(new[] { new[] { 0, 480 }, new[] { 480, 480 }, new[] { 960, 480 } },
                (project, part, notes) => {
            var (a, b, c) = (notes[0], notes[1], notes[2]);
            ChangeLyrics(part, true, (b, "bb"), (c, "ccc"));
            // Both changed notes get their new phonemes, though the first change already held the part.
            Assert.Equal(new[] { 510, 570 }, Enumerable.Range(0, 2).Select(i => PositionOf(part, b, i)));
            Assert.Equal(new[] { 1020, 1080, 1140 }, Enumerable.Range(0, 3).Select(i => PositionOf(part, c, i)));
            Assert.Equal(480 - 60, PositionOf(part, a, 100));
        }, new LetterPhonemizer(), new[] { "a", "b", "c" });
    }
}
