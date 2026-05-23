using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DynamicData;
using DynamicData.Binding;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SharpCompress;

namespace OpenUtau.App.ViewModels {
    public class NotesRefreshEvent { }
    public class NotesSelectionEvent {
        public readonly UNote[] selectedNotes;
        public readonly UNote[] tempSelectedNotes;
        public NotesSelectionEvent(NoteSelectionViewModel selection) {
            selectedNotes = selection.ToArray();
            tempSelectedNotes = selection.TempSelectedNotes.ToArray();
        }
    }
    public class WaveformRefreshEvent { }

    public class NotesViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public Rect Bounds { get; set; }
        public int TickCount => Part?.Duration ?? 480 * 4;
        public int TrackCount => ViewConstants.MaxTone;
        [Reactive] public double TickWidth { get; set; }
        public double TrackHeightMin => ViewConstants.NoteHeightMin;
        public double TrackHeightMax => ViewConstants.NoteHeightMax;
        [Reactive] public double TrackHeight { get; set; }
        [Reactive] public int TickOrigin { get; set; }
        [Reactive] public double TickOffset { get; set; }
        [Reactive] public double TrackOffset { get; set; }
        [Reactive] public int SnapDiv { get; set; }
        [Reactive] public int Key { get; set; }
        public ObservableCollectionExtended<int> SnapTicks { get; } = new ObservableCollectionExtended<int>();
        [Reactive] public double PlayPosX { get; set; }
        [Reactive] public double PlayPosHighlightX { get; set; }
        [Reactive] public double PlayPosHighlightWidth { get; set; }
        [Reactive] public bool PlayPosWaitingRendering { get; set; }
        [Reactive] public bool CursorTool { get; set; }
        [Reactive] public bool PenTool { get; set; }
        [Reactive] public bool PenPlusTool { get; set; }
        [Reactive] public bool EraserTool { get; set; }
        [Reactive] public bool DrawPitchTool { get; set; }
        [Reactive] public bool DrawLinePitchTool { get; set; }
        [Reactive] public bool OverwritePitchTool { get; set; }
        [Reactive] public bool OverwriteLinePitchTool { get; set; }
        [Reactive] public bool KnifeTool { get; set; }
        public ReactiveCommand<string, Unit> SelectToolCommand { get; }
        [Reactive] public bool ShowTips { get; set; }
        [Reactive] public bool PlayTone { get; set; }
        [Reactive] public bool ShowVibrato { get; set; }
        [Reactive] public bool ShowPitch { get; set; }
        [Reactive] public bool ShowFinalPitch { get; set; }
        [Reactive] public bool ShowWaveform { get; set; }
        [Reactive] public bool ShowPhoneme { get; set; }
        [Reactive] public bool ShowNoteParams { get; set; }
        [Reactive] public bool ShowExpressions { get; set; }
        [Reactive] public bool IsSnapOn { get; set; }
        [Reactive] public string SnapDivText { get; set; }
        [Reactive] public string KeyText { get; set; }
        [Reactive] public Rect ExpBounds { get; set; }
        [Reactive] public string PrimaryKey { get; set; }
        [Reactive] public bool PrimaryKeyNotSupported { get; set; }
        [Reactive] public bool ShowCurveToolbox { get; set; }
        [Reactive] public string SecondaryKey { get; set; }
        [Reactive] public double ExpTrackHeight { get; set; }
        [Reactive] public double ExpShadowOpacity { get; set; }
        [Reactive] public double ExpHeightMin { get; set; }
        [Reactive] public double ExpHeightMax { get; set; }
        [Reactive] public UVoicePart? Part { get; set; }
        [Reactive] public Bitmap? Avatar { get; set; }
        [Reactive] public Bitmap? Portrait { get; set; }
        [Reactive] public IBrush? PortraitMask { get; set; }
        [Reactive] public string WindowTitle { get; set; } = "Piano Roll";
        [Reactive] public SolidColorBrush TrackAccentColor { get; set; } = ThemeManager.GetTrackColor("Blue").AccentColor;
        public double ViewportTicks => viewportTicks.Value;
        public double ViewportTracks => viewportTracks.Value;
        public double SmallChangeX => smallChangeX.Value;
        public double SmallChangeY => smallChangeY.Value;
        public double HScrollBarMax => Math.Max(0, TickCount - ViewportTicks);
        public double VScrollBarMax => Math.Max(0, TrackCount - ViewportTracks);
        public UProject Project => DocManager.Inst.Project;
        [Reactive] public List<MenuItemViewModel> SnapDivs { get; set; }
        [Reactive] public List<MenuItemViewModel> Keys { get; set; }

        public ReactiveCommand<int, Unit> SetSnapUnitCommand { get; set; }
        public ReactiveCommand<int, Unit> SetKeyCommand { get; set; }

        // See the comments on TracksViewModel.playPosXToTickOffset
        private double playPosXToTickOffset => Bounds.Width != 0 ? ViewportTicks / Bounds.Width : 0;

        private readonly ObservableAsPropertyHelper<double> viewportTicks;
        private readonly ObservableAsPropertyHelper<double> viewportTracks;
        private readonly ObservableAsPropertyHelper<double> smallChangeX;
        private readonly ObservableAsPropertyHelper<double> smallChangeY;

        public readonly NoteSelectionViewModel Selection = new NoteSelectionViewModel();

        internal NotesViewModelHitTest HitTest;
        private int _lastNoteLength = 480;
        private string? portraitSource;
        private readonly object portraitLock = new object();
        private int userSnapDiv = -2;
        private int userKey => Project.key;

