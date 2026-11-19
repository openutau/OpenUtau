using System;
using System.Linq;
using OpenUtau.Core.Analysis;
using Xunit;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>Rd tension on a synthetic harmonic part with a known glottal shape.</summary>
    public class HifiRdTensionTest {
        const int Fs = 44100;
        const double F0 = 220;
        const int Harmonics = 40;

        // Harmonics of F0 shaped as an Rd 1 glottal flow through the lips, steady for 1 s.
        static readonly float[] Signal = Enumerable.Range(0, Fs).Select(i => {
            var shape = GlottalRd.FlowShape(1.0, F0, Harmonics);
            double t = i / (double)Fs, sum = 0;
            for (int k = 1; k <= Harmonics; k++) {
                sum += 0.05 * shape[k - 1] * GlottalRd.LipGain(k * F0) * Math.Cos(2 * Math.PI * k * F0 * t);
            }
            return (float)sum;
        }).ToArray();

        static readonly double[] SourceF0 = Enumerable.Repeat(F0, Fs / HifiRdTension.Hop + 2).ToArray();

        /// <summary>Amplitude at hz over the middle half (a single-bin DFT).</summary>
        static double Amplitude(float[] x, double hz) {
            int a = x.Length / 4, n = x.Length / 2;
            double re = 0, im = 0;
            for (int i = a; i < a + n; i++) {
                double w = 2 * Math.PI * hz * i / Fs;
                re += x[i] * Math.Cos(w);
                im -= x[i] * Math.Sin(w);
            }
            return 2 * Math.Sqrt(re * re + im * im) / n;
        }

        static double Db(double ratio) => 20 * Math.Log10(ratio);

        [Fact]
        public void ZeroTensionKeepsTheSignal() {
            var y = HifiRdTension.Apply(Signal, SourceF0, Fs, _ => 0, _ => F0);
            double maxErr = Signal.Zip(y, (a, b) => Math.Abs(a - b)).Max();
            Assert.True(maxErr < 1e-5, $"max error {maxErr}");
        }

        [Fact]
        public void TensionMovesRdOnTheSourceHarmonics() {
            var y = HifiRdTension.Apply(Signal, SourceF0, Fs, _ => 100, _ => F0);
            var expected = GlottalRd.Gains(1.0, 0.5, F0, Harmonics);
            Assert.InRange(Db(Amplitude(y, F0) / Amplitude(Signal, F0)), -0.5, 0.5);
            foreach (int k in new[] { 3, 8, 15 }) {
                double measured = Db(Amplitude(y, k * F0) / Amplitude(Signal, k * F0));
                Assert.InRange(measured - Db(expected[k - 1]), -1.0, 1.0);
            }
        }

        [Fact]
        public void GainsFollowTheTargetPitch() {
            // A note an octave above the source: the gains sit on the target's harmonics.
            double target = 2 * F0;
            var y = HifiRdTension.Apply(Signal, SourceF0, Fs, _ => 100, _ => target);
            var expected = GlottalRd.Gains(1.0, 0.5, target, Harmonics / 2);
            Assert.InRange(Db(Amplitude(y, target) / Amplitude(Signal, target)), -0.5, 0.5);
            foreach (int k in new[] { 2, 5 }) {
                double measured = Db(Amplitude(y, k * target) / Amplitude(Signal, k * target));
                Assert.InRange(measured - Db(expected[k - 1]), -1.0, 1.0);
            }
        }
    }
}
