using System;
using System.Linq;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Classic {
    public class WorldlineR11Test {
        [Fact]
        public void RendererIds() {
            Assert.Equal(Renderers.WORLDLINE_R11, Renderers.CreateRenderer(Renderers.WORLDLINE_R11).ToString());
            Assert.Equal(Renderers.WORLDLINE_R, Renderers.CreateRenderer(Renderers.WORLDLINE_R).ToString());
            Assert.Equal(Renderers.WORLDLINE_R2, Renderers.CreateRenderer(Renderers.WORLDLINE_R2).ToString());
            Assert.Contains(Renderers.WORLDLINE_R11, Renderers.GetSupportedRenderers(Core.Ustx.USingerType.Classic));
            // The Worldline-R variants share Worldline-R's expression graphs.
            Assert.Equal(Renderers.WORLDLINE_R, Renderers.GetExpressionGraphSlot(Renderers.WORLDLINE_R11));
            Assert.Equal(Renderers.WORLDLINE_R, Renderers.GetExpressionGraphSlot(Renderers.WORLDLINE_R2));
            Assert.Equal(Renderers.DIFFSINGER, Renderers.GetExpressionGraphSlot(Renderers.DIFFSINGER));
        }

        [Fact]
        public void DefaultRendererPreference() {
            var previous = Core.Util.Preferences.Default.DefaultRenderer;
            try {
                Assert.Contains(Renderers.WORLDLINE_R11, Renderers.getRendererOptions());
                Core.Util.Preferences.Default.DefaultRenderer = Renderers.WORLDLINE_R11;
                Assert.Equal(Renderers.WORLDLINE_R11, Renderers.GetDefaultRenderer(Core.Ustx.USingerType.Classic));
                // Only classic singers take it.
                Assert.Equal(Renderers.DIFFSINGER, Renderers.GetDefaultRenderer(Core.Ustx.USingerType.DiffSinger));
            } finally {
                Core.Util.Preferences.Default.DefaultRenderer = previous;
            }
        }

        [Fact]
        public void TensionReshapesTheHarmonicEnvelopeByRd() {
            var cfg = Worldline.InitAnalysisConfig(44100, 220, 2048);
            int spSize = cfg.fft_size / 2 + 1;
            double binHz = 44100.0 / cfg.fft_size;
            double[] Flat() => Enumerable.Repeat(1.0, spSize).ToArray();
            double[] Gain(double tension, double gender) {
                var sp = Flat();
                Worldline.PhraseSynthV2.ApplyRdTension(cfg, new[] { 200.0 }, sp, new[] { 1.0 },
                    new[] { 0.5 + 0.005 * tension }, new[] { gender });
                return sp;
            }
            // No tension: untouched.
            Assert.All(Gain(0, 0.5), v => Assert.Equal(1.0, v));
            // +100 halves Rd; the power gains are the squared Rd gains on the harmonics of f0.
            var gains = Core.Analysis.GlottalRd.Gains(1.0, 0.5, 200, 110);
            var power = Gain(100, 0.5);
            foreach (int k in new[] { 1, 4, 20 }) {
                int bin = (int)Math.Round(k * 200 / binHz);
                double expected = Core.Analysis.GlottalRd.GainAt(gains, 200, bin * binHz);
                Assert.Equal(expected * expected, power[bin], 9);
            }
            // With a gender shift (bins move to b / ratio), each gain is placed where the shift
            // brings it home, at the harmonic it belongs to.
            double ratio = Math.Pow(2, 50 * 0.01);
            var shifted = Gain(100, 0.75);
            foreach (int k in new[] { 4, 20 }) {
                int bin = (int)Math.Round(k * 200 * ratio / binHz);
                double expected = Core.Analysis.GlottalRd.GainAt(gains, 200, bin * binHz / ratio);
                Assert.Equal(expected * expected, shifted[bin], 9);
            }
        }

        [Fact]
        public void ContinuousNoiseMatchesWorldNoiseLevel() {
            // Unvoiced, flat and fully aperiodic: the output is the noise half alone, and
            // WORLD's noise with a flat spectrum S has per-sample variance S.
            const int fs = 44100, hop = 220, fftSize = 2048, spSize = fftSize / 2 + 1, frames = 400;
            const double s = 1e-4;
            double[] Fill(int n, double v) => Enumerable.Repeat(v, n).ToArray();
            var sp = Fill(frames * spSize, s);
            var y = Worldline.WorldSynthesisContinuousNoise(
                new double[frames], sp, Fill(frames * spSize, s), Fill(frames * spSize, 1.0),
                Fill(frames, 1.0), fftSize, hop, fs, seed: 1,
                Fill(frames, 0.5), Fill(frames, 0.5), Fill(frames, 0.5), Fill(frames, 1.0));
            var mid = y.Skip(y.Length / 10).Take(y.Length * 8 / 10);
            double db = 10 * Math.Log10(mid.Average(v => v * v) / s);
            Assert.InRange(db, -0.5, 0.5);
            Assert.All(sp, v => Assert.Equal(s, v));
        }
    }
}
