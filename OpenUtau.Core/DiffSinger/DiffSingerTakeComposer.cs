using System;
using System.Collections.Generic;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// Pure frame-domain math for take compositing: which frames changed, how the
    /// old and new results are blended, and how timeline ranges map to frames.
    /// Kept free of ONNX and file IO so it can be unit tested.
    /// </summary>
    internal static class DiffSingerTakeComposer {
        /// <summary>Below this the two floats are the same conditioning.</summary>
        public const float ConditionEpsilon = 1e-4f;
        /// <summary>f0 is in Hz; a hundredth of a cent at 1 kHz is still far below this.</summary>
        public const float F0Epsilon = 1e-3f;

        public static float EpsilonFor(string conditionName) {
            return conditionName == "f0" ? F0Epsilon : ConditionEpsilon;
        }

        /// <summary>
        /// Frames whose acoustic conditioning differs from the take. <paramref name="previous"/>
        /// and <paramref name="current"/> must have passed <see cref="DiffSingerTake.IsCompatible"/>.
        /// </summary>
        public static bool[] DiffConditions(
                IReadOnlyList<DiffSingerTakeCondition> previous,
                IReadOnlyList<DiffSingerTakeCondition> current,
                int totalFrames) {
            var changed = new bool[totalFrames];
            for (int c = 0; c < previous.Count && c < current.Count; c++) {
                var a = previous[c];
                var b = current[c];
                float epsilon = EpsilonFor(a.Name);
                bool[] mask = a.ValuesPerFrame <= 1
                    ? DiffSingerVariancePatch.BuildChangedFrameMask(a.Values, b.Values, epsilon)
                    : DiffSingerVariancePatch.BuildChangedFrameMask(
                        a.Values, b.Values, totalFrames, epsilon);
                for (int i = 0; i < totalFrames && i < mask.Length; i++) {
                    changed[i] |= mask[i];
                }
            }
            return changed;
        }

        /// <summary>
        /// Turns the changed-frame mask into per-frame "use the new take" weights.
        /// Changed frames are 1; the fade window outside them ramps linearly to 0,
        /// which is where the two takes are cross-faded. Never returns NaN.
        /// </summary>
        public static float[] BuildWeights(IReadOnlyList<bool> changed, int fadeFrames, int totalFrames) {
            var weights = new float[totalFrames];
            for (int i = 0; i < totalFrames; i++) {
                weights[i] = i < changed.Count && changed[i] ? 1f : 0f;
            }
            if (fadeFrames <= 0) {
                return weights;
            }
            int index = 0;
            while (index < totalFrames) {
                if (weights[index] < 1f) {
                    index++;
                    continue;
                }
                int start = index;
                while (index < totalFrames && weights[index] >= 1f) {
                    index++;
                }
                int end = index;
                for (int k = 1; k <= fadeFrames; k++) {
                    float w = 1f - (float)k / fadeFrames;
                    int left = start - k;
                    if (left >= 0) {
                        weights[left] = Math.Max(weights[left], w);
                    }
                    int right = end - 1 + k;
                    if (right < totalFrames) {
                        weights[right] = Math.Max(weights[right], w);
                    }
                }
            }
            return weights;
        }

        /// <summary>
        /// Protected ranges win over everything: their frames keep the old take, and
        /// the cross-fade ramps out of the protected range on both sides.
        /// </summary>
        public static void ApplyProtect(float[] weights, IReadOnlyList<(int start, int end)> ranges, int fadeFrames) {
            int totalFrames = weights.Length;
            foreach (var (rawStart, rawEnd) in ranges) {
                int start = Math.Clamp(rawStart, 0, totalFrames);
                int end = Math.Clamp(rawEnd, 0, totalFrames);
                for (int i = start; i < end; i++) {
                    weights[i] = 0f;
                }
                if (fadeFrames <= 0) {
                    continue;
                }
                for (int k = 0; k < fadeFrames; k++) {
                    float w = (float)k / fadeFrames;
                    int left = start - 1 - k;
                    if (left >= 0) {
                        weights[left] = Math.Min(weights[left], w);
                    }
                    int right = end + k;
                    if (right < totalFrames) {
                        weights[right] = Math.Min(weights[right], w);
                    }
                }
            }
        }

        public static bool IsAllOld(IReadOnlyList<float> weights) {
            for (int i = 0; i < weights.Count; i++) {
                if (weights[i] > 0f) {
                    return false;
                }
            }
            return true;
        }

        public static bool IsAllNew(IReadOnlyList<float> weights) {
            for (int i = 0; i < weights.Count; i++) {
                if (weights[i] < 1f) {
                    return false;
                }
            }
            return true;
        }

        public static float[] BlendMel(
                float[] previous, float[] current, IReadOnlyList<float> weights, int totalFrames, int bins) {
            if (weights.Count != totalFrames) {
                return current;
            }
            var result = new float[totalFrames * bins];
            for (int t = 0; t < totalFrames; t++) {
                float w = weights[t];
                int offset = t * bins;
                if (w <= 0f) {
                    Array.Copy(previous, offset, result, offset, bins);
                } else if (w >= 1f) {
                    Array.Copy(current, offset, result, offset, bins);
                } else {
                    float inv = 1f - w;
                    for (int c = 0; c < bins; c++) {
                        result[offset + c] = previous[offset + c] * inv + current[offset + c] * w;
                    }
                }
            }
            return result;
        }

        /// <summary>Blends in log-frequency so the cross-fade does not bend the pitch.</summary>
        public static float[] BlendF0(
                float[] previous, float[] current, IReadOnlyList<float> weights, int totalFrames) {
            if (weights.Count != totalFrames || previous.Length != totalFrames || current.Length != totalFrames) {
                return current;
            }
            var result = new float[totalFrames];
            for (int t = 0; t < totalFrames; t++) {
                float w = weights[t];
                if (w <= 0f) {
                    result[t] = previous[t];
                } else if (w >= 1f) {
                    result[t] = current[t];
                } else {
                    double a = Math.Log(Math.Max(previous[t], 1e-3f));
                    double b = Math.Log(Math.Max(current[t], 1e-3f));
                    result[t] = (float)Math.Exp(a + (b - a) * w);
                }
            }
            return result;
        }

        /// <summary>
        /// Maps absolute tick ranges onto this phrase's frame grid, clipped to it.
        /// </summary>
        public static List<(int start, int end)> ToFrameRanges(
                IReadOnlyList<(int startTick, int endTick)> tickRanges,
                TimeAxis axis,
                double phraseStartMs,
                float frameMs,
                int totalFrames) {
            var result = new List<(int, int)>();
            if (tickRanges == null || tickRanges.Count == 0 || frameMs <= 0) {
                return result;
            }
            foreach (var (startTick, endTick) in tickRanges) {
                if (endTick <= startTick) {
                    continue;
                }
                int start = FrameOf(axis.TickPosToMsPos(startTick), phraseStartMs, frameMs);
                int end = FrameOf(axis.TickPosToMsPos(endTick), phraseStartMs, frameMs);
                start = Math.Clamp(start, 0, totalFrames);
                end = Math.Clamp(end, 0, totalFrames);
                if (end > start) {
                    result.Add((start, end));
                }
            }
            return result;
        }

        static int FrameOf(double ms, double phraseStartMs, float frameMs) {
            return (int)Math.Round((ms - phraseStartMs) / frameMs);
        }
    }
}
