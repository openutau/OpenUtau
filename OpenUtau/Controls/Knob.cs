using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Rotary knob.  Drag vertically to turn (Shift for fine), mouse wheel or
    /// arrow keys to step, double-click to reset to <see cref="DefaultValue"/>.
    /// Sweeps 270° from 7:30 (Minimum) to 4:30 (Maximum).  Tick dots light
    /// up from the origin to the value; the origin is 0 when the range spans
    /// zero (e.g. ±12 dB), else Minimum.
    /// </summary>
    public class Knob : RangeBase {
        public static readonly StyledProperty<double> DefaultValueProperty =
            AvaloniaProperty.Register<Knob, double>(nameof(DefaultValue));
        public static readonly StyledProperty<IBrush?> CapBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(CapBrush), Brushes.LightGray);
        public static readonly StyledProperty<IBrush?> PointerBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(PointerBrush), Brushes.Black);
        public static readonly StyledProperty<IBrush?> TickBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(TickBrush), Brushes.Gray);
        public static readonly StyledProperty<IBrush?> TickActiveBrushProperty =
            AvaloniaProperty.Register<Knob, IBrush?>(nameof(TickActiveBrush), Brushes.White);

        public double DefaultValue {
            get => GetValue(DefaultValueProperty);
            set => SetValue(DefaultValueProperty, value);
        }
        public IBrush? CapBrush {
            get => GetValue(CapBrushProperty);
            set => SetValue(CapBrushProperty, value);
        }
        public IBrush? PointerBrush {
            get => GetValue(PointerBrushProperty);
            set => SetValue(PointerBrushProperty, value);
        }
        public IBrush? TickBrush {
            get => GetValue(TickBrushProperty);
            set => SetValue(TickBrushProperty, value);
        }
        public IBrush? TickActiveBrush {
            get => GetValue(TickActiveBrushProperty);
            set => SetValue(TickActiveBrushProperty, value);
        }

        const double SweepDegrees = 270;
        const int TickCount = 21;
        // Pixels of vertical drag for a full sweep.
        const double DragPixels = 200;
        const double FineFactor = 0.1;
        static readonly IBrush ShadowBrush = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0));
        static readonly IBrush SkirtBrush = new LinearGradientBrush {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = {
                new GradientStop(Color.Parse("#4A4A4A"), 0),
                new GradientStop(Color.Parse("#1A1A1A"), 0.5),
                new GradientStop(Color.Parse("#050505"), 1),
            },
        };
        static readonly IPen RimPen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), 1);

        double lastY;
        bool dragging;

        static Knob() {
            AffectsRender<Knob>(ValueProperty, MinimumProperty, MaximumProperty,
                CapBrushProperty, PointerBrushProperty, TickBrushProperty, TickActiveBrushProperty);
            FocusableProperty.OverrideDefaultValue<Knob>(true);
            CursorProperty.OverrideDefaultValue<Knob>(new Cursor(StandardCursorType.SizeNorthSouth));
        }

        double Range => Math.Max(0, Maximum - Minimum);

        void Nudge(double delta) {
            Value = Math.Clamp(Value + delta, Minimum, Maximum);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            if (e.ClickCount == 2) {
                Value = Math.Clamp(DefaultValue, Minimum, Maximum);
            } else {
                dragging = true;
                lastY = e.GetPosition(this).Y;
                e.Pointer.Capture(this);
            }
            Focus();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            if (!dragging) {
                return;
            }
            double y = e.GetPosition(this).Y;
            double scale = Range / DragPixels;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                scale *= FineFactor;
            }
            Nudge((lastY - y) * scale);
            lastY = y;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (dragging) {
                dragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            dragging = false;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e) {
            base.OnPointerWheelChanged(e);
            double step = Range / 100;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                step *= FineFactor;
            }
            Nudge(e.Delta.Y * step);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            double step = Range / 100;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                step *= FineFactor;
            }
            switch (e.Key) {
                case Key.Up:
                case Key.Right:
                    Nudge(step);
                    e.Handled = true;
                    break;
                case Key.Down:
                case Key.Left:
                    Nudge(-step);
                    e.Handled = true;
                    break;
                case Key.Home:
                    Value = Minimum;
                    e.Handled = true;
                    break;
                case Key.End:
                    Value = Maximum;
                    e.Handled = true;
                    break;
            }
        }

        public override void Render(DrawingContext context) {
            double size = Math.Min(Bounds.Width, Bounds.Height);
            if (size <= 16) {
                return;
            }
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            double tickRadius = size / 2 - 2;
            double skirtRadius = size / 2 - 8;
            double capRadius = skirtRadius * 0.78;

            double t = Range > 0 ? Math.Clamp((Value - Minimum) / Range, 0, 1) : 0;
            double origin = Minimum < 0 && Maximum > 0 ? -Minimum / Range : 0;
            double litFrom = Math.Min(origin, t) - 1e-6, litTo = Math.Max(origin, t) + 1e-6;
            double dot = Math.Max(1.2, size / 44);
            for (int i = 0; i < TickCount; i++) {
                double ti = (double)i / (TickCount - 1);
                var brush = ti >= litFrom && ti <= litTo ? TickActiveBrush : TickBrush;
                if (brush != null) {
                    context.DrawEllipse(brush, null, PointAt(center, tickRadius, ti), dot, dot);
                }
            }

            context.DrawEllipse(ShadowBrush, null, center + new Point(0, 2), skirtRadius + 1, skirtRadius + 1);
            context.DrawEllipse(SkirtBrush, RimPen, center, skirtRadius, skirtRadius);
            context.DrawEllipse(CapFill(), RimPen, center, capRadius, capRadius);

            var pen = new Pen(PointerBrush, Math.Max(2, size / 22)) { LineCap = PenLineCap.Round };
            context.DrawLine(pen, PointAt(center, capRadius * 0.25, t), PointAt(center, skirtRadius - 2, t));
        }

        // A lit-from-top-left dome in the cap colour.
        IBrush? CapFill() {
            if (CapBrush is not ISolidColorBrush solid) {
                return CapBrush;
            }
            var c = solid.Color;
            return new RadialGradientBrush {
                GradientOrigin = new RelativePoint(0.35, 0.25, RelativeUnit.Relative),
                Center = new RelativePoint(0.45, 0.4, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                GradientStops = {
                    new GradientStop(Mix(c, Avalonia.Media.Colors.White, 0.45), 0),
                    new GradientStop(c, 0.55),
                    new GradientStop(Mix(c, Avalonia.Media.Colors.Black, 0.45), 1),
                },
            };
        }

        static Color Mix(Color a, Color b, double f) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * f), (byte)(a.G + (b.G - a.G) * f), (byte)(a.B + (b.B - a.B) * f));

        // Point at fraction t ∈ [0, 1] of the sweep, 0 = 7:30, 1 = 4:30.
        static Point PointAt(Point center, double radius, double t) {
            double rad = (-SweepDegrees / 2 + SweepDegrees * t) * Math.PI / 180;
            return new Point(center.X + Math.Sin(rad) * radius, center.Y - Math.Cos(rad) * radius);
        }
    }
}
