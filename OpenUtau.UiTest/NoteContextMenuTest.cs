using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>The context menu of notes in the piano roll.</summary>
    public class NoteContextMenuTest {
        // Opens a piano roll on a part with two notes, as double-clicking the part in the main window does.
        static void WithPianoRoll(Action<Window, PianoRollViewModel, Control, UVoicePart> test) => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            var document = DocManager.Inst;
            document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            var project = document.Project;
            var model = new PianoRollViewModel { PlaybackViewModel = new PlaybackViewModel() };
            var pianoRoll = new PianoRoll(model);
            var window = new Window { Width = 1280, Height = 800, Content = pianoRoll };
            try {
                window.Show();
                pianoRoll.InitializePianoRollWindowAsync();
                var part = new UVoicePart { trackNo = 0, position = 0, Duration = 1920 };
                foreach (var (position, lyric) in new[] { (0, "a"), (480, "+") }) {
                    var note = project.CreateNote(60, position, 480);
                    note.lyric = lyric;
                    part.notes.Add(note);
                }
                document.StartUndoGroup();
                document.ExecuteCmd(new AddPartCommand(project, part));
                document.EndUndoGroup();
                document.ExecuteCmd(new LoadPartNotification(part, project, 0));
                model.NotesViewModel.TrackOffset = model.NotesViewModel.TrackCount - 1 - 66;
                HeadlessUi.Flush();
                var canvas = pianoRoll.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "NotesCanvas");
                // Only what the test does counts, not opening the piano roll outside the main window.
                HeadlessUi.Errors.Clear();
                test(window, model, canvas, part);
                var errors = HeadlessUi.Errors.Snapshot();
                Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
            } finally {
                window.Close();
                // Leave no unsaved edits behind for later tests, which share DocManager.
                document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            }
        });

        // Right-clicks the middle of a note, which fills the note context menu.
        static void RightClick(Window window, PianoRollViewModel model, Control canvas, UNote note) {
            var notesVm = model.NotesViewModel;
            var inCanvas = notesVm.TickToneToPoint(note.position + note.duration / 2, note.tone) + new Point(0, notesVm.TrackHeight / 2);
            var at = canvas.TranslatePoint(inCanvas, window)!.Value;
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            HeadlessUi.Flush();
        }

        static MenuItemViewModel? MergeItem(Window window, PianoRollViewModel model) =>
            model.NotesContextMenuItems.SingleOrDefault(item => item.Header == window.FindResource("context.note.merge") as string);

        [Fact]
        public void MergesSelectedNotes() => WithPianoRoll((window, model, canvas, part) => {
            var (first, second) = (part.notes.First(), part.notes.Last());
            model.NotesViewModel.SelectNote(first);
            model.NotesViewModel.SelectNote(second, false);
            RightClick(window, model, canvas, second);

            var merge = MergeItem(window, model);
            Assert.NotNull(merge);
            Assert.Equal(new KeyGesture(Key.U, KeyModifiers.Control), merge.InputGesture);
            merge.Command!.Execute(merge.CommandParameter);
            HeadlessUi.Flush();
            Assert.Same(first, Assert.Single(part.notes));
            Assert.Equal(960, first.duration);
            Assert.Equal("a", first.lyric);
        });

        [Fact]
        public void NoMergeForOneNote() => WithPianoRoll((window, model, canvas, part) => {
            RightClick(window, model, canvas, part.notes.First());
            Assert.NotEmpty(model.NotesContextMenuItems);
            Assert.Null(MergeItem(window, model));
        });
    }
}
