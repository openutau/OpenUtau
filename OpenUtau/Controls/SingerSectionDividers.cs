using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Draws the lines between sections of the singer flyout's tile grid, over the grid instead of
    /// between its tiles so that every tile keeps its place in the rows and columns.
    /// Tiles fill columns top-down, so a section ending mid-column is bounded by a step:
    /// down the right of its last column's tiles, across under its last tile, down the left of the next section's tiles.
    /// </summary>
    public class SingerSectionDividers : Control {
        public static readonly StyledProperty<IBrush?> StrokeProperty =
            AvaloniaProperty.Register<SingerSectionDividers, IBrush?>(nameof(Stroke));

        public IBrush? Stroke {
            get => GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        static SingerSectionDividers() {
            AffectsRender<SingerSectionDividers>(StrokeProperty);
            IsHitTestVisibleProperty.OverrideDefaultValue<SingerSectionDividers>(false);
        }

        private double tileWidth;
        private double tileHeight;
        private int rows;
        private int count;
        private IReadOnlyList<int> starts = Array.Empty<int>();

        /// <param name="rows">Tiles per column.</param>
        /// <param name="count">Number of tiles.</param>
        /// <param name="starts">Index of the first tile of each section after the first.</param>
        public void Update(double tileWidth, double tileHeight, int rows, int count, IReadOnlyList<int> starts) {
            this.tileWidth = tileWidth;
            this.tileHeight = tileHeight;
            this.rows = rows;
            this.count = count;
            this.starts = starts;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context) {
            if (Stroke == null || rows <= 0) {
                return;
            }
            var pen = new Pen(Stroke, 1);
            // Half a pixel in, so the 1px line covers whole pixels.
            double X(int column) => Math.Round(column * tileWidth) + 0.5;
            double Y(int row) => Math.Round(row * tileHeight) + 0.5;
            foreach (int start in starts) {
                if (start <= 0 || start >= count) {
                    continue;
                }
                int column = start / rows;
                int row = start % rows;
                if (row == 0) {
                    // The section starts a new column.
                    context.DrawLine(pen, new Point(X(column), Y(0)), new Point(X(column), Y(rows)));
                    continue;
                }
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open()) {
                    // Right of the previous section's tiles, only where the next column has tiles.
                    bool nextColumn = count > (column + 1) * rows;
                    if (nextColumn) {
                        ctx.BeginFigure(new Point(X(column + 1), Y(0)), false);
                        ctx.LineTo(new Point(X(column + 1), Y(row)));
                    } else {
                        ctx.BeginFigure(new Point(X(column + 1), Y(row)), false);
                    }
                    ctx.LineTo(new Point(X(column), Y(row)));
                    // Left of the section's tiles in this column, unless that is the grid's edge.
                    if (column > 0) {
                        ctx.LineTo(new Point(X(column), Y(rows)));
                    }
                    ctx.EndFigure(false);
                }
                context.DrawGeometry(null, pen, geometry);
            }
        }
    }
}
