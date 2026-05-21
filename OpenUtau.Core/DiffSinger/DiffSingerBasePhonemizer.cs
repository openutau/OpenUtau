using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using K4os.Hash.xxHash;
using Serilog;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.DiffSinger
{
    public abstract class DiffSingerBasePhonemizer : MachineLearningPhonemizer
    {
        USinger singer;
        DsConfig dsConfig;
        Dictionary<string, int>languageIds = new Dictionary<string, int>();
        string rootPath;
        float frameMs;
        ulong linguisticHash;
        ulong durationHash;
        InferenceSession linguisticModel;
        InferenceSession durationModel;
        IG2p g2p;
        Dictionary<string, int> phonemeTokens;
        DiffSingerSpeakerEmbedManager speakerEmbedManager;

        string defaultPause = "SP";
        protected virtual string GetDictionaryName()=>"dsdict.yaml";
        public virtual string GetLangCode()=>String.Empty;//The language code of the language the phonemizer is made for

        private bool _singerLoaded;

        public override void SetSinger(USinger singer) {
            if (_singerLoaded && singer == this.singer) return;
            try {
                _singerLoaded = _executeSetSinger(singer);
            } catch {
                _singerLoaded = false;
                throw;
            }
        }

        private bool _executeSetSinger(USinger singer) {
            this.singer = singer;
            if (singer == null) {
                throw new Exception("Singer is null.");
            }
            if(singer.Location == null){
                throw new Exception("Singer location is null.");
            }
            rootPath = Path.Combine(singer.Location, "dsdur");
            //Load Config
            var configPath = Path.Join(rootPath, "dsconfig.yaml");
            try {
                var configTxt = File.ReadAllText(configPath);
                dsConfig = Yaml.DefaultDeserializer.Deserialize<DsConfig>(configTxt);
            } catch(Exception e) {
                Log.Error(e, $"failed to load dsconfig from {configPath}");
                throw new Exception($"Failed to load {configPath}", e);
            }
            //Load language id if needed
            if (dsConfig.use_lang_id) {
                if (dsConfig.languages == null) {
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
            this.frameMs = dsConfig.frameMs();
            //Load g2p
            g2p = LoadG2p(rootPath, dsConfig.use_lang_id);
            //Load phonemes list
            string phonemesPath = Path.Combine(rootPath, dsConfig.phonemes);
            phonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);
            //Load models
            var linguisticModelPath = Path.Join(rootPath, dsConfig.linguistic);
            try {
                var linguisticModelBytes = File.ReadAllBytes(linguisticModelPath);
                linguisticHash = XXH64.DigestOf(linguisticModelBytes);
                linguisticModel = new InferenceSession(linguisticModelBytes);
            } catch (Exception e) {
                Log.Error(e, $"failed to load linguistic model from {linguisticModelPath}");
                throw new Exception($"Failed to load {linguisticModelPath}", e);
            }
            var durationModelPath = Path.Join(rootPath, dsConfig.dur);
            try {
                var durationModelBytes = File.ReadAllBytes(durationModelPath);
                durationHash = XXH64.DigestOf(durationModelBytes);
                durationModel = new InferenceSession(durationModelBytes);
            } catch (Exception e) {
                Log.Error(e, $"failed to load duration model from {durationModelPath}");
                throw new Exception($"Failed to load {durationModelPath}", e);
            }
            return true;
        }

        protected virtual IG2p LoadG2p(string rootPath, bool useLangId = false) {
            //Each phonemizer has a delicated dictionary name, such as dsdict-en.yaml, dsdict-ru.yaml.
            //If this dictionary exists, load it.
            //If not, load dsdict.yaml.
            var g2ps = new List<IG2p>();
            var dictionaryNames = new string[] {GetDictionaryName(), "dsdict.yaml"};
            // Load dictionary from singer folder.
            G2pDictionary.Builder g2pBuilder = new G2pDictionary.Builder();
            bool dictFound = false;
            foreach(var dictionaryName in dictionaryNames){
                string dictionaryPath = Path.Combine(rootPath, dictionaryName);
                if (File.Exists(dictionaryPath)) {
                    try {
                        g2pBuilder.Load(File.ReadAllText(dictionaryPath)).Build();
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {dictionaryPath}");
                        throw;
                    }
                    dictFound = true;
                    break;
                }
            }
            if(!dictFound){
                var triedPaths = string.Join(", ", dictionaryNames.Select(n => Path.Combine(rootPath, n)));
                throw new FileNotFoundException(
                    $"No dictionary file found. Tried: {triedPaths}");
            }
            //SP and AP should always be vowel
            g2pBuilder.AddSymbol("SP", true);
            g2pBuilder.AddSymbol("AP", true);
            g2ps.Add(g2pBuilder.Build());
            return new G2pFallbacks(g2ps.ToArray());
        }

        //Check if the phoneme is supported. If unsupported, return an empty string.
        //And apply language prefix to phoneme
        string ValidatePhoneme(string phoneme){
            if(g2p.IsValidSymbol(phoneme) && phonemeTokens.ContainsKey(phoneme)){
                return phoneme;
            }
            var langCode = GetLangCode();
            if(langCode != String.Empty){
                var phonemeWithLanguage = langCode + "/" + phoneme;
                if(g2p.IsValidSymbol(phonemeWithLanguage)  && phonemeTokens.ContainsKey(phonemeWithLanguage)){
                    return phonemeWithLanguage;
                }
            }
            return String.Empty;
        }

        string[] ParsePhoneticHint(string phoneticHint) {
            return phoneticHint.Split()
                .Select(ValidatePhoneme)
                .Where(s => !String.IsNullOrEmpty(s)) // skip invalid symbols.
                .ToArray();
        }

        string[] GetRejectedSymbols(string phoneticHint) {
            return phoneticHint.Split()
                .Where(s => String.IsNullOrEmpty(ValidatePhoneme(s)))
                .ToArray();
        }

        string[] GetSymbols(Note note, out string[] rejectedSymbols) {
            rejectedSymbols = Array.Empty<string>();
            //priority:
            //1. phonetic hint
            //2. query from g2p dictionary
            //3. treat lyric as phonetic hint, including single phoneme
            //4. empty
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                rejectedSymbols = GetRejectedSymbols(note.phoneticHint);
                return ParsePhoneticHint(note.phoneticHint);
            }
            // User has not provided hint, query g2p dictionary.
            var g2presult = g2p.Query(note.lyric)
                ?? g2p.Query(note.lyric.ToLowerInvariant());
            if(g2presult != null) {
                return g2presult;
            }
            //not found in g2p dictionary, treat lyric as phonetic hint
            rejectedSymbols = GetRejectedSymbols(note.lyric);
            var lyricSplited = ParsePhoneticHint(note.lyric);
            if (lyricSplited.Length > 0) {
                return lyricSplited;
            }
            return new string[] { };
        }

        string GetSpeakerAtIndex(Note note, int index) {
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == index) ?? default;
            var speaker = singer.Subbanks
                .FirstOrDefault(subbank => subbank.Color == attr.voiceColor && subbank.toneSet.Contains(note.tone));
            if (speaker is null) {
                //Fall back to the first subbank matching the voice color
                speaker = singer.Subbanks
                    .FirstOrDefault(subbank => subbank.Color == attr.voiceColor);
            }
            if (speaker is null) {
                //Fall back to the first defined subbank
                speaker = singer.Subbanks.FirstOrDefault();
            }
            if (speaker is null) {
                throw new Exception(
                    $"No subbanks defined for singer \"{singer.Name}\". " +
                    "Please check the singer's configuration.");
            }
            return speaker.Suffix;
        }

        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        /// <summary>
        /// distribute phonemes to each note inside the group
        /// </summary>
        List<phonemesPerNote> ProcessWord(Note[] notes, string[] symbols){
            //Check if all phonemes are defined in dsdict.yaml (for their types)
            foreach (var symbol in symbols) {
                if (!g2p.IsValidSymbol(symbol)) {
                    throw new InvalidDataException(
                        $"Type definition of symbol \"{symbol}\" not found. Consider adding it to dsdict.yaml (or dsdict-<lang>.yaml) of the phonemizer.");
                }
            }
            var wordPhonemes = new List<phonemesPerNote>{
                new phonemesPerNote(-1, notes[0].tone)
            };
            var dsPhonemes = symbols
                .Select((symbol, index) => new dsPhoneme(symbol, GetSpeakerAtIndex(notes[0], index)))
                .ToArray();
            var isVowel = dsPhonemes.Select(s => g2p.IsVowel(s.Symbol)).ToArray();
            var isGlide = dsPhonemes.Select(s => g2p.IsGlide(s.Symbol)).ToArray();
            var nonExtensionNotes = notes.Where(n=>!IsSyllableVowelExtensionNote(n)).ToArray();
            var isStart = new bool[dsPhonemes.Length];
            if(isVowel.All(b=>!b)){
                isStart[0] = true;
            }
            for(int i=0; i<dsPhonemes.Length; i++){
                if(isVowel[i]){
                    //In "Consonant-Glide-Vowel" syllable, the glide phoneme is the first phoneme in the note's timespan.
                    if(i>=2 && isGlide[i-1] && !isVowel[i-2]){
                        isStart[i-1] = true;
                    }else{
                        isStart[i] = true;
                    }
                }
            }
            //distribute phonemes to notes
            var noteIndex = 0;
            for (int i = 0; i < dsPhonemes.Length; i++) {
                if (isStart[i] && noteIndex < nonExtensionNotes.Length) {
                    var note = nonExtensionNotes[noteIndex];
                    wordPhonemes.Add(new phonemesPerNote(note.position, note.tone));
                    noteIndex++;
                }
                wordPhonemes[^1].Phonemes.Add(dsPhonemes[i]);
            }
            return wordPhonemes;
        }

        int framesBetweenTickPos(double tickPos1, double tickPos2) {
            return (int)(timeAxis.TickPosToMsPos(tickPos2)/frameMs) 
                - (int)(timeAxis.TickPosToMsPos(tickPos1)/frameMs);
        }

        public static IEnumerable<double> CumulativeSum(IEnumerable<double> sequence, double start = 0) {
            double sum = start;
            foreach (var item in sequence) {
                sum += item;
                yield return sum;
            }
        }

        public static IEnumerable<int> CumulativeSum(IEnumerable<int> sequence, int start = 0) {
            int sum = start;
            foreach (var item in sequence) {
                sum += item;
                yield return sum;
            }
        }

        public List<double> stretch(IList<double> source, double ratio, double endPos) {
            //source：音素时长序列，单位ms
            //ratio：缩放比例
            //endPos：目标终点时刻，单位ms
            //输出：缩放后的音素位置，单位ms
            double startPos = endPos - source.Sum() * ratio;
            var result = CumulativeSum(source.Select(x => x * ratio).Prepend(0), startPos).ToList();
            result.RemoveAt(result.Count - 1);
            return result;
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
                throw new Exception($"Phoneme \"{phoneme}\" isn't supported by duration model. Please check {Path.Combine(rootPath, dsConfig.phonemes)}");
            }
            return token;
        }
        
        protected override void ProcessPart(Note[][] phrase) {
            float padding = 500f;//Padding time for consonants at the beginning of a sentence, ms
            float frameMs = dsConfig.frameMs();
            var startMs = timeAxis.TickPosToMsPos(phrase[0][0].position) - padding;
            var lastNote = phrase[^1][^1];
            var endTick = lastNote.position+lastNote.duration;
            //[(Tick position of note, [phonemes])]
            //The first item of this list is for the consonants before the first note.
            var phrasePhonemes = new List<phonemesPerNote>{
                new phonemesPerNote(-1,phrase[0][0].tone, new List<dsPhoneme>{new dsPhoneme("SP", GetSpeakerAtIndex(phrase[0][0], 0))})
            };
            var notePhIndex = new List<int> { 1 };
            var wordFound = new bool[phrase.Length];
            foreach (int wordIndex in Enumerable.Range(0, phrase.Length)) {
                Note[] word = phrase[wordIndex];
                var rawSymbols = GetSymbols(word[0], out string[] rejectedSymbols);
                var symbols = rawSymbols.Where(s => phonemeTokens.ContainsKey(s)).ToArray();
                // Collect symbols that passed GetSymbols but failed phonemeTokens lookup
                var tokensRejected = rawSymbols.Where(s => !phonemeTokens.ContainsKey(s));
                rejectedSymbols = rejectedSymbols.Concat(tokensRejected).ToArray();
                if (rejectedSymbols.Length > 0) {
                    unrecognizedLyrics[word[0].position] = string.Join(" ", rejectedSymbols);
                } else if (symbols.Length == 0) {
                    unrecognizedLyrics[word[0].position] = word[0].lyric ?? string.Empty;
                }
                if (symbols.Length == 0) {
                    symbols = new string[] { defaultPause };
                    wordFound[wordIndex] = false;
                } else {
                    wordFound[wordIndex] = true;
                }
                var wordPhonemes = ProcessWord(word, symbols);
                phrasePhonemes[^1].Phonemes.AddRange(wordPhonemes[0].Phonemes);
                phrasePhonemes.AddRange(wordPhonemes.Skip(1));
                notePhIndex.Add(notePhIndex[^1]+wordPhonemes.SelectMany(n=>n.Phonemes).Count());
            }
            
            phrasePhonemes.Add(new phonemesPerNote(endTick,lastNote.tone));
            phrasePhonemes[0].Position = timeAxis.MsPosToTickPos(
                timeAxis.TickPosToMsPos(phrasePhonemes[1].Position)-padding
                );
            //Linguistic Encoder
            var tokens = phrasePhonemes
                .SelectMany(n => n.Phonemes)
                .Select(p => (Int64)PhonemeTokenize(p.Symbol))
                .ToArray();
            var word_div = phrasePhonemes.Take(phrasePhonemes.Count-1)
                .Select(n => (Int64)n.Phonemes.Count)
                .ToArray();
            //Pairwise(phrasePhonemes)
            var word_dur = phrasePhonemes
                .Zip(phrasePhonemes.Skip(1), (a, b) => (long)framesBetweenTickPos(a.Position, b.Position))
                .ToArray();
            //Call Diffsinger Linguistic Encoder model
            var linguisticInputs = new List<NamedOnnxValue>();
            linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
                new DenseTensor<Int64>(tokens, new int[] { tokens.Length }, false)
                .Reshape(new int[] { 1, tokens.Length })));
            linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("word_div",
                new DenseTensor<Int64>(word_div, new int[] { word_div.Length }, false)
                .Reshape(new int[] { 1, word_div.Length })));
            linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("word_dur",
                new DenseTensor<Int64>(word_dur, new int[] { word_dur.Length }, false)
                .Reshape(new int[] { 1, word_dur.Length })));
            //Language id
            if(dsConfig.use_lang_id){
                var langIdByPhone = phrasePhonemes
                    .SelectMany(n => n.Phonemes)
                    .Select(p => (long)languageIds.GetValueOrDefault(p.Language(), 0))
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
            }
            Tensor<float> encoder_out = linguisticOutputs
                .Where(o => o.Name == "encoder_out")
                .First()
                .AsTensor<float>();
            Tensor<bool> x_masks = linguisticOutputs
                .Where(o => o.Name == "x_masks")
                .First()
                .AsTensor<bool>();
            //Duration Predictor
            var ph_midi = phrasePhonemes
                .SelectMany(n=>Enumerable.Repeat((Int64)n.Tone, n.Phonemes.Count))
                .ToArray();
            //Call Diffsinger Duration Predictor model
            var durationInputs = new List<NamedOnnxValue>();
            durationInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_out", encoder_out));
            durationInputs.Add(NamedOnnxValue.CreateFromTensor("x_masks", x_masks));
            durationInputs.Add(NamedOnnxValue.CreateFromTensor("ph_midi",
                new DenseTensor<Int64>(ph_midi, new int[] { ph_midi.Length }, false)
                .Reshape(new int[] { 1, ph_midi.Length })));
            //Speaker
            if(dsConfig.speakers != null){
                var speakerEmbedManager = getSpeakerEmbedManager();
                var speakersByPhone =  phrasePhonemes
                    .SelectMany(n => n.Phonemes)
                    .Select(p => p.Speaker)
                    .ToArray();
                var spkEmbedTensor = speakerEmbedManager.PhraseSpeakerEmbedByPhone(speakersByPhone);
                durationInputs.Add(NamedOnnxValue.CreateFromTensor("spk_embed", spkEmbedTensor));
            }
            Onnx.VerifyInputNames(durationModel, durationInputs);
            var durationCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(durationHash, durationInputs)
                : null;
            var durationOutputs = durationCache?.Load();
            if (durationOutputs is null) {
                durationOutputs = durationModel.Run(durationInputs).Cast<NamedOnnxValue>().ToList();
                durationCache?.Save(durationOutputs);
            }
            List<double> durationFrames = durationOutputs.First().AsTensor<float>().Select(x=>(double)x).ToList();
            
            //Alignment
            //(the index of the phoneme to be aligned, the Ms position of the phoneme)
            var phAlignPoints = new List<Tuple<int, double>>();
            phAlignPoints = CumulativeSum(phrasePhonemes.Select(n => n.Phonemes.Count).ToList(), 0)
                .Zip(phrasePhonemes.Skip(1), 
                    (a, b) => new Tuple<int, double>(a, timeAxis.TickPosToMsPos(b.Position)))
                .ToList();
            var positions = new List<double>();
            List<double> alignGroup = durationFrames.GetRange(1, phAlignPoints[0].Item1 - 1);
            
            var phs = phrasePhonemes.SelectMany(n => n.Phonemes).ToList();
            //The starting consonant's duration keeps unchanged
            positions.AddRange(stretch(alignGroup, frameMs, phAlignPoints[0].Item2));
            //Stretch the duration of the rest phonemes
            foreach (var pair in phAlignPoints.Zip(phAlignPoints.Skip(1), (a, b) => Tuple.Create(a, b))) {
                var currAlignPoint = pair.Item1;
                var nextAlignPoint = pair.Item2;
                alignGroup = durationFrames.GetRange(currAlignPoint.Item1, nextAlignPoint.Item1 - currAlignPoint.Item1);
                double ratio = (nextAlignPoint.Item2 - currAlignPoint.Item2) / alignGroup.Sum();
                positions.AddRange(stretch(alignGroup, ratio, nextAlignPoint.Item2));
            }

            //Convert the position sequence to tick and fill into the result list
            int index = 1;
            foreach (int wordIndex in Enumerable.Range(0, phrase.Length)) {
                Note[] word = phrase[wordIndex];
                var noteResult = new List<Tuple<string, int>>();
                if (!wordFound[wordIndex]){
                    partResult[word[0].position] = noteResult;
                    continue;
                }
                if (word[0].lyric.StartsWith("+")) {
                    continue;
                }
                double notePos = timeAxis.TickPosToMsPos(word[0].position);//start position of the note, ms
                for (int phIndex = notePhIndex[wordIndex]; phIndex < notePhIndex[wordIndex + 1]; ++phIndex) {
                    if (!String.IsNullOrEmpty(phs[phIndex].Symbol)) {
                        noteResult.Add(Tuple.Create(phs[phIndex].Symbol, timeAxis.TicksBetweenMsPos(
                           notePos, positions[phIndex - 1])));
                    }
                }
                partResult[word[0].position] = noteResult;
            }
        }
    }

    struct dsPhoneme{
        public string Symbol;
        public string Speaker;

        public dsPhoneme(string symbol, string speaker){
            Symbol = symbol;
            Speaker = speaker;
        }

        public string Language(){
            return DiffSingerUtils.PhonemeLanguage(Symbol);
        }
    }

    class phonemesPerNote{
        public int Position;
        public int Tone;
        public List<dsPhoneme> Phonemes;

        public phonemesPerNote(int position, int tone, List<dsPhoneme> phonemes)
        {
            Position = position;
            Tone = tone;
            Phonemes = phonemes;
        }

        public phonemesPerNote(int position, int tone)
        {
            Position = position;
            Tone = tone;
            Phonemes = new List<dsPhoneme>();
        }
    }
}
