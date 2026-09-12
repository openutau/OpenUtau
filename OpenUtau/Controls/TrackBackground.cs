using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtau.Core.Util.MusicTheory;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class TrackBackground : TemplatedControl {
        public static readonly DirectProperty<TrackBackground, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackBackground, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<TrackBackground, bool> IsPianoRollProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsPianoRoll),
                o => o.IsPianoRoll,
                (o, v) => o.IsPianoRoll = v);
        public static readonly DirectProperty<TrackBackground, bool> IsKeyboardProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, bool>(
                nameof(IsKeyboard),
                o => o.IsKeyboard,
                (o, v) => o.IsKeyboard = v
            );

        public static readonly DirectProperty<TrackBackground, Scale> ScaleProperty =
            AvaloniaProperty.RegisterDirect<TrackBackground, Scale>(
                nameof(Scale),
                o => o.Scale,
                (o, v) => o.Scale = v
            );

        public double TrackHeight {
            get => _trackHeight;
            private set => SetAndRaise(TrackHeightProperty, ref _trackHeight, value);
        }
        public double TrackOffset {
            get => _trackOffset;
            private set => SetAndRaise(TrackOffsetProperty, ref _trackOffset, value);
        }
        public bool IsPianoRoll {
            get => _isPianoRoll;
            set => SetAndRaise(IsPianoRollProperty, ref _isPianoRoll, value);
        }
        public bool IsKeyboard {
            get => _isKeyboard;
            set => SetAndRaise(IsPianoRollProperty, ref _isKeyboard, value);
        }
        public Scale Scale
        {
            get => _key;
            set => SetAndRaise(ScaleProperty, ref _key, value);
        }

        private double _trackHeight;
        private double _trackOffset;
        private bool _isPianoRoll;
        private bool _isKeyboard;
        private Scale _key = Scale.Default();

        public TrackBackground() {
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TrackHeightProperty ||
                change.Property == TrackOffsetProperty ||
                change.Property == ForegroundProperty ||
                change.Property == ScaleProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context)
        {
            if (TrackHeight == 0)
            {
                return;
            }
            int track = (int)TrackOffset;
            double top = TrackHeight * (track - TrackOffset);

            while (top < Bounds.Height)
            {
                bool isAltTrack = IsOutOfScale(track) ^ (ThemeManager.IsDarkMode && !IsKeyboard);
                bool isCenterKey = IsKeyboard && IsTonic(track);
                var brush =
                    isCenterKey ? ThemeManager.CenterKeyBrush
                    : IsKeyboard
                        ? (isAltTrack ? ThemeManager.BlackKeyBrush : ThemeManager.WhiteKeyBrush)
                    : isAltTrack ? Foreground
                    : Background;
                context.DrawRectangle(
                    brush,
                    null,
                    new Rect(0, (int)top, Bounds.Width, TrackHeight)
                );
                if (IsKeyboard && TrackHeight >= 12)
                {
                    brush =
                        isCenterKey ? ThemeManager.CenterKeyNameBrush
                        : isAltTrack ? ThemeManager.BlackKeyNameBrush
                        : ThemeManager.WhiteKeyNameBrush;
                    int tone = ViewConstants.MaxTone - 1 - track;
                    string toneName = MusicMath.GetToneName(tone);
                    var toneTextLayout = TextLayoutCache.Get(toneName, brush, 12);
                    var toneTextPosition = new Point(
                        Bounds.Width - 4 - (int)toneTextLayout.Width,
                        (int)(top + (TrackHeight - toneTextLayout.Height) / 2)
                    );
                    using (
                        var state = context.PushTransform(
                            Matrix.CreateTranslation(toneTextPosition)
                        )
                    )
                    {
                        toneTextLayout.Draw(context, new Point());
                    }

                    // TODO interval notations has nothing to do with scales, both should coexist : if solfege notation then all the app should turn C into Do etc
                    string degreeName = Preferences.Default.DegreeStyle switch
                    {
                        1 => Scale.SolfegeIntervalName(NoteHelper.CastNote(tone))?.ToString() ?? "",
                        2 => Scale.Interval(NoteHelper.CastNote(tone)).ToString() ?? "",
                        _ => ""
                    };

                    var degreeTextLayout = TextLayoutCache.Get(degreeName, brush, 12);
                    var degreeTextPosition = new Point(
                        4,
                        (int)(top + (TrackHeight - degreeTextLayout.Height) / 2)
                    );
                    using (
                        var state = context.PushTransform(
                            Matrix.CreateTranslation(degreeTextPosition)
                        )
                    )
                    {
                        degreeTextLayout.Draw(context, new Point());
                    }
                }
                track++;
                top += TrackHeight;
            }
        }

        private bool IsOutOfScale(int track)
        {
            if (!IsPianoRoll)
                return track % 2 == 1;

            int tone = ViewConstants.MaxTone - 1 - track;
            if (tone < 0)
                return false;
            return Scale.IsOutOfScale(NoteHelper.CastNote(tone));
        }

        private bool IsTonic(int track)
        {
            int tone = ViewConstants.MaxTone - 1 - track;
            return Scale.IsTonic(NoteHelper.CastNote(tone));
        }
    }
}
