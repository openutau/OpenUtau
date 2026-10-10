// Ported from hifisampler's util/growl.py (https://github.com/openhachimi/hifisampler),
// licensed under the Apache License 2.0; see LICENSE in this folder.
// Modified for OpenUtau: translated to C#, reading OpenUtau's resampler arguments.

using System;
using System.Linq;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>
    /// util/growl.py: the band above freqLow is pitch-modulated by a square LFO (time-warped
    /// by the integrated pitch ratio, with the drift high-passed away), RMS-matched, and added
    /// back to the band below.
    /// </summary>
    internal static class HifiGrowl {
        const double MaxVibratoCents = 100.0;
        const double HpCutoffHz = 20.0;
        const double MinNyqFrac = 0.01;

        public static float[] Apply(float[] audio, int sampleRate, double frequency, double strength, double freqLow = 400.0) {
            return Apply(audio, sampleRate, frequency, _ => strength, freqLow);
        }

        /// <param name="strengthAt">The strength at each sample, for a growl curve; a constant is the HG flag.</param>
        public static float[] Apply(float[] audio, int sampleRate, double frequency, Func<int, double> strengthAt, double freqLow = 400.0) {
            if (frequency <= 0 || Enumerable.Range(0, audio.Length).All(i => strengthAt(i) == 0)) {
                return (float[])audio.Clone();
            }
            int n = audio.Length;
            var x = new double[n];
            for (int i = 0; i < n; i++) {
                x[i] = audio[i];
            }
            double nyq = sampleRate / 2.0;
            double norm = Math.Clamp(freqLow / nyq, MinNyqFrac, 0.99);
            var band = HifiIir.SosFilter(HifiIir.ButterHighpassSos(4, norm), x);
            var complement = new double[n];
            for (int i = 0; i < n; i++) {
                complement[i] = x[i] - band[i];
            }
            var modulated = ModulatePitch(band, sampleRate, SquareLfo(n, sampleRate, frequency), strengthAt);
            var result = new float[n];
            for (int i = 0; i < n; i++) {
                result[i] = (float)(complement[i] + modulated[i]);
            }
            return result;
        }

        /// <summary>scipy.signal.square(2 pi f t), duty 0.5.</summary>
        static double[] SquareLfo(int n, int sampleRate, double frequency) {
            var lfo = new double[n];
            double w = 2 * Math.PI * frequency;
            for (int i = 0; i < n; i++) {
                double phase = w * (i / (double)sampleRate);
                double tmod = phase % (2 * Math.PI);
                lfo[i] = tmod < Math.PI ? 1 : -1;
            }
            return lfo;
        }

        static double[] ModulatePitch(double[] band, int sampleRate, double[] lfo, Func<int, double> strengthAt) {
            int n = band.Length;
            if (n == 0) {
                return band;
            }
            var ratio = new double[n];
            double ratioSum = 0;
            for (int i = 0; i < n; i++) {
                ratio[i] = Math.Pow(2, lfo[i] * strengthAt(i) * MaxVibratoCents / 1200.0);
                ratioSum += ratio[i];
            }
            double ratioMean = ratioSum / n;
            var drift = new double[n];
            double cumsum = 0;
            for (int i = 0; i < n; i++) {
                cumsum += ratio[i];
                drift[i] = (cumsum - ratio[0]) - i * ratioMean;
            }
            if (n > 100) {
                drift = HifiIir.SosFilter(HifiIir.ButterHighpassSos(2, HpCutoffHz / (sampleRate / 2.0)), drift);
            }
            var modulated = new double[n];
            double sumOrig = 0;
            double sumNew = 0;
            for (int i = 0; i < n; i++) {
                double idx = Math.Clamp(i + drift[i], 0, n - 1);
                int i0 = Math.Min((int)Math.Floor(idx), n - 1);
                int i1 = Math.Min(i0 + 1, n - 1);
                double frac = idx - i0;
                modulated[i] = band[i0] + (band[i1] - band[i0]) * frac;
                sumOrig += band[i] * band[i];
                sumNew += modulated[i] * modulated[i];
            }
            double rmsOrig = Math.Sqrt(sumOrig / n);
            double rmsNew = Math.Sqrt(sumNew / n);
            if (rmsNew > 1e-10) {
                double gain = rmsOrig / rmsNew;
                for (int i = 0; i < n; i++) {
                    modulated[i] *= gain;
                }
            }
            return modulated;
        }
    }
}
