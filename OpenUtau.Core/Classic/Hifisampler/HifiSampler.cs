// Ported from hifisampler's backend/resampler.py (https://github.com/openhachimi/hifisampler),
// licensed under the Apache License 2.0; see LICENSE in this folder.
// Modified for OpenUtau: translated to C#, reading OpenUtau's resampler arguments.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>The consonant reaches the cutoff, so there is nothing to stretch or loop.</summary>
    public class ConsonantExceedsCutoffError : SynthRequestError { }

    /// <summary>
    /// hifisampler (openhachimi/hifisampler backend/resampler.py) for one note: the source's
    /// log mel is stretched to the requested length and voiced by pc-nsf-hifigan at the note's
    /// pitch. Kept in the Python's order so the two can be read side by side.
    /// </summary>
    public static class HifiSampler {
        public static float[] Resample(ResamplerItem item) {
            // item.modulation is unused, as in hifisampler.
            return Resample(item.inputFile, item.tone, item.velocity, item.flags, item.offset,
                item.consonant, item.cutoff, item.durRequired, item.volume, item.tempo, item.pitches,
                item.tension, item.breathiness, item.voicing, item.gender, item.growl, new HifiSamplerConfig());
        }

        /// <summary>
        /// The UTAU resampler arguments; pitches are cents relative to tone, every 5 ticks, and
        /// the tension, breathiness, voicing, gender and growl curves (null for their defaults)
        /// are on the same grid.
        /// </summary>
        public static float[] Resample(string inputFile, int tone, int velocity,
                IEnumerable<Tuple<string, int?, string>> flagList, double offset, double consonant,
                double cutoff, double length, int volume, double tempo, int[] pitches,
                float[]? tension, float[]? breathiness, float[]? voicing, float[]? gender, float[]? growl,
                HifiSamplerConfig config) {
            var vocoder = HifiVocoder.Instance;
            config.Validate(vocoder.Config);

            float[] wave;
            using (var waveStream = Wave.OpenFile(inputFile)) {
                // GetSamples resamples to 44.1 kHz, as WorldlineResampler reads its input.
                wave = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
            }
            if (wave.Length == 0) {
                throw new Exception($"Empty samples in {inputFile}.");
            }

            var flags = new HifiFlags(flagList);
            int melFrames = HifiMelSpectrogram.FrameCount(wave.Length, config.OriginHopSize);
            var timing = new HifiNoteTiming(config, melFrames, velocity, offset,
                consonant, cutoff, length, flags.Has("e"));
            var curves = timing.SourceCurves(tension, breathiness, voicing, gender, tone, pitches, tempo, config.OriginHopSize);
            var features = HifiFeatures.Generate(wave, curves, config,
                x => HnsepCache.Harmonic(inputFile, x, Hnsep.Instance.Harmonic),
                () => Worldline.SourceF0(inputFile, wave, Worldline.InitAnalysisConfig(config.SampleRate, HifiRdTension.Hop, 2048))
                    ?? throw new Exception($"Failed to analyze the f0 of {inputFile}."));
            var melRender = timing.RenderMel(features.Mel);

            var t = new double[melRender.Length];
            for (int i = 0; i < t.Length; i++) {
                t[i] = i * timing.Thop;
            }
            var pitchRender = HifiNotePitch.Render(pitches, tone, tempo, timing.NewStart, t);
            var f0 = pitchRender.Select(p => 440 * Math.Pow(2, (p - 69) / 12)).ToArray();

            var wavCon = vocoder.Synthesize(melRender, f0);
            int cutStart = Math.Min(wavCon.Length, (int)(timing.NewStart * config.SampleRate));
            int cutEnd = Math.Clamp((int)(timing.NewEnd * config.SampleRate), cutStart, wavCon.Length);
            var render = wavCon[cutStart..cutEnd];

            // Worldline's auto gain: the whole source's peak, and the voiced ratio of the kept frames
            // from the source f0 (its .frq when there is one), on Worldline's resampler frames.
            double wavMax = HifiArray.MaxAbs(wave);
            double VoicedRatio() {
                var cfg = Worldline.InitAnalysisConfig(config.SampleRate, 441, 2048);
                var f0Src = Worldline.SourceF0(inputFile, wave, cfg);
                return timing.VoicedRatio(f0Src, cfg.frame_ms / 1000, Worldline.ResamplerVoicedF0);
            }

            return HifiPostProcess.Apply(render, features.Scale, flags, config, pitchRender, t,
                timing.NewStart, timing.NewEnd, timing.LengthReq, volume, growl, tempo, wavMax, VoicedRatio);
        }
    }

    /// <summary>A note's flags, the first occurrence of each, as hifisampler parses them.</summary>
    internal sealed class HifiFlags {
        readonly Dictionary<string, int?> values = new Dictionary<string, int?>();

        public HifiFlags(IEnumerable<Tuple<string, int?, string>> flags) {
            foreach (var flag in flags) {
                values.TryAdd(flag.Item1, flag.Item2);
            }
        }

        public bool Has(string name) => values.ContainsKey(name);

        public int? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    /// The time map of Resampler.resample: velocity-scaled consonant, stretched (or looped)
    /// vowel, mel frames cut to the note plus <see cref="HifiSamplerConfig.Fill"/> on each side.
    /// Times in seconds; the "//" floor divisions of the Python are Math.Floor.
    /// </summary>
    internal sealed class HifiNoteTiming {
        public readonly double ThopOrigin;
        public readonly double Thop;
        public readonly double Vel;
        public readonly double Start;
        public readonly double End;
        public readonly double Con;
        public readonly double LengthReq;
        public readonly bool Loop;
        public readonly int ConFrame;
        public readonly int EndFrame;
        public readonly double PadLoopSize;
        /// <summary>The source frames from the consonant end to the cutoff that the loop reflects.</summary>
        public readonly int LoopFrames;
        /// <summary>The source's analysis frames (before any loop).</summary>
        public readonly int SourceFrameCount;
        public double StretchLength { get; private set; }
        public double TotalTime { get; private set; }
        public double ScalingRatio { get; private set; }
        public double StretchedNFrames { get; private set; }
        public double CutLeftMelFrames { get; private set; }
        public double CutRightMelFrames { get; private set; }
        public double NewStart { get; private set; }
        public double NewEnd { get; private set; }

        readonly int fill;
        double[] tAreaOrigin;

        public HifiNoteTiming(HifiSamplerConfig config, int melFrames, int velocity, double offset,
                double consonant, double cutoff, double length, bool loop) {
            ThopOrigin = config.OriginHopSize / (double)config.SampleRate;
            Thop = config.HopSize / (double)config.SampleRate;
            fill = config.Fill;
            Loop = loop;
            SourceFrameCount = melFrames;
            tAreaOrigin = FrameTimes(melFrames);
            TotalTime = tAreaOrigin[^1] + ThopOrigin / 2;

            Vel = Math.Pow(2, 1 - velocity / 100.0);
            Start = offset / 1000;
            End = cutoff < 0 ? Start - cutoff / 1000 : TotalTime - cutoff / 1000;
            Con = Start + consonant / 1000;
            if (End <= Start) {
                throw new CutOffBeforeOffsetError();
            }
            if (End <= Con) {
                throw new ConsonantExceedsCutoffError();
            }
            // hifisampler reads the length argument as an int.
            LengthReq = (int)length / 1000.0;
            StretchLength = End - Con;

            if (loop) {
                ConFrame = (int)Math.Floor((Con + ThopOrigin / 2) / ThopOrigin);
                EndFrame = (int)Math.Floor((End + ThopOrigin / 2) / ThopOrigin);
                PadLoopSize = Math.Floor(LengthReq / ThopOrigin) + 1;
                LoopFrames = Math.Min(EndFrame, melFrames) - ConFrame;
                if (LoopFrames <= 0) {
                    throw new ConsonantExceedsCutoffError();
                }
                StretchLength = PadLoopSize * ThopOrigin;
                tAreaOrigin = FrameTimes(ConFrame + LoopFrames + (int)PadLoopSize);
                TotalTime = tAreaOrigin[^1] + ThopOrigin / 2;
            }

            ScalingRatio = StretchLength < LengthReq ? LengthReq / StretchLength : 1;
            StretchedNFrames = Math.Floor((Con * Vel + (TotalTime - Con) * ScalingRatio) / Thop) + 1;

            double startLeftMelFrames = Math.Floor((Start * Vel + Thop / 2) / Thop);
            CutLeftMelFrames = startLeftMelFrames > fill ? startLeftMelFrames - fill : 0;
            double endRightMelFrames = StretchedNFrames - Math.Floor((LengthReq + Con * Vel + Thop / 2) / Thop);
            CutRightMelFrames = endRightMelFrames > fill ? endRightMelFrames - fill : 0;

            NewStart = Start * Vel - CutLeftMelFrames * Thop;
            NewEnd = (LengthReq + Con * Vel) - CutLeftMelFrames * Thop;
        }

        double[] FrameTimes(int frames) {
            var t = new double[frames];
            for (int i = 0; i < frames; i++) {
                t[i] = i * ThopOrigin + ThopOrigin / 2;
            }
            return t;
        }

        /// <summary>The analysis times (seconds) of the vocoder frames: stretch(), clipped to the source.</summary>
        public double[] SourceTimes() {
            int first = (int)CutLeftMelFrames;
            int last = (int)(StretchedNFrames - CutRightMelFrames);
            var times = new double[Math.Max(0, last - first)];
            for (int i = 0; i < times.Length; i++) {
                double t = (first + i) * Thop + Thop / 2;
                double s = t < Vel * Con ? t / Vel : Con + (t - Vel * Con) / ScalingRatio;
                times[i] = Math.Clamp(s, 0, tAreaOrigin[^1]);
            }
            return times;
        }

        /// <summary>The source mel (looped when <see cref="Loop"/>), interpolated at <see cref="SourceTimes"/>.</summary>
        public float[][] RenderMel(float[][] mel) {
            if (Loop) {
                mel = LoopMel(mel);
            }
            var times = SourceTimes();
            int bins = mel[0].Length;
            var result = new float[times.Length][];
            for (int j = 0; j < times.Length; j++) {
                double v = times[j];
                int i = HifiInterp.Interval(tAreaOrigin, v);
                double x0 = tAreaOrigin[i];
                double x1 = tAreaOrigin[i + 1];
                var lo = mel[i];
                var hi = mel[i + 1];
                var row = new float[bins];
                for (int b = 0; b < bins; b++) {
                    double slope = (hi[b] - (double)lo[b]) / (x1 - x0);
                    row[b] = (float)(slope * (v - x0) + lo[b]);
                }
                result[j] = row;
            }
            return result;
        }

        /// <summary>Frames before the consonant end, then the consonant-to-cutoff frames reflect-padded by PadLoopSize.</summary>
        float[][] LoopMel(float[][] mel) {
            int total = ConFrame + LoopFrames + (int)PadLoopSize;
            var looped = new float[total][];
            for (int i = 0; i < ConFrame; i++) {
                looped[i] = mel[i];
            }
            for (int i = ConFrame; i < total; i++) {
                looped[i] = mel[SourceFrame(i)];
            }
            return looped;
        }

        /// <summary>The source frame of a frame of the (looped, when <see cref="Loop"/>) analysis timeline.</summary>
        public int SourceFrame(int frame) {
            if (!Loop || frame < ConFrame) {
                return frame;
            }
            return ConFrame + HifiArray.ReflectIndex(frame - ConFrame, LoopFrames);
        }

        /// <summary>
        /// The fraction of the vocoder frames in the requested length (the output's first
        /// <see cref="LengthReq"/>, the part the wavtool keeps) whose source frame is voiced, as
        /// Worldline's resampler counts its output frames for the auto gain.
        /// </summary>
        /// <param name="sourceF0">The source's f0 (Hz, 0 unvoiced) every <paramref name="f0FrameSeconds"/>.</param>
        public double VoicedRatio(double[] sourceF0, double f0FrameSeconds, double voicedF0) {
            if (sourceF0.Length == 0) {
                return 0;
            }
            var times = SourceTimes();
            int last = tAreaOrigin.Length - 1;
            int count = 0;
            int voiced = 0;
            for (int k = 0; k < times.Length; k++) {
                // Vocoder frame k is at k * Thop; the output starts at NewStart.
                double t = k * Thop;
                if (t < NewStart || t >= NewStart + LengthReq) {
                    continue;
                }
                int frame = Math.Clamp((int)Math.Round(times[k] / ThopOrigin - 0.5), 0, last);
                double sourceSeconds = (SourceFrame(frame) + 0.5) * ThopOrigin;
                int i = Math.Clamp((int)(sourceSeconds / f0FrameSeconds), 0, sourceF0.Length - 1);
                count++;
                if (sourceF0[i] > voicedF0) {
                    voiced++;
                }
            }
            return count == 0 ? 0 : voiced / (double)count;
        }

        /// <summary>
        /// The note's curves (on the pitch bend grid, from <see cref="NewStart"/>) carried back to
        /// the source frames through the time map. A source frame the loop uses more than once
        /// takes the average of its uses; frames the note doesn't use take the nearest values.
        /// </summary>
        public HifiSourceCurves SourceCurves(float[]? tension, float[]? breathiness, float[]? voicing,
                float[]? gender, int tone, int[] pitches, double tempo, int hop) {
            double step = 60.0 / (tempo * 96);
            var times = SourceTimes();
            double[] Map(float[]? curve, double defaultValue) {
                var result = new double[SourceFrameCount];
                if (curve == null || curve.Length == 0 || curve.All(v => v == defaultValue)) {
                    Array.Fill(result, defaultValue);
                    return result;
                }
                var sum = new double[SourceFrameCount];
                var weight = new double[SourceFrameCount];
                int last = tAreaOrigin.Length - 1;
                for (int k = 0; k < times.Length; k++) {
                    // Vocoder frame k is at k * Thop; the curve starts at NewStart.
                    double pos = Math.Clamp((k * Thop - NewStart) / step, 0, curve.Length - 1);
                    int ci = (int)pos;
                    double value = ci + 1 < curve.Length ? curve[ci] + (curve[ci + 1] - curve[ci]) * (pos - ci) : curve[ci];
                    // The two analysis frames RenderMel interpolates for this vocoder frame.
                    double p = Math.Clamp(times[k] / ThopOrigin - 0.5, 0, last);
                    int f0 = Math.Min((int)p, last);
                    int f1 = Math.Min(f0 + 1, last);
                    double a = p - f0;
                    foreach (var (f, w) in new[] { (f0, 1 - a), (f1, a) }) {
                        int m = SourceFrame(f);
                        if (m < SourceFrameCount) {
                            sum[m] += w * value;
                            weight[m] += w;
                        }
                    }
                }
                var known = Enumerable.Range(0, SourceFrameCount).Where(m => weight[m] > 1e-9).ToArray();
                if (known.Length == 0) {
                    Array.Fill(result, defaultValue);
                    return result;
                }
                return HifiInterp.Linear(
                    Enumerable.Range(0, SourceFrameCount).Select(m => (double)m).ToArray(),
                    known.Select(m => (double)m).ToArray(),
                    known.Select(m => sum[m] / weight[m]).ToArray());
            }
            var pitch = new float[pitches.Length];
            for (int i = 0; i < pitch.Length; i++) {
                pitch[i] = tone + pitches[i] / 100f;
            }
            return new HifiSourceCurves(Map(breathiness, 0), Map(voicing, 100), Map(tension, 0), Map(gender, 0),
                Map(pitch, tone), hop);
        }
    }

    /// <summary>
    /// The note's pitch curve: the pitchbend (cents per 5 ticks, from the note start minus the
    /// stretched preutterance) with the zero point hifisampler's decoder appends, plus the
    /// tone, Akima-interpolated at the vocoder frames.
    /// </summary>
    internal static class HifiNotePitch {
        public static double[] Render(int[] pitches, int tone, double tempo, double newStart, double[] t) {
            var pitch = new double[pitches.Length + 1];
            for (int i = 0; i < pitches.Length; i++) {
                pitch[i] = pitches[i] / 100.0 + tone;
            }
            pitch[^1] = tone;
            var tPitch = new double[pitch.Length];
            for (int i = 0; i < tPitch.Length; i++) {
                tPitch[i] = 60.0 * i / (tempo * 96) + newStart;
            }
            var x = t.Select(v => Math.Clamp(v, newStart, Math.Max(newStart, tPitch[^1]))).ToArray();
            return HifiInterp.Akima(tPitch, pitch, x);
        }
    }

    /// <summary>The pc-nsf-hifigan package's vocoder: log mel [1, T, bins] and f0 [1, T] Hz to a waveform.</summary>
    internal sealed class HifiVocoder {
        public const string PackageId = "pc_nsf_hifigan_44.1k_hop512_128bin_2025.02";

        static readonly object loadLock = new object();
        static HifiVocoder? instance;

        public readonly DsVocoderConfig Config;
        readonly InferenceSession session;
        readonly bool isCpuRunner;
        readonly object runLock = new object();

        HifiVocoder(DsVocoderConfig config, string modelPath) {
            Config = config;
            session = Onnx.getInferenceSession(modelPath);
            isCpuRunner = Onnx.IsCpuRunner();
        }

        /// <summary>The installed package's vocoder, loaded once. Throws when the package is missing.</summary>
        public static HifiVocoder Instance {
            get {
                lock (loadLock) {
                    return instance ??= Load();
                }
            }
        }

        static HifiVocoder Load() {
            string? dir = PackageManager.Inst.GetInstalledPath(PackageId);
            string? configPath = dir == null ? null : Path.Combine(dir, "vocoder.yaml");
            if (configPath == null || !File.Exists(configPath)) {
                throw new MissingPackageException(PackageId);
            }
            var config = Yaml.DefaultDeserializer.Deserialize<DsVocoderConfig>(
                File.ReadAllText(configPath, System.Text.Encoding.UTF8));
            return new HifiVocoder(config, Path.Combine(dir!, config.model));
        }

        public float[] Synthesize(float[][] mel, double[] f0) {
            int frames = mel.Length;
            if (frames == 0) {
                return new float[0];
            }
            int bins = mel[0].Length;
            var melTensor = new DenseTensor<float>(new[] { 1, frames, bins });
            var melSpan = melTensor.Buffer.Span;
            for (int i = 0; i < frames; i++) {
                mel[i].AsSpan().CopyTo(melSpan.Slice(i * bins, bins));
            }
            var f0Tensor = new DenseTensor<float>(f0.Select(v => (float)v).ToArray(), new[] { 1, frames });
            var inputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("mel", melTensor),
                NamedOnnxValue.CreateFromTensor("f0", f0Tensor),
            };
            if (isCpuRunner) {
                return Run(inputs);
            }
            lock (runLock) {
                return Run(inputs);
            }
        }

        float[] Run(List<NamedOnnxValue> inputs) {
            using var dmlScope = Onnx.EnterDmlScope();
            using var results = session.Run(inputs);
            return results.First().AsTensor<float>().ToArray();
        }
    }

    /// <summary>
    /// After the vocoder: A (pitch-slope amplitude modulation), undo the analysis scale, growl
    /// (the growl curve, hifisampler's HG flag), P (Worldline's resampler auto gain, in place of
    /// hifisampler's loudness normalization), the peak limit, and volume.
    /// </summary>
    internal static class HifiPostProcess {
        /// <param name="lengthReq">The requested length (s): the output's part the wavtool keeps.</param>
        /// <param name="wavMax">The whole source file's peak.</param>
        /// <param name="voicedRatio">The voiced ratio of the kept frames, only called when P is not 0.</param>
        public static float[] Apply(float[] render, double scale, HifiFlags flags, HifiSamplerConfig config,
                double[] pitchRender, double[] t, double newStart, double newEnd, double lengthReq, int volume,
                float[]? growl, double tempo, double wavMax, Func<double> voicedRatio) {
            int aFlag = flags.Get("A") ?? 0;
            if (aFlag != 0 && pitchRender.Length > 1 && t.Length > 1) {
                double a = Math.Clamp(aFlag, -100, 100);
                var derivative = HifiInterp.Gradient(pitchRender, t);
                var gain = derivative.Select(d => Math.Pow(5, 1e-4 * a * d)).ToArray();
                int n = render.Length;
                var audioTime = new double[n];
                for (int i = 0; i < n; i++) {
                    audioTime[i] = newStart + i * ((newEnd - newStart) / n);
                }
                var interpolated = HifiInterp.Linear(audioTime, t, gain);
                for (int i = 0; i < n; i++) {
                    render[i] = (float)(render[i] * interpolated[i]);
                }
            }

            for (int i = 0; i < render.Length; i++) {
                render[i] = (float)(render[i] / scale);
            }

            if (growl != null && growl.Length > 0) {
                // The curve starts at the output start, a point every 5 ticks.
                double pointsPerSample = 1.0 / (config.SampleRate * 60.0 / (tempo * 96));
                render = HifiGrowl.Apply(render, config.SampleRate, 80.0, i => {
                    double pos = Math.Clamp(i * pointsPerSample, 0, growl.Length - 1);
                    int j = (int)pos;
                    double v = j + 1 < growl.Length ? growl[j] + (growl[j + 1] - growl[j]) * (pos - j) : growl[j];
                    return v / 100.0;
                });
            }

            // P as Worldline's resampler takes it (86 when absent), from the kept part's peak.
            int kept = Math.Min(render.Length, (int)(lengthReq * config.SampleRate));
            int peakComp = flags.Get("P") ?? 86;
            if (peakComp != 0) {
                double outMax = HifiArray.MaxAbs(render.AsSpan(0, kept));
                double gain = Worldline.ResamplerAutoGain(outMax, wavMax, voicedRatio(), peakComp);
                for (int i = 0; i < render.Length; i++) {
                    render[i] = (float)(render[i] * gain);
                }
            }

            double newMax = HifiArray.MaxAbs(render.AsSpan(0, kept));
            if (newMax > config.PeakLimit) {
                for (int i = 0; i < render.Length; i++) {
                    render[i] = (float)(render[i] / newMax);
                }
            }

            double volumeScale = volume / 100.0;
            for (int i = 0; i < render.Length; i++) {
                render[i] = (float)(render[i] * volumeScale);
            }
            return render;
        }
    }
}
