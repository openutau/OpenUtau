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
using OpenUtau.Core;

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
            
            runtimeGlides.Clear();

            var syllables = MakeSyllables(notes, MakeEnding(prevNeighbours));
            if (syllables == null) {
                return HandleError();
            }

            var phonemes = new List<Phoneme>();
            int globalPhonemeIndex = 0; // Track the exact index for OpenUtau's UI

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
                        phonemes.AddRange(MakePhonemes(endingPhonemes, modifiedSyllable.duration, modifiedSyllable.position, false, modifiedSyllable.tone, mainNote.phonemeAttributes, globalPhonemeIndex));
                        globalPhonemeIndex += endingPhonemes.Count;
                    }
                    continue; 
                }
                
                var syllablePhonemes = ProcessSyllable(modifiedSyllable);
                var madePhonemes = MakePhonemes(syllablePhonemes, modifiedSyllable.duration, modifiedSyllable.position, false, modifiedSyllable.tone, mainNote.phonemeAttributes, globalPhonemeIndex).ToList();
                int currentSyllablePhonemeCount = syllablePhonemes.Count;

                var basePhoneme = madePhonemes.LastOrDefault();
                string baseAlias = basePhoneme.phoneme ?? "";
                if (vowelSustains.TryGetValue(baseAlias, out var sustainData) || 
                    vowelSustains.TryGetValue(modifiedSyllable.v, out sustainData)) {
                    
                    string mappedSustain = ValidateAliasIfNeeded(sustainData.sustain, modifiedSyllable.tone);
                    if (HasOto(mappedSustain, modifiedSyllable.tone) || HasOto(sustainData.sustain, modifiedSyllable.tone)) {
                        int offsetTicks = MsToTick(GetTransitionBasicLengthMsByConstant() * sustainData.offset);
                        madePhonemes.Add(new Phoneme {
                            phoneme = sustainData.sustain,
                            position = basePhoneme.position + offsetTicks,
                            index = globalPhonemeIndex + currentSyllablePhonemeCount
                        });
                        currentSyllablePhonemeCount++;
                    }
                }
                phonemes.AddRange(madePhonemes);
                globalPhonemeIndex += currentSyllablePhonemeCount;
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
                        phonemes.AddRange(MakePhonemes(endingPhonemes, modifiedEnding.duration, modifiedEnding.position, true, ending.tone, mainNote.phonemeAttributes, globalPhonemeIndex));
                        globalPhonemeIndex += endingPhonemes.Count; 
                    }
                }
            }

            var phonemesArray = phonemes.ToArray();
            CustomParameters(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours, phonemesArray);
            var finalPhonemes = AssignAllAffixes(phonemesArray.ToList(), notes, prevNeighbours);
            return new Result() {
                phonemes = finalPhonemes
            };
        }

        protected virtual Phoneme[] AssignAllAffixes(List<Phoneme> phonemes, Note[] notes, Note[] prevs) {
            int noteIndex = 0;
            for (int i = 0; i < phonemes.Count; i++) {
                var attr = notes[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == i) ?? default;
                string alt = (attr.alternate ?? GetParentAlternate())?.ToString() ?? string.Empty;
                string color = attr.voiceColor ?? GetParentVoiceColor();
                int toneShift = attr.toneShift ?? GetParentToneShift();
                var phoneme = phonemes[i];
                while (noteIndex < notes.Length - 1 && notes[noteIndex].position - notes[0].position < phoneme.position) {
                    noteIndex++;
                }

                var noteStartPosition = notes[noteIndex].position - notes[0].position;
                int tone;
                if (phoneme.position < noteStartPosition) {
                    tone = (noteIndex > 0) ? notes[noteIndex - 1].tone : 
                        (prevs != null && prevs.Length > 0) ? prevs.Last().tone : 
                        notes[noteIndex].tone;
                } else {
                    tone = notes[noteIndex].tone;
                }
                
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

        protected static readonly YamlDotNet.Serialization.IDeserializer TolerantDeserializer = 
            new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        public override void SetSinger(USinger singer) {
            if (this.singer != singer || this.localYamlGeneration != globalYamlGeneration) {
                this.singer = singer;
                this.localYamlGeneration = globalYamlGeneration;

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

                // file paths
                string globalFile = Path.Combine(PluginDir, YamlFileName);
                string singerFile = (singer != null && singer.Found && singer.Loaded && !string.IsNullOrEmpty(singer.Location)) 
                    ? Path.Combine(singer.Location, YamlFileName) 
                    : null;

                // Local helper function to update and backup YAML files safely
                void UpdateYamlIfNeeded(string filePath, bool isGlobal) {
                    if (string.IsNullOrEmpty(filePath)) return;
                    SetupYamlWatcher(filePath);

                    bool shouldWriteTemplate = false;
                    bool shouldBackupOldFile = false;
                    string currentVersion = "unknown";

                    if (File.Exists(filePath)) {
                        if (YamlTemplate != null && !string.IsNullOrEmpty(YamlVersion)) {
                            try {
                                var checkData = Core.Yaml.DefaultDeserializer.Deserialize<YAMLData>(File.ReadAllText(filePath));
                                currentVersion = checkData?.version?.Trim() ?? "";

                                // Update if missing, or if the parsed decimal is strictly lower than the target YamlVersion
                                if (string.IsNullOrEmpty(currentVersion)) {
                                    shouldWriteTemplate = true;
                                    shouldBackupOldFile = true;
                                } else if (Version.TryParse(currentVersion, out Version currV) && 
                                        Version.TryParse(YamlVersion, out Version targetV)) {
                                    if (currV < targetV) {
                                        shouldWriteTemplate = true;
                                        shouldBackupOldFile = true;
                                    }
                                } else if (currentVersion != YamlVersion && !double.TryParse(currentVersion, out _)) {
                                    // Fallback string check if version formats aren't purely numeric (e.g., "1.3b")
                                    shouldWriteTemplate = true;
                                    shouldBackupOldFile = true;
                                }
                            } catch (Exception ex) {
                                Log.Error(ex, $"Syntax error detected in '{filePath}'. Skipping template update to protect data.");
                                return; 
                            }
                        }
                    } else if (isGlobal && YamlTemplate != null) {
                        shouldWriteTemplate = true;
                    }

                    if (shouldBackupOldFile && File.Exists(filePath)) {
                        try {
                            // Include the version in the backup file name, e.g., arpa_backup(1.2).yaml
                            string safeVersion = string.IsNullOrEmpty(currentVersion) ? "unknown" : currentVersion;
                            string backupFile = Path.Combine(Path.GetDirectoryName(filePath), $"{Path.GetFileNameWithoutExtension(YamlFileName)}_backup({safeVersion}){Path.GetExtension(YamlFileName)}");
                            
                            if (File.Exists(backupFile)) File.Delete(backupFile);
                            File.Move(filePath, backupFile);
                            Log.Information($"Old {YamlFileName} backed up to {backupFile}");
                        } catch (Exception e) {
                            Log.Error(e, $"Failed to back up {filePath}. Aborting overwrite.");
                            return;
                        }
                    }

                    if (shouldWriteTemplate) {
                        try {
                            File.WriteAllBytes(filePath, YamlTemplate);
                            Log.Information($"'{filePath}' created or updated to version {YamlVersion ?? "default"}");
                        } catch (Exception e) {
                            Log.Error(e, $"Failed to write template to {filePath}");
                        }
                    }
                }

                UpdateYamlIfNeeded(globalFile, true);
                UpdateYamlIfNeeded(singerFile, false);

                // add to parsing list (Global first, Singer second)
                var filesToParse = new List<string>();
                if (File.Exists(globalFile)) filesToParse.Add(globalFile);
                if (!string.IsNullOrEmpty(singerFile) && File.Exists(singerFile)) filesToParse.Add(singerFile);

                // backups of hardcoded defaults exist
                if (backupVowels == null) backupVowels = GetVowels() ?? Array.Empty<string>();
                if (backupConsonants == null) backupConsonants = GetConsonants() ?? Array.Empty<string>();
                if (backupDictionaryReplacements == null) backupDictionaryReplacements = new Dictionary<string, string>(dictionaryReplacements);
                if (backupDiphthongTails == null) backupDiphthongTails = new Dictionary<string, string>(diphthongTails);
                if (backupDiphthongSplits == null) backupDiphthongSplits = new Dictionary<string, string[]>(diphthongSplits);

                // reset live arrays/lists back to defaults before stacking
                vowels = backupVowels;
                consonants = backupConsonants;
                tails = "-".Split(','); 

                fricative = Array.Empty<string>();
                aspirate = Array.Empty<string>();
                semivowel = Array.Empty<string>();
                liquid = Array.Empty<string>();
                nasal = Array.Empty<string>();
                stop = Array.Empty<string>();
                tap = Array.Empty<string>();
                affricate = Array.Empty<string>();

                dictionaryReplacements.Clear();
                foreach (var kvp in backupDictionaryReplacements) dictionaryReplacements[kvp.Key] = kvp.Value;

                diphthongTails.Clear();
                foreach (var kvp in backupDiphthongTails) diphthongTails[kvp.Key] = kvp.Value;

                diphthongSplits.Clear();
                foreach (var kvp in backupDiphthongSplits) diphthongSplits[kvp.Key] = kvp.Value;

                mergingReplacements.Clear();
                splittingReplacements.Clear();
                yamlFallbacks.Clear();
                PhonemeOverrides.Clear();
                if (backupVowelSustains == null) backupVowelSustains = new Dictionary<string, (string, double)>(vowelSustains);
                vowelSustains.Clear();
                foreach (var kvp in backupVowelSustains) vowelSustains[kvp.Key] = kvp.Value;

                // parse the files sequentially (Singer configs seamlessly overwrite global configs)
                foreach (var file in filesToParse) {
                    try {
                        var data = TolerantDeserializer.Deserialize<YAMLData>(File.ReadAllText(file));
                        
                        var yamlVowels = data.symbols?.Where(s => s.type == "vowel" || s.type == "diphthong").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        vowels = yamlVowels.Concat(vowels).Distinct().ToArray();

                        var yamlTails = data.symbols?.Where(s => s.type == "tail").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        tails = yamlTails.Concat(tails).Distinct().ToArray();
                        
                        if (data?.isglides != null) enableGlides = data.isglides.Value; 
                        
                        var yFricative = data.symbols?.Where(s => s.type == "fricative").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        fricative = yFricative.Concat(fricative).Distinct().ToArray();
                        var yAspirate = data.symbols?.Where(s => s.type == "aspirate").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        aspirate = yAspirate.Concat(aspirate).Distinct().ToArray();
                        var ySemivowel = data.symbols?.Where(s => s.type == "semivowel").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        semivowel = ySemivowel.Concat(semivowel).Distinct().ToArray();
                        var yLiquid = data.symbols?.Where(s => s.type == "liquid").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        liquid = yLiquid.Concat(liquid).Distinct().ToArray();
                        var yNasal = data.symbols?.Where(s => s.type == "nasal").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        nasal = yNasal.Concat(nasal).Distinct().ToArray();
                        var yStop = data.symbols?.Where(s => s.type == "stop").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        stop = yStop.Concat(stop).Distinct().ToArray();
                        var yTap = data.symbols?.Where(s => s.type == "tap").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        tap = yTap.Concat(tap).Distinct().ToArray();
                        var yAffricate = data.symbols?.Where(s => s.type == "affricate").Select(s => s.symbol).ToArray() ?? Array.Empty<string>();
                        affricate = yAffricate.Concat(affricate).Distinct().ToArray();

                        var yamlConsonants = yFricative.Concat(yAspirate).Concat(ySemivowel).Concat(yLiquid)
                            .Concat(yNasal).Concat(yStop).Concat(yTap).Concat(yAffricate).ToArray();
                        consonants = yamlConsonants.Concat(consonants).Distinct().ToArray();

                        // DIPHTHONG AUTO-TAIL DETECTION
                        var yamlDiphthongs = data.symbols?.Where(s => s.type == "diphthong").Select(s => s.symbol).Distinct().ToArray() ?? Array.Empty<string>();
                        var dynamicTails = consonants.OrderByDescending(c => c.Length).ToArray();

                        foreach (var d in yamlDiphthongs) {
                            if (!diphthongSplits.ContainsKey(d)) {
                                foreach (var tail in dynamicTails) {
                                    if (d.EndsWith(tail) && d != tail) {
                                        diphthongTails[d] = tail;
                                        break;
                                    }
                                }
                            }
                        }

                        // OVERRIDES & DICTIONARIES (Singer keys overwrite global keys)
                        if (data?.timings != null) {
                            foreach (var t in data.timings) PhonemeOverrides[t.symbol] = t.value;
                        }

                        if (data?.replacements != null) {
                            var localMerge = new List<Replacement>();
                            var localSplit = new List<Replacement>();
                            string GetFromKey(object fromObj) {
                                if (fromObj is string s) return s;
                                if (fromObj is System.Collections.IEnumerable e) {
                                    return string.Join(",", e.Cast<object>().Select(x => x?.ToString() ?? ""));
                                }
                                return "";
                            }

                            foreach (var rawReplacement in data.replacements) {
                                string fromKey = GetFromKey(rawReplacement.from);
                                mergingReplacements.RemoveAll(r => GetFromKey(r.from) == fromKey);
                                splittingReplacements.RemoveAll(r => GetFromKey(r.from) == fromKey);
                                
                                if (rawReplacement.from is string fromStr) {
                                    dictionaryReplacements.Remove(fromStr);
                                }

                                List<string> fromList = rawReplacement.FromList;
                                List<string> toList = rawReplacement.ToList;
                                object parsedFrom = fromList.Count == 1 ? fromList[0] : fromList.ToArray();
                                object parsedTo = toList.Count == 1 ? toList[0] : toList.ToArray();

                                var cleanReplacement = new Replacement {
                                    from = parsedFrom,
                                    to = parsedTo,
                                    where = rawReplacement.where
                                };

                                if (parsedFrom is string fromString) {
                                    // FIX: Treat all 1:1 replacements as splitting rules.
                                    // This prevents them from becoming global fallbacks
                                    // and forces them to respect string-length priority.
                                    localSplit.Add(cleanReplacement);
                                } else {
                                    // Many-to-Any goes to Merge
                                    localMerge.Add(cleanReplacement);
                                }
                            }
                            mergingReplacements.InsertRange(0, localMerge);
                            splittingReplacements.InsertRange(0, localSplit);
                        }

                        if (data?.fallbacks != null) {
                            foreach (var df in data.fallbacks) {
                                if (!string.IsNullOrEmpty(df.from) && !string.IsNullOrEmpty(df.to)) {
                                    yamlFallbacks[df.from] = df.to; 
                                }
                            }
                        }

                        if (data?.diphthongs != null) {
                            foreach (var d in data.diphthongs) {
                                if (!string.IsNullOrEmpty(d.from) && !string.IsNullOrEmpty(d.to)) {
                                    diphthongTails[d.from] = d.to; 
                                }
                            }
                        }

                        if (data?.vowelsustains != null) {
                            foreach (var v in data.vowelsustains) {
                                if (!string.IsNullOrEmpty(v.symbol) && !string.IsNullOrEmpty(v.sustain)) {
                                    vowelSustains[v.symbol] = (v.sustain, v.offset);
                                }
                            }
                        }

                    } catch (Exception ex) {
                        Log.Error($"Failed to parse {file}: {ex.Message}");
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
        public static YamlWatcher yamlWatcher;
        public static string currentlyWatchedYaml;
        public static int globalYamlGeneration = 0;
        public int localYamlGeneration = 0;

        private Dictionary<Type, IG2p> dictionaries = new Dictionary<Type, IG2p>();
        private const string FORCED_ALIAS_SYMBOL = "?";
        private string error = "";
        private readonly string[] wordSeparators = new[] { " ", "_" };
        private readonly string[] wordSeparator = new[] { "  " };

        /// <summary>
        /// A tracker to identify which phonemes were marked as glides dynamically.
        /// </summary>
        protected HashSet<string> runtimeGlides = new HashSet<string>();

        /// <summary>
        /// Flag a specific generated string as a glide during your ProcessSyllable / ProcessEnding loops.
        /// </summary>
        protected void glides(string alias) {
            runtimeGlides.Add(alias);
        }

        protected bool enableGlides = true;

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
        /// Defines whether a consonant (like a liquid or semi-vowel etc) should be placed ON the note (anchor)
        /// instead of pushing backward. Will return true if dynamically flagged using glides() or TryAddPhoneme().
        /// </summary>
        protected virtual bool IsGlide(string alias) {
            return runtimeGlides.Contains(alias) && enableGlides;
        }

        protected virtual bool NoGap => true;

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
        private Dictionary<string, string> backupDiphthongTails = null;
        private Dictionary<string, string[]> backupDiphthongSplits = null;
        private Dictionary<string, string> backupDictionaryReplacements = null;
        protected Dictionary<string, (string sustain, double offset)> vowelSustains = new Dictionary<string, (string, double)>();
        private Dictionary<string, (string sustain, double offset)> backupVowelSustains = null;

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
            string alt = (attr.alternate ?? GetParentAlternate())?.ToString() ?? string.Empty;
            string color = attr.voiceColor ?? GetParentVoiceColor();
            int toneShift = attr.toneShift ?? GetParentToneShift();
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

        protected virtual double GetTransitionMultiplier(string alias) {
            if (alias != null && PhonemeOverrides != null && PhonemeOverrides.TryGetValue(alias, out double overrideRatio)) {
                return overrideRatio;
            }
            return 1.0;
        }

        private void SetupYamlWatcher(string yamlFilePath) {
            if (string.IsNullOrEmpty(yamlFilePath) || currentlyWatchedYaml == yamlFilePath) {
                return;
            }

            if (yamlWatcher != null) {
                yamlWatcher.Dispose();
                yamlWatcher = null;
            }

            currentlyWatchedYaml = yamlFilePath;
            string directory = Path.GetDirectoryName(yamlFilePath);

            if (Directory.Exists(directory)) {
                yamlWatcher = new YamlWatcher(directory, () => {
                    Log.Information($"[SyllableBasedPhonemizer] Detected change in {YamlFileName}, incrementing generation...");
                    
                    System.Threading.Thread.Sleep(200);

                    lock (dictionaries) {
                        if (dictionaries.ContainsKey(this.GetType())) {
                            dictionaries.Remove(this.GetType());
                        }
                    }

                    backupVowels = null;
                    backupConsonants = null;
                    backupDictionaryReplacements = null;
                    backupDiphthongSplits = null;
                    backupDiphthongTails = null;
                    globalYamlGeneration++;

                    if (this.singer != null) {
                        OpenUtau.Core.SingerManager.Inst.ScheduleReload(this.singer);
                    }
                });
            }
        }

        /// <summary>
        /// Uses Preutterance length
        /// </summary>
        protected virtual double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            return GetTransitionBasicLengthMs(alias);
        }

        /// <summary>
        /// OTO HELPER: Calculates transition length based on the mapped Oto's Preutterance.
        /// </summary>
        protected double GetTransitionBasicLengthMsByOto(string alias, int tone = 0, PhonemeAttributes attr = default) {
            if (string.IsNullOrEmpty(alias)) return GetTransitionBasicLengthMsByConstant();

            string color = attr.voiceColor ?? string.Empty;
            string alt = attr.alternate?.ToString() ?? string.Empty;
            int toneShift = attr.toneShift ?? 0;
            
            var validatedAlias = ValidateAliasIfNeeded(alias, tone + toneShift);
            var mappedAlias = MapPhoneme(validatedAlias, tone + toneShift, color, alt, singer);

            if (singer.TryGetMappedOto(mappedAlias, tone + toneShift, out var oto)) {
                // If overlap is negative, add that absolute duration to the preutterance 
                // to ensure the entire consonant timing is preserved.
                if (oto.Overlap < 0) {
                    return oto.Preutter - oto.Overlap;
                }
                return oto.Preutter; 
            }

            return GetTransitionBasicLengthMsByConstant();
        }

        /// <summary>
        /// a note length modifier, from 1 to 0.3. Used to make transition notes shorter on high tempo
        /// </summary>
        /// <returns></returns>
        protected double GetTempoNoteLengthFactor() {
            return (300 - Math.Clamp(bpm, 90, 300)) / (300 - 90) / 3 + 0.33;
        }

        protected virtual IG2p[] GetBaseG2ps() {
            return Array.Empty<IG2p>();
        }

        protected virtual IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();

            // Native YAML Dictionary Logic
            if (!string.IsNullOrEmpty(YamlFileName)) {
                string path = Path.Combine(PluginDir, YamlFileName);
                
                // Write template if missing
                if (!File.Exists(path) && YamlTemplate != null) {
                    Directory.CreateDirectory(PluginDir);
                    File.WriteAllBytes(path, YamlTemplate);
                }

                // Load dictionary from Singer Folder (Highest Priority)
                if (singer != null && singer.Found && singer.Loaded) {
                    string file = Path.Combine(singer.Location, YamlFileName);
                    if (File.Exists(file)) {
                        try {
                            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                        } catch (Exception e) {
                            Log.Error(e, $"Failed to load {file}");
                        }
                    }
                }

                // Load dictionary from Plugin Folder (Fallback Priority)
                if (File.Exists(path)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {path}");
                    }
                }
            } 
            // Legacy Text Dictionary Logic (if child uses GetDictionaryName instead of YAML)
            else {
                var dictionaryName = GetDictionaryName();
                if (!string.IsNullOrEmpty(dictionaryName)) {
                    var filename = Path.Combine(DictionariesPath, dictionaryName);
                    if (File.Exists(filename)) {
                        var dictionaryText = File.ReadAllText(filename);
                        var builder = G2pDictionary.NewBuilder();
                        foreach (var vowel in GetVowels()) builder.AddSymbol(vowel, true);
                        foreach (var consonant in GetConsonants()) builder.AddSymbol(consonant, false);
                        builder.AddEntry("a", new string[] { "a" });
                        ParseDictionary(dictionaryText, builder);
                        g2ps.Add(builder.Build());
                    }
                }
            }

            // Append the Child-Specific G2P Models (e.g., ArpabetPlusG2p)
            var childG2ps = GetBaseG2ps();
            if (childG2ps != null && childG2ps.Any()) {
                g2ps.AddRange(childG2ps);
            }

            return new G2pFallbacks(g2ps.ToArray());
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
        /// Native API for child phonemizers to automatically apply expressions (vel, alt, clr, etc.)
        /// This is called internally after all phonemes are generated and aligned, right before returning to the engine.
        /// </summary>
        protected virtual void CustomParameters(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours, Phoneme[] phonemes) {
            // Base implementation does nothing. Child classes override this to implement custom logic.
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
        /// Appends a phoneme and optionally marks it as a glide simultaneously.
        /// </summary>
        protected bool TryAddPhoneme(List<string> sourcePhonemes, int tone, bool isGlide, params string[] targetPhonemes) {
            foreach (var phoneme in targetPhonemes) {
                if (HasOto(phoneme, tone)) {
                    sourcePhonemes.Add(phoneme);
                    if (isGlide) glides(phoneme);
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

        protected Dictionary<string, string> diphthongTails = new Dictionary<string, string>();
        protected Dictionary<string, string[]> diphthongSplits = new Dictionary<string, string[]>();

        public class YAMLData {
            public string version { get; set; }
            public bool? isglides { get; set; }
            public SymbolData[] symbols { get; set; } = Array.Empty<SymbolData>();
            public Replacement[] replacements { get; set; } = Array.Empty<Replacement>();
            public Fallbacks[] fallbacks { get; set; } = Array.Empty<Fallbacks>();
            public Timings[] timings { get; set; } = Array.Empty<Timings>();
            public DiphthongData[] diphthongs { get; set; } = Array.Empty<DiphthongData>();
            public VowelSustainData[] vowelsustains { get; set; } = Array.Empty<VowelSustainData>();

            public struct SymbolData { public string symbol { get; set; } public string type { get; set; } }
            public struct Fallbacks { public string from { get; set; } public string to { get; set; } }
            public struct Timings { public string symbol { get; set; } public double value { get; set; } }
            public struct DiphthongData { public string from { get; set; } public string to { get; set; } }
            public struct VowelSustainData { public string symbol { get; set; } public string sustain { get; set; } public double offset { get; set; } }
        }

        public class Replacement {
            public object from { get; set; }
            public object to { get; set; }
            public string where { get; set; } = "inside";

            public List<string> FromList {
                get {
                    if (from is string s) return new List<string> { s };
                    if (from is IEnumerable<object> list) return list.Select(x => x.ToString() ?? "null").ToList();
                    return new List<string>();
                }
            }

            public List<string> ToList {
                get {
                    if (to is string s) return new List<string> { s };
                    if (to is IEnumerable<object> list) return list.Select(x => x.ToString() ?? "null").ToList();
                    return new List<string>();
                }
            }
        }

        protected List<Replacement> mergingReplacements = new List<Replacement>();
        protected List<Replacement> splittingReplacements = new List<Replacement>();

        protected virtual bool IsGroupKeyword(string rulePhoneme) {
            // Trim parentheses so "(vowel)" evaluates identically to "vowel"
            string cleanRule = rulePhoneme.Trim('(', ')');
            string baseGroup = cleanRule.Split(new[] { '!', '=', '&' })[0];
            return new[] { "vowel", "vowels", "consonant", "consonants", 
                           "affricate", "fricative", "aspirate", "semivowel", 
                           "liquid", "nasal", "stop", "tap" }.Contains(baseGroup);
        }

        protected virtual bool IsGroupMatch(string rulePhoneme, string actualPhoneme) {
            string cleanRule = rulePhoneme.Trim('(', ')');
            string baseGroup = cleanRule.Split(new[] { '!', '=', '&' })[0];
            
            // Replaced '+' with '&' for group addition
            if (cleanRule.Contains("&")) {
                string added = cleanRule.Substring(cleanRule.IndexOf('&') + 1).Split(new[] { '!', '=' })[0];
                foreach (string inc in added.Split(',')) {
                    if (IsGroupKeyword(inc) ? IsGroupMatch(inc, actualPhoneme) : inc == actualPhoneme) {
                        return true;
                    }
                }
            }

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

            if (cleanRule.Contains("!")) {
                string excluded = cleanRule.Substring(cleanRule.IndexOf('!') + 1).Split(new[] { '=', '&' })[0];
                if (excluded.Split(',').Contains(actualPhoneme)) return false;
            }

            if (cleanRule.Contains("=")) {
                string restricted = cleanRule.Substring(cleanRule.IndexOf('=') + 1).Split(new[] { '!', '&' })[0];
                if (!restricted.Split(',').Contains(actualPhoneme)) return false;
            }

            return true;
        }

        protected virtual List<string> ApplyReplacements(List<string> inputPhonemes, bool isBoundary) {
            if (!mergingReplacements.Any() && !splittingReplacements.Any()) return inputPhonemes;

            List<string> finalPhonemes = new List<string>();
            int idx = 0;
            
            // Sort validRules by the length of the matching array descending.
            // This guarantees multi-phoneme matches evaluate BEFORE 1:1 matches.
            var validRules = mergingReplacements.Concat(splittingReplacements)
                .Where(r => r.where == "all" || (!isBoundary && r.where == "inside") || (isBoundary && r.where == "boundary"))
                .OrderByDescending(r => r.FromList.Count)
                .ThenByDescending(r => r.FromList.Sum(s => s.Length)) // Prioritize longer strings
                .ToList();
                
            var validSplits = splittingReplacements
                .Where(r => r.where == "all" || (!isBoundary && r.where == "inside") || (isBoundary && r.where == "boundary"))
                .OrderByDescending(r => r.FromList.Sum(s => s.Length)) // Sort fallback splits too
                .ToList();

            while (idx < inputPhonemes.Count) {
                bool replaced = false;
                
                foreach (var rule in validRules) {
                    List<string> fromArray = rule.FromList;
                    
                    if (fromArray != null && fromArray.Count > 0 && idx + fromArray.Count <= inputPhonemes.Count) {
                        bool match = true;
                        var captures = new Dictionary<string, List<string>>();
                        
                        for (int j = 0; j < fromArray.Count; j++) {
                            string rulePh = fromArray[j];
                            string actualPh = inputPhonemes[idx + j];
                            
                            string cleanRulePh = rulePh.Trim('(', ')');
                            string baseRulePh = cleanRulePh.Split(new[] { '!', '=', '&' })[0];
                            
                            if (IsGroupKeyword(baseRulePh)) {
                                if (IsGroupMatch(rulePh, actualPh)) {
                                    if (!captures.ContainsKey(baseRulePh)) captures[baseRulePh] = new List<string>();
                                    captures[baseRulePh].Add(actualPh);
                                } else {
                                    match = false; break;
                                }
                            } else if (rulePh != actualPh) {
                                match = false; break;
                            }
                        }
                        
                        if (match) {
                            List<string> toArray = rule.ToList;

                            if (toArray != null && toArray.Count > 0) {
                                var captureIndices = new Dictionary<string, int>();
                                
                                foreach (string toPh in toArray) {
                                    // Split by + for concatenation
                                    string[] parts = toPh.Split('+');
                                    string[] cleanParts = new string[parts.Length];
                                    string baseGroupTo = null;

                                    for (int k = 0; k < parts.Length; k++) {
                                        // Strip parenthesis to find the base group cleanly
                                        string partNoParens = parts[k].Trim('(', ')');
                                        int cutoff = partNoParens.IndexOfAny(new[] { '!', '=', '&' });
                                        string potentialGroup = cutoff >= 0 ? partNoParens.Substring(0, cutoff) : partNoParens;
                                        
                                        if (baseGroupTo == null && IsGroupKeyword(potentialGroup)) {
                                            baseGroupTo = potentialGroup;
                                            cleanParts[k] = potentialGroup; // Store just the base group name
                                        } else {
                                            cleanParts[k] = partNoParens; // Store literals
                                        }
                                    }

                                    if (baseGroupTo != null && captures.ContainsKey(baseGroupTo) && captures[baseGroupTo].Count > 0) {
                                        if (!captureIndices.ContainsKey(baseGroupTo)) captureIndices[baseGroupTo] = 0;
                                        int cIdx = captureIndices[baseGroupTo];
                                        if (cIdx >= captures[baseGroupTo].Count) cIdx = captures[baseGroupTo].Count - 1;
                                        
                                        string capturedPhoneme = captures[baseGroupTo][cIdx];
                                        
                                        string reconstructed = "";
                                        for (int k = 0; k < cleanParts.Length; k++) {
                                            if (cleanParts[k] == baseGroupTo) {
                                                reconstructed += capturedPhoneme;
                                            } else {
                                                reconstructed += cleanParts[k]; 
                                            }
                                        }
                                        finalPhonemes.Add(reconstructed);
                                        captureIndices[baseGroupTo]++;
                                    } else {
                                        finalPhonemes.Add(string.Join("", cleanParts));
                                    }
                                }
                            }
                            
                            idx += fromArray.Count;
                            replaced = true;
                            break;
                        }
                    }
                }

                // Fallback for single-phoneme splitting rules
                if (!replaced && validSplits.Any()) {
                    string currentPhoneme = inputPhonemes[idx];
                    bool singleReplaced = false;
                    foreach (var rule in validSplits) {
                        List<string> fromArray = rule.FromList;
                        if (fromArray == null || fromArray.Count != 1) continue;

                        string rulePh = fromArray[0];
                        string cleanRulePh = rulePh.Trim('(', ')');
                        string baseRulePh = cleanRulePh.Split(new[] { '!', '=', '&' })[0];

                        if (IsGroupKeyword(baseRulePh) ? IsGroupMatch(rulePh, currentPhoneme) : rulePh == currentPhoneme) {
                            
                            List<string> toArray = rule.ToList;

                            if (toArray != null && toArray.Count > 0) {
                                foreach(string toPh in toArray) {
                                    string[] parts = toPh.Split('+');
                                    string[] cleanParts = new string[parts.Length];
                                    string baseGroupTo = null;

                                    for (int k = 0; k < parts.Length; k++) {
                                        string partNoParens = parts[k].Trim('(', ')');
                                        int cutoff = partNoParens.IndexOfAny(new[] { '!', '=', '&' });
                                        string potentialGroup = cutoff >= 0 ? partNoParens.Substring(0, cutoff) : partNoParens;
                                        
                                        if (baseGroupTo == null && IsGroupKeyword(potentialGroup)) {
                                            baseGroupTo = potentialGroup;
                                            cleanParts[k] = potentialGroup;
                                        } else {
                                            cleanParts[k] = partNoParens;
                                        }
                                    }

                                    if (baseGroupTo != null) {
                                        string reconstructed = "";
                                        for (int k = 0; k < cleanParts.Length; k++) {
                                            if (cleanParts[k] == baseGroupTo) {
                                                reconstructed += currentPhoneme;
                                            } else {
                                                reconstructed += cleanParts[k];
                                            }
                                        }
                                        finalPhonemes.Add(reconstructed);
                                    } else {
                                        finalPhonemes.Add(string.Join("", cleanParts));
                                    }
                                }
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

            currentPhonemes.Add(hasPrevV ? syllable.prevV : "null");
            
            if (syllable.cc != null) currentPhonemes.AddRange(syllable.cc);
            if (hasV) currentPhonemes.Add(syllable.v);

            bool isBoundary = (hasPrevV && syllable.position == 0) || !hasPrevV;
            List<string> finalPhonemes = ApplyReplacements(currentPhonemes, isBoundary);

            string newPrevV = "";
            string newV = "";
            List<string> newCc = new List<string>();

            if (finalPhonemes.Count > 0) {
                string firstPh = finalPhonemes[0];
                
                if (firstPh == "null") {
                    newPrevV = "";
                    finalPhonemes.RemoveAt(0);
                } else {
                    newPrevV = firstPh;
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
            currentPhonemes.Add(hasPrevV ? ending.prevV : "null");
            
            if (ending.cc != null) currentPhonemes.AddRange(ending.cc);
            
            bool hasTail = ending.HasTail;
            currentPhonemes.Add(hasTail ? ending.tail : "null");

            List<string> finalPhonemes = ApplyReplacements(currentPhonemes, true);

            string newPrevV = "";
            string newTail = "";
            List<string> newCc = new List<string>();

            if (finalPhonemes.Count > 0) {
                // The first item is always the previous vowel (or empty if null)
                string firstPh = finalPhonemes[0];
                if (firstPh == "null") {
                    newPrevV = "";
                } else {
                    newPrevV = firstPh;
                }
                finalPhonemes.RemoveAt(0);
            }
            
            if (finalPhonemes.Count > 0) {
                // The last item is always the tail (or empty if null)
                string lastPh = finalPhonemes.Last();
                if (lastPh == "null") {
                    newTail = "";
                } else {
                    newTail = lastPh;
                }
                finalPhonemes.RemoveAt(finalPhonemes.Count - 1);
            }

            newCc.AddRange(finalPhonemes);
            
            ending.prevV = newPrevV;
            ending.cc = newCc.ToArray();
            ending.tail = newTail;
            
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
        
        private Phoneme[] MakePhonemes(List<string> phonemeSymbols, int containerLength, int position, bool isEnding, int tone = 0, PhonemeAttributes[] attributes = null, int globalStartIndex = 0) {
            var phonemes = new Phoneme[phonemeSymbols.Count];
            
            int[] trueLengths = new int[phonemeSymbols.Count];
            for (int i = 1; i < phonemeSymbols.Count; i++) {
                var prevPhonemeI = phonemeSymbols.Count - i;
                var currentPhonemeI = phonemeSymbols.Count - i - 1; 
                
                var nextGlobalIndex = globalStartIndex + prevPhonemeI;
                var nextPAttr = attributes?.FirstOrDefault(a => a.index == nextGlobalIndex) ?? default;
                
                string nextAlias = phonemeSymbols[prevPhonemeI];
                string currentAlias = phonemeSymbols[currentPhonemeI];

                double baseLengthMs;
                double stretch = nextPAttr.consonantStretchRatio ?? 1.0;
                
                // Check if the alias has a YAML or Categorical multiplier
                double overrideRatio = currentAlias != null ? GetTransitionMultiplier(currentAlias) : 1.0;

                if (overrideRatio != 1.0) {
                    baseLengthMs = GetTransitionBasicLengthMsByConstant();
                    stretch *= overrideRatio; 
                } else {
                    baseLengthMs = GetTransitionBasicLengthMs(nextAlias, tone, nextPAttr);
                }
                
                trueLengths[i] = MsToTick(baseLengthMs * stretch);
            }

            // IsGlide
            int anchorI = 0;
            if (!isEnding) {
                for (int i = 1; i < phonemeSymbols.Count; i++) {
                    var phonemeI = phonemeSymbols.Count - i - 1;
                    if (phonemeSymbols[phonemeI] != null && IsGlide(phonemeSymbols[phonemeI])) {
                        anchorI = i;
                    } else {
                        break;
                    }
                }
            }

            for (var i = 0; i < phonemeSymbols.Count; i++) {
                var phonemeI = phonemeSymbols.Count - i - 1;
                var globalIndex = globalStartIndex + phonemeI;
                var validatedAlias = phonemeSymbols[phonemeI];

                if (validatedAlias != null) {
                    phonemes[phonemeI] = new Phoneme {
                        phoneme = validatedAlias,
                        index = globalIndex 
                    };
                    
                    if (i == 0) {
                        if (isEnding) {
                            var pAttr = attributes?.FirstOrDefault(a => a.index == globalIndex) ?? default;
                            double baseLengthMs;
                            double stretch = pAttr.consonantStretchRatio ?? 1.0;
                            
                            double overrideRatio = phonemes[phonemeI].phoneme != null ? GetTransitionMultiplier(phonemes[phonemeI].phoneme) : 1.0;

                            if (overrideRatio != 1.0) {
                                // YAML Override active: Use the multiplier and bypass NoGap entirely
                                baseLengthMs = GetTransitionBasicLengthMsByConstant();
                                phonemes[phonemeI].position = MsToTick(baseLengthMs * stretch * overrideRatio);
                            } else {
                                // Default behavior
                                baseLengthMs = GetTransitionBasicLengthMsByOto(phonemes[phonemeI].phoneme, tone, pAttr);

                                if (NoGap) {
                                    // Snapped mode: Use a visible 50-tick anchor capped at 1/3 of the note
                                    int targetTicks = 50; 
                                    int maxAllowed = containerLength / 3;
                                    phonemes[phonemeI].position = System.Math.Min(targetTicks, maxAllowed);
                                } else {
                                    // Natural mode: Use the full Preutterance
                                    phonemes[phonemeI].position = MsToTick(baseLengthMs);
                                }
                            }
                        } else {
                            int sum = 0;
                            for (int k = 1; k <= anchorI; k++) {
                                sum += trueLengths[k];
                            }
                            phonemes[phonemeI].position = -sum;
                        }
                    } else {
                        // VC transitions keep their full stretched length
                        phonemes[phonemeI].position = trueLengths[i];
                    }
                } else {
                    // Initialize empty slots properly to avoid null crashes
                    phonemes[phonemeI] = new Phoneme {
                        phoneme = null,
                        position = 0,
                        index = globalIndex
                    };
                }
            }
            
            return ScalePhonemes(phonemes, position, isEnding ? phonemeSymbols.Count - 1 : phonemeSymbols.Count - 1, containerLength);
        }

        private string ValidateAliasIfNeeded(string alias, int tone) {
            if (HasOto(alias, tone)) {
                return alias;
            }
            return ValidateAlias(alias);
        }

        private Phoneme[] ScalePhonemes(Phoneme[] phonemes, int startPosition, int phonemesCount, int containerLengthTick = -1) {
            var offset = 0;
            var lengthModifier = 1.0;

            if (containerLengthTick > 0) {
                var allTransitionsLengthTick = phonemes.Sum(n => n.position);

                // Instead of a fixed "Constant * 2", use a proportional limit.
                // This allows transitions to occupy up to 80% of the note.
                var maxAllowedConsonantTick = (int)(containerLengthTick * 0.8);

                if (allTransitionsLengthTick > maxAllowedConsonantTick) {
                    lengthModifier = (double)maxAllowedConsonantTick / allTransitionsLengthTick;
                }
            }

            for (var i = phonemes.Length - 1; i >= 0; i--) {
                if (phonemes[i].phoneme == null) continue;
                var finalLengthTick = (int)(phonemes[i].position * lengthModifier);
                phonemes[i].position = startPosition - finalLengthTick - offset;
                offset += finalLengthTick;
            }

            return phonemes.Where(n => n.phoneme != null).ToArray();
        }

        #endregion
    }
}
