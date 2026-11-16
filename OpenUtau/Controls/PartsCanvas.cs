using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    class PartsCanvas : Canvas {
        public static readonly DirectProperty<PartsCanvas, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<PartsCanvas, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<PartsCanvas, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<PartsCanvas, double> TrackOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(TrackOffset),
                o => o.TrackOffset,
                (o, v) => o.TrackOffset = v);
        public static readonly DirectProperty<PartsCanvas, ObservableCollection<UPart>?> ItemsProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, ObservableCollection<UPart>?>(
                nameof(Items),
                o => o.Items,
                (o, v) => o.Items = v);
        public static readonly DirectProperty<PartsCanvas, UPart?> PianoRollOpenPartProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, UPart?>(
                nameof(PianoRollOpenPart),
                o => o.PianoRollOpenPart,
                (o, v) => o.PianoRollOpenPart = v);
        public static readonly DirectProperty<PartsCanvas, double> PianoRollViewTickOffsetProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(PianoRollViewTickOffset),
                o => o.PianoRollViewTickOffset,
                (o, v) => o.PianoRollViewTickOffset = v);
        public static readonly DirectProperty<PartsCanvas, double> PianoRollViewViewportTicksProperty =
            AvaloniaProperty.RegisterDirect<PartsCanvas, double>(
                nameof(PianoRollViewViewportTicks),
                o => o.PianoRollViewViewportTicks,
                (o, v) => o.PianoRollViewViewportTicks = v);

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
        public ObservableCollection<UPart>? Items {
            get => _items;
            set => SetAndRaise(ItemsProperty, ref _items, value);
        }
        public UPart? PianoRollOpenPart {
            get => _pianoRollOpenPart;
            set {
                if (SetAndRaise(PianoRollOpenPartProperty, ref _pianoRollOpenPart, value)) {
                    InvalidatePartViewport();
                }
            }
        }
        public double PianoRollViewTickOffset {
            get => _pianoRollViewTickOffset;
            set {
                if (SetAndRaise(PianoRollViewTickOffsetProperty, ref _pianoRollViewTickOffset, value)) {
                    InvalidatePartViewport();
                }
            }
        }
        public double PianoRollViewViewportTicks {
            get => _pianoRollViewViewportTicks;
            set {
                if (SetAndRaise(PianoRollViewViewportTicksProperty, ref _pianoRollViewViewportTicks, value)) {
                    InvalidatePartViewport();
                }
            }
        }

        private double tickWidth;
        private double trackHeight;
        private double tickOffset;
        private double trackOffset;
        private ObservableCollection<UPart>? _items;
        private UPart? _pianoRollOpenPart;
        private double _pianoRollViewTickOffset;
        private double _pianoRollViewViewportTicks;

        Dictionary<UPart, PartControl> partControls = new Dictionary<UPart, PartControl>();

        readonly PianoRollViewportIndicator viewportIndicator;

        public PartsCanvas() {
            viewportIndicator = new PianoRollViewportIndicator(this);
            Children.Add(viewportIndicator);
            // Parts move and resize through layout, which moves the piano roll's view with its part.
            LayoutUpdated += (_, _) => viewportIndicator.InvalidateVisual();
            MessageBus.Current.Listen<TracksRefreshEvent>()
                .Subscribe(_ => {
                    foreach (var (part, control) in partControls) {
                        control.SetPosition();
                    }
                });
            MessageBus.Current.Listen<PartsSelectionEvent>()
                .Subscribe(e => {
                    foreach (var (part, control) in partControls) {
                        control.Selected = e.selectedParts.Contains(part)
                            || e.tempSelectedParts.Contains(part);
                    }
                });
            MessageBus.Current.Listen<PartRefreshEvent>()
                .Subscribe(e => {
                    if (partControls.TryGetValue(e.part, out var control)) {
                        control.SetSize();
                        control.SetPosition();
                        control.Refersh();
                    }
                });
            MessageBus.Current.Listen<PartRedrawEvent>()
                .Subscribe(e => {
                    if (partControls.TryGetValue(e.part, out var control)) {
                        control.InvalidateVisual();
                    }
                });
            MessageBus.Current.Listen<TimeAxisChangedEvent>()
                .Subscribe(e => {
                    foreach (var (part, control) in partControls) {
                        control.InvalidateWaveform();
                    }
                });
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => InvalidateVisual());
        }

        protected override Size ArrangeOverride(Size finalSize) {
            var size = base.ArrangeOverride(finalSize);
            viewportIndicator.Arrange(new Rect(finalSize));
            return size;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == TickWidthProperty || change.Property == TrackHeightProperty
                || change.Property == TickOffsetProperty || change.Property == TrackOffsetProperty) {
                InvalidatePartViewport();
            }
            if (change.Property == ItemsProperty) {
                if (change.OldValue != null && change.OldValue is ObservableCollection<UPart> oldCol) {
                    oldCol.CollectionChanged -= Items_CollectionChanged;
                }
                if (change.NewValue != null && change.NewValue is ObservableCollection<UPart> newCol) {
                    newCol.CollectionChanged += Items_CollectionChanged;
                }
            }
        }

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                    if (e.OldItems != null) {
                        foreach (var item in e.OldItems) {
                            if (item is UPart part) {
                                Remove(part);
                            }
                        }
                    }
                    if (e.NewItems != null) {
                        foreach (var item in e.NewItems) {
                            if (item is UPart part) {
                                Add(part);
                            }
                        }
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (var (part, _) in partControls) {
                        Remove(part);
                    }
                    break;
            }
        }

        void Add(UPart part) {
            var control = new PartControl(part, this);
            Children.Add(control);
            partControls.Add(part, control);
        }

        void Remove(UPart part) {
            var control = partControls[part];
            control.Dispose();
            partControls.Remove(part);
            Children.Remove(control);
        }

        public void HandlePhraseRenderStateEvent(UVoicePart part, int startTick, int endTick, bool rendered) {
            foreach (var control in partControls) {
                if (control.Key.Equals(part)) {
                    control.Value.UpdateRenderStatus(startTick, endTick, rendered);
                    return;
                }
            }
        }

        public void HandlePhraseRenderStateEvent(UVoicePart part, RenderPhrase phrase, bool rendered) {
            foreach (var control in partControls) {
                if (control.Key.Equals(part)) {
                    control.Value.UpdateRenderStatus(phrase, rendered);
                    return;
                }
            }
        }

        public void HandleRebuildPhraseBlocksEvent(UVoicePart part) {
            foreach (var control in partControls) {
                if (control.Key.Equals(part)) {
                    if (control.Value.RebuildPhraseBlocks(part)) {
                        control.Value.InvalidateVisual();
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Set the rendered status on all RenderPhrases in every part.
        /// </summary>
        public void SetAllRenderPhrases(bool state) {
            foreach (var control in partControls) {
                control.Value.SetAllBlocks(state);
            }
        }

        /// <summary>
        /// Set the rendered status on all RenderPhrases in UTrack.
        /// </summary>
        public void SetAllRenderPhrases(UTrack track, bool state) {
            foreach (var control in partControls) {
                if (control.Key.trackNo.Equals(track.TrackNo)) {
                    control.Value.SetAllBlocks(state);
                    return;
                }
            }
        }

        /// <summary>
        /// Set the rendered status on all RenderPhrases in UVoicePart.
        /// </summary>
        public void SetAllRenderPhrases(UVoicePart voicePart, bool state) {
            foreach (var control in partControls) {
                if (control.Key.Equals(voicePart)) {
                    control.Value.SetAllBlocks(state);
                    return;
                }
            }
        }

        void InvalidatePartViewport() => viewportIndicator.InvalidateVisual();

        /// <summary>
        /// Where the piano roll's view is, on the row of its open part, in this canvas's coordinates.
        /// The piano roll scrolls over the whole track, so this can go past the open part.
        /// </summary>
        public Rect? PianoRollViewportRect() {
            if (_pianoRollOpenPart is not UVoicePart part || _pianoRollViewViewportTicks <= 0
                || !partControls.ContainsKey(part)) {
                return null;
            }
            const double inset = 1;
            double left = (part.position + _pianoRollViewTickOffset - TickOffset) * TickWidth;
            double width = _pianoRollViewViewportTicks * TickWidth;
            double top = (part.trackNo - TrackOffset) * TrackHeight;
            if (width <= 1) {
                return null;
            }
            return new Rect(left, top + inset, width, Math.Max(0, TrackHeight - 2 * inset));
        }

        /// <summary>Whether a point, in this canvas's coordinates, is on the drag handle of the piano roll's view.</summary>
        public bool HitPianoRollViewportHandle(Point point) {
            return PianoRollViewportRect() is Rect viewport
                && PianoRollViewportIndicator.GripRect(viewport) is Rect grip
                && grip.Inflate(new Thickness(6, 8)).Contains(point);
        }

        /// <summary>Shows the drag handle while the pointer is over the piano roll's view.</summary>
        public void HoverPianoRollViewport(Point? point) {
            bool hovered = point is Point p && PianoRollViewportRect() is Rect viewport && viewport.Contains(p);
            if (viewportIndicator.Hovered != hovered) {
                viewportIndicator.Hovered = hovered;
                viewportIndicator.InvalidateVisual();
            }
        }

        /// <summary>
        /// Draws the piano roll's view over the parts, across the parts it spans. Lets the pointer
        /// through; the canvas hit tests its drag handle.
        /// </summary>
        class PianoRollViewportIndicator : Control {
            static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            static readonly IPen Border = new Pen(Brushes.White, 2);
            // Keeps the white border visible where the view spans the track background between parts.
            static readonly IPen Outline = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 4);
            const double GripDot = 2;
            const double GripGap = 3;
            readonly PartsCanvas canvas;
            public bool Hovered;

            public PianoRollViewportIndicator(PartsCanvas canvas) {
                this.canvas = canvas;
                IsHitTestVisible = false;
                ZIndex = int.MaxValue;
            }

            /// <summary>The drag handle in the middle of the view, if it fits.</summary>
            public static Rect? GripRect(Rect viewport) {
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

            public override void Render(DrawingContext context) {
                if (canvas.PianoRollViewportRect() is not Rect viewport) {
                    return;
                }
                var shape = new RoundedRect(viewport, new CornerRadius(3));
                context.DrawRectangle(null, Outline, shape);
                context.DrawRectangle(Fill, Border, shape);
                if (Hovered && GripRect(viewport) is Rect grip) {
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
        }
    }
}
