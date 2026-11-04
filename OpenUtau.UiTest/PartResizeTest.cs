using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// Dragging part edges on the parts canvas with the pointer. At the default 480 ticks per beat in 4/4.
    /// </summary>
    public class PartResizeTest {
        // Opens the editor with the given voice parts, added in order, so later parts are drawn on top.
        static void WithParts((int trackNo, int position, int duration, int[]? notes)[] parts,
                Action<MainWindow, MainWindowViewModel, Canvas, UVoicePart[]> test,
                [CallerMemberName] string name = "") => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            var document = DocManager.Inst;
            document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            HeadlessUi.Errors.Clear();
            var project = document.Project;
            var window = new MainWindow { Width = 1280, Height = 800 };
            try {
                window.Show();
                window.InitProject();
                var viewModel = Assert.IsType<MainWindowViewModel>(window.DataContext);
                viewModel.Page = 1;
                document.StartUndoGroup();
                while (project.tracks.Count <= parts.Max(p => p.trackNo)) {
                    document.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = project.tracks.Count }));
                }
                var added = parts.Select(p => {
                    var part = new UVoicePart { trackNo = p.trackNo, position = p.position, Duration = p.duration };
                    // Notes as pairs of position and duration, relative to the part.
                    for (int i = 0; i + 1 < (p.notes?.Length ?? 0); i += 2) {
                        part.notes.Add(project.CreateNote(60, p.notes![i], p.notes[i + 1]));
                    }
                    document.ExecuteCmd(new AddPartCommand(project, part));
                    return part;
                }).ToArray();
                document.EndUndoGroup();
                viewModel.TracksViewModel.TickWidth = 0.1;
                HeadlessUi.Flush();
                var canvas = Assert.IsAssignableFrom<Canvas>(window.FindControl<Canvas>("partsCanvas"));
                test(window, viewModel, canvas, added);
                var errors = HeadlessUi.Errors.Snapshot();
                Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
            } finally {
                HeadlessUi.Flush();
                HeadlessUi.SaveScreenshot(window, $"{nameof(PartResizeTest)}-{name}");
                window.Close();
                // Leave no unsaved edits behind for later tests, which share DocManager.
                document.ExecuteCmd(new LoadProjectNotification(Core.Format.Ustx.Create()));
            }
        });

        // The window point at a tick, in the middle of a track; dx moves it by pixels. Dragged voice part
        // edges snap to the grid line after the pointer, so a point just before a line ends up on it.
        static Point At(Window window, Canvas canvas, MainWindowViewModel viewModel, int tick, int trackNo, double dx = 0) {
            var tracks = viewModel.TracksViewModel;
            var inCanvas = tracks.TickTrackToPoint(tick, trackNo) + new Point(dx, tracks.TrackHeight / 2);
            return canvas.TranslatePoint(inCanvas, window)!.Value;
        }

        static void Drag(Window window, Point from, params Point[] to) {
            window.MouseDown(from, MouseButton.Left);
            foreach (var point in to) {
                window.MouseMove(point, RawInputModifiers.LeftMouseButton);
            }
            window.MouseUp(to.Last(), MouseButton.Left);
            HeadlessUi.Flush();
        }

        [Fact]
        public void CoveredEdgeOfAnOverlappedPartResizes() => WithParts(new (int, int, int, int[]?)[] {
            (0, 0, 3840, null),
            (0, 1920, 3840, null),
        }, (window, viewModel, canvas, parts) => {
            var (below, above) = (parts[0], parts[1]);
            // The end of the part below lies inside the part above, and is drawn over it.
            Assert.Equal(Avalonia.Media.Colors.White, PixelAt(window, At(window, canvas, viewModel, below.End, 0)));
            Assert.NotEqual(Avalonia.Media.Colors.White, PixelAt(window, At(window, canvas, viewModel, below.End, 0, 6)));
            var edge = At(window, canvas, viewModel, below.End, 0, -2);
            Drag(window, edge, At(window, canvas, viewModel, 2880, 0, -1));

            Assert.Equal(0, below.position);
            Assert.Equal(2880, below.End);
            Assert.Equal((1920, 3840), (above.position, above.Duration));
            // The mark follows the edge.
            Assert.Equal(Avalonia.Media.Colors.White, PixelAt(window, At(window, canvas, viewModel, below.End, 0)));
        });

        // The color the window shows at a point.
        static Color PixelAt(Window window, Point point) {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            using var buffer = frame.Lock();
            int x = (int)(point.X * window.RenderScaling);
            int y = (int)(point.Y * window.RenderScaling);
            uint bgra = (uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
            return Color.FromUInt32(bgra);
        }

        [Fact]
        public void StartNearTheFirstNoteDoesNotGrowWhenShrinking() => WithParts(new (int, int, int, int[]?)[] {
            (0, 1920, 1920, new[] { 100, 480 }),
        }, (window, viewModel, canvas, parts) => {
            var part = parts[0];
            Drag(window, At(window, canvas, viewModel, part.position, 0, 2), At(window, canvas, viewModel, 2400, 0));

            // The first note is within the first beat, so the start can't move later.
            Assert.Equal((1920, 1920), (part.position, part.Duration));
        });

        [Fact]
        public void SelectedPartsShrinkAsFarAsEachAllows() => WithParts(new (int, int, int, int[]?)[] {
            (0, 0, 3840, new[] { 0, 2880 }),
            (1, 0, 3840, null),
        }, (window, viewModel, canvas, parts) => {
            var (full, empty) = (parts[0], parts[1]);
            viewModel.TracksViewModel.SelectPart(full);
            viewModel.TracksViewModel.SelectPart(empty);
            HeadlessUi.Flush();
            // Drag the end of the part that can't go below its note, several times to the same place.
            var target = At(window, canvas, viewModel, 1440, 0, -1);
            Drag(window, At(window, canvas, viewModel, full.End, 0, -2), target, target, target);

            Assert.Equal(2880, full.Duration);
            Assert.Equal(1440, empty.Duration);
        });
    }
}
