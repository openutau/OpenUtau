using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    public readonly struct RealCurveUpdate {
        public readonly string abbr;
        public readonly ulong phraseHash;
        public readonly int startTick;
        public readonly int endTick;
        public readonly int[] xs;
        public readonly int[] ys;

        public RealCurveUpdate(string abbr, ulong phraseHash, int startTick, int endTick, int[] xs, int[] ys) {
            this.abbr = abbr;
            this.phraseHash = phraseHash;
            this.startTick = startTick;
            this.endTick = endTick;
            this.xs = xs;
            this.ys = ys;
        }

        public bool IsValid => !string.IsNullOrEmpty(abbr) &&
            endTick >= startTick && xs.Length == ys.Length && xs.Length > 0;
    }

    public static class RealCurveUpdater {
        public static RealCurveUpdate[] LoadPhraseUpdates(UVoicePart part, RenderPhrase phrase) {
            if (!phrase.renderer.SupportsRealCurve) {
                return Array.Empty<RealCurveUpdate>();
            }
            return BuildUpdates(part, phrase, phrase.renderer.LoadRenderedRealCurves(phrase));
        }

        internal static RealCurveUpdate[] BuildUpdates(
            UVoicePart part,
            RenderPhrase phrase,
            IEnumerable<RenderRealCurveResult> results) {
            return BuildUpdates(part.position, phrase.position, phrase.hash, results);
        }

        internal static RealCurveUpdate[] BuildUpdates(
            int partPosition,
            int phrasePosition,
            ulong phraseHash,
            IEnumerable<RenderRealCurveResult> results) {
            var updates = new List<RealCurveUpdate>();
            foreach (var result in results) {
                if (string.IsNullOrEmpty(result.abbr) || result.ticks == null || result.values == null) {
                    continue;
                }
                int count = Math.Min(result.ticks.Length, result.values.Length);
                if (count == 0) {
                    continue;
                }
                var ticks = result.ticks
                    .Take(count)
                    .Select(tick => phrasePosition - partPosition + (int)tick)
                    .ToArray();
                var values = result.values
                    .Take(count)
                    .Select(value => (int)(value * 1000.0))
                    .ToArray();
                int startTick = ticks.Min();
                int endTick = ticks.Max();
                var xs = new List<int>(count + 1) { ticks[0] };
                var ys = new List<int>(count + 1) { -1 };
                xs.AddRange(ticks);
                ys.AddRange(values);
                updates.Add(new RealCurveUpdate(
                    result.abbr,
                    phraseHash,
                    startTick,
                    endTick,
                    xs.ToArray(),
                    ys.ToArray()));
            }
            return updates.ToArray();
        }

        public static bool Apply(UProject project, UVoicePart part, IReadOnlyList<RealCurveUpdate> updates) {
            if (updates.Count == 0 || !project.parts.Contains(part)) {
                return false;
            }
            var phraseHashes = part.renderPhrases.Select(phrase => phrase.hash).ToHashSet();
            return Apply(project, part, updates, phraseHashes);
        }

        // Removes realXs/realYs points outside the union of `ranges` (the tick spans the renderer
        // actually produced this pass). Clears ghost real curves left behind when a phrase shrinks,
        // moves, splits, or is deleted. realXs is kept sorted, so a single two-pointer walk decides
        // each point; a new list is allocated only when a stale point is found (clean case is free).
        public static bool TrimToCoverage(
            UProject project, UVoicePart part, IReadOnlyList<(int start, int end)> ranges) {
            if (!project.parts.Contains(part) || ranges.Count == 0) {
                return false;
            }
            var merged = MergeRanges(ranges);
            bool changed = false;
            foreach (var curve in part.curves) {
                if (TrimCurve(curve, merged)) {
                    changed = true;
                }
            }
            return changed;
        }

        static bool TrimCurve(UCurve curve, List<(int start, int end)> merged) {
            var xs = curve.realXs;
            var ys = curve.realYs;
            if (xs.Count == 0) {
                return false;
            }
            List<int>? newXs = null;
            List<int>? newYs = null;
            int ri = 0;
            for (int i = 0; i < xs.Count; ++i) {
                int x = xs[i];
                while (ri < merged.Count && merged[ri].end < x) {
                    ri++;
                }
                bool keep = ri < merged.Count && x >= merged[ri].start;
                if (keep) {
                    newXs?.Add(x);
                    newYs?.Add(ys[i]);
                } else if (newXs == null) {
                    newXs = new List<int>(xs.Count);
                    newYs = new List<int>(ys.Count);
                    for (int j = 0; j < i; ++j) {
                        newXs.Add(xs[j]);
                        newYs.Add(ys[j]);
                    }
                }
            }
            if (newXs == null) {
                return false;
            }
            curve.realXs = newXs;
            curve.realYs = newYs;
            return true;
        }

        internal static List<(int start, int end)> MergeRanges(IReadOnlyList<(int start, int end)> ranges) {
            var sorted = ranges.ToList();
            sorted.Sort((a, b) => a.start.CompareTo(b.start));
            var merged = new List<(int start, int end)> { sorted[0] };
            for (int i = 1; i < sorted.Count; ++i) {
                var last = merged[merged.Count - 1];
                if (sorted[i].start <= last.end) {
                    merged[merged.Count - 1] = (last.start, Math.Max(last.end, sorted[i].end));
                } else {
                    merged.Add(sorted[i]);
                }
            }
            return merged;
        }

        internal static bool Apply(
            UProject project,
            UVoicePart part,
            IReadOnlyList<RealCurveUpdate> updates,
            IReadOnlySet<ulong> phraseHashes) {
            bool changed = false;
            foreach (var update in updates) {
                if (!update.IsValid || !phraseHashes.Contains(update.phraseHash)) {
                    continue;
                }
                changed |= ApplyUpdate(project, part, update);
            }
            return changed;
        }

        private static bool ApplyUpdate(UProject project, UVoicePart part, RealCurveUpdate update) {
            var curve = part.curves.FirstOrDefault(curve => curve.abbr == update.abbr);
            if (curve == null) {
                var track = project.tracks[part.trackNo];
                if (!track.TryGetExpDescriptor(project, update.abbr, out var descriptor)) {
                    return false;
                }
                curve = new UCurve(descriptor);
                part.curves.Add(curve);
            }
            RemoveRange(curve.realXs, curve.realYs, update.startTick, update.endTick);
            InsertRange(curve.realXs, curve.realYs, update.xs, update.ys);
            return true;
        }

        internal static void RemoveRange(List<int> xs, List<int> ys, int startTick, int endTick) {
            for (int i = xs.Count - 1; i >= 0; --i) {
                if (xs[i] >= startTick && xs[i] <= endTick) {
                    xs.RemoveAt(i);
                    ys.RemoveAt(i);
                }
            }
        }

        internal static void InsertRange(List<int> targetXs, List<int> targetYs, int[] xs, int[] ys) {
            if (xs.Length == 0) {
                return;
            }
            int insertIndex = targetXs.BinarySearch(xs[0]);
            if (insertIndex < 0) {
                insertIndex = ~insertIndex;
            } else {
                while (insertIndex < targetXs.Count && targetXs[insertIndex] <= xs[0]) {
                    insertIndex++;
                }
            }
            targetXs.InsertRange(insertIndex, xs);
            targetYs.InsertRange(insertIndex, ys);
        }
    }
}
