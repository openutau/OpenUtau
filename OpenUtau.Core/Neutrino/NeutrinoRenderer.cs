using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    public class NeutrinoRenderer : IRenderer {
        public const int headTicks = 480;
        public const int tailTicks = 480;

        const int sampleRate = 48000;
        const int outputSampleRate = 44100;
        const int hopSize = 480;
        const int pitchInterval = 5;
        const int numMelBins = 100;
        const int cacheVersion = 1;
        const int edgeSilenceSamples = 240;
        const int fadeInSamples = 240;
        const int fadeOutSamples = 240;
        const float f0Min = 40f;
        const float f0Max = 2000f;
        const float melspecMin = -7f;
        const float melspecMax = 1f;
        const float wavScale = 0.9885531068f;
        const float wavClamp = 0.9988493919f;

        static readonly HashSet<string> supportedExp = new HashSet<string>() {
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.SHFC,
        };

        static readonly object lockObj = new object();

        sealed class NeutrinoTimingContext {
            public long[] PhonemeIds { get; }
            public float[] ScorePitchesHz { get; }
            public float[] ScoreDurations { get; }
            public long[] PhonePositions { get; }
            public float[] TimingDurations { get; }
            public long[] FramePhonemeMap { get; }
            public int TotalFrames { get; }
            public double StartOffsetSeconds { get; }
            public NeutrinoFrameChunk[] Chunks { get; }

            public NeutrinoTimingContext(
                long[] phonemeIds,
                float[] scorePitchesHz,
                float[] scoreDurations,
                long[] phonePositions,
                float[] timingDurations,
                long[] framePhonemeMap,
                int totalFrames,
                double startOffsetSeconds,
                NeutrinoFrameChunk[] chunks) {

                PhonemeIds = phonemeIds;
                ScorePitchesHz = scorePitchesHz;
                ScoreDurations = scoreDurations;
                PhonePositions = phonePositions;
                TimingDurations = timingDurations;
                FramePhonemeMap = framePhonemeMap;
                TotalFrames = totalFrames;
                StartOffsetSeconds = startOffsetSeconds;
                Chunks = chunks;
            }
        }


        public USingerType SingerType => USingerType.Neutrino;
        public bool SupportsRenderPitch => true;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            var headMs = phrase.positionMs - phrase.timeAxis.TickPosToMsPos(phrase.position - headTicks);
            var tailMs = phrase.timeAxis.TickPosToMsPos(phrase.end + tailTicks) - phrase.endMs;
            return new RenderResult() {
                leadingMs = headMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = headMs + phrase.durationMs + tailMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress,
            int trackNo, CancellationTokenSource cancellation, bool isPreRender) {

            return Task.Run(() => {
                lock (lockObj) {
                    if (cancellation.IsCancellationRequested) {
                        return new RenderResult();
                    }

                    string progressInfo = $"Track {trackNo + 1}: {this} " +
                        $"\"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                    progress.Complete(0, progressInfo);

                    var result = Layout(phrase);
                    var wavPath = Path.Join(PathManager.Inst.CachePath,
                        $"neutrino-v3-native-v{cacheVersion}-{phrase.hash:x16}.wav");
                    phrase.AddCacheFile(wavPath);

                    if (TryLoadWaveCache(wavPath, out var cachedSamples)) {
                        result.samples = cachedSamples;
                    }

                    if (result.samples == null) {
                        result.samples = InvokeNeutrino(phrase, cancellation);
                        if (result.samples != null) {
                            Wave.CorrectSampleScale(result.samples);
                            SaveWaveCache(wavPath, result.samples);
                        }
                    }

                    if (result.samples != null) {
                        Renderers.ApplyDynamics(phrase, result);
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    return result;
                }
            });
        }

        float[] InvokeNeutrino(RenderPhrase phrase, CancellationTokenSource cancellation) {
            if (cancellation.IsCancellationRequested) return null;
            var timing = BuildTimingContext(phrase);
            if (timing.PhonemeIds.Length == 0) {
                return Array.Empty<float>();
            }

            if (cancellation.IsCancellationRequested) return null;

            float[] f0 = BuildEditorF0(phrase, timing);
            if (HasNonDefaultValue(phrase.toneShift, 0)) {
                // Keep the editor pitch authoritative and apply only the contour
                // difference produced by p.bin's StyleShift behavior.
                var neutralF0 = RunPredictedF0(phrase, timing, applyStyleShift: false);
                var shiftedF0 = RunPredictedF0(phrase, timing, applyStyleShift: true);
                ApplyStyleShiftContour(f0, neutralF0, shiftedF0);
            }
            ClampF0(f0);

            if (cancellation.IsCancellationRequested) return null;

            var singer = phrase.singer as NeutrinoSinger;
            var waveform = new float[timing.TotalFrames * hopSize];
            foreach (var chunk in timing.Chunks) {
                if (!chunk.IsActive || chunk.FrameCount <= 0) {
                    continue;
                }
                if (cancellation.IsCancellationRequested) return null;

                var chunkTiming = BuildChunkTimingContext(timing, chunk);
                var chunkF0 = NeutrinoInferenceUtil.Slice(f0, chunk.FrameStart, chunk.FrameCount);
                var chunkWaveform = RunAcousticChunk(singer, chunkTiming, chunkF0, cancellation);
                if (chunkWaveform == null) return null;
                Array.Copy(
                    chunkWaveform,
                    0,
                    waveform,
                    chunk.FrameStart * hopSize,
                    chunkWaveform.Length);
            }

            var layout = Layout(phrase);
            double waveformOffsetMs = layout.leadingMs + timing.StartOffsetSeconds * 1000.0;
            int headSamples = Math.Max(0, (int)(waveformOffsetMs / 1000.0 * sampleRate));
            int tailSamples = Math.Max(0,
                (int)(layout.estimatedLengthMs / 1000.0 * sampleRate) - headSamples - waveform.Length);

            int totalSamples = headSamples + waveform.Length + tailSamples;
            var result = new float[totalSamples];
            Array.Copy(waveform, 0, result, headSamples, waveform.Length);

            if (sampleRate != outputSampleRate) {
                var signal = new NWaves.Signals.DiscreteSignal(sampleRate, result);
                signal = NWaves.Operations.Operation.Resample(signal, outputSampleRate);
                result = signal.Samples;
            }

            return result;
        }

        NeutrinoTimingContext BuildChunkTimingContext(
            NeutrinoTimingContext timing,
            NeutrinoFrameChunk chunk) {

            var timingDurations = NeutrinoInferenceUtil.Slice(
                timing.TimingDurations, chunk.PhoneStart, chunk.PhoneCount);
            return new NeutrinoTimingContext(
                NeutrinoInferenceUtil.Slice(timing.PhonemeIds, chunk.PhoneStart, chunk.PhoneCount),
                NeutrinoInferenceUtil.Slice(timing.ScorePitchesHz, chunk.PhoneStart, chunk.PhoneCount),
                NeutrinoInferenceUtil.Slice(timing.ScoreDurations, chunk.PhoneStart, chunk.PhoneCount),
                NeutrinoInferenceUtil.Slice(timing.PhonePositions, chunk.PhoneStart, chunk.PhoneCount),
                timingDurations,
                BuildFramePhonemeMap(timingDurations, chunk.FrameCount),
                chunk.FrameCount,
                0,
                Array.Empty<NeutrinoFrameChunk>());
        }

        float[] RunAcousticChunk(
            NeutrinoSinger singer,
            NeutrinoTimingContext timing,
            float[] f0,
            CancellationTokenSource cancellation) {

            int numPhones = timing.PhonemeIds.Length;
            int totalFrames = timing.TotalFrames;
            var melspecInputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("electron",
                    new DenseTensor<long>(timing.PhonemeIds, new[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor("muon",
                    new DenseTensor<float>(timing.TimingDurations, new[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor("tau",
                    new DenseTensor<float>(timing.ScorePitchesHz, new[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor("selectron",
                    new DenseTensor<float>(timing.ScoreDurations, new[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor("smuon",
                    new DenseTensor<long>(timing.PhonePositions, new[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor("stau",
                    new DenseTensor<long>(timing.FramePhonemeMap, new[] { 1, totalFrames })),
                NamedOnnxValue.CreateFromTensor("photon",
                    new DenseTensor<float>(f0, new[] { 1, totalFrames })),
            };

            float[] melSpectrogram = NeutrinoInferenceUtil.RequireLength(
                singer.RunMelspec(melspecInputs),
                totalFrames * numMelBins,
                "NEUTRINO v3 s.bin mel output");
            ClampMelspec(melSpectrogram);

            if (cancellation.IsCancellationRequested) return null;

            var vocoderInput = new float[totalFrames * (numMelBins + 1)];
            for (int frame = 0; frame < totalFrames; frame++) {
                for (int bin = 0; bin < numMelBins; bin++) {
                    vocoderInput[frame * (numMelBins + 1) + bin] =
                        melSpectrogram[frame * numMelBins + bin];
                }
                vocoderInput[frame * (numMelBins + 1) + numMelBins] = f0[frame];
            }

            var vocoderInputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("input",
                    new DenseTensor<float>(vocoderInput, new[] { 1, totalFrames, numMelBins + 1 })),
            };
            var waveform = NeutrinoInferenceUtil.RequireLength(
                singer.RunVocoder(vocoderInputs),
                totalFrames * hopSize,
                "NEUTRINO v3 v.bin waveform output");
            if (cancellation.IsCancellationRequested) return null;
            PostProcessWaveform(waveform);
            return waveform;
        }

        static bool HasNonDefaultValue(float[] values, float defaultValue) {
            if (values == null) {
                return false;
            }
            foreach (float value in values) {
                if (Math.Abs(value - defaultValue) > 0.5f) {
                    return true;
                }
            }
            return false;
        }

        NeutrinoTimingContext BuildTimingContext(RenderPhrase phrase) {
            var singer = phrase.singer as NeutrinoSinger;
            var (phonemeIds, scorePitchesHz, scoreDurations, phonePositions, manualBoundaries) =
                BuildPhonemeSequence(phrase);

            int numPhones = phonemeIds.Length;
            if (numPhones == 0) {
                return new NeutrinoTimingContext(
                    phonemeIds,
                    scorePitchesHz,
                    scoreDurations,
                    phonePositions,
                    Array.Empty<float>(),
                    Array.Empty<long>(),
                    0,
                    0,
                    Array.Empty<NeutrinoFrameChunk>());
            }

            var phoneChunks = NeutrinoInferenceUtil.BuildPhoneChunks(phonemeIds);
            double frameSeconds = (double)hopSize / sampleRate;
            double scoreOriginMs = GetScoreOriginMs(phrase);
            double leadingContextSeconds = GetLeadingContextSeconds(phrase);
            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                scoreDurations,
                phonePositions,
                phoneChunks,
                frameSeconds,
                chunk => {
                    var chunkPitches = NeutrinoInferenceUtil.Slice(
                        scorePitchesHz, chunk.PhoneStart, chunk.PhoneCount);
                    var chunkScoreDurations = NeutrinoInferenceUtil.Slice(
                        scoreDurations, chunk.PhoneStart, chunk.PhoneCount);
                    var chunkPhonePositions = NeutrinoInferenceUtil.Slice(
                        phonePositions, chunk.PhoneStart, chunk.PhoneCount);
                    var chunkPhonemeIds = NeutrinoInferenceUtil.Slice(
                        phonemeIds, chunk.PhoneStart, chunk.PhoneCount);
                    var timingInputs = new List<NamedOnnxValue> {
                        NamedOnnxValue.CreateFromTensor("electron",
                            new DenseTensor<long>(chunkPhonemeIds, new[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor("muon",
                            new DenseTensor<float>(chunkPitches, new[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor("tau",
                            new DenseTensor<float>(chunkScoreDurations, new[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor("selectron",
                            new DenseTensor<long>(chunkPhonePositions, new[] { 1, chunk.PhoneCount })),
                    };
                    return NeutrinoInferenceUtil.RequireTimingBoundaryLength(
                        singer.RunTiming(timingInputs),
                        chunk.PhoneCount,
                        "NEUTRINO v3 t.bin timing output");
                },
                leadingContextSeconds);

            ApplyManualBoundaryOverrides(
                boundaries,
                manualBoundaries,
                leadingContextSeconds);
            double boundaryStartSeconds = NeutrinoInferenceUtil.NormalizeBoundaryStart(boundaries);
            double startOffsetSeconds =
                (scoreOriginMs - phrase.positionMs) / 1000.0 + boundaryStartSeconds;
            var timingDurations = BuildTimingDurations(boundaries);
            int totalFrames = Math.Max(1, (int)Math.Round(boundaries[^1] * sampleRate / hopSize));
            var framePhonemeMap = BuildFramePhonemeMap(timingDurations, totalFrames);
            var frameChunks = NeutrinoInferenceUtil.BuildFrameChunks(
                phoneChunks,
                boundaries,
                totalFrames,
                (double)hopSize / sampleRate);

            return new NeutrinoTimingContext(
                phonemeIds,
                scorePitchesHz,
                scoreDurations,
                phonePositions,
                timingDurations,
                framePhonemeMap,
                totalFrames,
                startOffsetSeconds,
                frameChunks);
        }

        float[] RunPredictedF0(
            RenderPhrase phrase,
            NeutrinoTimingContext timing,
            bool applyStyleShift = true) {

            if (timing.TotalFrames <= 0 || timing.PhonemeIds.Length == 0) {
                return Array.Empty<float>();
            }

            var styleShiftCentsByFrame = applyStyleShift
                ? BuildStyleShiftCentsByFrame(phrase, timing)
                : Array.Empty<float>();
            var singer = phrase.singer as NeutrinoSinger;
            var f0 = new float[timing.TotalFrames];
            foreach (var chunk in timing.Chunks) {
                if (!chunk.IsActive || chunk.FrameCount <= 0) {
                    continue;
                }

                var chunkTiming = BuildChunkTimingContext(timing, chunk);
                var chunkStyleShift = styleShiftCentsByFrame.Length == 0
                    ? Array.Empty<float>()
                    : NeutrinoInferenceUtil.Slice(
                        styleShiftCentsByFrame, chunk.FrameStart, chunk.FrameCount);
                var scorePitchesHz = ApplyStyleShiftToScorePitches(
                    chunkTiming.ScorePitchesHz,
                    BuildPhoneStyleShiftCents(chunkTiming, chunkStyleShift));
                int numPhones = chunkTiming.PhonemeIds.Length;
                var pitchInputs = new List<NamedOnnxValue> {
                    NamedOnnxValue.CreateFromTensor("electron",
                        new DenseTensor<long>(chunkTiming.PhonemeIds, new[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor("muon",
                        new DenseTensor<float>(chunkTiming.TimingDurations, new[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor("tau",
                        new DenseTensor<float>(scorePitchesHz, new[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor("selectron",
                        new DenseTensor<float>(chunkTiming.ScoreDurations, new[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor("smuon",
                        new DenseTensor<long>(chunkTiming.PhonePositions, new[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor("stau",
                        new DenseTensor<long>(
                            chunkTiming.FramePhonemeMap,
                            new[] { 1, chunkTiming.TotalFrames })),
                };
                var chunkF0 = NeutrinoInferenceUtil.RequireLength(
                    singer.RunPitch(pitchInputs),
                    chunkTiming.TotalFrames,
                    "NEUTRINO v3 p.bin F0 output");
                ApplyInverseStyleShiftToF0(chunkF0, chunkStyleShift);
                ClampF0(chunkF0);
                Array.Copy(chunkF0, 0, f0, chunk.FrameStart, chunkF0.Length);
            }
            return f0;
        }

        internal static void ApplyStyleShiftContour(
            float[] editorF0,
            float[] neutralF0,
            float[] shiftedF0) {

            int frameCount = Math.Min(editorF0.Length, Math.Min(neutralF0.Length, shiftedF0.Length));
            for (int frame = 0; frame < frameCount; frame++) {
                if (editorF0[frame] <= 0 || neutralF0[frame] <= 0 || shiftedF0[frame] <= 0) {
                    continue;
                }
                float ratio = shiftedF0[frame] / neutralF0[frame];
                if (float.IsFinite(ratio)) {
                    editorF0[frame] *= Math.Clamp(ratio, 0.5f, 2f);
                }
            }
        }

        float[] BuildStyleShiftCentsByFrame(RenderPhrase phrase, NeutrinoTimingContext timing) {
            if (!HasNonDefaultValue(phrase.toneShift, 0)) {
                return Array.Empty<float>();
            }
            var result = new float[timing.TotalFrames];
            for (int frame = 0; frame < result.Length; frame++) {
                result[frame] = GetFrameToneShiftCents(phrase, timing, frame);
            }
            return result;
        }

        float[] BuildPhoneStyleShiftCents(NeutrinoTimingContext timing, float[] styleShiftCentsByFrame) {
            if (styleShiftCentsByFrame.Length == 0) {
                return Array.Empty<float>();
            }
            int numPhones = timing.PhonemeIds.Length;
            var sums = new double[numPhones];
            var counts = new int[numPhones];
            int frameCount = Math.Min(styleShiftCentsByFrame.Length, timing.FramePhonemeMap.Length);
            for (int frame = 0; frame < frameCount; frame++) {
                int phone = Math.Clamp((int)timing.FramePhonemeMap[frame] - 1, 0, numPhones - 1);
                sums[phone] += styleShiftCentsByFrame[frame];
                counts[phone]++;
            }
            var result = new float[numPhones];
            for (int phone = 0; phone < result.Length; phone++) {
                if (counts[phone] > 0) {
                    result[phone] = (float)(sums[phone] / counts[phone]);
                }
            }
            return result;
        }

        float[] ApplyStyleShiftToScorePitches(float[] scorePitchesHz, float[] phoneStyleShiftCents) {
            if (phoneStyleShiftCents.Length == 0) {
                return scorePitchesHz;
            }
            var result = (float[])scorePitchesHz.Clone();
            for (int i = 0; i < result.Length && i < phoneStyleShiftCents.Length; i++) {
                if (result[i] > 0 && Math.Abs(phoneStyleShiftCents[i]) > 0.5f) {
                    result[i] *= StyleShiftFactor(phoneStyleShiftCents[i]);
                }
            }
            return result;
        }

        void ApplyInverseStyleShiftToF0(float[] f0, float[] styleShiftCentsByFrame) {
            if (styleShiftCentsByFrame.Length == 0) {
                return;
            }
            for (int frame = 0; frame < f0.Length && frame < styleShiftCentsByFrame.Length; frame++) {
                if (f0[frame] > 0 && Math.Abs(styleShiftCentsByFrame[frame]) > 0.5f) {
                    f0[frame] /= StyleShiftFactor(styleShiftCentsByFrame[frame]);
                }
            }
        }

        static float StyleShiftFactor(float cents) {
            return (float)Math.Pow(2.0, cents / 1200.0);
        }

        float[] BuildEditorF0(RenderPhrase phrase, NeutrinoTimingContext timing) {
            var f0 = new float[timing.TotalFrames];
            for (int frame = 0; frame < f0.Length; frame++) {
                int phoneIndex = GetFramePhoneIndex(timing, frame);
                if (phoneIndex < 0
                    || timing.PhonemeIds[phoneIndex] == NeutrinoPhoneme.PAU
                    || timing.ScorePitchesHz[phoneIndex] <= 0) {
                    continue;
                }

                if (phrase.pitches == null || phrase.pitches.Length == 0) {
                    f0[frame] = timing.ScorePitchesHz[phoneIndex];
                    continue;
                }

                int pitchIndex = GetFramePitchIndex(phrase, timing, frame);
                f0[frame] = (float)MusicMath.ToneToFreq(phrase.pitches[pitchIndex] * 0.01);
            }
            return f0;
        }

        int GetFramePhoneIndex(NeutrinoTimingContext timing, int frame) {
            if (timing.FramePhonemeMap.Length == 0 || timing.PhonemeIds.Length == 0) {
                return -1;
            }
            int mapIndex = Math.Clamp(frame, 0, timing.FramePhonemeMap.Length - 1);
            return Math.Clamp((int)timing.FramePhonemeMap[mapIndex] - 1, 0, timing.PhonemeIds.Length - 1);
        }

        int GetFramePitchIndex(RenderPhrase phrase, NeutrinoTimingContext timing, int frame) {
            int ticks = GetFramePitchTick(phrase, timing, frame);
            return Math.Clamp((int)(ticks / (double)pitchInterval), 0, phrase.pitches.Length - 1);
        }

        float GetFrameToneShiftCents(
            RenderPhrase phrase,
            NeutrinoTimingContext timing,
            int frame) {

            if (phrase.toneShift == null || phrase.toneShift.Length == 0) {
                return 0;
            }
            int ticks = GetFramePitchTick(phrase, timing, frame);
            int index = Math.Clamp((int)(ticks / (double)pitchInterval), 0, phrase.toneShift.Length - 1);
            return phrase.toneShift[index];
        }

        int GetFramePitchTick(RenderPhrase phrase, NeutrinoTimingContext timing, int frame) {
            double frameMs = 1000.0 * hopSize / sampleRate;
            double posMs = phrase.positionMs - phrase.leadingMs
                + timing.StartOffsetSeconds * 1000.0
                + frame * frameMs;
            return phrase.timeAxis.MsPosToTickPos(posMs) - (phrase.position - phrase.leading);
        }

        int GetFrameResultTick(RenderPhrase phrase, NeutrinoTimingContext timing, int frame) {
            double frameMs = 1000.0 * hopSize / sampleRate;
            double posMs = phrase.positionMs - phrase.leadingMs
                + timing.StartOffsetSeconds * 1000.0
                + frame * frameMs;
            return phrase.timeAxis.MsPosToTickPos(posMs) - phrase.position;
        }

        static bool TryLoadWaveCache(string path, out float[] samples) {
            samples = null;
            if (!File.Exists(path)) {
                return false;
            }
            try {
                using var waveStream = Wave.OpenFile(path);
                samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                return true;
            } catch (Exception e) {
                Log.Error(e, "Failed to read NEUTRINO cache, re-rendering");
                return false;
            }
        }

        static void SaveWaveCache(string path, float[] samples) {
            var source = new WaveSource(0, 0, 0, 1);
            source.SetSamples(samples);
            WaveFileWriter.CreateWaveFile16(path, new ExportAdapter(source).ToMono(1, 0));
        }

        (long[] phonemeIds, float[] scorePitchesHz, float[] scoreDurations, long[] phonePositions, double?[] manualBoundaries)
            BuildPhonemeSequence(RenderPhrase phrase) {

            double scoreOriginMs = GetScoreOriginMs(phrase);
            var phonesByNote = new Dictionary<int, List<NeutrinoScorePhoneInput>>();

            foreach (var phone in phrase.phones) {
                var phoneStrs = NeutrinoPhoneme.RenderPhoneToPhonemes(phone.phoneme);
                int noteIndex = Math.Clamp(phone.noteIndex, 0, phrase.notes.Length - 1);
                if (!phonesByNote.TryGetValue(noteIndex, out var modelPhones)) {
                    modelPhones = new List<NeutrinoScorePhoneInput>();
                    phonesByNote[noteIndex] = modelPhones;
                }

                for (int i = 0; i < phoneStrs.Length; i++) {
                    int id = NeutrinoPhoneme.GetPhonemeId(phoneStrs[i]);
                    modelPhones.Add(new NeutrinoScorePhoneInput(
                        id,
                        manualBoundarySeconds: phone.positionOverridden && i == 0
                            ? (phone.positionMs - scoreOriginMs) / 1000.0
                            : null));
                }
            }

            var scoreNotes = new List<NeutrinoScoreNoteInput>();
            int firstPhoneNoteIndex = GetFirstPhoneNoteIndex(phrase);
            int lastPhoneNoteIndex = phrase.phones.Length == 0
                ? firstPhoneNoteIndex
                : Math.Clamp(
                    phrase.phones.Max(phone => phone.noteIndex),
                    firstPhoneNoteIndex,
                    phrase.notes.Length - 1);
            for (int noteIndex = firstPhoneNoteIndex; noteIndex < phrase.notes.Length; noteIndex++) {
                var note = phrase.notes[noteIndex];
                bool isExtension = NeutrinoInferenceUtil.IsExtensionLyric(note.lyric);
                bool hasPhones = phonesByNote.TryGetValue(noteIndex, out var modelPhones);
                if (noteIndex > lastPhoneNoteIndex && !isExtension) {
                    break;
                }

                float notePitchHz = (float)MusicMath.ToneToFreq(
                    note.tone + note.tuning * 0.01f);
                float noteDurationSec = Math.Max(0.001f, (float)(note.durationMs / 1000.0));
                scoreNotes.Add(new NeutrinoScoreNoteInput(
                    notePitchHz,
                    noteDurationSec,
                    isExtension,
                    hasPhones ? modelPhones.ToArray() : Array.Empty<NeutrinoScorePhoneInput>()));
            }

            var sequence = NeutrinoInferenceUtil.BuildScoreSequence(scoreNotes);
            return (
                sequence.PhonemeIds,
                sequence.ScorePitchesHz,
                sequence.ScoreDurations,
                sequence.PhonePositions,
                sequence.ManualBoundaries
            );
        }

        int GetFirstPhoneNoteIndex(RenderPhrase phrase) {
            if (phrase.phones.Length == 0 || phrase.notes.Length == 0) {
                return 0;
            }
            return Math.Clamp(phrase.phones[0].noteIndex, 0, phrase.notes.Length - 1);
        }

        double GetScoreOriginMs(RenderPhrase phrase) {
            if (phrase.notes.Length == 0) {
                return phrase.positionMs;
            }
            return phrase.notes[GetFirstPhoneNoteIndex(phrase)].positionMs;
        }

        double GetLeadingContextSeconds(RenderPhrase phrase) {
            if (phrase.notes.Length == 0) {
                return 0;
            }
            var firstNote = phrase.notes[GetFirstPhoneNoteIndex(phrase)];
            int scoreOriginTick = phrase.position + firstNote.position;
            double contextStartMs = phrase.timeAxis.TickPosToMsPos(scoreOriginTick - headTicks);
            double maximumContextMs = Math.Max(0, firstNote.positionMs - contextStartMs);
            return Math.Min(maximumContextMs, phrase.availableLeadingMs) / 1000.0;
        }

        internal static void ApplyManualBoundaryOverrides(
            double[] boundaries,
            double?[] manualBoundaries,
            double leadingContextSeconds) {

            if (manualBoundaries == null || manualBoundaries.Length == 0) {
                return;
            }

            double frameSec = (double)hopSize / sampleRate;
            int count = Math.Min(boundaries.Length - 1, manualBoundaries.Length - 1);
            for (int i = 0; i < count; i++) {
                if (!manualBoundaries[i].HasValue) {
                    continue;
                }

                double min = i == 0
                    ? Math.Min(0, -Math.Max(0, leadingContextSeconds) + frameSec)
                    : boundaries[i - 1] + frameSec;
                double max = boundaries[i + 1] - frameSec;
                if (max < min) {
                    max = min;
                }
                boundaries[i] = Math.Round(Math.Clamp(manualBoundaries[i].Value, min, max) * 1000.0) / 1000.0;
            }

            for (int i = 1; i < boundaries.Length; i++) {
                if (boundaries[i] <= boundaries[i - 1]) {
                    boundaries[i] = Math.Round((boundaries[i - 1] + frameSec) * 1000.0) / 1000.0;
                }
            }
        }

        float[] BuildTimingDurations(double[] boundaries) {
            var durations = new float[boundaries.Length - 1];
            for (int i = 0; i < durations.Length; i++) {
                durations[i] = Math.Max(0.001f, (float)(boundaries[i + 1] - boundaries[i]));
            }
            return durations;
        }

        internal static long[] BuildFramePhonemeMap(float[] timingDurations, int totalFrames) {
            var stau = new long[totalFrames];
            double frameSec = (double)hopSize / sampleRate;
            double time = 0;
            for (int phone = 0; phone < timingDurations.Length; phone++) {
                int startFrame = (int)Math.Round(time / frameSec);
                time += timingDurations[phone];
                int endFrame = Math.Min(totalFrames, (int)Math.Round(time / frameSec));
                for (int frame = startFrame; frame < endFrame; frame++) {
                    stau[frame] = phone + 1;
                }
            }
            long finalPhone = timingDurations.Length;
            for (int frame = 0; frame < totalFrames; frame++) {
                if (stau[frame] == 0) {
                    stau[frame] = finalPhone;
                }
            }
            return stau;
        }

        void ClampF0(float[] f0) {
            for (int i = 0; i < f0.Length; i++) {
                if (!float.IsFinite(f0[i]) || f0[i] < f0Min) {
                    f0[i] = 0;
                } else if (f0[i] > f0Max) {
                    f0[i] = f0Max;
                }
            }
        }

        void ClampMelspec(float[] melSpectrogram) {
            for (int i = 0; i < melSpectrogram.Length; i++) {
                if (!float.IsFinite(melSpectrogram[i])) {
                    melSpectrogram[i] = melspecMin;
                } else if (melSpectrogram[i] < melspecMin) {
                    melSpectrogram[i] = melspecMin;
                } else if (melSpectrogram[i] > melspecMax) {
                    melSpectrogram[i] = melspecMax;
                }
            }
        }

        void PostProcessWaveform(float[] waveform) {
            int edge = Math.Min(edgeSilenceSamples, waveform.Length / 2);
            for (int i = 0; i < edge; i++) {
                waveform[i] = 0;
                waveform[waveform.Length - 1 - i] = 0;
            }

            int fadeIn = Math.Min(fadeInSamples, Math.Max(0, waveform.Length - edge));
            for (int i = 0; i < fadeIn; i++) {
                int index = edge + i;
                if (index >= waveform.Length) break;
                float gain = (float)Math.Pow((double)i / fadeInSamples, 2.0);
                waveform[index] *= gain;
            }

            int fadeOut = Math.Min(fadeOutSamples, Math.Max(0, waveform.Length - edge));
            for (int i = 0; i < fadeOut; i++) {
                int index = waveform.Length - edge - 1 - i;
                if (index < 0) break;
                float gain = (float)Math.Pow((double)i / fadeOutSamples, 2.0);
                waveform[index] *= gain;
            }

            for (int i = 0; i < waveform.Length; i++) {
                float value = waveform[i] * wavScale;
                if (!float.IsFinite(value)) value = 0;
                if (value > wavClamp) value = wavClamp;
                if (value < -wavClamp) value = -wavClamp;
                waveform[i] = value;
            }
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            var timing = BuildTimingContext(phrase);
            if (timing.TotalFrames <= 0) {
                return null;
            }

            var f0 = RunPredictedF0(phrase, timing);
            var result = new RenderPitchResult {
                ticks = new float[f0.Length],
                tones = new float[f0.Length],
            };

            for (int frame = 0; frame < f0.Length; frame++) {
                result.ticks[frame] = GetFrameResultTick(phrase, timing, frame);
                int phoneIndex = GetFramePhoneIndex(timing, frame);
                bool voiced = phoneIndex >= 0
                    && timing.PhonemeIds[phoneIndex] != NeutrinoPhoneme.PAU
                    && timing.ScorePitchesHz[phoneIndex] > 0
                    && f0[frame] > 0;
                result.tones[frame] = voiced
                    ? (float)MusicMath.FreqToTone(f0[frame])
                    : -1f;
            }
            return result;
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(
            USinger singer, URenderSettings renderSettings) {
            return Array.Empty<UExpressionDescriptor>();
        }

        public override string ToString() => Renderers.NEUTRINO;
    }
}
