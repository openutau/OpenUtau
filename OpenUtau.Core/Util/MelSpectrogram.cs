using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FftFlat;
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

        // Window and mel filters for one (FftSize, Bands) pair.
        private sealed class Plan {
            private static readonly ConcurrentDictionary<(int, int), Plan> cache = new();

            public readonly int FftSize;
            public readonly int Bands;
            public readonly double[] Window;
            public readonly int[] FilterStart;
            public readonly float[][] FilterWeights;
            public readonly double PowerNorm;

            public static Plan Get(int fftSize, int bands) =>
                cache.GetOrAdd((fftSize, bands), key => new Plan(key.Item1, key.Item2));

            private Plan(int fftSize, int bands) {
                FftSize = fftSize;
                Bands = bands;
                Window = new double[fftSize];
                FilterStart = new int[bands];
                FilterWeights = new float[bands][];

                double sum = 0;
                for (int i = 0; i < fftSize; ++i) {
                    Window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / fftSize);
                    sum += Window[i];
                }
                // A full-scale sine then has a peak-bin power of 1.
                PowerNorm = 1.0 / ((sum / 2) * (sum / 2));

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
            var fft = new RealFourierTransform(fftSize);
            var buffer = new double[fftSize + 2];
            int frames = samples.Length / channels;
            for (int f = 0; f < frameCount; ++f) {
                // Windowed mono audio for one frame.
                long start = (long)(firstFrame + f) * hop - fftSize / 2;
                for (int i = 0; i < fftSize; ++i) {
                    long s = start + i;
                    double v = 0;
                    if (s >= 0 && s < frames) {
                        for (int c = 0; c < channels; ++c) {
                            v += samples[s * channels + c];
                        }
                        v /= channels;
                    }
                    buffer[i] = v * plan.Window[i];
                }
                var spectrum = fft.Forward(buffer);

                // Filter, convert to dB and quantize.
                int offset = destOffset + f * bands;
                for (int b = 0; b < bands; ++b) {
                    var weights = plan.FilterWeights[b];
                    int first = plan.FilterStart[b];
                    double sum = 0;
                    for (int i = 0; i < weights.Length; ++i) {
                        var bin = spectrum[first + i];
                        sum += weights[i] * (bin.Real * bin.Real + bin.Imaginary * bin.Imaginary);
                    }
                    double db = 10 * Math.Log10(sum * plan.PowerNorm + 1e-12);
                    double t = Math.Clamp((db - MinDb) / -MinDb, 0, 1);
                    dest[offset + b] = (byte)(1 + Math.Round(t * 254));
                }
            }
        }
    }
}
