using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using NWaves.Audio;
using NWaves.FeatureExtractors;
using NWaves.FeatureExtractors.Options;
using NWaves.Filters.Fda;
using NWaves.Utils;

namespace OpenUtau.App.Controls {
    class OtoPlot : Control {
        public struct OtoPlotTiming {
            public double cutoff;
            public double offset;
            public double consonant;
            public double preutter;
            public double overlap;
        }

        public static readonly DirectProperty<OtoPlot, bool> ZoomInMelProperty =
            AvaloniaProperty.RegisterDirect<OtoPlot, bool>(
                nameof(ZoomInMel),
                o => o.ZoomInMel,
                (o, v) => o.ZoomInMel = v);
        public static readonly DirectProperty<OtoPlot, WaveFile?> WaveFileProperty =
            AvaloniaProperty.RegisterDirect<OtoPlot, WaveFile?>(
                nameof(WaveFile),
                o => o.WaveFile,
                (o, v) => o.WaveFile = v);
        public static readonly DirectProperty<OtoPlot, Tuple<int, double[]>?> F0Property =
            AvaloniaProperty.RegisterDirect<OtoPlot, Tuple<int, double[]>?>(
                nameof(F0),
                o => o.F0,
                (o, v) => o.F0 = v);
        public static readonly DirectProperty<OtoPlot, OtoPlotTiming> TimingProperty =
            AvaloniaProperty.RegisterDirect<OtoPlot, OtoPlotTiming>(
                nameof(Timing),
                o => o.Timing,
                (o, v) => o.Timing = v);

        public bool ZoomInMel {
            get => zoomInMel;
            set => SetAndRaise(ZoomInMelProperty, ref zoomInMel, value);
        }
        public WaveFile? WaveFile {
            get => waveFile;
            set => SetAndRaise(WaveFileProperty, ref waveFile, value);
        }
        public Tuple<int, double[]>? F0 {
            get => f0;
            set => SetAndRaise(F0Property, ref f0, value);
        }
        public OtoPlotTiming Timing {
            get => timing;
            set => SetAndRaise(TimingProperty, ref timing, value);
        }

        private bool zoomInMel;
        private WaveFile? waveFile;
        private Tuple<int, double[]>? f0;
        private OtoPlotTiming timing;

        const int kFftSize = 1024;
        const int kMelSize = 80;

        private WriteableBitmap? wavBitmap;
        private byte[]? wavBitmapData;
        private WriteableBitmap? melBitmap;

        private IBrush blueFill = new SolidColorBrush(Avalonia.Media.Colors.LightBlue, 0.5);
        private IBrush pinkFill = new SolidColorBrush(Avalonia.Media.Colors.Pink, 0.5);
        private IPen blueLine = new Pen(SolidColorBrush.Parse("#4EA6EA"), 2);
        private IPen limeLine = new Pen(Brushes.Lime);
        private IPen redLine = new Pen(Brushes.Red);
        private IPen whiteLine = new Pen(Brushes.White);
        private TextLayout ovlText;
        private TextLayout preText;
        private PolylineGeometry? f0Geometry;

        private double xStart;
        private double xSpan;
        private Point panPosition;
        private Point lastPointerPos;

        public OtoPlot() {
            ClipToBounds = true;
            ovlText = new TextLayout(
                "OVL",
                new Typeface(FontFamily.Default, weight: FontWeight.Normal),
                12, Brushes.Lime, TextAlignment.Left, TextWrapping.NoWrap);
            preText = new TextLayout(
                "PRE",
                new Typeface(FontFamily.Default, weight: FontWeight.Normal),
                12, Brushes.Red, TextAlignment.Left, TextWrapping.NoWrap);

            PointerPressed += OtoPlot_PointerPressed;
            PointerReleased += OtoPlot_PointerReleased;
            PointerMoved += OtoPlot_PointerMoved;
            PointerWheelChanged += OtoPlot_PointerWheelChanged;
        }

        private void OtoPlot_PointerMoved(object? sender, PointerEventArgs e) {
            var point = e.GetCurrentPoint(this);
            lastPointerPos = point.Position;
            if (point.Properties.IsLeftButtonPressed) {
                Pan((panPosition - point.Position).X / Bounds.Width);
                panPosition = point.Position;
            }
        }

