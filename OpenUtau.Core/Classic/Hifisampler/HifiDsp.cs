using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Tasks;
using FftFlat;
using OpenUtau.Core.Analysis;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>
    /// Forward real DFT of any length, numpy's sign convention: n/2+1 bins.
    /// Power-of-two lengths use FftFlat directly; other lengths (the gender-shifted
    /// mel analysis) use Bluestein's chirp-z on a power-of-two complex FFT.
    /// One instance per thread.
    /// </summary>
    internal sealed class HifiRealDft {
        public readonly int Length;
        readonly RealFourierTransform? real;
        readonly double[]? realBuffer;
        readonly Bluestein? bluestein;
        readonly Complex[]? work;

        static readonly ConcurrentDictionary<int, Bluestein> bluesteins = new ConcurrentDictionary<int, Bluestein>();

        public HifiRealDft(int length) {
            Length = length;
            if (BitOperations.IsPow2(length) && length >= 4) {
                real = new RealFourierTransform(length);
                realBuffer = new double[length + 2];
            } else {
                bluestein = bluesteins.GetOrAdd(length, n => new Bluestein(n));
                work = new Complex[bluestein.M];
            }
        }

        /// <summary>DFT of frame (length <see cref="Length"/>) into bins (length n/2+1).</summary>
        public void Forward(ReadOnlySpan<double> frame, Span<Complex> bins) {
            if (real != null) {
                frame.CopyTo(realBuffer);
                real.Forward(realBuffer).Slice(0, Length / 2 + 1).CopyTo(bins);
                return;
            }
            var b = bluestein!;
            var a = work!;
            int n = Length;
            for (int i = 0; i < n; i++) {
                a[i] = frame[i] * b.Chirp[i];
            }
            Array.Clear(a, n, a.Length - n);
            b.Fft.Forward(a);
            for (int i = 0; i < a.Length; i++) {
                a[i] *= b.KernelSpectrum[i];
            }
            b.Fft.Inverse(a);
            double scale = b.InverseScale;
            for (int k = 0; k < n / 2 + 1; k++) {
                bins[k] = a[k] * b.Chirp[k] * scale;
            }
        }

        sealed class Bluestein {
            public readonly int M;
            public readonly FastFourierTransform Fft;
            public readonly Complex[] Chirp;  // exp(-i pi k^2 / n)
            public readonly Complex[] KernelSpectrum;
            public readonly double InverseScale;

            public Bluestein(int n) {
                M = (int)BitOperations.RoundUpToPowerOf2((uint)(2 * n - 1));
                Fft = new FastFourierTransform(M);
                Chirp = new Complex[n];
                long twoN = 2L * n;
                for (int k = 0; k < n; k++) {
                    // k^2 mod 2n keeps the angle exact for large k.
                    double angle = Math.PI * ((long)k * k % twoN) / n;
                    Chirp[k] = new Complex(Math.Cos(angle), -Math.Sin(angle));
                }
                KernelSpectrum = new Complex[M];
                KernelSpectrum[0] = Complex.Conjugate(Chirp[0]);
                for (int k = 1; k < n; k++) {
                    KernelSpectrum[k] = Complex.Conjugate(Chirp[k]);
                    KernelSpectrum[M - k] = Complex.Conjugate(Chirp[k]);
                }
                Fft.Forward(KernelSpectrum);
                // FftFlat's complex inverse is unnormalized; find out once.
                var probe = new Complex[M];
                probe[0] = 1;
                Fft.Forward(probe);
                Fft.Inverse(probe);
                InverseScale = 1.0 / probe[0].Real;
            }
        }
    }

    /// <summary>The torch.stft / torch.istft subset hifisampler uses, with periodic Hann windows.</summary>
    internal static class HifiStft {
        public static double[] HannWindow(int length) => Hnsep.PeriodicHann(length);

        /// <summary>
        /// torch.stft(center=False) magnitude: frames of nfft samples every hop, the window
        /// (length &lt;= nfft) centered in the frame as torch pads it. Returns [frame][bin].
        /// </summary>
        public static float[][] Magnitude(double[] x, int nfft, int hop, double[] window) {
            int bins = nfft / 2 + 1;
            int frames = x.Length < nfft ? 0 : 1 + (x.Length - nfft) / hop;
            var paddedWindow = PadWindow(window, nfft);
            var result = new float[frames][];
            Parallel.For(0, frames, () => new Scratch(nfft), (m, _, s) => {
                for (int i = 0; i < nfft; i++) {
                    s.frame[i] = x[m * hop + i] * paddedWindow[i];
                }
                s.dft.Forward(s.frame, s.bins);
                var row = new float[bins];
                for (int k = 0; k < bins; k++) {
                    row[k] = (float)s.bins[k].Magnitude;
                }
                result[m] = row;
                return s;
            }, _ => { });
            return result;
        }

        /// <summary>torch.stft(center=True, pad_mode="reflect") complex spectrum. Returns [frame][bin].</summary>
        public static Complex[][] Spectrum(double[] x, int nfft, int hop, double[] window) {
            int pad = nfft / 2;
            var padded = HifiArray.ReflectPad(x, pad, pad);
            int bins = nfft / 2 + 1;
            int frames = 1 + (padded.Length - nfft) / hop;
            var paddedWindow = PadWindow(window, nfft);
            var result = new Complex[frames][];
            Parallel.For(0, frames, () => new Scratch(nfft), (m, _, s) => {
                for (int i = 0; i < nfft; i++) {
                    s.frame[i] = padded[m * hop + i] * paddedWindow[i];
                }
                var row = new Complex[bins];
                s.dft.Forward(s.frame, row);
                result[m] = row;
                return s;
            }, _ => { });
            return result;
        }

        /// <summary>
        /// torch.istft(center=True) without a length: overlap-add divided by the summed squared
        /// window, n_fft/2 trimmed from both ends, hop * (frames - 1) samples.
        /// </summary>
        public static double[] Inverse(Complex[][] spectrum, int nfft, int hop, double[] window) {
            if (!BitOperations.IsPow2(nfft)) {
                throw new ArgumentException($"ISTFT length {nfft} is not a power of two.");
            }
            int frames = spectrum.Length;
            int bins = nfft / 2 + 1;
            var paddedWindow = PadWindow(window, nfft);
            var frameOut = new double[frames * nfft];
            Parallel.For(0, frames, () => (fft: new RealFourierTransform(nfft), buffer: new double[nfft + 2]), (m, _, s) => {
                var bufferSpectrum = System.Runtime.InteropServices.MemoryMarshal.Cast<double, Complex>(s.buffer.AsSpan());
                spectrum[m].AsSpan(0, bins).CopyTo(bufferSpectrum);
                // DC and Nyquist are real for a real signal, as torch's C2R transform assumes.
                bufferSpectrum[0] = bufferSpectrum[0].Real;
                bufferSpectrum[bins - 1] = bufferSpectrum[bins - 1].Real;
                var time = s.fft.Inverse(bufferSpectrum);
                int o = m * nfft;
                for (int i = 0; i < nfft; i++) {
                    frameOut[o + i] = time[i] * paddedWindow[i];
                }
                return s;
            }, _ => { });
            int total = nfft + hop * (frames - 1);
            var y = new double[total];
            var wsum = new double[total];
            for (int m = 0; m < frames; m++) {
                int o = m * nfft;
                int p = m * hop;
                for (int i = 0; i < nfft; i++) {
                    y[p + i] += frameOut[o + i];
                    wsum[p + i] += paddedWindow[i] * paddedWindow[i];
                }
            }
            int start = nfft / 2;
            int length = Math.Max(0, total - nfft);
            var result = new double[length];
            for (int i = 0; i < length; i++) {
                int j = start + i;
                result[i] = wsum[j] > 1e-11 ? y[j] / wsum[j] : 0;
            }
            return result;
        }

        static double[] PadWindow(double[] window, int nfft) {
            if (window.Length == nfft) {
                return window;
            }
            if (window.Length > nfft) {
                throw new ArgumentException($"Window length {window.Length} exceeds n_fft {nfft}.");
            }
            var padded = new double[nfft];
            int left = (nfft - window.Length) / 2;
            Array.Copy(window, 0, padded, left, window.Length);
            return padded;
        }

        sealed class Scratch {
            public readonly HifiRealDft dft;
            public readonly double[] frame;
            public readonly Complex[] bins;

            public Scratch(int nfft) {
                dft = new HifiRealDft(nfft);
                frame = new double[nfft];
                bins = new Complex[nfft / 2 + 1];
            }
        }
    }

    internal static class HifiArray {
        /// <summary>
        /// numpy.pad(mode="reflect") on one axis: mirror without repeating the edge, repeated
        /// for pads longer than the signal. A single sample is repeated, as numpy does.
        /// </summary>
        public static int ReflectIndex(int i, int length) {
            if (length == 1) {
                return 0;
            }
            int period = 2 * (length - 1);
            int m = i % period;
            if (m < 0) {
                m += period;
            }
            return m < length ? m : period - m;
        }

        public static double[] ReflectPad(double[] x, int left, int right) {
            if (x.Length == 0) {
                throw new ArgumentException("Cannot reflect-pad an empty signal.");
            }
            var y = new double[left + x.Length + right];
            for (int i = 0; i < y.Length; i++) {
                y[i] = x[ReflectIndex(i - left, x.Length)];
            }
            return y;
        }

        public static double MaxAbs(ReadOnlySpan<double> x) {
            double max = 0;
            foreach (var v in x) {
                max = Math.Max(max, Math.Abs(v));
            }
            return max;
        }

        public static double MaxAbs(ReadOnlySpan<float> x) {
            double max = 0;
            foreach (var v in x) {
                max = Math.Max(max, Math.Abs(v));
            }
            return max;
        }
    }

    internal static class HifiInterp {
        /// <summary>numpy.interp: linear, clamped to the end values outside xp.</summary>
        public static double[] Linear(double[] x, double[] xp, double[] fp) {
            var y = new double[x.Length];
            int j = 0;
            for (int i = 0; i < x.Length; i++) {
                double v = x[i];
                if (v <= xp[0]) {
                    y[i] = fp[0];
                    continue;
                }
                if (v >= xp[^1]) {
                    y[i] = fp[^1];
                    continue;
                }
                // x is usually increasing; restart the search when it isn't.
                if (j >= xp.Length - 1 || xp[j] > v) {
                    j = 0;
                }
                while (xp[j + 1] <= v) {
                    j++;
                }
                double slope = (fp[j + 1] - fp[j]) / (xp[j + 1] - xp[j]);
                y[i] = slope * (v - xp[j]) + fp[j];
            }
            return y;
        }

        /// <summary>
        /// The interval i with xp[i] &lt;= v &lt; xp[i + 1], the last interval for v at the right
        /// end, clamped to the ends outside xp (searchsorted as scipy's interp1d and PPoly use it).
        /// </summary>
        public static int Interval(double[] xp, double v) {
            int lo = 0;
            int hi = xp.Length - 1;
            if (v >= xp[hi]) {
                return hi - 1;
            }
            if (v < xp[0]) {
                return 0;
            }
            while (hi - lo > 1) {
                int mid = (lo + hi) >> 1;
                if (xp[mid] <= v) {
                    lo = mid;
                } else {
                    hi = mid;
                }
            }
            return lo;
        }

        /// <summary>
        /// scipy.interpolate.Akima1DInterpolator (method="akima") evaluated at x. Fewer than
        /// three points, which Akima can't take, fall back to linear (two) or constant (one).
        /// </summary>
        public static double[] Akima(double[] xp, double[] yp, double[] x) {
            int n = xp.Length;
            if (n < 3) {
                if (n == 2) {
                    return Linear(x, xp, yp);
                }
                var constant = new double[x.Length];
                Array.Fill(constant, yp[0]);
                return constant;
            }
            var m = new double[n - 1];
            for (int i = 0; i < n - 1; i++) {
                m[i] = (yp[i + 1] - yp[i]) / (xp[i + 1] - xp[i]);
            }
            double mm = 2.0 * m[0] - m[1];
            double mmm = 2.0 * mm - m[0];
            double mp = 2.0 * m[n - 2] - m[n - 3];
            double mpp = 2.0 * mp - m[n - 2];
            var m1 = new double[n + 3];
            m1[0] = mmm;
            m1[1] = mm;
            Array.Copy(m, 0, m1, 2, n - 1);
            m1[n + 1] = mp;
            m1[n + 2] = mpp;
            var dm = new double[n + 2];
            for (int i = 0; i < n + 2; i++) {
                dm[i] = Math.Abs(m1[i + 1] - m1[i]);
            }
            var f1 = new double[n];
            var f2 = new double[n];
            var f12 = new double[n];
            double f12Max = double.NegativeInfinity;
            for (int i = 0; i < n; i++) {
                f1[i] = dm[i + 2];
                f2[i] = dm[i];
                f12[i] = f1[i] + f2[i];
                f12Max = Math.Max(f12Max, f12[i]);
            }
            var t = new double[n];
            for (int i = 0; i < n; i++) {
                if (f12[i] > 1e-9 * f12Max) {
                    t[i] = (f1[i] * m1[i + 1] + f2[i] * m1[i + 2]) / f12[i];
                } else {
                    t[i] = 0.5 * (m1[i + 3] + m1[i]);
                }
            }
            // CubicHermiteSpline coefficients, evaluated as PPoly.
            var y = new double[x.Length];
            for (int j = 0; j < x.Length; j++) {
                int i = Interval(xp, x[j]);
                double dx = xp[i + 1] - xp[i];
                double slope = (yp[i + 1] - yp[i]) / dx;
                double tt = (t[i] + t[i + 1] - 2 * slope) / dx;
                double c0 = tt / dx;
                double c1 = (slope - t[i]) / dx - tt;
                double c2 = t[i];
                double c3 = yp[i];
                double s = x[j] - xp[i];
                y[j] = ((c0 * s + c1) * s + c2) * s + c3;
            }
            return y;
        }

        /// <summary>numpy.gradient(f, x): second-order central differences, first-order at the ends.</summary>
        public static double[] Gradient(double[] f, double[] x) {
            int n = f.Length;
            if (n < 2) {
                throw new ArgumentException("Gradient needs at least two points.");
            }
            var g = new double[n];
            for (int i = 1; i < n - 1; i++) {
                double dx1 = x[i] - x[i - 1];
                double dx2 = x[i + 1] - x[i];
                double a = -dx2 / (dx1 * (dx1 + dx2));
                double b = (dx2 - dx1) / (dx1 * dx2);
                double c = dx1 / (dx2 * (dx1 + dx2));
                g[i] = a * f[i - 1] + b * f[i] + c * f[i + 1];
            }
            g[0] = (f[1] - f[0]) / (x[1] - x[0]);
            g[n - 1] = (f[n - 1] - f[n - 2]) / (x[n - 1] - x[n - 2]);
            return g;
        }
    }

    /// <summary>scipy.signal butter(output="sos") high-pass designs and sosfilt / lfilter.</summary>
    internal static class HifiIir {
        /// <summary>
        /// scipy.signal.butter(order, wn, "high", output="sos"): analog prototype, lp2hp,
        /// bilinear, conjugate pole pairs with the gain in the first section and the poles
        /// nearest the unit circle last, as zpk2sos orders them.
        /// </summary>
        public static double[][] ButterHighpassSos(int order, double wn) {
            // fs = 2 in scipy's normalized design.
            double warped = 4.0 * Math.Tan(Math.PI * wn / 2.0);
            var poles = new Complex[order];
            Complex gainNum = 1;
            Complex gainDen = 1;
            for (int k = 0; k < order; k++) {
                // buttap: -exp(i pi m / (2 order)), m = -order+1, -order+3, ..., order-1.
                int mIndex = -order + 1 + 2 * k;
                var analog = -Complex.Exp(new Complex(0, Math.PI * mIndex / (2.0 * order)));
                var hp = warped / analog;
                poles[k] = (4.0 + hp) / (4.0 - hp);
                // Zeros of the high-pass are at s = 0: (fs2 - 0) over (fs2 - p).
                gainNum *= 4.0;
                gainDen *= 4.0 - hp;
            }
            double gain = (gainNum / gainDen).Real;
            // Pair conjugates: keep the upper half-plane poles (and a real pole when odd).
            var pairs = new System.Collections.Generic.List<Complex>();
            Complex? realPole = null;
            foreach (var p in poles) {
                if (Math.Abs(p.Imaginary) < 1e-12) {
                    realPole = p.Real;
                } else if (p.Imaginary > 0) {
                    pairs.Add(p);
                }
            }
            pairs.Sort((a, b) => a.Magnitude.CompareTo(b.Magnitude));
            var sections = new System.Collections.Generic.List<double[]>();
            if (realPole.HasValue) {
                sections.Add(new[] { 1.0, -1.0, 0.0, 1.0, -realPole.Value.Real, 0.0 });
            }
            foreach (var p in pairs) {
                sections.Add(new[] { 1.0, -2.0, 1.0, 1.0, -2.0 * p.Real, p.Real * p.Real + p.Imaginary * p.Imaginary });
            }
            for (int i = 0; i < 3; i++) {
                sections[0][i] *= gain;
            }
            return sections.ToArray();
        }

        /// <summary>scipy.signal.sosfilt with zero initial state.</summary>
        public static double[] SosFilter(double[][] sos, double[] x) {
            var y = (double[])x.Clone();
            foreach (var s in sos) {
                Biquad(s[0] / s[3], s[1] / s[3], s[2] / s[3], s[4] / s[3], s[5] / s[3], y);
            }
            return y;
        }

        /// <summary>Direct form II transposed biquad in place (scipy.signal.lfilter for 3-tap b, a).</summary>
        public static void Biquad(double b0, double b1, double b2, double a1, double a2, double[] y) {
            double z1 = 0;
            double z2 = 0;
            for (int i = 0; i < y.Length; i++) {
                double xi = y[i];
                double yi = b0 * xi + z1;
                z1 = b1 * xi - a1 * yi + z2;
                z2 = b2 * xi - a2 * yi;
                y[i] = yi;
            }
        }
    }
}
