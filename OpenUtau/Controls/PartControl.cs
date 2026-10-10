using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;
using Serilog;

namespace OpenUtau.App.Controls {
    class PartControl : Control, IDisposable, IProgress<int> {
        public static readonly DirectProperty<PartControl, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<PartControl, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<PartControl, double> ViewWidthProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(ViewWidth),
                o => o.ViewWidth,
                (o, v) => o.ViewWidth = v);
        public static readonly DirectProperty<PartControl, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<PartControl, Point> OffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, Point>(
                nameof(Offset),
                o => o.Offset,
                (o, v) => o.Offset = v);
        public static readonly DirectProperty<PartControl, string> TextProperty =
            AvaloniaProperty.RegisterDirect<PartControl, string>(
                nameof(Text),
                o => o.Text,
                (o, v) => o.Text = v);
        public static readonly DirectProperty<PartControl, bool> SelectedProperty =
            AvaloniaProperty.RegisterDirect<PartControl, bool>(
                nameof(Selected),
                o => o.Selected,
                (o, v) => o.Selected = v);
        public static readonly DirectProperty<PartControl, double> FadeInProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(FadeIn),
                o => o.FadeIn,
                (o, v) => o.FadeIn = v);
        public static readonly DirectProperty<PartControl, double> FadeOutProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(FadeOut),
                o => o.FadeOut,
                (o, v) => o.FadeOut = v);
        public static readonly DirectProperty<PartControl, double> PianoRollViewTickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(PianoRollViewTickOffset),
                o => o.PianoRollViewTickOffset,
                (o, v) => o.PianoRollViewTickOffset = v);
        public static readonly DirectProperty<PartControl, double> PianoRollViewViewportTicksProperty =
            AvaloniaProperty.RegisterDirect<PartControl, double>(
                nameof(PianoRollViewViewportTicks),
                o => o.PianoRollViewViewportTicks,
                (o, v) => o.PianoRollViewViewportTicks = v);

        // Tick width in pixel.
        public double TickWidth {
            get => tickWidth;
            set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TrackHeight {
            get => trackHeight;
            set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public double ViewWidth {
            get { return viewWidth; }
            set { SetAndRaise(ViewWidthProperty, ref viewWidth, value); }
        }
        public double TickOffset {
            get { return tickOffset; }
            set { SetAndRaise(TickOffsetProperty, ref tickOffset, value); }
        }
        public Point Offset {
            get { return offset; }
            set { SetAndRaise(OffsetProperty, ref offset, value); }
        }
        public string Text {
            get { return text; }
            set { SetAndRaise(TextProperty, ref text, value); }
        }
        public bool Selected {
            get { return selected; }
            set { SetAndRaise(SelectedProperty, ref selected, value); }
        }
        public double FadeIn {
            get { return fadeIn * TickWidth; }
            set { SetAndRaise(FadeInProperty, ref fadeIn, value); }
        }
        public double FadeOut {
            get { return Width - (fadeOut * TickWidth); }
            set { SetAndRaise(FadeOutProperty, ref fadeOut, value); }
        }
        public double PianoRollViewTickOffset {
            get => pianoRollViewTickOffset;
            set => SetAndRaise(PianoRollViewTickOffsetProperty, ref pianoRollViewTickOffset, value);
        }
        public double PianoRollViewViewportTicks {
            get => pianoRollViewViewportTicks;
            set => SetAndRaise(PianoRollViewViewportTicksProperty, ref pianoRollViewViewportTicks, value);
        }

        private bool _pianoRollViewportHovered;
        private bool PianoRollViewportHovered {
            get => _pianoRollViewportHovered;
            set {
                if (_pianoRollViewportHovered != value) {
                    _pianoRollViewportHovered = value;
                    InvalidateVisual();
                }
            }
        }

        private double tickWidth;
        private double trackHeight;
        private double viewWidth;
        private double tickOffset;
        private Point offset;
        private string text = string.Empty;
        private bool selected;
        private double fadeIn;
        private double fadeOut;
        private double pianoRollViewTickOffset;
        private double pianoRollViewViewportTicks;
        private Geometry pointGeometry;

        public readonly UPart part;
        private readonly PartsCanvas partsCanvas;
        private const byte ContentAlpha = 0xF8;
        private readonly Pen notePen = new Pen(new SolidColorBrush(Color.FromArgb(ContentAlpha, 255, 255, 255)), 3);
        private static readonly IBrush viewportFill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        private static readonly IPen viewportPen = new Pen(Brushes.White, 2);
        private const double GripDot = 2;
        private const double GripGap = 3;
        private readonly Pen fadePen = new Pen(Brushes.White);
        private List<IDisposable> unbinds = new List<IDisposable>();
        private static readonly IBrush waveformFill = new SolidColorBrush(Color.FromArgb(ContentAlpha, 255, 255, 255));
        private readonly WaveformEnvelope waveform = new WaveformEnvelope();

        public PartControl(UPart part, PartsCanvas canvas) {
            this.part = part;
            partsCanvas = canvas;
            pointGeometry = new EllipseGeometry(new Rect(0, 0, 6, 6));

            unbinds.Add(this.Bind(TickWidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty)));
            unbinds.Add(this.Bind(TrackHeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(WidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty).Select(tickWidth => tickWidth * part.Duration)));
            unbinds.Add(this.Bind(HeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(OffsetProperty, canvas.WhenAnyValue(x => x.TickOffset, x => x.TrackOffset,
                (tick, track) => new Point(-tick * TickWidth, -track * TrackHeight))));
            unbinds.Add(this.Bind(ViewWidthProperty, canvas.WhenAnyValue(x => x.Bounds).Select(bounds => bounds.Width)));
            unbinds.Add(this.Bind(TickOffsetProperty, canvas.WhenAnyValue(x => x.TickOffset).Select(tickOffset => tickOffset)));
            unbinds.Add(this.Bind(PianoRollViewTickOffsetProperty, canvas.GetObservable(PartsCanvas.PianoRollViewTickOffsetProperty)));
            unbinds.Add(this.Bind(PianoRollViewViewportTicksProperty, canvas.GetObservable(PartsCanvas.PianoRollViewViewportTicksProperty)));

            SetPosition();
            Refersh();

            if (part is UWavePart wavePart) {
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                wavePart.Peaks.ContinueWith((task) => {
                    if (task.IsFaulted) {
                        Log.Error(task.Exception, "Failed to build peaks");
                    } else {
                        InvalidateVisual();
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, scheduler);
            }

            this.PointerMoved += (o, e) => PointerChanged(e.GetPosition(this));
            this.PointerEntered += (o, e) => PointerChanged(e.GetPosition(this));
            this.PointerExited += (_, _) => PianoRollViewportHovered = false;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == OffsetProperty ||
                change.Property == TrackHeightProperty ||
                change.Property == TickWidthProperty) {
                SetPosition();
            }
            // The piano roll viewport only redraws the open part, which PartsCanvas
            // invalidates itself; redrawing every part here made scrolling the piano
            // roll redraw all parts, waveforms included, on every frame.
            if (change.Property == SelectedProperty ||
                change.Property == TextProperty || 
                change.Property == FadeInProperty ||
                change.Property == FadeOutProperty) {
                InvalidateVisual();
            }
        }

        public void SetPosition() {
            Canvas.SetLeft(this, Offset.X + part.position * tickWidth);
            Canvas.SetTop(this, Offset.Y + part.trackNo * trackHeight);
        }

        public void SetSize() {
            Width = TickWidth * part.Duration;
            Height = trackHeight;
        }

        public void Refersh() {
            Text = part.DisplayName;
            if (part is UWavePart wavePart) {
                FadeIn = wavePart.fadein;
                FadeOut = wavePart.fadeout;
            }
            InvalidateWaveform();
        }

        /// <summary>Redraws, rebuilding the waveform, as after a tempo change.</summary>
        public void InvalidateWaveform() {
            waveform.Invalidate();
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) {
            var backgroundBrush = Selected ? ThemeManager.AccentBrush2 : ThemeManager.AccentBrush1;
            // Background
            context.DrawRectangle(backgroundBrush, null, new Rect(1, 0, Width - 1, Height - 1), 4, 4);

            // Text
            var textLayout = TextLayoutCache.Get(Text, Brushes.White, 12);
            using (var state = context.PushTransform(Matrix.CreateTranslation(3, 2))) {
                context.DrawRectangle(backgroundBrush, null, new Rect(new Point(0, 0), new Size(textLayout.Width, textLayout.Height)));
                textLayout.Draw(context, new Point());
            }

            if (part == null) {
                return;
            }
            if (part is UVoicePart voicePart) {
                // Notes
                if (voicePart.notes.Count > 0) {
                    int maxTone = voicePart.notes.Max(note => note.tone);
                    int minTone = voicePart.notes.Min(note => note.tone);
                    if (maxTone - minTone < 52) {
                        int additional = (52 - (maxTone - minTone)) / 2;
                        minTone -= additional;
                        maxTone += additional;
                    }
                    using var pushedState = context.PushTransform(Matrix.CreateScale(1, trackHeight / (maxTone - minTone)));
                    foreach (var note in voicePart.notes) {
                        var start = new Point((int)(note.position * tickWidth), maxTone - note.tone);
                        var end = new Point((int)(note.End * tickWidth), maxTone - note.tone);
                        context.DrawLine(notePen, start, end);
                    }
                }
                // Highlight
                if (PianoRollViewportRect() is Rect vpRect) {
                    context.DrawRectangle(viewportFill, viewportPen, new RoundedRect(vpRect, new CornerRadius(3)));
                    if (PianoRollViewportHovered && GripRect(vpRect) is Rect grip) {
                        // A 3 by 3 grid of dots.
                        for (int column = 0; column < 3; ++column) {
                            for (int row = 0; row < 3; ++row) {
                                var center = new Point(
                                    grip.X + GripDot / 2 + column * (GripDot + GripGap),
                                    grip.Y + GripDot / 2 + row * (GripDot + GripGap));
                                context.DrawEllipse(Brushes.White, null, center, GripDot / 2, GripDot / 2);
                            }
                        }
                    }
                }
            } else if (part is UWavePart wavePart) {
                // Waveform
                try {
                    DrawWaveform(context, wavePart);
                } catch (Exception e) {
                    Log.Error(e, "failed to draw waveform");
                }
                // Fade
                var brush = Brushes.White;
                var pen = Selected ? ThemeManager.AccentPen2 : ThemeManager.AccentPen1;
                using (var state = context.PushTransform(Matrix.CreateTranslation(FadeIn, 0))) {
                    context.DrawGeometry(brush, pen, pointGeometry);
                }
                if (wavePart.fadein > 0) {
                    context.DrawLine(fadePen, new Point(2, Height - 2), new Point(FadeIn + 1, 2));
                }
                using (var state = context.PushTransform(Matrix.CreateTranslation(FadeOut - 6, 0))) {
                    context.DrawGeometry(brush, pen, pointGeometry);
                }
                if (wavePart.fadeout > 0) {
                    context.DrawLine(fadePen, new Point(Width - 1, Height - 2), new Point(FadeOut, 2));
                }
            }
        }

        /// <summary>
        /// The piano roll's visible range inside this part, if the piano roll has
        /// this part open.
        /// </summary>
        private Rect? PianoRollViewportRect() {
            if (part is not UVoicePart || part != partsCanvas.PianoRollOpenPart || pianoRollViewViewportTicks <= 0) {
                return null;
            }
            const double inset = 1;
            double innerWidth = Math.Max(0, Width - 2 * inset);
            double innerHeight = Math.Max(0, Height - 2 * inset);
            double vpLeft = Math.Max(0, pianoRollViewTickOffset * tickWidth);
            double vpRight = Math.Min(innerWidth, (pianoRollViewTickOffset + pianoRollViewViewportTicks) * tickWidth);
            if (vpRight <= vpLeft + 1) {
                return null;
            }
            return new Rect(inset + vpLeft, inset, vpRight - vpLeft, innerHeight);
        }

        /// <summary>The drag handle in the middle of the viewport indicator, if it fits.</summary>
        private static Rect? GripRect(Rect viewport) {
            const double width = 3 * GripDot + 2 * GripGap;
            const double height = 3 * GripDot + 2 * GripGap;
            if (viewport.Width < width + 6 || viewport.Height < height + 6) {
                return null;
            }
            return new Rect(
                Math.Round(viewport.Center.X - width / 2),
                Math.Round(viewport.Center.Y - height / 2),
                width, height);
        }

        /// <summary>
        /// Whether a point, in this control's coordinates, is on the drag handle of
        /// the piano roll viewport indicator.
        /// </summary>
        public bool HitPianoRollViewportHandle(Point point) {
            return PianoRollViewportRect() is Rect vpRect
                && GripRect(vpRect) is Rect grip
                && grip.Inflate(new Thickness(6, 8)).Contains(point);
        }

        private void PointerChanged(Point point) {
            if (PianoRollViewportRect() is Rect vpRect) {
                PianoRollViewportHovered = vpRect.Contains(point);
            }
        }

        // The file's channels as lanes, on a grid from the part's start: scrolling
        // moves the whole control, so it never re-bins the samples.
        private void DrawWaveform(DrawingContext context, UWavePart wavePart) {
            if (wavePart.Peaks is not { IsCompletedSuccessfully: true, Result: WavePeaks peaks }) {
                return;
            }
            var project = Core.DocManager.Inst.Project;
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            // Everything below is in device pixels. left is where the part starts,
            // from the left edge of the canvas.
            double pixelsPerTick = TickWidth * scale;
            double left = Bounds.X * scale;
            int columns = (int)Math.Ceiling(wavePart.Duration * pixelsPerTick);
            int visibleStart = Math.Clamp((int)Math.Floor(-left), 0, columns);
            int visibleEnd = Math.Clamp((int)Math.Ceiling(ViewWidth * scale - left) + 1, visibleStart, columns);
            if (visibleEnd <= visibleStart) {
                return;
            }
            double fileStartMs = project.timeAxis.TickPosToMsPos(wavePart.position) - wavePart.GetSkipMs(project);
            waveform.Update(project.timeAxis, (peaks, fileStartMs), wavePart.position, pixelsPerTick,
                Lanes(peaks.Channels, Math.Round(Bounds.Height * scale), scale), visibleStart, visibleEnd, 0, columns,
                (edges, min, max) => FillColumns(peaks, fileStartMs, edges, min, max));
            // Snap the columns to whole device pixels of the canvas.
            waveform.Draw(context, Bounds.Size, scale, Math.Round(left) - left, waveformFill);
        }

        // One band per channel, 2 pixels from the edges and from each other.
        private static (double y, double height)[] Lanes(int channels, double height, double scale) {
            double gap = Math.Round(2 * scale);
            double laneHeight = Math.Floor((height - gap * (channels + 1)) / channels);
            var lanes = new (double y, double height)[channels];
            for (int i = 0; i < channels; ++i) {
                lanes[i] = (gap + i * (laneHeight + gap), laneHeight);
            }
            return lanes;
        }

        private static bool FillColumns(WavePeaks peaks, double fileStartMs, double[] edges, float[][] min, float[][] max) {
            int Frame(double ms) => (int)Math.Floor((ms - fileStartMs) * peaks.SampleRate / 1000);
            for (int i = 0; i + 1 < edges.Length; ++i) {
                int f0 = Frame(edges[i]);
                int f1 = Math.Min(Frame(edges[i + 1]), peaks.Frames);
                for (int lane = 0; lane < min.Length; ++lane) {
                    if (f0 < 0 || f0 >= peaks.Frames) {
                        min[lane][i] = max[lane][i] = float.NaN;
                    } else if (f1 > f0) {
                        peaks.MinMax(lane, f0, f1, out min[lane][i], out max[lane][i]);
                    } else {
                        // Zoomed in past one sample per column: hold the last sample.
                        int f = Math.Max(0, f0 - 1);
                        peaks.MinMax(lane, f, f + 1, out min[lane][i], out max[lane][i]);
                    }
                }
            }
            return true;
        }

        public void Report(int value) {
        }

        public void Dispose() {
            unbinds.ForEach(u => u.Dispose());
            unbinds.Clear();
        }
    }
}
