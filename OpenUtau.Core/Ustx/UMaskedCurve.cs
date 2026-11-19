using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    /// <summary>Consecutive values of a masked curve, one per grid step from tick <see cref="x"/>.</summary>
    public class UMaskedRun {
        /// <summary>Part-relative tick of the first value, on the grid.</summary>
        public int x;
        public float[] ys = Array.Empty<float>();

        [YamlIgnore] public int End => x + (ys.Length - 1) * UMaskedCurve.interval;

        public UMaskedRun Clone() => new UMaskedRun { x = x, ys = ys.ToArray() };
    }

    /// <summary>
    /// A curve that has no value over some stretches, unlike a curve, which always has at least its default.
    /// Values are float32 on the same 5-tick grid as curves, linear within a run; outside every run there is
    /// no value. Only expression graphs read it.
    /// </summary>
    public class UMaskedCurve {
        public const int interval = UCurve.interval;

        public string abbr = string.Empty;
        public List<UMaskedRun> runs = new List<UMaskedRun>();

        public UMaskedCurve() { }
        public UMaskedCurve(string abbr) {
            this.abbr = abbr;
        }

        [YamlIgnore] public bool IsEmpty => runs.All(r => r.ys.Length == 0);

        public UMaskedCurve Clone() => new UMaskedCurve {
            abbr = abbr,
            runs = runs.Select(r => r.Clone()).ToList(),
        };

        public static int Snap(double tick) => (int)Math.Round(tick / interval) * interval;

        /// <summary>The value at a part-relative tick, linear between grid steps, if a run covers it.</summary>
        public bool TrySample(double tick, out float value) => TrySample(runs, tick, out value);

        internal static bool TrySample(IReadOnlyList<UMaskedRun> runs, double tick, out float value) {
            value = 0;
            // The last run starting at or before the tick; runs are sorted and don't overlap.
            int lo = 0, hi = runs.Count - 1, found = -1;
            while (lo <= hi) {
                int mid = (lo + hi) / 2;
                if (runs[mid].x <= tick) {
                    found = mid;
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }
            if (found < 0 || runs[found].ys.Length == 0 || tick > runs[found].End) {
                return false;
            }
            var run = runs[found];
            double position = (tick - run.x) / interval;
            int i = Math.Min((int)Math.Floor(position), run.ys.Length - 1);
            value = i == run.ys.Length - 1
                ? run.ys[i]
                : (float)(run.ys[i] + (position - i) * (run.ys[i + 1] - run.ys[i]));
            return true;
        }

        /// <summary>The values by grid tick.</summary>
        public SortedDictionary<int, float> ToSteps() {
            var steps = new SortedDictionary<int, float>();
            foreach (var run in runs) {
                for (int i = 0; i < run.ys.Length; ++i) {
                    steps[run.x + i * interval] = run.ys[i];
                }
            }
            return steps;
        }

        /// <summary>Rebuilds the runs from values by grid tick: consecutive steps form a run.</summary>
        public void SetSteps(SortedDictionary<int, float> steps) {
            runs.Clear();
            UMaskedRun? run = null;
            var values = new List<float>();
            foreach (var (x, y) in steps) {
                if (run == null || x != run.x + values.Count * interval) {
                    if (run != null) {
                        run.ys = values.ToArray();
                        runs.Add(run);
                    }
                    run = new UMaskedRun { x = x };
                    values.Clear();
                }
                values.Add(y);
            }
            if (run != null) {
                run.ys = values.ToArray();
                runs.Add(run);
            }
        }

        /// <summary>Draws a straight line of values from one tick to another, replacing what was there.</summary>
        public void Set(int x0, float y0, int x1, float y1) {
            x0 = Snap(x0);
            x1 = Snap(x1);
            if (x1 < x0) {
                (x0, y0, x1, y1) = (x1, y1, x0, y0);
            }
            var steps = ToSteps();
            for (int x = x0; x <= x1; x += interval) {
                steps[x] = x1 == x0 ? y1 : y0 + (y1 - y0) * (x - x0) / (float)(x1 - x0);
            }
            SetSteps(steps);
        }

        /// <summary>Sets the given values, replacing what was at their ticks.</summary>
        public void SetValues(IEnumerable<(int x, float y)> values) {
            var steps = ToSteps();
            foreach (var (x, y) in values) {
                steps[Snap(x)] = y;
            }
            SetSteps(steps);
        }

        /// <summary>Removes the values from one tick to another, both included.</summary>
        public void Clear(int x0, int x1) {
            x0 = Snap(x0);
            x1 = Snap(x1);
            if (x1 < x0) {
                (x0, x1) = (x1, x0);
            }
            var steps = ToSteps();
            foreach (var x in steps.Keys.Where(x => x >= x0 && x <= x1).ToList()) {
                steps.Remove(x);
            }
            SetSteps(steps);
        }
    }
}