        public NotesViewModel() {
            SnapDivs = new List<MenuItemViewModel>();
            SetSnapUnitCommand = ReactiveCommand.Create<int>(div => {
                userSnapDiv = div;
                UpdateSnapDiv();
            });

            Keys = new List<MenuItemViewModel>();
            SetKeyCommand = ReactiveCommand.Create<int>(key => {
                DocManager.Inst.StartUndoGroup("command.project.key");
                DocManager.Inst.ExecuteCmd(new KeyCommand(Project, key));
                DocManager.Inst.EndUndoGroup();
                UpdateKey();
            });

            viewportTicks = this.WhenAnyValue(x => x.Bounds, x => x.TickWidth)
                .Select(v => v.Item1.Width / Math.Max(v.Item2, ViewConstants.TickWidthMin))
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
            this.WhenAnyValue(x => x.ExpBounds, x => x.PrimaryKey)
                .Subscribe(t => {
                    if (t.Item2 != null && Project.expressions.TryGetValue(t.Item2, out var descriptor)) {
                        if (descriptor.type == UExpressionType.Options && descriptor.options.Length > 0) {
                            ExpTrackHeight = t.Item1.Height / descriptor.options.Length;
                            ExpShadowOpacity = 0;
                        }
                        ShowCurveToolbox = descriptor.type == UExpressionType.Curve;
                    } else {
                        ExpTrackHeight = 0;
                        ExpShadowOpacity = 0.3;
                        ShowCurveToolbox = false;
                    }
                });
            this.WhenAnyValue(x => x.Project)
                .Subscribe(project => {
                    if (project == null) {
                        return;
                    }
                    SnapDivs.Clear();
                    SnapDivs.Add(new MenuItemViewModel {
                        Header = ThemeManager.GetString("pianoroll.toggle.snap.auto"),
                        Command = SetSnapUnitCommand,
                        CommandParameter = -2,
                    });
                    SnapDivs.Add(new MenuItemViewModel {
                        Header = ThemeManager.GetString("pianoroll.toggle.snap.autotriplet"),
                        Command = SetSnapUnitCommand,
                        CommandParameter = -3,
                    });
                    SnapDivs.AddRange(MusicMath.GetSnapDivs(project.resolution)
                        .Select(div => new MenuItemViewModel {
                            Header = $"1/{div}",
                            Command = SetSnapUnitCommand,
                            CommandParameter = div,
                        }));
                    Keys.Clear();
                    Keys.AddRange(MusicMath.KeysInOctave
                        .Select((key, index) => new MenuItemViewModel {
                            Header = $"1={key.Item1}",
                            Command = SetKeyCommand,
                            CommandParameter = index,
                        }));
                });

            CursorTool = false;
            if (Preferences.Default.PenPlusDefault) {
                PenPlusTool = true;
                PenTool = false;
            } else {
                PenTool = true;
                PenPlusTool = false;
            }
            EraserTool = false;
            DrawPitchTool = false;
            DrawLinePitchTool = false;
            OverwritePitchTool = false;
            OverwriteLinePitchTool = false;
            KnifeTool = false;
            SelectToolCommand = ReactiveCommand.Create<string>(index => {
                CursorTool = index == "1";
                PenTool = index == "2";
                PenPlusTool = index == "2+";
                EraserTool = index == "3";
                DrawPitchTool = index == "4";
                OverwritePitchTool = index == "4+";
                DrawLinePitchTool = index == "4++";
                OverwriteLinePitchTool = index == "4+++";
                KnifeTool = index == "5";
            });

            ShowTips = Preferences.Default.ShowTips;
            IsSnapOn = true;
            SnapDivText = string.Empty;
            KeyText = string.Empty;

            PlayTone = Preferences.Default.PlayTone;
            this.WhenAnyValue(x => x.PlayTone)
             .Subscribe(playTone => {
                 Preferences.Default.PlayTone = playTone;
                 Preferences.Save();
             });
            ShowVibrato = Preferences.Default.ShowVibrato;
            this.WhenAnyValue(x => x.ShowVibrato)
            .Subscribe(showVibrato => {
                Preferences.Default.ShowVibrato = showVibrato;
                Preferences.Save();
            });
            ShowPitch = Preferences.Default.ShowPitch;
            this.WhenAnyValue(x => x.ShowPitch)
            .Subscribe(showPitch => {
                Preferences.Default.ShowPitch = showPitch;
                Preferences.Save();
            });
            ShowFinalPitch = Preferences.Default.ShowFinalPitch;
            this.WhenAnyValue(x => x.ShowFinalPitch)
            .Subscribe(showFinalPitch => {
                Preferences.Default.ShowFinalPitch = showFinalPitch;
                Preferences.Save();
            });
            ShowWaveform = Preferences.Default.ShowWaveform;
            this.WhenAnyValue(x => x.ShowWaveform)
            .Subscribe(showWaveform => {
                Preferences.Default.ShowWaveform = showWaveform;
                Preferences.Save();
            });
            ShowPhoneme = Preferences.Default.ShowPhoneme;
            this.WhenAnyValue(x => x.ShowPhoneme)
            .Subscribe(showPhoneme => {
                Preferences.Default.ShowPhoneme = showPhoneme;
                Preferences.Save();
            });
            ShowExpressions = Preferences.Default.ShowExpressions;
            this.WhenAnyValue(x => x.ShowExpressions)
            .Subscribe(showExpressions => {
                ExpHeightMin = showExpressions
                    ? ViewConstants.ExpHeightMin : 0;
                ExpHeightMax = showExpressions
                    ? ViewConstants.ExpHeightMax : 0;
                Preferences.Default.ShowExpressions = showExpressions;
                Preferences.Save();
            });
            ShowNoteParams = Preferences.Default.ShowNoteParams;
            this.WhenAnyValue(x => x.ShowNoteParams)
            .Subscribe(showNoteParams => {
                Preferences.Default.ShowNoteParams = showNoteParams;
                Preferences.Save();
            });

            TickWidth = ViewConstants.PianoRollTickWidthDefault;
            TrackHeight = ViewConstants.NoteHeightDefault;
            TrackOffset = 4 * 12 + 6;
            if (Preferences.Default.ShowTips) {
                Preferences.Default.ShowTips = false;
                Preferences.Save();
            }
            PrimaryKey = Core.Format.Ustx.VEL;
            SecondaryKey = Core.Format.Ustx.VOL;

            HitTest = new NotesViewModelHitTest(this);
            DocManager.Inst.AddSubscriber(this);

            MessageBus.Current.Listen<PianorollRefreshEvent>()
                .Subscribe(e => {
                    switch (e.refreshItem) {
                        case "Part":
                            if (Part == null || Project == null) {
                                UnloadPart();
                            } else {
                                LoadPart(Part, Project);
                            }
                            break;
                        case "Portrait":
                            LoadPortrait(Part, Project);
                            break;
                        case "TrackColor":
                            LoadTrackColor(Part, Project);
                            break;
                    }
                });
            MessageBus.Current.Listen<CurveSelectionEvent>()
                .Subscribe(e => {
                    Selection.SelectNone();
                    MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                });
            MessageBus.Current.Listen<CurveCopyEvent>()
                .Subscribe(e => {
                    DocManager.Inst.NotesClipboard?.Clear();
                });
        }

