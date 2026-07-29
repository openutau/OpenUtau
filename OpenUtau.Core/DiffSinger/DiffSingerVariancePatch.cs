using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core.DiffSinger {
    internal readonly struct VariancePatchRange {
        public readonly int start;
        public readonly int end;

        public VariancePatchRange(int start, int end) {
            this.start = start;
            this.end = end;
        }
    }

    internal class VariancePatchState {
        public readonly float[] pitch;
        public readonly VarianceResult result;

        public VariancePatchState(float[] pitch, VarianceResult result) {
            this.pitch = pitch.ToArray();
            this.result = DiffSingerVariancePatch.CloneResult(result);
        }
    }

    internal static class DiffSingerVariancePatch {
        const float PitchEpsilon = 1e-4f;
        const float CrossfadeMs = 50f;

        public static ulong BuildStateKey(ulong baseHash, int phrasePosition, int phraseEnd) {
            unchecked {
                ulong hash = baseHash;
                hash = (hash ^ (uint)phrasePosition) * 1099511628211UL;
                hash = (hash ^ (uint)phraseEnd) * 1099511628211UL;
                return hash;
            }
        }

        public static VarianceResult Merge(
            VariancePatchState? previous,
            float[] currentPitch,
            VarianceResult current) {
            if (previous == null ||
                previous.pitch.Length != currentPitch.Length ||
                !IsMetadataCompatible(previous.result, current)) {
                return CloneResult(current);
            }
            var ranges = FindChangedRanges(previous.pitch, currentPitch, PitchEpsilon);
            if (ranges.Count == 0) {
                return CloneResult(previous.result);
            }
            int crossfadeFrames = Math.Clamp((int)Math.Round(CrossfadeMs / current.frameMs), 1, 20);
            var weights = BuildWeights(currentPitch.Length, ranges, crossfadeFrames);
            return new VarianceResult {
                energy = Blend(previous.result.energy, current.energy, weights),
                breathiness = Blend(previous.result.breathiness, current.breathiness, weights),
                voicing = Blend(previous.result.voicing, current.voicing, weights),
                tension = Blend(previous.result.tension, current.tension, weights),
                frameMs = current.frameMs,
                headFrames = current.headFrames,
                tailFrames = current.tailFrames,
                totalFrames = current.totalFrames,
            };
        }

        internal static List<VariancePatchRange> FindChangedRanges(
            IReadOnlyList<float> previousPitch,
            IReadOnlyList<float> currentPitch,
            float epsilon) {
            var ranges = new List<VariancePatchRange>();
            int length = Math.Min(previousPitch.Count, currentPitch.Count);
            int start = -1;
            for (int i = 0; i < length; ++i) {
                bool changed = Math.Abs(previousPitch[i] - currentPitch[i]) > epsilon;
                if (changed && start < 0) {
                    start = i;
                } else if (!changed && start >= 0) {
                    ranges.Add(new VariancePatchRange(start, i));
                    start = -1;
                }
            }
            if (start >= 0) {
                ranges.Add(new VariancePatchRange(start, length));
            }
            return ranges;
        }

        internal static float[] BuildWeights(int length, IReadOnlyList<VariancePatchRange> ranges, int crossfadeFrames) {
            var weights = new float[length];
            foreach (var range in ranges) {
                int start = Math.Clamp(range.start, 0, length);
                int end = Math.Clamp(range.end, start, length);
                for (int i = start; i < end; ++i) {
                    weights[i] = 1f;
                }
                int leftStart = Math.Max(0, start - crossfadeFrames);
                int leftLength = start - leftStart;
                for (int i = leftStart; i < start; ++i) {
                    float weight = (float)(i - leftStart + 1) / (leftLength + 1);
                    weights[i] = Math.Max(weights[i], weight);
                }
                int rightEnd = Math.Min(length, end + crossfadeFrames);
                int rightLength = rightEnd - end;
                for (int i = end; i < rightEnd; ++i) {
                    float weight = 1f - (float)(i - end + 1) / (rightLength + 1);
                    weights[i] = Math.Max(weights[i], weight);
                }
            }
            return weights;
        }

        internal static float[]? Blend(float[]? previous, float[]? current, IReadOnlyList<float> weights) {
            if (previous == null || current == null || previous.Length != current.Length || previous.Length != weights.Count) {
                return current?.ToArray();
            }
            var result = new float[current.Length];
            for (int i = 0; i < result.Length; ++i) {
                result[i] = previous[i] * (1f - weights[i]) + current[i] * weights[i];
            }
            return result;
        }

        internal static VarianceResult CloneResult(VarianceResult result) {
            return new VarianceResult {
                energy = result.energy?.ToArray(),
                breathiness = result.breathiness?.ToArray(),
                voicing = result.voicing?.ToArray(),
                tension = result.tension?.ToArray(),
                frameMs = result.frameMs,
                headFrames = result.headFrames,
                tailFrames = result.tailFrames,
                totalFrames = result.totalFrames,
            };
        }

        static bool IsMetadataCompatible(VarianceResult previous, VarianceResult current) {
            return previous.totalFrames == current.totalFrames &&
                previous.headFrames == current.headFrames &&
                previous.tailFrames == current.tailFrames &&
                Math.Abs(previous.frameMs - current.frameMs) < 1e-4f;
        }
    }
}
