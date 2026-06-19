using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NumSharp;
using OpenUtau.Classic;
using OpenUtau.Core.Format;
using Serilog;

namespace OpenUtau.Core.Render {
    public class SynthRequestError : Exception { }

    public class CutOffExceedDurationError : SynthRequestError { }

    public class CutOffBeforeOffsetError : SynthRequestError { }

    public static class Worldline {
        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int F0(
            float[] samples, int length, int fs, double framePeriod, int method, ref IntPtr f0);

        public static double[] F0(float[] samples, int fs, double framePeriod, int method) {
            try {
                unsafe {
                    IntPtr buffer = IntPtr.Zero;
                    int size = F0(samples, samples.Length, fs, framePeriod, method, ref buffer);
                    var data = new double[size];
                    Marshal.Copy(buffer, data, 0, size);
                    return data;
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to calculate f0.");
                return null;
            }
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int DecodeMgc(
            int f0Length, double[] mgc, int mgcSize,
            int fftSize, int fs, ref IntPtr spectrogram);

        public static double[,] DecodeMgc(int f0Length, double[] mgc, int fftSize, int fs) {
            try {
                int mgcSize = mgc.Length / f0Length;
                unsafe {
                    IntPtr buffer = IntPtr.Zero;
                    int size = DecodeMgc(f0Length, mgc, mgcSize, fftSize, fs, ref buffer);
                    var data = new double[f0Length * size];
                    Marshal.Copy(buffer, data, 0, data.Length);
                    Marshal.FreeCoTaskMem(buffer);
                    var output = new double[f0Length, size];
                    Buffer.BlockCopy(data, 0, output, 0, data.Length * sizeof(double));
                    return output;
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to decode.");
                return null;
            }
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int DecodeBap(
            int f0Length, double[] bap,
            int fftSize, int fs, ref IntPtr aperiodicity);

        public static double[,] DecodeBap(int f0Length, double[] bap, int fftSize, int fs) {
            try {
                unsafe {
                    IntPtr buffer = IntPtr.Zero;
                    int size = DecodeBap(f0Length, bap, fftSize, fs, ref buffer);
                    var data = new double[f0Length * size];
                    Marshal.Copy(buffer, data, 0, data.Length);
                    Marshal.FreeCoTaskMem(buffer);
                    var output = new double[f0Length, size];
                    Buffer.BlockCopy(data, 0, output, 0, data.Length * sizeof(double));
                    return output;
                }
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
        static extern unsafe void WorldAnalysis(
            ref AnalysisConfig config, float[] samples, int num_samples,
            double** f0_out, double** sp_env_out, double** ap_out,
            ref int num_frames);
        public static unsafe void WorldAnalysis(ref AnalysisConfig config, float[] samples,
            out NDArray f0Out, out NDArray spEnv, out NDArray ap, out int num_frames) {
            double* f0Ptr = null;
            double* spEnvPtr = null;
            double* apPtr = null;
            num_frames = 0;

            WorldAnalysis(ref config, samples, samples.Length, &f0Ptr, &spEnvPtr, &apPtr, ref num_frames);

            int spSize = config.fft_size / 2 + 1;

            f0Out = np.ndarray(new Shape(num_frames), typeof(double));
            spEnv = np.ndarray(new Shape(num_frames, spSize), typeof(double));
            ap = np.ndarray(new Shape(num_frames, spSize), typeof(double));

            Buffer.MemoryCopy(f0Ptr, f0Out.Data<double>().Address,
                num_frames * sizeof(double), num_frames * sizeof(double));
            Buffer.MemoryCopy(spEnvPtr, spEnv.Data<double>().Address,
                num_frames * spSize * sizeof(double), num_frames * spSize * sizeof(double));
            Buffer.MemoryCopy(apPtr, ap.Data<double>().Address,
                num_frames * spSize * sizeof(double), num_frames * spSize * sizeof(double));

            Marshal.FreeCoTaskMem(new IntPtr(f0Ptr));
            Marshal.FreeCoTaskMem(new IntPtr(spEnvPtr));
            Marshal.FreeCoTaskMem(new IntPtr(apPtr));
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
        static extern int WorldSynthesis(
            double[] f0, int f0Length,
            double[,] mgcOrSp, bool isMgc, int mgcSize,
            double[,] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs, ref IntPtr y,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing);

        public static double[] WorldSynthesis(
            double[] f0,
            double[,] mgcOrSp, bool isMgc, int mgcSize,
            double[,] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing) {
            unsafe {
                IntPtr buffer = IntPtr.Zero;
                int size = WorldSynthesis(
                    f0, f0.Length,
                    mgcOrSp, isMgc, mgcSize,
                    bapOrAp, isBap, fftSize,
                    framePeriod, fs, ref buffer,
                    gender, tension, breathiness, voicing);
                var data = new double[size];
                Marshal.Copy(buffer, data, 0, size);
                Marshal.FreeCoTaskMem(buffer);
                return data;
            }
        }

        [DllImport("worldline", CallingConvention = CallingConvention.Cdecl)]
        static extern int WorldSynthesis(
            double[] f0, int f0Length,
            double[] mgcOrSp, bool isMgc, int mgcSize,
            double[] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs, ref IntPtr y,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing);

        public static double[] WorldSynthesis(
            double[] f0,
            double[] mgcOrSp, bool isMgc, int mgcSize,
            double[] bapOrAp, bool isBap, int fftSize,
            double framePeriod, int fs,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing) {
            unsafe {
                IntPtr buffer = IntPtr.Zero;
                int size = WorldSynthesis(
                    f0, f0.Length,
                    mgcOrSp, isMgc, mgcSize,
                    bapOrAp, isBap, fftSize,
                    framePeriod, fs, ref buffer,
                    gender, tension, breathiness, voicing);
                var data = new double[size];
                Marshal.Copy(buffer, data, 0, size);
                Marshal.FreeCoTaskMem(buffer);
                return data;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SynthRequest {
            public int sample_fs;
            public int sample_length;
            public IntPtr sample;
            public int frq_length;
            public IntPtr frq;
            public int tone;
            public double con_vel;
            public double offset;
            public double required_length;
            public double consonant;
            public double cut_off;
            public double volume;
            public double modulation;
            public double tempo;
            public int pitch_bend_length;
            public IntPtr pitch_bend;
            public int flag_g;
            public int flag_O;
            public int flag_P;
            public int flag_Mt;
            public int flag_Mb;
            public int flag_Mv;
        };

        class SynthRequestWrapper : IDisposable {
            public SynthRequest request;
            private bool disposedValue;
            private GCHandle[] handles;

            public SynthRequestWrapper(ResamplerItem item) {
                int fs;
                double[] sample;
                using (var waveStream = Wave.OpenFile(item.inputFile)) {
                    fs = waveStream.WaveFormat.SampleRate;
                    sample = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0))
                        .Select(f => (double)f).ToArray();
                }
                string frqFile = VoicebankFiles.GetFrqFile(item.inputFile);
                GCHandle? pinnedFrq = null;
                byte[] frq = null;
                if (File.Exists(frqFile)) {
                    using (var frqStream = File.OpenRead(frqFile)) {
                        using (var memStream = new MemoryStream()) {
                            frqStream.CopyTo(memStream);
                            frq = memStream.ToArray();
                            pinnedFrq = GCHandle.Alloc(frq, GCHandleType.Pinned);
                        }
                    }
                }

                var pinnedSample = GCHandle.Alloc(sample, GCHandleType.Pinned);
                var pinnedPitchBend = GCHandle.Alloc(item.pitches, GCHandleType.Pinned);
                handles = pinnedFrq == null
                    ? new[] { pinnedSample, pinnedPitchBend }
                    : new[] { pinnedSample, pinnedPitchBend, pinnedFrq.Value };
                request = new SynthRequest {
                    sample_fs = fs,
                    sample_length = sample.Length,
                    sample = pinnedSample.AddrOfPinnedObject(),
                    frq_length = frq?.Length ?? 0,
                    frq = pinnedFrq?.AddrOfPinnedObject() ?? IntPtr.Zero,
                    tone = item.tone,
                    con_vel = item.velocity,
                    offset = item.offset,
                    required_length = item.durRequired,
                    consonant = item.consonant,
                    cut_off = item.cutoff,
                    volume = item.phone.direct ? 0 : item.volume,
                    modulation = item.modulation,
                    tempo = item.tempo,
                    pitch_bend_length = item.pitches.Length,
                    pitch_bend = pinnedPitchBend.AddrOfPinnedObject(),
                    flag_g = 0,
                    flag_O = 0,
                    flag_P = 86,
                    flag_Mt = 0,
                    flag_Mb = 0,
                    flag_Mv = 100,
                };
                var flag = item.flags.FirstOrDefault(f => f.Item1 == "g");
                if (flag != null && flag.Item2.HasValue) {
                    request.flag_g = flag.Item2.Value;
                }
                flag = item.flags.FirstOrDefault(f => f.Item1 == "O");
                if (flag != null && flag.Item2.HasValue) {
                    request.flag_O = flag.Item2.Value;
                }
                flag = item.flags.FirstOrDefault(f => f.Item1 == "P");
                if (flag != null && flag.Item2.HasValue) {
                    request.flag_P = flag.Item2.Value;
                }
                flag = item.flags.FirstOrDefault(f => f.Item1 == "Mt");
                if (flag != null && flag.Item2.HasValue) {
                    request.flag_Mt = flag.Item2.Value;
                }
                flag = item.flags.FirstOrDefault(f => f.Item1 == "Mb");
                if (flag != null && flag.Item2.HasValue) {
                    request.flag_Mb = flag.Item2.Value;
                }
                flag = item.flags.FirstOrDefault(f => f.Item1 == "Mv");
                if (flag != null && flag.Item2.HasValue) {
                    request.flag_Mv = flag.Item2.Value;
                }
                Validate(request);
            }
            static void Validate(SynthRequest request) {
                int frame_ms = 10;
                var total_ms = 1000.0 * request.sample_length / request.sample_fs; 
                var in_start_ms = (double)request.offset;
                var in_length_ms = request.cut_off < 0
                    ? -request.cut_off
                    : total_ms - request.offset - request.cut_off;
                int in_start_frame = (int)(in_start_ms / frame_ms); 
                int in_length_frame = (int)Math.Ceiling((in_start_ms + in_length_ms) / (double)frame_ms) - in_start_frame;
                if (in_start_ms + in_length_ms > total_ms + 0.1) {
                    throw new CutOffExceedDurationError();
                }
                int max_frames = (int)Math.Ceiling(total_ms / (double)frame_ms);
                if (in_start_frame + in_length_frame > max_frames) {
                    in_length_frame = max_frames - in_start_frame;
                }
                if (in_length_frame <= 0) {
                    throw new CutOffBeforeOffsetError();
                }
            }

            protected virtual void Dispose(bool disposing) {
                if (!disposedValue) {
                    foreach (var handle in handles) {
                        handle.Free();
                    }
                    disposedValue = true;
                }
            }

            public void Dispose() {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        [DllImport("worldline")]
        static extern int Resample(IntPtr request, ref IntPtr y);

        public static float[] Resample(ResamplerItem item) {
            var requestWrapper = new SynthRequestWrapper(item);
            SynthRequest request = requestWrapper.request;
            try {
                unsafe {
                    IntPtr buffer = IntPtr.Zero;
                    int size = Resample(new IntPtr(&request), ref buffer);
                    var data = new float[size];
                    Marshal.Copy(buffer, data, 0, size);
                    Marshal.FreeCoTaskMem(buffer);
                    return data;
                }
            } finally {
                requestWrapper.Dispose();
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void LogCallback(string log);

        [DllImport("worldline")]
        static extern IntPtr PhraseSynthNew();

        [DllImport("worldline")]
        static extern void PhraseSynthDelete(IntPtr phrase_synth);

        [DllImport("worldline")]
        static extern void PhraseSynthAddRequest(
            IntPtr phrase_synth, IntPtr request,
            double posMs, double skipMs, double lengthMs,
            double fadeInMs, double fadeOutMs, LogCallback logCallback);

        [DllImport("worldline")]
        static extern void PhraseSynthSetCurves(
            IntPtr phraseSynth, double[] f0,
            double[] gender, double[] tension,
            double[] breathiness, double[] voicing,
            int length, LogCallback logCallback);

        [DllImport("worldline")]
        static extern int PhraseSynthSynth(
            IntPtr phrase_synth,
            ref IntPtr y, LogCallback logCallback);

        public class PhraseSynth : IDisposable {
            private IntPtr ptr;
            private bool disposedValue;

            public PhraseSynth() {
                ptr = PhraseSynthNew();
            }

            protected virtual void Dispose(bool disposing) {
                if (!disposedValue) {
                    PhraseSynthDelete(ptr);
                    disposedValue = true;
                }
            }

            ~PhraseSynth() {
                Dispose(disposing: false);
            }

            public void Dispose() {
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }

            public void AddRequest(
                ResamplerItem item, double posMs, double skipMs,
                double lengthMs, double fadeInMs, double fadeOutMs) {
                var requestWrapper = new SynthRequestWrapper(item);
                SynthRequest request = requestWrapper.request;
                try {
                    unsafe {
                        PhraseSynthAddRequest(
                            ptr, new IntPtr(&request),
                            posMs, skipMs, lengthMs,
                            fadeInMs, fadeOutMs, Log.Information);
                    }
                } finally {
                    requestWrapper.Dispose();
                }
            }

            public void SetCurves(
                double[] f0, double[] gender,
                double[] tension, double[] breathiness,
                double[] voicing) {
                PhraseSynthSetCurves(
                    ptr, f0,
                    gender, tension, breathiness, voicing,
                    f0.Length, Log.Information);
            }

            public float[] Synth() {
                IntPtr buffer = IntPtr.Zero;
                int size = PhraseSynthSynth(ptr, ref buffer, Log.Information);
                var data = new float[size];
                Marshal.Copy(buffer, data, 0, size);
                Marshal.FreeCoTaskMem(buffer);
                return data;
            }
        }

        class SynthSegment {
            public readonly AnalysisConfig config;
            public NDArray f0;
            public NDArray spEnv;
            public NDArray ap;

            public int skipFrames;
            public int p0;
            public int p1;
            public int p3;
            public int p4;

            public SynthSegment(AnalysisConfig cfg, ResamplerItem item,
                double posMs, double skipMs, double lengthMs,
                double fadeInMs, double fadeOutMs) {
                const int fs = 44100;
                config = cfg;
                float[] samples = new float[0];
                using (var waveStream = Wave.OpenFile(item.inputFile)) {
                    int wavFs = waveStream.WaveFormat.SampleRate;
                    if (wavFs != fs) {
                        throw new Exception($"Unsupported sample rate {wavFs} Hz in {item.inputFile}. Only {fs} Hz is supported.");
                    }
                    samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0)).ToArray();
                }
                if (samples.Length == 0) {
                    throw new Exception($"Empty samples in {item.inputFile}.");
                }

                var frq = new Frq();
                bool hasFrq = frq.Load(item.inputFile);
                var f0Src = F0(samples, fs, cfg.frame_ms, hasFrq ? -1 : 0);
                if (hasFrq) {
                    for (int i = 0; i < f0Src.Length; ++i) {
                        double ratio = (double)config.hop_size / frq.hopSize;
                        int index0 = (int)Math.Floor(i * ratio);
                        int index1 = (int)Math.Ceiling((i + 1) * ratio);
                        index0 = Math.Min(frq.f0.Length - 1, index0);
                        index1 = Math.Min(frq.f0.Length - 1, index1);
                        double sumF0 = 0.0;
                        int count = 0;
                        for (int j = index0; j <= index1; ++j) {
                            if (frq.f0[j] > config.f0_floor) {
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

                int srcStartFrame = (int)(item.offset / cfg.frame_ms);
                srcStartFrame = Math.Max(0, srcStartFrame);
                double srcEndMs = item.cutoff < 0
                    ? -item.cutoff + item.offset
                    : (samples.Length / (double)fs * 1000.0) - item.cutoff;
                int srcEndFrame = (int)Math.Ceiling(srcEndMs / cfg.frame_ms);
                srcEndFrame = Math.Min(f0Src.Length, srcEndFrame);
                if (srcEndFrame <= srcStartFrame) {
                    throw new CutOffBeforeOffsetError();
                }

                float wavMax = samples.Max(s => Math.Abs(s));

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

                // Gain control
                float gain = item.volume * 0.01f;
                int flag_P = 86;
                var itemFlag = item.flags.FirstOrDefault(f => f.Item1 == "P");
                if (itemFlag != null && itemFlag.Item2.HasValue) {
                    flag_P = itemFlag.Item2.Value;
                }
                float autoGain = GetAutoGain(samples, f0Src, wavMax, flag_P);
                gain *= autoGain;
                for (int i = 0; i < samples.Length; ++i) {
                    samples[i] = samples[i] * gain;
                }

                WorldAnalysisF0In(ref cfg, samples, f0Src, out var spEnvSrc, out var apSrc);

                double[] tSrc = new double[srcEndFrame - srcStartFrame];
                for (int i = 0; i < tSrc.Length; ++i) {
                    tSrc[i] = i * cfg.frame_ms;
                }
                double[] tDst = new double[(int)Math.Ceiling(item.durRequired / cfg.frame_ms)];
                {
                    double srcLengthMs = tSrc.Length * cfg.frame_ms;
                    double consonantSpeed = Math.Pow(0.5, 1.0 - item.velocity / 100.0);
                    double srcConsonantMs = item.consonant;
                    double srcVowelMs = srcLengthMs - srcConsonantMs;
                    double dstLengthMs = tDst.Length * cfg.frame_ms;
                    double dstConsonantMs = srcConsonantMs / consonantSpeed;
                    double dstVowelMs = dstLengthMs - dstConsonantMs;
                    double vowelSpeed = dstVowelMs > 0 ? srcVowelMs / dstVowelMs : 1.0;

                    for (int i = 0; i < tDst.Length; ++i) {
                        double dstMs = i * cfg.frame_ms;
                        if (dstMs < dstConsonantMs) {
                            double srcMs = dstMs * consonantSpeed;
                            tDst[i] = srcMs / cfg.frame_ms + srcStartFrame;
                        } else {
                            double vowelMs = dstMs - dstConsonantMs;
                            double srcMs = srcConsonantMs + vowelMs * vowelSpeed;
                            tDst[i] = srcMs / cfg.frame_ms + srcStartFrame;
                        }
                    }
                }

                var f0Dst = np.ndarray(new Shape(tDst.Length), typeof(double));
                var spEnvDst = np.ndarray(new Shape(tDst.Length, spEnvSrc.shape[1]), typeof(double));
                var apDst = np.ndarray(new Shape(tDst.Length, apSrc.shape[1]), typeof(double));
                for (int i = 0; i < tDst.Length; ++i) {
                    double pos = tDst[i];
                    int index = (int)Math.Floor(pos);
                    double frac = pos - index;
                    if (index + 1 < f0Src.Length) {
                        f0Dst[i] = f0Src[index] * (1.0 - frac) + f0Src[index + 1] * frac;
                        spEnvDst[i] = spEnvSrc[index] * (1.0 - frac) + spEnvSrc[index + 1] * frac;
                        apDst[i] = apSrc[index] * (1.0 - frac) + apSrc[index + 1] * frac;
                    } else {
                        f0Dst[i] = f0Src[index];
                        spEnvDst[i] = spEnvSrc[index];
                        apDst[i] = apSrc[index];
                    }
                }

                f0 = f0Dst;
                spEnv = spEnvDst;
                ap = apDst;

                skipFrames = (int)Math.Round(skipMs / cfg.frame_ms);
                p0 = (int)Math.Round(posMs / cfg.frame_ms);
                p1 = (int)Math.Round((posMs + fadeInMs) / cfg.frame_ms);
                p3 = (int)Math.Round((posMs + lengthMs - fadeOutMs) / cfg.frame_ms);
                p4 = (int)Math.Round((posMs + lengthMs) / cfg.frame_ms);
                p0 = Math.Max(0, p0);
                p1 = Math.Max(p0 + 1, p1);
                p3 = Math.Min(p4 - 1, p3);
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

            double[]? f0Curve;
            double[]? genderCurve;
            double[]? tensionCurve;
            double[]? breathinessCurve;
            double[]? voicingCurve;

            public PhraseSynthV2(int fs, int hopSize, int fftSize) {
                config = InitAnalysisConfig(fs, hopSize, fftSize);
            }

            public void AddRequest(ResamplerItem item,
                double posMs, double skipMs, double lengthMs,
                double fadeInMs, double fadeOutMs) {
                segments.Add(new SynthSegment(config, item,
                    posMs, skipMs, lengthMs, fadeInMs, fadeOutMs));
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
                int spSize = config.fft_size / 2 + 1;
                int totalFrames = segments.Max(s => s.p4) + 1;
                NDArray f0Out = np.zeros<double>(totalFrames);
                NDArray spEnvOut = np.full<double>(1e-12, new int[] { totalFrames, spSize });
                NDArray apOut = np.full<double>(1.0, new int[] { totalFrames, spSize });
                NDArray dirty = np.zeros<int>(totalFrames);

                for (int i = 0; i < segments.Count; ++i) {
                    var segment = segments[i];
                    for (int j = segment.p0; j < segment.p4; ++j) {
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

                if (f0Curve != null) {
                    for (int i = 0; i < totalFrames; ++i) {
                        if (f0Out.GetAtIndex<double>(i) > config.f0_floor) {
                            f0Out[i] = f0Curve[i];
                        }
                    }
                }

                return (totalFrames, f0Out, spEnvOut, apOut);
            }

            public float[] Synth() {
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
                    genderCurve ?? Enumerable.Repeat(0.5, totalFrames).ToArray(),
                    tensionCurve ?? Enumerable.Repeat(0.5, totalFrames).ToArray(),
                    breathinessCurve ?? Enumerable.Repeat(0.5, totalFrames).ToArray(),
                    voicingCurve ?? Enumerable.Repeat(1.0, totalFrames).ToArray());
                return samples.Select(s => (float)s).ToArray();
            }
        }
    }
}
