using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Views {
    public partial class MainWindow {
        private const double TempoMarkerHitPadding = 5;
        private const double TempoMarkerDragThreshold = 4;

        private bool tempoMarkerHandlersAttached;
        private UTempo? draggedTempo;
        private Point tempoDragStartPoint;
        private int tempoDragStartTick;
        private double tempoDragPointerOffset;
        private int tempoDragMinTick;
        private int tempoDragMaxTick;
        private bool tempoDragHasMoved;
        private UTempo? highlightedTempo;

        protected override void OnOpened(EventArgs e) {
            base.OnOpened(e);
            if (tempoMarkerHandlersAttached) {
                return;
            }
            tempoMarkerHandlersAttached = true;
            TimelineCanvas.AddHandler(
                PointerPressedEvent,
                TempoMarkerPointerPressed,
                RoutingStrategies.Tunnel,
                true);
            TimelineCanvas.AddHandler(
                PointerMovedEvent,
                TempoMarkerPointerMoved,
                RoutingStrategies.Tunnel,
                true);
            TimelineCanvas.AddHandler(
                PointerReleasedEvent,
                TempoMarkerPointerReleased,
                RoutingStrategies.Tunnel,
                true);
            TimelineCanvas.AddHandler(
                PointerExitedEvent,
                TempoMarkerPointerExited,
                RoutingStrategies.Direct,
                true);
            TimelineCanvas.AddHandler(
                PointerCaptureLostEvent,
                TempoMarkerPointerCaptureLost,
                RoutingStrategies.Direct,
                true);
        }

        private void TempoMarkerPointerPressed(
            object? sender,
            PointerPressedEventArgs args) {
            var point = args.GetCurrentPoint(TimelineCanvas);
            if (!point.Properties.IsLeftButtonPressed || draggedTempo != null) {
                return;
            }

            var tempo = HitTestTempoMarker(point.Position);
            if (tempo == null) {
                return;
            }

            var project = DocManager.Inst.Project;
            int index = project.tempos.IndexOf(tempo);
            if (index <= 0) {
                return;
            }

            draggedTempo = tempo;
            tempoDragStartPoint = point.Position;
            tempoDragStartTick = tempo.position;
            tempoDragPointerOffset = point.Position.X - TempoMarkerX(tempo);
            tempoDragMinTick = project.tempos[index - 1].position + 1;
            tempoDragMaxTick = index + 1 < project.tempos.Count
                ? project.tempos[index + 1].position - 1
                : int.MaxValue;
            if (tempoDragMinTick > tempoDragMaxTick) {
                tempoDragMinTick = tempo.position;
                tempoDragMaxTick = tempo.position;
            }
            tempoDragHasMoved = false;
            HighlightTempoMarker(draggedTempo);

            args.Pointer.Capture(TimelineCanvas);
            DocManager.Inst.StartUndoGroup("command.project.tempo");
            Cursor = ViewConstants.cursorSizeWE;
            args.Handled = true;
        }

        private void TempoMarkerPointerMoved(
            object? sender,
            PointerEventArgs args) {
            var point = args.GetCurrentPoint(TimelineCanvas);
            if (draggedTempo != null) {
                if (!tempoDragHasMoved) {
                    double distance = Math.Abs(point.Position.X - tempoDragStartPoint.X);
                    if (distance < TempoMarkerDragThreshold) {
                        HighlightTempoMarker(draggedTempo);
                        Cursor = ViewConstants.cursorSizeWE;
                        args.Handled = true;
                        return;
                    }
                    tempoDragHasMoved = true;
                }

                var tracksVm = viewModel.TracksViewModel;
                var markerPoint = new Point(
                    point.Position.X - tempoDragPointerOffset,
                    point.Position.Y);
                int rawTick = tracksVm.PointToTick(markerPoint);
                int newTick = NearestValidTempoSnap(tracksVm, rawTick);
                if (newTick != draggedTempo.position) {
                    DocManager.Inst.ExecuteCmd(new MoveTempoChangeCommand(
                        DocManager.Inst.Project,
                        draggedTempo,
                        newTick));
                }

                HighlightTempoMarker(draggedTempo);
                Cursor = ViewConstants.cursorSizeWE;
                args.Handled = true;
                return;
            }

            bool pointerIdle =
                !point.Properties.IsLeftButtonPressed &&
                !point.Properties.IsRightButtonPressed &&
                !point.Properties.IsMiddleButtonPressed;
            var hoveredTempo = pointerIdle
                ? HitTestTempoMarker(point.Position)
                : null;
            HighlightTempoMarker(hoveredTempo);
            if (hoveredTempo != null) {
                Cursor = ViewConstants.cursorSizeWE;
                args.Handled = true;
            }
        }

        private void TempoMarkerPointerReleased(
            object? sender,
            PointerReleasedEventArgs args) {
            if (draggedTempo == null) {
                return;
            }

            FinishTempoMarkerUndoGroup();
            draggedTempo = null;
            tempoDragHasMoved = false;
            args.Pointer.Capture(null);
            var hoveredTempo = HitTestTempoMarker(args.GetPosition(TimelineCanvas));
            HighlightTempoMarker(hoveredTempo);
            Cursor = hoveredTempo != null
                ? ViewConstants.cursorSizeWE
                : null;
            args.Handled = true;
        }

        private void TempoMarkerPointerExited(
            object? sender,
            PointerEventArgs args) {
            if (draggedTempo == null) {
                HighlightTempoMarker(null);
                Cursor = null;
            }
        }

        private void TempoMarkerPointerCaptureLost(
            object? sender,
            PointerCaptureLostEventArgs args) {
            if (draggedTempo == null) {
                return;
            }
            FinishTempoMarkerUndoGroup();
            draggedTempo = null;
            tempoDragHasMoved = false;
            HighlightTempoMarker(null);
            Cursor = null;
        }

        private int NearestValidTempoSnap(TracksViewModel tracksVm, int rawTick) {
            int boundedTick = Math.Clamp(rawTick, tempoDragMinTick, tempoDragMaxTick);
            tracksVm.TickToLineTick(boundedTick, out int left, out int right);
            bool leftValid = left >= tempoDragMinTick && left <= tempoDragMaxTick;
            bool rightValid = right >= tempoDragMinTick && right <= tempoDragMaxTick;
            if (leftValid && rightValid) {
                return Math.Abs((long)boundedTick - left) <= Math.Abs((long)right - boundedTick)
                    ? left
                    : right;
            }
            if (leftValid) {
                return left;
            }
            if (rightValid) {
                return right;
            }
            return draggedTempo?.position ?? tempoDragStartTick;
        }

        private void FinishTempoMarkerUndoGroup() {
            if (!DocManager.Inst.HasOpenUndoGroup) {
                return;
            }
            if (draggedTempo != null &&
                tempoDragHasMoved &&
                draggedTempo.position == tempoDragStartTick) {
                DocManager.Inst.RollBackUndoGroup();
            }
            DocManager.Inst.EndUndoGroup();
        }

        private void HighlightTempoMarker(UTempo? tempo) {
            if (ReferenceEquals(tempo, highlightedTempo)) {
                return;
            }
            highlightedTempo = tempo;
            MessageBus.Current.SendMessage(
                new TempoMarkerHighlightEvent(viewModel, tempo));
        }

        private UTempo? HitTestTempoMarker(Point point) {
            if (point.X < 0 || point.X > TimelineCanvas.Bounds.Width ||
                point.Y < 0 || point.Y > TimelineCanvas.Bounds.Height) {
                return null;
            }

            return DocManager.Inst.Project.tempos
                .Skip(1)
                .Select(tempo => new {
                    Tempo = tempo,
                    X = TempoMarkerX(tempo),
                    Width = Math.Max(
                        TempoMarkerHitPadding * 2,
                        tempo.bpm.ToString("#0.00").Length * 6 + 6),
                })
                .Where(marker =>
                    point.X >= marker.X - TempoMarkerHitPadding &&
                    point.X <= marker.X + marker.Width)
                .OrderBy(marker => Math.Abs(point.X - marker.X))
                .Select(marker => marker.Tempo)
                .FirstOrDefault();
        }

        private double TempoMarkerX(UTempo tempo) {
            return viewModel.TracksViewModel.TickTrackToPoint(tempo.position, 0).X;
        }
    }
}
