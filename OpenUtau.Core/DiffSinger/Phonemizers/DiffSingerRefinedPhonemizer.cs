using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using K4os.Hash.xxHash;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    #region Data Structures

    /// <summary>
    /// Dictionary data structure for DiffSinger G2P (Grapheme-to-Phoneme) conversions
    /// with support for various replacement types (single, merge, split, many-to-many)
    /// </summary>
    class DiffSingerRefG2pDictionaryData : G2pDictionaryData {
        public struct Replacement {
            public object from { get; set; }
            public object to { get; set; }

            /// <summary>
            /// Gets the 'from' values as a list of strings
            /// </summary>
            public List<string> FromList {
                get {
                    if (from is string s) return new List<string> { s };
                    if (from is IEnumerable<object> list) return list.Select(x => x.ToString()).ToList();
                    return new List<string>();
                }
            }

            /// <summary>
            /// Gets the 'to' values as a list of strings
            /// </summary>
            public List<string> ToList {
                get {
                    if (to is string s) return new List<string> { s };
                    if (to is IEnumerable<object> list) return list.Select(x => x.ToString()).ToList();
                    return new List<string>();
                }
            }
        }

        public Replacement[]? replacements;

        /// <summary>
        /// Creates a dictionary for one-to-one replacements (single symbol mapping)
        /// </summary>
        public Dictionary<string, string> replacementsDict() {
            var dict = new Dictionary<string, string>();
            if (replacements != null) {
                foreach (var r in replacements) {
                    if (r.from is string fromStr && r.to is string toStr) {
                        dict[fromStr] = toStr;
                    }
                }
            }
            return dict;
        }

        /// <summary>
        /// Creates a dictionary for merge replacements (many-to-one mapping)
        /// </summary>
        public Dictionary<string, string> mergeDict() {
            var dict = new Dictionary<string, string>();
            if (replacements != null) {
                foreach (var r in replacements) {
                    var fromList = r.FromList;
                    var toList = r.ToList;

                    // If 'from' is an array (more than one element), add to merge dict
                    if (fromList.Count > 1 && toList.Count == 1) {
                        string fromKey = string.Join("|", fromList);
                        dict[fromKey] = r.to.ToString();
                    }
                }
            }
            return dict;
        }

        /// <summary>
        /// Creates a dictionary for split replacements (one-to-many mapping)
        /// </summary>
        public Dictionary<string, string> splitDict() {
            var dict = new Dictionary<string, string>();
            if (replacements != null) {
                foreach (var r in replacements) {
                    var fromList = r.FromList;
                    var toList = r.ToList;

                    // If 'to' is an array (more than one element), add to split dict
                    if (fromList.Count == 1 && toList.Count > 1) {
                        dict[r.from.ToString()] = string.Join("|", toList);
                    }
                }
            }
            return dict;
        }

        /// <summary>
        /// Creates a dictionary for many-to-many replacements (array-to-array mapping)
        /// </summary>
        public Dictionary<string, string> manyToManyDict() {
            var dict = new Dictionary<string, string>();
            if (replacements != null) {
                foreach (var r in replacements) {
                    var fromList = r.FromList;
                    var toList = r.ToList;

                    // If both 'from' and 'to' are arrays with more than one element
                    if (fromList.Count > 1 && toList.Count > 1) {
                        string fromKey = string.Join("|", fromList);
                        dict[fromKey] = string.Join("|", toList);
                    }
                }
            }
            return dict;
        }
    }

    /// <summary>
    /// Represents a phoneme with its symbol and speaker information
    /// Made public to allow external access to phoneme data
    /// </summary>
    public struct dsRefPhoneme {
        public string Symbol;
        public string Speaker;

        public dsRefPhoneme(string symbol, string speaker) {
            Symbol = symbol;
            Speaker = speaker;
        }

        /// <summary>
        /// Gets the language code for this phoneme
        /// </summary>
        public string Language() {
            return DiffSingerUtils.PhonemeLanguage(Symbol);
        }
    }

    /// <summary>
    /// Represents phonemes associated with a specific note position and tone
    /// Made public to allow external access to note phoneme data
    /// </summary>
    public class phPerNote {
        public int Position;
        public int Tone;
        public List<dsRefPhoneme> Phonemes;

        public phPerNote(int position, int tone, List<dsRefPhoneme> phonemes) {
            Position = position;
            Tone = tone;
            Phonemes = phonemes;
        }

        public phPerNote(int position, int tone) {
            Position = position;
            Tone = tone;
            Phonemes = new List<dsRefPhoneme>();
        }
    }

    #endregion

    #region Main Phonemizer Class

    /// <summary>
    /// Abstract base class for DiffSinger refined phonemizers
    /// Provides infrastructure for phoneme processing, timing prediction, and speech synthesis
    /// </summary>
    public abstract class DiffSingerRefinedPhonemizer : MachineLearningPhonemizer {
        #region Fields and Properties

        // Core singer and configuration
        protected USinger singer;
        protected DsConfig dsConfig;
        protected string rootPath;

        // Machine learning models and their hashes
        protected InferenceSession linguisticModel;
        protected InferenceSession durationModel;
        protected ulong linguisticHash;
        protected ulong durationHash;

        // Phoneme processing infrastructure
        protected IG2p g2p;
        protected Dictionary<string, int> phonemeTokens;
        protected Dictionary<string, int> languageIds = new Dictionary<string, int>();
        protected DiffSingerSpeakerEmbedManager speakerEmbedManager;

        // Symbol validation and replacement dictionaries
        private Dictionary<string, bool> phonemeSymbols = new Dictionary<string, bool>();
        private Dictionary<string, string> singleReplacements = new Dictionary<string, string>();
        private Dictionary<string, string> mergeReplacements = new Dictionary<string, string>();
        private Dictionary<string, string> splitReplacements = new Dictionary<string, string>();
        private Dictionary<string, string> manyToManyReplacements = new Dictionary<string, string>();

        // Configuration and timing
        protected float frameMs;
        private string defaultPause = "SP";
        private bool _singerLoaded;

        // Abstract methods for customization
        protected virtual string GetDictionaryName() => "dsdict.yaml";
        public virtual string GetLangCode() => string.Empty;
        protected virtual IG2p LoadBaseG2p() => null;
        protected virtual string[] GetBaseG2pVowels() => new string[] { };
        protected virtual string[] GetBaseG2pConsonants() => new string[] { };

        #endregion

        #region Singer Management

        /// <summary>
        /// Sets the singer and initializes all required components
        /// </summary>
        public override void SetSinger(USinger singer) {
            if (_singerLoaded && singer == this.singer) return;
            try {
                _singerLoaded = _executeSetSinger(singer);
            } catch {
                _singerLoaded = false;
                throw;
            }
        }

        /// <summary>
        /// Internal implementation of singer setup
        /// </summary>
        private bool _executeSetSinger(USinger singer) {
            this.singer = singer;
            if (singer == null) {
                return false;
            }
            if (singer.Location == null) {
                Log.Error("Singer location is null");
                return false;
            }

            // Determine root path (dsdur folder or singer root)
            rootPath = File.Exists(Path.Join(singer.Location, "dsdur", "dsconfig.yaml"))
                ? Path.Combine(singer.Location, "dsdur")
                : singer.Location;

            // Load configuration
            if (!LoadConfiguration()) return false;

            // Load language IDs if needed
            if (dsConfig.use_lang_id) {
                if (!LoadLanguageIds()) return false;
            }

            this.frameMs = dsConfig.frameMs();

            // Load remaining components
            g2p = LoadG2p(rootPath, dsConfig.use_lang_id);
            if (!LoadPhonemeTokens()) return false;
            if (!LoadModels()) return false;

            return true;
        }

        /// <summary>
        /// Loads the DiffSinger configuration from dsconfig.yaml
        /// </summary>
        private bool LoadConfiguration() {
            var configPath = Path.Join(rootPath, "dsconfig.yaml");
            try {
                var configTxt = File.ReadAllText(configPath);
                dsConfig = Yaml.DefaultDeserializer.Deserialize<DsConfig>(configTxt);
                return true;
            } catch (Exception e) {
                Log.Error(e, $"failed to load dsconfig from {configPath}");
                return false;
            }
        }

        /// <summary>
        /// Loads language ID mappings if required by the configuration
        /// </summary>
        private bool LoadLanguageIds() {
            if (dsConfig.languages == null) {
                Log.Error("\"languages\" field is not specified in dsconfig.yaml");
                return false;
            }
            var langIdPath = Path.Join(rootPath, dsConfig.languages);
            try {
                languageIds = DiffSingerUtils.LoadLanguageIds(langIdPath);
                return true;
            } catch (Exception e) {
                Log.Error(e, $"failed to load language id from {langIdPath}");
                return false;
            }
        }

        /// <summary>
        /// Loads the phoneme token mappings
        /// </summary>
        private bool LoadPhonemeTokens() {
            string phonemesPath = Path.Combine(rootPath, dsConfig.phonemes);
            try {
                phonemeTokens = DiffSingerUtils.LoadPhonemes(phonemesPath);
                return true;
            } catch (Exception e) {
                Log.Error(e, $"failed to load phonemes from {phonemesPath}");
                return false;
            }
        }

        /// <summary>
        /// Loads the linguistic and duration prediction models
        /// </summary>
        private bool LoadModels() {
            // Load linguistic model
            var linguisticModelPath = Path.Join(rootPath, dsConfig.linguistic);
            try {
                var linguisticModelBytes = File.ReadAllBytes(linguisticModelPath);
                linguisticHash = XXH64.DigestOf(linguisticModelBytes);
                linguisticModel = new InferenceSession(linguisticModelBytes);
            } catch (Exception e) {
                Log.Error(e, $"failed to load linguistic model from {linguisticModelPath}");
                return false;
            }

            // Load duration model
            var durationModelPath = Path.Join(rootPath, dsConfig.dur);
            try {
                var durationModelBytes = File.ReadAllBytes(durationModelPath);
                durationHash = XXH64.DigestOf(durationModelBytes);
                durationModel = new InferenceSession(durationModelBytes);
            } catch (Exception e) {
                Log.Error(e, $"failed to load duration model from {durationModelPath}");
                return false;
            }

            return true;
        }

        #endregion

        #region Phoneme Processing Infrastructure

        /// <summary>
        /// Loads and configures the G2P (Grapheme-to-Phoneme) system
        /// </summary>
        protected virtual IG2p LoadG2p(string rootPath, bool useLangId = false) {
            var dictionaryNames = new string[] { GetDictionaryName(), "dsdict.yaml" };
            var g2ps = new List<IG2p>();

            // Load dictionary from singer folder
            G2pDictionary.Builder g2pBuilder = G2pDictionary.NewBuilder();
            var replacements = new Dictionary<string, string>();

            foreach (var dictionaryName in dictionaryNames) {
                string dictionaryPath = Path.Combine(rootPath, dictionaryName);
                if (File.Exists(dictionaryPath)) {
                    try {
                        string dictText = File.ReadAllText(dictionaryPath);
                        var dictData = Yaml.DefaultDeserializer.Deserialize<DiffSingerRefG2pDictionaryData>(dictText);
                        g2pBuilder.Load(dictData);

                        // Load replacement dictionaries
                        replacements = dictData.replacementsDict();
                        singleReplacements = replacements;
                        mergeReplacements = dictData.mergeDict();
                        splitReplacements = dictData.splitDict();
                        manyToManyReplacements = dictData.manyToManyDict();

                        // Collect all symbols from the dictionary
                        if (dictData.symbols != null) {
                            foreach (var symbol in dictData.symbols) {
                                phonemeSymbols[symbol.symbol.Trim()] = true;
                            }
                        }
                        Log.Information("Loaded symbols: " + string.Join(", ", phonemeSymbols.Keys));
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {dictionaryPath}");
                    }
                    break;
                }
            }

            // SP and AP should always be vowels
            g2pBuilder.AddSymbol("SP", true);
            g2pBuilder.AddSymbol("AP", true);
            g2ps.Add(g2pBuilder.Build());

            // Load base G2P
            var baseG2p = LoadBaseG2p();
            if (baseG2p == null) {
                return new G2pFallbacks(g2ps.ToArray());
            }

            ConfigureBaseG2pSymbols(baseG2p);

            if (useLangId) {
                ConfigureLanguageSpecificReplacements(replacements);
            }

            // Configure symbol types based on replacements
            ConfigureSymbolTypes(replacements, baseG2p);

            g2ps.Add(new G2pRemapper(baseG2p, phonemeSymbols, replacements));
            return new G2pFallbacks(g2ps.ToArray());
        }

        /// <summary>
        /// Configures base G2P vowel and consonant symbols
        /// </summary>
        private void ConfigureBaseG2pSymbols(IG2p baseG2p) {
            foreach (var v in GetBaseG2pVowels()) {
                phonemeSymbols[v] = true;
            }
            foreach (var c in GetBaseG2pConsonants()) {
                phonemeSymbols[c] = false;
            }
        }

        /// <summary>
        /// Configures language-specific replacements for multi-language models
        /// </summary>
        private void ConfigureLanguageSpecificReplacements(Dictionary<string, string> replacements) {
            var langCode = GetLangCode();

            // Add default language prefix to G2P phonemes
            foreach (var ph in GetBaseG2pVowels().Concat(GetBaseG2pConsonants())) {
                if (!replacements.ContainsKey(ph)) {
                    replacements[ph] = langCode + "/" + ph;
                }
            }

            // Add language prefix to replacement dictionaries
            ConfigureReplacementDictionary(ref mergeReplacements, true);
            ConfigureReplacementDictionary(ref splitReplacements, false);
            ConfigureReplacementDictionary(ref manyToManyReplacements, true);
        }

        /// <summary>
        /// Configures a replacement dictionary with language prefixes
        /// </summary>
        private void ConfigureReplacementDictionary(ref Dictionary<string, string> replacements, bool arrayFrom) {
            var replacementsWithLang = new Dictionary<string, string>();
            foreach (var kvp in replacements) {
                if (arrayFrom) {
                    var fromParts = kvp.Key.Split('|');
                    var fromWithLang = fromParts.Select(part => GetLangCode() + "/" + part).ToArray();
                    replacementsWithLang[string.Join("|", fromWithLang)] = kvp.Value;
                } else {
                    replacementsWithLang[GetLangCode() + "/" + kvp.Key] = kvp.Value;
                }
            }
            replacements = replacementsWithLang;
        }

        /// <summary>
        /// Configures symbol types (vowel/consonant) based on replacement mappings
        /// </summary>
        private void ConfigureSymbolTypes(Dictionary<string, string> replacements, IG2p baseG2p) {
            foreach (var from in replacements.Keys) {
                var to = replacements[from];
                if (baseG2p.IsValidSymbol(to)) {
                    if (baseG2p.IsVowel(to)) {
                        phonemeSymbols[from] = true;
                    } else {
                        phonemeSymbols[from] = false;
                    }
                }
            }
        }

        #endregion

        #region Phoneme Validation and Processing

        /// <summary>
        /// Checks if a phoneme symbol is supported by the model
        /// </summary>
        protected bool HasPhoneme(string phoneme) {
            return phonemeSymbols.ContainsKey(phoneme);
        }

        /// <summary>
        /// Validates a phoneme and applies language prefix if needed
        /// Returns empty string if phoneme is unsupported
        /// </summary>
        string ValidatePhoneme(string phoneme) {
            if (g2p.IsValidSymbol(phoneme) && phonemeTokens.ContainsKey(phoneme)) {
                return phoneme;
            }

            var langCode = GetLangCode();
            if (langCode != string.Empty) {
                var phonemeWithLanguage = langCode + "/" + phoneme;
                if (g2p.IsValidSymbol(phonemeWithLanguage) && phonemeTokens.ContainsKey(phonemeWithLanguage)) {
                    return phonemeWithLanguage;
                }
            }

            // Special cases for note extensions and rests (no error logging needed)
            if (phoneme == "+" || phoneme == "-" || phoneme == "+*" || phoneme == "+~" || phoneme == "R") {
                return string.Empty;
            }

            Log.Error($"Phoneme \"{phoneme}\" isn't supported by the model. Skipping...");
            return string.Empty;
        }

        /// <summary>
        /// Checks if a phoneme is valid without applying language prefixes
        /// </summary>
        public bool IsValidPhoneme(string phoneme) {
            return g2p.IsValidSymbol(phoneme) && phonemeTokens.ContainsKey(phoneme);
        }

        /// <summary>
        /// Parses a phonetic hint string into validated phoneme symbols
        /// </summary>
        string[] ParsePhoneticHint(string phoneticHint) {
            return phoneticHint.Split()
                .Select(ValidatePhoneme)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }

        /// <summary>
        /// Method for single phoneme replacements (kept for compatibility with accent-changing dictionaries)
        /// </summary>
        /// <param name="phoneme">Input phoneme</param>
        /// <returns>Replaced phoneme or original if no replacement found</returns>
        protected string GetReplacement(string phoneme) {
            return singleReplacements.TryGetValue(phoneme, out var replacement) ? replacement : phoneme;
        }

        #endregion

        #region Symbol Resolution

        /// <summary>
        /// Gets phoneme symbols for a note with the following priority:
        /// 1. Phonetic hint (user-provided)
        /// 2. G2P dictionary query
        /// 3. Treat lyric as phonetic hint
        /// 4. Empty array
        /// </summary>
        string[] GetSymbols(Note note) {
            // Priority 1: Use phonetic hint if provided
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                return ParsePhoneticHint(note.phoneticHint);
            }

            // Priority 2: Query G2P dictionary
            var g2presult = g2p.Query(note.lyric)
                ?? g2p.Query(note.lyric.ToLowerInvariant());
            if (g2presult != null) {
                return g2presult;
            }

            // Priority 3: Treat lyric as phonetic hint
            var lyricSplited = ParsePhoneticHint(note.lyric);
            if (lyricSplited.Length > 0) {
                return lyricSplited;
            }

            // Priority 4: Return empty array
            return new string[] { };
        }

        /// <summary>
        /// Gets the speaker suffix for a phoneme at a specific index in a note
        /// </summary>
        string GetSpeakerAtIndex(Note note, int index) {
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == index) ?? default;
            var speaker = singer.Subbanks
                .Where(subbank => subbank.Color == attr.voiceColor && subbank.toneSet.Contains(note.tone))
                .FirstOrDefault();
            return speaker?.Suffix ?? string.Empty;
        }

        /// <summary>
        /// Checks if a note is a syllable vowel extension note (for processing)
        /// </summary>
        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        #endregion

        #region Phoneme Replacement System

        /// <summary>
        /// Applies comprehensive phoneme replacements including merge, split, and many-to-many operations
        /// Respects user phonetic hints by skipping replacements when phonetic hints are present
        /// </summary>
        protected virtual List<string> ApplyPhonemeReplacements(List<string> phonemes, int noteIndex, Note[] notes) {
            if (phonemes == null) {
                throw new ArgumentNullException(nameof(phonemes));
            }

            // Check if any note has a phonetic hint - if so, skip all replacements
            if (notes != null && notes.Any(note => !string.IsNullOrEmpty(note.phoneticHint))) {
                return new List<string>(phonemes);
            }

            // Fast path: if no replacements are configured, return a copy
            if (!mergeReplacements.Any() && !splitReplacements.Any() && !manyToManyReplacements.Any()) {
                return new List<string>(phonemes);
            }

            // Apply replacements in logical order: merge -> split -> many-to-many
            var mergedPhonemes = ApplyMergeReplacements(phonemes);
            var splitPhonemes = ApplySplitReplacements(mergedPhonemes);
            var finalPhonemes = ApplyManyToManyReplacements(splitPhonemes);

            return finalPhonemes;
        }

        /// <summary>
        /// Applies merge replacements where multiple consecutive phonemes are replaced with a single phoneme
        /// Example: [t, s] -> [ts]
        /// </summary>
        protected List<string> ApplyMergeReplacements(List<string> phonemes) {
            if (!mergeReplacements.Any()) {
                return new List<string>(phonemes);
            }

            var result = new List<string>(phonemes.Count);
            int currentIndex = 0;

            while (currentIndex < phonemes.Count) {
                bool merged = false;

                // Try each merge rule to see if it matches starting at current position
                foreach (var mergeRule in mergeReplacements) {
                    var fromPhonemes = mergeRule.Key.Split('|');

                    if (currentIndex + fromPhonemes.Length > phonemes.Count) {
                        continue;
                    }

                    if (MatchesPhonemePattern(phonemes, currentIndex, fromPhonemes)) {
                        result.Add(mergeRule.Value);
                        currentIndex += fromPhonemes.Length;
                        merged = true;
                        break;
                    }
                }

                // If no merge rule matched, add current phoneme as-is
                if (!merged) {
                    result.Add(phonemes[currentIndex]);
                    currentIndex++;
                }
            }

            return result;
        }

        /// <summary>
        /// Applies split replacements where a single phoneme is replaced with multiple phonemes
        /// Example: [kw] -> [k, w]
        /// </summary>
        protected List<string> ApplySplitReplacements(List<string> phonemes) {
            if (!splitReplacements.Any()) {
                return new List<string>(phonemes);
            }

            var result = new List<string>(Math.Max(phonemes.Count, phonemes.Count * 2));

            foreach (var phoneme in phonemes) {
                if (splitReplacements.TryGetValue(phoneme, out var splitValue)) {
                    var splitPhonemes = splitValue.Split('|');
                    result.AddRange(splitPhonemes);
                } else {
                    result.Add(phoneme);
                }
            }

            return result;
        }

        /// <summary>
        /// Applies many-to-many replacements with one-to-one array mappings
        /// Example: [a, b, c] -> [x, y]
        /// </summary>
        protected List<string> ApplyManyToManyReplacements(List<string> phonemes) {
            if (!manyToManyReplacements.Any()) {
                return new List<string>(phonemes);
            }

            var result = new List<string>(phonemes.Count);
            int currentIndex = 0;

            while (currentIndex < phonemes.Count) {
                bool replaced = false;

                foreach (var replacementRule in manyToManyReplacements) {
                    var fromPhonemes = replacementRule.Key.Split('|');

                    if (currentIndex + fromPhonemes.Length > phonemes.Count) {
                        continue;
                    }

                    if (MatchesPhonemePattern(phonemes, currentIndex, fromPhonemes)) {
                        var toPhonemes = replacementRule.Value.Split('|');
                        result.AddRange(toPhonemes);
                        currentIndex += fromPhonemes.Length;
                        replaced = true;
                        break;
                    }
                }

                if (!replaced) {
                    result.Add(phonemes[currentIndex]);
                    currentIndex++;
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if phonemes at the specified position match the given pattern
        /// </summary>
        protected bool MatchesPhonemePattern(List<string> phonemes, int startIndex, string[] pattern) {
            for (int i = 0; i < pattern.Length; i++) {
                if (!string.Equals(phonemes[startIndex + i], pattern[i], StringComparison.Ordinal)) {
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region Word-Level Phoneme Editing

        /// <summary>
        /// Edit phonemes for a complete word, allowing access to all word phonemes at once
        /// Can be overridden to implement cross-phoneme modifications across the entire word
        /// </summary>
        protected virtual List<phPerNote> EditPhonemesForWord(
            List<phPerNote> wordPhonemes,
            Note[] wordNotes,
            List<phPerNote>? previousWordPhonemes,
            List<phPerNote>? nextWordPhonemes,
            Note[]? previousWordNotes,
            Note[]? nextWordNotes) {
            return wordPhonemes;
        }

        #endregion

        #region Timing-Based Phoneme Processing

        /// <summary>
        /// Data structure for phoneme information during timing operations
        /// </summary>
        private struct PhonemeInfo {
            public readonly string Symbol;
            public readonly int StartTime;

            public PhonemeInfo(string symbol, int startTime) {
                Symbol = symbol;
                StartTime = startTime;
            }
        }

        /// <summary>
        /// Data structure for phoneme timing information
        /// </summary>
        private struct PhonemeTiming {
            public readonly double StartTime;
            public readonly double EndTime;
            public readonly double DurationMs;

            public PhonemeTiming(double startTime, double endTime) {
                StartTime = startTime;
                EndTime = endTime;
                DurationMs = endTime - startTime;
            }
        }

        /// <summary>
        /// Edit phonemes based on their timing information, allowing for duration-dependent modifications
        /// Can be overridden to implement duration-based phoneme transformations
        /// </summary>
        protected virtual List<Tuple<string, int>> EditTimedPhonemes(
            List<Tuple<string, int>> phonemes,
            Note currentNote,
            int currentNoteDur,
            Note[] nextNote,
            int? nextFirstPhonemeDur) {

            // Input validation
            if (phonemes == null) {
                throw new ArgumentNullException(nameof(phonemes));
            }

            if (currentNoteDur <= 0) {
                Log.Warning($"Invalid note duration ({currentNoteDur}ms) for note at position {currentNote.position}. Using default behavior.");
                return phonemes.ToList();
            }

            if (phonemes.Count == 0) {
                return new List<Tuple<string, int>>();
            }

            var result = new List<Tuple<string, int>>(phonemes.Count);

            try {
                var phonemeData = ExtractPhonemeData(phonemes);

                // Calculate timing with consideration for next note
                List<PhonemeTiming> timingData;
                if (nextNote != null && nextNote.Length > 0 && nextFirstPhonemeDur.HasValue) {
                    // Calculate timing considering next note's first phoneme duration
                    timingData = CalculatePhonemeTimingWithNextNote(phonemeData, currentNoteDur, nextNote[0], nextFirstPhonemeDur.Value);
                } else {
                    // Use original timing calculation if no next note
                    timingData = CalculatePhonemeTiming(phonemeData, currentNoteDur);
                }

                for (int i = 0; i < phonemeData.Count; i++) {
                    var phonemeInfo = phonemeData[i];
                    var timingInfo = timingData[i];

                    if (timingInfo.DurationMs <= 0) {
                        Log.Debug($"Skipping phoneme '{phonemeInfo.Symbol}' with invalid duration {timingInfo.DurationMs}ms");
                        result.Add(Tuple.Create(phonemeInfo.Symbol, phonemeInfo.StartTime));
                        continue;
                    }

                    // Apply duration-based replacements if the method is overridden
                    var transformedPhoneme = ApplyDurationBasedReplacements(phonemeInfo.Symbol, timingInfo.DurationMs, currentNote);
                    result.Add(Tuple.Create(transformedPhoneme, phonemeInfo.StartTime));
                }
            } catch (Exception ex) {
                Log.Error(ex, "Error occurred during phoneme timing edits with next note. Returning original phonemes.");
                return phonemes.ToList();
            }

            return result;
        }

        /// <summary>
        /// Calculates timing information for each phoneme with consideration for next note onset
        /// </summary>
        private List<PhonemeTiming> CalculatePhonemeTimingWithNextNote(
            List<PhonemeInfo> phonemeData,
            int currentNoteDur,
            Note nextNote,
            int nextFirstPhonemeDur) {

            var timingData = new List<PhonemeTiming>(phonemeData.Count);

            for (int i = 0; i < phonemeData.Count; i++) {
                double startTime = timeAxis.TickPosToMsPos(phonemeData[i].StartTime);
                double endTime;

                if (i + 1 < phonemeData.Count) {
                    endTime = timeAxis.TickPosToMsPos(phonemeData[i + 1].StartTime);
                } else {
                    endTime = currentNoteDur + nextFirstPhonemeDur;
                }

                timingData.Add(new PhonemeTiming(startTime, endTime));
            }

            return timingData;
        }

        /// <summary>
        /// Extracts phoneme data from tuple list for efficient processing
        /// </summary>
        private static List<PhonemeInfo> ExtractPhonemeData(List<Tuple<string, int>> phonemes) {
            var phonemeData = new List<PhonemeInfo>(phonemes.Count);

            foreach (var phoneme in phonemes) {
                phonemeData.Add(new PhonemeInfo(phoneme.Item1, phoneme.Item2));
            }

            return phonemeData;
        }

        /// <summary>
        /// Calculates timing information for each phoneme including start, end, and duration
        /// </summary>
        private List<PhonemeTiming> CalculatePhonemeTiming(List<PhonemeInfo> phonemeData, int noteDur) {
            var timingData = new List<PhonemeTiming>(phonemeData.Count);

            for (int i = 0; i < phonemeData.Count; i++) {
                double startTime = timeAxis.TickPosToMsPos(phonemeData[i].StartTime);
                double endTime;

                if (i + 1 < phonemeData.Count) {
                    endTime = timeAxis.TickPosToMsPos(phonemeData[i + 1].StartTime);
                } else {
                    endTime = noteDur;
                }

                timingData.Add(new PhonemeTiming(startTime, endTime));
            }

            return timingData;
        }

        /// <summary>
        /// Applies duration-based replacements to a phoneme based on its duration
        /// Override this method in your phonemizer to implement duration-based phoneme replacements
        /// </summary>
        protected virtual string ApplyDurationBasedReplacements(string phoneme, double durationMs, Note note) {
            return phoneme;
        }

        #endregion

        #region Phoneme Access Helpers

        /// <summary>
        /// Gets the last phoneme of a note for cross-note processing
        /// </summary>
        public dsRefPhoneme? GetLastPhonemeOfNote(Note note) {
            var symbols = GetSymbols(note);
            if (symbols.Length > 0) {
                return new dsRefPhoneme(symbols[^1], GetSpeakerAtIndex(note, symbols.Length - 1));
            }
            return null;
        }

        /// <summary>
        /// Gets the first phoneme of a note for cross-note processing
        /// </summary>
        public dsRefPhoneme? GetFirstPhonemeOfNote(Note note) {
            var symbols = GetSymbols(note);
            if (symbols.Length > 0) {
                return new dsRefPhoneme(symbols[0], GetSpeakerAtIndex(note, 0));
            }
            return null;
        }

        #endregion

        #region Word Processing

        /// <summary>
        /// Processes a word by distributing phonemes across its notes
        /// </summary>
        List<phPerNote> ProcessWord(Note[] notes, string[] symbols) {
            // Apply phoneme replacements and transformations
            var processedSymbols = ApplyPhonemeReplacements(symbols.ToList(), 0, notes).ToArray();

            // Validate all phonemes are defined in the dictionary
            foreach (var symbol in processedSymbols) {
                if (!g2p.IsValidSymbol(symbol)) {
                    throw new InvalidDataException(
                        $"Type definition of symbol \"{symbol}\" not found. Consider adding it to {GetDictionaryName()} of the phonemizer.");
                }
            }

            // Initialize word phonemes with initial position and tone
            var wordPhonemes = new List<phPerNote>{
                new phPerNote(-1, notes[0].tone)
            };

            // Create DiffSinger phonemes with speaker information
            var dsPhonemes = processedSymbols
                .Select((symbol, index) => new dsRefPhoneme(symbol, GetSpeakerAtIndex(notes[0], index)))
                .ToArray();

            // Calculate phoneme properties
            var isVowel = dsPhonemes.Select(s => g2p.IsVowel(s.Symbol)).ToArray();
            var isGlide = dsPhonemes.Select(s => g2p.IsGlide(s.Symbol)).ToArray();
            var nonExtensionNotes = notes.Where(n => !IsSyllableVowelExtensionNote(n)).ToArray();
            var isStart = new bool[dsPhonemes.Length];

            // Determine start positions for phonemes
            if (isVowel.All(b => !b)) {
                isStart[0] = true;
            }

            for (int i = 0; i < dsPhonemes.Length; i++) {
                if (isVowel[i]) {
                    // In "Consonant-Glide-Vowel" syllable, the glide phoneme is the first phoneme in the note's timespan
                    if (i >= 2 && isGlide[i - 1] && !isVowel[i - 2]) {
                        isStart[i - 1] = true;
                    } else {
                        isStart[i] = true;
                    }
                }
            }

            // Distribute phonemes to notes
            var noteIndex = 0;
            for (int i = 0; i < dsPhonemes.Length; i++) {
                if (isStart[i] && noteIndex < nonExtensionNotes.Length) {
                    var note = nonExtensionNotes[noteIndex];
                    wordPhonemes.Add(new phPerNote(note.position, note.tone));
                    noteIndex++;
                }
                wordPhonemes[^1].Phonemes.Add(dsPhonemes[i]);
            }

            return wordPhonemes;
        }

        #endregion

        #region Timing and Duration Utilities

        /// <summary>
        /// Calculates the number of frames between two tick positions
        /// </summary>
        int framesBetweenTickPos(double tickPos1, double tickPos2) {
            return (int)(timeAxis.TickPosToMsPos(tickPos2) / frameMs)
                - (int)(timeAxis.TickPosToMsPos(tickPos1) / frameMs);
        }

        /// <summary>
        /// Calculates the cumulative sum of a double sequence
        /// </summary>
        public static IEnumerable<double> CumulativeSum(IEnumerable<double> sequence, double start = 0) {
            double sum = start;
            foreach (var item in sequence) {
                sum += item;
                yield return sum;
            }
        }

        /// <summary>
        /// Calculates the cumulative sum of an integer sequence
        /// </summary>
        public static IEnumerable<int> CumulativeSum(IEnumerable<int> sequence, int start = 0) {
            int sum = start;
            foreach (var item in sequence) {
                sum += item;
                yield return sum;
            }
        }

        /// <summary>
        /// Stretches a sequence of phoneme durations to fit a target end position
        /// </summary>
        public List<double> stretch(IList<double> source, double ratio, double endPos) {
            // source: phoneme duration sequence in ms
            // ratio: scaling factor
            // endPos: target end position in ms
            // output: scaled phoneme positions in ms
            double startPos = endPos - source.Sum() * ratio;
            var result = CumulativeSum(source.Select(x => x * ratio).Prepend(0), startPos).ToList();
            result.RemoveAt(result.Count - 1);
            return result;
        }

        /// <summary>
        /// Gets the speaker embedding manager for the current singer
        /// </summary>
        public DiffSingerSpeakerEmbedManager getSpeakerEmbedManager() {
            if (speakerEmbedManager is null) {
                speakerEmbedManager = new DiffSingerSpeakerEmbedManager(dsConfig, rootPath);
            }
            return speakerEmbedManager;
        }

        /// <summary>
        /// Converts a phoneme symbol to its token index
        /// </summary>
        int PhonemeTokenize(string phoneme) {
            bool success = phonemeTokens.TryGetValue(phoneme, out int token);
            if (!success) {
                throw new Exception($"Phoneme \"{phoneme}\" isn't supported by timing model. Please check {Path.Combine(rootPath, dsConfig.phonemes)}");
            }
            return token;
        }

        #endregion

        #region Main Processing Pipeline

        /// <summary>
        /// Main processing method that handles phrase-level phonemization
        /// </summary>
        protected override void ProcessPart(Note[][] phrase) {
            float padding = 500f; // Padding time for consonants at the beginning of a sentence, ms
            float frameMs = dsConfig.frameMs();

            var startMs = timeAxis.TickPosToMsPos(phrase[0][0].position) - padding;
            var lastNote = phrase[^1][^1];
            var endTick = lastNote.position + lastNote.duration;

            // Initialize phrase phonemes with initial silence
            var phrasePhonemes = new List<phPerNote>{
                new phPerNote(-1, phrase[0][0].tone, new List<dsRefPhoneme>{ new dsRefPhoneme("SP", GetSpeakerAtIndex(phrase[0][0], 0)) })
            };

            var notePhIndex = new List<int> { 1 };
            var wordFound = new bool[phrase.Length];

            // Store processed phonemes for each word to provide context to neighboring words
            var processedWordPhonemesList = new List<List<phPerNote>>();

            // First pass: Process each word individually
            foreach (int wordIndex in Enumerable.Range(0, phrase.Length)) {
                Note[] word = phrase[wordIndex];
                var symbols = GetSymbols(word[0]).ToArray();

                if (symbols == null || symbols.Length == 0) {
                    symbols = new string[] { defaultPause };
                    wordFound[wordIndex] = false;
                } else {
                    wordFound[wordIndex] = true;
                }

                var wordPhonemes = ProcessWord(word, symbols);
                processedWordPhonemesList.Add(wordPhonemes);
            }

            // Second pass: Edit phonemes with access to processed phonemes from neighboring words
            foreach (int wordIndex in Enumerable.Range(0, phrase.Length)) {
                Note[] word = phrase[wordIndex];

                // Prepare context notes for EditPhonemesForWord
                Note[]? previousWordNotes = (wordIndex > 0 && phrase[wordIndex - 1].Length > 0) ? phrase[wordIndex - 1] : null;
                Note[]? nextWordNotes = (wordIndex < phrase.Length - 1 && phrase[wordIndex + 1].Length > 0) ? phrase[wordIndex + 1] : null;

                // Get processed phonemes from neighboring words
                List<phPerNote>? previousWordPhonemes = (wordIndex > 0 && wordFound[wordIndex - 1]) ? processedWordPhonemesList[wordIndex - 1] : null;
                List<phPerNote>? nextWordPhonemes = (wordIndex < phrase.Length - 1 && wordFound[wordIndex + 1]) ? processedWordPhonemesList[wordIndex + 1] : null;

                var wordPhonemes = processedWordPhonemesList[wordIndex];

                // If word not found, skip editing and just append phonemes
                if (!wordFound[wordIndex]) {
                    wordPhonemes = processedWordPhonemesList[wordIndex];
                    phrasePhonemes[^1].Phonemes.AddRange(wordPhonemes[0].Phonemes);
                    phrasePhonemes.AddRange(wordPhonemes.Skip(1));
                    notePhIndex.Add(notePhIndex[^1] + wordPhonemes.SelectMany(n => n.Phonemes).Count());
                    continue;
                }

                // Edit phonemes for the entire word, giving access to all word phonemes and neighboring word phonemes
                wordPhonemes = EditPhonemesForWord(wordPhonemes, word, previousWordPhonemes, nextWordPhonemes, previousWordNotes, nextWordNotes);

                phrasePhonemes[^1].Phonemes.AddRange(wordPhonemes[0].Phonemes);
                phrasePhonemes.AddRange(wordPhonemes.Skip(1));
                notePhIndex.Add(notePhIndex[^1] + wordPhonemes.SelectMany(n => n.Phonemes).Count());
            }

            // Set final phrase position
            phrasePhonemes.Add(new phPerNote(endTick, lastNote.tone));
            phrasePhonemes[0].Position = timeAxis.MsPosToTickPos(
                timeAxis.TickPosToMsPos(phrasePhonemes[1].Position) - padding
            );

            // Linguistic Encoder Processing
            var tokens = phrasePhonemes
                .SelectMany(n => n.Phonemes)
                .Select(p => (Int64)PhonemeTokenize(p.Symbol))
                .ToArray();

            var word_div = phrasePhonemes.Take(phrasePhonemes.Count - 1)
                .Select(n => (Int64)n.Phonemes.Count)
                .ToArray();

            var word_dur = phrasePhonemes
                .Zip(phrasePhonemes.Skip(1), (a, b) => (long)framesBetweenTickPos(a.Position, b.Position))
                .ToArray();

            // Prepare linguistic model inputs
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

            // Add language ID if required
            if (dsConfig.use_lang_id) {
                var langIdByPhone = phrasePhonemes
                    .SelectMany(n => n.Phonemes)
                    .Select(p => (long)languageIds.GetValueOrDefault(p.Language(), 0))
                    .ToArray();
                var langIdTensor = new DenseTensor<Int64>(langIdByPhone, new int[] { langIdByPhone.Length }, false)
                    .Reshape(new int[] { 1, langIdByPhone.Length });
                linguisticInputs.Add(NamedOnnxValue.CreateFromTensor("languages", langIdTensor));
            }

            // Run linguistic model
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

            // Duration Predictor Processing
            var ph_midi = phrasePhonemes
                .SelectMany(n => Enumerable.Repeat((Int64)n.Tone, n.Phonemes.Count))
                .ToArray();

            var durationInputs = new List<NamedOnnxValue>();
            durationInputs.Add(NamedOnnxValue.CreateFromTensor("encoder_out", encoder_out));
            durationInputs.Add(NamedOnnxValue.CreateFromTensor("x_masks", x_masks));
            durationInputs.Add(NamedOnnxValue.CreateFromTensor("ph_midi",
                new DenseTensor<Int64>(ph_midi, new int[] { ph_midi.Length }, false)
                .Reshape(new int[] { 1, ph_midi.Length })));

            // Add speaker embeddings if available
            if (dsConfig.speakers != null) {
                var speakerEmbedManager = getSpeakerEmbedManager();
                var speakersByPhone = phrasePhonemes
                    .SelectMany(n => n.Phonemes)
                    .Select(p => p.Speaker)
                    .ToArray();
                var spkEmbedTensor = speakerEmbedManager.PhraseSpeakerEmbedByPhone(speakersByPhone);
                durationInputs.Add(NamedOnnxValue.CreateFromTensor("spk_embed", spkEmbedTensor));
            }

            // Run duration model
            Onnx.VerifyInputNames(durationModel, durationInputs);
            var durationCache = Preferences.Default.DiffSingerTensorCache
                ? new DiffSingerCache(durationHash, durationInputs)
                : null;
            var durationOutputs = durationCache?.Load();
            if (durationOutputs is null) {
                durationOutputs = durationModel.Run(durationInputs).Cast<NamedOnnxValue>().ToList();
                durationCache?.Save(durationOutputs);
            }

            List<double> durationFrames = durationOutputs.First().AsTensor<float>().Select(x => (double)x).ToList();

            // Alignment Processing
            var phAlignPoints = CumulativeSum(phrasePhonemes.Select(n => n.Phonemes.Count).ToList(), 0)
                .Zip(phrasePhonemes.Skip(1),
                    (a, b) => new Tuple<int, double>(a, timeAxis.TickPosToMsPos(b.Position)))
                .ToList();

            var positions = new List<double>();
            List<double> alignGroup = durationFrames.GetRange(1, phAlignPoints[0].Item1 - 1);
            var phs = phrasePhonemes.SelectMany(n => n.Phonemes).ToList();

            // The starting consonant's duration keeps unchanged
            positions.AddRange(stretch(alignGroup, frameMs, phAlignPoints[0].Item2));

            // Stretch the duration of the rest phonemes
            foreach (var pair in phAlignPoints.Zip(phAlignPoints.Skip(1), (a, b) => Tuple.Create(a, b))) {
                var currAlignPoint = pair.Item1;
                var nextAlignPoint = pair.Item2;
                alignGroup = durationFrames.GetRange(currAlignPoint.Item1, nextAlignPoint.Item1 - currAlignPoint.Item1);
                double ratio = (nextAlignPoint.Item2 - currAlignPoint.Item2) / alignGroup.Sum();
                positions.AddRange(stretch(alignGroup, ratio, nextAlignPoint.Item2));
            }

            // Convert positions to tick format and fill result list
            int index = 1;
            var tempResults = new Dictionary<int, List<Tuple<string, int>>>();

            // First pass: Process phonemes without timing edits to collect all results
            foreach (int wordIndex in Enumerable.Range(0, phrase.Length)) {
                Note[] word = phrase[wordIndex];
                var noteResult = new List<Tuple<string, int>>();

                if (!wordFound[wordIndex]) {
                    continue;
                }

                if (word[0].lyric.StartsWith("+")) {
                    continue;
                }

                double notePos = timeAxis.TickPosToMsPos(word[0].position);
                for (int phIndex = notePhIndex[wordIndex]; phIndex < notePhIndex[wordIndex + 1]; ++phIndex) {
                    if (!string.IsNullOrEmpty(phs[phIndex].Symbol)) {
                        noteResult.Add(Tuple.Create(phs[phIndex].Symbol, timeAxis.TicksBetweenMsPos(
                           notePos, positions[phIndex - 1])));
                    }
                }

                // Store temporary result without timing edits
                if (noteResult.Count > 0) {
                    tempResults[word[0].position] = noteResult;
                }
            }

            // Second pass: Apply timing edits with access to neighboring note information
            foreach (int wordIndex in Enumerable.Range(0, phrase.Length)) {
                Note[] word = phrase[wordIndex];

                if (!wordFound[wordIndex] || word[0].lyric.StartsWith("+")) {
                    continue;
                }

                if (!tempResults.ContainsKey(word[0].position)) {
                    continue;
                }

                var noteResult = tempResults[word[0].position];
                var wordDur = (int)timeAxis.MsBetweenTickPos(word[0].position, word[0].position + word.Sum(n => n.duration));

                // Get next note information if available
                Note[] nextWord = (wordIndex + 1 < phrase.Length) ? phrase[wordIndex + 1] : null;

                // Get first phoneme duration from next note if available
                int? nextFirstPhonemeDur = null;
                if (nextWord != null && nextWord.Length > 0 && tempResults.ContainsKey(nextWord[0].position)) {
                    var nextNoteResult = tempResults[nextWord[0].position];
                    if (nextNoteResult.Count > 0) {
                        nextFirstPhonemeDur = (int)timeAxis.MsBetweenTickPos(nextWord[0].position, nextWord[0].position + nextNoteResult[0].Item2);
                    }
                }

                // Apply timing edits with access to both current and next note
                noteResult = EditTimedPhonemes(noteResult, word[0], wordDur, nextWord, nextFirstPhonemeDur);

                partResult[word[0].position] = noteResult;
            }
        }

        #endregion
    }

    #endregion
}
