using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.ViewModels;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    public enum ExpDisMode { Hidden, Visible, Shadow };

    class ExpressionCanvas : Control {
        public static readonly DirectProperty<ExpressionCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<ExpressionCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<ExpressionCanvas, UVoicePart?> PartProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, UVoicePart?>(
                nameof(Part),
                o => o.Part,
                (o, v) => o.Part = v);
        public static readonly DirectProperty<ExpressionCanvas, string> KeyProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, string>(
                nameof(Key),
                o => o.Key,
                (o, v) => o.Key = v);
        public static readonly DirectProperty<ExpressionCanvas, bool> ShowRealCurveProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, bool>(
                nameof(ShowRealCurve),
                o => o.ShowRealCurve,
                (o, v) => o.ShowRealCurve = v);

        public static readonly DirectProperty<ExpressionCanvas, ExpDisMode> DisplayModeProperty =
            AvaloniaProperty.RegisterDirect<ExpressionCanvas, ExpDisMode>(
                nameof(DisplayMode), o => o.DisplayMode, (o, v) => o.DisplayMode = v);

        public double TickWidth {
            get => tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref tickWidth, value);
        }
        public double TickOffset {
            get => tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref tickOffset, value);
        }
        public UVoicePart? Part {
            get => part;
            set => SetAndRaise(PartProperty, ref part, value);
        }
        public string Key {
            get => key;
            set => SetAndRaise(KeyProperty, ref key, value);
        }
        public bool ShowRealCurve {
            get => showRealCurve;
            set => SetAndRaise(ShowRealCurveProperty, ref showRealCurve, value);
        }
        
        public ExpDisMode DisplayMode {
            get => displayMode;
            set => SetAndRaise(DisplayModeProperty, ref displayMode, value);
        }

        private double tickWidth;
        private double tickOffset;
        private UVoicePart? part;
        private string key = string.Empty;
        private bool showRealCurve = true;
        private ExpDisMode displayMode = ExpDisMode.Visible;

        private HashSet<UNote> selectedNotes = new HashSet<UNote>();
        private CurveSelection curveSelection = new CurveSelection();
        private Geometry pointGeometry;
        private Geometry circleGeometry;

        public ExpressionCanvas() {
            ClipToBounds = true;
            pointGeometry = new EllipseGeometry(new Rect(-2.5, -2.5, 5, 5));
            circleGeometry = new EllipseGeometry(new Rect(-4.5, -4.5, 9, 9));
            MessageBus.Current.Listen<NotesRefreshEvent>()
                .Subscribe(_ => InvalidateVisual());
            MessageBus.Current.Listen<RealCurveRefreshEvent>()
                .Subscribe(_ => {
                    if (ShowRealCurve) {
                        InvalidateVisual();
                    }
                });
            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    selectedNotes.Clear();
                    selectedNotes.UnionWith(e.selectedNotes);
                    selectedNotes.UnionWith(e.tempSelectedNotes);
                    InvalidateVisual();
                });
            MessageBus.Current.Listen<CurveSelectionEvent>()
                .Subscribe(e => {
                    curveSelection = e.selection;
                    InvalidateVisual();
                });
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            
            // Skip rendering if hidden
            if (DisplayMode == ExpDisMode.Hidden) {
                return;
            }
            if (Part == null) {
                return;
            }
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            if (viewModel == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            var track = project.tracks[Part.trackNo];
            if (!track.TryGetExpDescriptor(project, key, out var descriptor)) {
                return;
            }
            if (descriptor.max <= descriptor.min) {
                return;
            }
            
            if (DisplayMode != ExpDisMode.Shadow) {
                DrawBackgroundForHitTest(context);
            }
            
            double leftTick = TickOffset - 480;
            double rightTick = TickOffset + Bounds.Width / TickWidth + 480;
            double optionHeight = descriptor.type == UExpressionType.Options
                ? Bounds.Height / descriptor.options.Length
                : 0;
            if (descriptor.type == UExpressionType.Curve) {
                var curve = Part.curves.FirstOrDefault(c => c.descriptor == descriptor);
                double defaultHeight = Math.Round(Bounds.Height - Bounds.Height * (descriptor.defaultValue - descriptor.min) / (descriptor.max - descriptor.min));
                
                var lPen = DisplayMode == ExpDisMode.Shadow ? ThemeManager.NeutralAccentPen : ThemeManager.AccentPen1;
                var lPen2 = DisplayMode == ExpDisMode.Shadow ? new Pen(ThemeManager.NeutralAccentBrush, 3) : ThemeManager.AccentPen1Thickness3;
                var lPenSelected = DisplayMode == ExpDisMode.Shadow ? ThemeManager.NeutralAccentPen : ThemeManager.AccentPen2;
                var lPen2Selected = DisplayMode == ExpDisMode.Shadow ? new Pen(ThemeManager.NeutralAccentBrush, 3) : ThemeManager.AccentPen2Thickness3;
                var lPen3 = new Pen(ThemeManager.NeutralAccentBrush, 1, new DashStyle(new double[] { 4, 4 }, 0));
                var brush = DisplayMode == ExpDisMode.Shadow ? ThemeManager.NeutralAccentBrush : ThemeManager.AccentBrush1;
                
                double x3 = Math.Round(viewModel.TickToneToPoint(leftTick, 0).X);
                double x4 = Math.Round(viewModel.TickToneToPoint(rightTick, 0).X);
                context.DrawLine(lPen3, new Point(x3, defaultHeight), new Point(x4, defaultHeight));

                curveSelection.GetWholeCurveAndSelection(descriptor.abbr, curve, out List<int> xs, out List<int> ys);
                if (curve == null) {
                    xs.Insert(0, (int)leftTick);
                    xs.Add((int)rightTick);
                    for (int i = 0; i < xs.Count - 1; i++) {
                        double x1 = Math.Round(viewModel.TickToneToPoint(xs[i], 0).X);
                        double x2 = Math.Round(viewModel.TickToneToPoint(xs[i + 1], 0).X);
                        if (curveSelection.HasValue(descriptor.abbr)) {
                            if (curveSelection.StartPoint.x <= xs[i] && xs[i] <= curveSelection.EndPoint.x
                                && curveSelection.StartPoint.x <= xs[i + 1] && xs[i + 1] <= curveSelection.EndPoint.x) {
                                context.DrawLine(lPenSelected, new Point(x1, defaultHeight), new Point(x2, defaultHeight));
                            } else {
                                context.DrawLine(lPen, new Point(x1, defaultHeight), new Point(x2, defaultHeight));
                            }
                        } else {
                            context.DrawLine(lPen, new Point(x1, defaultHeight), new Point(x2, defaultHeight));
                        }
                    }
                    return;
                }

                int lTick = (int)Math.Floor(leftTick / 5) * 5;
                int rTick = (int)Math.Ceiling(rightTick / 5) * 5;
                int index = xs.BinarySearch(lTick);
                if (index < 0) {
                    index = -index - 1;
                }
                index = Math.Max(0, index) - 1;

                // Create geometry elements for the custom curve fill
                var fillGeometry = new PathGeometry();
                var fillFigure = new PathFigure { IsClosed = true };
                bool fillStarted = false;

                while (index < xs.Count) {
                    float tick1 = index < 0 ? lTick : xs[index];
                    float value1 = index < 0 ? descriptor.defaultValue : ys[index];
                    double x1 = viewModel.TickToneToPoint(tick1, 0).X;
                    double y1 = defaultHeight - Bounds.Height * (value1 - descriptor.defaultValue) / (descriptor.max - descriptor.min);
                    float tick2 = index == xs.Count - 1 ? rTick : xs[index + 1];
                    float value2 = index == xs.Count - 1 ? descriptor.defaultValue : ys[index + 1];
                    double x2 = viewModel.TickToneToPoint(tick2, 0).X;
                    double y2 = defaultHeight - Bounds.Height * (value2 - descriptor.defaultValue) / (descriptor.max - descriptor.min);
                    
                    if (!fillStarted) {
                        fillFigure.StartPoint = new Point(x1, defaultHeight);
                        fillFigure.Segments!.Add(new LineSegment { Point = new Point(x1, y1), IsStroked = false });
                        fillStarted = true;
                    }
                    fillFigure.Segments!.Add(new LineSegment { Point = new Point(x2, y2), IsStroked = false });
                    
                    if (tick2 >= rTick || index == xs.Count - 1) {
                        fillFigure.Segments!.Add(new LineSegment { Point = new Point(x2, defaultHeight), IsStroked = false });
                    }

                    IPen pen;
                    if (curveSelection.HasValue(descriptor.abbr)) {
                        if (curveSelection.StartPoint.x <= tick1 && tick1 <= curveSelection.EndPoint.x
                            && curveSelection.StartPoint.x <= tick2 && tick2 <= curveSelection.EndPoint.x) {
                            pen = value1 == descriptor.defaultValue && value2 == descriptor.defaultValue ? lPenSelected : lPen2Selected;
                        } else {
                            pen = value1 == descriptor.defaultValue && value2 == descriptor.defaultValue ? lPen : lPen2;
                        }
                    } else {
                        pen = value1 == descriptor.defaultValue && value2 == descriptor.defaultValue ? lPen : lPen2;
                    }
                    context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                    index++;
                    if (tick2 >= rTick) {
                        break;
                    }
                }
                
                if (fillStarted) {
                    fillGeometry.Figures!.Add(fillFigure);
                    using (var state = context.PushOpacity(0.2)) {
                        context.DrawGeometry(brush, null, fillGeometry);
                    }
                }

                if (ShowRealCurve) {
                    int baseIndexL = curve.realXs.BinarySearch(lTick);
                    if (baseIndexL < 0) {
                        baseIndexL = ~baseIndexL;
                    }
                    baseIndexL = Math.Max(0, baseIndexL - 1);
                    int baseIndexR = curve.realXs.BinarySearch(rTick);
                    if (baseIndexR < 0) {
                        baseIndexR = ~baseIndexR;
                    }
                    int offset = baseIndexL;
                    while (offset < baseIndexR) {
                        int start = offset;
                        while (start < baseIndexR && curve.realYs[start] < 0) ++start;
                        int end = start;
                        while (end < baseIndexR && curve.realYs[end] >= 0) ++end;
                        if (end - start < 2) {
                            offset = end;
                            continue;
                        }
                        var geometry = new PathGeometry();
                        // allinone: #2148 moved this fill's anchor from the canvas bottom to the
                        // default-value line together with the new envelope fill. The envelope fill
                        // is intended upstream behavior and stays; the real curve's own fill keeps
                        // its pre-#2148 shape (curve down to the bottom of the canvas).
                        var figure = new PathFigure {
                            IsClosed = false
                        };
                        for (int i = start; i < end; ++i) {
                            float tick = curve.realXs[i];
                            float value = curve.realYs[i];
                            double x = viewModel.TickToneToPoint(tick, 0).X;
                            double y = Bounds.Height * (1 - value / 1000.0);

                            if (i == start) {
                                figure.StartPoint = new Point(x, Bounds.Height);
                            }
                            figure.Segments!.Add(new LineSegment {
                                Point = new Point(x, y),
                                IsStroked = i != start
                            });
                            if (i == end - 1) {
                                figure.Segments!.Add(new LineSegment {
                                    Point = new Point(x, Bounds.Height),
                                    IsStroked = false
                                });
                            }
                        }
                        geometry.Figures!.Add(figure);
                        context.DrawGeometry(ThemeManager.RealCurveFillBrush, ThemeManager.RealCurvePen, geometry);
                        offset = end;
                    }
                }
                return;
            }
            if (descriptor.type == UExpressionType.Numerical) {
                double p1 = Math.Round(viewModel.TickToneToPoint(leftTick, 0).X);
                double p2 = Math.Round(viewModel.TickToneToPoint(rightTick, 0).X);
                var dashedPen = new Pen(ThemeManager.NeutralAccentBrushSemi, 1, new DashStyle(new double[] { 4, 4 }, 0));
                double defaultHeight = Math.Round(Bounds.Height - Bounds.Height * (descriptor.defaultValue - descriptor.min) / (descriptor.max - descriptor.min));
                context.DrawLine(dashedPen, new Point(p1, defaultHeight), new Point(p2, defaultHeight));
            }
            var shadowHPen = new Pen(ThemeManager.NeutralAccentBrush, 3);
            var shadowVPen = new Pen(ThemeManager.NeutralAccentBrush, 3);

            foreach (var phoneme in Part.phonemes) {
                if (phoneme.Error || phoneme.Parent == null) {
                    continue;
                }
                double leftBound = phoneme.position;
                double rightBound = phoneme.End;
                if (leftBound >= rightTick || rightBound <= leftTick) {
                    continue;
                }
                var note = phoneme.Parent;
                
                var hPen = DisplayMode == ExpDisMode.Shadow ? shadowHPen : (selectedNotes.Contains(note) ? ThemeManager.AccentPen2Thickness3 : ThemeManager.AccentPen1Thickness3);
                var vPen = DisplayMode == ExpDisMode.Shadow ? shadowVPen : (selectedNotes.Contains(note) ? ThemeManager.AccentPen2Thickness3 : ThemeManager.AccentPen1Thickness3);
                var brush = DisplayMode == ExpDisMode.Shadow ? ThemeManager.NeutralAccentBrush : (selectedNotes.Contains(note) ? ThemeManager.AccentBrush2 : ThemeManager.AccentBrush1);
                
                var (value, overriden) = phoneme.GetExpression(project, track, Key);
                double x1 = Math.Round(viewModel.TickToneToPoint(phoneme.position, 0).X);
                double x2 = Math.Round(viewModel.TickToneToPoint(phoneme.End, 0).X);
                
                if (descriptor.type == UExpressionType.Numerical) {
                    double valueHeight = Math.Round(Bounds.Height - Bounds.Height * (value - descriptor.min) / (descriptor.max - descriptor.min));
                    double zeroHeight = Math.Round(Bounds.Height - Bounds.Height * (0f - descriptor.min) / (descriptor.max - descriptor.min));
                    
                    double rectX = x1;
                    double rectY = Math.Min(zeroHeight, valueHeight);
                    double rectHeight = Math.Abs(zeroHeight - valueHeight);
                    double rectWidth = Math.Max(0, Math.Max(x1, x2) - rectX);
                    var fillRect = new Rect(rectX, rectY, rectWidth, rectHeight);
                    
                    // Use 20% opacity if edited, 10% opacity if default
                    double fillOpacity = overriden ? 0.20 : 0.10;

                    using (var state = context.PushOpacity(fillOpacity)) {
                        context.DrawRectangle(brush, null, fillRect);
                    }

                    // Vertical and horizontal lines
                    context.DrawLine(vPen, new Point(x1 + 0.5, zeroHeight + 0.5), new Point(x1 + 0.5, valueHeight + 3));
                    context.DrawLine(hPen, new Point(x1 + 3, valueHeight), new Point(Math.Max(x1 + 3, x2), valueHeight));
                    
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x1 + 0.5, valueHeight))) {
                        context.DrawGeometry(overriden ? brush : ThemeManager.BackgroundBrush, vPen, pointGeometry);
                    }
                } else if (descriptor.type == UExpressionType.Options) {
                    for (int i = 0; i < descriptor.options.Length; ++i) {
                        double y = optionHeight * (descriptor.options.Length - 1 - i + 0.5);
                        using (var state = context.PushTransform(Matrix.CreateTranslation(x1 + 4.5, y))) {
                            if ((int)value == i) {
                                if (overriden) {
                                    context.DrawGeometry(brush, null, pointGeometry);
                                }
                                context.DrawGeometry(null, hPen, circleGeometry);
                            } else {
                                context.DrawGeometry(null, ThemeManager.NeutralAccentPenSemi, circleGeometry);
                            }
                        }
                    }
                }
            }
            
            if (descriptor.type == UExpressionType.Options && DisplayMode != ExpDisMode.Shadow) {
                for (int i = 0; i < descriptor.options.Length; ++i) {
                    string option = descriptor.options[i];
                    if (string.IsNullOrEmpty(option)) {
                        option = "\"\"";
                    }
                    var textLayout = TextLayoutCache.Get(option, ThemeManager.ForegroundBrush, 12);
                    double y = optionHeight * (descriptor.options.Length - 1 - i + 0.5) - textLayout.Height * 0.5;
                    y = Math.Round(y);
                    var size = new Size(textLayout.Width + 8, textLayout.Height + 2);
                    using (var state = context.PushTransform(Matrix.CreateTranslation(12, y))) {
                        context.DrawRectangle(
                            ThemeManager.BackgroundBrush,
                            ThemeManager.NeutralAccentPenSemi,
                            new Rect(new Point(-4, -0.5), size), 4, 4);
                        textLayout.Draw(context, new Point());
                    }
                }
            }
        }

        private void DrawBackgroundForHitTest(DrawingContext context) {
            context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        }
    }
}
