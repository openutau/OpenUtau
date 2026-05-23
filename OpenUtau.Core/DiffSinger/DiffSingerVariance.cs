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
            var tokens = phrase.phones.Select(p => p.phoneme)
                .Prepend("SP")
                .Append("SP")
                .Select(x => (Int64)PhonemeTokenize(x))
                .ToArray();
            var ph_dur = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            int totalFrames = ph_dur.Sum();
            linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
                new DenseTensor<Int64>(tokens, new int[] { tokens.Length }, false)
                .Reshape(new int[] { 1, tokens.Length })));
            if(dsConfig.predict_dur){
                //if predict_dur is true, use word encode mode
                var (word_div, word_dur) = DiffSingerUtils.PaddedWordDivAndDur(phrase, ph_dur, g2p.IsVowel);
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
                var langIdByPhone = phrase.phones
                    .Select(p => (long)languageIds.GetValueOrDefault(
                        DiffSingerUtils.PhonemeLanguage(p.phoneme),0
                        ))
                    .Prepend(0)
                    .Append(0)
                    .ToArray();
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
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_out", encoder_out));
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("ph_dur",
                new DenseTensor<Int64>(ph_dur.Select(x=>(Int64)x).ToArray(), new int[] { ph_dur.Length }, false)
                .Reshape(new int[] { 1, ph_dur.Length })));
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("pitch",
                new DenseTensor<float>(pitch, new int[] { pitch.Length }, false)
                .Reshape(new int[] { 1, totalFrames })));
            if (dsConfig.predict_energy) {
                var energy = Enumerable.Repeat(0f, totalFrames).ToArray();
                varianceInputs.Add(NamedOnnxValue.CreateFromTensor("energy",
                    new DenseTensor<float>(energy, new int[] { energy.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }
            if (dsConfig.predict_breathiness) {
                var breathiness = Enumerable.Repeat(0f, totalFrames).ToArray();
                varianceInputs.Add(NamedOnnxValue.CreateFromTensor("breathiness",
                    new DenseTensor<float>(breathiness, new int[] { breathiness.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }
            if (dsConfig.predict_voicing) {
                var voicing = Enumerable.Repeat(0f, totalFrames).ToArray();
                varianceInputs.Add(NamedOnnxValue.CreateFromTensor("voicing",
                    new DenseTensor<float>(voicing, new int[] { voicing.Length }, false)
                        .Reshape(new int[] { 1, totalFrames })));
            }
            if (dsConfig.predict_tension) {
                var tension = Enumerable.Repeat(0f, totalFrames).ToArray();
                varianceInputs.Add(NamedOnnxValue.CreateFromTensor("tension",
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
            varianceInputs.Add(NamedOnnxValue.CreateFromTensor("retake",
                new DenseTensor<bool>(retake, new int[] { retake.Length }, false)
                .Reshape(new int[] { 1, totalFrames, numVariances })));
            var steps = Preferences.Default.DiffSingerStepsVariance;
            if (dsConfig.useContinuousAcceleration) {
                varianceInputs.Add(NamedOnnxValue.CreateFromTensor("steps",
                    new DenseTensor<long>(new long[] { steps }, new int[] { 1 }, false)));
            } else {
                // find a largest integer speedup that are less than 1000 / steps and is a factor of 1000
                long speedup = Math.Max(1, 1000 / steps);
                while (1000 % speedup != 0 && speedup > 1) {
                    speedup--;
                }
                varianceInputs.Add(NamedOnnxValue.CreateFromTensor("speedup",
                    new DenseTensor<long>(new long[] { speedup }, new int[] { 1 },false)));
            }
            //Speaker
            if(dsConfig.speakers != null) {
                var speakerEmbedManager = getSpeakerEmbedManager();
                var spkEmbedTensor = speakerEmbedManager.PhraseSpeakerEmbedByFrame(phrase, ph_dur, frameMs, totalFrames, headFrames, tailFrames);
                varianceInputs.Add(NamedOnnxValue.CreateFromTensor("spk_embed", spkEmbedTensor));
            }
            Onnx.VerifyInputNames(varianceModel, varianceInputs);
            var varianceCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(varianceHash, varianceInputs)
                : null;
            var varianceOutputs = varianceCache?.Load();
            if (varianceOutputs is null) {
                varianceOutputs = varianceModel.Run(varianceInputs).Cast<NamedOnnxValue>().ToList();
                varianceCache?.Save(varianceOutputs);
                phrase.AddCacheFile(varianceCache?.Filename);
            }
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
            return new VarianceResult{
                energy = energy_pred?.ToArray(),
                breathiness = breathiness_pred?.ToArray(),
                voicing = voicing_pred?.ToArray(),
                tension = tension_pred?.ToArray(),
                frameMs = frameMs,
                headFrames = headFrames,
                tailFrames = tailFrames,
                totalFrames = totalFrames,
            };
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
