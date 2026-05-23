using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class PhonemeCanvas : Control {
        private static readonly IPen ErrorPen = new Pen(Brushes.Red, 1);

        public static readonly DirectProperty<PhonemeCanvas, IBrush> BackgroundProperty =
            AvaloniaProperty.RegisterDirect<PhonemeCanvas, IBrush>(
                nameof(Background),
                o => o.Background,
                (o, v) => o.Background = v);
        public static readonly DirectProperty<PhonemeCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<PhonemeCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<PhonemeCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PhonemeCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<PhonemeCanvas, UVoicePart?> PartProperty =
            AvaloniaProperty.RegisterDirect<PhonemeCanvas, UVoicePart?>(
                nameof(Part),
                o => o.Part,
                (o, v) => o.Part = v);
        public static readonly DirectProperty<PhonemeCanvas, bool> ShowPhonemeProperty =
            AvaloniaProperty.RegisterDirect<PhonemeCanvas, bool>(
                nameof(ShowPhoneme),
                o => o.ShowPhoneme,
                (o, v) => o.ShowPhoneme = v);

        public IBrush Background {
            get => background;
            private set => SetAndRaise(BackgroundProperty, ref background, value);
        }
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
        public bool ShowPhoneme {
            get => showPhoneme;
            private set => SetAndRaise(ShowPhonemeProperty, ref showPhoneme, value);
        }

        private IBrush background = Brushes.White;
        private double tickWidth;
        private double tickOffset;
        private UVoicePart? part;
        private bool showPhoneme = true;

        private HashSet<UNote> selectedNotes = new HashSet<UNote>();
        private Geometry pointGeometry;
        private UPhoneme? mouseoverPhoneme;

        public PhonemeCanvas() {
            ClipToBounds = true;
            pointGeometry = new EllipseGeometry(new Rect(-2.5, -2.5, 5, 5));
            MessageBus.Current.Listen<NotesRefreshEvent>()
                .Subscribe(_ => InvalidateVisual());
            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    selectedNotes.Clear();
                    selectedNotes.UnionWith(e.selectedNotes);
                    selectedNotes.UnionWith(e.tempSelectedNotes);
                    InvalidateVisual();
                });
            MessageBus.Current.Listen<PhonemeMouseoverEvent>()
                .Subscribe(e => {
                    if (mouseoverPhoneme != e.mouseoverPhoneme) {
                        mouseoverPhoneme = e.mouseoverPhoneme;
                        InvalidateVisual();
                    }
                });
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            if (Part == null || !ShowPhoneme) {
                return;
            }
            string langCode = PhonemeUIRender.getLangCode(Part);
            var viewModel = ((PianoRollViewModel?)DataContext)?.NotesViewModel;
            if (viewModel == null) {
                return;
            }
            context.DrawRectangle(Background, null, Bounds.WithX(0).WithY(0));
            double leftTick = TickOffset - 480;
            double rightTick = TickOffset + Bounds.Width / TickWidth + 480;
            bool raiseText = false;
            double lastTextEndX = double.NegativeInfinity;

            const double y = 35.5;
            const double height = 24;
            foreach (var phoneme in Part.phonemes) {
                double leftBound = viewModel.Project.timeAxis.MsPosToTickPos(phoneme.PositionMs - phoneme.preutter) - Part.position;
                double rightBound = phoneme.End;
                if (leftBound > rightTick || rightBound < leftTick || phoneme.Parent.OverlapError) {
                    continue;
                }
                var timeAxis = viewModel.Project.timeAxis;
                double x = Math.Round(viewModel.TickToneToPoint(phoneme.position, 0).X) + 0.5;
                double posMs = phoneme.PositionMs;
                if (!phoneme.Error) {
                    double x0 = viewModel.TickToneToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[0].X) - Part.position, 0).X;
                    double y0 = (1 - phoneme.envelope.data[0].Y / 100) * height;
                    double x1 = viewModel.TickToneToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[1].X) - Part.position, 0).X;
                    double y1 = (1 - phoneme.envelope.data[1].Y / 100) * height;
                    double x2 = viewModel.TickToneToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[2].X) - Part.position, 0).X;
                    double y2 = (1 - phoneme.envelope.data[2].Y / 100) * height;
                    double x3 = viewModel.TickToneToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[3].X) - Part.position, 0).X;
                    double y3 = (1 - phoneme.envelope.data[3].Y / 100) * height;
                    double x4 = viewModel.TickToneToPoint(timeAxis.MsPosToTickPos(posMs + phoneme.envelope.data[4].X) - Part.position, 0).X;
                    double y4 = (1 - phoneme.envelope.data[4].Y / 100) * height;

                    var pen = selectedNotes.Contains(phoneme.Parent) ? ThemeManager.AccentPen2 : ThemeManager.AccentPen1;
                    var brush = selectedNotes.Contains(phoneme.Parent) ? ThemeManager.AccentBrush2Semi : ThemeManager.AccentBrush1Semi;

                    var point0 = new Point(x0, y + y0);
                    var point1 = new Point(x1, y + y1);
                    var point2 = new Point(x2, y + y2);
                    var point3 = new Point(x3, y + y3);
                    var point4 = new Point(x4, y + y4);
                    var polyline = new PolylineGeometry(new Point[] { point0, point1, point2, point3, point4 }, true);
                    context.DrawGeometry(brush, pen, polyline);

                    brush = phoneme.preutterDelta.HasValue ? pen!.Brush : ThemeManager.BackgroundBrush;
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x0, y + y0 - 1))) {
                        context.DrawGeometry(brush, pen, pointGeometry);
                    }
                    brush = phoneme.overlapDelta.HasValue ? pen!.Brush : ThemeManager.BackgroundBrush;
                    using (var state = context.PushTransform(Matrix.CreateTranslation(point1))) {
                        context.DrawGeometry(brush, pen, pointGeometry);
                    }
                }

                var penPos = ThemeManager.AccentPen2;
                if (phoneme.rawPosition != phoneme.position) {
                    penPos = ThemeManager.AccentPen2Thickness3;
                }
                context.DrawLine(penPos, new Point(x, y), new Point(x, y + height));

                // FIXME: Changing code below may break `HitTestAlias`.
                if (viewModel.TickWidth > ViewConstants.PianoRollTickWidthShowDetails) {
                    string phonemeText = !string.IsNullOrEmpty(phoneme.phonemeMapped) ? phoneme.phonemeMapped : phoneme.phoneme;
                    if (!string.IsNullOrEmpty(phonemeText)) {
                        (double textX, double textY, Size size, TextLayout textLayout) 
                        = PhonemeUIRender.AliasPosition(viewModel, phoneme, langCode, ref lastTextEndX, ref raiseText);
                        using (var state = context.PushTransform(Matrix.CreateTranslation(textX + 2, textY))) {
                            var pen = mouseoverPhoneme == phoneme ? ThemeManager.AccentPen1Thickness2
                                : phoneme.Error ? ErrorPen
                                : ThemeManager.NeutralAccentPenSemi;
                            context.DrawRectangle(ThemeManager.BackgroundBrush, pen, new Rect(new Point(-2, 1.5), size), 4, 4);
                            textLayout.Draw(context, new Point());
                        }
                    }
                }
            }
        }
    }
}