        private void OtoPlot_PointerPressed(object? sender, PointerPressedEventArgs e) {
            e.Pointer.Capture(this);
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed) {
                panPosition = point.Position;
            }
        }

        private void OtoPlot_PointerReleased(object? sender, PointerReleasedEventArgs e) {
            e.Pointer.Capture(null);
        }

        public void Pan(double delta) {
            if (WaveFile != null) {
                delta *= xSpan;
                double duration = WaveFile.Signals[0].Duration;
                xStart += delta;
                xSpan = Math.Min(xSpan, duration);
                xStart = Math.Max(xStart, 0);
                xStart = Math.Min(xStart, duration - xSpan);
            }
            InvalidateVisual();
        }

        private void OtoPlot_PointerWheelChanged(object? sender, PointerWheelEventArgs e) {
            var point = e.GetCurrentPoint(this);
            Zoom(1.0 - 0.25 * Math.Sign(e.Delta.Y), point.Position.X / Bounds.Width);
        }

        public void Zoom(double mult, double center) {
            if (WaveFile != null) {
                double duration = WaveFile.Signals[0].Duration;
                double xCenter = xStart + xSpan * center;
                xSpan *= mult;
                xSpan = Math.Max(xSpan, 0.1);
                xSpan = Math.Min(xSpan, duration);
                xStart = xCenter - xSpan * center;
                xStart = Math.Max(xStart, 0);
                xStart = Math.Min(xStart, duration - xSpan);
            }
            InvalidateVisual();
        }

        public double GetPointerMs() {
            return (lastPointerPos.X / Bounds.Width * xSpan + xStart) * 1000.0;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ZoomInMelProperty) {
                UpdateMel(WaveFile);
                InvalidateVisual();
            } else if (change.Property == WaveFileProperty) {
                xStart = 0;
                if (WaveFile != null) {
                    xSpan = WaveFile.Signals[0].Duration;
                }
                UpdateMel(WaveFile);
                InvalidateVisual();
            } else if (change.Property == F0Property ||
                change.Property == TimingProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
            UpdateWav();
            if (wavBitmap != null) {
                context.DrawImage(wavBitmap,
                    new Rect(0, 0, wavBitmap.Size.Width, wavBitmap.Size.Height),
                    new Rect(0, 0, Bounds.Width, Bounds.Height / 3));
            }
            if (melBitmap != null) {
                if (WaveFile != null) {
                    double duration = WaveFile.Signals[0].Duration;
                    double srcX = melBitmap.Size.Width * xStart / duration;
                    double srcWidth = melBitmap.Size.Width * xSpan / duration;
                    context.DrawImage(melBitmap,
                        new Rect(srcX, 0, srcWidth, melBitmap.Size.Height),
                        new Rect(0, Bounds.Height / 3, Bounds.Width, Bounds.Height * 2 / 3));
                }
                DrawF0(context);
            }
            DrawTiming(context);
        }

