using System;
using System.Linq;
using Xunit;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>Breathiness / voicing / tension branching, with a stand-in for hnsep, so no package is needed.</summary>
    public class HifiFeaturesTest {
        static float[] Sine(int n, double amplitude) =>
            Enumerable.Range(0, n).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * 220 * i / 44100.0))).ToArray();

        static float[][] Mel(float[] x) {
            var mel = new HifiMelSpectrogram(44100, 2048, 2048, 128, 40, 16000, 128).Compute(x, 0);
            HifiMelSpectrogram.LogCompress(mel);
            return mel;
        }

        // A steady 220 Hz source, on HifiRdTension's frames.
        static Func<double[]> F0(float[] x) => () => Enumerable.Repeat(220.0, x.Length / HifiRdTension.Hop + 2).ToArray();

        static HifiSourceCurves Constant(float[] x, double breathiness, double voicing, double tension, double gender = 0) =>
            HifiSourceCurves.Constant(HifiMelSpectrogram.FrameCount(x.Length, 128), breathiness, voicing, tension, 128, gender);

        static void AssertMelEqual(float[][] expected, float[][] actual) {
            Assert.Equal(expected.Length, actual.Length);
            double maxErr = 0;
            for (int i = 0; i < expected.Length; i++) {
                for (int k = 0; k < expected[i].Length; k++) {
                    maxErr = Math.Max(maxErr, Math.Abs(expected[i][k] - actual[i][k]));
                }
            }
            Assert.True(maxErr < 1e-4, $"max error {maxErr}");
        }

        [Theory]
        [InlineData(0, 100, 0, false)]
        [InlineData(-50, 50, 0, false)]  // equal gains: plain scaling
        [InlineData(50, 100, 0, true)]  // breathiness 50 is gain 2
        [InlineData(0, 50, 0, true)]
        [InlineData(-100, 100, 0, true)]
        [InlineData(0, 100, 20, true)]
        public void SeparationOnlyWhenNeeded(double breathiness, double voicing, double tension, bool expected) {
            var x = Sine(4410, 0.3);
            var curves = Constant(x, breathiness, voicing, tension);
            Assert.Equal(expected, curves.NeedsSeparation);
            bool called = false;
            HifiFeatures.Generate(x, curves, new HifiSamplerConfig(), s => {
                called = true;
                return s.Select(v => v * 0.5f).ToArray();
            }, F0(x));
            Assert.Equal(expected, called);
        }

        [Fact]
        public void DefaultCurvesAnalyzeTheSource() {
            var x = Sine(44100, 0.3);
            var f = HifiFeatures.Generate(x, Constant(x, 0, 100, 0), new HifiSamplerConfig(), _ => throw new Exception("not needed"), F0(x));
            Assert.Equal(1.0, f.Scale);
            Assert.Equal(HifiMelSpectrogram.FrameCount(x.Length, 128), f.Mel.Length);
            AssertMelEqual(Mel(x), f.Mel);
        }

        [Fact]
        public void BreathAndVoicingGains() {
            var x = Sine(44100, 0.3);
            // Stand-in: the harmonic part is half the signal, so the noise part is the other half.
            Func<float[], float[]> half = s => s.Select(v => v * 0.5f).ToArray();
            // Breathiness -100 drops the noise, voicing 100 keeps the harmonic: 0.5 x.
            var f = HifiFeatures.Generate(x, Constant(x, -100, 100, 0), new HifiSamplerConfig(), half, F0(x));
            AssertMelEqual(Mel(x.Select(v => v * 0.5f).ToArray()), f.Mel);
            // Breathiness 100 triples the noise (as Worldline-R), voicing 50 halves the harmonic: 1.75 x,
            // over 0.5 peak so rescaled.
            x = Sine(44100, 0.6);
            f = HifiFeatures.Generate(x, Constant(x, 100, 50, 0), new HifiSamplerConfig(), half, F0(x));
            Assert.Equal(0.5 / (0.6 * 1.75), f.Scale, 4);
        }

        [Fact]
        public void CurvesVaryAlongTheSource() {
            // Breathiness -100 then 0 (harmonic only, then all): the first half is 0.5 x, the second x.
            var x = Sine(44100, 0.3);
            int frames = HifiMelSpectrogram.FrameCount(x.Length, 128);
            var breathiness = Enumerable.Range(0, frames).Select(m => m < frames / 2 ? -100.0 : 0).ToArray();
            var curves = new HifiSourceCurves(breathiness, Enumerable.Repeat(100.0, frames).ToArray(), new double[frames], new double[frames],
                Enumerable.Repeat(60.0, frames).ToArray(), 128);
            var f = HifiFeatures.Generate(x, curves, new HifiSamplerConfig(), s => s.Select(v => v * 0.5f).ToArray(), F0(x));
            var quiet = Mel(x.Select(v => v * 0.5f).ToArray());
            var loud = Mel(x);
            int q = frames / 4, l = frames * 3 / 4;
            Assert.Equal(quiet[q][10], f.Mel[q][10], 3);
            Assert.Equal(loud[l][10], f.Mel[l][10], 3);
        }

        [Fact]
        public void LoudSourceIsScaledToHalfPeak() {
            var x = Sine(44100, 0.8);
            var f = HifiFeatures.Generate(x, Constant(x, 0, 100, 0), new HifiSamplerConfig(), _ => throw new Exception(), F0(x));
            Assert.Equal(0.5 / 0.8, f.Scale, 4);
        }

        [Fact]
        public void GenderCurveAnalyzesEachFrameAtItsShift() {
            // A frame of the per-frame analysis is that frame of the single-shift analysis.
            var x = Sine(22050, 0.3);
            int frames = HifiMelSpectrogram.FrameCount(x.Length, 128);
            var analyzer = new HifiMelSpectrogram(44100, 2048, 2048, 128, 40, 16000, 128);
            var shifts = Enumerable.Range(0, frames).Select(m => (m % 3 - 1) * 1.5).ToArray();
            var varying = analyzer.Compute(x, shifts);
            foreach (double shift in new[] { -1.5, 0, 1.5 }) {
                var constant = analyzer.Compute(x, shift);
                for (int m = 0; m < frames; m++) {
                    if (shifts[m] == shift) {
                        Assert.Equal(constant[m], varying[m]);
                    }
                }
            }
            Assert.Equal(analyzer.Compute(x, 0.5), analyzer.Compute(x, Enumerable.Repeat(0.5, frames).ToArray()));
        }

        [Theory]
        [InlineData(0, 1000)]
        [InlineData(100, 500)]   // down an octave, as Worldline-R
        [InlineData(-100, 2000)]
        [InlineData(50, 707)]
        public void GenderCurveMovesFormantsAsWorldline(double gender, double expectedHz) {
            var x = Enumerable.Range(0, 44100).Select(i => (float)(0.3 * Math.Sin(2 * Math.PI * 1000 * i / 44100.0))).ToArray();
            var f = HifiFeatures.Generate(x, Constant(x, 0, 100, 0, gender), new HifiSamplerConfig(), _ => throw new Exception(), F0(x));
            var row = f.Mel[f.Mel.Length / 2];
            int peak = Array.IndexOf(row, row.Max());
            double peakHz = HifiMelBasis.MelFrequencies(130, 40, 16000)[peak + 1];
            Assert.InRange(1200 * Math.Log2(peakHz / expectedHz), -60, 60);
        }

        [Fact]
        public void GrowlCurveOfConstantValueIsTheFlag() {
            var x = Sine(4410, 0.3);
            Assert.Equal(HifiGrowl.Apply(x, 44100, 80, 0.6), HifiGrowl.Apply(x, 44100, 80, _ => 0.6));
            Assert.Equal(x, HifiGrowl.Apply(x, 44100, 80, _ => 0.0));
        }

    }
}
