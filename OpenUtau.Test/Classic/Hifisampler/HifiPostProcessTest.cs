using System;
using System.Linq;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Classic.Hifisampler {
    public class HifiPostProcessTest {
        static float[] Constant(float value, double seconds) => Enumerable.Repeat(value, (int)(seconds * 44100)).ToArray();

        static float[] Apply(float[] render, int? p, double wavMax, Func<double> voicedRatio, double lengthReq) {
            var flags = new HifiFlags(p.HasValue
                ? new[] { Tuple.Create<string, int?, string>("P", p, "norm") }
                : Array.Empty<Tuple<string, int?, string>>());
            return HifiPostProcess.Apply(render, 1.0, flags, new HifiSamplerConfig(), new double[] { 60, 60 },
                new double[] { 0, 0.01 }, 0, lengthReq, lengthReq, 100, null, 120, wavMax, voicedRatio);
        }

        [Fact]
        public void AutoGainFallsBackToTheSourcePeakWhenUnvoiced() {
            // A quiet note cut from a loud recording: voiced, it is leveled by its own peak;
            // unvoiced (a consonant), by the recording's.
            double voiced = Worldline.ResamplerAutoGain(0.05, 0.8, 1, 86);
            double unvoiced = Worldline.ResamplerAutoGain(0.05, 0.8, 0, 86);
            Assert.InRange(voiced / Math.Pow(0.5 / 0.05, 0.86), 0.9, 1.0);
            Assert.InRange(unvoiced / Math.Pow(0.5 / 0.8, 0.86), 1.0, 1.01);
            Assert.Equal(1.0, Worldline.ResamplerAutoGain(0.05, 0.8, 1, 0));
        }

        [Fact]
        public void PIsWorldlineAutoGainOnTheKeptPart() {
            // 300 ms kept, then a loud tail the wavtool drops, which the gain ignores.
            var render = Constant(0.1f, 0.5);
            for (int i = (int)(0.3 * 44100); i < render.Length; i++) {
                render[i] = 0.9f;
            }
            var y = Apply(render, 86, 0.4, () => 0.7, 0.3);
            Assert.Equal(0.1 * Worldline.ResamplerAutoGain(0.1, 0.4, 0.7, 86), y[0], 5);
        }

        [Fact]
        public void MissingPIs86AndP0KeepsTheLevel() {
            var none = Apply(Constant(0.1f, 0.3), null, 0.4, () => 1, 0.3);
            var p86 = Apply(Constant(0.1f, 0.3), 86, 0.4, () => 1, 0.3);
            Assert.Equal(p86, none);
            // P0 is no gain, without analyzing the source f0.
            var p0 = Apply(Constant(0.1f, 0.3), 0, 0.4, () => throw new InvalidOperationException(), 0.3);
            Assert.Equal(0.1f, p0[0]);
        }
    }
}