        void UpdateWav() {
            int width = (int)Bounds.Width;
            int height = (int)(Bounds.Height / 3);
            if (wavBitmap == null ||
                wavBitmap.Size.Width != Bounds.Width ||
                wavBitmap.Size.Height < Bounds.Height / 3) {
                wavBitmap?.Dispose();
                wavBitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Rgba8888,
                    Avalonia.Platform.AlphaFormat.Unpremul);
                wavBitmapData = new byte[width * height * 4];
            }
            if (wavBitmapData == null) {
                return;
            }
            Array.Clear(wavBitmapData);
            if (WaveFile != null) {
                var samples = WaveFile.Signals[0].Samples;
                double duration = WaveFile.Signals[0].Duration;
                int startSample = (int)Math.Clamp(
                    xStart / duration * samples.Length, 0, samples.Length - 1);
                int endSample = (int)Math.Clamp(
                    (xStart + xSpan) / duration * samples.Length, 0, samples.Length - 1);
                double samplesPerPixel = (endSample - startSample) / width;
                if (samplesPerPixel > 64) {
                    for (int x = 0; x < width; ++x) {
                        double min = 0;
                        double max = 0;
                        for (int j = startSample + (int)(samplesPerPixel * x);
                            j < startSample + (int)(samplesPerPixel * (x + 1)); ++j) {
                            min = Math.Min(min, samples[j]);
                            max = Math.Max(max, samples[j]);
                        }
                        int maxY = (int)Math.Clamp((height - 1) * (0.5 - max / 2), 0, height - 1);
                        int minY = (int)Math.Clamp((height - 1) * (0.5 - min / 2), 0, height - 1);
                        for (int y = maxY; y <= minY; ++y) {
                            int index = y * width + x;
                            wavBitmapData[index * 4] = 0;
                            wavBitmapData[index * 4 + 1] = 0;
                            wavBitmapData[index * 4 + 2] = 0xFF;
                            wavBitmapData[index * 4 + 3] = 0xFF;
                        }
                    }
                } else {
                    double lastX = 0;
                    double lastY = 0;
                    for (int i = startSample; i < endSample; ++i) {
                        double x = Math.Clamp((i - startSample) / samplesPerPixel, 0, width - 1);
                        double y = Math.Clamp((height - 1) * (0.5 - samples[i] / 2), 0, height - 1);
                        if (i > startSample) {
                            double dx;
                            double dy;
                            if (x - lastX > Math.Abs(y - lastY)) {
                                dx = 1;
                                dy = (y - lastY) / (x - lastX);
                            } else {
                                dx = (x - lastX) / Math.Abs(y - lastY);
                                dy = Math.Sign(y - lastY);
                            }
                            double xx = lastX;
                            double yy = lastY;
                            while (xx < x) {
                                int index = (int)(Math.Round(yy) * width + Math.Round(xx));
                                wavBitmapData[index * 4] = 0;
                                wavBitmapData[index * 4 + 1] = 0;
                                wavBitmapData[index * 4 + 2] = 0xFF;
                                wavBitmapData[index * 4 + 3] = 0xFF;
                                xx += dx;
                                yy += dy;
                            }
                        }
                        lastX = x;
                        lastY = y;
                    }
                }
            }
            using (var frameBuffer = wavBitmap.Lock()) {
                Marshal.Copy(wavBitmapData, 0, frameBuffer.Address, wavBitmapData.Length);
            }
        }

        void UpdateMel(WaveFile? waveFile) {
            melBitmap?.Dispose();
            melBitmap = null;
            if (waveFile == null) {
                return;
            }
            var colormap = new Viridis();
            double[,] mel = GetMel(waveFile, ZoomInMel);
            double min = mel.Cast<double>().Min();
            double range = mel.Cast<double>().Max() - min;
            byte[] bitmapData = new byte[mel.GetLength(1) * mel.GetLength(0) * 4];
            if (range > 0) {
                int index = 0;
                for (int i = 0; i < mel.GetLength(0); ++i) {
                    for (int j = 0; j < mel.GetLength(1); ++j) {
                        mel[i, j] = (mel[i, j] - min) / range;
                        byte intensity = (byte)Math.Clamp(mel[i, j] * 0xFF, 0, 0xFF);
                        var (r, g, b) = colormap.GetRGB(intensity);
                        bitmapData[index++] = r;
                        bitmapData[index++] = g;
                        bitmapData[index++] = b;
                        bitmapData[index++] = 0xFF;
                    }
                }
            }
            melBitmap = new WriteableBitmap(
                new PixelSize(mel.GetLength(1), mel.GetLength(0)),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Unpremul);
            using (var frameBuffer = melBitmap.Lock()) {
                Marshal.Copy(bitmapData, 0, frameBuffer.Address, bitmapData.Length);
            }
        }

        static double[,] GetMel(WaveFile wav, bool zoomInMel) {
            int zoomIn = zoomInMel ? 8 : 1;
            var bands = FilterBanks.MelBands(
                kMelSize, wav.WaveFmt.SamplingRate,
                highFreq: wav.WaveFmt.SamplingRate / 2 / zoomIn);
            int fftSize = kFftSize * Math.Max(1, zoomIn / 4);
            var extractor = new FilterbankExtractor(
               new FilterbankOptions {
                   SamplingRate = wav.WaveFmt.SamplingRate,
                   FrameSize = fftSize,
                   FftSize = fftSize,
                   HopSize = GetHopSize(wav.WaveFmt.SamplingRate),
                   Window = NWaves.Windows.WindowType.Hann,
                   FilterBank = FilterBanks.Triangular(
                       fftSize, wav.WaveFmt.SamplingRate, bands),
               });
            var padded = new float[fftSize / 2].Concat(wav.Signals[0].Samples).Concat(new float[fftSize / 2]).ToArray();
            var mel = extractor.ComputeFrom(padded);
            var heatmap = new double[mel[0].Length, mel.Count];
            for (int i = 0; i < mel.Count; i++) {
                for (int j = 0; j < mel[i].Length; j++) {
                    heatmap[kMelSize - 1 - j, i] = Math.Log(Math.Max(mel[i][j], 1e-4));
                }
            }
            return heatmap;
        }

        void DrawF0(DrawingContext context) {
            if (F0 == null || WaveFile == null || melBitmap == null) {
                f0Geometry = null;
                return;
            }
            int hopSize = F0.Item1;
            int zoomIn = zoomInMel ? 8 : 1;
            double high = Scale.HerzToMel(WaveFile.WaveFmt.SamplingRate / 2 / zoomIn);
            double low = Scale.HerzToMel(0);
            var points = new List<Point>();
            points.Clear();
            for (int i = 0; i < F0.Item2.Length; ++i) {
                double f0X = 1.0 * i * hopSize / WaveFile.WaveFmt.SamplingRate;
                f0X = (f0X - xStart) * Bounds.Width / xSpan;
                double f0Y = Bounds.Height - Scale.HerzToMel(F0.Item2[i]) / (high - low) * (Bounds.Height * 2 / 3);
                points.Add(new Point(f0X, f0Y));
            }
            f0Geometry = new PolylineGeometry(points, false);
            context.DrawGeometry(null, whiteLine, f0Geometry);
        }

        static int GetHopSize(int sampleRate) {
            return sampleRate / 400;
        }

        void DrawTiming(DrawingContext context) {
            if (WaveFile == null) {
                return;
            }
            int width = (int)Bounds.Width;
            int height = (int)(Bounds.Height / 3);
            double duration = WaveFile.Signals[0].Duration;
            double msToX = 0.001 / xSpan * width;
            double xOffset = xStart / xSpan * width;
            double totalDurMs = duration * 1000.0;
            double cutoff = Timing.cutoff >= 0
                ? totalDurMs - Timing.cutoff
                : Timing.offset - Timing.cutoff;
            double offsetX = Timing.offset * msToX - xOffset;
            double consonantX = (Timing.offset + Timing.consonant) * msToX - xOffset;
            double preutterX = (Timing.offset + Timing.preutter) * msToX - xOffset;
            double overlapX = (Timing.offset + Timing.overlap) * msToX - xOffset;
            double cutoffX = cutoff * msToX - xOffset;

            if (offsetX > 0) {
                context.DrawRectangle(blueFill, null,
                    new Rect(0, 0, offsetX, height));
            }
            if (consonantX > offsetX) {
                context.DrawRectangle(pinkFill, null,
                    new Rect(offsetX, 0, consonantX - offsetX, height));
            }
            if (cutoffX <= width) {
                context.DrawRectangle(blueFill, null,
                    new Rect(cutoffX, 0, width - cutoffX, height));
            }

            context.DrawLine(blueLine, new Point(offsetX, height), new Point(overlapX, 1));
            context.DrawLine(blueLine, new Point(overlapX, 1), new Point(cutoffX, 1));
            context.DrawLine(blueLine, new Point(cutoffX, 1), new Point(cutoffX, height));

            context.DrawLine(limeLine, new Point(overlapX, 0), new Point(overlapX, Bounds.Height));
            ovlText.Draw(context, new Point(overlapX, height));
            context.DrawLine(redLine, new Point(preutterX, 0), new Point(preutterX, Bounds.Height));
            preText.Draw(context, new Point(preutterX, height));
        }

        protected override void OnUnloaded(RoutedEventArgs e) {
            base.OnUnloaded(e);
            wavBitmap?.Dispose();
            melBitmap?.Dispose();
        }
    }
}
