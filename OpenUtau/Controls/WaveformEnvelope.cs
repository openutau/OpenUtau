using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using OpenUtau.Core;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// A min/max envelope of audio with one column per device pixel, kept as
    /// geometry. The piano roll and wave parts draw their waveforms with it.
    ///
    /// Columns sit on a fixed grid in song time (column k covers ticks
    /// [origin + k / p, origin + (k + 1) / p] for p device pixels per tick), so
    /// scrolling by a fraction of a pixel does not re-bin the samples and make
    /// the peaks shimmer. The envelope is built for a range wider than the view;
    /// scrolling only moves it, by whole device pixels so it stays crisp, and it
    /// is rebuilt on zoom, on <see cref="Invalidate"/>, on a new key, or when the
    /// view leaves the range.
    /// </summary>
    class WaveformEnvelope {
        /// <summary>
        /// Fills min[lane][i] and max[lane][i] with the audio from edgesMs[i] to
        /// edgesMs[i + 1], or NaN where there is none, so those columns are left
        /// blank instead of drawing a zero-volume line. Returns false if no audio
        /// is available yet, to try again on the next render.
        /// </summary>
        public delegate bool Fill(double[] edgesMs, float[][] min, float[][] max);

        private StreamGeometry? geometry;
        // The geometry covers columns [cacheStart, cacheEnd) and was built for these
        // inputs. It is in device pixels; x = 0 is column cacheStart.
        private int cacheStart;
        private int cacheEnd;
        private object? cacheKey;
        private double cacheOrigin;
        private double cachePixelsPerTick;
        private (double y, double height)[] cacheLanes = Array.Empty<(double, double)>();
        private bool cacheValid;

        public void Invalidate() => cacheValid = false;

        /// <summary>
        /// Builds the envelope if it does not cover columns [visibleStart, visibleEnd)
        /// for these inputs, with margins clamped to [minColumn, maxColumn).
        /// </summary>
        /// <param name="key">Anything else the audio depends on, compared with Equals.</param>
        /// <param name="lanes">Bands stacked top to bottom in device pixels, one per lane of audio.</param>
        public void Update(TimeAxis timeAxis, object key, double origin, double pixelsPerTick,
                (double y, double height)[] lanes, int visibleStart, int visibleEnd,
                int minColumn, int maxColumn, Fill fill) {
            if (pixelsPerTick != cachePixelsPerTick) {
                // Zooming rebuilds every frame, so build only what is visible; the
                // first scroll afterwards builds the margins.
                Build(timeAxis, key, origin, pixelsPerTick, lanes, visibleStart, visibleEnd, fill);
            } else if (!cacheValid || geometry == null || !Equals(key, cacheKey) || origin != cacheOrigin ||
                !lanes.SequenceEqual(cacheLanes) || visibleStart < cacheStart || visibleEnd > cacheEnd) {
                // Half a view of margin on each side, so scrolling rarely rebuilds.
                int margin = (visibleEnd - visibleStart) / 2;
                Build(timeAxis, key, origin, pixelsPerTick, lanes,
                    Math.Max(minColumn, visibleStart - margin), Math.Min(maxColumn, visibleEnd + margin), fill);
            }
        }

        /// <summary>
        /// Draws the envelope, clipped to the control, with column 0 at column0X
        /// device pixels from the control's left edge. column0X must put the columns
        /// on whole device pixels.
        /// </summary>
        public void Draw(DrawingContext context, Size size, double scale, double column0X, IBrush brush) {
            if (geometry == null) {
                return;
            }
            // Map device pixels to the control's units. The cached margins lie
            // outside the control, so clip them.
            var transform = Matrix.CreateTranslation(cacheStart + column0X, 0) * Matrix.CreateScale(1 / scale, 1 / scale);
            using (context.PushClip(new Rect(size)))
            using (context.PushTransform(transform)) {
                context.DrawGeometry(brush, null, geometry);
            }
        }

        private void Build(TimeAxis timeAxis, object key, double origin, double pixelsPerTick,
                (double y, double height)[] lanes, int start, int end, Fill fill) {
            cacheKey = key;
            cacheOrigin = origin;
            cachePixelsPerTick = pixelsPerTick;
            cacheLanes = lanes;
            cacheStart = start;
            cacheEnd = end;
            cacheValid = true;
            geometry = null;

            int columns = end - start;
            if (columns <= 0) {
                return;
            }
            // Song time of each column's left edge; edges[columns] is the right edge of the last one.
            var edges = new double[columns + 1];
            for (int i = 0; i <= columns; ++i) {
                edges[i] = timeAxis.TickPosToMsPos(origin + (start + i) / pixelsPerTick);
            }
            var min = new float[lanes.Length][];
            var max = new float[lanes.Length][];
            for (int lane = 0; lane < lanes.Length; ++lane) {
                min[lane] = new float[columns];
                max[lane] = new float[columns];
            }
            if (!fill(edges, min, max)) {
                return;
            }

            // One filled figure per run of columns with audio: along the tops, then
            // back along the bottoms. Each column spans [i, i + 1).
            var top = new double[columns];
            var bottom = new double[columns];
            var g = new StreamGeometry();
            using (var ctx = g.Open()) {
                for (int lane = 0; lane < lanes.Length; ++lane) {
                    var (y, height) = lanes[lane];
                    if (height < 1) {
                        continue;
                    }
                    for (int i = 0; i < columns; ++i) {
                        if (float.IsNaN(max[lane][i])) {
                            continue;
                        }
                        // Whole pixel rows, at least one, so edges stay sharp and quiet
                        // audio still shows a line.
                        top[i] = Math.Clamp(Math.Round(y + (0.5 - max[lane][i] * 0.5) * height), y, y + height - 1);
                        bottom[i] = Math.Clamp(Math.Round(y + (0.5 - min[lane][i] * 0.5) * height), top[i] + 1, y + height);
                    }
                    int k = 0;
                    while (k < columns) {
                        if (float.IsNaN(max[lane][k])) {
                            ++k;
                            continue;
                        }
                        int runStart = k;
                        while (k < columns && !float.IsNaN(max[lane][k])) {
                            ++k;
                        }
                        ctx.BeginFigure(new Point(runStart, top[runStart]), true);
                        for (int i = runStart; i < k; ++i) {
                            ctx.LineTo(new Point(i, top[i]));
                            ctx.LineTo(new Point(i + 1, top[i]));
                        }
                        for (int i = k - 1; i >= runStart; --i) {
                            ctx.LineTo(new Point(i + 1, bottom[i]));
                            ctx.LineTo(new Point(i, bottom[i]));
                        }
                        ctx.EndFigure(true);
                    }
                }
            }
            geometry = g;
        }
    }
}
