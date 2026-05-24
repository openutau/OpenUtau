using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using Microsoft.ML.OnnxRuntime;

namespace OpenUtau.Core.DiffSinger {
    class DiffSingerSinger : USinger {
        public override string Id => voicebank.Id;
        public override string Name => voicebank.Name;
        public override Dictionary<string, string> LocalizedNames => voicebank.LocalizedNames;
        public override USingerType SingerType => voicebank.SingerType;
        public override string BasePath => voicebank.BasePath;
        public override string Author => voicebank.Author;
        public override string Voice => voicebank.Voice;
        public override string Location => Path.GetDirectoryName(voicebank.File);
        public override string Web => voicebank.Web;
        public override string Version => voicebank.Version;
        public override string OtherInfo => voicebank.OtherInfo;
        public override IList<string> Errors => errors;
        public override string Avatar => voicebank.Image == null ? null : Path.Combine(Location, voicebank.Image);
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null ? null : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string Sample => voicebank.Sample == null ? null : Path.Combine(Location, voicebank.Sample);
        public override string DefaultPhonemizer => voicebank.DefaultPhonemizer;
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => subbanks;
        public override IList<UOto> Otos => otos;

        Voicebank voicebank;
        List<string> errors = new List<string>();
        Dictionary<string, string[]> table = new Dictionary<string, string[]>();
        public byte[] avatarData;
        List<USubbank> subbanks = new List<USubbank>();
        List<UOto> otos = new List<UOto>();
        Dictionary<string, UOto> otoMap = new Dictionary<string, UOto>();
        public List<string> phonemes = new List<string>();
        Dictionary<string, int> phonemeTokens;
        public Dictionary<string, int> languageIds = new Dictionary<string, int>();
        public DsConfig dsConfig;
        public ulong acousticHash;
        public InferenceSession acousticSession = null;
        public DsVocoder vocoder = null;
        public DsPitch pitchPredictor = null;
        public DiffSingerSpeakerEmbedManager speakerEmbedManager = null;
        public DsVariance variancePredictor = null;
        public bool HasPitchPredictor => File.Exists(Path.Join(Location, "dspitch", "dsconfig.yaml"));
        public bool HasVariancePredictor => File.Exists(Path.Join(Location,"dsvariance", "dsconfig.yaml"));

        public DiffSingerSinger(Voicebank voicebank) {
            this.voicebank = voicebank;
            //Load Avatar
            if (Avatar != null && File.Exists(Avatar)) {
                try {
                    using (var stream = new FileStream(Avatar, FileMode.Open, FileAccess.Read)) {
                        using (var memoryStream = new MemoryStream()) {
                            stream.CopyTo(memoryStream);
                            avatarData = memoryStream.ToArray();
                        }
                    }
                } catch (Exception e) {
                    avatarData = null;
                    Log.Error(e, "Failed to load avatar data.");
                }
            } else {
                avatarData = null;
                Log.Information("Avatar not found");
            }

            subbanks.Clear();
            subbanks.AddRange(voicebank.Subbanks
                .Select(subbank => new USubbank(subbank)));

            //Load diffsinger config of a voicebank
            string configPath = Path.Combine(Location, "dsconfig.yaml");
            bool dsConfigLoaded = false;
            if(configPath != null && File.Exists(configPath)){
                try {
                    dsConfig = Core.Yaml.DefaultDeserializer.Deserialize<DsConfig>(
                        File.ReadAllText(configPath, Encoding.UTF8));
                    dsConfigLoaded = true;
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load dsconfig.yaml for {Name} from {configPath}");
                    errors.Add($"Failed to load dsconfig.yaml: {e.Message}");
                    dsConfig = new DsConfig();
                }
            } else {
                Log.Error($"dsconfig.yaml not found for {Name} at {configPath}");
                errors.Add($"dsconfig.yaml not found at {configPath}");
                dsConfig = new DsConfig();
            }

            if(dsConfigLoaded) {
                //Load phoneme tokens for acoustic model (render-time tokenization)
                string phonemesPath = Path.Combine(Location, dsConfig.phonemes);
                if(phonemesPath != null && File.Exists(phonemesPath)){
                    try {
                        phonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);
                        phonemes = phonemeTokens.Keys.ToList();
                    } catch (Exception e){
                        Log.Error(e, $"Failed to load phoneme tokens for {Name} from {phonemesPath}");
                        errors.Add($"Failed to load phoneme tokens: {e.Message}");
                        phonemeTokens = new Dictionary<string, int>();
                    }
                } else {
                    Log.Error($"phonemes file not found for {Name} at {phonemesPath}");
                    errors.Add($"Phonemes file not found at {phonemesPath}");
                    phonemeTokens = new Dictionary<string, int>();
                }

                //Load language Id if needed
                if(dsConfig.use_lang_id){
                    if(dsConfig.languages == null){
                        Log.Error("\"languages\" field is not specified in dsconfig.yaml");
                        errors.Add("\"languages\" field is not specified in dsconfig.yaml but use_lang_id is true");
                    } else {
                        var langIdPath = Path.Join(Location, dsConfig.languages);
                        try {
                            languageIds = DiffSingerUtils.LoadLanguageIds(langIdPath);
                        } catch (Exception e) {
                            Log.Error(e, $"failed to load language id from {langIdPath}");
                            errors.Add($"Failed to load language IDs: {e.Message}");
                        }
                    }
                }
            }

