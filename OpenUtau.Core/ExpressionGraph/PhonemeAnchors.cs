using System;
using System.Collections.Generic;

namespace OpenUtau.Core.ExpressionGraph {
    public enum AnchorInterpolation { Step, Linear, Cubic }

    /// <summary>
    /// Per-phoneme expression values anchored at each phoneme's position, and read between phonemes
    /// by step, linear or cubic interpolation. Every mode passes exactly through the anchor values.
    /// </summary>
    public sealed class PhonemeAnchors {
        public static readonly PhonemeAnchors Empty = new PhonemeAnchors(Array.Empty<int>(), new Dictionary<string, float[]>());

        /// <summary>Part-relative phoneme positions, in phoneme order.</summary>
        public readonly int[] Ticks;
        readonly Dictionary<string, float[]> values;
        // Per expression: the anchors with distinct ticks (the last phoneme wins) and their cubic slopes.
        readonly Dictionary<string, (int[] xs, float[] ys, double[] slopes)> series = new Dictionary<string, (int[], float[], double[])>();

        public PhonemeAnchors(int[] ticks, Dictionary<string, float[]> values) {
            Ticks = ticks;
            this.values = values;
        }

        public bool Has(string abbr) => values.ContainsKey(abbr);

        /// <summary>The value of phoneme <paramref name="index"/>, exactly as the snapshot resolved it.</summary>
        public float At(string? abbr, int index) =>
            abbr != null && values.TryGetValue(abbr, out var ys) ? ys[index] : 0;

        /// <summary>The value at a part-relative tick, held flat before the first and after the last phoneme.</summary>
        public float Sample(string? abbr, int tick, AnchorInterpolation mode) {
            if (abbr == null || !values.ContainsKey(abbr) || Ticks.Length == 0) {
                return 0;
            }
            var (xs, ys, slopes) = Series(abbr);
            if (tick <= xs[0]) {
                return ys[0];
            }
            if (tick >= xs[^1]) {
                return ys[^1];
            }
            // The last anchor at or before the tick.
            int k = Array.BinarySearch(xs, tick);
            if (k >= 0) {
                return ys[k];
            }
            k = ~k - 1;
            switch (mode) {
                case AnchorInterpolation.Linear:
                    return (float)(ys[k] + (double)(tick - xs[k]) * (ys[k + 1] - ys[k]) / (xs[k + 1] - xs[k]));
                case AnchorInterpolation.Cubic: {
                        double h = xs[k + 1] - xs[k];
                        double t = (tick - xs[k]) / h;
                        double t2 = t * t, t3 = t2 * t;
                        return (float)((2 * t3 - 3 * t2 + 1) * ys[k] + (t3 - 2 * t2 + t) * h * slopes[k]
                            + (-2 * t3 + 3 * t2) * ys[k + 1] + (t3 - t2) * h * slopes[k + 1]);
                    }
                default:
                    return ys[k];
            }
        }

        (int[] xs, float[] ys, double[] slopes) Series(string abbr) {
            if (series.TryGetValue(abbr, out var cached)) {
                return cached;
            }
            var all = values[abbr];
            var xs = new List<int>();
            var ys = new List<float>();
            for (int i = 0; i < Ticks.Length; ++i) {
                if (xs.Count > 0 && xs[^1] == Ticks[i]) {
                    ys[^1] = all[i];
                } else {
                    xs.Add(Ticks[i]);
                    ys.Add(all[i]);
                }
            }
            var result = (xs.ToArray(), ys.ToArray(), MonotoneSlopes(xs, ys));
            series[abbr] = result;
            return result;
        }

        /// <summary>Fritsch–Carlson slopes: the cubic never overshoots between two anchors.</summary>
        static double[] MonotoneSlopes(List<int> xs, List<float> ys) {
            int n = xs.Count;
            var m = new double[n];
            if (n < 2) {
                return m;
            }
            var d = new double[n - 1];
            for (int k = 0; k < n - 1; ++k) {
                d[k] = (ys[k + 1] - ys[k]) / (double)(xs[k + 1] - xs[k]);
            }
            m[0] = d[0];
            m[n - 1] = d[n - 2];
            for (int k = 1; k < n - 1; ++k) {
                m[k] = d[k - 1] * d[k] <= 0 ? 0 : (d[k - 1] + d[k]) / 2;
            }
            for (int k = 0; k < n - 1; ++k) {
                if (d[k] == 0) {
                    m[k] = 0;
                    m[k + 1] = 0;
                    continue;
                }
                double a = m[k] / d[k];
                double b = m[k + 1] / d[k];
                double s = a * a + b * b;
                if (s > 9) {
                    double t = 3 / Math.Sqrt(s);
                    m[k] = t * a * d[k];
                    m[k + 1] = t * b * d[k];
                }
            }
            return m;
        }
    }
}
