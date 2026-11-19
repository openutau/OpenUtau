using System;
using System.Linq;
using System.Numerics;
using Xunit;

namespace OpenUtau.Classic.Hifisampler {
    public class HifiDspTest {
        static double[] Noise(int n, int seed) {
            var rng = new Random(seed);
            return Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 2 - 1).ToArray();
        }

        [Theory]
        [InlineData(2048)]
        [InlineData(2170)]  // a gender-shifted n_fft: Bluestein
        [InlineData(1999)]  // odd
        public void RealDftMatchesDirectDft(int n) {
            var x = Noise(n, n);
            var bins = new Complex[n / 2 + 1];
            new HifiRealDft(n).Forward(x, bins);
            double maxErr = 0;
            foreach (int k in new[] { 0, 1, 7, n / 3, n / 2 }) {
                Complex sum = 0;
                for (int i = 0; i < n; i++) {
                    double angle = -2 * Math.PI * ((long)k * i % n) / n;
                    sum += x[i] * new Complex(Math.Cos(angle), Math.Sin(angle));
                }
                maxErr = Math.Max(maxErr, (sum - bins[k]).Magnitude);
            }
            Assert.True(maxErr < 1e-8, $"max error {maxErr}");
        }

        [Fact]
        public void StftRoundTrip() {
            var x = Noise(512 * 40, 1);
            var window = HifiStft.HannWindow(2048);
            var y = HifiStft.Inverse(HifiStft.Spectrum(x, 2048, 512, window), 2048, 512, window);
            Assert.Equal(x.Length, y.Length);
            double maxErr = x.Zip(y, (a, b) => Math.Abs(a - b)).Max();
            Assert.True(maxErr < 1e-9, $"max error {maxErr}");
        }

        [Fact]
        public void ReflectPadMatchesNumpy() {
            // np.pad([1, 2, 3], (2, 5), mode="reflect")
            Assert.Equal(new double[] { 3, 2, 1, 2, 3, 2, 1, 2, 3, 2 }, HifiArray.ReflectPad(new double[] { 1, 2, 3 }, 2, 5));
            // A single sample is repeated.
            Assert.Equal(new double[] { 4, 4, 4 }, HifiArray.ReflectPad(new double[] { 4 }, 1, 1));
        }

        [Fact]
        public void AkimaReproducesLinesAndKnots() {
            var xp = new double[] { 0, 0.5, 1.5, 2, 3.25, 4 };
            var line = xp.Select(v => 3 * v - 1).ToArray();
            var x = new double[] { 0, 0.2, 0.5, 1.1, 2.7, 3.9, 4 };
            var y = HifiInterp.Akima(xp, line, x);
            for (int i = 0; i < x.Length; i++) {
                Assert.Equal(3 * x[i] - 1, y[i], 12);
            }
            var bumpy = new double[] { 0, 1, -1, 2, 0, 5 };
            Assert.Equal(bumpy, HifiInterp.Akima(xp, bumpy, xp).Select(v => Math.Round(v, 12)).ToArray());
        }

        [Fact]
        public void GradientIsExactForQuadratics() {
            var x = Enumerable.Range(0, 10).Select(i => i * 0.25).ToArray();
            var f = x.Select(v => v * v).ToArray();
            var g = HifiInterp.Gradient(f, x);
            for (int i = 1; i < x.Length - 1; i++) {
                Assert.Equal(2 * x[i], g[i], 12);
            }
            Assert.Equal((f[1] - f[0]) / 0.25, g[0], 12);
        }

        [Theory]
        [InlineData(4, 400.0)]
        [InlineData(2, 20.0)]
        public void ButterworthHighpassResponse(int order, double cutoffHz) {
            var sos = HifiIir.ButterHighpassSos(order, cutoffHz / 22050.0);
            Assert.Equal(order / 2, sos.Length);
            double Gain(double hz) {
                var z = Complex.Exp(new Complex(0, Math.PI * hz / 22050.0));
                Complex h = 1;
                foreach (var s in sos) {
                    h *= (s[0] + s[1] / z + s[2] / (z * z)) / (s[3] + s[4] / z + s[5] / (z * z));
                }
                return h.Magnitude;
            }
            Assert.Equal(1 / Math.Sqrt(2), Gain(cutoffHz), 6);
            Assert.Equal(1.0, Gain(22050), 6);
            // A decade below, the slope is 20 dB per order.
            Assert.InRange(Gain(cutoffHz / 10), 0.5 * Math.Pow(10, -order), 2 * Math.Pow(10, -order));
        }

        [Fact]
        public void GrowlStrengthZeroIsIdentity() {
            var x = Noise(4410, 3).Select(v => (float)v).ToArray();
            Assert.Equal(x, HifiGrowl.Apply(x, 44100, 80, 0));
            var y = HifiGrowl.Apply(x, 44100, 80, 0.5);
            Assert.Equal(x.Length, y.Length);
            Assert.NotEqual(x, y);
        }

    }
}
