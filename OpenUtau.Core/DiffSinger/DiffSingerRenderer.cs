using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using NumSharp;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    public class DiffSingerRenderer : IRenderer {
        const string VELC = DiffSingerUtils.VELC;
        const string ENE = DiffSingerUtils.ENE;
        const string PEXP = DiffSingerUtils.PEXP;
        const string VoiceColorHeader = DiffSingerUtils.VoiceColorHeader;

        // Curves the renderer itself reads. The voicebank's tunable parameters come
        // from its DsParamRegistry (DsParamRegistry.IsKnownAbbr), which also covers the
        // built-in ones once it is built; they stay listed here so a project opened
        // without a voicebank keeps its curves.
        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.CLR,
            Format.Ustx.SHFC,
            Format.Ustx.GENC,
            Format.Ustx.BREC,
            Format.Ustx.VOIC,
            Format.Ustx.TENC,
            VELC,
            ENE,
            PEXP,
        };

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.DiffSinger;

        public bool SupportsRenderPitch => true;

        public bool SupportsRealCurve => true;

        public bool SupportsPhonemeEnvelope => false;

        public bool IsVoiceColorCurve(string abbr, out int subBankId) {
            subBankId = 0;
            if (abbr.StartsWith(VoiceColorHeader) && int.TryParse(abbr.Substring(2), out subBankId)) {;
                subBankId -= 1;
                return true;
            } else {
                return false;
            }
        }

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr) ||
                DsParamRegistry.IsKnownAbbr(descriptor.abbr) ||
                (descriptor.abbr.StartsWith(VoiceColorHeader) && int.TryParse(descriptor.abbr.Substring(2), out int _));
        }

        //Calculate the Timing layout of the RenderPhrase, 
        //including the position of the phrase, 
        //the length of the head consonant, and the estimated total length
        public RenderResult Layout(RenderPhrase phrase) {
            var singer = phrase.singer as DiffSingerSinger;
            if (singer == null) {
                throw new InvalidDataException("Singer is not DiffSingerSinger.");
            }
            float headMs = DiffSingerUtils.GetHeadMs(phrase);
            float tailMs = DiffSingerUtils.GetTailMs(phrase);
            return new RenderResult() {
                leadingMs = headMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = headMs + phrase.durationMs + tailMs,
            };
        }

        public (double HeadMs, double TailMs) PhrasePadding(USinger singer, IEnumerable<UPhoneme> phonemes) {
            var dsSinger = singer as DiffSingerSinger;
            if (dsSinger == null) {
                return (0, 0);
            }
            double frameMs = dsSinger.dsConfig.frameMs();
            return (DiffSingerUtils.GetHeadMs(frameMs), DiffSingerUtils.GetTailMs(frameMs));
        }

        /// <summary>
        /// Merging is opt-in (piano roll toggle): it changes how the model sees
        /// a passage, so it stays off unless the user enables it.
        /// </summary>
        public bool ShouldMergePhrases(UProject project, UTrack track, UPhoneme prev, UPhoneme next) {
            return Preferences.Default.DiffSingerMergeNearbyPhrases
                && IRenderer.GapOverlapsPadding(this, track, prev, next);
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender, RenderPhraseEvents? renderEvents = null) {
            var task = Task.Run(() => {
                lock (lockObj) {
                    // The static lock serializes DiffSinger renders; the DirectML scope additionally
                    // keeps them from overlapping pitch/variance work or session disposal.
                    using var dmlScope = Onnx.EnterDmlScope();
                    if (cancellation.IsCancellationRequested) {
                        return new RenderResult();
                    }
                    var result = Layout(phrase);

                    // calculate real depth
                    var singer = (DiffSingerSinger) phrase.singer;
                    double depth;
                    int steps = Preferences.Default.DiffSingerSteps;
                    if (singer.dsConfig.useVariableDepth) {
                        double maxDepth = singer.dsConfig.maxDepth;
                        if (maxDepth < 0) {
                            throw new InvalidDataException("Max depth is unset or is negative.");
                        }
                        depth = Math.Min(Preferences.Default.DiffSingerDepth, maxDepth);
                    } else {
                        depth = 1.0;
                    }
                    var wavName = $"ds-{phrase.hash:x16}-depth{depth:f2}-steps{steps}.wav";
                    var wavPath = Path.Join(PathManager.Inst.CachePath, wavName);
                    phrase.AddCacheFile(wavPath);
                    string progressInfo = $"Track {trackNo + 1}: {this} depth={depth:f2} steps={steps} \"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                    if (File.Exists(wavPath)) {
                        try {
                            using (var waveStream = Wave.OpenFile(wavPath)) {
                                result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                            }
                        } catch (Exception e) {
                            Log.Error(e, "Failed to read cached render, re-rendering.");
                        }
                    }
                    if (result.samples == null) {
                        result.samples = InvokeDiffsinger(phrase, depth, steps, cancellation, renderEvents);
                        if (result.samples != null) {
                            Wave.WriteMono16Wav(wavPath, result.samples);
                        }
                    }
                    if (result.samples != null) {
                        Renderers.ApplyDynamics(phrase, result);
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    return result;
                }
            });
            return task;
        }
        /*result format: 
        result.samples: Rendered audio, float[]
        leadingMs、positionMs、estimatedLengthMs: timeaxis layout in Ms, double
         */

        float[] InvokeDiffsinger(RenderPhrase phrase, double depth, int steps, CancellationTokenSource cancellation, RenderPhraseEvents? renderEvents) {
            var singer = phrase.singer as DiffSingerSinger;
            //Check if dsconfig.yaml is correct
            if(String.IsNullOrEmpty(singer.dsConfig.vocoder) ||
                String.IsNullOrEmpty(singer.dsConfig.acoustic) ||
                String.IsNullOrEmpty(singer.dsConfig.phonemes)){
                if(singer.Errors.Count > 0) {
                    throw new Exception(singer.Errors[0]);
                }
                throw new Exception("Invalid dsconfig.yaml. Please ensure that dsconfig.yaml contains keys \"vocoder\", \"acoustic\" and \"phonemes\".");
            }

            var vocoder = singer.getVocoder();
            //mel specification validity checks
            //num_mel_bins must be a sane vocoder value. This is a hard-coded
            //upper bound (see DsVocoderConfig.MaxMelBins): voicebank authors
            //cannot override it. Not checking this lets a malformed vocoder
            //smuggle in a non-vocoder onnx model that requires an unusually
            //large mel tensor for inputs.
            if (vocoder.num_mel_bins < 1 || vocoder.num_mel_bins > DsVocoderConfig.MaxMelBins) {
                throw new Exception(
                    $"Vocoder num_mel_bins must be between 1 and {DsVocoderConfig.MaxMelBins}, but got {vocoder.num_mel_bins}");
            }
            if (singer.dsConfig.num_mel_bins < 1 || singer.dsConfig.num_mel_bins > DsVocoderConfig.MaxMelBins) {
                throw new Exception(
                    $"Acoustic model num_mel_bins must be between 1 and {DsVocoderConfig.MaxMelBins}, but got {singer.dsConfig.num_mel_bins}");
            }
            //mel base must be 10 or e
            if (vocoder.mel_base != "10" && vocoder.mel_base != "e") {
                throw new Exception(
                    $"Mel base must be \"10\" or \"e\", but got \"{vocoder.mel_base}\" from vocoder");
            }
            if (singer.dsConfig.mel_base != "10" && singer.dsConfig.mel_base != "e") {
                throw new Exception(
                    $"Mel base must be \"10\" or \"e\", but got \"{singer.dsConfig.mel_base}\" from acoustic model");
            }
            //mel scale must be slaney or htk
            if (vocoder.mel_scale != "slaney" && vocoder.mel_scale != "htk") {
                throw new Exception(
                    $"Mel scale must be \"slaney\" or \"htk\", but got \"{vocoder.mel_scale}\" from vocoder");
            }
            if (singer.dsConfig.mel_scale != "slaney" && singer.dsConfig.mel_scale != "htk") {
                throw new Exception(
                    $"Mel scale must be \"slaney\" or \"htk\", but got \"{singer.dsConfig.mel_scale}\" from acoustic model");
            }
            //mel specification matching checks
            if(vocoder.sample_rate != singer.dsConfig.sample_rate) {
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching sample rate ({vocoder.sample_rate} != {singer.dsConfig.sample_rate})");
            }
            if(vocoder.hop_size != singer.dsConfig.hop_size){
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching hop size ({vocoder.hop_size} != {singer.dsConfig.hop_size})");
            }
            if(vocoder.win_size != singer.dsConfig.win_size){
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching win size ({vocoder.win_size} != {singer.dsConfig.win_size})");
            }
            if(vocoder.fft_size != singer.dsConfig.fft_size){
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching FFT size ({vocoder.fft_size} != {singer.dsConfig.fft_size})");
            }
            if (vocoder.num_mel_bins != singer.dsConfig.num_mel_bins) {
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching mel bins ({vocoder.num_mel_bins} != {singer.dsConfig.num_mel_bins})");
            }
            if (Math.Abs(vocoder.mel_fmin - singer.dsConfig.mel_fmin) > 1e-5) {
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching fmin ({vocoder.mel_fmin} != {singer.dsConfig.mel_fmin})");
            }
            if (Math.Abs(vocoder.mel_fmax - singer.dsConfig.mel_fmax) > 1e-5) {
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching fmax ({vocoder.mel_fmax} != {singer.dsConfig.mel_fmax})");
            }
            // mismatching mel base can be transformed
            // if (vocoder.mel_base != singer.dsConfig.mel_base) {
            //     throw new Exception(
            //         $"Vocoder and acoustic model has mismatching mel base ({vocoder.mel_base} != {singer.dsConfig.mel_base})");
            // }
            if (vocoder.mel_scale != singer.dsConfig.mel_scale) {
                throw new Exception(
                    $"Vocoder and acoustic model has mismatching mel scale ({vocoder.mel_scale} != {singer.dsConfig.mel_scale})");
            }

            var acousticModel = singer.getAcousticSession();
            var frameMs = vocoder.frameMs();
            var frameSec = frameMs / 1000;
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            var result = Layout(phrase);
            //acoustic
            //mel = session.run(['mel'], {'tokens': tokens, 'durations': durations, 'f0': f0, 'speedup': speedup})[0]
            //tokens: phoneme index in the phoneme set
            //durations: phoneme duration in frames
            //f0: pitch curve in Hz by frame
            //speedup: Diffusion render speedup, int
            var segments = DiffSingerUtils.PaddedSegments(phrase, frameMs, headFrames, tailFrames);
            var tokens = segments
                .Select(segment => (Int64)singer.PhonemeTokenize(segment.Phoneme))
                .ToList();
            var durations = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames)
                .ToList();
            int totalFrames = durations.Sum();
            float[] f0 = DiffSingerUtils.SampleCurve(phrase, phrase.pitches, 0, frameMs, totalFrames, headFrames, tailFrames, 
                x => MusicMath.ToneToFreq(x * 0.01))
                .Select(f => (float)f).ToArray();
            float[] shiftedF0 = f0.Zip(DiffSingerUtils.SampleCurve(phrase, phrase.toneShift, 0, frameMs, totalFrames,
                headFrames, tailFrames, x => x),
                (x, d) => x * (float) Math.Pow(2, d / 1200)).ToArray();

            var acousticInputs = new List<NamedOnnxValue>();
            acousticInputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
                new DenseTensor<long>(tokens.ToArray(), new int[] { tokens.Count },false)
                .Reshape(new int[] { 1, tokens.Count })));
            acousticInputs.Add(NamedOnnxValue.CreateFromTensor("durations",
                new DenseTensor<long>(durations.Select(x=>(long)x).ToArray(), new int[] { durations.Count }, false)
                .Reshape(new int[] { 1, durations.Count })));
            var f0Tensor = new DenseTensor<float>(f0, new int[] { f0.Length })
                .Reshape(new int[] { 1, f0.Length });
            if (vocoder.pitch_controllable) {
                var shiftedF0Tensor = new DenseTensor<float>(shiftedF0, new int[] { shiftedF0.Length })
                    .Reshape(new int[] { 1, shiftedF0.Length });
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("f0", shiftedF0Tensor));
            } else {
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("f0", f0Tensor));
            }

            // sampling acceleration related
            if (singer.dsConfig.useContinuousAcceleration) {
                if (singer.dsConfig.useVariableDepth) {
                    acousticInputs.Add(NamedOnnxValue.CreateFromTensor("depth",
                        new DenseTensor<float>(new float[] {(float)depth}, new int[] { 1 }, false)));
                }
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("steps",
                    new DenseTensor<long>(new long[] { steps }, new int[] { 1 }, false)));
            } else {
                long speedup;
                if (singer.dsConfig.useVariableDepth) {
                    long int64Depth = (long) Math.Round(depth * 1000);
                    speedup = Math.Max(1, int64Depth / steps);
                    int64Depth = int64Depth / speedup * speedup;  // make sure depth can be divided by speedup
                    acousticInputs.Add(NamedOnnxValue.CreateFromTensor("depth",
                        new DenseTensor<long>(new long[] { int64Depth }, new int[] { 1 }, false)));
                } else {
                    // find a largest integer speedup that are less than 1000 / steps and is a factor of 1000
                    speedup = Math.Max(1, 1000 / steps);
                    while (1000 % speedup != 0 && speedup > 1) {
                        speedup--;
                    }
                }
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("speedup",
                    new DenseTensor<long>(new long[] { speedup }, new int[] { 1 }, false)));
            }
            //Language id
            if(singer.dsConfig.use_lang_id){
                var langIdByPhone = DiffSingerUtils.PaddedLanguageIds(
                    phrase, frameMs, headFrames, tailFrames,
                    phoneme => (long)singer.languageIds.GetValueOrDefault(
                        DiffSingerUtils.PhonemeLanguage(phoneme), 0));
                var langIdTensor = new DenseTensor<Int64>(langIdByPhone, new int[] { langIdByPhone.Length }, false)
                    .Reshape(new int[] { 1, langIdByPhone.Length });
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("languages", langIdTensor));
            }
            //speaker
            if(singer.dsConfig.speakers != null) {
                var speakerEmbedManager = singer.getSpeakerEmbedManager();
                var spkEmbedTensor = speakerEmbedManager.PhraseSpeakerEmbedByFrame(phrase, durations, frameMs, totalFrames, headFrames, tailFrames);
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("spk_embed", spkEmbedTensor));
            }
            //Tunable parameters the acoustic model takes. Acoustic-only ones are the
            //curve itself; variance ones are handled below, on top of their prediction.
            var parameters = singer.paramRegistry;
            foreach (var param in parameters.Acoustic) {
                float[] values = DiffSingerParamInputs.Acoustic(
                    phrase, param, frameMs, totalFrames, headFrames, tailFrames);
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor(param.Name,
                    new DenseTensor<float>(values, new int[] { values.Length })
                    .Reshape(new int[] { 1, values.Length })));
            }

            //Variance parameters: the variance model predicts each one, and the drawn
            //curve is combined with the prediction before it reaches the acoustic model.
            var varianceParams = parameters.Variance;
            if (varianceParams.Count > 0) {
                if(!singer.HasVariancePredictor){
                    throw new Exception(
                        "This singer has no variance predictor but its acoustic model requires one.");
                }
                VarianceResult varianceResult;
                lock(singer.SessionLock){
                    if(cancellation.IsCancellationRequested) {
                        return null;
                    }
                    varianceResult = singer.getVariancePredictor().Process(phrase);
                }
                renderEvents?.ReportRealCurves(BuildRenderedRealCurves(phrase, varianceResult));
                foreach (var param in varianceParams) {
                    if (varianceResult.Channel(param.Name) == null) {
                        throw new KeyNotFoundException(
                            $"The parameter \"{param.Name}\" required by the acoustic model is not found in the variance predictions.");
                    }
                    float[] values = DiffSingerParamInputs.Variance(
                        phrase, param, varianceResult.Channel(param.Name)!, varianceResult,
                        frameMs, totalFrames, headFrames, tailFrames);
                    acousticInputs.Add(NamedOnnxValue.CreateFromTensor(param.Name,
                        new DenseTensor<float>(values, new int[] { values.Length })
                        .Reshape(new int[] { 1, values.Length })));
                }
            }
            Onnx.VerifyInputNames(acousticModel, acousticInputs);
            var acousticCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(singer.acousticHash, acousticInputs)
                : null;
            var acousticOutputs = acousticCache?.Load();
            if (acousticOutputs is null) {
                lock(singer.SessionLock){
                    if(cancellation.IsCancellationRequested) {
                        return null;
                    }
                    acousticOutputs = acousticModel.Run(acousticInputs).Cast<NamedOnnxValue>().ToList();
                }
                acousticCache?.Save(acousticOutputs);
                phrase.AddCacheFile(acousticCache?.Filename);
            }
            Tensor<float> mel = acousticOutputs.First().AsTensor<float>().Clone();
            //mel transforms for different mel base
            if (vocoder.mel_base != singer.dsConfig.mel_base) {
                float k;
                if (vocoder.mel_base == "e" && singer.dsConfig.mel_base == "10") {
                    k = 2.30259f;
                }
                else if (vocoder.mel_base == "10" && singer.dsConfig.mel_base == "e") {
                    k = 0.434294f;
                } else {
                    // this should never happen
                    throw new Exception("This should never happen");
                }
                for (int b = 0; b < mel.Dimensions[0]; ++b) {
                    for (int t = 0; t < mel.Dimensions[1]; ++t) {
                        for (int c = 0; c < mel.Dimensions[2]; ++c) {
                            mel[b, t, c] *= k;
                        }
                    }
                }
            }
            //vocoder
            //waveform = session.run(['waveform'], {'mel': mel, 'f0': f0})[0]
            var vocoderInputs = new List<NamedOnnxValue>();
            vocoderInputs.Add(NamedOnnxValue.CreateFromTensor("mel", mel));
            vocoderInputs.Add(NamedOnnxValue.CreateFromTensor("f0",f0Tensor));
            var vocoderCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(vocoder.hash, vocoderInputs)
                : null;
            var vocoderOutputs = vocoderCache?.Load();
            if (vocoderOutputs is null) {
                lock(singer.SessionLock){
                    if(cancellation.IsCancellationRequested) {
                        return null;
                    }
                    vocoderOutputs = vocoder.session.Run(vocoderInputs).Cast<NamedOnnxValue>().ToList();
                }
                vocoderCache?.Save(vocoderOutputs);
                phrase.AddCacheFile(vocoderCache?.Filename);
            }
            Tensor<float> samplesTensor = vocoderOutputs.First().AsTensor<float>();
            //Check the size of samplesTensor
            int[] expectedShape = new int[] { 1, -1 };
            if(!DiffSingerUtils.ValidateShape(samplesTensor, expectedShape)){
                throw new Exception($"The shape of vocoder output should be (1, length), but the actual shape is {DiffSingerUtils.ShapeString(samplesTensor)}");
            }
            var samples = samplesTensor.ToArray();
            if (vocoder.sample_rate != 44100) {
                var signal = new NWaves.Signals.DiscreteSignal(vocoder.sample_rate, samples);
                signal = NWaves.Operations.Operation.Resample(signal, 44100);
                samples = signal.Samples;
            }
            return samples;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            return LoadRenderedPitch(phrase, pitchSteps: null);
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase, double? pitchSteps, bool fastRealtime = false) {
            DiffSingerSinger singer = (DiffSingerSinger) phrase.singer;
            if (!singer.HasPitchPredictor) {
                throw new Exception("This singer has no pitch predictor.");
            }
            using var dmlScope = Onnx.EnterDmlScope();
            // Use the reference inside SessionLock so a concurrent FreeMemory cannot dispose it first.
            lock (singer.SessionLock) {
                return singer.getPitchPredictor()!.Process(phrase, pitchSteps, fastRealtime);
            }
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase, HashSet<int> selectedNotePositions) {
            return LoadRenderedPitch(phrase, selectedNotePositions, pitchSteps: null, fastRealtime: false);
        }

        /// <summary>Live pitch: partial retake for changed notes with fast sampling settings.</summary>
        internal RenderPitchResult LoadRenderedPitchLive(
            RenderPhrase phrase, HashSet<int> selectedNotePositions, double pitchSteps, bool fastRealtime) {
            return LoadRenderedPitch(phrase, selectedNotePositions, pitchSteps, fastRealtime);
        }

        RenderPitchResult LoadRenderedPitch(
            RenderPhrase phrase,
            HashSet<int> selectedNotePositions,
            double? pitchSteps,
            bool fastRealtime) {
            DiffSingerSinger singer = (DiffSingerSinger) phrase.singer;
            if (!singer.HasPitchPredictor) {
                throw new Exception("This singer has no pitch predictor.");
            }
            using var dmlScope = Onnx.EnterDmlScope();
            var noteRelativePositions = new int[phrase.notes.Length];
            for (int i = 0; i < phrase.notes.Length; i++) {
                noteRelativePositions[i] = phrase.notes[i].position;
            }
            var retakeNoteIndexes = DiffSingerRetake.MapSelectedPositionsToNoteIndexes(
                phrase.position, noteRelativePositions, selectedNotePositions);
            // Use the reference inside SessionLock so a concurrent FreeMemory cannot dispose it first.
            lock (singer.SessionLock) {
                var pitchPredictor = singer.getPitchPredictor()!;
                if (retakeNoteIndexes.Count == 0 || retakeNoteIndexes.Count == phrase.notes.Length) {
                    return pitchPredictor.Process(phrase, pitchSteps, fastRealtime);
                }
                var frameMs = pitchPredictor.FrameMs;
                int headFrames = DiffSingerUtils.headFrames;
                int tailFrames = DiffSingerUtils.tailFrames;
                var ph_dur = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
                int totalFrames = ph_dur.Sum();
                var existingPitch = DiffSingerUtils.SampleCurve(phrase, phrase.pitches, 0, frameMs, totalFrames, headFrames, tailFrames,
                    x => x * 0.01).Select(f => (float)f).ToArray();
                return pitchPredictor.Process(phrase, pitchSteps, fastRealtime, retakeNoteIndexes, existingPitch);
            }
        }

        public void ScheduleRealCurveRefresh(UProject project, UVoicePart part, UCommand command) {
            DiffSingerRealCurveScheduler.TrySchedule(project, part, command);
        }

        public List<RenderRealCurveResult> LoadRenderedRealCurves(RenderPhrase phrase) {
            if (!Preferences.Default.DiffSingerTensorCache) {
                throw new Exception("Please enable DiffSinger tensor cache and re-render the phrase to display correct base curves.");
            }
            DiffSingerSinger singer = (DiffSingerSinger) phrase.singer;
            if (!singer.HasVariancePredictor) {
                return new List<RenderRealCurveResult>(0);
            }
            using var dmlScope = Onnx.EnterDmlScope();
            // Use the reference inside SessionLock so a concurrent FreeMemory cannot dispose it first.
            lock (singer.SessionLock) {
                var result = singer.getVariancePredictor()!.Process(phrase);
                return BuildRenderedRealCurves(phrase, result);
            }
        }

        // The built-in variance parameters, for calls without a voicebank to ask.
        static readonly HashSet<string> builtinVarianceExp = new HashSet<string>() {
            ENE, Format.Ustx.BREC, Format.Ustx.VOIC, Format.Ustx.TENC,
        };

        internal static bool ShouldRefreshRealCurvesOnCurveEdit(USinger? singer, string abbr) {
            var param = (singer as DiffSingerSinger)?.paramRegistry.ByAbbr(abbr);
            if (param != null) {
                return param.Category == DsParamCategory.Variance;
            }
            return singer == null && builtinVarianceExp.Contains(abbr);
        }

        private List<RenderRealCurveResult> BuildRenderedRealCurves(RenderPhrase phrase, VarianceResult result) {
            var frameMs = result.frameMs;
            var headFrames = result.headFrames;
            var tailFrames = result.tailFrames;
            var startMs = phrase.positionMs - headFrames * frameMs;
            var parameters = (phrase.singer as DiffSingerSinger)?.paramRegistry.Variance
                ?? Array.Empty<DsParam>();
            var realCurves = new List<RenderRealCurveResult>(parameters.Count);
            foreach (var param in parameters) {
                var predicted = result.Channel(param.Name) ?? Array.Empty<float>();
                if (predicted.Length == 0) {
                    realCurves.Add(new RenderRealCurveResult {
                        abbr = param.Abbr,
                        ticks = Array.Empty<float>(),
                        values = Array.Empty<float>(),
                    });
                    continue;
                }
                var deltaCurve = DiffSingerUtils.SampleCurve(
                    phrase, param.CurveOf(phrase) ?? Array.Empty<float>(), param.NeutralCurve,
                    frameMs, predicted.Length, headFrames, tailFrames, x => x)
                    .Select(x => (float)x)
                    .ToArray();
                realCurves.Add(new RenderRealCurveResult {
                    abbr = param.Abbr,
                    ticks = Enumerable.Range(0, predicted.Length)
                        .Select(i => (float)phrase.timeAxis.MsPosToTickPos(startMs + i * frameMs) - phrase.position)
                        .ToArray(),
                    values = predicted.Zip(deltaCurve, (p, d) => param.Normalize(param.CombinedValue(p, d)))
                        .ToArray()
                });
            }
            return realCurves;
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            var result = new List<UExpressionDescriptor> {
                //velocity
                new UExpressionDescriptor{
                    name="velocity (curve)",
                    abbr=VELC,
                    type=UExpressionType.Curve,
                    min=0,
                    max=200,
                    defaultValue=100,
                    isFlag=false,
                },
                //energy
                new UExpressionDescriptor{
                    name="energy (curve)",
                    abbr=ENE,
                    type=UExpressionType.Curve,
                    min=-100,
                    max=100,
                    defaultValue=0,
                    isFlag=false,
                },
                //expressiveness
                new UExpressionDescriptor {
                    name = "pitch expressiveness (curve)",
                    abbr = PEXP,
                    type = UExpressionType.Curve,
                    min = 0,
                    max = 100,
                    defaultValue = 100,
                    isFlag = false
                },
            };
            // The voicebank's own parameters, e.g. from params.yaml. The built-in ones
            // are already in the project's default expressions, so suggestions skip them.
            var dsSinger = singer as DiffSingerSinger;
            if (dsSinger != null) {
                foreach (var param in dsSinger.paramRegistry.All) {
                    if (result.Any(d => d.abbr == param.Abbr)) {
                        continue;
                    }
                    result.Add(param.Descriptor);
                }
            }
            //speakers
            if(dsSinger!=null && dsSinger.dsConfig.speakers != null) {
                result.AddRange(Enumerable.Zip(
                    dsSinger.Subbanks,
                    Enumerable.Range(1, dsSinger.Subbanks.Count),
                    (subbank,index)=>new UExpressionDescriptor {
                        name=$"voice color {subbank.Color}",
                        abbr=VoiceColorHeader+index.ToString("D2"),
                        type=UExpressionType.Curve,
                        min=0,
                        max=100,
                        defaultValue=0,
                        isFlag=false,
                    }));
            }

            return result.ToArray();
        }

        public override string ToString() => Renderers.DIFFSINGER;
    }
}
