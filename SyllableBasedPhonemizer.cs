using System;
using System.Collections.Generic;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using System.Linq;
using System.IO;
using Serilog;
using System.Threading.Tasks;
using static OpenUtau.Api.Phonemizer;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Use this class as a base for easier phonemizer configuration. Works for vb styles like VCV, VCCV, CVC etc;
    /// 
    /// - Supports dictionary;
    /// - Automatically align phonemes to notes;
    /// - Supports syllable extension;
    /// - Automatically calculates transition phonemes length, with constants by default,
    /// but there is a pre-created function to use Oto value;
    /// - The transition length is scaled based on Tempo and note length.
    /// 
    /// Note that here "Vowel" means "stretchable phoneme" and "Consonant" means "non-stretchable phoneme".
    /// 
    /// So if a diphthong is represented with several phonemes, like English "byke" -> [b a y k], 
    /// then [a] as a stretchable phoneme would be a "Vowel", and [y] would be a "Consonant".
    /// 
    /// Some reclists have consonants that also may behave as vowels, like long "M" and "N". They are "Vowels".
    /// 
    /// If your oto hase same symbols for them, like "n" for stretchable "n" from a long note and "n" from CV,
    /// then you can use a vitrual symbol [N], and then replace it with [n] in ValidateAlias().
    /// </summary>
    public abstract class SyllableBasedPhonemizer : Phonemizer {

        /// <summary>
        /// Syllable is [V] [C..] [V]
        /// </summary>
        protected struct Syllable {
            /// <summary>
            /// vowel from previous syllable for VC
            /// </summary>
            public string prevV;
            /// <summary>
            /// CCs, may be empty
            /// </summary>
            public string[] cc;
            /// <summary>
            /// "base" note. May not actually be vowel, if only consonants way provided
            /// </summary>
            public string v;
            /// <summary>
            /// Start position for vowel. All VC CC goes before this position
            /// </summary>
            public int position;
            /// <summary>
            /// previous note duration, i.e. this is container for VC and CC notes
            /// </summary>
            public int duration;
            /// <summary>
            /// Tone for VC and CC
            /// </summary>
            public int tone;
            /// <summary>
            /// Other phoneme attributes for VC and CC
            /// </summary>
            public PhonemeAttributes[] attr;
            /// <summary>
            /// tone for base "vowel" phoneme
            /// </summary>
            public int vowelTone;
            /// <summary>
            /// Other phoneme attributes for base "vowel" phoneme
            /// </summary>
            public PhonemeAttributes[] vowelAttr;

            /// <summary>
            /// 0 if no consonants are taken from previous word;
            /// 1 means first one is taken from previous word, etc.
            /// </summary>
            public int prevWordConsonantsCount;

            /// <summary>
            /// If true, you may use alias extension instead of VV, by putting the phoneme as null if vowels match. 
            /// If you do this when canAliasBeExtended == false, the note will produce no phoneme and there will be a break.
            /// Use CanMakeAliasExtension() to pass all checks if alias extension is possible
            /// </summary>
            public bool canAliasBeExtended;

            // helpers
            public bool IsStartingV => prevV == "" && cc.Length == 0;
            public bool IsVV => prevV != "" && cc.Length == 0;

            public bool IsStartingCV => prevV == "" && cc.Length > 0;
            public bool IsVCV => prevV != "" && cc.Length > 0;

            public bool IsStartingCVWithOneConsonant => prevV == "" && cc.Length == 1;
            public bool IsVCVWithOneConsonant => prevV != "" && cc.Length == 1;

            public bool IsStartingCVWithMoreThanOneConsonant => prevV == "" && cc.Length > 1;
            public bool IsVCVWithMoreThanOneConsonant => prevV != "" && cc.Length > 1;

            public string[] PreviousWordCc => cc.Take(prevWordConsonantsCount).ToArray();
            public string[] CurrentWordCc => cc.Skip(prevWordConsonantsCount).ToArray();

            public override string ToString() {
                return $"({prevV}) {(cc != null ? string.Join(" ", cc) : "")} {v}";
            }
        }

        protected struct Ending {
            /// <summary>
            /// vowel from the last syllable to make VC
            /// </summary>
            public string prevV;
            /// <summary>
            ///  actuall CC at the ending
            /// </summary>
            public string[] cc;
            /// <summary>
            /// last note position + duration, all phonemes must be less than this
            /// </summary>
            public int position;
            /// <summary>
            /// last syllable length, max container for all VC CC C-
            /// </summary>
            public int duration;
            /// <summary>
            /// the tone from last syllable, for all ending phonemes
            /// </summary>
            public int tone;
            /// <summary>
            /// Other phoneme attributes from last syllable
            /// </summary>
            public PhonemeAttributes[] attr;

            // helpers
            public bool IsEndingV => cc.Length == 0;
            public bool IsEndingVC => cc.Length > 0;
            public bool IsEndingVCWithOneConsonant => cc.Length == 1;
            public bool IsEndingVCWithMoreThanOneConsonant => cc.Length > 1;

            public override string ToString() {
                return $"({prevV}) {(cc != null ? string.Join(" ", cc) : "")}";
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            error = "";
            var mainNote = notes[0];
            if (mainNote.lyric.StartsWith(FORCED_ALIAS_SYMBOL)) {
                return MakeForcedAliasResult(mainNote);
            }
            if (hasDictionary && isDictionaryLoading) {
                return MakeSimpleResult("");
            }

            var syllables = MakeSyllables(notes, MakeEnding(prevNeighbours));
            if (syllables == null) {
                return HandleError();
            }

            var phonemes = new List<Phoneme>();
            foreach (var syllable in syllables) {
                phonemes.AddRange(MakePhonemes(ProcessSyllable(syllable), syllable.duration, syllable.position, false));
            }
            if (!nextNeighbour.HasValue) {
                var tryEnding = MakeEnding(notes);
                if (tryEnding.HasValue) {
                    var ending = tryEnding.Value;
                    phonemes.AddRange(MakePhonemes(ProcessEnding(ending), ending.duration, ending.position, true));
                }
            }

            return new Result() {
                phonemes = AssignAllAffixes(phonemes, notes, prevNeighbours)
            };
        }

        protected virtual Phoneme[] AssignAllAffixes(List<Phoneme> phonemes, Note[] notes, Note[] prevs) {
            int noteIndex = 0;
            for (int i = 0; i < phonemes.Count; i++) {
                var attr = notes[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == i) ?? default;
                string alt = attr.alternate?.ToString() ?? string.Empty;
                string color = attr.voiceColor;
                int toneShift = attr.toneShift;
                var phoneme = phonemes[i];
                while (noteIndex < notes.Length - 1 && notes[noteIndex].position - notes[0].position < phoneme.position) {
                    noteIndex++;
                }
                var noteStartPosition = notes[noteIndex].position - notes[0].position;
                int tone = (prevs != null && prevs.Length > 0 && phoneme.position < noteStartPosition) ?
                    prevs.Last().tone : (noteIndex > 0 && phoneme.position < noteStartPosition) ?
                    notes[noteIndex - 1].tone : notes[noteIndex].tone;

                var validatedAlias = phoneme.phoneme;
                if (validatedAlias != null) {
                    validatedAlias = ValidateAliasIfNeeded(validatedAlias, tone + toneShift);
                    validatedAlias = MapPhoneme(validatedAlias, tone + toneShift, color, alt, singer);

                    phoneme.phoneme = validatedAlias;
                } else {
                    phoneme.phoneme = null;
                    phoneme.position = 0;
                }

                phonemes[i] = phoneme;
            }
            return phonemes.ToArray();
        }

        private Result HandleError() {
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = error
                    }
                }
            };
        }

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            if (!hasDictionary) {
                ReadDictionaryAndInit();
            } else {
                Init();
            }
        }

        protected USinger singer;
        protected bool hasDictionary => dictionaries.ContainsKey(GetType());
        protected IG2p dictionary => dictionaries[GetType()];
        protected bool isDictionaryLoading => dictionaries[GetType()] == null;
        protected double TransitionBasicLengthMs => 100;

        private Dictionary<Type, IG2p> dictionaries = new Dictionary<Type, IG2p>();
        private const string FORCED_ALIAS_SYMBOL = "?";
        private string error = "";
        private readonly string[] wordSeparators = new[] { " ", "_" };
        private readonly string[] wordSeparator = new[] { "  " };

        /// <summary>
        /// Returns list of vowels
        /// </summary>
        /// <returns></returns>
        protected abstract string[] GetVowels();

        /// <summary>
        /// Returns list of consonants. Only needed if there is a dictionary
        /// </summary>
        /// <returns></returns>
        protected virtual string[] GetConsonants() {
            throw new NotImplementedException();
        }

        /// <summary>
        /// returns phoneme symbols, like, VCV, or VC + CV, or -CV, etc
        /// </summary>
        /// <returns>List of phonemes</returns>
        protected abstract List<string> ProcessSyllable(Syllable syllable);

        /// <summary>
        /// phoneme symbols for ending, like, V-, or VC-, or VC+C
        /// </summary>
        protected abstract List<string> ProcessEnding(Ending ending);

        /// <summary>
        /// simple alias to alias fallback
        /// </summary>
        /// <returns></returns>
        protected virtual Dictionary<string, string> GetAliasesFallback() { return null; }

        /// <summary>
        /// Use to some custom init, if needed
        /// </summary>
        protected virtual void Init() { }

        /// <summary>
        /// Dictionary name. Must be stored in Dictionaries folder.
        /// If missing or can't be read, phonetic input is used
        /// </summary>
        /// <returns></returns>
        protected virtual string GetDictionaryName() { return null; }

        /// <summary>
        /// extracts array of phoneme symbols from note. Override for procedural dictionary or something
        /// reads from dictionary if provided
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected virtual string[] GetSymbols(Note note) {
            string[] getSymbolsRaw(string lyrics) {
                if (lyrics == null) {
                    return new string[0];
                } else return lyrics.Split(" ");
            }

            if (hasDictionary) {
                if (!string.IsNullOrEmpty(note.phoneticHint)) {
                    return getSymbolsRaw(note.phoneticHint);
                }

                var result = new List<string>();
                foreach (var subword in note.lyric.Trim().ToLowerInvariant().Split(wordSeparators, StringSplitOptions.RemoveEmptyEntries)) {
                    var subResult = dictionary.Query(subword);
                    if (subResult == null) {
                        Log.Warning($"Subword '{subword}' from word '{note.lyric}' can't be found in the dictionary");
                        subResult = HandleWordNotFound(note);
                        if (subResult == null) {
                            return null;
                        }
                    }
                    result.AddRange(subResult);
                }
                return result.ToArray();
            } else {
                return getSymbolsRaw(note.lyric);
            }
        }

        /// <summary>
        /// Instead of changing symbols in cmudict itself for each reclist, 
        /// you may leave it be and provide symbol replacements with this method.
        /// </summary>
        /// <returns></returns>
        protected virtual Dictionary<string, string> GetDictionaryPhonemesReplacement() { return null; }

        /// <summary>
        /// separates symbols to syllables, without an ending.
        /// </summary>
        /// <param name="inputNotes"></param>
        /// <param name="prevWord"></param>
        /// <returns></returns>
        protected virtual Syllable[] MakeSyllables(Note[] inputNotes, Ending? prevEnding) {
            (var symbols, var vowelIds, var notes) = GetSymbolsAndVowels(inputNotes);
            if (symbols == null || vowelIds == null || notes == null) {
                return null;
            }
            var firstVowelId = vowelIds[0];
            if (notes.Length < vowelIds.Length) {
                error = $"Not enough extension notes, {vowelIds.Length - notes.Length} more expected";
                return null;
            }

            var syllables = new Syllable[vowelIds.Length];

            // Making the first syllable
            if (prevEnding.HasValue) {
                var prevEndingValue = prevEnding.Value;
                var beginningCc = prevEndingValue.cc.ToList();
                beginningCc.AddRange(symbols.Take(firstVowelId));

                // If we had a prev neighbour ending, let's take info from it
                syllables[0] = new Syllable() {
                    prevV = prevEndingValue.prevV,
                    cc = beginningCc.ToArray(),
                    v = symbols[firstVowelId],
                    tone = prevEndingValue.tone,
                    attr = prevEndingValue.attr,
                    duration = prevEndingValue.duration,
                    position = 0,
                    vowelTone = notes[0].tone,
                    vowelAttr = notes[0].phonemeAttributes,
                    prevWordConsonantsCount = prevEndingValue.cc.Count()
                };
            } else {
                // there is only empty space before us
                syllables[0] = new Syllable() {
                    prevV = "",
                    cc = symbols.Take(firstVowelId).ToArray(),
                    v = symbols[firstVowelId],
                    tone = notes[0].tone,
                    attr = notes[0].phonemeAttributes,
                    duration = -1,
                    position = 0,
                    vowelTone = notes[0].tone,
                    vowelAttr = notes[0].phonemeAttributes
                };
            }

            // normal syllables after the first one
            var noteI = 1;
            var ccs = new List<string>();
            var position = 0;
            var lastSymbolI = firstVowelId + 1;
            for (; lastSymbolI < symbols.Length & noteI < notes.Length; lastSymbolI++) {
                if (!vowelIds.Contains(lastSymbolI)) {
                    ccs.Add(symbols[lastSymbolI]);
                } else {
                    position += notes[noteI - 1].duration;
                    syllables[noteI] = new Syllable() {
                        prevV = syllables[noteI - 1].v,
                        cc = ccs.ToArray(),
                        v = symbols[lastSymbolI],
                        tone = notes[noteI - 1].tone,
                        attr = notes[noteI - 1].phonemeAttributes,
                        duration = notes[noteI - 1].duration,
                        position = position,
                        vowelTone = notes[noteI].tone,
                        vowelAttr = notes[noteI].phonemeAttributes,
                        canAliasBeExtended = true // for all not-first notes is allowed
                    };
                    ccs = new List<string>();
                    noteI++;
                }
            }

            return syllables;
        }

        /// <summary>
        /// extracts word ending
        /// </summary>
        /// <param inputNotes="notes"></param>
        /// <returns></returns>
        protected Ending? MakeEnding(Note[] inputNotes) {
            if (inputNotes == null || inputNotes.Length == 0 || inputNotes[0].lyric.StartsWith(FORCED_ALIAS_SYMBOL)) {
                return null;
            }

            (var symbols, var vowelIds, var notes) = GetSymbolsAndVowels(inputNotes);
            if (symbols == null || vowelIds == null || notes == null) {
                return null;
            }

            return new Ending() {
                prevV = symbols[vowelIds.Last()],
                cc = symbols.Skip(vowelIds.Last() + 1).ToArray(),
                tone = notes.Last().tone,
                attr = notes.Last().phonemeAttributes,
                duration = notes.Skip(vowelIds.Length - 1).Sum(n => n.duration),
                position = notes.Sum(n => n.duration)
            };
        }

        /// <summary>
        /// extracts and validates symbols and vowels
        /// </summary>
        /// <param name="notes"></param>
        /// <returns></returns>
        private (string[], int[], Note[]) GetSymbolsAndVowels(Note[] notes) {
            var mainNote = notes[0];
            var symbols = GetSymbols(mainNote);
            if (symbols == null) {
                return (null, null, null);
            }
            if (symbols.Length == 0) {
                symbols = new string[] { "" };
            }
            symbols = ApplyExtensions(symbols, notes);
            List<int> vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            if (notes.Length < vowelIds.Count) {
                notes = HandleNotEnoughNotes(notes, vowelIds);
            }
            return (symbols, vowelIds.ToArray(), notes);
        }

        /// <summary>
        /// When there are more syllables than notes, recombines notes to match syllables count
        /// </summary>
        /// <param name="notes"></param>
        /// <param name="vowelIds"></param>
        /// <returns></returns>
        protected virtual Note[] HandleNotEnoughNotes(Note[] notes, List<int> vowelIds) {
            var newNotes = new List<Note>();
            newNotes.AddRange(notes.SkipLast(1));
            var lastNote = notes.Last();
            var position = lastNote.position;
            var notesToSplit = vowelIds.Count - newNotes.Count;
            var duration = lastNote.duration / notesToSplit / 15 * 15;
            for (var i = 0; i < notesToSplit; i++) {
                var durationFinal = i != notesToSplit - 1 ? duration : lastNote.duration - duration * (notesToSplit - 1);
                newNotes.Add(new Note() {
                    position = position,
                    duration = durationFinal,
                    tone = lastNote.tone,
                    phonemeAttributes = lastNote.phonemeAttributes
                });
                position += durationFinal;
            }

            return newNotes.ToArray();
        }

        /// <summary>
        /// Override this method, if you want to implement some machine converting from a word to phonemes
        /// </summary>
        /// <param name="word"></param>
        /// <returns></returns>
        protected virtual string[] HandleWordNotFound(Note note) {
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            string alt = attr.alternate?.ToString() ?? string.Empty;
            string color = attr.voiceColor;
            int toneShift = attr.toneShift;
            var mpdlyric = MapPhoneme(note.lyric, note.tone + toneShift, color, alt, singer);
            if(HasOto(mpdlyric, note.tone)){
                error = mpdlyric;
            }else{
                error = "word not found";
            }
            return null;
        }

        /// <summary>
        /// Does this note extend the previous syllable?
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        /// <summary>
        /// Used to extract phonemes from CMU Dict word. Override if you need some extra logic
        /// </summary>
        /// <param name="phonemesString"></param>
        /// <returns></returns>
        protected virtual string[] GetDictionaryWordPhonemes(string phonemesString) {
            return phonemesString.Split(' ');
        }

        /// <summary>
        /// use to validate alias
        /// </summary>
        /// <param name="alias"></param>
        /// <returns></returns>
        protected virtual string ValidateAlias(string alias) {
            return alias;
        }

        /// <summary>
        /// Defines basic transition length before scaling it according to tempo and note length
        /// Use GetTransitionBasicLengthMsByConstant, GetTransitionBasicLengthMsByOto or your own implementation
        /// </summary>
        /// <param name="alias">Mapped alias</param>
        /// <returns></returns>
        protected virtual double GetTransitionBasicLengthMs(string alias = "") {
            return GetTransitionBasicLengthMsByConstant();
        }

        protected double GetTransitionBasicLengthMsByConstant() {
            return TransitionBasicLengthMs * GetTempoNoteLengthFactor();
        }

        /// <summary>
        /// a note length modifier, from 1 to 0.3. Used to make transition notes shorter on high tempo
        /// </summary>
        /// <returns></returns>
        protected double GetTempoNoteLengthFactor() {
            return (300 - Math.Clamp(bpm, 90, 300)) / (300 - 90) / 3 + 0.33;
        }

        protected virtual IG2p LoadBaseDictionary() {
            var dictionaryName = GetDictionaryName();
            var filename = Path.Combine(DictionariesPath, dictionaryName);
            var dictionaryText = File.ReadAllText(filename);
            var builder = G2pDictionary.NewBuilder();
            var vowels = GetVowels();
            foreach (var vowel in vowels) {
                builder.AddSymbol(vowel, true);
            }
            var consonants = GetConsonants();
            foreach (var consonant in consonants) {
                builder.AddSymbol(consonant, false);
            }
            builder.AddEntry("a", new string[] { "a" });
            ParseDictionary(dictionaryText, builder);
            return builder.Build();
        }

        /// <summary>
        /// Parses CMU dictionary, when phonemes are separated by spaces, and word vs phonemes are separated with two spaces,
        /// and replaces phonemes with replacement table
        /// Is Running Async!
        /// </summary>
        /// <param name="dictionaryText"></param>
        /// <param name="builder"></param>
        protected virtual void ParseDictionary(string dictionaryText, G2pDictionary.Builder builder) {
            var replacements = GetDictionaryPhonemesReplacement();
            foreach (var line in dictionaryText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) {
                if (line.StartsWith(";;;")) {
                    continue;
                }
                var parts = line.Trim().Split(wordSeparator, StringSplitOptions.None);
                if (parts.Length != 2) {
                    continue;
                }
                string key = parts[0].ToLowerInvariant();
                var values = GetDictionaryWordPhonemes(parts[1]).Select(
                        n => replacements != null && replacements.ContainsKey(n) ? replacements[n] : n);
                lock (builder) {
                    builder.AddEntry(key, values);
                };
            };
        }

        #region helpers

        /// <summary>
        /// May be used if you have different logic for short and long notes
        /// </summary>
        /// <param name="syllable"></param>
        /// <returns></returns>
        protected bool IsShort(Syllable syllable) {
            return syllable.duration != -1 && TickToMs(syllable.duration) < GetTransitionBasicLengthMs() * 2;
        }
        protected bool IsShort(Ending ending) {
            return TickToMs(ending.duration) < GetTransitionBasicLengthMs() * 2;
        }

        /// <summary>
        /// Checks if mapped and validated alias exists in oto
        /// </summary>
        /// <param name="alias"></param>
        /// <param name="tone"></param>
        /// <returns></returns>
        protected bool HasOto(string alias, int tone) {
            return singer.TryGetMappedOto(alias, tone, out _);
        }

        /// <summary>
        /// Can be used for different variants, like exhales [v R], [v -] etc
        /// </summary>
        /// <param name="sourcePhonemes">phonemes container to add to</param>
        /// <param name="tone">to map alias</param>
        /// <param name="targetPhonemes">target phoneme variants</param>
        /// <returns>returns true if added any</returns>
        protected bool TryAddPhoneme(List<string> sourcePhonemes, int tone, params string[] targetPhonemes) {
            foreach (var phoneme in targetPhonemes) {
                if (HasOto(phoneme, tone)) {
                    sourcePhonemes.Add(phoneme);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// if true, you can put phoneme as null so the previous alias will be extended
        /// </summary>
        /// <param name="syllable"></param>
        /// <returns></returns>
        protected bool CanMakeAliasExtension(Syllable syllable) {
            return syllable.canAliasBeExtended && syllable.prevV == syllable.v && syllable.cc.Length == 0;
        }

        /// <summary>
        /// if current syllable is VV and previous one is from the same pitch,
        /// you may wan't to just extend the previous alias. Put the phoneme as null fot that
        /// </summary>
        /// <param name="tone1"></param>
        /// <param name="tone2"></param>
        /// <returns></returns>
        protected bool AreTonesFromTheSameSubbank(int tone1, int tone2) {
            if (singer.Subbanks.Count == 1) {
                return true;
            }
            if (tone1 == tone2) {
                return true;
            }
            var toneSets = singer.Subbanks.Select(n => n.toneSet);
            foreach (var toneSet in toneSets) {
                if (toneSet.Contains(tone1) && toneSet.Contains(tone2)) {
                    return true;
                }
                if (toneSet.Contains(tone1) != toneSet.Contains(tone2)) {
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region private

        private Result MakeForcedAliasResult(Note note) {
            return MakeSimpleResult(note.lyric.Substring(1));
        }

        protected void ReadDictionaryAndInit() {
            var dictionaryName = GetDictionaryName();
            if (dictionaryName == null) {
                return;
            }
            dictionaries[GetType()] = null;
            if (Testing) {
                ReadDictionary(dictionaryName);
                Init();
                return;
            }
            OnAsyncInitStarted();
            Task.Run(() => {
                ReadDictionary(dictionaryName);
                Init();
                OnAsyncInitFinished();
            });
        }

        private void ReadDictionary(string dictionaryName) {
            try {
                var phonemeSymbols = new Dictionary<string, bool>();
                foreach (var vowel in GetVowels()) {
                    phonemeSymbols.Add(vowel, true);
                }
                foreach (var consonant in GetConsonants()) {
                    phonemeSymbols.Add(consonant, false);
                }
                dictionaries[GetType()] = new G2pRemapper(
                    LoadBaseDictionary(),
                    phonemeSymbols,
                    GetDictionaryPhonemesReplacement());
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to read dictionary {dictionaryName}");
            }
        }

        private string[] ApplyExtensions(string[] symbols, Note[] notes) {
            var newSymbols = new List<string>();
            var vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            var lastVowelI = 0;
            newSymbols.AddRange(symbols.Take(vowelIds[lastVowelI] + 1));
            for (var i = 1; i < notes.Length && lastVowelI + 1 < vowelIds.Count; i++) {
                if (!IsSyllableVowelExtensionNote(notes[i])) {
                    var prevVowel = vowelIds[lastVowelI];
                    lastVowelI++;
                    var vowel = vowelIds[lastVowelI];
                    newSymbols.AddRange(symbols.Skip(prevVowel + 1).Take(vowel - prevVowel));
                } else {
                    newSymbols.Add(symbols[vowelIds[lastVowelI]]);
                }
            }
            newSymbols.AddRange(symbols.Skip(vowelIds[lastVowelI] + 1));
            return newSymbols.ToArray();
        }

        private List<int> ExtractVowels(string[] symbols) {
            var vowelIds = new List<int>();
            var vowels = GetVowels();
            for (var i = 0; i < symbols.Length; i++) {
                if (vowels.Contains(symbols[i])) {
                    vowelIds.Add(i);
                }
            }
            return vowelIds;
        }

        private Phoneme[] MakePhonemes(List<string> phonemeSymbols, int containerLength, int position, bool isEnding) {

            var phonemes = new Phoneme[phonemeSymbols.Count];
            for (var i = 0; i < phonemeSymbols.Count; i++) {
                var phonemeI = phonemeSymbols.Count - i - 1;

                var validatedAlias = phonemeSymbols[phonemeI];
                if (validatedAlias != null) {
                    phonemes[phonemeI].phoneme = validatedAlias;
                    var transitionLengthTick = MsToTick(GetTransitionBasicLengthMs(phonemes[phonemeI].phoneme));
                    if (i == 0) {
                        if (!isEnding) {
                            transitionLengthTick = 0;
                        } else {
                            transitionLengthTick *= 2;
                        }
                    }
                    // yet it's actually a length; will became position in ScalePhonemes
                    phonemes[phonemeI].position = transitionLengthTick;
                } else {
                    phonemes[phonemeI].phoneme = null;
                    phonemes[phonemeI].position = 0;
                }
            }

            return ScalePhonemes(phonemes, position, isEnding ? phonemeSymbols.Count : phonemeSymbols.Count - 1, containerLength);
        }

        private string ValidateAliasIfNeeded(string alias, int tone) {
            if (HasOto(alias, tone)) {
                return alias;
            }
            return ValidateAlias(alias);
        }

        private Phoneme[] ScalePhonemes(Phoneme[] phonemes, int startPosition, int phonemesCount, int containerLengthTick = -1) {
            var offset = 0;
            // reserved length for prev vowel, double length of a transition;
            var containerSafeLengthTick = MsToTick(GetTransitionBasicLengthMsByConstant() * 2);
            var lengthModifier = 1.0;
            if (containerLengthTick > 0) {
                var allTransitionsLengthTick = phonemes.Sum(n => n.position);
                if (allTransitionsLengthTick + containerSafeLengthTick > containerLengthTick) {
                    lengthModifier = (double)containerLengthTick / (allTransitionsLengthTick + containerSafeLengthTick);
                }
            }

            for (var i = phonemes.Length - 1; i >= 0; i--) {
                var finalLengthTick = (int)(phonemes[i].position * lengthModifier) / 5 * 5;
                phonemes[i].position = startPosition - finalLengthTick - offset;
                offset += finalLengthTick;
            }

            return phonemes.Where(n => n.phoneme != null).ToArray();
        }

        #endregion
    }
}
