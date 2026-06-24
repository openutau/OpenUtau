using System.Collections.Generic;
using System.Linq;
using System.IO;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Pinyin;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Cantonese phonemizer for Syo-style banks.
    /// Supports both full jyutping syllables as well as syllable fallbacks without a final consonant or falling diphthong.
    /// Supports hanzi and jyutping input.
    /// Supports custom dictionary loading from YAML files.
    /// </summary>
    [Phonemizer("Cantonese Syo-Style Phonemizer", "ZH-YUE SYO", "Lotte V", language: "ZH-YUE")]
    public class CantoneseSyoPhonemizer : Phonemizer {

        /// <summary>
        /// The default consonant table.
        /// </summary>
        static readonly string defaultConsonants = "b,p,m,f,d,t,n,l,g,k,ng,h,gw,kw,w,z,c,s,j";

        /// <summary>
        /// The default vowel split table.
        /// </summary>
        static readonly string defaultVowels = "aap=aa p,aat=aa t,aak=aa k,aam=aa m,aan=aa n,aang=aa ng,aai=aa i,aau=aa u,ap=a p,at=a t,ak=a k,am=a m,an=a n,ang=a ng,ai=a i,au=a u,op=o p,ot=o t,ok=o k,om=o m,on=o n,ong=o ng,oi=o i,ou=o u,oet=oe t,oek=oe k,oeng=oe ng,oei=oe i,eot=eo t,eon=eo n,eoi=eo i,ep=e p,et=e t,ek=e k,em=e m,en=e n,eng=e ng,ei=e i,eu=e u,up=u p,ut=u t,uk=uu k,um=um,un=u n,ung=uu ng,ui=u i,yut=yu t,yun=yu n,ip=i p,it=i t,ik=ii k,im=i m,in=i n,ing=ii ng,iu=i u";

        /// <summary>
        /// Default vowel substitutes.
        /// </summary>
        static readonly string[] defaultSubstitution = new string[] {
            "aap,aat,aak,aam,aan,aang,aai,aau=aa", "ap,at,ak,am,an,ang,ai,au=a", "op,ot,ok,om,on,ong,oi,ou=o", "oet,oek,oen,oeng,oei=oe", "eot,eon,eoi=eo","ep,et,ek,em,en,eng,ei,eu=e", "uk,ung=uu", "up,ut,um,un,ui=u", "yut,yun=yu","ik,ing=ii", "ip,it,im,in,iu=i"
        };

        /// <summary>
        /// Default substitutes for finals.
        /// </summary>
        static readonly string[] defaultFinalSub = new string[] {
            "ii ng=i ng", "ii k=i k", "uu k=u k", "uu ng=u ng", "oe t=eo t", "oe i=eo i"
        };

        private HashSet<string> cSet;
        private Dictionary<string, string> vDict;
        private Dictionary<string, string> substituteLookup;
        private Dictionary<string, string> finalSubLookup;
        private IG2p customG2p;

        private USinger singer;

        /// <summary>
        /// Loads custom dictionary from YAML file.
        /// Priority order:
        /// 1. Singer folder (OpenUtau/Singers/{singer}/zhyue.yaml)
        /// 2. Plugin folder (OpenUtau/Plugins/zhyue.yaml)
        /// 3. Built-in rules (if no custom dictionary found)
        /// </summary>
        private void LoadCustomDictionary() {
            var g2ps = new List<IG2p>();

            // Try to load from singer folder first
            if (singer != null && !string.IsNullOrEmpty(singer.Location)) {
                string singerDictPath = Path.Combine(singer.Location, "zhyue.yaml");
                if (File.Exists(singerDictPath)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(singerDictPath)).Build());
                        Log.Information($"Loaded custom Cantonese dictionary from singer folder: {singerDictPath}");
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load custom Cantonese dictionary from {singerDictPath}");
                    }
                }
            }
            
            // Try to load from plugin folder
            string pluginDictPath = Path.Combine(PathManager.Inst.PluginsPath, "zhyue.yaml");
            if (File.Exists(pluginDictPath)) {
                try {
                    g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(pluginDictPath)).Build());
                    Log.Information($"Loaded custom Cantonese dictionary from plugin folder: {pluginDictPath}");
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load custom Cantonese dictionary from {pluginDictPath}");
                }
            }

            // If custom dictionaries were loaded, wrap them in a G2pFallbacks
            if (g2ps.Count > 0) {
                customG2p = new G2pFallbacks(g2ps.ToArray());
                Log.Information("Using custom dictionary for Cantonese phonemizer");
            } else {
                // Load built-in rules
                LoadBuiltInRules();
                Log.Information("Using built-in rules for Cantonese phonemizer");
            }
        }

        /// <summary>
        /// Loads built-in rules (original hardcoded rules).
        /// </summary>
        private void LoadBuiltInRules() {
            cSet = new HashSet<string>(defaultConsonants.Split(','));
            vDict = defaultVowels.Split(',')
                .Select(s => s.Split('='))
                .ToDictionary(a => a[0], a => a[1]);
            substituteLookup = defaultSubstitution.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[0].Split(',').Select(orig => (orig, parts[1]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            finalSubLookup = defaultFinalSub.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[0].Split(',').Select(orig => (orig, parts[1]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            LoadCustomDictionary();
        }

        /// <summary>
        /// Converts hanzi notes to jyutping phonemes.
        /// </summary>
        /// <param name="groups"></param>
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            JyutpingConversion.RomanizeNotes(groups);
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var lyric = note.lyric;
            
            // First check custom dictionary if loaded
            if (customG2p != null) {
                var customPhonemes = customG2p.Query(lyric);
                if (customPhonemes != null && customPhonemes.Length > 0) {
                    return ProcessWithCustomPhonemes(note, customPhonemes, prevNeighbour, nextNeighbour);
                }
            }
            
            // Otherwise use default logic
            return ProcessWithDefaultRules(note, prevNeighbour, nextNeighbour);
        }

        /// <summary>
        /// Processes note using custom phonemes from dictionary.
        /// </summary>
        private Result ProcessWithCustomPhonemes(Note note, string[] phonemes, Note? prevNeighbour, Note? nextNeighbour) {
            var totalDuration = note.duration;
            
            // If only one phoneme, return simple result
            if (phonemes.Length == 1) {
                return MakeSimpleResult(phonemes[0]);
            }
            
            // For multiple phonemes, split the duration
            var resultPhonemes = new List<Phoneme>();
            int remainingDuration = totalDuration;
            
            for (int i = 0; i < phonemes.Length; i++) {
                int position = totalDuration - remainingDuration;
                int length = CalculatePhonemeLength(i, phonemes.Length, totalDuration);
                
                var phoneme = new Phoneme {
                    phoneme = phonemes[i],
                    position = position
                };
                
                resultPhonemes.Add(phoneme);
                remainingDuration -= length;
            }
            
            return new Result {
                phonemes = resultPhonemes.ToArray()
            };
        }

        /// <summary>
        /// Calculates appropriate length for each phoneme based on position and total duration.
        /// </summary>
        private int CalculatePhonemeLength(int index, int totalPhonemes, int totalDuration) {
            // Default logic: last phoneme gets 120 ticks or half duration, whichever is smaller
            if (index == totalPhonemes - 1) {
                int lastLength = 120;
                if (lastLength > totalDuration / 2) {
                    lastLength = totalDuration / 2;
                }
                return lastLength;
            }
            
            // For other phonemes, distribute remaining duration evenly
            int remainingPhonemes = totalPhonemes - index;
            return totalDuration / remainingPhonemes;
        }

        /// <summary>
        /// Original processing logic with default rules.
        /// </summary>
        private Result ProcessWithDefaultRules(Note note, Note? prevNeighbour, Note? nextNeighbour) {
            // The overall logic is:
            // 1. Remove consonant: "jyut" -> "yut".
            // 2. Lookup the trailing sound in vowel table: "yut" -> "yu t".
            // 3. Split the total duration and returns "jyut"/"jyu" and "yu t".
            var lyric = note.lyric;
            string consonant = string.Empty;
            string vowel = string.Empty;

            if (lyric.Length > 2 && cSet.Contains(lyric.Substring(0, 2))) {
                // First try to find consonant "gw", "kw", "ng", and extract vowel.
                consonant = lyric.Substring(0, 2);
                vowel = lyric.Substring(2);
            } else if (lyric.Length > 1 && cSet.Contains(lyric.Substring(0, 1)) && lyric != "ng") {
                // Then try to find single character consonants, and extract vowel.
                consonant = lyric.Substring(0, 1);
                vowel = lyric.Substring(1);
            } else {
                // Otherwise the lyric is a vowel.
                vowel = lyric;
            }

            string phoneme0 = lyric;

            // Get color
            string color = string.Empty;
            int toneShift = 0;
            int? alt = 0;
            if (note.phonemeAttributes != null) {
                var attr = note.phonemeAttributes.FirstOrDefault(attr0 => attr0.index == 0);
                color = attr.voiceColor;
                toneShift = attr.toneShift;
                alt = attr.alternate;
            }

            string fin = $"{vowel} -";
            // We will need to split the total duration for phonemes, so we compute it here.
            int totalDuration = note.duration;
            // Lookup the vowel split table. For example, "yut" will match "yu t".
            if (vDict.TryGetValue(vowel, out var phoneme1) && !string.IsNullOrEmpty(phoneme1)) {
                // Now phoneme0="jyu" and phoneme1="yu t",
                // try to give "yu t" 120 ticks, but no more than half of the total duration.
                int length1 = 120;

                if (length1 > totalDuration / 2) {
                    length1 = totalDuration / 2;
                }
                var lyrics = new List<string> { lyric };
                // find potential substitute symbol
                if (substituteLookup.TryGetValue(vowel ?? string.Empty, out var sub)) {
                    if (!string.IsNullOrEmpty(consonant)) {
                        lyrics.Add($"{consonant}{sub}");
                    } else {
                        lyrics.Add(sub);
                    }
                }

                // Try initial and then a plain lyric
                if (prevNeighbour == null || (prevNeighbour != null && (prevNeighbour.Value.lyric.EndsWith("p") || prevNeighbour.Value.lyric.EndsWith("t") || prevNeighbour.Value.lyric.EndsWith("k")))) {
                    var initial = $"- {lyric}";
                    var initial2 = $"- {lyrics[1]}";
                    var tests = new List<string> { initial, initial2, lyric, lyrics[1] };
                    if (checkOtoUntilHit(tests, note, out var otoInit)) {
                        phoneme0 = otoInit.Alias;
                    }
                } else { // nothing special necessary
                    if (checkOtoUntilHit(lyrics, note, out var otoLyric)) {
                        phoneme0 = otoLyric.Alias;
                    }
                }

                int length2 = 60;
                if (length2 > totalDuration / 2) {
                    length2 = totalDuration / 2;
                }
                if (nextNeighbour == null && singer.TryGetMappedOto(fin, note.tone, out _)) {
                    // Vowel ending is minimum 60 ticks, maximum half of note
                    var finals = new List<string> { fin };
                    if (checkOtoUntilHitFinal(finals, note, out var otoFin)) {
                        phoneme1 = otoFin.Alias;
                    }
                    return new Result {
                        phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = phoneme0,
                        },
                        new Phoneme() {
                            phoneme = phoneme1,
                            position = totalDuration - length2,
                        }
                    },
                    };
                } else {
                    var tails = new List<string> { phoneme1 };
                    // find potential substitute symbol
                    if (finalSubLookup.TryGetValue(phoneme1 ?? string.Empty, out var finSub)) {
                        tails.Add(finSub);
                    }
                    if (checkOtoUntilHitFinal(tails, note, out var otoTail)) {
                        phoneme1 = otoTail.Alias;
                    } else {
                        return MakeSimpleResult(phoneme0);
                    }
                }

                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = phoneme0,
                        },
                        new Phoneme() {
                            phoneme = phoneme1,
                            position = totalDuration - length1,
                        }
                    },
                };
            }

            // Check for vowel ending on open syllables.
            // If a vowel ending does not exist, it will not be inserted.
            if (nextNeighbour == null && string.IsNullOrEmpty(phoneme1) && !string.IsNullOrEmpty(fin)) {
                // Vowel ending is minimum 60 ticks, maximum half of note
                int length1 = 60;

                if (length1 > totalDuration / 2) {
                    length1 = totalDuration / 2;
                }
                // Try initial and then a plain lyric
                var lyrics = new List<string> { lyric };
                if (prevNeighbour == null || (prevNeighbour != null && (prevNeighbour.Value.lyric.EndsWith("p") || prevNeighbour.Value.lyric.EndsWith("t") || prevNeighbour.Value.lyric.EndsWith("k")))) {
                    var initial = $"- {lyric}";
                    var tests = new List<string> { initial, lyric };
                    if (checkOtoUntilHit(tests, note, out var otoInit)) {
                        phoneme0 = otoInit.Alias;
                    }
                } else { // nothing special necessary
                    if (checkOtoUntilHit(lyrics, note, out var otoLyric)) {
                        phoneme0 = otoLyric.Alias;
                    } else {
                        return MakeSimpleResult(phoneme0);
                    }
                }

                // Map vowel ending
                var tails = new List<string> { fin };
                if (checkOtoUntilHitFinal(tails, note, out var otoTail)) {
                    fin = otoTail.Alias;
                } else {
                    return MakeSimpleResult(phoneme0);
                }

                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = phoneme0,
                        },
                        new Phoneme() {
                            phoneme = fin,
                            position = totalDuration - length1,
                        }
                    },
                };
            }

            // Try initial and then a plain lyric
            if (prevNeighbour == null || (prevNeighbour != null && (prevNeighbour.Value.lyric.EndsWith("p") || prevNeighbour.Value.lyric.EndsWith("t") || prevNeighbour.Value.lyric.EndsWith("k")))) {
                var simpleInitial = $"- {lyric}";
                var tests = new List<string> { simpleInitial, lyric };
                if (checkOtoUntilHit(tests, note, out var otoInit)) {
                    phoneme0 = otoInit.Alias;
                } else {
                    return MakeSimpleResult(phoneme0);
                }
            } else { // nothing special necessary
                var tests = new List<string> { lyric };
                if (checkOtoUntilHit(tests, note, out var otoLyric)) {
                    phoneme0 = otoLyric.Alias;
                } else {
                    return MakeSimpleResult(phoneme0);
                }
            }
            // Not spliting is needed. Return as is.
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = phoneme0,
                    }
                },
            };
        }

        private Result MakeSimpleResult(string phoneme) {
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = phoneme,
                    }
                },
            };
        }

        /// <summary>
        /// Converts hanzi to jyutping, based on G2P.
        /// </summary>
        public class JyutpingConversion {
            public static Note[] ChangeLyric(Note[] group, string lyric) {
                var oldNote = group[0];
                group[0] = new Note {
                    lyric = lyric,
                    phoneticHint = oldNote.phoneticHint,
                    tone = oldNote.tone,
                    position = oldNote.position,
                    duration = oldNote.duration,
                    phonemeAttributes = oldNote.phonemeAttributes,
                };
                return group;
            }

            public static string[] Romanize(IEnumerable<string> lyrics) {
                var lyricsArray = lyrics.ToArray();
                var hanziLyrics = lyricsArray
                    .Where(Pinyin.Jyutping.Instance.IsHanzi)
                    .ToList();
                var jyutpingResult = Pinyin.Jyutping.Instance.HanziToPinyin(hanziLyrics, CanTone.Style.NORMAL, Pinyin.Error.Default).ToStrList();
                if (jyutpingResult == null) {
                    return lyricsArray;
                }
                var jyutpingIndex = 0;
                for (int i = 0; i < lyricsArray.Length; i++) {
                    if (lyricsArray[i].Length == 1 && Pinyin.Jyutping.Instance.IsHanzi(lyricsArray[i])) {
                        lyricsArray[i] = jyutpingResult[jyutpingIndex];
                        jyutpingIndex++;
                    }
                }
                return lyricsArray;
            }

            public static void RomanizeNotes(Note[][] groups) {
                var ResultLyrics = Romanize(groups.Select(group => group[0].lyric));
                Enumerable.Zip(groups, ResultLyrics, ChangeLyric).Last();
            }

            public void SetUp(Note[][] groups) {
                RomanizeNotes(groups);
            }
        }

        // make it quicker to check multiple oto occurrences at once rather than spamming if else if
        private bool checkOtoUntilHit(List<string> input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attrCheck => attrCheck.index == 0) ?? default;

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + attr.alternate, note.tone + attr.toneShift, attr.voiceColor, out var otoAlt)) {
                    otos.Add(otoAlt);
                } else if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            string color = attr.voiceColor ?? "";
            if (otos.Count > 0) {
                oto = otos.FirstOrDefault(oto => oto.IsColorMatch(color));
                if (oto == null) {
                    oto = otos.First();
                }
                return true;
            }
            return false;
        }

        // Check for final consonant or vowel ending
        private bool checkOtoUntilHitFinal(List<string> input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attrCheck => attrCheck.index == 1) ?? default;

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + attr.alternate, note.tone + attr.toneShift, attr.voiceColor, out var otoAlt)) {
                    otos.Add(otoAlt);
                } else if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            string color = attr.voiceColor ?? "";
            if (otos.Count > 0) {
                oto = otos.FirstOrDefault(oto => oto.IsColorMatch(color));
                if (oto != null) {
                    return true;
                }
            }
            return false;
        }
    }
}