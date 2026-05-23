using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SharpCompress;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public class UCurve {
        public const int interval = 5;

        [YamlIgnore] public UExpressionDescriptor descriptor;
        public List<int> xs = new List<int>();
        public List<int> ys = new List<int>();
        [YamlIgnore] public List<int> realXs = new List<int>();
        [YamlIgnore] public List<int> realYs = new List<int>();
        public string abbr;

        [YamlIgnore] public bool IsEmpty => xs.Count == 0 || ys.All(y => y == 0);

        public UCurve(UExpressionDescriptor descriptor) {
            Trace.Assert(descriptor != null);
            this.descriptor = descriptor;
            abbr = descriptor.abbr;
        }

        public UCurve() { }

        public UCurve(string abbr) {
            this.abbr = abbr;
        }

        public UCurve Clone() {
            return new UCurve(descriptor) {
                xs = xs.ToList(),
                ys = ys.ToList(),
            };
        }

        public bool IsEmptyBetween(int x0, int x1, int defaultValue) {
            if (Sample(x0) != defaultValue || Sample(x1) != defaultValue) {
                return false;
            }
            int idx = xs.BinarySearch(x0);
            if (idx < 0) {
                idx = ~idx;
            }
            while (idx < xs.Count && xs[idx] <= x1) {
                if (ys[idx] != defaultValue) {
                    return false;
                }
                idx++;
            }
            return true;
        }

        public int Sample(int x) {
            int idx = xs.BinarySearch(x);
            if (idx >= 0) {
                return ys[idx];
            }
            idx = ~idx;
            if (idx > 0 && idx < xs.Count) {
                return (int)Math.Round(MusicMath.Linear(xs[idx - 1], xs[idx], ys[idx - 1], ys[idx], x));
            }
            return (int)descriptor.defaultValue;
        }

        private void Insert(int x, int y) {
            int idx = xs.BinarySearch(x);
            if (idx >= 0) {
                ys[idx] = y;
                return;
            }
            idx = ~idx;
            xs.Insert(idx, x);
            ys.Insert(idx, y);
        }

        public void Set(int x, int y, int lastX, int lastY) {
            x = (int)Math.Round((float)x / interval) * interval;
            lastX = (int)Math.Round((float)lastX / interval) * interval;
            if (x == lastX) {
                int leftY = Sample(x - interval);
                int rightY = Sample(x + interval);
                Insert(x - interval, leftY);
                Insert(x, y);
                Insert(x + interval, rightY);
            } else if (x < lastX) {
                int leftY = Sample(x - interval);
                DeleteBetweenExclusive(x, lastX);
                Insert(x - interval, leftY);
                Insert(x, y);
            } else {
                int rightY = Sample(x + interval);
                DeleteBetweenExclusive(lastX, x);
                Insert(x, y);
                Insert(x + interval, rightY);
            }
        }

        private void DeleteBetweenExclusive(int x1, int x2) {
            int li = xs.BinarySearch(x1);
            if (li >= 0) {
                li++;
            } else {
                li = ~li;
            }
            int ri = xs.BinarySearch(x2);
            if (ri >= 0) {
                ri--;
            } else {
                ri = ~ri - 1;
            }
            if (ri >= li) {
                xs.RemoveRange(li, ri - li + 1);
                ys.RemoveRange(li, ri - li + 1);
            }
        }
        public void Simplify() {
            if (xs == null || xs.Count < 3) {
                return;
            }
            int first = 0;
            int last = xs.Count - 1;
            var toKeep = new List<int>() { first, last };
            double tolerance = Math.Min(5, (descriptor.max - descriptor.min) * 0.005);
            Simplify(first, last, tolerance, toKeep);
            toKeep.Sort();
            var newXs = new List<int>();
            var newYs = new List<int>();
            foreach (int index in toKeep) {
                newXs.Add(xs[index]);
                newYs.Add(ys[index]);
            }
            xs = newXs;
            ys = newYs;
        }

        public void Simplify(int first, int last, double tolerance, List<int> toKeep) {
            double maxHeight = 0;
            int maxHeightIdx = 0;
            for (int index = first; index < last; index++) {
                double height = PerpendicularDistance(
                    xs[first], ys[first], xs[last], ys[last], xs[index], ys[index]);
                if (height > maxHeight) {
                    maxHeight = height;
                    maxHeightIdx = index;
                }
            }
            if (maxHeight > tolerance && maxHeightIdx != 0) {
                toKeep.Add(maxHeightIdx);
                Simplify(first, maxHeightIdx, tolerance, toKeep);
                Simplify(maxHeightIdx, last, tolerance, toKeep);
            }
        }

        private double PerpendicularDistance(int x, int y, int x1, int y1, int x2, int y2) {
            double area = 0.5 * Math.Abs(x1 * (y2 - y) + x2 * (y - y1) + x * (y1 - y2));
            double bottom = Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
            return area / bottom * 2;
        }
        public static List<UCurve> MergeCurves(params List<UCurve>[] merging) {
            var merged = new Dictionary<UExpressionDescriptor, UCurve>();
            merging.ForEach(curves => {
                foreach (var curve in curves) {
                    if (curve.descriptor == null) continue;
                    if (!merged.TryGetValue(curve.descriptor, out var existing)) {
                        merged[curve.descriptor] = curve.Clone();
                    } else {
                        // Merge xs and ys, keeping them sorted by xs
                        var xs = existing.xs.Concat(curve.xs).ToList();
                        var ys = existing.ys.Concat(curve.ys).ToList();
                        var zipped = xs.Zip(ys, (x, y) => (x, y)).ToList();
                        zipped.Sort((a, b) => a.x.CompareTo(b.x));
                        existing.xs = zipped.Select(z => z.x).ToList();
                        existing.ys = zipped.Select(z => z.y).ToList();
                    }
                }
            });
            return merged.Values.ToList();
        }
    }

    public class CurveSelection {
        public string? Abbr { get; private set; }
        public (int x, int y) StartPoint { get; set; } = (0, 0);
        public (int x, int y) EndPoint { get; set; } = (0, 0);
        private List<int> xs = new List<int>(); // tick from part start
        private List<int> ys = new List<int>();

        public CurveSelection() { }

        public bool HasValue(string? abbr = null) {
            return Abbr != null && (abbr == null || Abbr == abbr);
        }

        public void Clear() {
            Abbr = null;
            StartPoint = (0, 0);
            EndPoint = (0, 0);
            xs.Clear();
            ys.Clear();
        }

        public void Add (string abbr, (int x, int y) startPoint, (int x, int y) endPoint, IEnumerable<int> xs, IEnumerable<int> ys) {
            Abbr = abbr;
            StartPoint = startPoint;
            EndPoint = endPoint;
            this.xs.AddRange(xs);
            this.ys.AddRange(ys);
        }

        public void GetWholeCurveAndSelection(string abbr, UCurve? curve, out List<int> wholeXs, out List<int> wholeYs) {
            wholeXs = new List<int>();
            wholeYs = new List<int>();
            if (curve != null) {
                wholeXs.AddRange(curve.xs);
                wholeYs.AddRange(curve.ys);
            }
            if (HasValue(abbr)) {
                bool flag = false;
                for (int i = 0; i < wholeXs.Count; i++) {
                    int x = wholeXs[i];
                    if (StartPoint.x < x) {
                        wholeXs.Insert(i, StartPoint.x);
                        wholeYs.Insert(i, StartPoint.y);
                        flag = true;
                        break;
                    }
                }
                if (!flag) {
                    wholeXs.Add(StartPoint.x);
                    wholeYs.Add(StartPoint.y);
                }

                if (StartPoint.x != EndPoint.x) {
                    flag = false;
                    for (int i = 0; i < wholeXs.Count; i++) {
                        int x = wholeXs[i];
                        if (EndPoint.x < x) {
                            wholeXs.Insert(i, EndPoint.x);
                            wholeYs.Insert(i, EndPoint.y);
                            flag = true;
                            break;
                        }
                    }
                    if (!flag) {
                        wholeXs.Add(EndPoint.x);
                        wholeYs.Add(EndPoint.y);
                    }
                }
            }
        }

        public void GetSelectedRange(string abbr, out List<int> xs, out List<int> ys) {
            xs = new List<int>();
            ys = new List<int>();
            if (!HasValue(abbr)) {
                return;
            }
            xs.Add(StartPoint.x);
            ys.Add(StartPoint.y);
            xs.AddRange(this.xs);
            ys.AddRange(this.ys);
            xs.Add(EndPoint.x);
            ys.Add(EndPoint.y);
        }

        public CurveSelection Clone() {
            return new CurveSelection() {
                Abbr = Abbr,
                StartPoint = StartPoint,
                EndPoint = EndPoint,
                xs = new List<int>(xs),
                ys = new List<int>(ys)
            };
        }
    }
}