        private void UpdateSnapDiv() {
            if (userSnapDiv > 0) {
                SnapDiv = userSnapDiv;
                SnapDivText = $"1/{userSnapDiv}";
                return;
            }
            MusicMath.GetSnapUnit(
                Project.resolution,
                ViewConstants.PianoRollMinTicklineWidth / TickWidth,
                userSnapDiv % 3 == 0,
                out int ticks,
                out int div);
            SnapDiv = div;
            SnapDivText = $"(1/{div})";
        }

        private void UpdateKey(){
            Key = userKey;
            KeyText = "1="+MusicMath.KeysInOctave[userKey].Item1;
        }

        public void OnXZoomed(Point position, double delta) {
            bool recenter = true;
            if (TickOffset == 0 && position.X < 0.1) {
                recenter = false;
            }
            double center = TickOffset + position.X * ViewportTicks;
            double tickWidth = TickWidth * (1.0 + delta * 2);
            tickWidth = Math.Clamp(tickWidth, ViewConstants.PianoRollTickWidthMin, ViewConstants.PianoRollTickWidthMax);
            tickWidth = Math.Max(tickWidth, Bounds.Width / TickCount);
            TickWidth = tickWidth;
            double tickOffset = recenter
                    ? center - position.X * ViewportTicks
                    : TickOffset;
            TickOffset = Math.Clamp(tickOffset, 0, HScrollBarMax);
            Notify();
        }

