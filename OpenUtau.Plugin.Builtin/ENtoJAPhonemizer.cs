using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;
using WanaKanaNet;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("English to Japanese Phonemizer", "EN to JA", "TUBS, Cadlaxa", language: "EN")]
    // EN2JA+ merged
    public class ENtoJAPhonemizer : SyllableBasedPhonemizer {
        protected override string YamlFileName => "en2ja.yaml";
        protected override byte[] YamlTemplate => Data.Resources.en2ja_template;
        protected override string YamlVersion => "1.2";

        public ENtoJAPhonemizer() {
            this.vowels = new string[] {
                "aa", "ax", "ae", "ah", "ao", "aw", "ay", "eh", "er", "ey", "ih", "iy", "ow", "oy", "uh", "uw",
                "a", "e", "i", "o", "u"
            };
            this.consonants = "b,ch,d,dh,dr,dx,f,g,hh,jh,k,l,m,n,ng,p,q,r,s,sh,t,th,tr,v,w,y,z".Split(',');
        }
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "";

        public Dictionary<string, List<string>> WanaKanaDictionary = new Dictionary<string, List<string>>();

        protected override IG2p[] GetBaseG2ps() {
            return new IG2p[] { new ArpabetPlusG2p() };
        }

        public class ChildYAMLData: YAMLData {
            public WanaKanaData[] wanakana { get; set; } = Array.Empty<WanaKanaData>();
        }

        public class WanaKanaData {
            public object roma { get; set; }
            public object kana { get; set; }

            public List<string> FromList {
                get {
                    if (roma is string s) return new List<string> { s };
                    if (roma is IEnumerable<object> list) return list.Select(x => x.ToString()).ToList();
                    return new List<string>();
                }
            }

            public List<string> ToList {
                get {
                    if (kana is string s) return new List<string> { s };
                    if (kana is IEnumerable<object> list) return list.Select(x => x.ToString()).ToList();
                    return new List<string>();
                }
            }
        }

        public override void SetSinger(USinger singer) {
            base.SetSinger(singer);

            if (this.singer != null && this.singer.Loaded) {
                
                string globalFile = Path.Combine(PluginDir, YamlFileName);
                string singerFile = Path.Combine(this.singer.Location, YamlFileName);

                var filesToParse = new List<string>();
                if (File.Exists(globalFile)) filesToParse.Add(globalFile);
                if (File.Exists(singerFile) && globalFile != singerFile) filesToParse.Add(singerFile);

                WanaKanaDictionary.Clear();

                foreach (var file in filesToParse) {
                    try {
                        var data = Core.Yaml.DefaultDeserializer.Deserialize<ChildYAMLData>(File.ReadAllText(file));

                        if (data?.wanakana != null) {
                            foreach (var entry in data.wanakana) {
                                string key = string.Join("", entry.FromList);
                                string value = string.Join(" ", entry.ToList);

                                if (!WanaKanaDictionary.ContainsKey(key)) {
                                    WanaKanaDictionary.Add(key, new List<string>());
                                }
                                
                                if (!WanaKanaDictionary[key].Contains(value)) {
                                    WanaKanaDictionary[key].Add(value); 
                                }
                                
                                // Add the romaji (key) as a fallback at the very end of the candidates
                                if (!WanaKanaDictionary[key].Contains(key)) {
                                    WanaKanaDictionary[key].Add(key); 
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Log.Error($"Failed to parse wanakana from {file}: {ex.Message}");
                    }
                }
            }
        }

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (original == null) {
                return null;
            }

            List<string> finalProcessedPhonemes = new List<string>();
            string[] tr = new[] { "tr" };
            string[] dr = new[] { "dr" };

            // Apply dr/tr splits dynamically 
            foreach (string s in original) {
                if (dr.Contains(s)) {
                    finalProcessedPhonemes.AddRange(new string[] { "jh", s[1].ToString() });
                } else if (tr.Contains(s)) {
                    finalProcessedPhonemes.AddRange(new string[] { "ch", s[1].ToString() });
                } else {
                    finalProcessedPhonemes.Add(s);
                }
            }
            return finalProcessedPhonemes.ToArray();
        }

        private string[] SpecialClusters = "ky gy ts ny hy by py my ry ly".Split();

        private Dictionary<string, string> AltCv => altCv;
        private static readonly Dictionary<string, string> altCv = new Dictionary<string, string> {
            {"si", "suli" }, {"zi", "zuli" },
            {"ti", "teli" }, {"tu", "tolu" },
            {"di", "deli" }, {"du", "dolu" },
            {"hu", "holu" },
            {"yi", "i" }, {"wu", "u" }, {"rra", "wa" },
            {"rri", "wi" }, {"rru", "ru" }, {"rre", "we" }, {"rro", "ulo" },
        };

        private Dictionary<string, string[]> ExtraCv => extraCv;
        private static readonly Dictionary<string, string[]> extraCv = new Dictionary<string, string[]> {
            {"kye", new [] { "ki", "e" } }, {"gye", new [] { "gi", "e" } }, {"suli", new [] { "se", "i" } },
            {"she", new [] { "si", "e" } }, {"zuli", new [] { "ze", "i" } }, {"je", new [] { "ji", "e" } },
            {"teli", new [] { "te", "i" } }, {"tolu", new [] { "to", "u" } }, {"che", new [] { "chi", "e" } },
            {"tsa", new [] { "tsu", "a" } }, {"tsi", new [] { "tsu", "i" } }, {"tse", new [] { "tsu", "e" } },
            {"tso", new [] { "tsu", "o" } }, {"deli", new [] { "de", "i" } }, {"dolu", new [] { "do", "u" } },
            {"nye", new [] { "ni", "e" } }, {"hye", new [] { "hi", "e" } }, {"holu", new [] { "ho", "u" } },
            {"fa", new [] { "fu", "a" } }, {"fi", new [] { "fu", "i" } }, {"fe", new [] { "fu", "e" } },
            {"fo", new [] { "fu", "o" } }, {"bye", new [] { "bi", "e" } }, {"pye", new [] { "pi", "e" } },
            {"mye", new [] { "mi", "e" } }, {"ye", new [] { "i", "e" } }, {"rye", new [] { "ri", "e" } },
            {"wi", new [] { "u", "i" } }, {"we", new [] { "u", "e" } }, {"ulo", new [] { "u", "o" } },
        };

        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = string.IsNullOrEmpty(syllable.prevV) ? "" : ReplacePhoneme(syllable.prevV, syllable.tone);
            string v = ReplacePhoneme(syllable.v, syllable.vowelTone);
            string[] cc = syllable.cc.Select(c => ReplacePhoneme(c, syllable.tone)).ToArray();

            List<string> vowels = new List<string> { v };
            var phonemes = new List<string>();
            var lastC = cc.Length - 1;
            var firstC = 0;

            if (CanMakeAliasExtension(syllable)) {
                return new List<string> { null };
            }

            var usingVC = false;

            if (prevV.Length == 0) {
                prevV = "-";
            }

            var adjustedCC = new List<string>();
            for (var i = 0; i < cc.Length; i++) {
                if (i == cc.Length - 1) {
                    adjustedCC.Add(cc[i]);
                } else {
                    if (cc[i] == cc[i + 1]) {
                        adjustedCC.Add(cc[i]);
                        i++;
                        continue;
                    }
                    var diphone = $"{cc[i]}{cc[i + 1]}";
                    if (SpecialClusters.Contains(diphone)) {
                        adjustedCC.Add(diphone);
                        i++;
                    } else {
                        adjustedCC.Add(cc[i]);
                    }
                }
            }
            cc = adjustedCC.ToArray();

            var finalCons = "";
            if (cc.Length > 0) {
                finalCons = cc[cc.Length - 1];

                var start = 0;
                (var hasVc, var vcPhonemes, var vcConsumed, _) = HasVc(prevV, cc, v, "", syllable.tone);
                usingVC = hasVc;

                bool hasStartAlias = false;
                int step = 0;

                if (prevV == "-") {
                    // Starting Multi-Consonants (- CC)
                    for (var i = cc.Length; i > 1; i--) {
                        var spaced = string.Join(" ", cc.Take(i));
                        var merged = string.Join("", cc.Take(i));

                        if (TryAddPhoneme(phonemes, syllable.tone, 
                            $"- {spaced}", ValidateAlias($"- {spaced}", syllable.tone),
                            $"-{spaced}", ValidateAlias($"-{spaced}", syllable.tone),
                            $"- {merged}", ValidateAlias($"- {merged}", syllable.tone),
                            $"-{merged}", ValidateAlias($"-{merged}", syllable.tone)
                        )) {
                            hasStartAlias = true;
                            step = i - 1;
                            break;
                        }
                    }

                    // 2Starting Single Consonants (- C)
                    if (!hasStartAlias) {
                        string hiraganaC = ToHiragana(cc[0], syllable.tone);
                        if (TryAddPhoneme(phonemes, syllable.tone, 
                            $"- {cc[0]}", ValidateAlias($"- {cc[0]}", syllable.tone), 
                            $"-{cc[0]}", ValidateAlias($"-{cc[0]}", syllable.tone)
                        )) {
                            hasStartAlias = true;
                            step = 0;
                        }
                    }
                }

                if (!hasStartAlias) {
                    phonemes.AddRange(vcPhonemes);
                    start = vcConsumed;
                } else {
                    usingVC = true;
                    start = step;
                }

                if (phonemes.Count > 0) {
                    prevV = WanaKana.ToRomaji(phonemes.Last()).Last<char>().ToString();
                }

                for (var i = start; i < cc.Length - 1; i++) {
                    string selectedPhoneme = null;
                    int loopStep = 0;

                    var extendedSpace1 = $"{cc[i]} {string.Join("", cc.Skip(i + 1))}";
                    var extendedSpace2 = $"{cc[i]} {string.Join(" ", cc.Skip(i + 1))}";
                    var extendedNoSpace = $"{cc[i]}{string.Join("", cc.Skip(i + 1))}";

                    // Check multi-consonant transitions first ([C1 C2] or [C1 C2C3])
                    if (HasOto(extendedSpace1, syllable.tone)) {
                        selectedPhoneme = extendedSpace1; loopStep = cc.Length - 2 - i;
                    } else if (HasOto(extendedSpace2, syllable.tone)) {
                        selectedPhoneme = extendedSpace2; loopStep = cc.Length - 2 - i;
                    } else if (HasOto(extendedNoSpace, syllable.tone)) { 
                        selectedPhoneme = extendedNoSpace; loopStep = cc.Length - 2 - i;
                    } else if (HasOto($"{cc[i]} {cc[i + 1]}", syllable.tone)) {
                        selectedPhoneme = $"{cc[i]} {cc[i + 1]}"; loopStep = 0;
                    } else if (HasOto(ValidateAlias($"{cc[i]} {cc[i + 1]}", syllable.tone), syllable.tone)) {
                        selectedPhoneme = ValidateAlias($"{cc[i]} {cc[i + 1]}", syllable.tone); loopStep = 0;
                    } else if (HasOto($"{cc[i]}{cc[i + 1]}", syllable.tone)) {
                        selectedPhoneme = $"{cc[i]}{cc[i + 1]}"; loopStep = 0;
                    } else if (HasOto(ValidateAlias($"{cc[i]}{cc[i + 1]}", syllable.tone), syllable.tone)) {
                        selectedPhoneme = ValidateAlias($"{cc[i]}{cc[i + 1]}", syllable.tone); loopStep = 0;
                    }

                    if (selectedPhoneme != null) {
                        TryAddPhoneme(phonemes, syllable.tone, selectedPhoneme);
                        prevV = WanaKana.ToRomaji(selectedPhoneme).Last<char>().ToString();
                        i += loopStep;
                        continue;
                    }

                    // Singular C Handling
                    bool isStop = stop != null && stop.Contains(cc[i]);
                    bool hasRomanC = HasOto(cc[i], syllable.tone) || HasOto(ValidateAlias(cc[i], syllable.tone), syllable.tone);

                    // Skip the stop ONLY if a VC already absorbed it (usingVC && i == start) in a <= 2 CC cluster
                    if (isStop && usingVC && i == start && cc.Length <= 2 && !hasRomanC) {
                        continue;
                    }

                    // If not absorbed by VC, or in clusters > 2, preserve the stop or fall back to mora/VCV (like ProcessEnding)
                    if (isStop) {
                        if (hasRomanC) {
                            selectedPhoneme = HasOto(cc[i], syllable.tone) ? cc[i] : ValidateAlias(cc[i], syllable.tone);
                        } else {
                            var hiraganaStop = ToHiragana(cc[i], syllable.tone);
                            var hiraganaStopVcv = TryVcv(prevV, hiraganaStop, syllable.tone);

                            if (HasOto(hiraganaStopVcv, syllable.tone)) {
                                selectedPhoneme = hiraganaStopVcv;
                                usingVC = true;
                            } else if (HasOto(hiraganaStop, syllable.tone)) {
                                selectedPhoneme = hiraganaStop;
                            } else if (HasOto(ValidateAlias(hiraganaStop, syllable.tone), syllable.tone)) {
                                selectedPhoneme = ValidateAlias(hiraganaStop, syllable.tone);
                            }
                        }

                        if (selectedPhoneme != null) {
                            TryAddPhoneme(phonemes, syllable.tone, selectedPhoneme);
                            prevV = WanaKana.ToRomaji(selectedPhoneme).Last<char>().ToString();
                        }
                        continue;
                    }

                    // Direct Roman Single Consonant Check [r], [s], [m], [n], [l], [f], [v]
                    if (usingVC && i == start) {
                        bool isAffricate = affricate != null && affricate.Contains(cc[i]);

                        if (HasOto(cc[i], syllable.tone)) {
                            selectedPhoneme = cc[i];
                        } else if (HasOto(ValidateAlias(cc[i], syllable.tone), syllable.tone)) {
                            selectedPhoneme = ValidateAlias(cc[i], syllable.tone);
                        } 
                        // If it's an affricate, also check and allow Japanese Kana entries (e.g. [ち], [つ])
                        else if (isAffricate) {
                            var hiraganaAff = ToHiragana(cc[i], syllable.tone);
                            var hiraganaVcv = TryVcv(prevV, hiraganaAff, syllable.tone);

                            if (HasOto(hiraganaVcv, syllable.tone)) {
                                selectedPhoneme = hiraganaVcv;
                            } else if (HasOto(hiraganaAff, syllable.tone)) {
                                selectedPhoneme = hiraganaAff;
                            } else if (HasOto(ValidateAlias(hiraganaAff, syllable.tone), syllable.tone)) {
                                selectedPhoneme = ValidateAlias(hiraganaAff, syllable.tone);
                            }
                        }

                        if (selectedPhoneme != null) {
                            TryAddPhoneme(phonemes, syllable.tone, selectedPhoneme);
                            prevV = WanaKana.ToRomaji(selectedPhoneme).Last<char>().ToString();
                        }

                        // For non-affricates without an explicit Roman entry, let the VC connect directly to the next CV
                        continue;
                    }
                    // Only fall back to Kana / CV representations if Roman standalone C doesn't exist in OTO
                    else {
                        var hiraganaCC = ToHiragana(cc[i], syllable.tone);
                        var hiraganaVcv = TryVcv(prevV, hiraganaCC, syllable.tone);

                        if (HasOto(hiraganaVcv, syllable.tone)) {
                            TryAddPhoneme(phonemes, syllable.tone, hiraganaVcv);
                            prevV = WanaKana.ToRomaji(hiraganaVcv).Last<char>().ToString();
                            usingVC = true;
                        } else if (HasOto(hiraganaCC, syllable.tone)) {
                            TryAddPhoneme(phonemes, syllable.tone, hiraganaCC);
                            prevV = WanaKana.ToRomaji(hiraganaCC).Last<char>().ToString();
                        }
                    }
                }
            }

            var cv = $"{finalCons}{v}";
            var crv = $"{finalCons} {v}";
            var hiraganaCv = ToHiragana(cv, syllable.vowelTone);
            
            switch (usingVC) {
                case false:
                    if (HasOto(TryVcv(prevV, hiraganaCv, syllable.vowelTone), syllable.vowelTone) || HasOto(ValidateAlias(TryVcv(prevV, hiraganaCv, syllable.vowelTone), syllable.vowelTone), syllable.vowelTone)) {
                        hiraganaCv = TryVcv(prevV, hiraganaCv, syllable.vowelTone);
                    } else if (HasOto(TryVcv(prevV, cv, syllable.vowelTone), syllable.vowelTone) || HasOto(ValidateAlias(TryVcv(prevV, cv, syllable.vowelTone), syllable.vowelTone), syllable.vowelTone)) {
                        hiraganaCv = TryVcv(prevV, cv, syllable.vowelTone);
                    } else if ((HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv, syllable.vowelTone), syllable.vowelTone))
                    || (HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv, syllable.vowelTone), syllable.vowelTone))) {
                        hiraganaCv = FixCv(AliasFormat($"{finalCons} {v}", "dynMid", syllable.vowelTone, ""), syllable.vowelTone);
                    } else {
                        hiraganaCv = FixCv(hiraganaCv, syllable.vowelTone);
                    }
                    break;
                case true when (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv, syllable.vowelTone), syllable.vowelTone))
                    || (HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv, syllable.vowelTone), syllable.vowelTone)):
                    usingVC = true;
                    hiraganaCv = FixCv(AliasFormat($"{finalCons} {v}", "dynMid", syllable.vowelTone, ""), syllable.vowelTone);
                    break;
                default:
                    usingVC = true;
                    var tryVcv = TryVcv(prevV, hiraganaCv, syllable.vowelTone);
                    if (HasOto(tryVcv, syllable.vowelTone) || HasOto(ValidateAlias(tryVcv, syllable.vowelTone), syllable.vowelTone)) {
                        hiraganaCv = tryVcv;
                    } else {
                        hiraganaCv = FixCv(hiraganaCv, syllable.vowelTone);
                    }
                    break;
            }

            var split = false;
            bool isStart = string.IsNullOrEmpty(syllable.prevV) || prevV == "-";

            if (isStart && cc.Length <= 1) {
                var dashCv = $"- {hiraganaCv}";
                var dashCvNoSpace = $"-{hiraganaCv}";
                var dashRomaji = $"- {cv}";
                var dashRomajiNoSpace = $"-{cv}";

                if (HasOto(dashCv, syllable.vowelTone)) { hiraganaCv = dashCv; }
                else if (HasOto(ValidateAlias(dashCv, syllable.vowelTone), syllable.vowelTone)) { hiraganaCv = ValidateAlias(dashCv, syllable.vowelTone); }
                else if (HasOto(dashCvNoSpace, syllable.vowelTone)) { hiraganaCv = dashCvNoSpace; }
                else if (HasOto(ValidateAlias(dashCvNoSpace, syllable.vowelTone), syllable.vowelTone)) { hiraganaCv = ValidateAlias(dashCvNoSpace, syllable.vowelTone); }
                else if (HasOto(dashRomaji, syllable.vowelTone)) { hiraganaCv = dashRomaji; }
                else if (HasOto(ValidateAlias(dashRomaji, syllable.vowelTone), syllable.vowelTone)) { hiraganaCv = ValidateAlias(dashRomaji, syllable.vowelTone); }
                else if (HasOto(dashRomajiNoSpace, syllable.vowelTone)) { hiraganaCv = dashRomajiNoSpace; }
                else if (HasOto(ValidateAlias(dashRomajiNoSpace, syllable.vowelTone), syllable.vowelTone)) { hiraganaCv = ValidateAlias(dashRomajiNoSpace, syllable.vowelTone); }
            }

            string finalAlias = null;
            if (HasOto(hiraganaCv, syllable.vowelTone)) { finalAlias = hiraganaCv; }
            else if (HasOto(ValidateAlias(hiraganaCv, syllable.vowelTone), syllable.vowelTone)) { finalAlias = ValidateAlias(hiraganaCv, syllable.vowelTone); }
            else {
                var dashCv = $"- {hiraganaCv}";
                var dashCvNoSpace = $"-{hiraganaCv}";
                if (HasOto(dashCv, syllable.vowelTone)) { finalAlias = dashCv; }
                else if (HasOto(ValidateAlias(dashCv, syllable.vowelTone), syllable.vowelTone)) { finalAlias = ValidateAlias(dashCv, syllable.vowelTone); }
                else if (HasOto(dashCvNoSpace, syllable.vowelTone)) { finalAlias = dashCvNoSpace; }
                else if (HasOto(ValidateAlias(dashCvNoSpace, syllable.vowelTone), syllable.vowelTone)) { finalAlias = ValidateAlias(dashCvNoSpace, syllable.vowelTone); }
            }

            if (finalAlias != null) {
                // Double Onset Cleanup: If finalAlias already contains the onset dash ("- わ"), remove redundant "- w"
                if (isStart && cc.Length == 1 && (finalAlias.StartsWith("- ") || finalAlias.StartsWith("-"))) {
                    var c = cc[0];
                    var kanaC = ToHiragana(c, syllable.tone);
                    phonemes.RemoveAll(p => 
                        p == $"- {c}" || p == $"-{c}" || 
                        p == $"- {kanaC}" || p == $"-{kanaC}" ||
                        p == ValidateAlias($"- {c}", syllable.tone) || p == ValidateAlias($"-{c}", syllable.tone) ||
                        p == ValidateAlias($"- {kanaC}", syllable.tone) || p == ValidateAlias($"-{kanaC}", syllable.tone)
                    );
                }
                phonemes.Add(finalAlias);
            } else {
                split = true;
            }
            
            if (split) {
                bool handledByAlt = false;

                // Try AltCv substitution first
                if (AltCv.TryGetValue(cv, out var substituteCv)) {
                    var altKana = ToHiragana(substituteCv, syllable.vowelTone);
                    var altVcv = TryVcv(prevV, altKana, syllable.vowelTone);

                    if (HasOto(altVcv, syllable.vowelTone) || HasOto(ValidateAlias(altVcv, syllable.vowelTone), syllable.vowelTone)) {
                        phonemes.Add(HasOto(altVcv, syllable.vowelTone) ? altVcv : ValidateAlias(altVcv, syllable.vowelTone));
                        handledByAlt = true;
                    } else if (HasOto(altKana, syllable.vowelTone) || HasOto(ValidateAlias(altKana, syllable.vowelTone), syllable.vowelTone)) {
                        phonemes.Add(FixCv(altKana, syllable.vowelTone));
                        handledByAlt = true;
                    } else if (HasOto(substituteCv, syllable.vowelTone) || HasOto(ValidateAlias(substituteCv, syllable.vowelTone), syllable.vowelTone)) {
                        phonemes.Add(FixCv(substituteCv, syllable.vowelTone));
                        handledByAlt = true;
                    }
                }

                // Fall back to multi-step ExtraCv decomposition if AltCv wasn't matched
                string[] splitCv = null;
                if (!handledByAlt) {
                    if (ExtraCv.TryGetValue(cv, out var directSplit)) {
                        splitCv = directSplit;
                    } else if (substituteCv != null && ExtraCv.TryGetValue(substituteCv, out var subSplit)) {
                        splitCv = subSplit;
                    }
                }

                if (splitCv != null) {
                    for (var i = 0; i < splitCv.Length; i++) {
                        if (splitCv[i] != prevV) {
                            var converted = ToHiragana(splitCv[i], syllable.vowelTone);
                            string candidate = (prevV == "-" || string.IsNullOrEmpty(prevV)) 
                                ? FixCv(converted, syllable.vowelTone)
                                : TryVcv(prevV, converted, syllable.vowelTone);

                            if (HasOto(candidate, syllable.vowelTone) || HasOto(ValidateAlias(candidate, syllable.vowelTone), syllable.vowelTone)) {
                                phonemes.Add(HasOto(candidate, syllable.vowelTone) ? candidate : ValidateAlias(candidate, syllable.vowelTone));
                            } else {
                                phonemes.Add(converted);
                            }
                            prevV = splitCv[i].Last<char>().ToString();
                        }
                    }
                }
            }
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string prevV = ReplacePhoneme(ending.prevV, ending.tone);
            string[] cc = ending.cc.Select(c => ReplacePhoneme(c, ending.tone)).ToArray();
            var phonemes = new List<string>();
            string v = ReplacePhoneme(ending.prevV, ending.tone);
            string t = ending.HasTail ? ReplacePhoneme(ending.tail, ending.tone) : "-";

            var lastC = cc.Length - 1;
            var firstC = 0;

            var adjustedCC = new List<string>();
            for (var i = 0; i < cc.Length; i++) {
                if (i == cc.Length - 1) {
                    adjustedCC.Add(cc[i]);
                } else {
                    if (cc[i] == cc[i + 1]) {
                        adjustedCC.Add(cc[i]);
                        i++;
                        continue;
                    }
                    var diphone = $"{cc[i]}{cc[i + 1]}";
                    if (SpecialClusters.Contains(diphone)) {
                        adjustedCC.Add(diphone);
                        i++;
                    } else {
                        adjustedCC.Add(cc[i]);
                    }
                }
            }
            cc = adjustedCC.ToArray();

            var usingVC = false;
            bool addedEnding = false;

            if (cc.Length > 0) {
                (var hasVc, var vcPhonemes, var vcConsumed, var isTail) = HasVc(prevV, cc, "", t, ending.tone);
                usingVC = hasVc;
                phonemes.AddRange(vcPhonemes);
                
                if (isTail) {
                    addedEnding = true;
                }

                var hasVCV = HasOto(TryVcv(prevV, ToHiragana($"{cc[0]}{v}", ending.tone), ending.tone), ending.tone);
                bool skipFirstFallback = usingVC && hasVCV;
                var start = vcConsumed;

                if (phonemes.Count > 0) {
                    prevV = WanaKana.ToRomaji(phonemes.Last()).Last<char>().ToString();
                }

                for (var i = start; i < cc.Length; i++) {
                    string selectedPhoneme = null;
                    int loopStep = 0;

                    bool isCCEndingIndex = (i == cc.Length - 2);
                    bool isCCEndingSkipped = (i == cc.Length - 1 && start > cc.Length - 2 && cc.Length > 1);

                    if ((isCCEndingIndex || isCCEndingSkipped) && ending.IsEndingVCWithMoreThanOneConsonant) {
                        int c1Idx = cc.Length - 2;
                        int c2Idx = cc.Length - 1;
                        string[] possibleCCEnds = new[] {
                            $"{cc[c1Idx]} {cc[c2Idx]} {t}", $"{cc[c1Idx]}{cc[c2Idx]} {t}",
                            $"{cc[c1Idx]} {cc[c2Idx]}{t}", $"{cc[c1Idx]}{cc[c2Idx]}{t}"
                        };

                        foreach (var endAlias in possibleCCEnds) {
                            if (HasOto(endAlias, ending.tone)) {
                                selectedPhoneme = endAlias;
                                loopStep = isCCEndingIndex ? 1 : 0;
                                addedEnding = true;
                                break;
                            } else if (HasOto(ValidateAlias(endAlias), ending.tone)) {
                                selectedPhoneme = ValidateAlias(endAlias);
                                loopStep = isCCEndingIndex ? 1 : 0;
                                addedEnding = true;
                                break;
                            }
                        }
                    }

                    if (selectedPhoneme == null && (cc[i] == "n" || cc[i] == "w" || cc[i] == "y")) {
                        var hiraganaN = ToHiragana(cc[i], ending.tone);
                        var hiraganaNVcv = TryVcv(prevV, hiraganaN, ending.tone);

                        bool hasVcv = hiraganaNVcv != hiraganaN && (HasOto(hiraganaNVcv, ending.tone) || HasOto(ValidateAlias(hiraganaNVcv, ending.tone), ending.tone));
                        bool hasKana = HasOto(hiraganaN, ending.tone) || HasOto(ValidateAlias(hiraganaN, ending.tone), ending.tone);

                        if (i == cc.Length - 1 || hasVcv) {
                            if (hasVcv || hasKana) {
                                selectedPhoneme = hasVcv 
                                    ? (HasOto(hiraganaNVcv, ending.tone) ? hiraganaNVcv : ValidateAlias(hiraganaNVcv, ending.tone))
                                    : (HasOto(hiraganaN, ending.tone) ? hiraganaN : ValidateAlias(hiraganaN, ending.tone));
                                usingVC = true;
                                loopStep = 0;

                                // At final position, check if a coda tail (e.g. [n R], [n -]) can follow this mora
                                if (i == cc.Length - 1) {
                                    TryAddPhoneme(phonemes, ending.tone, selectedPhoneme);
                                    prevV = WanaKana.ToRomaji(selectedPhoneme).Last<char>().ToString();

                                    string[] nTails = new[] { $"{cc[i]} {t}", $"{cc[i]} R", $"{cc[i]}{t}", $"{cc[i]} -", $"{cc[i]}-" };
                                    selectedPhoneme = null;
                                    foreach (var tailAlias in nTails) {
                                        if (HasOto(tailAlias, ending.tone)) {
                                            selectedPhoneme = tailAlias;
                                            addedEnding = true;
                                            break;
                                        }
                                    }
                                    if (selectedPhoneme != null) {
                                        TryAddPhoneme(phonemes, ending.tone, selectedPhoneme);
                                    }
                                    continue;
                                }
                            }
                        }
                    }

                    if (selectedPhoneme == null && i == cc.Length - 1 && (ending.IsEndingVCWithOneConsonant || ending.IsEndingVCWithMoreThanOneConsonant)) {
                        string[] possibleEnds = new[] { 
                            $"{cc[i]} {t}", $"{cc[i]} R", $"{cc[i]}{t}", 
                            $"{cc[i]} -", $"{cc[i]}-", cc[i], 
                            $"{ValidateAlias(cc[i])} {t}", $"{ValidateAlias(cc[i])} R", $"{ValidateAlias(cc[i])}{t}",
                            $"{ValidateAlias(cc[i])} -", $"{ValidateAlias(cc[i])}-", ValidateAlias(cc[i])
                        };
                        foreach (var endAlias in possibleEnds) {
                            if (HasOto(endAlias, ending.tone)) {
                                selectedPhoneme = endAlias;
                                loopStep = 0;
                                addedEnding = true;
                                break;
                            }
                        }
                    }

                    if (selectedPhoneme == null && i < cc.Length - 1) {
                        var extendedSpace1 = $"{cc[i]} {string.Join("", cc.Skip(i + 1))}";
                        var extendedSpace2 = $"{cc[i]} {string.Join(" ", cc.Skip(i + 1))}";
                        var extendedNoSpace = $"{cc[i]}{string.Join("", cc.Skip(i + 1))}";

                        if (HasOto(extendedSpace1, ending.tone)) { selectedPhoneme = extendedSpace1; loopStep = cc.Length - 1 - i; }
                        else if (HasOto(extendedSpace2, ending.tone)) { selectedPhoneme = extendedSpace2; loopStep = cc.Length - 1 - i; }
                        else if (HasOto(extendedNoSpace, ending.tone)) { selectedPhoneme = extendedNoSpace; loopStep = cc.Length - 1 - i; }
                        else if (HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) { selectedPhoneme = $"{cc[i]} {cc[i + 1]}"; loopStep = 0; }
                        else if (HasOto(ValidateAlias($"{cc[i]} {cc[i + 1]}"), ending.tone)) { selectedPhoneme = ValidateAlias($"{cc[i]} {cc[i + 1]}"); loopStep = 0; }
                        else if (HasOto($"{cc[i]}{cc[i + 1]}", ending.tone)) { selectedPhoneme = $"{cc[i]}{cc[i + 1]}"; loopStep = 0; }
                        else if (HasOto(ValidateAlias($"{cc[i]}{cc[i + 1]}"), ending.tone)) { selectedPhoneme = ValidateAlias($"{cc[i]}{cc[i + 1]}"); loopStep = 0; }
                    }

                    bool skipSingular = (usingVC && i == start);

                    if (selectedPhoneme == null && !skipSingular) {
                        if (HasOto(cc[i], ending.tone)) { selectedPhoneme = cc[i]; loopStep = 0; }
                        else if (HasOto(ValidateAlias(cc[i]), ending.tone)) { selectedPhoneme = ValidateAlias(cc[i]); loopStep = 0; }
                    }

                    if (selectedPhoneme != null) {
                        TryAddPhoneme(phonemes, ending.tone, selectedPhoneme);
                        prevV = WanaKana.ToRomaji(selectedPhoneme).Last<char>().ToString();
                        i += loopStep;
                    } else {
                        if (skipSingular) {
                            continue;
                        }

                        var hiragana = ToHiragana(cc[i], ending.tone);
                        var hiraganaVcv = TryVcv(prevV, hiragana, ending.tone);
                        bool blockVcv = usingVC && i == start;

                        if (!blockVcv && HasOto(hiraganaVcv, ending.tone)) {
                            TryAddPhoneme(phonemes, ending.tone, hiraganaVcv);
                            prevV = WanaKana.ToRomaji(hiraganaVcv).Last<char>().ToString();
                            usingVC = true;
                        } else if (HasOto(hiragana, ending.tone)) {
                            TryAddPhoneme(phonemes, ending.tone, hiragana);
                            prevV = WanaKana.ToRomaji(hiragana).Last<char>().ToString();
                        } else {
                            TryAddPhoneme(phonemes, ending.tone, ValidateAlias(hiragana), cc[i], ValidateAlias(cc[i]));
                            prevV = WanaKana.ToRomaji(hiragana).Last<char>().ToString();
                        }
                    }
                }
            }

            if (ending.IsEndingV) {
                TryAddPhoneme(phonemes, ending.tone, $"{prevV} {t}", $"{prevV} R", $"{prevV}{t}",
                $"{ValidateAlias(prevV)} {t}", $"{ValidateAlias(prevV)} R", $"{ValidateAlias(prevV)}{t}");
                
            } else if (!addedEnding && (ending.IsEndingVCWithOneConsonant || ending.IsEndingVCWithMoreThanOneConsonant)) {
                if (cc.Length > 0) { 
                    string lastCC = cc.Last();
                    TryAddPhoneme(phonemes, ending.tone, 
                        $"{lastCC} {t}", $"{lastCC} R", $"{lastCC}{t}",
                        $"{ValidateAlias(lastCC)} {t}", $"{ValidateAlias(lastCC)} R", $"{ValidateAlias(lastCC)}{t}");
                }
            }

            return phonemes;
        }

        private string AliasFormat(string alias, string type, int tone, string prevV) {
            var aliasFormats = new Dictionary<string, string[]> {
                { "dynStart", new string[] { "" } }, { "dynMid", new string[] { "" } },
                { "dynMid_vv", new string[] { "" } }, { "dynEnd", new string[] { "" } },
                { "startingV", new string[] { "-", "- ", "_", "" } }, { "vcEx", new string[] { $"{prevV} ", $"{prevV}" } },
                { "vvExtend", new string[] { "", "_", "-", "- " } }, { "cv", new string[] { "-", "", "- ", "_" } },
                { "cvStart", new string[] { "-", "- ", "_" } }, { "consEn", new string[] { "_", "- ", "_" } },
                { "ending", new string[] { " R", "-", " -" } }, { "ending_mix", new string[] { "-", " -", "R", " R", "_", "--" } },
                { "cc", new string[] { "", "-", "- ", "_" } }, { "cc_start", new string[] { "- ", "-"} },
                { "cc_end", new string[] { " -", "-", "" } }, { "cc_mix", new string[] { " -", " R", "-", "", "_", "- ", "-" } },
                { "cc1_mix", new string[] { "", " -", "-", " R", "_", "- ", "-" } }, { "cc_teto", new string[] { "_", ""} },
                { "cc_teto_end", new string[] { "_", ""} }
            };

            if (!aliasFormats.ContainsKey(type) && !type.Contains("dynamic")) {
                return alias;
            }

            if (type.Contains("dynStart")) {
                string consonant = ""; string vowel = "";
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' '); consonant = parts[0]; vowel = parts[1];
                } else { consonant = alias; }

                var dynamicVariations = new List<string> {
                    $"- {consonant}{vowel}", $"- {consonant} {vowel}", $"-{consonant} {vowel}",
                    $"-{consonant}{vowel}", $"-{consonant}_{vowel}", $"- {consonant}_{vowel}",
                };
                foreach (var variation in dynamicVariations) {
                    if (HasOto(variation, tone) || HasOto(ValidateAlias(variation), tone)) return variation;
                }
            }

            if (type.Contains("dynMid")) {
                string consonant = ""; string vowel = "";
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' '); consonant = parts[0]; vowel = parts[1];
                } else { consonant = alias; }
                
                var dynamicVariations1 = new List<string> {
                    $"{consonant}{vowel}", $"{consonant} {vowel}", $"{consonant}_{vowel}",
                };
                foreach (var variation1 in dynamicVariations1) {
                    if (HasOto(variation1, tone) || HasOto(ValidateAlias(variation1), tone)) return variation1;
                }
            }

            if (type.Contains("dynMid_vv")) {
                string consonant = ""; string vowel = "";
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' '); consonant = parts[0]; vowel = parts[1];
                } else { consonant = alias; }
                
                var dynamicVariations1 = new List<string> {
                    $"{consonant} {vowel}", $"{consonant}{vowel}", $"{consonant}_{vowel}",
                };
                foreach (var variation1 in dynamicVariations1) {
                    if (HasOto(variation1, tone) || HasOto(ValidateAlias(variation1), tone)) return variation1;
                }
            }

            if (type.Contains("dynEnd")) {
                string consonant = ""; string vowel = "";
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' '); consonant = parts[1]; vowel = parts[0];
                } else { consonant = alias; }
                
                var dynamicVariations1 = new List<string> {
                    $"{vowel}{consonant} -", $"{vowel} {consonant}-", $"{vowel}{consonant}-", $"{vowel} {consonant} -",
                };
                foreach (var variation1 in dynamicVariations1) {
                    if (HasOto(variation1, tone) || HasOto(ValidateAlias(variation1), tone)) return variation1;
                }
            }

            var formatsToTry = aliasFormats[type];
            int counter = 0;
            foreach (var format in formatsToTry) {
                string aliasFormat;
                if (type.Contains("mix") && counter < 4) {
                    aliasFormat = (counter % 2 == 0) ? $"{alias}{format}" : $"{format}{alias}";
                    counter++;
                } else if (type.Contains("end") && !(type.Contains("dynEnd"))) {
                    aliasFormat = $"{alias}{format}";
                } else {
                    aliasFormat = $"{format}{alias}";
                }
                
                if (HasOto(aliasFormat, tone) || HasOto(ValidateAlias(aliasFormat), tone)) {
                    return aliasFormat;
                }
            }
            return alias;
        }

        protected override string ValidateAlias(string alias, int tone = 0) {
            if (HasOto(alias, tone)) return alias;

            string baseResolved = base.ValidateAlias(alias, tone);
            if (!string.IsNullOrEmpty(baseResolved) && baseResolved != alias) {
                if (HasOto(baseResolved, tone)) {
                    return baseResolved;
                }
                alias = baseResolved;
            }

            if (alias == "a dx") return alias.Replace("dx", "r");
            if (alias == "e dx") return alias.Replace("dx", "r");
            if (alias == "i dx") return alias.Replace("dx", "r");
            if (alias == "o dx") return alias.Replace("dx", "r");
            if (alias == "u dx") return alias.Replace("dx", "r");

            bool ccSpecific = true;
            if (ccSpecific) {
                foreach (var c1 in new[] { "ng" }) {
                    foreach (var c2 in GetConsonants()) {
                        alias = alias.Replace(c1 + " " + c2, "n" + " " + c2);
                    }
                }
                foreach (var c2 in GetConsonants()) {
                    if (!(alias.Contains($"aw {c2}") || alias.Contains($"ew {c2}") || alias.Contains($"ow {c2}") || alias.Contains($"uw {c2}"))) {
                        alias = alias.Replace($"r {c2}", $"er {c2}");
                    }
                }
                foreach (var c2 in GetConsonants()) {
                    if (!(alias.Contains($"aw {c2}") || alias.Contains($"ew {c2}") || alias.Contains($"ow {c2}") || alias.Contains($"uw {c2}"))) {
                        alias = alias.Replace($"{c2} r", $"{c2} er");
                    }
                }
                foreach (var c2 in GetConsonants()) {
                    if (!(alias.Contains($"aw {c2}") || alias.Contains($"ew {c2}") || alias.Contains($"iw {c2}") || alias.Contains($"ow {c2}") || alias.Contains($"uw {c2}"))) {
                        alias = alias.Replace($"w {c2}", $"uw {c2}");
                    }
                }
                foreach (var c2 in GetConsonants()) {
                    if (!(alias.Contains($"aw {c2}") || alias.Contains($"ew {c2}") || alias.Contains($"iw {c2}") || alias.Contains($"ow {c2}") || alias.Contains($"uw {c2}"))) {
                        alias = alias.Replace($"{c2} w", $"{c2} uw");
                    }
                }
                if (alias == "w -") return alias.Replace("w", "uw");

                foreach (var c2 in GetConsonants()) {
                    if (!(alias.Contains($"ay {c2}") || alias.Contains($"ey {c2}") || alias.Contains($"iy {c2}") || alias.Contains($"oy {c2}"))) {
                        alias = alias.Replace($"y {c2}", $"i {c2}");
                    }
                }
                foreach (var c2 in GetConsonants()) {
                    if (!(alias.Contains($"ay {c2}") || alias.Contains($"ey {c2}") || alias.Contains($"iy {c2}") || alias.Contains($"oy {c2}"))) {
                        alias = alias.Replace($"{c2} y", $"{c2} y");
                    }
                }
                if (alias == "y -") return alias.Replace("y", "iy");
                
                foreach (var c2 in GetConsonants()) {
                    if (!(alias.Contains($"ay {c2}") || alias.Contains($"ey {c2}") || alias.Contains($"iy {c2}") || alias.Contains($"oy {c2}"))) {
                        alias = alias.Replace($"{c2} R", $"{c2} -");
                    }
                }
                foreach (var c2 in GetVowels()) {
                    alias = alias.Replace($"{c2} -", $"{c2} R");
                }
            }

            return alias;
        }

        private (bool, string[], int, bool) HasVc(string vowel, string[] cc, string nextV, string t, int tone) {
            if (string.IsNullOrEmpty(vowel) || vowel == "-") {
                return (false, new string[0], 0, false);
            }

            var phonemes = new List<string>();

            string safeVowel = vowel.Replace("_", "").Replace("-", "").Trim();
            string romaji = WanaKana.ToRomaji(safeVowel);
            char lastVowel = romaji.LastOrDefault(c => "aeiouAEIOU".Contains(c));
            string jpVowel = lastVowel != '\0' ? lastVowel.ToString().ToLower() : safeVowel;

            var singleRules = yamlFallbacks.Where(r => r.FromList.Count == 1).ToList();
            foreach (var rule in singleRules) {
                if (rule.FromList[0] == jpVowel && rule.ToList.Count > 0) {
                    jpVowel = rule.ToList[0];
                    break;
                }
                if (rule.FromList[0] == safeVowel && rule.ToList.Count > 0) {
                    jpVowel = rule.ToList[0];
                    break;
                }
            }

            var vowelsToTry = new HashSet<string> { vowel, jpVowel, ValidateAlias(vowel), ValidateAlias(jpVowel) }
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            string c1 = cc.Length > 0 ? cc[0] : "";
            string c1Val = ValidateAlias(c1);

            string c1Mapped = c1;
            foreach (var rule in singleRules) {
                if (rule.FromList[0] == c1 && rule.ToList.Count > 0) {
                    c1Mapped = rule.ToList[0];
                    break;
                }
            }

            var c1Candidates = new HashSet<string> { c1, c1Mapped, c1Val, ValidateAlias(c1Mapped) }
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            // VCC formats (v cc, vc c, vcc)
            if (cc.Length > 1) {
                string c2 = cc[1];
                string c2Val = ValidateAlias(c2);
                string c2Mapped = c2;
                foreach (var rule in singleRules) {
                    if (rule.FromList[0] == c2 && rule.ToList.Count > 0) {
                        c2Mapped = rule.ToList[0];
                        break;
                    }
                }
                var c2Candidates = new HashSet<string> { c2, c2Mapped, c2Val, ValidateAlias(c2Mapped) }
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();

                // Tail endings FIRST (Returns 2 consumed, isTail = true)
                if (!string.IsNullOrEmpty(t)) {
                    foreach (var v in vowelsToTry) {
                        foreach (var cA in c1Candidates) {
                            foreach (var cB in c2Candidates) {
                                var formats = new[] {
                                    $"{v} {cA}{cB} {t}", $"{v} {cA} {cB} {t}", $"{v}{cA} {cB} {t}", $"{v}{cA}{cB} {t}",
                                    $"{v} {cA}{cB}{t}", $"{v} {cA} {cB}{t}", $"{v}{cA} {cB}{t}", $"{v}{cA}{cB}{t}"
                                };
                                foreach (var format in formats) {
                                    if (HasOto(format, tone) || HasOto(ValidateAlias(format, tone), tone)) {
                                        phonemes.Add(HasOto(format, tone) ? format : ValidateAlias(format, tone));
                                        return (true, phonemes.ToArray(), 2, true);
                                    }
                                }
                            }
                        }
                    }
                }

                // Standard VCC formats (Returns 1 consumed, isTail = false)
                foreach (var v in vowelsToTry) {
                    foreach (var cA in c1Candidates) {
                        foreach (var cB in c2Candidates) {
                            var formats = new[] {
                                $"{v} {cA}{cB}", $"{v} {cA} {cB}", $"{v}{cA} {cB}", $"{v}{cA}{cB}"
                            };
                            foreach (var format in formats) {
                                if (HasOto(format, tone) || HasOto(ValidateAlias(format, tone), tone)) {
                                    phonemes.Add(HasOto(format, tone) ? format : ValidateAlias(format, tone));
                                    return (true, phonemes.ToArray(), 1, false);
                                }
                            }
                        }
                    }
                }
            }

            // Standard VC formats (v c, vc)
            if (cc.Length > 0) {
                if (c1 == "n") {
                    c1Candidates.Add("ん");
                    c1Candidates.Add("N");
                    c1Candidates.Add("n");
                }

                // Standard formats (Returns 0 consumed, isTail = false, or 1 if full mora ん matched)
                foreach (var v in vowelsToTry) {
                    foreach (var cA in c1Candidates) {
                        var formats = new[] {
                            $"{v} {cA}", $"{v}{cA}"
                        };
                        foreach (var format in formats) {
                            if (HasOto(format, tone) || HasOto(ValidateAlias(format, tone), tone)) {
                                phonemes.Add(HasOto(format, tone) ? format : ValidateAlias(format, tone));
                                bool consumedMora = (cA == "ん" || cA == "N");
                                return (true, phonemes.ToArray(), consumedMora ? 1 : 0, false);
                            }
                        }
                    }
                }

                // PRIORITIZE Tail endings FIRST (Returns 1 consumed, isTail = true)
                if (!string.IsNullOrEmpty(t)) {
                    foreach (var v in vowelsToTry) {
                        foreach (var cA in c1Candidates) {
                            var formats = new[] {
                                $"{v} {cA} {t}", $"{v}{cA} {t}",
                                $"{v} {cA}{t}", $"{v}{cA}{t}"
                            };
                            foreach (var format in formats) {
                                if (HasOto(format, tone) || HasOto(ValidateAlias(format, tone), tone)) {
                                    phonemes.Add(HasOto(format, tone) ? format : ValidateAlias(format, tone));
                                    return (true, phonemes.ToArray(), 1, true);
                                }
                            }
                        }
                    }
                }
            }
            return (false, new string[0], 0, false);
        }

        private string TryVcv(string vowel, string cv, int tone) {
            string safeVowel = vowel.Replace("_", "").Replace("-", "").Trim();
            string romaji = WanaKana.ToRomaji(safeVowel);
            char lastVowel = romaji.LastOrDefault(c => "aeiouAEIOU".Contains(c));
            string jpVowel = lastVowel != '\0' ? lastVowel.ToString().ToLower() : safeVowel;

            if (vowel == "-") {
                jpVowel = "-";
            }

            var singleRules = yamlFallbacks.Where(r => r.FromList.Count == 1).ToList();
    
            foreach (var rule in singleRules) {
                if (rule.FromList[0] == jpVowel && rule.ToList.Count > 0) {
                    jpVowel = rule.ToList[0];
                    break;
                }
            }

            foreach (var rule in singleRules) {
                if (rule.FromList[0] == safeVowel && rule.ToList.Count > 0) {
                    jpVowel = rule.ToList[0];
                    break;
                }
            }

            var vcv = $"{jpVowel} {cv}";
            var vcvNoSpace = $"{jpVowel}{cv}";

            if (HasOto(vcv, tone)) return vcv;
            if (HasOto(vcvNoSpace, tone)) return vcvNoSpace;

            var validatedVcv = ValidateAlias(vcv);
            if (HasOto(validatedVcv, tone)) return validatedVcv;
            return cv; 
        }

        private string FixCv(string cv, int tone) {
            var alt = $"- {cv}";
            var altNoSpace = $"-{cv}";

            if (HasOto(cv, tone)) { return cv; } 
            else if (HasOto(ValidateAlias(cv), tone)) { return ValidateAlias(cv); } 
            else if (HasOto(alt, tone)) { return alt; } 
            else if (HasOto(ValidateAlias(alt), tone)) { return ValidateAlias(alt); } 
            else if (HasOto(altNoSpace, tone)) { return altNoSpace; } 
            else if (HasOto(ValidateAlias(altNoSpace), tone)) { return ValidateAlias(altNoSpace); }
            
            return cv;
        }

        private string ToHiragana(string alias, int tone) {
            string fallbackAlias = alias;
            var singleRules = yamlFallbacks.Where(r => r.FromList.Count == 1).ToList();
            
            // Romaji Fallbacks
            foreach (var rule in singleRules) {
                string fromKey = rule.FromList[0];
                string toValue = rule.ToList.Count > 0 ? rule.ToList[0] : fromKey;

                if (fallbackAlias == fromKey) {
                    fallbackAlias = toValue;
                    break;
                } 
                else if (fallbackAlias.EndsWith(fromKey) && fromKey != toValue) {
                    fallbackAlias = fallbackAlias.Substring(0, fallbackAlias.Length - fromKey.Length) + toValue;
                    break;
                }
            }

            var convertedHiragana = "";
            int i = 0;

            // WanaKana Dictionary Lookup Loop
            while (i < fallbackAlias.Length) {
                bool foundMatch = false;

                var potentialRomajiKeys = WanaKanaDictionary.Keys
                    .Where(key => fallbackAlias.Length >= i + key.Length &&
                                fallbackAlias.Substring(i, key.Length).Equals(key, StringComparison.Ordinal))
                    .OrderByDescending(key => key.Length)
                    .ToList();

                foreach (var romajiKey in potentialRomajiKeys) {
                    var kanaValues = WanaKanaDictionary[romajiKey];
                    
                    foreach (var kana in kanaValues) {
                        bool isMatch = HasOto(kana, tone) || HasOto(ValidateAlias(kana, tone), tone);
                        
                        // Pure VCV Probe
                        if (!isMatch) {
                            string[] probes = { $"- {kana}", $"-{kana}", $"a {kana}", $"a{kana}", $"e {kana}", $"e{kana}" };
                            foreach (var probe in probes) {
                                if (HasOto(probe, tone) || HasOto(ValidateAlias(probe, tone), tone)) {
                                    isMatch = true;
                                    break;
                                }
                            }
                        }

                        if (isMatch) {
                            convertedHiragana += kana;
                            i += romajiKey.Length;
                            foundMatch = true;
                            break;
                        }
                    }
                    if (foundMatch) break;
                }

                if (!foundMatch && potentialRomajiKeys.Count > 0) {
                    // Fall back to the very first item (top of YAML list)
                    convertedHiragana += WanaKanaDictionary[potentialRomajiKeys[0]].FirstOrDefault() ?? fallbackAlias[i].ToString();
                    i += potentialRomajiKeys[0].Length;
                    foundMatch = true;
                }

                if (!foundMatch) {
                    convertedHiragana += fallbackAlias[i];
                    i++;
                }
            }
            foreach (var rule in singleRules) {
                string fromKey = rule.FromList[0];
                string toValue = rule.ToList.Count > 0 ? rule.ToList[0] : fromKey;

                if (fromKey.Any(c => c > 0xFF)) {
                    convertedHiragana = convertedHiragana.Replace(fromKey, toValue);
                }
            }

            return convertedHiragana;
        }

        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            double otoLength = GetTransitionBasicLengthMsByOto(alias, tone, attr);
            var parts = alias.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool isVcv = false;

            if (parts.Length == 2) {
                var startingVowels = GetVowels().ToList();
                startingVowels.Add("n");
                startingVowels.Add("N");
                
                if (startingVowels.Contains(parts[0])) {
                    string cv = parts[1];
                    bool isJapaneseVcv = cv.Any(c => c > 0xFF);
                    string cleanCv = new string(cv.TakeWhile(c => !char.IsDigit(c) && c != '_' && c != '#').ToArray());
                    bool isRomajiVcv = false;
                    foreach (var v in GetVowels()) {
                        if (cleanCv.EndsWith(v)) {
                            isRomajiVcv = true;
                            break;
                        }
                    }
                    if (isRomajiVcv || isJapaneseVcv) {
                        isVcv = true;
                    }
                }
            }

            if (isVcv) {
                return GetTransitionBasicLengthMsByConstant() * 1.3;
            }
            return otoLength;
        }
    }
}