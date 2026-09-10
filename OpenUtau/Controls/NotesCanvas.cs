using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class NotesCanvas : Control {
        public static readonly DirectProperty<NotesCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<NotesCanvas, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<NotesCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<NotesCanvas, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<NotesCanvas, UVoicePart?> PartProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, UVoicePart?>(
                nameof(Part),
                o => o.Part,
                (o, v) => o.Part = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPitchProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPitch),
                o => o.ShowPitch,
                (o, v) => o.ShowPitch = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowFinalPitchProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowFinalPitch),
                o => o.ShowFinalPitch,
                (o, v) => o.ShowFinalPitch = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowVibratoProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowVibrato),
                o => o.ShowVibrato,
                (o, v) => o.ShowVibrato = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPhonemizerTagsProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPhonemizerTags),
                o => o.ShowPhonemizerTags,
                (o, v) => o.ShowPhonemizerTags = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPlaybackNoteHighlightProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPlaybackNoteHighlight),
                o => o.ShowPlaybackNoteHighlight,
                (o, v) => o.ShowPlaybackNoteHighlight = v);
        public static readonly DirectProperty<NotesCanvas, bool> ShowPlaybackNoteBounceProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, bool>(
                nameof(ShowPlaybackNoteBounce),
                o => o.ShowPlaybackNoteBounce,
                (o, v) => o.ShowPlaybackNoteBounce = v);
        public static readonly DirectProperty<NotesCanvas, int> PlayPosTickProperty =
            AvaloniaProperty.RegisterDirect<NotesCanvas, int>(
                nameof(PlayPosTick),
                o => o.PlayPosTick,
                (o, v) => o.PlayPosTick = v);

        public double TickWidth {
            get => tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TrackHeight {
            get => trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double TickOffset {
            get => tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref tickOffset, value);
        }
        public double TrackOffset {
            get => trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref trackOffset, value);
        }
        public UVoicePart? Part {
            get => part;
            set => SetAndRaise(PartProperty, ref part, value);
        }
        public bool ShowPitch {
            get => showPitch;
            private set => SetAndRaise(ShowPitchProperty, ref showPitch, value);
        }
        public bool ShowFinalPitch {
            get => showFinalPitch;
            private set => SetAndRaise(ShowFinalPitchProperty, ref showFinalPitch, value);
        }
        public bool ShowVibrato {
            get => showVibrato;
            private set => SetAndRaise(ShowVibratoProperty, ref showVibrato, value);
        }
        public bool ShowPhonemizerTags {
            get => showPhonemizerTags;
            private set => SetAndRaise(ShowPhonemizerTagsProperty, ref showPhonemizerTags, value);
        }
        public bool ShowPlaybackNoteHighlight {
            get => showPlaybackNoteHighlight;
            private set => SetAndRaise(ShowPlaybackNoteHighlightProperty, ref showPlaybackNoteHighlight, value);
        }
        public bool ShowPlaybackNoteBounce {
            get => showPlaybackNoteBounce;
            private set => SetAndRaise(ShowPlaybackNoteBounceProperty, ref showPlaybackNoteBounce, value);
        }
        public int PlayPosTick {
            get => playPosTick;
            private set => SetAndRaise(PlayPosTickProperty, ref playPosTick, value);
        }

        private double tickWidth;
        private double trackHeight;
        private double tickOffset;
        private double trackOffset;
        private UVoicePart? part;
        private bool showPitch = true;
        private bool showFinalPitch = true;
        private bool showVibrato = true;
        private bool showPhonemizerTags = true;
        private bool showPlaybackNoteHighlight;
        private bool showPlaybackNoteBounce;
        private int playPosTick = int.MinValue;

        private UNote? activePlaybackNote;
        private UNote? fadingPlaybackNote;
        private float activeHighlight;
        private float fadingHighlight;
        private float activeBounceElapsed;
        private DateTime highlightLastFrame = DateTime.UtcNow;
        private readonly DispatcherTimer highlightTimer;
        private readonly Dictionary<(Color from, Color to, byte amount), IBrush> highlightBrushes = new();
        private bool playbackSeekPending = true;
        private bool renderPassActive;
        private bool invalidatePending;

        private const double HoverGlowDuration = 0.12;
        private const float PlaybackHighlightFadeInPerSecond = 8.0f;
        private const float PlaybackHighlightFadeOutPerSecond = 6.2f;
        private const float PlaybackNoteBounceDuration = 0.25f;
        private const double PlaybackNoteBounceHeight = 12.0;
        private UNote? hoverNote;
        private UNote? fadingHoverNote;
        private float hoverGlow;
        private float hoverFadeGlow;
        private DateTime hoverLastFrame = DateTime.UtcNow;
        private readonly DispatcherTimer hoverTimer;
        private Point lastPointerPos;
        private readonly Dictionary<(Color color, byte alpha, int thickness), Pen> glowPens = new();

        private PolylineGeometry polylineGeometry = new PolylineGeometry();
        private Points points = new Points();

        private HashSet<UNote> selectedNotes = new HashSet<UNote>();
        private Geometry pointGeometry;

        private bool showGhostNotes = true;
        private List<UPart> otherPartsInView = new List<UPart>();

        public NotesCanvas() {
            ClipToBounds = true;
            pointGeometry = new EllipseGeometry(new Rect(-2.5, -2.5, 5, 5));

            highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 30.0) };
            highlightTimer.Tick += (_, _) => UpdatePlaybackHighlight(false);
            hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
            hoverTimer.Tick += (_, _) => UpdateHoverGlow();

            MessageBus.Current.Listen<NotesRefreshEvent>()
                .Subscribe(_ => InvalidateVisual());
            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    selectedNotes.Clear();
                    selectedNotes.UnionWith(e.selectedNotes);
                    selectedNotes.UnionWith(e.tempSelectedNotes);
                    InvalidateVisual();
                });
            MessageBus.Current.Listen<PartRefreshEvent>()
                .Subscribe(_ => RefreshGhostNotes());
            this.WhenAnyValue(x => x.Part)
                .OfType<UVoicePart>()
                .Subscribe(_ => {
                    RefreshGhostNotes();
                    hoverNote = null;
                    fadingHoverNote = null;
                    hoverGlow = 0;
                    hoverFadeGlow = 0;
                    hoverTimer.Stop();
                });
        }

        void RefreshGhostNotes() {
            showGhostNotes = Convert.ToBoolean(Preferences.Default.ShowGhostNotes);
            if (Part == null || !showGhostNotes) {
                return;
            }
            otherPartsInView = DocManager.Inst.Project.parts
                .Where(other => other.trackNo != Part.trackNo &&
                    other.position < Part.End &&
                    Part.position < other.End)
                .ToList();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == PlayPosTickProperty) {
                if (!ShowPlaybackNoteHighlight && !ShowPlaybackNoteBounce) {
                    return;
                }
                playbackSeekPending = true;
                UpdatePlaybackHighlight(true);
                return;
            }
            if (change.Property == ShowPlaybackNoteHighlightProperty ||
                change.Property == ShowPlaybackNoteBounceProperty) {
                playbackSeekPending = true;
                UpdatePlaybackHighlight(false);
                InvalidateVisual();
                return;
            }
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            lastPointerPos = e.GetPosition(this);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                SetHoveredNote(null);
            } else {
                UpdateHoveredNote();
            }
        }

        protected override void OnPointerExited(PointerEventArgs e) {
            base.OnPointerExited(e);
            SetHoveredNote(null);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            SetHoveredNote(null);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            UpdateHoveredNote();
        }

        void UpdateHoveredNote() {
            if (!Preferences.Default.NoteHoverGlow || Part == null) {
                SetHoveredNote(null);
                return;
            }
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            if (viewModel == null) {
                SetHoveredNote(null);
                return;
            }
            SetHoveredNote(viewModel.SelectableNote);
        }

        void SetHoveredNote(UNote? note) {
            if (!Preferences.Default.NoteHoverGlow) {
                note = null;
            }
            if (ReferenceEquals(note, hoverNote)) {
                return;
            }
            if (hoverNote != null && hoverGlow > 0.001f) {
                fadingHoverNote = hoverNote;
                hoverFadeGlow = hoverGlow;
            } else if (hoverNote != null) {
                fadingHoverNote = null;
                hoverFadeGlow = 0;
            }
            hoverNote = note;
            hoverGlow = 0;
            hoverLastFrame = DateTime.UtcNow;
            hoverTimer.Start();
        }

        void UpdateHoverGlow() {
            var now = DateTime.UtcNow;
            float dt = (float)Math.Clamp((now - hoverLastFrame).TotalSeconds, 0, 0.1);
            hoverLastFrame = now;
            float step = dt / (float)HoverGlowDuration;
            bool changed = false;
            float newActive = MoveTowards(hoverGlow, hoverNote == null ? 0f : 1f, step);
            if (newActive != hoverGlow) {
                hoverGlow = newActive;
                changed = true;
            }
            float newFade = MoveTowards(hoverFadeGlow, 0f, step);
            if (newFade != hoverFadeGlow) {
                hoverFadeGlow = newFade;
                changed = true;
            }
            if (hoverFadeGlow <= 0.001f) {
                fadingHoverNote = null;
                hoverFadeGlow = 0;
            }
            bool settled = (hoverNote == null ? hoverGlow == 0f : hoverGlow == 1f) && fadingHoverNote == null;
            if (!changed && settled) {
                hoverTimer.Stop();
                return;
            }
            InvalidateVisual();
        }

        float GetHoverGlow(UNote note) {
            if (note == hoverNote) {
                return hoverGlow;
            }
            if (note == fadingHoverNote) {
                return hoverFadeGlow;
            }
            return 0f;
        }

        Pen GetGlowPen(Color color, byte alpha, int thickness) {
            var key = (color, alpha, thickness);
            if (!glowPens.TryGetValue(key, out var pen)) {
                pen = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), thickness) {
                    LineJoin = PenLineJoin.Round,
                };
                glowPens[key] = pen;
            }
            return pen;
        }

        void DrawHoverGlow(DrawingContext context, Point leftTop, Size size, double radius, IBrush brush, float glow) {
            if (glow <= 0.01f || !(brush is ISolidColorBrush solid)) {
                return;
            }
            byte alpha = (byte)Math.Clamp((int)Math.Round(glow * 100), 0, 255);
            context.DrawRectangle(null, GetGlowPen(solid.Color, alpha, 2),
                Inflate(leftTop, size, 1), radius + 1, radius + 1);
            context.DrawRectangle(null, GetGlowPen(solid.Color, (byte)(alpha * 2 / 5), 3),
                Inflate(leftTop, size, 2.5), radius + 2.5, radius + 2.5);
        }

        static Rect Inflate(Point leftTop, Size size, double d) =>
            new Rect(leftTop.X - d, leftTop.Y - d, size.Width + d * 2, size.Height + d * 2);

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (Part == null) {
                return;
            }
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            if (viewModel == null) {
                return;
            }
            renderPassActive = true;
            try {
                DrawBackgroundForHitTest(context);
                double leftTick = TickOffset - 480;
                double rightTick = TickOffset + Bounds.Width / TickWidth + 480;
                bool hidePitch = viewModel.TickWidth <= ViewConstants.PianoRollTickWidthShowDetails * 0.5;
                bool seek = playbackSeekPending;
                playbackSeekPending = false;
                UpdatePlaybackHighlight(seek);

                if (showGhostNotes) {
                foreach (UPart otherPart in otherPartsInView) {
                    if (otherPart is UVoicePart otherVoicePart) {
                        var xOffset = otherVoicePart.position - Part.position;
                        var brush = ThemeManager.NeutralAccentBrushSemi;
                        if (otherVoicePart.trackNo >= 0) {
                            var track = DocManager.Inst.Project.tracks[otherVoicePart.trackNo];
                            brush = ThemeManager.GetTrackColor(track.TrackColor).AccentColorLightSemi;
                        }

                            foreach (var note in otherVoicePart.notes) {
                                if (note.LeftBound + xOffset >= rightTick || note.RightBound + xOffset <= leftTick) {
                                    continue;
                                }
                                RenderGhostNote(note, viewModel, context, xOffset, brush);
                            }
                        }
                    }
                }

                foreach (var note in Part.notes) {
                    if (note.LeftBound >= rightTick || note.RightBound <= leftTick) {
                        continue;
                    }
                    RenderNoteBody(note, viewModel, context);
                }
                if (ShowFinalPitch && !hidePitch) {
                    RenderFinalPitch(leftTick, rightTick, viewModel, context);
                }
                foreach (var note in Part.notes) {
                    if (note.LeftBound >= rightTick || note.RightBound <= leftTick) {
                        continue;
                    }
                    if (ShowPitch && !hidePitch) {
                        RenderPitchBend(note, viewModel, context);
                    }
                    if ((ShowPitch || ShowVibrato) && !hidePitch) {
                        RenderVibrato(note, viewModel, context);
                    }
                    if (ShowVibrato && !note.Error && !hidePitch) {
                        RenderVibratoToggle(note, viewModel, context);
                        RenderVibratoControl(note, viewModel, context);
                    }
                }
            } finally {
                renderPassActive = false;
            }
        }

        private void DrawBackgroundForHitTest(DrawingContext context) {
            context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        }

        private void UpdatePlaybackHighlight(bool seek) {
            var now = DateTime.UtcNow;
            float dt = (float)Math.Clamp((now - highlightLastFrame).TotalSeconds, 0, 0.1);
            highlightLastFrame = now;
            var target = ((!ShowPlaybackNoteHighlight && !ShowPlaybackNoteBounce) || !PlaybackManager.Inst.PlayingMaster)
                ? null
                : seek || activePlaybackNote == null ? FindPlaybackNote() : activePlaybackNote;
            bool changed = false;
            if (target != activePlaybackNote) {
                if (activePlaybackNote != null && activeHighlight > 0.001f) {
                    fadingPlaybackNote = activePlaybackNote;
                    fadingHighlight = activeHighlight;
                }
                activePlaybackNote = target;
                activeHighlight = 0;
                activeBounceElapsed = 0;
                changed = true;
            }
            float newActive = MoveTowards(activeHighlight, !ShowPlaybackNoteHighlight || activePlaybackNote == null ? 0 : 1,
                PlaybackHighlightFadeInPerSecond * dt);
            if (newActive != activeHighlight) {
                activeHighlight = newActive;
                changed = true;
            }
            float newFading = MoveTowards(fadingHighlight, 0,
                PlaybackHighlightFadeOutPerSecond * dt);
            if (newFading != fadingHighlight) {
                fadingHighlight = newFading;
                changed = true;
            }
            if (fadingHighlight <= 0.001f) {
                fadingPlaybackNote = null;
                fadingHighlight = 0;
            }
            bool bouncing = ShowPlaybackNoteBounce && activePlaybackNote != null &&
                activeBounceElapsed < PlaybackNoteBounceDuration;
            if (bouncing) {
                activeBounceElapsed += dt;
                changed = true;
            }
            bool needed = activeHighlight > 0.001f || fadingHighlight > 0.001f || bouncing;
            if (needed) {
                if (!highlightTimer.IsEnabled) {
                    highlightTimer.Start();
                }
            } else if (highlightTimer.IsEnabled) {
                highlightTimer.Stop();
            }
            if (changed) {
                if (renderPassActive) {
                    if (!invalidatePending) {
                        invalidatePending = true;
                        Dispatcher.UIThread.Post(() => {
                            invalidatePending = false;
                            InvalidateVisual();
                        }, DispatcherPriority.Background);
                    }
                } else {
                    InvalidateVisual();
                }
            }
        }

        private UNote? FindPlaybackNote() {
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            return viewModel?.FindVoiceNoteAtTick(PlayPosTick);
        }

        private Vector GetPlaybackBounceOffset(UNote note) {
            if (!ShowPlaybackNoteBounce || note != activePlaybackNote || !PlaybackManager.Inst.PlayingMaster) {
                return default;
            }
            double progress = Math.Clamp(activeBounceElapsed / PlaybackNoteBounceDuration, 0, 1);
            double height = Math.Min(PlaybackNoteBounceHeight, TrackHeight * 0.4);
            return new Vector(0, -Math.Sin(progress * Math.PI) * height);
        }

        private IBrush BlendBrush(IBrush from, IBrush to, float amount) {
            if (amount <= 0.001f || from is not ISolidColorBrush fromSolid || to is not ISolidColorBrush toSolid) return from;
            byte quantizedAmount = (byte)Math.Clamp((int)Math.Round(amount * 255), 0, 255);
            var key = (fromSolid.Color, toSolid.Color, quantizedAmount);
            if (!highlightBrushes.TryGetValue(key, out var brush)) {
                float t = quantizedAmount / 255f;
                var a = fromSolid.Color;
                var b = toSolid.Color;
                brush = new SolidColorBrush(Color.FromArgb(
                    (byte)(a.A + (b.A - a.A) * t),
                    (byte)(a.R + (b.R - a.R) * t),
                    (byte)(a.G + (b.G - a.G) * t),
                    (byte)(a.B + (b.B - a.B) * t)));
                highlightBrushes[key] = brush;
            }
            return brush;
        }

        private static float MoveTowards(float value, float target, float delta) =>
            Math.Abs(target - value) <= delta ? target : value + Math.Sign(target - value) * delta;

        private void RenderNoteBody(UNote note, NotesViewModel viewModel, DrawingContext context) {
            Point leftTop = viewModel.TickToneToPoint(note.position, note.AdjustedTone);
            leftTop = leftTop.WithX(leftTop.X + 1).WithY(Math.Round(leftTop.Y + 1));
            Size size = viewModel.TickToneToSize(note.duration, 1);
            size = size.WithWidth(size.Width - 1).WithHeight(Math.Floor(size.Height - 2));
            leftTop += GetPlaybackBounceOffset(note);
            Point rightBottom = new Point(leftTop.X + size.Width, leftTop.Y + size.Height);
            bool hasError = note.Error;

            // Check for Phoneme Errors (mimicking PhonemeCanvas behavior)
            if (!hasError && Part != null && Part.phonemes != null) {
                int phonemeCount = 0;
                foreach (var p in Part.phonemes) {
                    if (p.Parent == note) {
                        phonemeCount++;
                        // If any attached phoneme has an error, the whole note is flagged
                        if (p.Error) {
                            hasError = true;
                            break;
                        }
                    }
                }
                // Edge Case: If the note is not a continuation/rest but generated 0 phonemes, 
                // it means the phonemizer completely failed to process the lyric.
                if (!hasError && phonemeCount == 0 && !note.lyric.StartsWith("+") && !note.lyric.StartsWith("-")) {
                    hasError = true;
                }
            }
            // apply the transparent/greyed-out brush if an error was found
            var brush = selectedNotes.Contains(note)
                ? (hasError ? ThemeManager.AccentBrush3Semi : ThemeManager.AccentBrush2)
                : (hasError ? ThemeManager.NeutralAccentBrushSemi : ThemeManager.AccentBrush1);
            if (!selectedNotes.Contains(note)) {
                float highlight = ShowPlaybackNoteHighlight
                    ? (note == activePlaybackNote ? activeHighlight : note == fadingPlaybackNote ? fadingHighlight : 0)
                    : 0;
                if (highlight > 0.001f) {
                    brush = BlendBrush(brush, hasError ? ThemeManager.AccentBrush3Semi : ThemeManager.AccentBrush2, highlight);
                }
            }
            context.DrawRectangle(brush, null, new Rect(leftTop, rightBottom), 2, 2);
            if (Preferences.Default.NoteHoverGlow) {
                DrawHoverGlow(context, leftTop, size, 2, brush, GetHoverGlow(note));
            }
            if (TrackHeight < 10 || note.lyric.Length == 0) {
                return;
            }
            // grey out the Phonemizer Transition Badges
            if (ShowPhonemizerTags && TrackHeight >= 20) {
                string currentOver = note.PhonemizerOverride ?? "";
                bool isCurrentDefault = string.IsNullOrEmpty(currentOver) || currentOver.Equals("Default", StringComparison.OrdinalIgnoreCase);
                string currentPh = isCurrentDefault ? "Default" : currentOver;
                string prevPh = "Default"; 
                if (note.Prev != null) {
                    string prevOver = note.Prev.PhonemizerOverride ?? "";
                    bool isPrevDefault = string.IsNullOrEmpty(prevOver) || prevOver.Equals("Default", StringComparison.OrdinalIgnoreCase);
                    prevPh = isPrevDefault ? "Default" : prevOver;
                }
                bool isContinuation = note.lyric.StartsWith("+");
                bool isTransition = !isContinuation && ((note.Prev == null && !isCurrentDefault) || (note.Prev != null && currentPh != prevPh));
                
                if (isTransition) {
                    // Badge Background utilizes the same hasError flag
                    var badgeBrush = selectedNotes.Contains(note)
                        ? (hasError ? ThemeManager.AccentBrush3Semi : ThemeManager.AccentBrush2)
                        : (hasError ? ThemeManager.NeutralAccentBrushSemi : ThemeManager.AccentBrush1);

                    if (isCurrentDefault) {
                        double boxWidth = 16; 
                        double boxHeight = 16;
                        double dotRadius = 3;
                        Avalonia.Rect boxRect = new Avalonia.Rect(
                            leftTop.X + 2, 
                            leftTop.Y - boxHeight - 4, 
                            boxWidth, 
                            boxHeight
                        );
                        Avalonia.Point center = new Avalonia.Point(
                            boxRect.X + boxWidth / 2, 
                            boxRect.Y + boxHeight / 2
                        );
                        context.DrawRectangle(badgeBrush, null, boxRect, 3, 3);
                        context.DrawEllipse(Brushes.White, null, center, dotRadius, dotRadius);
                        
                    } else {
                        var factory = OpenUtau.Api.PhonemizerFactory.Get(currentPh) ?? OpenUtau.Api.PhonemizerFactory.GetAll().FirstOrDefault(f => f.name == currentPh || (currentPh.Length > 0 && f.name.EndsWith(currentPh)));
                        string displayLang = factory?.language ?? "";
                        if (string.IsNullOrEmpty(displayLang) && !string.IsNullOrEmpty(factory?.tag)) {
                            displayLang = factory.tag.Split(' ')[0]; 
                        }
                        if (string.IsNullOrEmpty(displayLang)) {
                            string rawName = currentPh.Split('.').Last().Replace("Phonemizer", "");
                            displayLang = System.Text.RegularExpressions.Regex.Replace(rawName, "([A-Z])", " $1").Trim();
                            if (displayLang.Length > 5) {
                                displayLang = displayLang.Substring(0, 5);
                            }
                        }
                        if (!string.IsNullOrEmpty(displayLang)) {
                            var langLayout = TextLayoutCache.Get(displayLang, Avalonia.Media.Brushes.White, 10);
                            double paddingX = 3;
                            double paddingY = 1.5;
                            Avalonia.Rect badgeRect = new Avalonia.Rect(
                                leftTop.X + 2, 
                                leftTop.Y - langLayout.Height - (paddingY * 2) - 4, 
                                langLayout.Width + (paddingX * 2), 
                                langLayout.Height + (paddingY * 2)
                            );
                            context.DrawRectangle(badgeBrush, null, badgeRect, 3, 3);
                            Avalonia.Point textPos = new Avalonia.Point(badgeRect.X + paddingX, badgeRect.Y + paddingY);
                            using (var state = context.PushTransform(Avalonia.Matrix.CreateTranslation(textPos.X, textPos.Y))) {
                                langLayout?.Draw(context, new Avalonia.Point());
                            }
                        }
                    }
                }
            }
            string displayLyric = note.lyric;
            int txtsize = 12;
            var textLayout = TextLayoutCache.Get(displayLyric, Brushes.White, txtsize);
            if (txtsize > size.Height) {
                return;
            }
            if (textLayout.Height + 5 < size.Height) {
                txtsize = (int)(12 * (size.Height / textLayout.Height));
                textLayout = TextLayoutCache.Get(displayLyric, Brushes.White, txtsize);
            }
            if (textLayout.Width + 5 > size.Width) {
                displayLyric = displayLyric[0] + "..";
                textLayout = TextLayoutCache.Get(displayLyric, Brushes.White, txtsize);
                if (textLayout.Width + 5 > size.Width) {
                    return;
                }
            }
            Point textPosition = leftTop.WithX(leftTop.X + 5)
                .WithY(Math.Round(leftTop.Y + (size.Height - textLayout.Height) / 2));
            using (var state = context.PushTransform(Matrix.CreateTranslation(textPosition.X, textPosition.Y))) {
                textLayout.Draw(context, new Point());
            }
        }

        private void RenderGhostNote(UNote note, NotesViewModel viewModel, DrawingContext context, int partOffset, IBrush brush) {
            // REVIEW should ghost note be smaller?
            double relativeSize = 0.5d;
            double height = TrackHeight * relativeSize;
            double yOffset = Math.Floor(height * 0.5f);
            Point leftTop = viewModel.TickToneToPoint(partOffset + note.position, note.AdjustedTone);
            leftTop = leftTop.WithX(leftTop.X + 1).WithY(Math.Round(leftTop.Y + 1 + yOffset));

            Size size = viewModel.TickToneToSize(note.duration, relativeSize);
            size = size.WithWidth(size.Width - 1).WithHeight(Math.Floor(size.Height - 2));

            Point rightBottom = new Point(leftTop.X + size.Width, leftTop.Y + size.Height);

            context.DrawRectangle(brush, null, new Rect(leftTop, rightBottom), 2, 2);
        }

        private void RenderPitchBend(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var pitchExp = note.pitch;
            var pts = pitchExp.data;
            if (pts.Count < 2 || viewModel.Part == null) return;

            var project = viewModel.Project;
            double p0Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[0].X) - viewModel.Part.position;
            double p0Tone = note.AdjustedTone + pts[0].Y / 10.0;
            Point p0 = viewModel.TickToneToPoint(p0Tick, p0Tone - 0.5);
            Point p_1 = p0;
            var points = new Points();          
            points.Add(p0);

            var brush = note.pitch.snapFirst ? ThemeManager.AccentBrush3 : null;
            var pen = ThemeManager.AccentPen3;
            using (var state = context.PushTransform(Matrix.CreateTranslation(p0.X, p0.Y))) {
                context.DrawGeometry(brush, pen, pointGeometry);
            }

            for (int i = 1; i < pts.Count; i++) {
                double p1Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[i].X) - viewModel.Part.position;
                double p1Tone = note.AdjustedTone + pts[i].Y / 10.0;
                Point p1 = viewModel.TickToneToPoint(p1Tick, p1Tone - 0.5);
                CubicSplineSegment? curve = null;

                if (pts.Count > 2 && pts[i - 1].shape == PitchPointShape.sp) {
                    var p2 = p1;
                    if (i == 1) {
                        if (note.pitch.data[0].X > 0) {
                            p_1 = viewModel.TickToneToPoint(note.position, p0Tone - 0.5);
                        }
                    }
                    if (i < pts.Count - 1) {
                        double p2Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[i + 1].X) - viewModel.Part.position;
                        double p2Tone = note.AdjustedTone + pts[i + 1].Y / 10.0;
                        p2 = viewModel.TickToneToPoint(p2Tick, p2Tone - 0.5);
                    } else if (pts[i].X < note.DurationMs) {
                        p2 = viewModel.TickToneToPoint(note.End, note.AdjustedTone - 0.5);
                    }
                    curve = new CubicSplineSegment(
                                p_1.X, p_1.Y,
                                p0.X, p0.Y,
                                p1.X, p1.Y,
                                p2.X, p2.Y);
                }
                // Draw arc
                double x0 = p0.X;
                double y0 = p0.Y;
                double x1 = p0.X;
                double y1 = p0.Y;
                if (p1.X - p0.X < 5) {
                    points.Add(p1);
                } else {
                    points.Add(new Point(x0, y0));
                    while (x0 < p1.X) {
                        x1 = Math.Min(x1 + 4, p1.X);
                        y1 = curve?.GetY(x1) ?? MusicMath.InterpolateShape(p0.X, p1.X, p0.Y, p1.Y, x1, pts[i - 1].shape);
                        points.Add(new Point(x1, y1));
                        x0 = x1;
                        y0 = y1;
                    }
                }
                p_1 = p0;
                p0 = p1;
                using (var state = context.PushTransform(Matrix.CreateTranslation(p0.X, p0.Y))) {
                    context.DrawGeometry(null, pen, pointGeometry);
                }
            }
            var geometry = new PolylineGeometry(points, false);
            context.DrawGeometry(null, pen, geometry);
        }

        private void RenderVibrato(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var vibrato = note.vibrato;
            if (vibrato == null || vibrato.length == 0) {
                return;
            }

            var pen = ThemeManager.AccentPen3;
            float nPeriod = (float)viewModel.Project.timeAxis.TicksBetweenMsPos(note.PositionMs, note.PositionMs + vibrato.period) / note.duration;
            float nPos = vibrato.NormalizedStart;
            var point = vibrato.Evaluate(nPos, nPeriod, note);
            var points = new Points();
            points.Add(viewModel.TickToneToPoint(point.X, point.Y - 0.5));
            while (nPos < 1) {
                nPos = Math.Min(1, nPos + nPeriod / 16);
                point = vibrato.Evaluate(nPos, nPeriod, note);
                points.Add(viewModel.TickToneToPoint(point.X, point.Y - 0.5));
            }
            var geometry = new PolylineGeometry(points, false);
            context.DrawGeometry(null, pen, geometry);
        }

        private readonly Geometry vibratoIcon = Geometry.Parse("M-6.5 1 L-6 1.5 L-4.5 0 L-2 2.5 L0.5 0 L3 2.5 L6.5 -1 L6 -1.5 L4.5 0 L2 -2.5 L-0.5 0 L-3 -2.5 Z");
        private void RenderVibratoToggle(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var vibrato = note.vibrato;
            var togglePos = vibrato.GetToggle(note);
            Point icon = viewModel.TickToneToPoint(togglePos.X, togglePos.Y);
            var pen = ThemeManager.BarNumberPen;
            using (var state = context.PushTransform(Matrix.CreateTranslation(icon.X - 10, icon.Y))) {
                context.DrawGeometry(vibrato.length == 0 ? null : pen.Brush, pen, vibratoIcon);
            }
        }

        private void RenderVibratoControl(UNote note, NotesViewModel viewModel, DrawingContext context) {
            var vibrato = note.vibrato;
            if (vibrato.length == 0) {
                return;
            }
            var pen = ThemeManager.BarNumberPen!;
            Point start = viewModel.TickToneToPoint(vibrato.GetEnvelopeStart(note));
            Point fadeIn = viewModel.TickToneToPoint(vibrato.GetEnvelopeFadeIn(note));
            Point fadeOut = viewModel.TickToneToPoint(vibrato.GetEnvelopeFadeOut(note));
            Point end = viewModel.TickToneToPoint(vibrato.GetEnvelopeEnd(note));
            context.DrawLine(pen, start, fadeIn);
            context.DrawLine(pen, fadeIn, fadeOut);
            context.DrawLine(pen, fadeOut, end);
            using (var state = context.PushTransform(Matrix.CreateTranslation(start))) {
                context.DrawGeometry(pen.Brush, pen, pointGeometry);
            }
            using (var state = context.PushTransform(Matrix.CreateTranslation(fadeIn))) {
                context.DrawGeometry(pen.Brush, pen, pointGeometry);
            }
            using (var state = context.PushTransform(Matrix.CreateTranslation(fadeOut))) {
                context.DrawGeometry(pen.Brush, pen, pointGeometry);
            }
            vibrato.GetPeriodStartEnd(DocManager.Inst.Project, note, out var periodStartPos, out var periodEndPos);
            Point periodStart = viewModel.TickToneToPoint(periodStartPos);
            Point periodEnd = viewModel.TickToneToPoint(periodEndPos);
            float height = (float)TrackHeight / 3;
            periodStart = periodStart.WithY(periodStart.Y - height / 2 - 0.5f);
            double width = periodEnd.X - periodStart.X;
            periodEnd = periodEnd.WithX(periodEnd.X - 2).WithY(periodEnd.Y - height / 2 - 0.5f);
            context.DrawRectangle(null, pen, new Rect(periodStart, new Size(width, height)), 1, 1);
            context.DrawLine(pen, periodEnd, periodEnd + new Vector(0, height));
        }

        private void RenderFinalPitch(double leftTick, double rightTick, NotesViewModel viewModel, DrawingContext context) {
            var pen = ThemeManager.FinalPitchPen!;
            lock (Part!) {
                foreach (var phrase in Part!.renderPhrases) {
                    if (phrase.position - Part.position > rightTick || phrase.end - Part.position < leftTick) {
                        continue;
                    }
                    int pitchStart = phrase.position - phrase.leading - Part.position;
                    int startIdx = (int)Math.Max(0, (leftTick - pitchStart) / 5);
                    int endIdx = (int)Math.Min(phrase.pitches.Length, (rightTick - pitchStart) / 5 + 1);
                    var points = new Points();
                    for (int i = startIdx; i < endIdx; ++i) {
                        int t = pitchStart + i * 5;
                        float p = phrase.pitches[i];
                        points.Add(viewModel.TickToneToPoint(t, p / 100 - 0.5));
                    }
                    var geometry = new PolylineGeometry(points, false);
                    context.DrawGeometry(null, pen, geometry);
                }
            }
        }
    }
}
