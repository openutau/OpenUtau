using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// The log-mel spectrogram of one 44.1 kHz audio buffer, computed once in the
    /// background and kept whole (Bands bytes per frame; with the defaults about 25 KB
    /// per second of audio). Frame f is centered on sample f * Hop. Values are bytes:
    /// 0 means "no data", 1..255 span [MinDb, 0] dB, where 0 dB is a full-scale sine.
    /// FftSize sets the frequency resolution, Hop the time resolution, Bands the number
    /// of rows kept between 0 Hz and MaxHz.
    /// </summary>
    public sealed class MelSpectrogram : IDisposable {
        public const int SampleRate = 44100;
        public const double MaxHz = 8000;
        public const double MinDb = -100;

        public const int DefaultFftSize = 2048;
        public const int DefaultHop = 220;
        public const int DefaultBands = 128;

        private const int ChunkFrames = 256;

        // Window, FFT twiddles and mel filters for one (FftSize, Bands) pair.
        private sealed class Plan {
            private static readonly ConcurrentDictionary<(int, int), Plan> cache = new();

            public readonly int FftSize;
            public readonly int Bands;
            public readonly float[] Window;
            public readonly int[] BitReverse;
            public readonly float[] Cos;
            public readonly float[] Sin;
            public readonly int[] FilterStart;
            public readonly float[][] FilterWeights;
            public readonly double PowerNorm;

            public static Plan Get(int fftSize, int bands) =>
                cache.GetOrAdd((fftSize, bands), key => new Plan(key.Item1, key.Item2));

            private Plan(int fftSize, int bands) {
                FftSize = fftSize;
                Bands = bands;
                Window = new float[fftSize];
                BitReverse = new int[fftSize];
                Cos = new float[fftSize / 2];
                Sin = new float[fftSize / 2];
                FilterStart = new int[bands];
                FilterWeights = new float[bands][];

                double sum = 0;
                for (int i = 0; i < fftSize; ++i) {
                    Window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / fftSize));
                    sum += Window[i];
                }
                // A full-scale sine then has a peak-bin power of 1.
                PowerNorm = 1.0 / ((sum / 2) * (sum / 2));

                int bits = (int)Math.Log2(fftSize);
                for (int i = 0; i < fftSize; ++i) {
                    int r = 0;
                    for (int b = 0; b < bits; ++b) {
                        if ((i & (1 << b)) != 0) {
                            r |= 1 << (bits - 1 - b);
                        }
                    }
                    BitReverse[i] = r;
                }
                for (int i = 0; i < fftSize / 2; ++i) {
                    Cos[i] = (float)Math.Cos(2 * Math.PI * i / fftSize);
                    Sin[i] = (float)-Math.Sin(2 * Math.PI * i / fftSize);
                }
                BuildFilters();
            }

            private void BuildFilters() {
                double binHz = (double)SampleRate / FftSize;
                int bins = FftSize / 2 + 1;
                double melMax = HzToMel(MaxHz);
                for (int b = 0; b < Bands; ++b) {
                    double lo = MelToHz(melMax * (b - 1) / (Bands - 1));
                    double center = MelToHz(melMax * b / (Bands - 1));
                    double hi = MelToHz(melMax * (b + 1) / (Bands - 1));
                    int first = Math.Max(0, (int)Math.Ceiling(lo / binHz));
                    int last = Math.Min(bins - 1, (int)Math.Floor(hi / binHz));
                    var weights = new float[last - first + 1];
                    double total = 0;
                    for (int k = first; k <= last; ++k) {
                        double f = k * binHz;
                        double w = f <= center ? (f - lo) / (center - lo) : (hi - f) / (hi - center);
                        weights[k - first] = (float)w;
                        total += w;
                    }
                    if (total < 0.5) {
                        // Narrower than a bin (low bands): interpolate the spectrum at the center.
                        int k0 = Math.Min(bins - 2, (int)Math.Floor(center / binHz));
                        double frac = center / binHz - k0;
                        FilterStart[b] = k0;
                        FilterWeights[b] = new[] { (float)(1 - frac), (float)frac };
                    } else {
                        FilterStart[b] = first;
                        FilterWeights[b] = weights;
                    }
                }
            }
        }

        private readonly byte[] data;
        private readonly bool[] chunkDone;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private long lastRaiseTicks;

        public int FftSize { get; }
        public int Hop { get; }
        public int Bands { get; }
        public double FrameRate => (double)SampleRate / Hop;
        public int FrameCount { get; }

        /// <summary>Raised from a background thread when more frames became available.</summary>
        public event Action? Changed;

        /// <summary>
        /// Starts computing every frame of samples (interleaved, channels per frame).
        /// fftSize must be a power of two.
        /// </summary>
        public MelSpectrogram(float[] samples, int channels,
                int fftSize = DefaultFftSize, int hop = DefaultHop, int bands = DefaultBands) {
            FftSize = fftSize;
            Hop = hop;
            Bands = bands;
            FrameCount = samples.Length / channels / Hop + 1;
            data = new byte[(long)FrameCount * Bands];
            chunkDone = new bool[(FrameCount + ChunkFrames - 1) / ChunkFrames];
            Task.Run(() => Run(samples, channels));
        }

        public static double HzToMel(double hz) => 2595 * Math.Log10(1 + hz / 700);
        public static double MelToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);

        /// <summary>The fractional band position of a frequency; 0 is 0 Hz, bands - 1 is MaxHz.</summary>
        public static double HzToBand(double hz, int bands) => HzToMel(hz) / HzToMel(MaxHz) * (bands - 1);

        /// <summary>The level in dB of a stored value (valid for 1..255).</summary>
        public static double ByteToDb(byte value) => MinDb + (value - 1) / 254.0 * -MinDb;

        /// <summary>
        /// Fills dest with count frames from firstFrame, Bands bytes each. Frames outside the
        /// audio, or not computed yet, are zero.
        /// </summary>
        public void Read(int firstFrame, int count, byte[] dest) {
            Array.Clear(dest, 0, count * Bands);
            for (int i = 0; i < count;) {
                int frame = firstFrame + i;
                if (frame < 0 || frame >= FrameCount) {
                    ++i;
                    continue;
                }
                int chunk = frame / ChunkFrames;
                int run = Math.Min(count - i, (chunk + 1) * ChunkFrames - frame);
                run = Math.Min(run, FrameCount - frame);
                if (Volatile.Read(ref chunkDone[chunk])) {
                    Buffer.BlockCopy(data, frame * Bands, dest, i * Bands, run * Bands);
                }
                i += run;
            }
        }

        public void Dispose() {
            cts.Cancel();
        }

        private void Run(float[] samples, int channels) {
            try {
                var options = new ParallelOptions {
                    CancellationToken = cts.Token,
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                };
                Parallel.For(0, chunkDone.Length, options, chunk => {
                    int first = chunk * ChunkFrames;
                    int count = Math.Min(ChunkFrames, FrameCount - first);
                    Compute(samples, channels, FftSize, Hop, Bands, first, count, data, first * Bands);
                    Volatile.Write(ref chunkDone[chunk], true);
                    RaiseThrottled();
                });
                Changed?.Invoke();
            } catch (OperationCanceledException) {
            } catch (Exception e) {
                Log.Error(e, "Mel computation failed");
            }
        }

        private void RaiseThrottled() {
            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref lastRaiseTicks);
            if ((now - last) * 1000 / Stopwatch.Frequency >= 100 &&
                Interlocked.CompareExchange(ref lastRaiseTicks, now, last) == last) {
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Writes frameCount frames starting at firstFrame into dest at destOffset, bands
        /// bytes per frame. Audio outside the buffer is silence. fftSize must be a power
        /// of two.
        /// </summary>
        public static void Compute(float[] samples, int channels, int fftSize, int hop, int bands,
                int firstFrame, int frameCount, byte[] dest, int destOffset) {
            var plan = Plan.Get(fftSize, bands);
            var re = new float[fftSize];
            var im = new float[fftSize];
            var power = new float[fftSize / 2 + 1];
            for (int f = 0; f < frameCount; f += 2) {
                bool pair = f + 1 < frameCount;
                Load(plan, samples, channels, hop, firstFrame + f, re);
                if (pair) {
                    Load(plan, samples, channels, hop, firstFrame + f + 1, im);
                } else {
                    Array.Clear(im);
                }
                Fft(plan, re, im);
                Quantize(plan, re, im, false, power, dest, destOffset + f * bands);
                if (pair) {
                    Quantize(plan, re, im, true, power, dest, destOffset + (f + 1) * bands);
                }
            }
        }

        // Windowed mono audio for one frame.
        private static void Load(Plan plan, float[] samples, int channels, int hop, int frame, float[] target) {
            int fftSize = plan.FftSize;
            long start = (long)frame * hop - fftSize / 2;
            int frames = samples.Length / channels;
            for (int i = 0; i < fftSize; ++i) {
                long s = start + i;
                float v = 0;
                if (s >= 0 && s < frames) {
                    if (channels == 1) {
                        v = samples[s];
                    } else {
                        float sum = 0;
                        for (int c = 0; c < channels; ++c) {
                            sum += samples[s * channels + c];
                        }
                        v = sum / channels;
                    }
                }
                target[i] = v * plan.Window[i];
            }
        }

        // In-place radix-2 FFT of re + i * im.
        private static void Fft(Plan plan, float[] re, float[] im) {
            int fftSize = plan.FftSize;
            var bitReverse = plan.BitReverse;
            var cos = plan.Cos;
            var sin = plan.Sin;
            for (int i = 0; i < fftSize; ++i) {
                int j = bitReverse[i];
                if (j > i) {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int size = 2; size <= fftSize; size <<= 1) {
                int half = size / 2;
                int step = fftSize / size;
                for (int start = 0; start < fftSize; start += size) {
                    for (int k = 0, t = 0; k < half; ++k, t += step) {
                        int a = start + k;
                        int b = a + half;
                        float tr = re[b] * cos[t] - im[b] * sin[t];
                        float ti = re[b] * sin[t] + im[b] * cos[t];
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
                }
            }
        }

        // Two real frames were packed as re and im; split their spectra back out, then
        // filter, convert to dB and quantize one of them.
        private static void Quantize(Plan plan, float[] re, float[] im, bool second, float[] power,
                byte[] dest, int offset) {
            int fftSize = plan.FftSize;
            int bins = fftSize / 2 + 1;
            for (int k = 0; k < bins; ++k) {
                int m = (fftSize - k) & (fftSize - 1);
                float xr, xi;
                if (!second) {
                    xr = 0.5f * (re[k] + re[m]);
                    xi = 0.5f * (im[k] - im[m]);
                } else {
                    xr = 0.5f * (im[k] + im[m]);
                    xi = -0.5f * (re[k] - re[m]);
                }
                power[k] = xr * xr + xi * xi;
            }
            for (int b = 0; b < plan.Bands; ++b) {
                var weights = plan.FilterWeights[b];
                int start = plan.FilterStart[b];
                double sum = 0;
                for (int i = 0; i < weights.Length; ++i) {
                    sum += weights[i] * power[start + i];
                }
                double db = 10 * Math.Log10(sum * plan.PowerNorm + 1e-12);
                double t = Math.Clamp((db - MinDb) / -MinDb, 0, 1);
                dest[offset + b] = (byte)(1 + Math.Round(t * 254));
            }
        }
    }
}
