using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using DynamicData;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class TracksRefreshEvent { }
    public class TracksSoloEvent {
        public readonly int trackNo;
        public readonly bool solo;
        public readonly bool additionally;
        public TracksSoloEvent(int trackNo, bool solo, bool additionally) {
            this.trackNo = trackNo;
            this.solo = solo;
            this.additionally = additionally;
        }
    }
    public class TracksMuteEvent {
        public readonly int trackNo;
        public readonly bool allmute; // use only when track number is -1
        public TracksMuteEvent(int trackNo, bool allmute) {
            this.trackNo = trackNo;
            this.allmute = allmute;
        }
    }
    public class TrackSelectionEvent {
        public readonly UTrack[] selectedTracks;
        public TrackSelectionEvent(UTrack[] selectedTracks) {
            this.selectedTracks = selectedTracks;
        }
    }
    public class PartsSelectionEvent {
        public readonly UPart[] selectedParts;
        public readonly UPart[] tempSelectedParts;
        public PartsSelectionEvent(UPart[] selectedParts, UPart[] tempSelectedParts) {
            this.selectedParts = selectedParts;
            this.tempSelectedParts = tempSelectedParts;
        }
    }
    public class PartRefreshEvent {
        public readonly UPart part;
        public PartRefreshEvent(UPart part) { this.part = part; }
    }
    public class PartRedrawEvent {
        public readonly UPart part;
        public PartRedrawEvent(UPart part) { this.part = part; }
    }

    public class TracksViewModel : ViewModelBase, ICmdSubscriber {
        public UProject Project => DocManager.Inst.Project;
        [Reactive] public Rect Bounds { get; set; }
        public int TickCount => Math.Max(Project.timeAxis.BarBeatToTickPos(32, 0), Project.EndTick + 23040);
        public int TrackCount => Math.Max(20, Project.tracks.Count + 1);
        [Reactive] public double TickWidth { get; set; }
        public double TrackHeightMin => ViewConstants.TrackHeightMin;
        public double TrackHeightMax => ViewConstants.TrackHeightMax;
        [Reactive] public double TrackHeight { get; set; }
        [Reactive] public double TickOffset { get; set; }
        [Reactive] public double TrackOffset { get; set; }
        [Reactive] public int SnapDiv { get; set; }
        [Reactive] public int SnapUnit { get; set; }
        public ObservableCollectionExtended<int> SnapTicks { get; } = new ObservableCollectionExtended<int>();
        [Reactive] public double PlayPosX { get; set; }
        [Reactive] public double PlayPosHighlightX { get; set; }
        [Reactive] public double PlayPosHighlightWidth { get; set; }
        [Reactive] public bool PlayPosWaitingRendering { get; set; }
        public double ViewportTicks => viewportTicks.Value;
        public double ViewportTracks => viewportTracks.Value;
        public double SmallChangeX => smallChangeX.Value;
        public double SmallChangeY => smallChangeY.Value;
        public double HScrollBarMax => Math.Max(0, TickCount - ViewportTicks);
        public double VScrollBarMax => Math.Max(0, TrackCount - ViewportTracks);
        public ObservableCollectionExtended<UPart> Parts { get; } = new ObservableCollectionExtended<UPart>();
        public ObservableCollectionExtended<UTrack> Tracks { get; } = new ObservableCollectionExtended<UTrack>();

        // There are two kinds of values here. Let's call them PlayPosX values and TickOffset values.
        // Bounds.Width (which binds to the TrackBackground control) belongs to the former, and
        // ViewportTicks (which binds to the horizontal scroll bar) belongs to the latter. Remember
        // these two categories, and when you're reading the auto-scroll-related code, try to figure
        // out which category each value falls into. If you do so, you will find reading that part of
        // the code easier.
        //
        // ViewportTicks and Bounds.Width are chosen here to calculate the ratio between these two
        // kinds of values.
        //
        // These values could be better named so as to make the code more readable.
        private double playPosXToTickOffset => Bounds.Width != 0 ? ViewportTicks / Bounds.Width : 0;

        private readonly ObservableAsPropertyHelper<double> viewportTicks;
        private readonly ObservableAsPropertyHelper<double> viewportTracks;
        private readonly ObservableAsPropertyHelper<double> smallChangeX;
        private readonly ObservableAsPropertyHelper<double> smallChangeY;

        public readonly List<UPart> SelectedParts = new List<UPart>();
        public readonly List<UTrack> SelectedTracks = new List<UTrack>();
        private readonly HashSet<UPart> TempSelectedParts = new HashSet<UPart>();

        public TracksViewModel() {
            viewportTicks = this.WhenAnyValue(x => x.Bounds, x => x.TickWidth)
                .Select(v => v.Item1.Width / v.Item2)
                .ToProperty(this, x => x.ViewportTicks);
            viewportTracks = this.WhenAnyValue(x => x.Bounds, x => x.TrackHeight)
                .Select(v => v.Item1.Height / v.Item2)
                .ToProperty(this, x => x.ViewportTracks);
            smallChangeX = this.WhenAnyValue(x => x.ViewportTicks)
                .Select(w => w / 8)
                .ToProperty(this, x => x.SmallChangeX);
            smallChangeY = this.WhenAnyValue(x => x.ViewportTracks)
                .Select(h => h / 8)
                .ToProperty(this, x => x.SmallChangeY);
            this.WhenAnyValue(x => x.Bounds)
                .Subscribe(_ => {
                    OnXZoomed(new Point(), 0);
                    OnYZoomed(new Point(), 0);
                });

            this.WhenAnyValue(x => x.TickWidth)
                .Subscribe(tickWidth => {
                    UpdateSnapDiv();
                    SetPlayPos(DocManager.Inst.playPosTick, false);
                });
            this.WhenAnyValue(x => x.TickOffset)
                .Subscribe(tickOffset => {
                    SetPlayPos(DocManager.Inst.playPosTick, false);
                });

            TickWidth = ViewConstants.TickWidthDefault;
            TrackHeight = ViewConstants.TrackHeightDefault;
            Notify();

            DocManager.Inst.AddSubscriber(this);
        }

        private void UpdateSnapDiv() {
            MusicMath.GetSnapUnit(
                Project.resolution,
                ViewConstants.PianoRollMinTicklineWidth / TickWidth,
                false,
                out int ticks,
                out int div);
            SnapDiv = div;
            int snapUnit = Project.resolution * 4 / SnapDiv;
            while (snapUnit * TickWidth < ViewConstants.MinTicklineWidth) {
                snapUnit *= 2; // Avoid drawing too dense.
            }
            SnapUnit = snapUnit;
        }

        public void OnXZoomed(Point position, double delta) {
            bool recenter = true;
            if (TickOffset == 0 && position.X < 0.1) {
                recenter = false;
            }
            double center = TickOffset + position.X * ViewportTicks;
            double tickWidth = TickWidth * (1.0 + delta * 2);
            tickWidth = Math.Clamp(tickWidth, ViewConstants.TickWidthMin, ViewConstants.TickWidthMax);
            tickWidth = Math.Max(tickWidth, Bounds.Width / TickCount);
            TickWidth = tickWidth;
            double tickOffset = recenter
                    ? center - position.X * ViewportTicks
                    : TickOffset;
            TickOffset = Math.Clamp(tickOffset, 0, HScrollBarMax);
            Notify();
        }

        public void OnYZoomed(Point position, double delta) {
            double trackHeight = TrackHeight + Math.Sign(delta) * ViewConstants.TrackHeightDelta;
            trackHeight = Math.Clamp(trackHeight, ViewConstants.TrackHeightMin, ViewConstants.TrackHeightMax);
            trackHeight = Math.Max(trackHeight, TrackCount);
            TrackHeight = trackHeight;
            TrackOffset = Math.Clamp(TrackOffset, 0, VScrollBarMax);
            Notify();
        }

        private void Notify() {
            this.RaisePropertyChanged(nameof(TickCount));
            this.RaisePropertyChanged(nameof(HScrollBarMax));
            this.RaisePropertyChanged(nameof(ViewportTicks));
            this.RaisePropertyChanged(nameof(TrackCount));
            this.RaisePropertyChanged(nameof(VScrollBarMax));
            this.RaisePropertyChanged(nameof(ViewportTracks));
        }

        public int PointToTick(Point point) {
            return (int)(point.X / TickWidth + TickOffset);
        }

        public void TickToLineTick(int tick, out int left, out int right) {
            if(tick < 0 || SnapUnit <= 0){
                left = right = 0;
                return;
            }
            double minLineTick = ViewConstants.MinTicklineWidth / TickWidth;
            var timeSig = Project.timeAxis.TimeSignatureAtTick(tick);
            Project.timeAxis.TickPosToBarBeat(tick, out int bar, out int beat, out int remainingTicks);
            int barTick = Project.timeAxis.BarBeatToTickPos(bar, 0);
            int nextBarTick = Project.timeAxis.BarBeatToTickPos(bar + 1, 0);
            int ticksPerBeat = Project.resolution * 4 * timeSig.beatPerBar / timeSig.beatUnit;
            int ticksPerLine = SnapUnit;
            if (ticksPerBeat < SnapUnit) {
                ticksPerLine = ticksPerBeat;
            } else if (ticksPerBeat % SnapUnit != 0) {
                if (ticksPerBeat > minLineTick) {
                    ticksPerLine = ticksPerBeat;
                } else {
                    ticksPerLine = nextBarTick - barTick;
                }
            }
            left = barTick + (tick - barTick) / ticksPerLine * ticksPerLine;
            right = left + ticksPerLine;
        }

        public void PointToLineTick(Point point, out int left, out int right) {
            int tick = PointToTick(point);
            TickToLineTick(tick, out left, out right);
        }

        public int PointToTrackNo(Point point) {
            return (int)(point.Y / TrackHeight + TrackOffset);
        }

        public Point TickTrackToPoint(int tick, int trackNo) {
            return new Point(
                (tick - TickOffset) * TickWidth,
                (trackNo - TrackOffset) * TrackHeight);
        }

        public Size TickTrackToSize(int ticks, int tracks) {
            return new Size(ticks * TickWidth, tracks * TrackHeight);
        }

        public void AddTrack() {
            var project = DocManager.Inst.Project;
            DocManager.Inst.StartUndoGroup("command.track.add");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = project.tracks.Count() }));
            DocManager.Inst.EndUndoGroup();
        }

        public UPart? MaybeAddPart(Point point) {
            int trackNo = PointToTrackNo(point);
            var project = DocManager.Inst.Project;
            if (trackNo >= project.tracks.Count) {
                return null;
            }
            PointToLineTick(point, out int left, out int right);
            project.timeAxis.TickPosToBarBeat(left, out int bar, out int beat, out int remainingTicks);
            var durTick = project.timeAxis.BarBeatToTickPos(bar + 4, beat) + remainingTicks - left;
            UVoicePart part = new UVoicePart() {
                position = left,
                trackNo = trackNo,
                Duration = durTick,
            };
            DocManager.Inst.StartUndoGroup("command.part.add");
            DocManager.Inst.ExecuteCmd(new AddPartCommand(project, part));
            DocManager.Inst.EndUndoGroup();
            return part;
        }

        public void DeselectParts() {
            SelectedParts.Clear();
            TempSelectedParts.Clear();
            MessageBus.Current.SendMessage(
                new PartsSelectionEvent(
                    SelectedParts.ToArray(), TempSelectedParts.ToArray()));
        }

        public void DeselectTracks() {
            SelectedTracks.Clear();
            MessageBus.Current.SendMessage(new TrackSelectionEvent(SelectedTracks.ToArray()));
        }

        public void SelectTrack(UTrack track) {
            if (SelectedTracks.Count == 1 && SelectedTracks[0] == track) {
                return;
            }
            SelectedTracks.Clear();
            SelectedTracks.Add(track);
            MessageBus.Current.SendMessage(new TrackSelectionEvent(SelectedTracks.ToArray()));
        }

        public void ToggleSelectTrack(UTrack track) {
            if (SelectedTracks.Contains(track)) {
                SelectedTracks.Remove(track);
            } else {
                SelectedTracks.Add(track);
            }
            MessageBus.Current.SendMessage(new TrackSelectionEvent(SelectedTracks.ToArray()));
        }

        public void SelectTracksUntil(UTrack track) {
            if (SelectedTracks.Count == 0) {
                SelectTrack(track);
                return;
            }
            int start = SelectedTracks.Min(selected => selected.TrackNo);
            int end = track.TrackNo;
            if (start > end) {
                (start, end) = (end, start);
            }
            SelectedTracks.Clear();
            SelectedTracks.AddRange(Project.tracks.Where(t => start <= t.TrackNo && t.TrackNo <= end));
            MessageBus.Current.SendMessage(new TrackSelectionEvent(SelectedTracks.ToArray()));
        }

        public void SelectPart(UPart part) {
            TempSelectedParts.Clear();
            SelectedParts.Add(part);
            MessageBus.Current.SendMessage(
                new PartsSelectionEvent(
                    SelectedParts.ToArray(), TempSelectedParts.ToArray()));
        }

        public void SelectAllParts() {
            var project = DocManager.Inst.Project;
            DeselectParts();
            SelectedParts.AddRange(project.parts);
            MessageBus.Current.SendMessage(
                new PartsSelectionEvent(
                    SelectedParts.ToArray(), TempSelectedParts.ToArray()));
        }

        public void TempSelectParts(int x0, int x1, int y0, int y1) {
            var project = DocManager.Inst.Project;
            TempSelectedParts.Clear();
            foreach (var part in project.parts) {
                if (part.End > x0 && part.position < x1 && part.trackNo >= y0 && part.trackNo < y1) {
                    TempSelectedParts.Add(part);
                }
            }
            MessageBus.Current.SendMessage(
                new PartsSelectionEvent(
                    SelectedParts.ToArray(), TempSelectedParts.ToArray()));
        }

        public void CommitTempSelectParts() {
            var newSelection = SelectedParts.Union(TempSelectedParts).ToList();
            SelectedParts.Clear();
            SelectedParts.AddRange(newSelection);
            TempSelectedParts.Clear();
            MessageBus.Current.SendMessage(
                new PartsSelectionEvent(
                    SelectedParts.ToArray(), TempSelectedParts.ToArray()));
        }

        public void DeleteSelectedParts() {
            if (SelectedParts.Count <= 0) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.part.delete");
            var selectedParts = SelectedParts.ToArray();
            foreach (var part in selectedParts) {
                DocManager.Inst.ExecuteCmd(new RemovePartCommand(Project, part));
            }
            DocManager.Inst.EndUndoGroup();
            DeselectParts();
        }

        public void CopyParts() {
            if (SelectedParts.Count > 0) {
                DocManager.Inst.PartsClipboard = SelectedParts.Select(part => part.Clone()).ToList();
            }
        }

        public void CutParts() {
            if (SelectedParts.Count > 0) {
                DocManager.Inst.PartsClipboard = SelectedParts.Select(part => part.Clone()).ToList();
                DocManager.Inst.StartUndoGroup("command.part.delete");
                var toRemove = new List<UPart>(SelectedParts);
                SelectedParts.Clear();
                foreach (var part in toRemove) {
                    DocManager.Inst.ExecuteCmd(new RemovePartCommand(Project, part));
                }
                DocManager.Inst.EndUndoGroup();
            }
        }

        public void PasteParts() {
            if (DocManager.Inst.PartsClipboard == null || DocManager.Inst.PartsClipboard.Count == 0) {
                return;
            }
            var parts = DocManager.Inst.PartsClipboard
                .Select(part => part.Clone())
                .OrderBy(part => part.trackNo).ToList();
            int newTrackNo = Project.parts.Count > 0 ? Project.parts.Max(part => part.trackNo) : -1;
            int oldTrackNo = -1;
            foreach (var part in parts) {
                if (part.trackNo > oldTrackNo) {
                    oldTrackNo = part.trackNo;
                    newTrackNo++;
                }
                part.trackNo = newTrackNo;
            }
            DocManager.Inst.StartUndoGroup("command.part.paste");
            while (Project.tracks.Count <= newTrackNo) {
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(Project, new UTrack(Project) {
                    TrackNo = Project.tracks.Count,
                }));
            }
            foreach (var part in parts) {
                DocManager.Inst.ExecuteCmd(new AddPartCommand(Project, part));
            }
            DocManager.Inst.EndUndoGroup();
            DeselectParts();
            SelectedParts.AddRange(parts);
            MessageBus.Current.SendMessage(
                new PartsSelectionEvent(
                    SelectedParts.ToArray(), TempSelectedParts.ToArray()));
        }


        private void SetPlayPos(int tick, bool waitingRendering) {
            PlayPosWaitingRendering = waitingRendering;
            if (waitingRendering) {
                return;
            }
            PlayPosX = TickTrackToPoint(tick, 0).X;
            TickToLineTick(tick, out int left, out int right);
            PlayPosHighlightX = TickTrackToPoint(left, 0).X;
            PlayPosHighlightWidth = (right - left) * TickWidth;
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is NoteCommand noteCommand) {
                if (noteCommand is ResizeNoteCommand || noteCommand is AddNoteCommand) {
                    MessageBus.Current.SendMessage(new PartRefreshEvent(noteCommand.Part));
                }
                MessageBus.Current.SendMessage(new PartRedrawEvent(noteCommand.Part));
            } else if (cmd is PartCommand partCommand) {
                if (partCommand is AddPartCommand) {
                    if (!isUndo) {
                        Parts.Add(partCommand.part);
                    } else {
                        Parts.Remove(partCommand.part);
                        SelectedParts.Remove(partCommand.part);
                    }
                } else if (partCommand is RemovePartCommand) {
                    if (!isUndo) {
                        Parts.Remove(partCommand.part);
                        SelectedParts.Remove(partCommand.part);
                    } else {
                        Parts.Add(partCommand.part);
                    }
                } else if (partCommand is ReplacePartCommand replacePart) {
                    if (!isUndo) {
                        Parts.Remove(replacePart.part);
                        Parts.Add(replacePart.newPart);
                    } else {
                        Parts.Remove(replacePart.newPart);
                        Parts.Add(replacePart.part);
                    }
                }
                MessageBus.Current.SendMessage(new PartRefreshEvent(partCommand.part));
                Notify();
            } else if (cmd is TrackCommand) {
                if (cmd is AddTrackCommand addTrack) {
                    if (!isUndo) {
                        Tracks.Add(addTrack.track);
                    } else {
                        Tracks.Remove(addTrack.track);
                    }
                } else if (cmd is RemoveTrackCommand removeTrack) {
                    if (!isUndo) {
                        Tracks.Remove(removeTrack.track);
                        SelectedTracks.Remove(removeTrack.track);
                    } else {
                        Tracks.Add(removeTrack.track);
                    }
                }
                Notify();
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
                MessageBus.Current.SendMessage(new TrackSelectionEvent(SelectedTracks.ToArray()));
            } else if (cmd is UNotification) {
                if (cmd is LoadProjectNotification loadProjectNotif) {
                    Parts.Clear();
                    Parts.AddRange(loadProjectNotif.project.parts);
                    Tracks.Clear();
                    Tracks.AddRange(loadProjectNotif.project.tracks);
                    SelectedTracks.Clear();
                    MessageBus.Current.SendMessage(new TracksRefreshEvent());
                    MessageBus.Current.SendMessage(new TrackSelectionEvent(SelectedTracks.ToArray()));
                } else if (cmd is SetPlayPosTickNotification setPlayPosTick) {
                    SetPlayPos(setPlayPosTick.playPosTick, setPlayPosTick.waitingRendering);
                    if (!setPlayPosTick.pause || Preferences.Default.LockStartTime == 1) {
                        MaybeAutoScroll();
                    }
                } else if (cmd is LoadPartNotification loadPartNotif) {
                    if (SelectedParts.Count != 1 || SelectedParts.First() != loadPartNotif.part) {
                        DeselectParts();
                        SelectPart(loadPartNotif.part);
                    }
                    if (0 <= loadPartNotif.part.trackNo && loadPartNotif.part.trackNo < Project.tracks.Count) {
                        SelectTrack(Project.tracks[loadPartNotif.part.trackNo]);
                    }
                }
                Notify();
            }
        }

        private void MaybeAutoScroll() {
            var autoScrollPreference = Convert.ToBoolean(Preferences.Default.PlaybackAutoScroll);
            if (autoScrollPreference) {
                AutoScroll();
            }
        }

        private void AutoScroll() {
            double scrollDelta = GetScrollValueDelta();
            TickOffset = Math.Clamp(TickOffset + scrollDelta, 0, HScrollBarMax);
        }

        private double GetScrollValueDelta() {
            var pageScroll = Preferences.Default.PlaybackAutoScroll == 2;
            if (pageScroll) {
                return GetPageScrollScrollValueDelta();
            }
            return GetStationaryCursorScrollValueDelta();
        }

        private double GetStationaryCursorScrollValueDelta() {
            double rightMargin = Preferences.Default.PlayPosMarkerMargin * Bounds.Width;
            if (PlayPosX > rightMargin) {
                return (PlayPosX - rightMargin) * playPosXToTickOffset;
            } else if (PlayPosX < 0) {
                return PlayPosX * playPosXToTickOffset;
            }
            return 0;
        }

        private double GetPageScrollScrollValueDelta() {
            double leftMargin = (1 - Preferences.Default.PlayPosMarkerMargin) * Bounds.Width;
            if (PlayPosX > Bounds.Width) {
                return (Bounds.Width - leftMargin) * playPosXToTickOffset;
            } else if (PlayPosX < 0) {
                return (PlayPosX - leftMargin) * playPosXToTickOffset;
            }
            return 0;
        }
    }
}