            var dummyOtoSet = new UOtoSet(new OtoSet(), Location);
            foreach (var phone in phonemes) {
                var uOto = UOto.OfDummy(phone);
                if (!otoMap.ContainsKey(uOto.Alias)) {
                    otos.Add(uOto);
                    otoMap.Add(uOto.Alias, uOto);
                } else {
                    //Errors.Add($"oto conflict {Otos[oto.Alias].Set}/{oto.Alias} and {otoSet.Name}/{oto.Alias}");
                }
            }

            found = true;
            loaded = true;
        }

        public override bool TryGetOto(string phoneme, out UOto oto) {
            // We always return true here just not to let OTO get in our way.
            // Phonemizer and acoustic model work independently and both can report missing phonemes by their own,
            // so do other submodules.
            oto = UOto.OfDummy(phoneme);
            return true;
        }

        public override IEnumerable<UOto> GetSuggestions(string text) {
            if (text != null) {
                text = text.ToLowerInvariant().Replace(" ", "");
            }
            bool all = string.IsNullOrEmpty(text);
            return table.Keys
                .Where(key => all || key.Contains(text))
                .Select(key => UOto.OfDummy(key));
        }

        public override byte[] LoadPortrait() {
            return string.IsNullOrEmpty(Portrait)
                ? null
                : File.ReadAllBytes(Portrait);
        }

        public InferenceSession getAcousticSession() {
            if (acousticSession is null) {
                var acousticPath = Path.Combine(Location, dsConfig.acoustic);
                var acousticBytes = File.ReadAllBytes(acousticPath);
                acousticHash = XXH64.DigestOf(acousticBytes);
                acousticSession = Onnx.getInferenceSession(acousticBytes, OnnxRunnerChoice.Default);
            }
            return acousticSession;
        }

        public DsVocoder getVocoder() {
            if(vocoder is null) {
                if(File.Exists(Path.Join(Location, "dsvocoder", "vocoder.yaml"))) {
                    vocoder = new DsVocoder(Path.Join(Location, "dsvocoder"));
                    return vocoder;
                }
                vocoder = new DsVocoder(Path.Combine(PathManager.Inst.DependencyPath, dsConfig.vocoder));
            }
            return vocoder;
        }

        public DsPitch? getPitchPredictor(){
            if(pitchPredictor is null) {
                if(HasPitchPredictor){
                    pitchPredictor = new DsPitch(Path.Join(Location, "dspitch"));
                }
            }
            return pitchPredictor;
        }
       
        public DiffSingerSpeakerEmbedManager getSpeakerEmbedManager(){
            if(speakerEmbedManager is null) {
                speakerEmbedManager = new DiffSingerSpeakerEmbedManager(dsConfig, Location);
            }
            return speakerEmbedManager;
        }

        public DsVariance? getVariancePredictor(){
            if(variancePredictor is null) {
                if(HasVariancePredictor){
                    variancePredictor = new DsVariance(Path.Join(Location, "dsvariance"));
                }
            }
            return variancePredictor;
        }

        public int PhonemeTokenize(string phoneme){
            if(phonemeTokens == null || phonemeTokens.Count == 0){
                throw new Exception(
                    $"Phoneme vocabulary is not loaded for singer \"{Name}\". " +
                    "Please check that dsconfig.yaml and the phonemes file are valid.");
            }
            bool success = phonemeTokens.TryGetValue(phoneme, out int token);
            if(!success){
                throw new Exception($"Phoneme \"{phoneme}\" isn't supported by acoustic model. Please check {Path.Combine(Location, dsConfig.phonemes)}");
            }
            return token;
        }

        public override void FreeMemory(){
            Log.Information($"Freeing memory for singer {Id}");
            if(acousticSession != null) {
                lock(acousticSession) {
                    acousticSession?.Dispose();
                }
                acousticSession = null;
            }
            if(vocoder != null) {
                lock(vocoder) {
                    vocoder?.Dispose();
                }
                vocoder = null;
            }
            if(pitchPredictor != null) {
                lock(pitchPredictor) {
                    pitchPredictor?.Dispose();
                }
                pitchPredictor = null;
            }
            if(variancePredictor != null){
                lock(variancePredictor) {
                    variancePredictor?.Dispose();
                }
                variancePredictor = null;
            }
        }
    }
}