        public void OnYZoomed(Point position, double delta) {
            double center = TrackOffset + position.Y * ViewportTracks;
            double trackHeight = TrackHeight * (1.0 + delta * 2);
            trackHeight = Math.Clamp(trackHeight, ViewConstants.NoteHeightMin, ViewConstants.NoteHeightMax);
            trackHeight = Math.Max(trackHeight, Bounds.Height / TrackCount);
            TrackHeight = trackHeight;
            double trackOffset = center - position.Y * ViewportTracks;
            TrackOffset = Math.Clamp(trackOffset, 0, VScrollBarMax);
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

        /// <summary>
        /// Convert mouse position in piano roll window to tick in part
        /// </summary>
        /// <param name="point">Mouse position</param>
        /// <returns>Tick position related to the beginning of the part</returns>
        public int PointToTick(Point point) {
            return (int)(point.X / TickWidth + TickOffset);
        }

        public void TickToLineTick(int tick, out int left, out int right) {
            if (SnapTicks.Count == 0) {
                left = 0;
                right = Project.resolution;
                return;
            }
            int index = SnapTicks.BinarySearch(tick + TickOrigin);
            if (index < 0) {
                index = ~index - 1;
            }
            if (0 >= SnapTicks.Count - 2) {
                left = right = tick;
                return;
            }
            index = Math.Clamp(index, 0, SnapTicks.Count - 2);
            left = SnapTicks[index] - TickOrigin;
            right = SnapTicks[index + 1] - TickOrigin;
        }

        public void PointToLineTick(Point point, out int left, out int right) {
            int tick = PointToTick(point);
            TickToLineTick(tick, out left, out right);
        }

        public int PointToTone(Point point) {
            return ViewConstants.MaxTone - 1 - (int)(point.Y / TrackHeight + TrackOffset);
        }
        public double PointToToneDouble(Point point) {
            return ViewConstants.MaxTone - 1 - (point.Y / TrackHeight + TrackOffset) + 0.5;
        }
        public Point TickToneToPoint(double tick, double tone) {
            return new Point(
                (tick - TickOffset) * TickWidth,
                (ViewConstants.MaxTone - 1 - tone - TrackOffset) * TrackHeight);
        }
        public Point TickToneToPoint(Vector2 tickTone) {
            return TickToneToPoint(tickTone.X, tickTone.Y);
        }
        public Size TickToneToSize(double ticks, double tone) {
            return new Size(ticks * TickWidth, tone * TrackHeight);
        }

        public UNote? MaybeAddNote(Point point, bool useLastLength) {
            if (Part == null) {
                return null;
            }
            var project = DocManager.Inst.Project;
            int tone = PointToTone(point);
            if (tone >= ViewConstants.MaxTone || tone < 0) {
                return null;
            }
            int snapUnit = project.resolution * 4 / SnapDiv;
            int tick = PointToTick(point);
            int snappedTick = (int)Math.Floor((double)tick / snapUnit) * snapUnit;
            UNote note = project.CreateNote(tone, snappedTick,
                useLastLength ? _lastNoteLength : IsSnapOn ? snapUnit : 15);
            DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, note));
            return note;
        }

        private void LoadPart(UPart part, UProject project) {
            if (!(part is UVoicePart)) {
                return;
            }
            UnloadPart();
            Part = part as UVoicePart;
            OnPartModified();
            LoadPortrait(part, project);
            LoadWindowTitle(part, project);
            LoadTrackColor(part, project);
            UpdateKey();
        }

        //If PortraitHeight is 0, the default behaviour is resizing any image taller than 800px to 800px,
        //and keeping the sizes of images shorter than 800px unchanged.
        //If PortraitHeight isn't 0, then the image will be resized to the specified height.
        private Bitmap ResizePortrait(Bitmap Portrait, int PortraitHeight) {
            int targetHeight;
            if (PortraitHeight == 0) {
                if (Portrait.Size.Height > 800) {
                    targetHeight = 800;
                } else {
                    return Portrait;
                }
            } else {
                targetHeight = PortraitHeight;
            }
            int targetWidth = (int)Math.Round(targetHeight * Portrait.Size.Width / Portrait.Size.Height);
            if(targetWidth == 0){
                targetWidth = 1;
            }
            return Portrait.CreateScaledBitmap(new PixelSize(targetWidth, targetHeight));
        }

        private void LoadPortrait(UPart? part, UProject? project) {
            if (part == null || project == null) {
                lock (portraitLock) {
                    Avatar = null;
                    Portrait = null;
                    portraitSource = null;
                }
                return;
            }
            var singer = project.tracks[part.trackNo].Singer;
            lock (portraitLock) {
                Avatar?.Dispose();
                Avatar = null;
                if (singer != null && singer.AvatarData != null && Preferences.Default.ShowIcon) {
                    try {
                        using (var stream = new MemoryStream(singer.AvatarData)) {
                            Avatar = new Bitmap(stream);
                        }
                    } catch (Exception e) {
                        Avatar?.Dispose();
                        Avatar = null;
                        Log.Error(e, $"Failed to load Avatar {singer.Avatar}");
                    }
                }
            }
            if (singer == null || string.IsNullOrEmpty(singer.Portrait) || !Preferences.Default.ShowPortrait) {
                lock (portraitLock) {
                    Portrait = null;
                    portraitSource = null;
                }
                return;
            }
            if (portraitSource != singer.Portrait) {
                lock (portraitLock) {
                    Portrait?.Dispose();
                    Portrait = null;
                    portraitSource = null;
                }
                PortraitMask = new SolidColorBrush(Avalonia.Media.Colors.White, singer.PortraitOpacity);
                Task.Run(() => {
                    lock (portraitLock) {
                        try {
                            var data = singer.LoadPortrait();
                            if (data == null) {
                                Portrait = null;
                                portraitSource = null;
                            } else {
                                using (var stream = new MemoryStream(data)) { 
                                    Portrait = ResizePortrait(new Bitmap(stream), singer.PortraitHeight);
                                    portraitSource = singer.Portrait;
                                }
                            }
                        } catch (Exception e) {
                            Portrait?.Dispose();
                            Portrait = null;
                            portraitSource = null;
                            Log.Error(e, $"Failed to load Portrait {singer.Portrait}");
                        }
                    }
                });
            }
        }
        private void LoadWindowTitle(UPart? part, UProject? project) {
            if (part == null || project == null) {
                WindowTitle = "Piano Roll";
                return;
            }
            WindowTitle = project.tracks[part.trackNo].TrackName + " - " + part.DisplayName;
        }

        private void LoadTrackColor(UPart? part, UProject? project) {
            if (part == null || project == null) {
                TrackAccentColor = ThemeManager.GetTrackColor("Blue").AccentColor;
                ThemeManager.ChangePianorollColor("Blue");
                return;
            }
            TrackAccentColor = ThemeManager.GetTrackColor(project.tracks[part.trackNo].TrackColor).AccentColor;
            string name = Preferences.Default.UseTrackColor
                ? project.tracks[part.trackNo].TrackColor
                : "Blue";
            ThemeManager.ChangePianorollColor(name);
        }

        private void UnloadPart() {
            DeselectNotes();
            Part = null;
            LoadPortrait(null, null);
            LoadWindowTitle(null, null);
        }

        private void OnPartModified() {
            if (Part == null) {
                return;
            }
            TickOrigin = Part.position;
            Notify();
        }

        private void DeselectNote(UNote note) {
            if (Selection.Remove(note)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            }
        }

        public void DeselectNotes() {
            Selection.SelectNone();
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void ToggleSelectNote(UNote note) {
            /// <summary>
            /// Change the selection state of a note without affecting the selection state of the other notes.
            /// Add it to selection if it isn't selected, or deselect it if it is already selected.
            /// </summary>
            if (Part == null) {
                return;
            }
            if (Selection.Contains(note)) {
                DeselectNote(note);
            } else {
                SelectNote(note, false);
            }
        }

        public void SelectNote(UNote note) {
            /// <summary>
            /// Select a note and deselect all the other notes.
            /// </summary>
            SelectNote(note, true);
        }
        public void SelectNote(UNote note, bool deselectExisting) {
            if (Part == null) {
                return;
            }
            if (deselectExisting ? Selection.Select(note) : Selection.Add(note)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            }
        }
        public void MoveSelection(int delta) {
            if (Selection.Move(delta)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                ScrollIntoView(Selection.Head!);
            };
        }
        public void ExtendSelection(int delta) {
            if (Selection.Resize(delta)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                ScrollIntoView(Selection.Head!);
            };
        }
        public void ExtendSelection(UNote note) {
            if (Selection.SelectTo(note)) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            };
        }

        public void MoveCursor(int delta) {
            if (!Selection.IsEmpty) {
                MoveSelection(delta);
                return;
            }
            var target = Part!.notes.FirstOrDefault();
            if (target == null) {
                return;
            }
            var centerTick = TickOffset + ViewportTicks * 0.5;
            // get closest note to center, without going over
            while (target.Next != null && (target!.position < TickOffset || target!.Next.position < centerTick)) {
                target = target.Next;
            }
            SelectNote(target);
            ScrollIntoView(target);
        }

        public void SelectAllNotes() {
            if (Part == null) {
                return;
            }
            Selection.Select(Part);
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void SelectNotesUntil(UNote note) {
            if (Part == null) {
                return;
            }
            if (Part.notes.Intersect(Selection).ToList().Count == 0) {
                SelectNote(note);
                return;
            }
            var thisIndex = Part.notes.IndexOf(note);
            if (thisIndex < 0) {
                return;
            }
            var firstSelectedNote = Part.notes.FirstOrDefault(x => Selection.Contains(x));
            if (firstSelectedNote == null) {
                return;
            }
            var rangeStart = Part.notes.IndexOf(firstSelectedNote);
            var lastSelectedNote = Part.notes.LastOrDefault(x => Selection.Contains(x));
            if (lastSelectedNote == null) {
                return;
            }
            var rangeEndInclusive = Part.notes.IndexOf(lastSelectedNote);
            int rangeToAddStart;
            int rangeToAddEndInclusive;
            if (thisIndex < rangeStart) {
                rangeToAddStart = thisIndex;
                rangeToAddEndInclusive = rangeEndInclusive;
            } else if (thisIndex > rangeEndInclusive) {
                rangeToAddStart = rangeStart;
                rangeToAddEndInclusive = thisIndex;
            } else {
                rangeToAddStart = rangeStart;
                rangeToAddEndInclusive = rangeEndInclusive;
            }
            var notesToAdd = Part.notes.ToList().GetRange(rangeToAddStart, rangeToAddEndInclusive - rangeToAddStart + 1);
            var changed = Selection.Add(notesToAdd);
            if (changed) {
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
            }
        }

        public void TempSelectNotes(int x0, int x1, int y0, int y1) {
            if (Part == null) {
                return;
            }
            var tempNotes = Part.notes
                .Where(note => note.End > x0 && note.position < x1 && note.tone > y0 && note.tone <= y1)
                .ToList();

            Selection.SetTemporarySelection(tempNotes);
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void CommitTempSelectNotes() {
            Selection.CommitTemporarySelection();
            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
        }

        public void CleanupSelectedNotes() {
            if (Part == null) {
                return;
            }
            var toCleanup = Selection.Except(Part.notes).ToList();
            Selection.Remove(toCleanup);
        }

        public void InsertNote() {
            if (Part == null) {
                return;
            }

            var project = DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / SnapDiv;

            var fromNote = Selection.LastOrDefault();
            int DEFAULT_TONE = 12 * 5; // C4
            int tone = fromNote?.tone ?? DEFAULT_TONE;
            int tick = fromNote?.RightBound ?? (int)TickOffset;
            int dur = fromNote?.duration ?? snapUnit;
            DocManager.Inst.StartUndoGroup("command.note.add");
            UNote note = DocManager.Inst.Project.CreateNote(tone, tick, dur);
            DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, note));
            SelectNote(note);
            DocManager.Inst.EndUndoGroup();
        }

        public void TransposeSelection(int deltaNoteNum) {
            if (Part == null || Selection.IsEmpty) {
                return;
            }
            var selectedNotes = Selection.ToList();
            if (selectedNotes.Any(note => note.tone + deltaNoteNum <= 0 || note.tone + deltaNoteNum >= ViewConstants.MaxTone)) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.note.move");
            DocManager.Inst.ExecuteCmd(new MoveNoteCommand(Part, selectedNotes, 0, deltaNoteNum));
            DocManager.Inst.EndUndoGroup();
        }
        public void MoveSelectedNotes(int deltaTicks) {
            if (Part == null || Selection.IsEmpty) {
                return;
            }
            var selectedNotes = Selection.ToList();
            // TODO REVIEW should the end be clamped to end of part? or allow to go over?
            //var delta = Math.Clamp(deltaTicks, -1 * selectedNotes.First().position, Part.End - selectedNotes.Last().position);
            var delta = Math.Max(deltaTicks, -1 * selectedNotes.First().position);

            DocManager.Inst.StartUndoGroup("command.note.move");
            DocManager.Inst.ExecuteCmd(new MoveNoteCommand(Part, selectedNotes, delta, 0));
            DocManager.Inst.EndUndoGroup();
        }

        public void ResizeSelectedNotes(int deltaTicks) {
            if (Part == null || Selection.IsEmpty) {
                return;
            }

            var selectedNotes = Selection.ToList();

            // ignore if change would make a note smaller than minimal size
            if (deltaTicks < 0) {
                int smallestDuration = selectedNotes.Select(n => n.duration).Min();

                var project = DocManager.Inst.Project;
                int snapUnit = project.resolution * 4 / SnapDiv;
                int minNoteTicks = IsSnapOn ? snapUnit : 15;

                if (smallestDuration + deltaTicks < minNoteTicks) {
                    return;
                }
            }
            DocManager.Inst.StartUndoGroup("command.note.edit");
            DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(Part, selectedNotes, deltaTicks));
            DocManager.Inst.EndUndoGroup();
        }

        public void MergeSelectedNotes() {
            if (Part == null || Selection.IsEmpty || Selection.Count <= 1) {
                return;
            }
            var notes = Selection.ToList();
            notes.Sort((a, b) => a.position.CompareTo(b.position));
            //Ignore slur lyrics
            var mergedLyrics = String.Join("", notes.Select(x => x.lyric).Where(l => !l.StartsWith("+")));
            if(mergedLyrics == ""){ //If all notes are slur, the merged note is single slur note
                mergedLyrics = notes[0].lyric;
            }
            DocManager.Inst.StartUndoGroup("command.note.edit");
            DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(Part, notes[0], mergedLyrics));
            DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(Part, notes[0], notes.Last().End - notes[0].End));
            notes.RemoveAt(0);
            DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(Part, notes));
            DocManager.Inst.EndUndoGroup();
        }

        internal void DeleteSelectedNotes() {
            if (Part == null || Selection.IsEmpty) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.note.delete");
            DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(Part, Selection.ToList()));
            DocManager.Inst.EndUndoGroup();
        }

        public void CopyNotes() {
            if (Part != null && !Selection.IsEmpty) {
                var selectedNotes = Selection.ToList();
                DocManager.Inst.NotesClipboard = selectedNotes.Select(note => note.Clone()).ToList();
            }
        }

        public void CutNotes() {
            if (Part != null && !Selection.IsEmpty) {
                var selectedNotes = Selection.ToList();
                DocManager.Inst.NotesClipboard = selectedNotes.Select(note => note.Clone()).ToList();
                DocManager.Inst.StartUndoGroup("command.note.delete");
                DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(Part, selectedNotes));
                DocManager.Inst.EndUndoGroup();
            }
        }

        public void PasteNotes() {
            if (Part != null && DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                int snapUnit = DocManager.Inst.Project.resolution * 4 / SnapDiv;
                int left = (DocManager.Inst.playPosTick / snapUnit) * snapUnit;
                int minPosition = DocManager.Inst.NotesClipboard.Select(note => note.position).Min();
                //If PlayPos is before the beginning of the part, don't paste.
                if (left < Part.position) {
                    return;
                }
                int offset = left - minPosition - Part.position;
                var notes = DocManager.Inst.NotesClipboard.Select(note => note.Clone()).ToList();
                notes.ForEach(note => note.position += offset);
                DocManager.Inst.StartUndoGroup("command.note.paste");
                DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, notes));
                int minDurTick = Part.GetMinDurTick(Project);
                if (Part.Duration < minDurTick) {
                    DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(Project, Part, minDurTick - Part.Duration, false));
                }
                DocManager.Inst.EndUndoGroup();
                Selection.Select(notes);
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));

                var note = notes.First();
                if (left < TickOffset || TickOffset + ViewportTicks < note.position + note.duration + Part.position) {
                    TickOffset = Math.Clamp(note.position + note.duration * 0.5 - ViewportTicks * 0.5, 0, HScrollBarMax);
                }
            }
        }

        /// <summary>
        /// Paste notes but only keep tone, relative position, duration and lyric.
        /// </summary>
        public void PastePlainNotes() {
            UNote toPlainNote(UNote note) {
                var plainNote = DocManager.Inst.Project.CreateNote(
                    note.tone,
                    note.position,
                    note.duration);
                plainNote.lyric = note.lyric;
                return plainNote;
            }

            if (Part != null && DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                int snapUnit = DocManager.Inst.Project.resolution * 4 / SnapDiv;
                int left = (DocManager.Inst.playPosTick / snapUnit) * snapUnit;
                int minPosition = DocManager.Inst.NotesClipboard.Select(note => note.position).Min();
                //If PlayPos is before the beginning of the part, don't paste.
                if (left < Part.position) {
                    return;
                }
                int offset = left - minPosition - Part.position;
                var notes = DocManager.Inst.NotesClipboard.Select(note => toPlainNote(note)).ToList();
                notes.ForEach(note => note.position += offset);
                DocManager.Inst.StartUndoGroup("command.note.paste");
                DocManager.Inst.ExecuteCmd(new AddNoteCommand(Part, notes));
                int minDurTick = Part.GetMinDurTick(Project);
                if (Part.Duration < minDurTick) {
                    DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(Project, Part, minDurTick - Part.Duration, false));
                }
                DocManager.Inst.EndUndoGroup();
                Selection.Select(notes);
                MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));

                var note = notes.First();
                if (left < TickOffset || TickOffset + ViewportTicks < note.position + note.duration + Part.position) {
                    TickOffset = Math.Clamp(note.position + note.duration * 0.5 - ViewportTicks * 0.5, 0, HScrollBarMax);
                }
            }
        }

        public async void PasteSelectedParams(Window window) {
            if (Part != null && DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                var selectedNotes = Selection.ToList();
                if(selectedNotes.Count == 0) {
                    return;
                }

                var dialog = new PasteParamDialog();
                var vm = new PasteParamViewModel();
                dialog.DataContext = vm;
                await dialog.ShowDialog(window);

                if (dialog.Apply) {
                    DocManager.Inst.StartUndoGroup("command.parameter.paste");

                    int c = 0;
                    var track = Project.tracks[Part.trackNo];
                    foreach (var note in selectedNotes) {
                        var copyNote = DocManager.Inst.NotesClipboard[c];

                        for (int i = 0; i < vm.Params.Count; i++) {
                            switch (i) {
                                case 0:
                                    if (vm.Params[i].IsSelected) {
                                        DocManager.Inst.ExecuteCmd(new SetPitchPointsCommand(Part, note, copyNote.pitch));
                                    }
                                    break;
                                case 1:
                                    if (vm.Params[i].IsSelected) {
                                        DocManager.Inst.ExecuteCmd(new SetVibratoCommand(Part, note, copyNote.vibrato));
                                    }
                                    break;
                                default:
                                    if (vm.Params[i].IsSelected) {
                                        float?[] values = copyNote.GetExpressionNoteHas(Project, track, vm.Params[i].Abbr);
                                        DocManager.Inst.ExecuteCmd(new SetNoteExpressionCommand(Project, track, Part, note, vm.Params[i].Abbr, values));
                                    }
                                    break;
                            }
                        }

                        c++;
                        if (c >= DocManager.Inst.NotesClipboard.Count) {
                            c = 0;
                        }
                    }
                    DocManager.Inst.EndUndoGroup();
                }
            }
        }

        public void ToggleVibrato(UNote note) {
            if (Part == null) {
                return;
            }
            var vibrato = note.vibrato;
            DocManager.Inst.StartUndoGroup("command.vibrato.edit");
            DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(Part, note, vibrato.length == 0 ? NotePresets.Default.DefaultVibrato.VibratoLength : 0));
            DocManager.Inst.EndUndoGroup();
        }

        public void ClearPhraseCache() {
            if (Part != null && !Selection.IsEmpty) {
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.clearingcache")));
                var selectedNotes = Selection.ToList();
                var phrases = Part.renderPhrases.Where(phrase => selectedNotes.Any(note => phrase.notes.Any(rnote => rnote.position == Part.position + note.position - phrase.position && rnote.duration == note.duration)));
                phrases.ForEach(phrase => phrase.DeleteCacheFiles());
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("progress.cachecleared")));
            }
        }

        private void SetPlayPos(int tick, bool waitingRendering) {
            PlayPosWaitingRendering = waitingRendering;
            if (waitingRendering) {
                return;
            }
            tick -= Part?.position ?? 0;
            PlayPosX = TickToneToPoint(tick, 0).X;
            TickToLineTick(tick, out int left, out int right);
            PlayPosHighlightX = TickToneToPoint(left, 0).X;
            PlayPosHighlightWidth = (right - left) * TickWidth;
        }

        private void FocusNote(UNote note) {
            TickOffset = Math.Clamp(note.position + note.duration * 0.5 - ViewportTicks * 0.5, 0, HScrollBarMax);
            TrackOffset = Math.Clamp(ViewConstants.MaxTone - note.tone + 2 - ViewportTracks * 0.5, 0, VScrollBarMax);
        }

        private void ScrollIntoView(UNote note) {
            if (note.position < TickOffset || note.RightBound > TickOffset + ViewportTicks) {
                AutoScroll(TickToneToPoint(note.position, 0).X);
            }
            var toneMargin = 4;
            var noteOffset = ViewConstants.MaxTone - note.tone - 1;
            if (noteOffset < TrackOffset + toneMargin) {
                TrackOffset = Math.Max(noteOffset - toneMargin, 0);
            } else if (noteOffset > TrackOffset + ViewportTracks - toneMargin) {
                TrackOffset = Math.Min(noteOffset + toneMargin - ViewportTracks, VScrollBarMax);
            }
        }

        internal (UNote[], UNote[]) PrepareInsertLyrics() {
            var first = Selection.FirstOrDefault();
            if (Part == null) {
                return (Array.Empty<UNote>(), Array.Empty<UNote>());
            }
            //If no note is selected, InsertLyrics will apply to all notes in the part.
            if (first == null) {
                return (Part.notes.ToArray(), Array.Empty<UNote>());
            }
            List<UNote> notes = new List<UNote>();
            var note = first;
            while (note.Next != null) {
                notes.Add(note);
                note = note.Next;
            }
            notes.Add(note);
            return (notes.ToArray(), Selection.ToArray());
        }

        bool IsExpSupported(string expKey) {
            if (Project == null || Part == null || Project.tracks.Count <= Part.trackNo) {
                return true;
            }
            var track = Project.tracks[Part.trackNo];
            if (track.RendererSettings.Renderer == null) {
                return true;
            }
            if (track.TryGetExpDescriptor(Project, expKey, out var descriptor)) {
                return track.RendererSettings.Renderer.SupportsExpression(descriptor);
            }
            if (expKey == track.VoiceColorExp.abbr) {
                return track.RendererSettings.Renderer.SupportsExpression(track.VoiceColorExp);
            }
            return true;
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is UNotification notif) {
                if (cmd is LoadPartNotification loadPart) {
                    LoadPart(loadPart.part, loadPart.project);
                    double tickOffset = loadPart.tick - loadPart.part.position - Bounds.Width / TickWidth / 2;
                    TickOffset = Math.Clamp(tickOffset, 0, HScrollBarMax);
                    PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
                } else if (cmd is LoadProjectNotification) {
                    UnloadPart();
                    LoadPortrait(null, null);
                    PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
                } else if (cmd is SelectExpressionNotification selectExp) {
                    SecondaryKey = PrimaryKey;
                    PrimaryKey = selectExp.ExpKey;
                    PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
                } else if (cmd is SetPlayPosTickNotification setPlayPosTick) {
                    SetPlayPos(setPlayPosTick.playPosTick, setPlayPosTick.waitingRendering);
                    if (!setPlayPosTick.pause || Preferences.Default.LockStartTime == 1) {
                        MaybeAutoScroll(PlayPosX);
                    }
                } else if (cmd is FocusNoteNotification focusNote) {
                    if (focusNote.part == Part) {
                        FocusNote(focusNote.note);
                        if (Selection.Count <= 1) {
                            SelectNote(focusNote.note);
                        }
                    }
                } else if (cmd is ValidateProjectNotification || cmd is SingersRefreshedNotification) {
                    if (Part != null) {
                        LoadPortrait(Part, Project);
                    }
                    OnPartModified();
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());
                } else if (cmd is PhonemizedNotification) {
                    OnPartModified();
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());
                } else if (notif is PartRenderedNotification && notif.part == Part) {
                    MessageBus.Current.SendMessage(new WaveformRefreshEvent());
                }
            } else if (cmd is PartCommand partCommand) {
                if (cmd is ReplacePartCommand replacePart) {
                    if (!isUndo) {
                        LoadPart(replacePart.newPart, replacePart.project);
                    } else {
                        LoadPart(replacePart.part, replacePart.project);
                    }
                }
                if (partCommand.part != Part) {
                    return;
                }
                if (cmd is RemovePartCommand) {
                    if (!isUndo) {
                        UnloadPart();
                    }
                } else if (cmd is AddPartCommand addPart) {
                    if (isUndo && addPart.part == Part) {
                        UnloadPart();
                    }
                } else if (cmd is ResizeVoicePartCommand) {
                    OnPartModified();
                } else if (cmd is MovePartCommand) {
                    OnPartModified();
                } else if (cmd is RenamePartCommand) {
                    LoadWindowTitle(Part, Project);
                }
            } else if (cmd is NoteCommand noteCommand) {
                CleanupSelectedNotes();
                if (noteCommand.Part == Part) {
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());

                    if (noteCommand is RemoveNoteCommand && isUndo) {
                        if (Selection.Select(noteCommand.Notes)) {
                            MessageBus.Current.SendMessage(new NotesSelectionEvent(Selection));
                        }
                    }
                }
            } else if (cmd is ExpCommand) {
                MessageBus.Current.SendMessage(new NotesRefreshEvent());
            } else if (cmd is TrackCommand) {
                if (cmd is RenameTrackCommand) {
                    LoadWindowTitle(Part, Project);
                    return;
                } else if (cmd is ChangeTrackColorCommand) {
                    LoadTrackColor(Part, Project);
                    return;
                } else if (cmd is RemoveTrackCommand removeTrack) {
                    if (Part != null && removeTrack.removedParts.Contains(Part)) {
                        UnloadPart();
                    }
                }
                MessageBus.Current.SendMessage(new NotesRefreshEvent());
                if (cmd is TrackChangeSingerCommand trackChangeSinger) {
                    if (Part != null && trackChangeSinger.track.TrackNo == Part.trackNo) {
                        LoadPortrait(Part, Project);
                    }
                }
                PrimaryKeyNotSupported = !IsExpSupported(PrimaryKey);
            }
        }

        private void MaybeAutoScroll(double positionX) {
            var autoScrollPreference = Convert.ToBoolean(Preferences.Default.PlaybackAutoScroll);
            if (autoScrollPreference) {
                AutoScroll(positionX);
            }
        }

        private void AutoScroll(double positionX) {
            double scrollDelta = GetScrollValueDelta(positionX);
            TickOffset = Math.Clamp(TickOffset + scrollDelta, 0, HScrollBarMax);
        }

        private double GetScrollValueDelta(double positionX) {
            var pageScroll = Preferences.Default.PlaybackAutoScroll == 2;
            if (pageScroll) {
                return GetPageScrollScrollValueDelta(positionX);
            }
            return GetStationaryCursorScrollValueDelta(positionX);
        }

        private double GetStationaryCursorScrollValueDelta(double positionX) {
            double rightMargin = Preferences.Default.PlayPosMarkerMargin * Bounds.Width;
            if (positionX > rightMargin) {
                return (positionX - rightMargin) * playPosXToTickOffset;
            } else if (positionX < 0) {
                return positionX * playPosXToTickOffset;
            }
            return 0;
        }

        private double GetPageScrollScrollValueDelta(double positionX) {
            double leftMargin = (1 - Preferences.Default.PlayPosMarkerMargin) * Bounds.Width;
            if (positionX > Bounds.Width) {
                return (Bounds.Width - leftMargin) * playPosXToTickOffset;
            } else if (positionX < 0) {
                return (positionX - leftMargin) * playPosXToTickOffset;
            }
            return 0;
        }
    }
}
