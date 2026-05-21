using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NWaves.Signals;
using OpenUtau.Core.Ustx;
using ReactiveUI;
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

        private double tickWidth;
        private double trackHeight;
        private double viewWidth;
        private double tickOffset;
        private Point offset;
        private string text = string.Empty;
        private bool selected;
        private double fadeIn;
        private double fadeOut;
        private Geometry pointGeometry;

        public readonly UPart part;
        private readonly Pen notePen = new Pen(Brushes.White, 3);
        private readonly Pen fadePen = new Pen(Brushes.White);
        private List<IDisposable> unbinds = new List<IDisposable>();
        private WriteableBitmap? bitmap;
        private int[] bitmapData;

        public PartControl(UPart part, PartsCanvas canvas) {
            this.part = part;
            bitmapData = new int[0];
            pointGeometry = new EllipseGeometry(new Rect(0, 0, 6, 6));

            unbinds.Add(this.Bind(TickWidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty)));
            unbinds.Add(this.Bind(TrackHeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(WidthProperty, canvas.GetObservable(PartsCanvas.TickWidthProperty).Select(tickWidth => tickWidth * part.Duration)));
            unbinds.Add(this.Bind(HeightProperty, canvas.GetObservable(PartsCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(OffsetProperty, canvas.WhenAnyValue(x => x.TickOffset, x => x.TrackOffset,
                (tick, track) => new Point(-tick * TickWidth, -track * TrackHeight))));
            unbinds.Add(this.Bind(ViewWidthProperty, canvas.WhenAnyValue(x => x.Bounds).Select(bounds => bounds.Width)));
            unbinds.Add(this.Bind(TickOffsetProperty, canvas.WhenAnyValue(x => x.TickOffset).Select(tickOffset => tickOffset)));

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
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == OffsetProperty ||
                change.Property == TrackHeightProperty ||
                change.Property == TickWidthProperty) {
                SetPosition();
            }
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
            if (part is UVoicePart voicePart && voicePart.notes.Count > 0) {
                // Notes
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
            } else if (part is UWavePart wavePart) {
                // Waveform
                try {
                    DrawWaveform(wavePart, GetBitmap(ViewWidth));
                    if (bitmap != null) {
                        var srcRect = Bounds.WithY(0);
                        var dstRect = Bounds.WithX(1).WithY(0);
                        context.DrawImage(bitmap, srcRect, dstRect);
                    }
                } catch (Exception e) {
                    Log.Error(e, "failed to draw bitmap");
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

        private WriteableBitmap GetBitmap(double width) {
            int w = 128 * (int)(width / 128 + 1);
            if (bitmap == null || bitmap.Size.Width < w) {
                bitmap?.Dispose();
                var size = new PixelSize(w, (int)ViewConstants.TrackHeightMax);
                Log.Information($"created bitmap {size}");
                bitmap = new WriteableBitmap(
                    size, new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888,
                    Avalonia.Platform.AlphaFormat.Unpremul);
                bitmapData = new int[size.Width * size.Height];
            }
            return bitmap;
        }

        private void DrawWaveform(UWavePart wavePart, WriteableBitmap bitmap) {
            if (wavePart.Peaks == null ||
                !wavePart.Peaks.IsCompletedSuccessfully ||
                wavePart.Peaks.Result == null) {
                return;
            }
            var wholePeaks = wavePart.Peaks.Result;
            int skipCount = (int)(wavePart.peaksSampleRate * wavePart.GetSkipMs(Core.DocManager.Inst.Project) / 1000);
            if (skipCount >= wholePeaks[0].Length) return;

            double height = TrackHeight;
            double monoChnlAmp = (height - 4.0) / 2;
            double stereoChnlAmp = (height - 6.0) / 4;

            var timeAxis = Core.DocManager.Inst.Project.timeAxis;
            DiscreteSignal[] peaks = new DiscreteSignal[wholePeaks.Length];
            for (int i = 0; i < wholePeaks.Length; i++) {
                var newSamples = wholePeaks[i].Samples.Skip(skipCount);
                peaks[i] = new DiscreteSignal(wavePart.peaksSampleRate, newSamples);
            }
            int x = 0;
            if (TickOffset <= wavePart.position) {
                // Part starts in or to the right of view.
                x = (int)(TickWidth * (wavePart.position - TickOffset));
            }
            int posTick = (int)(TickOffset + x / TickWidth);
            double posMs = timeAxis.TickPosToMsPos(posTick);
            double offsetMs = timeAxis.TickPosToMsPos(wavePart.position);
            int sampleIndex = (int)(wavePart.peaksSampleRate * (posMs - offsetMs) * 0.001);
            sampleIndex = Math.Clamp(sampleIndex, 0, peaks[0].Length);
            using (var frameBuffer = bitmap.Lock()) {
                Array.Clear(bitmapData, 0, bitmapData.Length);
                while (x < frameBuffer.Size.Width) {
                    if (posTick >= wavePart.position + wavePart.Duration) {
                        break;
                    }
                    int nextPosTick = (int)(TickOffset + (x + 1) / TickWidth);
                    double nexPosMs = timeAxis.TickPosToMsPos(nextPosTick);
                    int nextSampleIndex = (int)(wavePart.peaksSampleRate * (nexPosMs - offsetMs) * 0.001);
                    nextSampleIndex = Math.Clamp(nextSampleIndex, 0, peaks[0].Length);
                    if (nextSampleIndex > sampleIndex) {
                        for (int i = 0; i < peaks.Length; ++i) {
                            var segment = new ArraySegment<float>(peaks[i].Samples, sampleIndex, nextSampleIndex - sampleIndex);
                            float min = segment.Min();
                            float max = segment.Max();
                            double ySpan = peaks.Length == 1 ? monoChnlAmp : stereoChnlAmp;
                            double yOffset = i == 1 ? monoChnlAmp : 0;
                            DrawPeak(bitmapData, frameBuffer.Size.Width, x,
                                (int)(ySpan * (1 + -min) + yOffset) + 2,
                                (int)(ySpan * (1 + -max) + yOffset) + 2);
                        }
                    }
                    x++;
                    posTick = nextPosTick;
                    posMs = nexPosMs;
                    sampleIndex = nextSampleIndex;
                }
                Marshal.Copy(bitmapData, 0, frameBuffer.Address, bitmapData.Length);
            }
        }

        private void DrawPeak(int[] data, int width, int x, int y1, int y2) {
            const int white = unchecked((int)0xFFFFFFFF);
            if (y1 > y2) {
                int temp = y2;
                y2 = y1;
                y1 = temp;
            }
            for (var y = y1; y <= y2; ++y) {
                data[x + width * y] = white;
            }
        }

        public void Report(int value) {
        }

        public void Dispose() {
            bitmap?.Dispose();
            unbinds.ForEach(u => u.Dispose());
            unbinds.Clear();
        }
    }
}
