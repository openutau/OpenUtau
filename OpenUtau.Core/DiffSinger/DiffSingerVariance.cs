using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using Serilog;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using OpenUtau.Api;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.DiffSinger{
    /// <summary>
    /// The variance model's predictions for one phrase: one channel per variance
    /// parameter, in the order the model's heads come in. <see cref="names"/> and
    /// <see cref="channels"/> are parallel, and a channel is null when the model did
    /// not predict it.
    /// </summary>
    public sealed class VarianceResult {
        public readonly string[] names;
        public readonly float[]?[] channels;
        public float frameMs;
        public int headFrames;
        public int tailFrames;
        public int totalFrames;

        public VarianceResult(
            string[] names, float[]?[] channels,
            float frameMs, int headFrames, int tailFrames, int totalFrames) {
            this.names = names;
            this.channels = channels;
            this.frameMs = frameMs;
            this.headFrames = headFrames;
            this.tailFrames = tailFrames;
            this.totalFrames = totalFrames;
        }

        /// <summary>The prediction of one parameter, or null when the model did not predict it.</summary>
        public float[]? Channel(string name) {
            for (int i = 0; i < names.Length && i < channels.Length; i++) {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) {
                    return channels[i];
                }
            }
            return null;
        }

        public VarianceResult Clone() => new VarianceResult(
            (string[])names.Clone(),
            channels.Select(channel => channel?.ToArray()).ToArray(),
            frameMs, headFrames, tailFrames, totalFrames);
    }
    public class DsVariance : IDisposable{
        string rootPath;
        DsConfig dsConfig;
        readonly IReadOnlyList<DsParam> predictedParams;
        Dictionary<string, int> languageIds = new Dictionary<string, int>();
        Dictionary<string, int> phonemeTokens;
        ulong linguisticHash;
        ulong varianceHash;
        InferenceSession linguisticModel;
        InferenceSession varianceModel;
        IG2p g2p;
        float frameMs;
        DiffSingerSpeakerEmbedManager speakerEmbedManager;
        const int VariancePatchStateCapacity = 16;
        readonly VariancePatchStateCache variancePatchStates =
            new VariancePatchStateCache(VariancePatchStateCapacity);

        public float FrameMs => frameMs;

        /// <summary>
        /// The variance parameters this predictor produces, in the order of the model's
        /// heads, including heads the acoustic model does not consume. The singer's
        /// registry supplies overrides; standalone users get one from dsconfig.
        /// </summary>
        public IReadOnlyList<DsParam> Params => predictedParams;

        public DsVariance(string rootPath, DsParamRegistry? registry = null)
        {
            this.rootPath = rootPath;
            var dsconfigPath = Path.Combine(rootPath, "dsconfig.yaml");
            try {
                dsConfig = DsConfig.Load(rootPath);
            } catch (Exception e) {
                throw new Exception($"Failed to load {dsconfigPath}", e);
            }
            if(dsConfig.variance == null){
                throw new Exception("This voicebank doesn't contain a variance model");
            }
            var paramRegistry = registry ?? DsParamRegistry.From(dsConfig);
            predictedParams = DsParamRegistry.Predicted(dsConfig, paramRegistry);
            foreach (var param in paramRegistry.Variance) {
                if (!dsConfig.Predicts(param.Name)) {
                    throw new InvalidDataException(
                        $"The variance dsconfig.yaml of {rootPath} does not set predict_{param.Name}, " +
                        $"but params.yaml declares \"{param.Name}\" as a variance parameter.");
                }
            }
            //Load language id if needed
            if(dsConfig.use_lang_id){
                if(dsConfig.languages == null){
                    throw new Exception("\"languages\" field is not specified in dsconfig.yaml");
                }
                var langIdPath = Path.Join(rootPath, dsConfig.languages);
                try {
                    languageIds = DiffSingerUtils.LoadLanguageIds(langIdPath);
                } catch (Exception e) {
                    Log.Error(e, $"failed to load language id from {langIdPath}");
                    throw new Exception($"Failed to load {langIdPath}", e);
                }
            }
            //Load phonemes list
            if (dsConfig.phonemes == null) {
                throw new Exception("Configuration key \"phonemes\" is null.");
            }
            string phonemesPath = Path.Combine(rootPath, dsConfig.phonemes);
            phonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);
            //Load models
            if (dsConfig.linguistic == null) {
                throw new Exception("Configuration key \"linguistic\" is null.");
            }
            var linguisticModelPath = Path.Join(rootPath, dsConfig.linguistic);
            var linguisticModelBytes = File.ReadAllBytes(linguisticModelPath);
            linguisticHash = XXH64.DigestOf(linguisticModelBytes);
            linguisticModel = Onnx.getInferenceSession(linguisticModelBytes);
            try {
                var varianceModelPath = Path.Join(rootPath, dsConfig.variance);
                var varianceModelBytes = File.ReadAllBytes(varianceModelPath);
                varianceHash = XXH64.DigestOf(varianceModelBytes);
                varianceModel = Onnx.getInferenceSession(varianceModelBytes);
                // Every variance parameter has to be an input and a prediction of the model;
                // a mismatch here is a config error, not something to discover mid-render.
                var varianceInputNames = varianceModel.InputNames.ToHashSet();
                var varianceOutputNames = varianceModel.OutputNames.ToHashSet();
                foreach (var param in predictedParams) {
                    if (!varianceInputNames.Contains(param.Name) || !varianceOutputNames.Contains(param.Name + "_pred")) {
                        throw new InvalidDataException(
                            $"The variance model of {rootPath} takes no \"{param.Name}\" input or does not output \"{param.Name}_pred\". " +
                            "Check dsconfig.yaml and params.yaml.");
                    }
                }
                frameMs = 1000f * dsConfig.hop_size / dsConfig.sample_rate;
                //Load g2p
                g2p = LoadG2p(rootPath);
            } catch {
                linguisticModel.Dispose();
                varianceModel?.Dispose();
                throw;
            }
        }

        protected IG2p LoadG2p(string rootPath) {
            // Load dictionary from singer folder.
            string file = Path.Combine(rootPath, "dsdict.yaml");
            if(!File.Exists(file)){
                throw new Exception($"File not found: {file}");
            }
            try {
                var g2pBuilder = G2pDictionary.NewBuilder().Load(File.ReadAllText(file));
                //SP and AP should always be vowel
                g2pBuilder.AddSymbol("SP", true);
                g2pBuilder.AddSymbol("AP", true);
                return g2pBuilder.Build();
            } catch (Exception e) {
                throw new Exception($"Failed to load {file}", e);
            }
        }

        public DiffSingerSpeakerEmbedManager getSpeakerEmbedManager(){
            if(speakerEmbedManager is null) {
                speakerEmbedManager = new DiffSingerSpeakerEmbedManager(dsConfig, rootPath);
            }
            return speakerEmbedManager;
        }

        int PhonemeTokenize(string phoneme){
            bool success = phonemeTokens.TryGetValue(phoneme, out int token);
            if(!success){
                throw new Exception($"Phoneme \"{phoneme}\" isn't supported by variance model. Please check {Path.Combine(rootPath, dsConfig.phonemes)}");
            }
            return token;
        }

        public VarianceResult Process(RenderPhrase phrase){
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            if (dsConfig.predict_dur) {
                //Check if all phonemes are defined in dsdict.yaml (for their types)
                foreach (var phone in phrase.phones) {
                    if (!g2p.IsValidSymbol(phone.phoneme)) {
                        throw new InvalidDataException(
                            $"Type definition of symbol \"{phone.phoneme}\" not found. Consider adding it to dsdict.yaml of the variance predictor.");
                    }
                }
            }
            //Linguistic Encoder
            var linguisticInputs = new List<NamedOnnxValue>();
            var segments = DiffSingerUtils.PaddedSegments(phrase, frameMs, headFrames, tailFrames);
            var tokens = segments.Select(x => (Int64)PhonemeTokenize(x.Phoneme)).ToArray();
            var ph_dur = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            int totalFrames = ph_dur.Sum();
            linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
                new DenseTensor<Int64>(tokens, new int[] { tokens.Length }, false)
                .Reshape(new int[] { 1, tokens.Length })));
            if(dsConfig.predict_dur){
                //if predict_dur is true, use word encode mode
                var (word_div, word_dur) = DiffSingerUtils.PaddedWordDivAndDur(phrase, ph_dur, g2p.IsVowel, frameMs, headFrames, tailFrames);
                linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("word_div",
                    new DenseTensor<Int64>(word_div, new int[] { word_div.Length }, false)
                    .Reshape(new int[] { 1, word_div.Length })));
                linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("word_dur",
                    new DenseTensor<Int64>(word_dur, new int[] { word_dur.Length }, false)
                    .Reshape(new int[] { 1, word_dur.Length })));
            }else{
                //if predict_dur is false, use phoneme encode mode
                linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("ph_dur",
                    new DenseTensor<Int64>(ph_dur.Select(x=>(Int64)x).ToArray(), new int[] { ph_dur.Length }, false)
                    .Reshape(new int[] { 1, ph_dur.Length })));
            }
            //Language id
            if(dsConfig.use_lang_id){
                var langIdByPhone = DiffSingerUtils.PaddedLanguageIds(
                    phrase, frameMs, headFrames, tailFrames,
                    phoneme => (long)languageIds.GetValueOrDefault(
                        DiffSingerUtils.PhonemeLanguage(phoneme), 0));
                var langIdTensor = new DenseTensor<Int64>(langIdByPhone, new int[] { langIdByPhone.Length }, false)
                    .Reshape(new int[] { 1, langIdByPhone.Length });
                linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("languages", langIdTensor));
            }

            Onnx.VerifyInputNames(linguisticModel, linguisticInputs);
            var linguisticCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(linguisticHash, linguisticInputs)
                : null;
            var linguisticOutputs = linguisticCache?.Load();
            if (linguisticOutputs is null) {
                linguisticOutputs = linguisticModel.Run(linguisticInputs).Cast<NamedOnnxValue>().ToList();
                linguisticCache?.Save(linguisticOutputs);
                phrase.AddCacheFile(linguisticCache?.Filename);
            }
            Tensor<float> encoder_out = linguisticOutputs
                .Where(o => o.Name == "encoder_out")
                .First()
                .AsTensor<float>();

            //Variance Predictor
            var pitch = DiffSingerUtils.SampleCurve(phrase, phrase.pitches, 0, frameMs, totalFrames, headFrames, tailFrames, 
                x => x * 0.01).Select(f => (float)f).ToArray();
            var toneShift = DiffSingerUtils.SampleCurve(phrase, phrase.toneShift, 0, frameMs, totalFrames, headFrames, tailFrames,
                x => x * 0.01).Select(f => (float)f).ToArray();
            pitch = pitch.Zip(toneShift, (x, d) => x + d).ToArray();

            var varianceInputs = new List<NamedOnnxValue>();
            var variancePatchInputs = new List<NamedOnnxValue>();
            void AddVarianceInput(NamedOnnxValue input, bool includeInPatchKey = true) {
                varianceInputs.Add(input);
                if (includeInPatchKey) {
                    variancePatchInputs.Add(input);
                }
            }
            AddVarianceInput(NamedOnnxValue.CreateFromTensor("encoder_out", encoder_out));
            AddVarianceInput(NamedOnnxValue.CreateFromTensor("ph_dur",
                new DenseTensor<Int64>(ph_dur.Select(x=>(Int64)x).ToArray(), new int[] { ph_dur.Length }, false)
                .Reshape(new int[] { 1, ph_dur.Length })));
            AddVarianceInput(NamedOnnxValue.CreateFromTensor("pitch",
                new DenseTensor<float>(pitch, new int[] { pitch.Length }, false)
                .Reshape(new int[] { 1, totalFrames })), includeInPatchKey: false);
            var varianceParams = predictedParams;
            foreach (var param in varianceParams) {
                var zeros = Enumerable.Repeat(0f, totalFrames).ToArray();
                AddVarianceInput(NamedOnnxValue.CreateFromTensor(param.Name,
                    new DenseTensor<float>(zeros, new int[] { zeros.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }

            var numVariances = varianceParams.Count;
            var retake = Enumerable.Repeat(true, totalFrames * numVariances).ToArray();
            AddVarianceInput(NamedOnnxValue.CreateFromTensor("retake",
                new DenseTensor<bool>(retake, new int[] { retake.Length }, false)
                .Reshape(new int[] { 1, totalFrames, numVariances })));
            var steps = Preferences.Default.DiffSingerStepsVariance;
            if (dsConfig.useContinuousAcceleration) {
                AddVarianceInput(NamedOnnxValue.CreateFromTensor("steps",
                    new DenseTensor<long>(new long[] { steps }, new int[] { 1 }, false)));
            } else {
                // find a largest integer speedup that are less than 1000 / steps and is a factor of 1000
                long speedup = Math.Max(1, 1000 / steps);
                while (1000 % speedup != 0 && speedup > 1) {
                    speedup--;
                }
                AddVarianceInput(NamedOnnxValue.CreateFromTensor("speedup",
                    new DenseTensor<long>(new long[] { speedup }, new int[] { 1 },false)));
            }
            //Speaker
            float[]? speakerEmbed = null;
            if(dsConfig.speakers != null) {
                var speakerEmbedManager = getSpeakerEmbedManager();
                var spkEmbedTensor = speakerEmbedManager.PhraseSpeakerEmbedByFrame(phrase, ph_dur, frameMs, totalFrames, headFrames, tailFrames);
                speakerEmbed = spkEmbedTensor.ToArray();
                // Speaker embedding is a retake-able frame-level condition.
                AddVarianceInput(NamedOnnxValue.CreateFromTensor("spk_embed", spkEmbedTensor), includeInPatchKey: false);
            }
            ulong? variancePatchKey = null;
            if (Preferences.Default.DiffSingerTensorCache &&
                Preferences.Default.DiffSingerVarianceLocalPitchPatch) {
                var baseHash = new DiffSingerCache(varianceHash, variancePatchInputs).Hash;
                variancePatchKey = DiffSingerVariancePatch.BuildStateKey(baseHash, phrase.position, phrase.end);
            }
            // Cache the final pipeline result in a separate namespace from raw predictor outputs.
            var resultCacheInputs = new List<NamedOnnxValue>(varianceInputs) {
                NamedOnnxValue.CreateFromTensor(
                    "result_cache_version",
                    new DenseTensor<long>(new long[] { 1 }, new int[] { 1 }, false)),
            };
            var resultCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(varianceHash, resultCacheInputs)
                : null;
            var cachedOutputs = resultCache?.Load();
            if (cachedOutputs != null) {
                var cachedResult = ParseVarianceResult(cachedOutputs, frameMs, headFrames, tailFrames, totalFrames);
                if (variancePatchKey.HasValue) {
                    variancePatchStates.Set(
                        variancePatchKey.Value,
                        new VariancePatchState(pitch, speakerEmbed, cachedResult));
                }
                return cachedResult;
            }
            VariancePatchState? previous = null;
            bool[]? retakeMask = null;
            if (variancePatchKey.HasValue && variancePatchStates.TryGetValue(variancePatchKey.Value, out var cachedState) &&
                DiffSingerVariancePatch.IsMetadataCompatible(cachedState.result, frameMs, headFrames, tailFrames, totalFrames) &&
                DiffSingerVariancePatch.IsChannelLayoutCompatible(cachedState.result, varianceParams, totalFrames)) {
                previous = cachedState;
                var pitchMask = DiffSingerVariancePatch.BuildChangedFrameMask(cachedState.pitch, pitch, 1e-4f);
                var speakerMask = DiffSingerVariancePatch.BuildChangedFrameMask(
                    cachedState.speakerEmbed ?? Array.Empty<float>(),
                    speakerEmbed ?? Array.Empty<float>(),
                    totalFrames,
                    1e-4f);
                retakeMask = new bool[totalFrames];
                for (int i = 0; i < retakeMask.Length; i++) {
                    retakeMask[i] = (i < pitchMask.Length && pitchMask[i]) ||
                        (i < speakerMask.Length && speakerMask[i]);
                }
                if (!retakeMask.Any(x => x)) {
                    return DiffSingerVariancePatch.CloneResult(cachedState.result);
                }
                if (retakeMask.All(x => x)) {
                    previous = null;
                } else {
                    ReplaceVarianceInputsWithPrevious(varianceInputs, cachedState.result);
                }
            }
            if (retakeMask != null) {
                var retakeTensorValues = DiffSingerVariancePatch.ExpandToChannels(retakeMask, numVariances);
                var retakeInput = varianceInputs.First(x => x.Name == "retake");
                varianceInputs[varianceInputs.IndexOf(retakeInput)] = NamedOnnxValue.CreateFromTensor(
                    "retake",
                    new DenseTensor<bool>(retakeTensorValues, new[] { retakeTensorValues.Length }, false)
                        .Reshape(new[] { 1, totalFrames, numVariances }));
            }
            Onnx.VerifyInputNames(varianceModel, varianceInputs);
            var varianceOutputs = varianceModel.Run(varianceInputs).Cast<NamedOnnxValue>().ToList();
            var channels = new float[]?[varianceParams.Count];
            for (int i = 0; i < varianceParams.Count; i++) {
                channels[i] = varianceOutputs
                    .Where(o => o.Name == varianceParams[i].Name + "_pred")
                    .First()
                    .AsTensor<float>()
                    .ToArray();
            }
            var result = new VarianceResult(
                varianceParams.Select(param => param.Name).ToArray(), channels,
                frameMs, headFrames, tailFrames, totalFrames);
            if (previous != null && retakeMask != null) {
                var channelMask = DiffSingerVariancePatch.ExpandToChannels(retakeMask, numVariances);
                result = DiffSingerVariancePatch.HardCompose(previous.result, result, channelMask, numVariances);
            }
            if (resultCache != null) {
                resultCache.Save(BuildVarianceOutputs(result));
                phrase.AddCacheFile(resultCache.Filename);
            }
            if (variancePatchKey.HasValue) {
                variancePatchStates.Set(
                    variancePatchKey.Value,
                    new VariancePatchState(pitch, speakerEmbed, result));
            }
            return result;
        }

        VarianceResult ParseVarianceResult(
            ICollection<NamedOnnxValue> outputs,
            float frameMs,
            int headFrames,
            int tailFrames,
            int totalFrames) {
            var varianceParams = predictedParams;
            var channels = new float[]?[varianceParams.Count];
            for (int i = 0; i < varianceParams.Count; i++) {
                channels[i] = outputs.First(o => o.Name == varianceParams[i].Name + "_pred").AsTensor<float>().ToArray();
            }
            return new VarianceResult(
                varianceParams.Select(param => param.Name).ToArray(), channels,
                frameMs, headFrames, tailFrames, totalFrames);
        }

        List<NamedOnnxValue> BuildVarianceOutputs(VarianceResult result) {
            var outputs = new List<NamedOnnxValue>();
            for (int i = 0; i < result.names.Length; i++) {
                var values = result.channels[i];
                if (values == null) {
                    continue;
                }
                outputs.Add(NamedOnnxValue.CreateFromTensor(
                    result.names[i] + "_pred",
                    new DenseTensor<float>(values, new[] { values.Length }, false)
                        .Reshape(new[] { 1, values.Length })));
            }
            return outputs;
        }

        static void ReplaceVarianceInputsWithPrevious(
            List<NamedOnnxValue> inputs,
            VarianceResult previous) {
            for (int i = 0; i < previous.names.Length; i++) {
                var values = previous.channels[i];
                if (values == null) continue;
                var input = inputs.FirstOrDefault(x => x.Name == previous.names[i]);
                if (input == null) continue;
                var current = input.AsTensor<float>().ToArray();
                if (current.Length != values.Length) continue;
                Array.Copy(values, current, values.Length);
                inputs[inputs.IndexOf(input)] = NamedOnnxValue.CreateFromTensor(
                    previous.names[i],
                    new DenseTensor<float>(current, new[] { current.Length }, false)
                        .Reshape(new[] { 1, current.Length }));
            }
        }

        private bool disposedValue;

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    linguisticModel?.Dispose();
                    varianceModel?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
