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
    public struct VarianceResult{
        public float[]? energy;
        public float[]? breathiness;
        public float[]? voicing;
        public float[]? tension;
        public float frameMs;
        public int headFrames;
        public int tailFrames;
        public int totalFrames;
    }
    public class DsVariance : IDisposable{
        string rootPath;
        DsConfig dsConfig;
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

        public DsVariance(string rootPath)
        {
            this.rootPath = rootPath;
            var dsconfigPath = Path.Combine(rootPath, "dsconfig.yaml");
            try {
                dsConfig = Yaml.DefaultDeserializer.Deserialize<DsConfig>(
                    File.ReadAllText(dsconfigPath, Encoding.UTF8));
            } catch (Exception e) {
                throw new Exception($"Failed to load {dsconfigPath}", e);
            }
            if(dsConfig.variance == null){
                throw new Exception("This voicebank doesn't contain a variance model");
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
            var varianceModelPath = Path.Join(rootPath, dsConfig.variance);
            var varianceModelBytes = File.ReadAllBytes(varianceModelPath);
            varianceHash = XXH64.DigestOf(varianceModelBytes);
            varianceModel = Onnx.getInferenceSession(varianceModelBytes);
            frameMs = 1000f * dsConfig.hop_size / dsConfig.sample_rate;
            //Load g2p
            g2p = LoadG2p(rootPath);
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
            if (dsConfig.predict_energy) {
                var energy = Enumerable.Repeat(0f, totalFrames).ToArray();
                AddVarianceInput(NamedOnnxValue.CreateFromTensor("energy",
                    new DenseTensor<float>(energy, new int[] { energy.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }
            if (dsConfig.predict_breathiness) {
                var breathiness = Enumerable.Repeat(0f, totalFrames).ToArray();
                AddVarianceInput(NamedOnnxValue.CreateFromTensor("breathiness",
                    new DenseTensor<float>(breathiness, new int[] { breathiness.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }
            if (dsConfig.predict_voicing) {
                var voicing = Enumerable.Repeat(0f, totalFrames).ToArray();
                AddVarianceInput(NamedOnnxValue.CreateFromTensor("voicing",
                    new DenseTensor<float>(voicing, new int[] { voicing.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }
            if (dsConfig.predict_tension) {
                var tension = Enumerable.Repeat(0f, totalFrames).ToArray();
                AddVarianceInput(NamedOnnxValue.CreateFromTensor("tension",
                    new DenseTensor<float>(tension, new int[] { tension.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }

            var numVariances = new[] {
                dsConfig.predict_energy,
                dsConfig.predict_breathiness,
                dsConfig.predict_voicing,
                dsConfig.predict_tension,
            }.Sum(Convert.ToInt32);
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
                DiffSingerVariancePatch.IsMetadataCompatible(cachedState.result, new VarianceResult {
                    frameMs = frameMs,
                    headFrames = headFrames,
                    tailFrames = tailFrames,
                    totalFrames = totalFrames,
                }) &&
                DiffSingerVariancePatch.IsChannelLayoutCompatible(
                    cachedState.result,
                    totalFrames,
                    dsConfig.predict_energy,
                    dsConfig.predict_breathiness,
                    dsConfig.predict_voicing,
                    dsConfig.predict_tension)) {
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
            Tensor<float>? energy_pred = dsConfig.predict_energy
                ? varianceOutputs
                    .Where(o => o.Name == "energy_pred")
                    .First()
                    .AsTensor<float>()
                : null;
            Tensor<float>? breathiness_pred = dsConfig.predict_breathiness
                ? varianceOutputs
                    .Where(o => o.Name == "breathiness_pred")
                    .First()
                    .AsTensor<float>()
                : null;
            Tensor<float>? voicing_pred = dsConfig.predict_voicing
                ? varianceOutputs
                    .Where(o => o.Name == "voicing_pred")
                    .First()
                    .AsTensor<float>()
                : null;
            Tensor<float>? tension_pred = dsConfig.predict_tension
                ? varianceOutputs
                    .Where(o => o.Name == "tension_pred")
                    .First()
                    .AsTensor<float>()
                : null;
            var result = new VarianceResult{
                energy = energy_pred?.ToArray(),
                breathiness = breathiness_pred?.ToArray(),
                voicing = voicing_pred?.ToArray(),
                tension = tension_pred?.ToArray(),
                frameMs = frameMs,
                headFrames = headFrames,
                tailFrames = tailFrames,
                totalFrames = totalFrames,
            };
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
            return new VarianceResult {
                energy = dsConfig.predict_energy ? outputs.First(o => o.Name == "energy_pred").AsTensor<float>().ToArray() : null,
                breathiness = dsConfig.predict_breathiness ? outputs.First(o => o.Name == "breathiness_pred").AsTensor<float>().ToArray() : null,
                voicing = dsConfig.predict_voicing ? outputs.First(o => o.Name == "voicing_pred").AsTensor<float>().ToArray() : null,
                tension = dsConfig.predict_tension ? outputs.First(o => o.Name == "tension_pred").AsTensor<float>().ToArray() : null,
                frameMs = frameMs,
                headFrames = headFrames,
                tailFrames = tailFrames,
                totalFrames = totalFrames,
            };
        }

        List<NamedOnnxValue> BuildVarianceOutputs(VarianceResult result) {
            var outputs = new List<NamedOnnxValue>();
            void Add(string name, float[]? values) {
                if (values != null) {
                    outputs.Add(NamedOnnxValue.CreateFromTensor(
                        name,
                        new DenseTensor<float>(values, new[] { values.Length }, false)
                            .Reshape(new[] { 1, values.Length })));
                }
            }
            Add("energy_pred", result.energy);
            Add("breathiness_pred", result.breathiness);
            Add("voicing_pred", result.voicing);
            Add("tension_pred", result.tension);
            return outputs;
        }

        static void ReplaceVarianceInputsWithPrevious(
            List<NamedOnnxValue> inputs,
            VarianceResult previous) {
            var channels = new[] {
                ("energy", previous.energy),
                ("breathiness", previous.breathiness),
                ("voicing", previous.voicing),
                ("tension", previous.tension),
            };
            foreach (var (name, values) in channels) {
                if (values == null) continue;
                var input = inputs.FirstOrDefault(x => x.Name == name);
                if (input == null) continue;
                var current = input.AsTensor<float>().ToArray();
                if (current.Length != values.Length) continue;
                Array.Copy(values, current, values.Length);
                inputs[inputs.IndexOf(input)] = NamedOnnxValue.CreateFromTensor(
                    name,
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
