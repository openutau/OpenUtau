using System;
using OpenUtau.Core.Analysis;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>
    /// Tension as Rd (<see cref="GlottalRd"/>) on the harmonic part, in place of hifisampler's
    /// spectral tilt: Rd per frame fitted to the STFT's harmonic peaks at the source f0, and
    /// each frame reshaped by the gains of moving it, on the harmonics of the note's pitch at
    /// that frame. No renormalization: the first harmonic keeps its level.
    /// </summary>
    internal static class HifiRdTension {
        public const int NFft = 2048;
        /// <summary>The frame hop, also the hop of the source f0 it takes.</summary>
        public const int Hop = 256;

        /// <param name="x">The harmonic part (any gain; Rd is fitted relative to the first harmonic).</param>
        /// <param name="sourceF0">Source f0 (Hz, 0 unvoiced) on frames of <see cref="Hop"/> samples.</param>
        /// <param name="tensionAt">The tension curve (-100..100) at a sample.</param>
        /// <param name="targetF0At">The note's f0 (Hz) at a sample.</param>
        public static float[] Apply(float[] x, double[] sourceF0, int sampleRate,
                Func<double, double> tensionAt, Func<double, double> targetF0At) {
            // Zero-padded to whole frames, so the inverse covers every sample.
            var signal = new double[(x.Length + Hop - 1) / Hop * Hop];
            for (int i = 0; i < x.Length; i++) {
                signal[i] = x[i];
            }
            var window = HifiStft.HannWindow(NFft);
            var spec = HifiStft.Spectrum(signal, NFft, Hop, window);
            double binHz = (double)sampleRate / NFft;
            int frames = spec.Length;

            var rd = new double[frames];
            var voiced = new bool[frames];
            for (int m = 0; m < frames; m++) {
                double f0 = m < sourceF0.Length ? sourceF0[m] : 0;
                if (f0 <= 0) {
                    continue;
                }
                var amplitudes = HarmonicPeaks(spec[m], f0, binHz, (int)(GlottalRd.MaxFitHz / f0));
                if (amplitudes.Length < 2) {
                    continue;
                }
                rd[m] = GlottalRd.Fit(amplitudes, f0);
                voiced[m] = true;
            }
            rd = GlottalRd.Smooth(rd, voiced, Math.Max(1, (int)Math.Round(0.02 * sampleRate / Hop)));

            for (int m = 0; m < frames; m++) {
                if (!voiced[m]) {
                    continue;
                }
                double t = tensionAt(m * Hop);
                if (Math.Abs(t) < 1e-9) {
                    continue;
                }
                double f0 = targetF0At(m * Hop);
                var gains = GlottalRd.Gains(rd[m], GlottalRd.TenseRd(rd[m], t), f0, (int)(sampleRate / 2.0 / f0));
                var frame = spec[m];
                for (int k = 0; k < frame.Length; k++) {
                    frame[k] *= GlottalRd.GainAt(gains, f0, k * binHz);
                }
            }

            var y = HifiStft.Inverse(spec, NFft, Hop, window);
            var result = new float[x.Length];
            for (int i = 0; i < result.Length && i < y.Length; i++) {
                result[i] = (float)y[i];
            }
            return result;
        }

        /// <summary>The magnitude peak near each harmonic of f0, interpolated on a parabola of log magnitudes.</summary>
        static double[] HarmonicPeaks(System.Numerics.Complex[] frame, double f0, double binHz, int n) {
            var peaks = new double[Math.Max(0, n)];
            double halfWidth = 0.3 * f0 / binHz;
            for (int k = 1; k <= peaks.Length; k++) {
                double center = k * f0 / binHz;
                int lo = (int)Math.Max(1, center - halfWidth);
                int hi = (int)Math.Min(frame.Length - 2, center + halfWidth);
                if (hi < lo) {
                    return peaks[..(k - 1)];
                }
                int best = lo;
                for (int b = lo + 1; b <= hi; b++) {
                    if (frame[b].Magnitude > frame[best].Magnitude) {
                        best = b;
                    }
                }
                double a = Math.Log(frame[best - 1].Magnitude + 1e-12);
                double c = Math.Log(frame[best].Magnitude + 1e-12);
                double d = Math.Log(frame[best + 1].Magnitude + 1e-12);
                double curvature = a - 2 * c + d;
                double p = curvature < 0 ? 0.5 * (a - d) / curvature : 0;
                peaks[k - 1] = Math.Exp(c - 0.25 * (a - d) * p);
            }
            return peaks;
        }
    }
}
