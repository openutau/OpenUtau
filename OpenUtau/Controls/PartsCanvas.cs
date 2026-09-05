using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using OpenUtau.App.ViewModels;
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

        private double tickWidth;
        private double trackHeight;
        private double tickOffset;
        private double trackOffset;
        private ObservableCollection<UPart>? _items;

        Dictionary<UPart, PartControl> partControls = new Dictionary<UPart, PartControl>();

        public PartsCanvas() {
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
                        control.InvalidateVisual();
                    }
                });
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
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
    }
}
