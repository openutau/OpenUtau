// Ported from hifisampler's util/wav2mel.py (https://github.com/openhachimi/hifisampler),
// licensed under the Apache License 2.0; see LICENSE in this folder.
// Modified for OpenUtau: translated to C#, reading OpenUtau's resampler arguments.
// util/wav2mel.py is itself from openvpi/SingingVocoders (MIT); the mel basis follows
// librosa.filters.mel (ISC).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>
    /// util/wav2mel.py PitchAdjustableMelSpectrogram (openvpi SingingVocoders): a magnitude
    /// mel spectrogram whose FFT and window are scaled by a key shift, resized back to the
    /// unshifted bin count, so the formants move by key_shift semitones.
    /// </summary>
    internal sealed class HifiMelSpectrogram {
        readonly int sampleRate;
        readonly int nfft;
        readonly int winSize;
        readonly int hopLength;
        readonly double fmin;
        readonly double fmax;
        readonly int nMels;

        public HifiMelSpectrogram(int sampleRate, int nfft, int winSize, int hopLength, double fmin, double fmax, int nMels) {
            this.sampleRate = sampleRate;
            this.nfft = nfft;
            this.winSize = winSize;
            this.hopLength = hopLength;
            this.fmin = fmin;
            this.fmax = fmax;
            this.nMels = nMels;
        }

        /// <summary>
        /// The frames <see cref="Compute"/> returns for n samples, whatever the key shift (the
        /// window always equals the FFT length, and the padding is window minus hop).
        /// </summary>
        public static int FrameCount(int n, int hop) => n < hop ? 0 : 1 + (n - hop) / hop;

        /// <summary>The mel magnitudes (before log) of y, [frame][mel]. speed is always 1 in hifisampler.</summary>
        public float[][] Compute(float[] y, double keyShift) {
            double factor = Math.Pow(2, keyShift / 12);
            int nfftNew = (int)Math.Round(nfft * factor, MidpointRounding.ToEven);
            int winSizeNew = (int)Math.Round(winSize * factor, MidpointRounding.ToEven);
            int hop = hopLength;

            var x = new double[y.Length];
            for (int i = 0; i < y.Length; i++) {
                x[i] = y[i];
            }
            var padded = HifiArray.ReflectPad(x, (winSizeNew - hop) / 2, (winSizeNew - hop + 1) / 2);
            var spec = HifiStft.Magnitude(padded, nfftNew, hop, HifiStft.HannWindow(winSizeNew));

            int size = nfft / 2 + 1;
            double binScale = keyShift != 0 ? (double)winSize / winSizeNew : 1.0;
            var basis = HifiMelBasis.Get(sampleRate, nfft, nMels, fmin, fmax);
            var mel = new float[spec.Length][];
            System.Threading.Tasks.Parallel.For(0, spec.Length, m => {
                var row = spec[m];
                int bins = Math.Min(size, row.Length);  // missing bins are zero-padded
                var outRow = new float[nMels];
                for (int b = 0; b < nMels; b++) {
                    var weights = basis[b];
                    double sum = 0;
                    for (int k = 0; k < bins; k++) {
                        sum += weights[k] * (row[k] * binScale);
                    }
                    outRow[b] = (float)sum;
                }
                mel[m] = outRow;
            });
            return mel;
        }

        /// <summary>
        /// <see cref="Compute(float[], double)"/> with a key shift per frame (a gender curve):
        /// each frame takes its own FFT size and window, centered where every size centers it.
        /// A constant shift is exactly the single-shift analysis.
        /// </summary>
        public float[][] Compute(float[] y, double[] keyShifts) {
            int frames = FrameCount(y.Length, hopLength);
            if (keyShifts.Length != frames) {
                throw new ArgumentException($"{keyShifts.Length} key shifts for {frames} frames.");
            }
            if (frames == 0 || keyShifts.All(k => k == keyShifts[0])) {
                return Compute(y, frames == 0 ? 0 : keyShifts[0]);
            }
            var x = new double[y.Length];
            for (int i = 0; i < y.Length; i++) {
                x[i] = y[i];
            }
            int hop = hopLength;
            int size = nfft / 2 + 1;
            var basis = HifiMelBasis.Get(sampleRate, nfft, nMels, fmin, fmax);
            var mel = new float[frames][];
            System.Threading.Tasks.Parallel.For(0, frames, () => new Dictionary<(int, int), FrameScratch>(), (m, _, scratches) => {
                double keyShift = keyShifts[m];
                double factor = Math.Pow(2, keyShift / 12);
                int nfftNew = (int)Math.Round(nfft * factor, MidpointRounding.ToEven);
                int winSizeNew = (int)Math.Round(winSize * factor, MidpointRounding.ToEven);
                if (!scratches.TryGetValue((nfftNew, winSizeNew), out var s)) {
                    s = new FrameScratch(nfftNew, winSizeNew);
                    scratches[(nfftNew, winSizeNew)] = s;
                }
                // Frame m of the reflect-padded signal, in the unpadded signal's samples.
                int start = m * hop - (winSizeNew - hop) / 2;
                for (int i = 0; i < nfftNew; i++) {
                    int j = start + i;
                    s.frame[i] = (j >= 0 && j < x.Length ? x[j] : x[HifiArray.ReflectIndex(j, x.Length)]) * s.window[i];
                }
                s.dft.Forward(s.frame, s.bins);
                int bins = Math.Min(size, s.bins.Length);  // missing bins are zero-padded
                double binScale = keyShift != 0 ? (double)winSize / winSizeNew : 1.0;
                var outRow = new float[nMels];
                for (int b = 0; b < nMels; b++) {
                    var weights = basis[b];
                    double sum = 0;
                    for (int k = 0; k < bins; k++) {
                        sum += weights[k] * ((float)s.bins[k].Magnitude * binScale);
                    }
                    outRow[b] = (float)sum;
                }
                mel[m] = outRow;
                return scratches;
            }, _ => { });
            return mel;
        }

        sealed class FrameScratch {
            public readonly HifiRealDft dft;
            public readonly double[] window;
            public readonly double[] frame;
            public readonly System.Numerics.Complex[] bins;

            public FrameScratch(int nfft, int winSize) {
                dft = new HifiRealDft(nfft);
                // torch centers a shorter window in the frame.
                window = new double[nfft];
                Array.Copy(HifiStft.HannWindow(winSize), 0, window, (nfft - winSize) / 2, winSize);
                frame = new double[nfft];
                bins = new System.Numerics.Complex[nfft / 2 + 1];
            }
        }

        /// <summary>util/audio.py dynamic_range_compression_torch, clip_val 1e-9 (the one resampler.py imports).</summary>
        public static void LogCompress(float[][] mel) {
            foreach (var row in mel) {
                for (int i = 0; i < row.Length; i++) {
                    row[i] = (float)Math.Log(Math.Max(row[i], 1e-9f));
                }
            }
        }
    }

    /// <summary>librosa.filters.mel(htk=False, norm="slaney"), cached per configuration.</summary>
    internal static class HifiMelBasis {
        static readonly ConcurrentDictionary<(int, int, int, double, double), float[][]> cache
            = new ConcurrentDictionary<(int, int, int, double, double), float[][]>();

        public static float[][] Get(int sampleRate, int nfft, int nMels, double fmin, double fmax) {
            return cache.GetOrAdd((sampleRate, nfft, nMels, fmin, fmax), key => Create(key.Item1, key.Item2, key.Item3, key.Item4, key.Item5));
        }

        /// <summary>[mel][fft bin] weights.</summary>
        public static float[][] Create(int sampleRate, int nfft, int nMels, double fmin, double fmax) {
            int bins = nfft / 2 + 1;
            // np.fft.rfftfreq(n_fft, 1 / sr), in its operation order.
            double binHz = 1.0 / (nfft * (1.0 / sampleRate));
            var fftFreqs = new double[bins];
            for (int k = 0; k < bins; k++) {
                fftFreqs[k] = k * binHz;
            }
            var melF = MelFrequencies(nMels + 2, fmin, fmax);
            var weights = new float[nMels][];
            for (int i = 0; i < nMels; i++) {
                double fdiffLower = melF[i + 1] - melF[i];
                double fdiffUpper = melF[i + 2] - melF[i + 1];
                double enorm = 2.0 / (melF[i + 2] - melF[i]);
                var row = new float[bins];
                for (int k = 0; k < bins; k++) {
                    double lower = -(melF[i] - fftFreqs[k]) / fdiffLower;
                    double upper = (melF[i + 2] - fftFreqs[k]) / fdiffUpper;
                    // librosa stores the triangle in float32 before the slaney normalization.
                    float w = (float)Math.Max(0, Math.Min(lower, upper));
                    row[k] = (float)(w * enorm);
                }
                weights[i] = row;
            }
            return weights;
        }

        const double FSp = 200.0 / 3;
        const double MinLogHz = 1000.0;
        const double MinLogMel = MinLogHz / FSp;
        static readonly double LogStep = Math.Log(6.4) / 27.0;

        public static double HzToMel(double hz) {
            return hz >= MinLogHz ? MinLogMel + Math.Log(hz / MinLogHz) / LogStep : hz / FSp;
        }

        public static double MelToHz(double mel) {
            return mel >= MinLogMel ? MinLogHz * Math.Exp(LogStep * (mel - MinLogMel)) : FSp * mel;
        }

        /// <summary>librosa.mel_frequencies: n mel-spaced frequencies from fmin to fmax (np.linspace).</summary>
        public static double[] MelFrequencies(int n, double fmin, double fmax) {
            double minMel = HzToMel(fmin);
            double maxMel = HzToMel(fmax);
            var result = new double[n];
            double step = (maxMel - minMel) / (n - 1);
            for (int i = 0; i < n; i++) {
                result[i] = MelToHz(i == n - 1 ? maxMel : minMel + i * step);
            }
            return result;
        }
    }
}
