using System;
using System.Linq;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// Turns a voicebank's tunable parameters into the per-frame values its models
    /// take, one array per parameter, named by the parameter.
    /// </summary>
    internal static class DiffSingerParamInputs {
        /// <summary>An acoustic-only parameter: the drawn curve converted to its input value.</summary>
        public static float[] Acoustic(
            RenderPhrase phrase, DsParam param,
            double frameMs, int totalFrames, int headFrames, int tailFrames) {
            return DiffSingerUtils.SampleCurve(
                    phrase, param.CurveOf(phrase) ?? Array.Empty<float>(),
                    param.AcousticValue(param.Descriptor.defaultValue),
                    frameMs, totalFrames, headFrames, tailFrames,
                    x => param.AcousticValue((float)x))
                .Select(f => (float)f)
                .ToArray();
        }

        /// <summary>
        /// A variance parameter: its prediction resampled onto the phrase's frame grid,
        /// combined with the drawn curve and clamped to the parameter's range.
        /// </summary>
        public static float[] Variance(
            RenderPhrase phrase, DsParam param, float[] predicted, VarianceResult result,
            double frameMs, int totalFrames, int headFrames, int tailFrames) {
            var resampled = DiffSingerUtils.ResamplePaddedCurve(
                predicted, totalFrames,
                result.headFrames, result.tailFrames,
                headFrames, tailFrames,
                result.frameMs, (float)frameMs);
            var user = DiffSingerUtils.SampleCurve(
                phrase, param.CurveOf(phrase) ?? Array.Empty<float>(), param.NeutralCurve,
                frameMs, totalFrames, headFrames, tailFrames,
                x => x);
            return resampled
                .Zip(user, (p, u) => param.CombinedValue(p, (float)u))
                .ToArray();
        }
    }
}
