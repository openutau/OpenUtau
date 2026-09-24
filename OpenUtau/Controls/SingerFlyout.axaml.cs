using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Controls {
    public partial class SingerFlyout : UserControl {
        const double TileWidth = 180;
        const double TileHeight = 40;
        const int MinRows = 3;
        const double WheelStep = 60;
        // Flyout presenter padding and border plus the footer row, around the tile grid.
        const double ChromeHeight = 50;
        // Flyout presenter padding and border left and right of the tile grid.
        const double ChromeWidth = 20;
        // Hover scroll speed in px/s, from the inner to the outer edge of a grabber.
        const double MinHoverSpeed = 150;
        const double MaxHoverSpeed = 1200;

        private int maxRows = int.MaxValue;
        private int maxColumns = int.MaxValue;
        private IDisposable? tilesSubscription;
        private readonly DispatcherTimer hoverTimer;
        private readonly Stopwatch hoverClock = new Stopwatch();
        private Control? hoveredGrabber;
        private double hoverSpeed;
        private Control? pressedTile;

        public SingerFlyout() {
            InitializeComponent();
            TileScroller.AddHandler(PointerWheelChangedEvent, TileScrollerWheel, RoutingStrategies.Tunnel);
            hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, HoverTick);
        }

        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);
            tilesSubscription?.Dispose();
            tilesSubscription = null;
            if (DataContext is SingerFlyoutViewModel viewModel) {
                tilesSubscription = viewModel.WhenAnyValue(x => x.Tiles)
                    .Subscribe(_ => UpdateSize());
            }
        }

        /// <summary>
        /// Lets columns grow down to the screen edge the flyout opens towards, and the grid grow right
        /// to the edge of the window (or the screen, whichever is nearer). The flyout opens below
        /// the anchor, or above it when there isn't room for even a few rows below.
        /// </summary>
        public void FitToScreen(Visual anchor) {
            maxRows = int.MaxValue;
            maxColumns = int.MaxValue;
            var topLevel = TopLevel.GetTopLevel(anchor);
            var screen = topLevel?.Screens?.ScreenFromVisual(anchor);
            var anchorTop = anchor.PointToScreen(new Point(0, 0));
            var anchorBottom = anchor.PointToScreen(new Point(0, anchor.Bounds.Height));
            if (screen != null) {
                var area = screen.WorkingArea;
                int RowsFor(double pixels) => (int)((pixels / screen.Scaling - ChromeHeight) / TileHeight);
                int below = RowsFor(area.Bottom - anchorBottom.Y);
                int above = RowsFor(anchorTop.Y - area.Y);
                maxRows = Math.Max(MinRows, below >= MinRows ? below : Math.Max(below, above));
                int right = area.Right;
                if (topLevel != null) {
                    right = Math.Min(right, topLevel.PointToScreen(new Point(topLevel.Bounds.Width, 0)).X);
                }
                double width = (right - anchorTop.X) / screen.Scaling - ChromeWidth;
                maxColumns = Math.Max(1, (int)(width / TileWidth));
            }
            UpdateSize();
        }

        void UpdateSize() {
            int count = (DataContext as SingerFlyoutViewModel)?.Tiles.Count ?? 0;
            int rows = Math.Clamp(count, 1, Math.Max(1, maxRows));
            int columns = Math.Max(1, (count + rows - 1) / rows);
            TileScroller.Height = rows * TileHeight;
            TileScroller.Width = Math.Min(columns, maxColumns) * TileWidth;
            TileScroller.Offset = new Vector(0, 0);
            UpdateGrabbers();
        }

        void UpdateGrabbers() {
            double offset = TileScroller.Offset.X;
            double max = TileScroller.Extent.Width - TileScroller.Viewport.Width;
            LeftGrabber.IsVisible = offset > 0.5;
            RightGrabber.IsVisible = offset < max - 0.5;
            if (hoveredGrabber != null && !hoveredGrabber.IsVisible) {
                // Reached the end in that direction.
                StopHoverScroll();
            }
        }

        void TileScrollerScrollChanged(object? sender, ScrollChangedEventArgs e) {
            UpdateGrabbers();
        }

        void TileScrollerWheel(object? sender, PointerWheelEventArgs e) {
            double delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            ScrollTo(TileScroller.Offset.X - delta * WheelStep);
            e.Handled = true;
        }

        void ScrollTo(double x) {
            double max = Math.Max(0, TileScroller.Extent.Width - TileScroller.Viewport.Width);
            TileScroller.Offset = new Vector(Math.Clamp(x, 0, max), 0);
        }

        // Hovering over an edge grabber scrolls towards it, faster nearer the outer edge.
        void GrabberPointerMoved(object? sender, PointerEventArgs e) {
            if (sender is not Control grabber) {
                return;
            }
            double width = Math.Max(1, grabber.Bounds.Width);
            double x = Math.Clamp(e.GetPosition(grabber).X / width, 0, 1);
            double depth = grabber == LeftGrabber ? 1 - x : x;
            double speed = MinHoverSpeed + (MaxHoverSpeed - MinHoverSpeed) * depth;
            hoverSpeed = grabber == LeftGrabber ? -speed : speed;
            if (hoveredGrabber != grabber) {
                hoveredGrabber = grabber;
                hoverClock.Restart();
                hoverTimer.Start();
            }
        }

        void GrabberPointerExited(object? sender, PointerEventArgs e) {
            if (sender == hoveredGrabber) {
                StopHoverScroll();
            }
        }

        void HoverTick(object? sender, EventArgs e) {
            double seconds = hoverClock.Elapsed.TotalSeconds;
            hoverClock.Restart();
            ScrollTo(TileScroller.Offset.X + hoverSpeed * seconds);
        }

        void StopHoverScroll() {
            hoveredGrabber = null;
            hoverTimer.Stop();
            hoverClock.Reset();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            StopHoverScroll();
            base.OnDetachedFromVisualTree(e);
        }

        // Tiles are plain Borders, so they implement click themselves: press, then release over the same tile.
        void TilePressed(object? sender, PointerPressedEventArgs e) {
            if (sender is Control tile && e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) {
                pressedTile = tile;
                e.Pointer.Capture(tile);
                e.Handled = true;
            }
        }

        void TileReleased(object? sender, PointerReleasedEventArgs e) {
            if (sender is not Control tile || tile != pressedTile || e.InitialPressMouseButton != MouseButton.Left) {
                return;
            }
            pressedTile = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (new Rect(tile.Bounds.Size).Contains(e.GetPosition(tile)) &&
                tile.DataContext is SingerTileViewModel tileViewModel &&
                DataContext is SingerFlyoutViewModel viewModel) {
                viewModel.Select(tileViewModel);
            }
        }

        // Handled here so the press doesn't reach the tile and select the singer.
        void FavStarPressed(object? sender, PointerPressedEventArgs e) {
            if (sender is Control { DataContext: SingerTileViewModel tile } star &&
                e.GetCurrentPoint(star).Properties.IsLeftButtonPressed) {
                tile.IsFavourite = !tile.IsFavourite;
                e.Handled = true;
            }
        }
    }
}
