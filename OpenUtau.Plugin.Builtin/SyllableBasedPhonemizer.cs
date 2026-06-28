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
using System.Collections;

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
            /// The exact lyric/symbol of the tail (e.g., "R", "br", "-", etc.)
            /// </summary>
            public string tail;
            public bool HasTail => !string.IsNullOrEmpty(tail);
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
                var modifiedSyllable = ApplyBoundaryReplacements(syllable);
                
                if (tails.Contains(modifiedSyllable.v)) {
                    var ending = new Ending {
                        prevV = modifiedSyllable.prevV,
                        cc = modifiedSyllable.cc,
                        tail = modifiedSyllable.v,
                        position = modifiedSyllable.position,
                        duration = modifiedSyllable.duration,
                        tone = modifiedSyllable.tone,
                        attr = modifiedSyllable.attr
                    };
                    
                    var endingPhonemes = ProcessEnding(ending);
                    
                    if (endingPhonemes != null) {
                        phonemes.AddRange(MakePhonemes(endingPhonemes, modifiedSyllable.duration, modifiedSyllable.position, false));
                    }
                    continue; 
                }
                phonemes.AddRange(MakePhonemes(ProcessSyllable(modifiedSyllable), modifiedSyllable.duration, modifiedSyllable.position, false));
            }

            if (!nextNeighbour.HasValue) {
                var tryEnding = MakeEnding(notes);
                if (tryEnding.HasValue) {
                    var ending = tryEnding.Value;

                    if (nextNeighbour.HasValue && tails.Contains(nextNeighbour.Value.lyric)) {
                        ending.tail = nextNeighbour.Value.lyric;
                    }
                    
                    var modifiedEnding = ApplyBoundaryReplacements(ending);
                    var endingPhonemes = ProcessEnding(modifiedEnding);

                    if (endingPhonemes != null) {
                        phonemes.AddRange(MakePhonemes(endingPhonemes, modifiedEnding.duration, modifiedEnding.position, true));
                    }
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
            if (this.singer != singer) {
                this.singer = singer;

                if (this.singer == null || !this.singer.Loaded) {
                    return;
                }

                if (string.IsNullOrEmpty(YamlFileName)) {
                    if (backupVowels != null) this.vowels = backupVowels;
                    else this.vowels = GetVowels();

                    if (backupConsonants != null) this.consonants = backupConsonants;
                    else this.consonants = GetConsonants();
                    
                    if (backupDictionaryReplacements != null) {
                        dictionaryReplacements.Clear();
                        foreach (var kvp in backupDictionaryReplacements) {
                            dictionaryReplacements[kvp.Key] = kvp.Value;
                        }
                    }
                    if (!hasDictionary) {
                        ReadDictionaryAndInit();
                    } else {
                        Init();
                    }
                    return; 
                }

                string file = null;
                if (singer != null && singer.Found && singer.Loaded && !string.IsNullOrEmpty(singer.Location)) {
                    file = Path.Combine(singer.Location, YamlFileName);
                } else if (!string.IsNullOrEmpty(PluginDir)) {
                    file = Path.Combine(PluginDir, YamlFileName);
                }

                if (!string.IsNullOrEmpty(file)) {
                    bool shouldWriteTemplate = false;
                    bool shouldBackupOldFile = false;

                    if (File.Exists(file)) {
                        if (YamlTemplate != null && !string.IsNullOrEmpty(YamlVersion)) {
                            try {
                                var checkData = Core.Yaml.DefaultDeserializer.Deserialize<YAMLData>(File.ReadAllText(file));
                                string currentVersion = checkData?.version?.Trim() ?? "";

                                if (string.IsNullOrEmpty(currentVersion) || currentVersion != YamlVersion) {
                                    shouldWriteTemplate = true;
                                    shouldBackupOldFile = true;
                                }
                            } catch (Exception ex) {
                                Log.Error(ex, $"Failed to read version from '{file}'. Backing up and resetting to template...");
                                shouldWriteTemplate = true;
                                shouldBackupOldFile = true;
                            }
                        }
                    } else if (YamlTemplate != null) {
                        shouldWriteTemplate = true;
                    }

                    if (shouldBackupOldFile && File.Exists(file)) {
                        try {
                            string backupFile = Path.Combine(Path.GetDirectoryName(file), $"{Path.GetFileNameWithoutExtension(YamlFileName)}_backup{Path.GetExtension(YamlFileName)}");
                            if (File.Exists(backupFile)) File.Delete(backupFile);
                            File.Move(file, backupFile);
                            Log.Information($"Old {YamlFileName} backed up to {backupFile}");
                        } catch (Exception e) {
                            Log.Error(e, $"Failed to back up {YamlFileName}");
                        }
                    }

                    if (shouldWriteTemplate) {
                        try {
                            File.WriteAllBytes(file, YamlTemplate);
                            Log.Information($"'{file}' created or updated to version {YamlVersion ?? "default"}");
                        } catch (Exception e) {
                            Log.Error(e, $"Failed to write template to {file}");
                        }
                    }

                    if (File.Exists(file)) {
                        try {
                            var data = Core.Yaml.DefaultDeserializer.Deserialize<YAMLData>(File.ReadAllText(file));
                            
                            if (backupVowels == null) backupVowels = GetVowels() ?? Array.Empty<string>();
                            if (backupConsonants == null) backupConsonants = GetConsonants() ?? Array.Empty<string>();

                            var yamlVowels = data.symbols?.Where(s => s.type == "vowel" || s.type == "diphthong").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                            vowels = backupVowels.Concat(yamlVowels).Distinct().ToArray();

                            tails = (tails ?? Array.Empty<string>()).Concat(data.symbols?.Where(s => s.type == "tail").Select(s => s.symbol) ?? Array.Empty<string>()).Distinct().ToArray();
                            
                            fricative = data.symbols?.Where(s => s.type == "fricative").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                            aspirate = data.symbols?.Where(s => s.type == "aspirate").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                            semivowel = data.symbols?.Where(s => s.type == "semivowel").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                            liquid = data.symbols?.Where(s => s.type == "liquid").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                            nasal = data.symbols?.Where(s => s.type == "nasal").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                            stop = data.symbols?.Where(s => s.type == "stop").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                            tap = data.symbols?.Where(s => s.type == "tap").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                            affricate = data.symbols?.Where(s => s.type == "affricate").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();

                            var yamlConsonants = fricative.Concat(aspirate).Concat(semivowel).Concat(liquid).Concat(nasal).Concat(stop).Concat(tap).Concat(affricate).ToArray();
                            consonants = backupConsonants.Concat(yamlConsonants).Distinct().ToArray();

                            PhonemeOverrides = data.timings?.ToDictionary(t => t.symbol, t => t.value) ?? new Dictionary<string, double>();
                            if (backupDictionaryReplacements == null) {
                                backupDictionaryReplacements = new Dictionary<string, string>(dictionaryReplacements);
                            }
                            dictionaryReplacements.Clear();
                            foreach (var kvp in backupDictionaryReplacements) {
                                dictionaryReplacements[kvp.Key] = kvp.Value;
                            }

                            mergingReplacements.Clear();
                            splittingReplacements.Clear();

                            if (data?.replacements != null && data.replacements.Any()) {
                                foreach (var replacement in data.replacements) {
                                    string ruleScope = string.IsNullOrEmpty(replacement.where) ? "inside" : replacement.where.ToLowerInvariant();
                                    if (replacement.from is IEnumerable<object> fromList) {
                                        string[] fromArray = fromList.Select(item => item.ToString()).ToArray();
                                        if (replacement.to is string toString) mergingReplacements.Add(new Replacement { from = fromArray, to = toString, where = ruleScope });
                                        else if (replacement.to is IEnumerable<object> toList) splittingReplacements.Add(new Replacement { from = fromArray, to = toList.Select(item => item.ToString()).ToArray(), where = ruleScope });
                                    } else if (replacement.from is string fromString) {
                                        if (replacement.to is string toString) dictionaryReplacements[fromString] = toString;
                                        else if (replacement.to is IEnumerable<object> toList) splittingReplacements.Add(new Replacement { from = fromString, to = toList.Select(item => item.ToString()).ToArray(), where = ruleScope });
                                    }
                                }
                            }

                            if (data?.fallbacks != null) {
                                yamlFallbacks.Clear();
                                foreach (var df in data.fallbacks) {
                                    if (!string.IsNullOrEmpty(df.from) && !string.IsNullOrEmpty(df.to)) {
                                        yamlFallbacks[df.from] = df.to;
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            Log.Error($"Failed to parse {YamlFileName}: {ex.Message}");
                        }
                    }
                }

                if (!hasDictionary) {
                    ReadDictionaryAndInit();
                } else {
                    Init();
                }
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

            if (tails.Contains(note.lyric)) {
                return new string[] { note.lyric };
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
                    } else {
                        for (int i = 0; i < subResult.Length; i++) {
                            string phoneme = subResult[i];
                            if (dictionaryReplacements.TryGetValue(phoneme, out string replaced)) {
                                subResult[i] = replaced;
                            } else if (dictionaryReplacements.TryGetValue(subResult[i], out string replacedExact)) {
                                subResult[i] = replacedExact;
                            }
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
        protected virtual Dictionary<string, string> GetDictionaryPhonemesReplacement() {
            return dictionaryReplacements ?? new Dictionary<string, string>();
        }
        private string[] backupVowels = null;
        private string[] backupConsonants = null;
        private Dictionary<string, string> backupDictionaryReplacements = null;

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

            symbols = ApplyReplacements(symbols.ToList(), false).ToArray();
            symbols = ApplyExtensions(symbols, notes);
            List<int> vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
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

        protected virtual string YamlFileName => null;
        protected virtual byte[] YamlTemplate => null;
        protected virtual string YamlVersion => null;

        protected string[] vowels = Array.Empty<string>();
        protected string[] consonants = Array.Empty<string>();
        protected string[] tails = "-,R".Split(',');
        protected string[] affricate = Array.Empty<string>();
        protected string[] fricative = Array.Empty<string>();
        protected string[] aspirate = Array.Empty<string>();
        protected string[] semivowel = Array.Empty<string>();
        protected string[] liquid = Array.Empty<string>();
        protected string[] nasal = Array.Empty<string>();
        protected string[] stop = Array.Empty<string>();
        protected string[] tap = Array.Empty<string>();

        protected Dictionary<string, string> dictionaryReplacements = new Dictionary<string, string>();
        protected Dictionary<string, double> PhonemeOverrides = new Dictionary<string, double>();
        protected Dictionary<string, string> yamlFallbacks = new Dictionary<string, string>();
        protected List<string> consExceptions = new List<string>();

        public class YAMLData {
            public string version { get; set; }
            public SymbolData[] symbols { get; set; } = Array.Empty<SymbolData>();
            public Replacement[] replacements { get; set; } = Array.Empty<Replacement>();
            public Fallbacks[] fallbacks { get; set; } = Array.Empty<Fallbacks>();
            public Timings[] timings { get; set; } = Array.Empty<Timings>();

            public struct SymbolData { public string symbol { get; set; } public string type { get; set; } }
            public struct Fallbacks { public string from { get; set; } public string to { get; set; } }
            public struct Timings { public string symbol { get; set; } public double value { get; set; } }
        }

        public class Replacement {
            public object from { get; set; }
            public object to { get; set; }
            public string where { get; set; } = "inside";

            public List<string> FromList {
                get {
                    if (from is string s) return new List<string> { s };
                    if (from is IEnumerable<object> list) return list.Select(x => x.ToString()).ToList();
                    return new List<string>();
                }
            }

            public List<string> ToList {
                get {
                    if (to is string s) return new List<string> { s };
                    if (to is IEnumerable<object> list) return list.Select(x => x.ToString()).ToList();
                    return new List<string>();
                }
            }
        }

        protected List<Replacement> mergingReplacements = new List<Replacement>();
        protected List<Replacement> splittingReplacements = new List<Replacement>();

        protected virtual bool IsGroupKeyword(string rulePhoneme) {
            string baseGroup = rulePhoneme.Split(new[] { '!', '=', '+' })[0];
            return new[] { "vowel", "vowels", "consonant", "consonants", 
                           "affricate", "fricative", "aspirate", "semivowel", 
                           "liquid", "nasal", "stop", "tap" }.Contains(baseGroup);
        }

        protected virtual bool IsGroupMatch(string rulePhoneme, string actualPhoneme) {
            string baseGroup = rulePhoneme.Split(new[] { '!', '=', '+' })[0];
            if (rulePhoneme.Contains("+")) {
                string added = rulePhoneme.Substring(rulePhoneme.IndexOf('+') + 1).Split(new[] { '!', '=' })[0];
                // If it matches another group name, or a literal letter, it passes
                foreach (string inc in added.Split(',')) {
                    if (IsGroupKeyword(inc) ? IsGroupMatch(inc, actualPhoneme) : inc == actualPhoneme) {
                        return true;
                    }
                }
            }

            // BASE GROUP: If it wasn't an addition, it must belong to the base group.
            bool inBaseGroup = false;
            switch (baseGroup) {
                case "vowel": case "vowels": inBaseGroup = GetVowels().Contains(actualPhoneme); break;
                case "consonant": case "consonants": inBaseGroup = GetConsonants().Contains(actualPhoneme); break;
                case "affricate": inBaseGroup = affricate.Contains(actualPhoneme); break;
                case "fricative": inBaseGroup = fricative.Contains(actualPhoneme); break;
                case "aspirate": inBaseGroup = aspirate.Contains(actualPhoneme); break;
                case "semivowel": inBaseGroup = semivowel.Contains(actualPhoneme); break;
                case "liquid": inBaseGroup = liquid.Contains(actualPhoneme); break;
                case "nasal": inBaseGroup = nasal.Contains(actualPhoneme); break;
                case "stop": inBaseGroup = stop.Contains(actualPhoneme); break;
                case "tap": inBaseGroup = tap.Contains(actualPhoneme); break;
            }

            if (!inBaseGroup) return false;

            // EXCLUSIONS (!): Reject if it's in the excluded list.
            if (rulePhoneme.Contains("!")) {
                string excluded = rulePhoneme.Substring(rulePhoneme.IndexOf('!') + 1).Split(new[] { '=', '+' })[0];
                if (excluded.Split(',').Contains(actualPhoneme)) return false;
            }

            // RESTRICTIONS (=): Reject if an equals list exists, and the phoneme isn't in it.
            if (rulePhoneme.Contains("=")) {
                string restricted = rulePhoneme.Substring(rulePhoneme.IndexOf('=') + 1).Split(new[] { '!', '+' })[0];
                if (!restricted.Split(',').Contains(actualPhoneme)) return false;
            }

            return true;
        }

        protected virtual List<string> ApplyReplacements(List<string> inputPhonemes, bool isBoundary) {
            if (!mergingReplacements.Any() && !splittingReplacements.Any()) return inputPhonemes;

            List<string> finalPhonemes = new List<string>();
            int idx = 0;
            
            var validRules = mergingReplacements.Concat(splittingReplacements)
                .Where(r => r.where == "all" || (!isBoundary && r.where == "inside") || (isBoundary && r.where == "boundary")).ToList();
                
            var validSplits = splittingReplacements
                .Where(r => r.where == "all" || (!isBoundary && r.where == "inside") || (isBoundary && r.where == "boundary")).ToList();

            while (idx < inputPhonemes.Count) {
                bool replaced = false;
                
                foreach (var rule in validRules) {
                    string[] fromArray = null;
                    if (rule.from is IList fromList) {
                        fromArray = fromList.Cast<object>().Select(x => x?.ToString()).ToArray();
                    } else if (rule.from is string[] strArr) {
                        fromArray = strArr;
                    }

                    if (fromArray != null && fromArray.Length > 0 && idx + fromArray.Length <= inputPhonemes.Count) {
                        bool match = true;
                        var captures = new Dictionary<string, Queue<string>>();
                        
                        for (int j = 0; j < fromArray.Length; j++) {
                            string rulePh = fromArray[j];
                            string actualPh = inputPhonemes[idx + j];
                            
                            if (IsGroupKeyword(rulePh)) {
                                if (IsGroupMatch(rulePh, actualPh)) {
                                    if (!captures.ContainsKey(rulePh)) captures[rulePh] = new Queue<string>();
                                    captures[rulePh].Enqueue(actualPh);
                                } else {
                                    match = false; break;
                                }
                            } else if (rulePh != actualPh) {
                                match = false; break;
                            }
                        }
                        
                        if (match) {
                            string[] toArray = null;
                            if (rule.to is IList toList) {
                                toArray = toList.Cast<object>().Select(x => x?.ToString()).ToArray();
                            } else if (rule.to is string[] strArr) {
                                toArray = strArr;
                            } else if (rule.to is string toStr) {
                                toArray = new string[] { toStr };
                            }

                            if (toArray != null) {
                                foreach (string toPh in toArray) {
                                    finalPhonemes.Add(IsGroupKeyword(toPh) && captures.ContainsKey(toPh) && captures[toPh].Count > 0 ? captures[toPh].Dequeue() : toPh);
                                }
                            }
                            
                            idx += fromArray.Length;
                            replaced = true;
                            break;
                        }
                    }
                }

                if (!replaced && validSplits.Any()) {
                    string currentPhoneme = inputPhonemes[idx];
                    bool singleReplaced = false;
                    foreach (var rule in validSplits) {
                        if (rule.from is IList || rule.from is string[]) continue;

                        string rulePh = rule.from?.ToString();
                        if (rulePh == null) continue;

                        if (IsGroupKeyword(rulePh) ? IsGroupMatch(rulePh, currentPhoneme) : rulePh == currentPhoneme) {
                            
                            string[] toArray = null;
                            if (rule.to is IList toList) {
                                toArray = toList.Cast<object>().Select(x => x?.ToString()).ToArray();
                            } else if (rule.to is string[] strArr) {
                                toArray = strArr;
                            }

                            if (toArray != null) {
                                foreach(string toPh in toArray) {
                                    finalPhonemes.Add(toPh == rulePh ? currentPhoneme : toPh);
                                }
                                singleReplaced = true;
                                break;
                            } else if (rule.to is string toStr) {
                                finalPhonemes.Add(toStr == rulePh ? currentPhoneme : toStr);
                                singleReplaced = true;
                                break;
                            }
                        }
                    }
                    if (!singleReplaced) finalPhonemes.Add(inputPhonemes[idx]);
                    idx++;
                } else if (!replaced) {
                    finalPhonemes.Add(inputPhonemes[idx]);
                    idx++;
                }
            }
            return finalPhonemes;
        }

        private Syllable ApplyBoundaryReplacements(Syllable syllable) {
            if (!mergingReplacements.Any() && !splittingReplacements.Any()) return syllable;

            List<string> currentPhonemes = new List<string>();
            bool hasPrevV = !string.IsNullOrEmpty(syllable.prevV);
            bool hasV = !string.IsNullOrEmpty(syllable.v);

            if (hasPrevV) currentPhonemes.Add(syllable.prevV);
            if (syllable.cc != null) currentPhonemes.AddRange(syllable.cc);
            if (hasV) currentPhonemes.Add(syllable.v);

            bool isBoundary = hasPrevV && syllable.position == 0;
            List<string> finalPhonemes = ApplyReplacements(currentPhonemes, isBoundary);

            string newPrevV = "";
            string newV = "";
            List<string> newCc = new List<string>();

            if (finalPhonemes.Count > 0) {
                if (hasPrevV) {
                    newPrevV = finalPhonemes[0];
                    finalPhonemes.RemoveAt(0);
                }
                if (hasV && finalPhonemes.Count > 0) {
                    var vowelsList = GetVowels();
                    int vIndex = finalPhonemes.Count - 1;
                    
                    for (int i = finalPhonemes.Count - 1; i >= 0; i--) {
                        if (vowelsList.Contains(finalPhonemes[i])) {
                            vIndex = i;
                            break;
                        }
                    }
                    newV = finalPhonemes[vIndex];
                    for (int i = 0; i < vIndex; i++) {
                        newCc.Add(finalPhonemes[i]);
                    }
                } else {
                    newCc.AddRange(finalPhonemes);
                }
            }
            
            syllable.prevV = newPrevV;
            syllable.cc = newCc.ToArray();
            syllable.v = newV;
            return syllable;
        }

        private Ending ApplyBoundaryReplacements(Ending ending) {
            if (!mergingReplacements.Any() && !splittingReplacements.Any()) return ending;

            List<string> currentPhonemes = new List<string>();
            bool hasPrevV = !string.IsNullOrEmpty(ending.prevV);

            if (hasPrevV) currentPhonemes.Add(ending.prevV);
            if (ending.cc != null) currentPhonemes.AddRange(ending.cc);

            List<string> finalPhonemes = ApplyReplacements(currentPhonemes, true);

            string newPrevV = "";
            List<string> newCc = new List<string>();

            if (finalPhonemes.Count > 0) {
                if (hasPrevV) {
                    newPrevV = finalPhonemes[0];
                    finalPhonemes.RemoveAt(0);
                }
                newCc.AddRange(finalPhonemes);
            }
            
            ending.prevV = newPrevV;
            ending.cc = newCc.ToArray();
            return ending;
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
                    phonemeSymbols[vowel] = true; 
                }
                foreach (var consonant in GetConsonants()) {
                    phonemeSymbols[consonant] = false;
                }

                var childDict = GetDictionaryPhonemesReplacement() ?? new Dictionary<string, string>();
                var safeDict = new Dictionary<string, string>();
                
                foreach (var kvp in childDict) {
                    safeDict[kvp.Key] = kvp.Value;
                    safeDict[kvp.Key.ToUpperInvariant()] = kvp.Value; // Safely catches 'AA'
                    safeDict[kvp.Key.ToLowerInvariant()] = kvp.Value; // Safely catches 'aa'
                }

                dictionaries[GetType()] = new G2pRemapper(
                    LoadBaseDictionary(),
                    phonemeSymbols,
                    safeDict); 

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
