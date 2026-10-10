using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace OpenUtau.Core.Analysis {
    /// <summary>
    /// The STFT / masking / ISTFT around the hnsep network, with stand-in masks in
    /// place of the network, so no package is needed.
    /// </summary>
    public class HnsepTest {
        const int Nfft = 2048;
        const int Hop = 512;

        static float[] Noise(int n, int seed) {
            var rng = new Random(seed);
            return Enumerable.Range(0, n).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray();
        }

        /// <summary>A mask of re + i im on every bin, in the [1, 2 (re, im), bins, frames] layout.</summary>
        static Func<DenseTensor<float>, float[]> ConstantMask(float re, float im) => input => {
            int half = (int)(input.Length / 2);
            var mask = new float[input.Length];
            Array.Fill(mask, re, 0, half);
            Array.Fill(mask, im, half, half);
            return mask;
        };

        [Theory]
        [InlineData(1f)]
        [InlineData(0.5f)]
        public void RealMaskScalesSignal(float gain) {
            var x = Noise(44100 * 3 + 123, 1);
            var y = Hnsep.Separate(x, Nfft, Hop, Hnsep.PeriodicHann(Nfft), ConstantMask(gain, 0));
            Assert.Equal(x.Length, y.Length);
            double maxErr = x.Zip(y, (a, b) => Math.Abs(a * gain - b)).Max();
            Assert.True(maxErr < 1e-5, $"max error {maxErr}");
        }

        [Fact]
        public void ImaginaryMaskShiftsPhase() {
            // Multiplying every bin by i delays a sinusoid by a quarter period.
            const int bin = 100;
            double w = 2 * Math.PI * bin / Nfft;
            var x = Enumerable.Range(0, 44100).Select(i => (float)Math.Cos(w * i)).ToArray();
            var y = Hnsep.Separate(x, Nfft, Hop, Hnsep.PeriodicHann(Nfft), ConstantMask(0, 1));
            for (int i = Nfft; i < x.Length - Nfft; i++) {
                Assert.Equal(-Math.Sin(w * i), y[i], 1e-4);
            }
        }

        [Fact]
        public void NetworkInputIsTheStft() {
            // A pure tone at bin 100: the network input peaks there, in every frame.
            const int bin = 100;
            var x = Enumerable.Range(0, 44100).Select(i => (float)Math.Sin(2 * Math.PI * bin * i / Nfft)).ToArray();
            int calls = 0;
            Hnsep.Separate(x, Nfft, Hop, Hnsep.PeriodicHann(Nfft), input => {
                calls++;
                int bins = input.Dimensions[2], frames = input.Dimensions[3];
                Assert.Equal(Nfft / 2 + 1, bins);
                Assert.Equal(0, frames % 32);  // the network's segment length
                var data = input.Buffer.ToArray();
                for (int m = 4; m < frames - 4; m++) {
                    double Power(int k) => Math.Pow(data[k * frames + m], 2) + Math.Pow(data[(bins + k) * frames + m], 2);
                    Assert.Equal(bin, Enumerable.Range(0, bins).MaxBy(Power));
                }
                return ConstantMask(1, 0)(input);
            });
            Assert.Equal(1, calls);
        }
    }
}
