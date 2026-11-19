using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NumSharp;
using OpenUtau.Classic;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Format;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Render {
    public class SynthRequestError : Exception {
        /// <summary>The request that failed, when raised by <see cref="Worldline.PhraseSynthV2"/>.</summary>
        public ResamplerItem? Item { get; internal set; }
    }

    public class CutOffExceedDurationError : SynthRequestError { }

    public class CutOffBeforeOffsetError : SynthRequestError { }

    public static class Worldline {
        // The exports below never allocate: the caller owns every buffer and
        // sizes it with F0FrameCount / WorldSynthesisSampleCount, so nothing
        // allocated in the native heap can outlive the call.

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int F0FrameCount(int length, int fs, double framePeriod, int method);

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int F0(
            float[] samples, int length, int fs, double framePeriod, int method, double[] f0);

        /// <summary>
        /// A source's f0 on cfg's frames: its .frq file when there is one (averaged onto the
        /// frames, voiced frames only), otherwise analyzed.
        /// </summary>
        public static double[] SourceF0(string sourceFile, float[] samples, AnalysisConfig cfg) {
            var frq = new Frq();
            bool hasFrq = frq.Load(sourceFile);
            var f0Src = F0(samples, cfg.fs, cfg.frame_ms, hasFrq ? -1 : 2);
            if (hasFrq) {
                for (int i = 0; i < f0Src.Length; ++i) {
                    double ratio = (double)cfg.hop_size / frq.hopSize;
                    int index0 = (int)Math.Floor(i * ratio);
                    int index1 = (int)Math.Ceiling((i + 1) * ratio);
                    index0 = Math.Min(frq.f0.Length - 1, index0);
                    index1 = Math.Min(frq.f0.Length - 1, index1);
                    double sumF0 = 0.0;
                    int count = 0;
                    for (int j = index0; j <= index1; ++j) {
                        if (frq.f0[j] > cfg.f0_floor) {
                            sumF0 += frq.f0[j];
                            count += 1;
                        }
                    }
                    if (count > 0) {
                        f0Src[i] = sumF0 / count;
                    } else {
                        f0Src[i] = 0.0;
                    }
                }
            }
            return f0Src;
        }

        /// <summary>
        /// Rd per frame (<see cref="GlottalRd"/>) from a harmonic spectral envelope (power,
        /// [frames, fft_size / 2 + 1]) sampled at the harmonics of f0, smoothed over 20 ms.
        /// </summary>
        static double[] RdTrack(NDArray spHarmonic, double[] f0, AnalysisConfig cfg) {
            int frames = Math.Min(f0.Length, spHarmonic.shape[0]);
            int spSize = spHarmonic.shape[1];
            var sp = spHarmonic.ToArray<double>();
            double binHz = (double)cfg.fs / cfg.fft_size;
            var rd = new double[frames];
            var voiced = new bool[frames];
            for (int i = 0; i < frames; ++i) {
                if (f0[i] <= cfg.f0_floor) {
                    continue;
                }
                int n = (int)Math.Min(GlottalRd.MaxFitHz / f0[i], (spSize - 2) * binHz / f0[i]);
                if (n < 2) {
                    continue;
                }
                var amplitudes = new double[n];
                for (int k = 0; k < n; ++k) {
                    double bin = (k + 1) * f0[i] / binHz;
                    int b = (int)bin;
                    double t = bin - b;
                    double power = sp[i * spSize + b] * (1 - t) + sp[i * spSize + b + 1] * t;
                    amplitudes[k] = Math.Sqrt(Math.Max(power, 0));
                }
                rd[i] = GlottalRd.Fit(amplitudes, f0[i]);
                voiced[i] = true;
            }
            return GlottalRd.Smooth(rd, voiced, Math.Max(1, (int)Math.Round(20 / cfg.frame_ms)));
        }

        public static double[] F0(float[] samples, int fs, double framePeriod, int method) {
            try {
                double[] buffer = new double[F0FrameCount(samples.Length, fs, framePeriod, method)];
                int size = F0(samples, samples.Length, fs, framePeriod, method, buffer);
                if (size == buffer.Length) {
                    return buffer;
                }
                var data = new double[size];
                Array.Copy(buffer, data, size);
                return data;
            } catch (Exception e) {
                Log.Error(e, "Failed to calculate f0.");
                return null;
            }
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern void DecodeMgc(
            int f0Length, double[] mgc, int mgcSize,
            int fftSize, int fs, double[] spectrogram);

        public static double[,] DecodeMgc(int f0Length, double[] mgc, int fftSize, int fs) {
            try {
                int mgcSize = mgc.Length / f0Length;
                int spSize = fftSize / 2 + 1;
                var data = new double[f0Length * spSize];
                DecodeMgc(f0Length, mgc, mgcSize, fftSize, fs, data);
                var output = new double[f0Length, spSize];
                Buffer.BlockCopy(data, 0, output, 0, data.Length * sizeof(double));
                return output;
            } catch (Exception e) {
                Log.Error(e, "Failed to decode.");
                return null;
            }
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern void DecodeBap(
            int f0Length, double[] bap,
            int fftSize, int fs, double[] aperiodicity);

        public static double[,] DecodeBap(int f0Length, double[] bap, int fftSize, int fs) {
            try {
                int apSize = fftSize / 2 + 1;
                var data = new double[f0Length * apSize];
                DecodeBap(f0Length, bap, fftSize, fs, data);
                var output = new double[f0Length, apSize];
                Buffer.BlockCopy(data, 0, output, 0, data.Length * sizeof(double));
                return output;
            } catch (Exception e) {
                Log.Error(e, "Failed to decode.");
                return null;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AnalysisConfig {
            public int fs;
            public int hop_size;
            public int fft_size;
            public float f0_floor;
            public double frame_ms;
        };

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern void InitAnalysisConfig(ref AnalysisConfig config,
            int fs, int hop_size, int fft_size);

        public static AnalysisConfig InitAnalysisConfig(int fs, int hop_size, int fft_size) {
            AnalysisConfig config = new AnalysisConfig();
            InitAnalysisConfig(ref config, fs, hop_size, fft_size);
            return config;
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void WorldAnalysisF0In(
            ref AnalysisConfig config, float[] samples, int num_samples,
            double[] f0_in, int num_frames, double* sp_env_out, double* ap_out);
        public static unsafe void WorldAnalysisF0In(ref AnalysisConfig config, float[] samples,
            double[] f0In, out NDArray spEnv, out NDArray ap) {
            int numFrames = f0In.Length;
            int spSize = config.fft_size / 2 + 1;
            spEnv = np.ndarray(new Shape(numFrames, spSize), typeof(double));
            ap = np.ndarray(new Shape(numFrames, spSize), typeof(double));
            WorldAnalysisF0In(ref config, samples, samples.Length, f0In, numFrames,
                spEnv.Data<double>().Address, ap.Data<double>().Address);
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int WorldSynthesisSampleCount(int f0Length, double framePeriod, int fs);

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int WorldSynthesis(
            double[] f0, int f0Length,
            double[,] mgcOrSp, bool isMgc, int mgcSize,
            double[,] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs, double[] y,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing);

        public static double[] WorldSynthesis(
            double[] f0,
            double[,] mgcOrSp, bool isMgc, int mgcSize,
            double[,] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing) {
            var data = new double[WorldSynthesisSampleCount(f0.Length, framePeriod, fs)];
            WorldSynthesis(
                f0, f0.Length,
                mgcOrSp, isMgc, mgcSize,
                bapOrAp, isBap, fftSize,
                framePeriod, fs, data,
                gender, tension, breathiness, voicing);
            return data;
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int WorldSynthesis(
            double[] f0, int f0Length,
            double[] mgcOrSp, bool isMgc, int mgcSize,
            double[] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs, double[] y,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing);

        public static double[] WorldSynthesis(
            double[] f0,
            double[] mgcOrSp, bool isMgc, int mgcSize,
            double[] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing) {
            var data = new double[WorldSynthesisSampleCount(f0.Length, framePeriod, fs)];
            WorldSynthesis(
                f0, f0.Length,
                mgcOrSp, isMgc, mgcSize,
                bapOrAp, isBap, fftSize,
                framePeriod, fs, data,
                gender, tension, breathiness, voicing);
            return data;
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void HnAnalysisF0In(
            ref AnalysisConfig config, float[] samples, float[] harmonic, int num_samples,
            double[] f0_in, int num_frames, double* sp_env_out, double* sp_harmonic_out, double* ap_out);

        /// <summary>
        /// Worldline-R1.1 analysis from a harmonic/noise separation: the envelopes of
        /// samples and of its harmonic part, and the noise/total power ratio as ap.
        /// </summary>
        public static unsafe void HnAnalysisF0In(ref AnalysisConfig config, float[] samples, float[] harmonic,
            double[] f0In, out NDArray spEnv, out NDArray spEnvHarmonic, out NDArray ap) {
            if (harmonic.Length != samples.Length) {
                throw new ArgumentException("harmonic and samples differ in length");
            }
            int numFrames = f0In.Length;
            int spSize = config.fft_size / 2 + 1;
            spEnv = np.ndarray(new Shape(numFrames, spSize), typeof(double));
            spEnvHarmonic = np.ndarray(new Shape(numFrames, spSize), typeof(double));
            ap = np.ndarray(new Shape(numFrames, spSize), typeof(double));
            HnAnalysisF0In(ref config, samples, harmonic, samples.Length, f0In, numFrames,
                spEnv.Data<double>().Address, spEnvHarmonic.Data<double>().Address, ap.Data<double>().Address);
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int WorldSynthesisContinuousNoise(
            double[] f0, int f0Length, double[] sp, double[] harmonicSp, double[] ap, double[] stretch,
            int fftSize, int hopSize, int fs, ulong seed, double[] y,
            double[] gender, double[] tension, double[] breathiness, double[] voicing);

        /// <summary>
        /// Worldline-R1.1 synthesis: WORLD's periodic half plus continuous noise (see
        /// worldline.h). Features are frames x (fftSize/2+1), row-major.
        /// </summary>
        public static double[] WorldSynthesisContinuousNoise(
            double[] f0, double[] sp, double[] harmonicSp, double[] ap, double[] stretch,
            int fftSize, int hopSize, int fs, ulong seed,
            double[] gender, double[] tension, double[] breathiness, double[] voicing) {
            var data = new double[WorldSynthesisSampleCount(f0.Length, hopSize * 1000.0 / fs, fs)];
            WorldSynthesisContinuousNoise(
                f0, f0.Length, sp, harmonicSp, ap, stretch,
                fftSize, hopSize, fs, seed, data,
                gender, tension, breathiness, voicing);
            return data;
        }

        const int ResamplerPadding = 2;
        // world::kFloorF0StoneMask, the voiced threshold of the resampler auto gain.
        internal const double ResamplerVoicedF0 = 40.0;

        /// <summary>
        /// The resampler auto gain, (0.5 / peak)^(P / 100): the peak is the output's blended with
        /// the whole source file's by voiced ratio, so a mostly unvoiced note (a consonant, a
        /// breath) takes the file's peak instead of being overamplified.
        /// </summary>
        internal static double ResamplerAutoGain(double outMax, double wavMax, double voicedRatio, int peakComp) {
            double weight = 1.0 / (1.0 + Math.Exp(5.0 - 10.0 * voicedRatio));
            double max = outMax * weight + wavMax * (1.0 - weight);
            return max == 0 ? 1.0 : Math.Pow(0.5 / max, peakComp * 0.01);
        }

        /// <summary>
        /// Worldline resampler: renders one note to exactly item.durRequired ms,
        /// leaving envelopes and overlaps to the wavtool.
        /// </summary>
        public static float[] Resample(ResamplerItem item) {
            var config = InitAnalysisConfig(44100, 441, 2048);
            var segment = new SynthSegment(config, item);
            int spSize = config.fft_size / 2 + 1;
            double frameMs = config.frame_ms;
            int fs = config.fs;

            // Pad edge frames so synthesis settles before the audio that is kept.
            int length = segment.f0.size;
            int total = length + 2 * ResamplerPadding;
            double[] segF0 = segment.f0.ToArray<double>();
            double[] segSp = segment.spEnv.ToArray<double>();
            double[] segAp = segment.ap.ToArray<double>();
            var f0 = new double[total];
            var sp = new double[total * spSize];
            var ap = new double[total * spSize];
            for (int i = 0; i < total; ++i) {
                int src = Math.Clamp(i - ResamplerPadding, 0, length - 1);
                f0[i] = segF0[src];
                Array.Copy(segSp, src * spSize, sp, i * spSize, spSize);
                Array.Copy(segAp, src * spSize, ap, i * spSize, spSize);
            }

            // Output starts after the padding plus the sub-frame part of the offset,
            // as in the native resampler.
            double startMs = ResamplerPadding * frameMs + segment.offsetFracMs;

            // Pitch bend: one value in cents per 5 ticks, from the output start.
            // Unvoiced frames keep their analyzed f0.
            double stepMs = 60000.0 / item.tempo / 480.0 * 5;
            for (int i = 0; i < total; ++i) {
                if (f0[i] <= config.f0_floor) {
                    continue;
                }
                double pitch = 0;
                if (item.pitches.Length > 0) {
                    double pos = Math.Clamp((i * frameMs - startMs) / stepMs, 0, item.pitches.Length - 1);
                    int index = (int)Math.Floor(pos);
                    double t = pos - index;
                    pitch = index + 1 < item.pitches.Length
                        ? item.pitches[index] * (1 - t) + item.pitches[index + 1] * t
                        : item.pitches[index];
                }
                f0[i] = MusicMath.ToneToFreq(item.tone + pitch * 0.01);
            }

            // The flags plus the curves (on the pitch bend's grid), each relative to its default.
            int flagG = GetFlag(item, "g", 0);
            int flagMt = GetFlag(item, "Mt", 0);
            int flagMb = GetFlag(item, "Mb", 0);
            int flagMv = GetFlag(item, "Mv", 100);
            double[] Curve(float[]? curve, double defaultValue, Func<double, double> convert) {
                var values = new double[total];
                for (int i = 0; i < total; ++i) {
                    double v = defaultValue;
                    if (curve != null && curve.Length > 0) {
                        double pos = Math.Clamp((i * frameMs - startMs) / stepMs, 0, curve.Length - 1);
                        int index = (int)Math.Floor(pos);
                        double t = pos - index;
                        v = index + 1 < curve.Length ? curve[index] * (1 - t) + curve[index + 1] * t : curve[index];
                    }
                    values[i] = convert(v);
                }
                return values;
            }

            double[] samples = WorldSynthesis(
                f0, sp, false, spSize, ap, false, config.fft_size, frameMs, fs,
                Curve(item.gender, 0, x => 0.5 + 0.005 * Math.Clamp(flagG + x, -100, 100)),
                Curve(item.tension, 0, x => 0.5 + 0.005 * Math.Clamp(flagMt + x, -100, 100)),
                Curve(item.breathiness, 0, x => 0.5 + 0.005 * Math.Clamp(flagMb + x, -100, 100)),
                Curve(item.voicing, 100, x => 0.01 * Math.Clamp(flagMv + x - 100, 0, 100)));

            int startSample = Math.Min(samples.Length, (int)(startMs * fs / 1000));
            int lengthSamples = Math.Min(samples.Length - startSample, (int)(item.durRequired * fs / 1000));
            var output = new float[lengthSamples];
            for (int i = 0; i < lengthSamples; ++i) {
                output[i] = (float)samples[startSample + i];
            }

            double voicedRatio = f0.Count(f => f > ResamplerVoicedF0) / (double)f0.Length;
            double outMax = output.Length > 0 ? output.Max(s => Math.Abs(s)) : 0;
            double gain = (item.phone.direct ? 0 : item.volume) * 0.01;
            double autoGain = ResamplerAutoGain(outMax, segment.wavMax, voicedRatio, GetFlag(item, "P", 86));
            if (autoGain * gain != 1) {
                for (int i = 0; i < output.Length; ++i) {
                    output[i] = (float)(output[i] * autoGain * gain);
                }
            }
            return output;
        }

        static int GetFlag(ResamplerItem item, string name, int defaultValue) {
            var flag = item.flags.FirstOrDefault(f => f.Item1 == name);
            return flag != null && flag.Item2.HasValue ? flag.Item2.Value : defaultValue;
        }

        class SynthSegment {
            public readonly AnalysisConfig config;
            public NDArray f0;
            public NDArray spEnv;
            public NDArray ap;

            public readonly float wavMax;
            // Sub-frame part of the oto offset; frame 0 of f0/spEnv/ap is at the exact offset.
            public readonly double offsetFracMs;

            public int skipFrames;
            public int p0;
            public int p1;
            public int p3;
            public int p4;

            /// <summary>
            /// The phrase frames in [p0, p4) the segment has analysis for. A preutterance lengthened
            /// past the oto's skips a negative amount, leaving frames before the analysis; those stay
            /// silent, as the wavtool leaves them.
            /// </summary>
            public int FirstFrame => Math.Max(p0, p0 - skipFrames);
            public int EndFrame => Math.Min(p4, p0 - skipFrames + f0.size);

            // Worldline-R1.1 only (null / unset otherwise): the harmonic part's spectral
            // envelope on the same frames as spEnv (ap then holds the separation's power
            // ratio), and output ms per source ms for each frame.
            public NDArray? spEnvHarmonic;
            public double[] stretch = Array.Empty<double>();
            // Worldline-R1.1: the source's Rd per frame, for tension.
            public double[] rd = Array.Empty<double>();

            /// <summary>Segment of a phrase, with the input gain applied before analysis.</summary>
            public SynthSegment(AnalysisConfig cfg, ResamplerItem item,
                double posMs, double skipMs, double lengthMs,
                double fadeInMs, double fadeOutMs, Hnsep? hnsep = null) : this(cfg, item, forResampler: false, hnsep) {
                skipFrames = (int)Math.Round(skipMs / cfg.frame_ms);
                p0 = (int)Math.Round(posMs / cfg.frame_ms);
                p1 = (int)Math.Round((posMs + fadeInMs) / cfg.frame_ms);
                p3 = (int)Math.Round((posMs + lengthMs - fadeOutMs) / cfg.frame_ms);
                p4 = (int)Math.Round((posMs + lengthMs) / cfg.frame_ms);
                p0 = Math.Max(0, p0);
                p1 = Math.Max(p0 + 1, p1);
                p3 = Math.Min(p4 - 1, p3);
            }

            /// <summary>
            /// Segment for the resampler: no input gain, since the resampler gains its
            /// output instead, and a cutoff past the end of the file is an error.
            /// </summary>
            public SynthSegment(AnalysisConfig cfg, ResamplerItem item) : this(cfg, item, forResampler: true, null) { }

            SynthSegment(AnalysisConfig cfg, ResamplerItem item, bool forResampler, Hnsep? hnsep) {
                const int fs = 44100;
                config = cfg;
                float[] samples = new float[0];
                using (var waveStream = Wave.OpenFile(item.inputFile)) {
                    // GetSamples resamples to 44.1 kHz, the rate .frq files are timed in too.
                    samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0)).ToArray();
                }
                if (samples.Length == 0) {
                    throw new Exception($"Empty samples in {item.inputFile}.");
                }

                var f0Src = SourceF0(item.inputFile, samples, cfg);

                int srcStartFrame = (int)(item.offset / cfg.frame_ms);
                srcStartFrame = Math.Max(0, srcStartFrame);
                offsetFracMs = Math.Max(0, item.offset - srcStartFrame * cfg.frame_ms);
                double wavMs = samples.Length / (double)fs * 1000.0;
                double srcEndMs = item.cutoff < 0
                    ? -item.cutoff + item.offset
                    : wavMs - item.cutoff;
                if (forResampler && srcEndMs > wavMs + 0.1) {
                    throw new CutOffExceedDurationError();
                }
                int srcEndFrame = (int)Math.Ceiling(srcEndMs / cfg.frame_ms);
                srcEndFrame = Math.Min(f0Src.Length, srcEndFrame);
                if (srcEndFrame <= srcStartFrame) {
                    throw new CutOffBeforeOffsetError();
                }

                wavMax = samples.Max(s => Math.Abs(s));

                int trimStartFrame = Math.Max(0, srcStartFrame - 2);
                int trimEndFrame = Math.Min(f0Src.Length, srcEndFrame + 2);
                srcStartFrame -= trimStartFrame;
                srcEndFrame -= trimStartFrame;
                f0Src = f0Src[trimStartFrame..trimEndFrame];
                int trimStartSample = trimStartFrame * cfg.hop_size;
                int trimEndSample = Math.Min(samples.Length, trimEndFrame * cfg.hop_size);
                var untrimmedSamples = samples;
                samples = new float[(trimEndFrame - trimStartFrame) * cfg.hop_size];
                Array.Copy(untrimmedSamples, trimStartSample, samples, 0, trimEndSample - trimStartSample);

                // Worldline-R1.1: the region of the whole file's harmonic part, cached next to the source.
                float[]? harmonic = null;
                if (hnsep != null) {
                    if (hnsep.SampleRate != fs) {
                        throw new NotSupportedException($"hnsep model sample rate {hnsep.SampleRate} Hz, expected {fs} Hz.");
                    }
                    var harmonicFile = HnsepCache.Harmonic(item.inputFile, untrimmedSamples, hnsep.Harmonic);
                    harmonic = new float[samples.Length];
                    Array.Copy(harmonicFile, trimStartSample, harmonic, 0, trimEndSample - trimStartSample);
                }

                if (!forResampler) {
                    float gain = item.volume * 0.01f * GetAutoGain(samples, f0Src, wavMax, GetFlag(item, "P", 86));
                    for (int i = 0; i < samples.Length; ++i) {
                        samples[i] = samples[i] * gain;
                    }
                    if (harmonic != null) {
                        for (int i = 0; i < harmonic.Length; ++i) {
                            harmonic[i] *= gain;
                        }
                    }
                }

                NDArray spEnvSrc, apSrc;
                NDArray? spEnvHarmonicSrc = null;
                double[]? rdSrc = null;
                if (harmonic != null) {
                    // R1.1: ap from the separation's power ratio, and the harmonic half's own
                    // envelope (the input's envelope also carries its noise energy).
                    HnAnalysisF0In(ref cfg, samples, harmonic, f0Src, out spEnvSrc, out var spEnvHarmonicOut, out apSrc);
                    spEnvHarmonicSrc = spEnvHarmonicOut;
                    rdSrc = RdTrack(spEnvHarmonicOut, f0Src, cfg);
                } else {
                    WorldAnalysisF0In(ref cfg, samples, f0Src, out spEnvSrc, out apSrc);
                }

                double[] tSrc = new double[srcEndFrame - srcStartFrame];
                for (int i = 0; i < tSrc.Length; ++i) {
                    tSrc[i] = i * cfg.frame_ms;
                }
                double[] tDst = new double[(int)Math.Ceiling(item.durRequired / cfg.frame_ms)];
                stretch = new double[tDst.Length];
                {
                    double srcLengthMs = tSrc.Length * cfg.frame_ms - offsetFracMs;
                    double consonantSpeed = Math.Pow(0.5, 1.0 - item.velocity / 100.0);
                    double srcConsonantMs = item.consonant;
                    double srcVowelMs = srcLengthMs - srcConsonantMs;
                    double dstLengthMs = tDst.Length * cfg.frame_ms;
                    double dstConsonantMs = srcConsonantMs / consonantSpeed;
                    double dstVowelMs = dstLengthMs - dstConsonantMs;
                    double vowelSpeed = dstVowelMs > 0 ? Math.Clamp(srcVowelMs / dstVowelMs, 0.01, 1.0) : 1.0;

                    for (int i = 0; i < tDst.Length; ++i) {
                        double dstMs = i * cfg.frame_ms;
                        if (dstMs < dstConsonantMs) {
                            double srcMs = dstMs * consonantSpeed;
                            tDst[i] = (srcMs + offsetFracMs) / cfg.frame_ms + srcStartFrame;
                            stretch[i] = 1.0 / consonantSpeed;
                        } else {
                            double vowelMs = dstMs - dstConsonantMs;
                            double srcMs = srcConsonantMs + vowelMs * vowelSpeed;
                            tDst[i] = (srcMs + offsetFracMs) / cfg.frame_ms + srcStartFrame;
                            stretch[i] = 1.0 / vowelSpeed;
                        }
                    }
                }

                var f0Dst = np.ndarray(new Shape(tDst.Length), typeof(double));
                var spEnvDst = np.ndarray(new Shape(tDst.Length, spEnvSrc.shape[1]), typeof(double));
                var apDst = np.ndarray(new Shape(tDst.Length, apSrc.shape[1]), typeof(double));
                NDArray? spEnvHarmonicDst = spEnvHarmonicSrc is null ? null
                    : np.ndarray(new Shape(tDst.Length, spEnvHarmonicSrc.shape[1]), typeof(double));
                var rdDst = rdSrc is null ? null : new double[tDst.Length];
                for (int i = 0; i < tDst.Length; ++i) {
                    double pos = Math.Max(0, Math.Min(tDst[i], f0Src.Length - 1.0));
                    int index = (int)Math.Floor(pos);
                    double frac = pos - index;
                    if (index + 1 < f0Src.Length) {
                        f0Dst[i] = f0Src[index] * (1.0 - frac) + f0Src[index + 1] * frac;
                        spEnvDst[i] = spEnvSrc[index] * (1.0 - frac) + spEnvSrc[index + 1] * frac;
                        apDst[i] = apSrc[index] * (1.0 - frac) + apSrc[index + 1] * frac;
                        if (spEnvHarmonicDst is not null) {
                            spEnvHarmonicDst[i] = spEnvHarmonicSrc![index] * (1.0 - frac) + spEnvHarmonicSrc[index + 1] * frac;
                        }
                        if (rdDst is not null) {
                            rdDst[i] = rdSrc![Math.Min(index, rdSrc.Length - 1)] * (1.0 - frac) + rdSrc[Math.Min(index + 1, rdSrc.Length - 1)] * frac;
                        }
                    } else {
                        f0Dst[i] = f0Src[index];
                        spEnvDst[i] = spEnvSrc[index];
                        apDst[i] = apSrc[index];
                        if (spEnvHarmonicDst is not null) {
                            spEnvHarmonicDst[i] = spEnvHarmonicSrc![index];
                        }
                        if (rdDst is not null) {
                            rdDst[i] = rdSrc![Math.Min(index, rdSrc.Length - 1)];
                        }
                    }
                }

                f0 = f0Dst;
                spEnv = spEnvDst;
                ap = apDst;
                spEnvHarmonic = spEnvHarmonicDst;
                rd = rdDst ?? Array.Empty<double>();
            }

            float GetAutoGain(float[] samples, double[] f0, float wavMax, int peakComp) {
                float segMax = samples.Max(s => Math.Abs(s));
                double voicedRatio = f0.Count(f => f > config.f0_floor) / (double)f0.Length;
                double weight = 1.0 / (1.0 + Math.Exp(5.0 - 10.0 * voicedRatio));
                float max = segMax * (float)weight + wavMax * (1.0f - (float)weight);
                float autoGain = (max < 1e-3f) ? 1.0f : (float)Math.Pow(0.5 / max, peakComp * 0.01);
                return autoGain;
            }
        }

        public class PhraseSynthV2 {
            readonly AnalysisConfig config;
            readonly List<SynthSegment> segments = new List<SynthSegment>();
            readonly List<(ResamplerItem item, Func<SynthSegment> analyze)> pendingRequests =
                new List<(ResamplerItem, Func<SynthSegment>)>();

            double[]? f0Curve;
            double[]? genderCurve;
            double[]? tensionCurve;
            double[]? breathinessCurve;
            double[]? voicingCurve;

            // Worldline-R1.1: analyzes with the hnsep separation, for SynthContinuousNoise.
            readonly Hnsep? hnsep;

            public PhraseSynthV2(int fs, int hopSize, int fftSize, bool useHnsep = false) {
                config = InitAnalysisConfig(fs, hopSize, fftSize);
                hnsep = useHnsep ? Hnsep.Instance : null;
            }

            /// <summary>Queues a request; the analysis runs in <see cref="AnalyzeRequests"/>.</summary>
            public void AddRequest(ResamplerItem item,
                double posMs, double skipMs, double lengthMs,
                double fadeInMs, double fadeOutMs) {
                var cfg = config;
                var hnsep = this.hnsep;
                pendingRequests.Add((item, () => new SynthSegment(cfg, item,
                    posMs, skipMs, lengthMs, fadeInMs, fadeOutMs, hnsep)));
            }

            /// <summary>
            /// Analyzes the queued requests in parallel, using up to NumRenderThreads threads.
            /// Rethrows the failure of the earliest failing request; a <see cref="SynthRequestError"/>
            /// carries that request as <see cref="SynthRequestError.Item"/>.
            /// </summary>
            public void AnalyzeRequests(CancellationToken cancellationToken = default) {
                if (pendingRequests.Count == 0) {
                    return;
                }
                var results = new SynthSegment[pendingRequests.Count];
                var errors = new Exception?[pendingRequests.Count];
                Parallel.For(0, pendingRequests.Count, new ParallelOptions {
                    MaxDegreeOfParallelism = Math.Max(1, Preferences.Default.NumRenderThreads),
                    CancellationToken = cancellationToken,
                }, (i, state) => {
                    try {
                        results[i] = pendingRequests[i].analyze();
                    } catch (Exception e) {
                        if (e is SynthRequestError sre) {
                            sre.Item = pendingRequests[i].item;
                        }
                        errors[i] = e;
                        // Break still runs every lower index, so the earliest failure is found.
                        state.Break();
                    }
                });
                var error = errors.FirstOrDefault(e => e != null);
                if (error != null) {
                    ExceptionDispatchInfo.Capture(error).Throw();
                }
                segments.AddRange(results);
                pendingRequests.Clear();
            }

            public void SetCurves(
                double[] f0, double[] gender,
                double[] tension, double[] breathiness,
                double[] voicing) {
                f0Curve = f0;
                genderCurve = gender;
                tensionCurve = tension;
                breathinessCurve = breathiness;
                voicingCurve = voicing;
            }

            public (int, NDArray, NDArray, NDArray) SynthFeatures() {
                AnalyzeRequests();
                int spSize = config.fft_size / 2 + 1;
                int totalFrames = segments.Max(s => s.p4) + 1;
                NDArray f0Out = np.zeros<double>(totalFrames);
                NDArray spEnvOut = np.full<double>(1e-12, new int[] { totalFrames, spSize });
                NDArray apOut = np.full<double>(1.0, new int[] { totalFrames, spSize });
                NDArray dirty = np.zeros<int>(totalFrames);

                for (int i = 0; i < segments.Count; ++i) {
                    var segment = segments[i];
                    for (int j = segment.FirstFrame; j < segment.EndFrame; ++j) {
                        double weight = 1.0;
                        if (j < segment.p1) {
                            weight = (double)(j - segment.p0) / (segment.p1 - segment.p0);
                        } else if (j >= segment.p3) {
                            weight = (double)(segment.p4 - j) / (segment.p4 - segment.p3);
                        }
                        int segIdx = segment.skipFrames + j - segment.p0;
                        if (dirty.GetAtIndex<int>(j) == 0 || weight > 0.5) {
                            f0Out[j] = segment.f0[segIdx];
                        }
                        spEnvOut[j] = spEnvOut[j] + segment.spEnv[segIdx] * weight;
                        double wa = dirty.GetAtIndex<int>(j) == 0 ? 0.0 : (1.0 - weight);
                        double wb = dirty.GetAtIndex<int>(j) == 0 ? 1.0 : weight;
                        apOut[j] = apOut[j] * wa + segment.ap[segIdx] * wb;
                        dirty[j] = 1;
                    }
                }

                // Repeat the last frame; the wavtool fades the phrase out.
                if (totalFrames >= 2) {
                    f0Out[totalFrames - 1] = f0Out[totalFrames - 2];
                    spEnvOut[totalFrames - 1] = spEnvOut[totalFrames - 2];
                    apOut[totalFrames - 1] = apOut[totalFrames - 2];
                }

                if (f0Curve != null) {
                    var f0Fit = FitCurve(f0Curve, totalFrames, 0);
                    for (int i = 0; i < totalFrames; ++i) {
                        if (f0Out.GetAtIndex<double>(i) > config.f0_floor) {
                            f0Out[i] = f0Fit[i];
                        }
                    }
                }

                return (totalFrames, f0Out, spEnvOut, apOut);
            }

            public float[] Synth() {
                AnalyzeRequests();
                if (segments.Count == 0) {
                    return new float[0];
                }
                var (totalFrames, f0Out, spEnvOut, apOut) = SynthFeatures();
                int spSize = config.fft_size / 2 + 1;
                double[] f0Array = f0Out.ToArray<double>();
                double[] spEnvArray = spEnvOut.ToArray<double>();
                double[] apArray = apOut.ToArray<double>();
                double[] samples = WorldSynthesis(
                    f0Array,
                    spEnvArray, false, spSize,
                    apArray, false, config.fft_size,
                    config.frame_ms, config.fs,
                    FitCurve(genderCurve, totalFrames, 0.5),
                    FitCurve(tensionCurve, totalFrames, 0.5),
                    FitCurve(breathinessCurve, totalFrames, 0.5),
                    FitCurve(voicingCurve, totalFrames, 1.0));
                return samples.Select(s => (float)s).ToArray();
            }

            /// <summary>
            /// Worldline-R1.1: WORLD's periodic half plus continuous noise, synthesized by
            /// <see cref="WorldSynthesisContinuousNoise"/>. Segments blend as in
            /// <see cref="SynthFeatures"/>, with each frame's stretch blended like ap and the
            /// harmonic envelope like sp. Needs the hnsep analysis (useHnsep).
            /// </summary>
            public float[] SynthContinuousNoise(ulong seed) {
                if (hnsep == null) {
                    throw new InvalidOperationException("SynthContinuousNoise needs the hnsep analysis.");
                }
                AnalyzeRequests();
                if (segments.Count == 0) {
                    return new float[0];
                }
                int spSize = config.fft_size / 2 + 1;
                int totalFrames = segments.Max(s => s.p4) + 1;
                var f0 = new double[totalFrames];
                var sp = new double[totalFrames * spSize];
                var spHarmonic = new double[totalFrames * spSize];
                var ap = new double[totalFrames * spSize];
                var stretch = new double[totalFrames];
                var rd = new double[totalFrames];
                var dirty = new bool[totalFrames];
                Array.Fill(sp, 1e-12);
                Array.Fill(spHarmonic, 1e-12);
                Array.Fill(ap, 1.0);
                Array.Fill(stretch, 1.0);
                Array.Fill(rd, 1.0);

                foreach (var segment in segments) {
                    var segSp = segment.spEnv.ToArray<double>();
                    var segSpHarmonic = segment.spEnvHarmonic!.ToArray<double>();
                    var segAp = segment.ap.ToArray<double>();
                    for (int j = segment.FirstFrame; j < segment.EndFrame; ++j) {
                        double weight = 1.0;
                        if (j < segment.p1) {
                            weight = (double)(j - segment.p0) / (segment.p1 - segment.p0);
                        } else if (j >= segment.p3) {
                            weight = (double)(segment.p4 - j) / (segment.p4 - segment.p3);
                        }
                        int segIdx = segment.skipFrames + j - segment.p0;
                        if (!dirty[j] || weight > 0.5) {
                            f0[j] = segment.f0.GetAtIndex<double>(segIdx);
                        }
                        double wa = dirty[j] ? 1.0 - weight : 0.0;
                        double wb = dirty[j] ? weight : 1.0;
                        for (int k = 0; k < spSize; ++k) {
                            sp[j * spSize + k] += segSp[segIdx * spSize + k] * weight;
                            spHarmonic[j * spSize + k] += segSpHarmonic[segIdx * spSize + k] * weight;
                            ap[j * spSize + k] = ap[j * spSize + k] * wa + segAp[segIdx * spSize + k] * wb;
                        }
                        stretch[j] = stretch[j] * wa + segment.stretch[segIdx] * wb;
                        rd[j] = rd[j] * wa + segment.rd[segIdx] * wb;
                        dirty[j] = true;
                    }
                }
                if (totalFrames >= 2) {
                    int last = totalFrames - 1;
                    f0[last] = f0[last - 1];
                    stretch[last] = stretch[last - 1];
                    rd[last] = rd[last - 1];
                    Array.Copy(sp, (last - 1) * spSize, sp, last * spSize, spSize);
                    Array.Copy(spHarmonic, (last - 1) * spSize, spHarmonic, last * spSize, spSize);
                    Array.Copy(ap, (last - 1) * spSize, ap, last * spSize, spSize);
                }

                if (f0Curve != null) {
                    var f0Fit = FitCurve(f0Curve, totalFrames, 0);
                    for (int i = 0; i < totalFrames; ++i) {
                        if (f0[i] > config.f0_floor) {
                            f0[i] = f0Fit[i];
                        }
                    }
                }

                var gender = FitCurve(genderCurve, totalFrames, 0.5);
                ApplyRdTension(config, f0, spHarmonic, rd, FitCurve(tensionCurve, totalFrames, 0.5), gender);
                double[] samples = WorldSynthesisContinuousNoise(
                    f0, sp, spHarmonic, ap, stretch,
                    config.fft_size, config.hop_size, config.fs, seed,
                    gender,
                    FitCurve(null, totalFrames, 0.5),  // tension is applied as Rd above
                    FitCurve(breathinessCurve, totalFrames, 0.5),
                    FitCurve(voicingCurve, totalFrames, 1.0));
                return samples.Select(s => (float)s).ToArray();
            }

            /// <summary>
            /// Tension (0.5 + 0.005 x, x in -100..100) as Rd: each voiced frame's harmonic
            /// envelope gets the gains of moving the source's Rd to <see cref="GlottalRd.TenseRd"/>,
            /// on the harmonics of the synthesized f0. The native synthesis then shifts the
            /// envelope for gender, so the gains are placed where that shift brings them home.
            /// </summary>
            internal static void ApplyRdTension(AnalysisConfig config, double[] f0, double[] spHarmonic,
                    double[] rd, double[] tension, double[] gender) {
                int spSize = config.fft_size / 2 + 1;
                double binHz = (double)config.fs / config.fft_size;
                for (int j = 0; j < f0.Length; ++j) {
                    double t = (tension[j] - 0.5) * 200;
                    if (f0[j] <= config.f0_floor || Math.Abs(t) < 1e-9) {
                        continue;
                    }
                    double rd2 = GlottalRd.TenseRd(rd[j], t);
                    var gains = GlottalRd.Gains(rd[j], rd2, f0[j], (int)(config.fs / 2.0 / f0[j]));
                    // As ShiftGender: bin b moves to b / ratio.
                    double ratio = Math.Pow(2, Math.Round((gender[j] - 0.5) * 200, MidpointRounding.AwayFromZero) * 0.01);
                    for (int b = 0; b < spSize; ++b) {
                        double g = GlottalRd.GainAt(gains, f0[j], b * binHz / ratio);
                        spHarmonic[j * spSize + b] *= g * g;
                    }
                }
            }

            /// <summary>Resizes a curve to length frames, padding with its last value.</summary>
            static double[] FitCurve(double[]? curve, int length, double defaultValue) {
                var result = new double[length];
                if (curve == null || curve.Length == 0) {
                    Array.Fill(result, defaultValue);
                    return result;
                }
                int copy = Math.Min(length, curve.Length);
                Array.Copy(curve, result, copy);
                Array.Fill(result, curve[^1], copy, length - copy);
                return result;
            }
        }
    }
}
