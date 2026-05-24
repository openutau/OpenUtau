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

        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Format.Ustx.DYN,
            Format.Ustx.PITD,
            Format.Ustx.GENC,
            Format.Ustx.CLR,
            Format.Ustx.BREC,
            Format.Ustx.VOIC,
            Format.Ustx.TENC,
            VELC,
            ENE,
            PEXP,
            Format.Ustx.SHFC,
        };

        private static readonly Dictionary<string, Func<float, float, float>> varianceDeltaFunctions =
            DiffSingerUtils.VarianceDeltaFunctions;

        static readonly object lockObj = new object();

        public USingerType SingerType => USingerType.DiffSinger;

        public bool SupportsRenderPitch => true;

        public bool SupportsRealCurve => true;

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

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender) {
            var task = Task.Run(() => {
                lock (lockObj) {
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
                        result.samples = InvokeDiffsinger(phrase, depth, steps, cancellation);
                        if (result.samples != null) {
                            var source = new WaveSource(0, 0, 0, 1);
                            source.SetSamples(result.samples);
                            WaveFileWriter.CreateWaveFile16(wavPath, new ExportAdapter(source).ToMono(1, 0));
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

        float[] InvokeDiffsinger(RenderPhrase phrase, double depth, int steps, CancellationTokenSource cancellation) {
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
            var tokens = phrase.phones
                .Select(p => p.phoneme)
                .Prepend("SP")
                .Append("SP")
                .Select(phoneme => (Int64)singer.PhonemeTokenize(phoneme))
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
                var langIdByPhone = phrase.phones
                    .Select(p => (long)singer.languageIds.GetValueOrDefault(
                        DiffSingerUtils.PhonemeLanguage(p.phoneme),0
                        ))
                    .Prepend(0)
                    .Append(0)
                    .ToArray();
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
            //gender
            //Definition of GENC: 100 = 12 semitones of formant shift, positive GENC means shift down
            if (singer.dsConfig.useKeyShiftEmbed) {
                var range = singer.dsConfig.augmentationArgs.randomPitchShifting.range;
                var positiveScale = (range[1]==0) ? 0 : (12/range[1]/100);
                var negativeScale = (range[0]==0) ? 0 : (-12/range[0]/100);
                float[] gender = DiffSingerUtils.SampleCurve(phrase, phrase.gender,
                    0, frameMs, totalFrames, headFrames, tailFrames,
                    x=> (x<0)?(-x * positiveScale):(-x * negativeScale))
                    .Select(f => (float)f).ToArray();
                var genderTensor = new DenseTensor<float>(gender, new int[] { gender.Length })
                    .Reshape(new int[] { 1, gender.Length });
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("gender", genderTensor));
            }

            //velocity
            //Definition of VELC: logarithmic scale, Default value 100 = original speed,
            //each 100 increase means speed x2
            if (singer.dsConfig.useSpeedEmbed) {
                var velocityCurve = phrase.curves.FirstOrDefault(curve => curve.Item1 == VELC);
                float[] velocity;
                if (velocityCurve != null) {
                    velocity = DiffSingerUtils.SampleCurve(phrase, velocityCurve.Item2,
                        1, frameMs, totalFrames, headFrames, tailFrames,
                        x => Math.Pow(2, (x - 100) / 100))
                        .Select(f => (float)f).ToArray();
                } else {
                    velocity = Enumerable.Repeat(1f, totalFrames).ToArray();
                }
                var velocityTensor = new DenseTensor<float>(velocity, new int[] { velocity.Length })
                    .Reshape(new int[] { 1, velocity.Length });
                acousticInputs.Add(NamedOnnxValue.CreateFromTensor("velocity", velocityTensor));
            }

            //Variance: Energy, Breathiness, Voicing and Tension
            if(
                singer.dsConfig.useBreathinessEmbed
                || singer.dsConfig.useEnergyEmbed
                || singer.dsConfig.useVoicingEmbed
                || singer.dsConfig.useTensionEmbed) {
                if(!singer.HasVariancePredictor){
                    throw new Exception(
                        "This singer has no variance predictor but its acoustic model requires one.");
                }
                var variancePredictor = singer.getVariancePredictor();
                VarianceResult varianceResult;
                lock(variancePredictor){
                    if(cancellation.IsCancellationRequested) {
                        return null;
                    }
                    varianceResult = singer.getVariancePredictor().Process(phrase);
                }
                //TODO: let user edit variance curves
                if(singer.dsConfig.useEnergyEmbed){
                    var energyCurve = phrase.curves.FirstOrDefault(curve => curve.Item1 == ENE);
                    IEnumerable<float> userEnergy;
                    if(energyCurve!=null){
                        userEnergy = DiffSingerUtils.SampleCurve(phrase, energyCurve.Item2,
                            0, frameMs, totalFrames, headFrames, tailFrames,
                            x => x).Select(x => (float) x);
                    } else{
                        userEnergy = Enumerable.Repeat(0f, totalFrames);
                    }
                    if (varianceResult.energy == null) {
                        throw new KeyNotFoundException(
                            "The parameter \"energy\" required by acoustic model is not found in variance predictions.");
                    }
                    var predictedEnergy = DiffSingerUtils.ResamplePaddedCurve(
                        varianceResult.energy, totalFrames,
                        varianceResult.headFrames, varianceResult.tailFrames,
                        headFrames, tailFrames,
                        varianceResult.frameMs, frameMs);
                    var energy = predictedEnergy.Zip(userEnergy, varianceDeltaFunctions[ENE])
                        .Select(x => Math.Clamp(x, -96f, 0f)).ToArray();
                    acousticInputs.Add(NamedOnnxValue.CreateFromTensor("energy",
                        new DenseTensor<float>(energy, new int[] { energy.Length })
                        .Reshape(new int[] { 1, energy.Length })));
                }
                if(singer.dsConfig.useBreathinessEmbed){
                    var userBreathiness = DiffSingerUtils.SampleCurve(phrase, phrase.breathiness,
                        0, frameMs, totalFrames, headFrames, tailFrames,
                        x => x).Select(x => (float) x);
                    if (varianceResult.breathiness == null) {
                        throw new KeyNotFoundException(
                            "The parameter \"breathiness\" required by acoustic model is not found in variance predictions.");
                    }
                    var predictedBreathiness = DiffSingerUtils.ResamplePaddedCurve(
                        varianceResult.breathiness, totalFrames,
                        varianceResult.headFrames, varianceResult.tailFrames,
                        headFrames, tailFrames,
                        varianceResult.frameMs, frameMs);
                    var breathiness = predictedBreathiness.Zip(userBreathiness, varianceDeltaFunctions[Format.Ustx.BREC])
                        .Select(x => Math.Clamp(x, -96f, 0f)).ToArray();
                    acousticInputs.Add(NamedOnnxValue.CreateFromTensor("breathiness",
                        new DenseTensor<float>(breathiness, new int[] { breathiness.Length })
                        .Reshape(new int[] { 1, breathiness.Length })));
                }
                if(singer.dsConfig.useVoicingEmbed){
                    var userVoicing = DiffSingerUtils.SampleCurve(phrase, phrase.voicing,
                        0, frameMs, totalFrames, headFrames, tailFrames,
                        x => x).Select(x => (float) x);
                    if (varianceResult.voicing == null) {
                        throw new KeyNotFoundException(
                            "The parameter \"voicing\" required by acoustic model is not found in variance predictions.");
                    }
                    var predictedVoicing = DiffSingerUtils.ResamplePaddedCurve(
                        varianceResult.voicing, totalFrames,
                        varianceResult.headFrames, varianceResult.tailFrames,
                        headFrames, tailFrames,
                        varianceResult.frameMs, frameMs);
                    var voicing = predictedVoicing.Zip(userVoicing, varianceDeltaFunctions[Format.Ustx.VOIC])
                        .Select(x => Math.Clamp(x, -96f, 0f)).ToArray();
                    acousticInputs.Add(NamedOnnxValue.CreateFromTensor("voicing",
                        new DenseTensor<float>(voicing, new int[] { voicing.Length })
                        .Reshape(new int[] { 1, voicing.Length })));
                }
                if(singer.dsConfig.useTensionEmbed){
                    var userTension = DiffSingerUtils.SampleCurve(phrase, phrase.tension,
                        0, frameMs, totalFrames, headFrames, tailFrames,
                        x => x).Select(x => (float) x);
                    if (varianceResult.tension == null) {
                        throw new KeyNotFoundException(
                            "The parameter \"tension\" required by acoustic model is not found in variance predictions.");
                    }
                    var predictedTension = DiffSingerUtils.ResamplePaddedCurve(
                        varianceResult.tension, totalFrames,
                        varianceResult.headFrames, varianceResult.tailFrames,
                        headFrames, tailFrames,
                        varianceResult.frameMs, frameMs);
                    var tension = predictedTension.Zip(userTension, varianceDeltaFunctions[Format.Ustx.TENC])
                        .Select(x => Math.Clamp(x, -10f, 10f)).ToArray();
                    acousticInputs.Add(NamedOnnxValue.CreateFromTensor("tension",
                        new DenseTensor<float>(tension, new int[] { tension.Length })
                        .Reshape(new int[] { 1, tension.Length })));
                }
            }
            Onnx.VerifyInputNames(acousticModel, acousticInputs);
            var acousticCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(singer.acousticHash, acousticInputs)
                : null;
            var acousticOutputs = acousticCache?.Load();
            if (acousticOutputs is null) {
                lock(acousticModel){
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
                lock(vocoder){
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
            return samples;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            DiffSingerSinger singer = (DiffSingerSinger) phrase.singer;
            if (!singer.HasPitchPredictor) {
                throw new Exception("This singer has no pitch predictor.");
            }
            var pitchPredictor = singer.getPitchPredictor()!;
            lock (pitchPredictor) {
                return pitchPredictor.Process(phrase);
            }
        }

        public List<RenderRealCurveResult> LoadRenderedRealCurves(RenderPhrase phrase) {
            if (!Preferences.Default.DiffSingerTensorCache) {
                throw new Exception("Please enable DiffSinger tensor cache and re-render the phrase to display correct base curves.");
            }
            DiffSingerSinger singer = (DiffSingerSinger) phrase.singer;
            if (!singer.HasVariancePredictor) {
                return new List<RenderRealCurveResult>(0);
            }
            var variancePredictor = singer.getVariancePredictor()!;
            lock (variancePredictor) {
                var result = variancePredictor.Process(phrase);
                var frameMs = result.frameMs;
                var headFrames = result.headFrames;
                var tailFrames = result.tailFrames;
                var startMs = phrase.positionMs - headFrames * frameMs;
                var realCurves = new (string, float[], float[], Func<float, float>)[] {
                    (
                        ENE, result.energy ?? Array.Empty<float>(),
                        phrase.curves.FirstOrDefault(curve => curve.Item1 == ENE)?.Item2
                        ?? Array.Empty<float>(),
                        x => Math.Clamp(x, -96f, 0f) / 96f + 1f
                    ),
                    (
                        Format.Ustx.BREC, result.breathiness ?? Array.Empty<float>(), phrase.breathiness,
                        x => Math.Clamp(x, -96f, 0f) / 96f + 1f
                    ),
                    (
                        Format.Ustx.VOIC, result.voicing ?? Array.Empty<float>(), phrase.voicing,
                        x => Math.Clamp(x, -96f, 0f) / 96f + 1f
                    ),
                    (
                        Format.Ustx.TENC, result.tension ?? Array.Empty<float>(), phrase.tension,
                        x => Math.Clamp(x, -10f, 10f) / 20f + 0.5f
                    ),
                }.Select(t => {
                    var abbr = t.Item1;
                    var realCurve = t.Item2;
                    if (realCurve.Length == 0) {
                        return new RenderRealCurveResult {
                            abbr = abbr,
                            ticks = Array.Empty<float>(),
                            values = Array.Empty<float>(),
                        };
                    }
                    var deltaCurve = DiffSingerUtils.SampleCurve(
                        phrase, t.Item3, 0, frameMs, realCurve.Length,
                        headFrames, tailFrames, x => x)
                        .Select(x => (float)x)
                        .ToArray();
                    var normFunc = t.Item4;
                    return new RenderRealCurveResult {
                        abbr = abbr,
                        ticks = Enumerable.Range(0, realCurve.Length)
                            .Select(i => (float)phrase.timeAxis.MsPosToTickPos(startMs + i * frameMs) - phrase.position)
                            .ToArray(),
                        values = realCurve.Zip(deltaCurve, varianceDeltaFunctions[abbr])
                            .Select(normFunc)
                            .ToArray()
                    };
                }).ToList();
                return realCurves;
            }
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
            //speakers
            var dsSinger = singer as DiffSingerSinger;
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
