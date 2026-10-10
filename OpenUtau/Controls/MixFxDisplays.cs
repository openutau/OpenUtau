using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.SignalChain.Effects;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Base for the curve screens on the Track Polish faceplates: background,
    /// grid and scale labels in <see cref="GridBrush"/>, the curve in
    /// <see cref="CurveBrush"/> with a translucent fill beneath it, and a
    /// faint glass highlight on top.
    /// </summary>
    public abstract class FxDisplay : Control {
        public static readonly StyledProperty<IBrush?> BackgroundProperty =
            AvaloniaProperty.Register<FxDisplay, IBrush?>(nameof(Background));
        public static readonly StyledProperty<IBrush?> CurveBrushProperty =
            AvaloniaProperty.Register<FxDisplay, IBrush?>(nameof(CurveBrush), Brushes.White);
        public static readonly StyledProperty<IBrush?> GridBrushProperty =
            AvaloniaProperty.Register<FxDisplay, IBrush?>(nameof(GridBrush), Brushes.Gray);

        public IBrush? Background {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }
        public IBrush? CurveBrush {
            get => GetValue(CurveBrushProperty);
            set => SetValue(CurveBrushProperty, value);
        }
        public IBrush? GridBrush {
            get => GetValue(GridBrushProperty);
            set => SetValue(GridBrushProperty, value);
        }

        static readonly IBrush Glass = new LinearGradientBrush {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops = {
                new GradientStop(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
            },
        };
        const double LabelSize = 9;

        static FxDisplay() {
            AffectsRender<FxDisplay>(BackgroundProperty, CurveBrushProperty, GridBrushProperty);
        }

        protected FxDisplay() {
            ClipToBounds = true;
        }

        public sealed override void Render(DrawingContext context) {
            var rect = new Rect(Bounds.Size);
            context.DrawRectangle(Background, null, rect);
            if (rect.Width < 16 || rect.Height < 16) {
                return;
            }
            RenderPlot(context, rect.Deflate(new Thickness(6, 6, 6, 4)));
            context.DrawRectangle(Glass, null, rect);
        }

        protected abstract void RenderPlot(DrawingContext context, Rect rect);

        protected Pen GridPen(double opacity = 0.5, double dash = 0) {
            var pen = new Pen(WithOpacity(GridBrush, opacity), 1);
            if (dash > 0) {
                pen.DashStyle = new DashStyle(new[] { dash, dash }, 0);
            }
            return pen;
        }

        protected Pen CurvePen() => new Pen(CurveBrush, 2) { LineJoin = PenLineJoin.Round };

        protected IBrush? CurveFill(double opacity = 0.22) => WithOpacity(CurveBrush, opacity);

        protected static IBrush? WithOpacity(IBrush? brush, double opacity) =>
            brush is ISolidColorBrush solid ? new SolidColorBrush(solid.Color, solid.Opacity * opacity) : brush;

        /// <summary>A marker dot on the curve: a ring in the background colour around a curve-coloured dot.</summary>
        protected void DrawMarker(DrawingContext context, Point p) {
            context.DrawEllipse(Background, new Pen(CurveBrush, 1.5), p, 3.5, 3.5);
        }

        protected static StreamGeometry Polyline(int count, Func<int, Point> point, double? closeToY = null) {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open()) {
                var first = point(0);
                if (closeToY is double y0) {
                    ctx.BeginFigure(new Point(first.X, y0), true);
                    ctx.LineTo(first);
                } else {
                    ctx.BeginFigure(first, false);
                }
                Point last = first;
                for (int i = 1; i < count; i++) {
                    last = point(i);
                    ctx.LineTo(last);
                }
                if (closeToY is double y1) {
                    ctx.LineTo(new Point(last.X, y1));
                }
                ctx.EndFigure(closeToY != null);
            }
            return geometry;
        }

        /// <summary>Draws a small scale label; <paramref name="anchor"/> is its top-left, top-centre or top-right.</summary>
        protected void DrawLabel(DrawingContext context, string text, Point anchor, TextAlignment align = TextAlignment.Left, IBrush? brush = null) {
            using var layout = new TextLayout(text, new Typeface(FontFamily.Default), LabelSize,
                brush ?? GridBrush, TextAlignment.Left, TextWrapping.NoWrap);
            double x = align switch {
                TextAlignment.Center => anchor.X - layout.Width / 2,
                TextAlignment.Right => anchor.X - layout.Width,
                _ => anchor.X,
            };
            layout.Draw(context, new Point(x, anchor.Y));
        }
    }

    /// <summary>EQ magnitude response, 20 Hz – 20 kHz log, ±15 dB, with a marker per band.</summary>
    public class EqCurveDisplay : FxDisplay {
        public static readonly StyledProperty<double> LowDbProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(LowDb));
        public static readonly StyledProperty<double> MidFreqProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(MidFreq), 1000);
        public static readonly StyledProperty<double> MidDbProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(MidDb));
        public static readonly StyledProperty<double> HighDbProperty =
            AvaloniaProperty.Register<EqCurveDisplay, double>(nameof(HighDb));

        public double LowDb { get => GetValue(LowDbProperty); set => SetValue(LowDbProperty, value); }
        public double MidFreq { get => GetValue(MidFreqProperty); set => SetValue(MidFreqProperty, value); }
        public double MidDb { get => GetValue(MidDbProperty); set => SetValue(MidDbProperty, value); }
        public double HighDb { get => GetValue(HighDbProperty); set => SetValue(HighDbProperty, value); }

        const double MinFreq = 20, MaxFreq = 20000, RangeDb = 15;
        // Shelf corners, as configured in BiquadEQ.
        const double LowShelfHz = 200, HighShelfHz = 8000;
        readonly BiquadEQ eq = new BiquadEQ(MixFxSource.SampleRate, MixFxSource.Channels);

        static EqCurveDisplay() {
            AffectsRender<EqCurveDisplay>(LowDbProperty, MidFreqProperty, MidDbProperty, HighDbProperty);
        }

        protected override void RenderPlot(DrawingContext context, Rect rect) {
            double X(double f) => rect.Left + Math.Log(f / MinFreq) / Math.Log(MaxFreq / MinFreq) * rect.Width;
            double Y(double db) => rect.Center.Y - Math.Clamp(db, -RangeDb, RangeDb) / RangeDb * rect.Height / 2;

            var minor = GridPen(0.25);
            foreach (var f in new[] { 50.0, 200, 500, 2000, 5000 }) {
                context.DrawLine(minor, new Point(X(f), rect.Top), new Point(X(f), rect.Bottom));
            }
            foreach (var db in new[] { -12.0, -6, 6, 12 }) {
                context.DrawLine(minor, new Point(rect.Left, Y(db)), new Point(rect.Right, Y(db)));
            }
            var major = GridPen(0.6);
            foreach (var (f, label) in new[] { (100.0, "100"), (1000.0, "1k"), (10000.0, "10k") }) {
                context.DrawLine(major, new Point(X(f), rect.Top), new Point(X(f), rect.Bottom));
                DrawLabel(context, label, new Point(X(f) + 2, rect.Bottom - 11));
            }
            context.DrawLine(major, new Point(rect.Left, Y(0)), new Point(rect.Right, Y(0)));
            DrawLabel(context, "+12", new Point(rect.Left, Y(12) - 5));
            DrawLabel(context, "-12", new Point(rect.Left, Y(-12) - 5));

            eq.Configure(LowDb, MidFreq, MixFxSource.EqMidQ, MidDb, HighDb);
            int n = Math.Max(2, (int)(rect.Width / 2));
            Point At(int i) {
                double f = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)i / (n - 1));
                return new Point(X(f), Y(eq.ResponseDb(f)));
            }
            context.DrawGeometry(CurveFill(), null, Polyline(n, At, Y(0)));
            context.DrawGeometry(null, CurvePen(), Polyline(n, At));
            foreach (var f in new[] { LowShelfHz, MidFreq, HighShelfHz }) {
                DrawMarker(context, new Point(X(f), Y(eq.ResponseDb(f))));
            }
        }
    }

    /// <summary>
    /// Compressor transfer curve: input −60…0 dB → output −60…+6 dB,
    /// including makeup, with the threshold marked.
    /// </summary>
    public class CompCurveDisplay : FxDisplay {
        public static readonly StyledProperty<double> ThresholdDbProperty =
            AvaloniaProperty.Register<CompCurveDisplay, double>(nameof(ThresholdDb));
        public static readonly StyledProperty<double> RatioProperty =
            AvaloniaProperty.Register<CompCurveDisplay, double>(nameof(Ratio), 1);
        public static readonly StyledProperty<double> MakeupDbProperty =
            AvaloniaProperty.Register<CompCurveDisplay, double>(nameof(MakeupDb));

        public double ThresholdDb { get => GetValue(ThresholdDbProperty); set => SetValue(ThresholdDbProperty, value); }
        public double Ratio { get => GetValue(RatioProperty); set => SetValue(RatioProperty, value); }
        public double MakeupDb { get => GetValue(MakeupDbProperty); set => SetValue(MakeupDbProperty, value); }

        const double InMin = -60, InMax = 0, OutMin = -60, OutMax = 6;

        static CompCurveDisplay() {
            AffectsRender<CompCurveDisplay>(ThresholdDbProperty, RatioProperty, MakeupDbProperty);
        }

        protected override void RenderPlot(DrawingContext context, Rect rect) {
            double X(double db) => rect.Left + (db - InMin) / (InMax - InMin) * rect.Width;
            double Y(double db) => rect.Bottom - (Math.Clamp(db, OutMin, OutMax) - OutMin) / (OutMax - OutMin) * rect.Height;
            double Out(double input) => input + SimpleCompressor.CurveGainDb(input, ThresholdDb, Ratio) + MakeupDb;

            var minor = GridPen(0.3);
            foreach (var db in new[] { -48.0, -36, -24, -12 }) {
                context.DrawLine(minor, new Point(X(db), rect.Top), new Point(X(db), rect.Bottom));
                context.DrawLine(minor, new Point(rect.Left, Y(db)), new Point(rect.Right, Y(db)));
                DrawLabel(context, db.ToString("0"), new Point(X(db), rect.Bottom - 11), TextAlignment.Center);
            }
            context.DrawLine(GridPen(0.6, 3), new Point(X(InMin), Y(InMin)), new Point(X(InMax), Y(InMax)));
            context.DrawLine(GridPen(0.9, 2), new Point(X(ThresholdDb), rect.Top), new Point(X(ThresholdDb), rect.Bottom));

            int n = Math.Max(2, (int)(rect.Width / 2));
            Point At(int i) {
                double input = InMin + (InMax - InMin) * i / (n - 1);
                return new Point(X(input), Y(Out(input)));
            }
            context.DrawGeometry(CurveFill(0.12), null, Polyline(n, At, rect.Bottom));
            context.DrawGeometry(null, CurvePen(), Polyline(n, At));
            DrawMarker(context, new Point(X(ThresholdDb), Y(Out(ThresholdDb))));
            DrawLabel(context, $"{Ratio:0.0}:1", new Point(rect.Right, rect.Top), TextAlignment.Right, CurveBrush);
        }
    }

    /// <summary>
    /// Reverb decay over 0–4 s on a dB scale (so decay reads as a slope): the
    /// full-band tail, and the shorter high-frequency tail that damping
    /// leaves, starting after the pre-delay gap at the wet level.
    /// </summary>
    public class ReverbCurveDisplay : FxDisplay {
        public static readonly StyledProperty<double> RoomSizeProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(RoomSize));
        public static readonly StyledProperty<double> DampProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(Damp));
        public static readonly StyledProperty<double> WetProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(Wet), 1);
        public static readonly StyledProperty<double> PreDelayMsProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, double>(nameof(PreDelayMs));
        public static readonly StyledProperty<string?> PresetProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, string?>(nameof(Preset));
        public static readonly StyledProperty<IBrush?> HighBrushProperty =
            AvaloniaProperty.Register<ReverbCurveDisplay, IBrush?>(nameof(HighBrush), Brushes.White);

        public double RoomSize { get => GetValue(RoomSizeProperty); set => SetValue(RoomSizeProperty, value); }
        public double Damp { get => GetValue(DampProperty); set => SetValue(DampProperty, value); }
        public double Wet { get => GetValue(WetProperty); set => SetValue(WetProperty, value); }
        public double PreDelayMs { get => GetValue(PreDelayMsProperty); set => SetValue(PreDelayMsProperty, value); }
        public string? Preset { get => GetValue(PresetProperty); set => SetValue(PresetProperty, value); }
        public IBrush? HighBrush { get => GetValue(HighBrushProperty); set => SetValue(HighBrushProperty, value); }

        const double Seconds = 4;
        const double FloorDb = -48;
        // Effective wet gain drawn at 0 dB (top).
        const double FullScaleWet = 0.5;

        static ReverbCurveDisplay() {
            AffectsRender<ReverbCurveDisplay>(RoomSizeProperty, DampProperty, WetProperty,
                PreDelayMsProperty, PresetProperty, HighBrushProperty);
        }

        protected override void RenderPlot(DrawingContext context, Rect rect) {
            double presetWet = FxPresets.Reverb.TryGetValue(Preset ?? FxPresets.Off, out var rp) ? rp.Wet : 0;
            double wet = presetWet * Math.Clamp(Wet, 0, 2) / FullScaleWet;
            var (low, high) = Freeverb.DecaySeconds(RoomSize, Damp);
            double preDelay = PreDelayMs / 1000;
            double top = rect.Top + 12;

            double X(double t) => rect.Left + t / Seconds * rect.Width;
            double Y(double db) => top + Math.Clamp(db / FloorDb, 0, 1) * (rect.Bottom - top);
            var minor = GridPen(0.3);
            for (double t = 0.5; t < Seconds; t += 0.5) {
                context.DrawLine(minor, new Point(X(t), rect.Top), new Point(X(t), rect.Bottom));
                if (t % 1 == 0) {
                    DrawLabel(context, $"{t:0}s", new Point(X(t) + 2, rect.Bottom - 11));
                }
            }
            foreach (var db in new[] { -12.0, -24, -36 }) {
                context.DrawLine(minor, new Point(rect.Left, Y(db)), new Point(rect.Right, Y(db)));
            }

            if (wet > 1e-4) {
                double levelDb = 20 * Math.Log10(wet);
                int n = Math.Max(2, (int)(rect.Width / 2));
                StreamGeometry Envelope(double rt60) => Polyline(n, i => {
                    double t = Seconds * i / (n - 1);
                    double db = t < preDelay ? FloorDb : levelDb - 60 * (t - preDelay) / rt60;
                    return new Point(X(t), Y(db));
                }, rect.Bottom);
                context.DrawGeometry(CurveFill(0.35), new Pen(CurveBrush, 1.5), Envelope(low));
                context.DrawGeometry(WithOpacity(HighBrush, 0.55), null, Envelope(high));
                DrawMarker(context, new Point(X(preDelay), Y(levelDb)));
            }
            DrawLabel(context, wet > 1e-4 ? $"RT60 {low:0.0} s" : "DRY", new Point(rect.Right, rect.Top), TextAlignment.Right, CurveBrush);
        }
    }
}
