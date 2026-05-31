using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using Classic;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;
using YamlDotNet.Core.Tokens;
using System.Text.RegularExpressions;
using OpenUtau.Core;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Filipino Phonemizer", "FIL VCV & CVVC", "Cadlaxa", language: "FIL")]
    public class FilipinoPhonemizer : SyllableBasedPhonemizer {
        protected override string YamlFileName => "filipino.yaml";
        protected override byte[] YamlTemplate => Data.Resources.filipino_template;
        public FilipinoPhonemizer() {
            this.vowels = new string[] {
                "a", "e", "i", "o", "u", "ay", "ey", "oy", "uy", "aw", "ew", "ow", "iw"
            };
            this.consonants = Array.Empty<string>();
        }
    
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "";
    
        List<string> consExceptions = new List<string>();

        string[] diphthongs = new[] { "ay", "ey", "oy", "uy", "aw", "ew", "ow", "iw"  };

        // For banks with missing vowels
        private readonly Dictionary<string, string> missingVphonemes = "ax=a".Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isMissingVPhonemes = false;
        private bool isYamlFallbacks = false;


        // For banks with missing custom consonants
        private readonly Dictionary<string, string> missingCphonemes = "N=n".Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isMissingCPhonemes = false;
        private bool cPV_FallBack = false;

        private readonly Dictionary<string, string> vvDiphthongExceptions =
            new Dictionary<string, string>() {
                {"aw","a"},
                {"ow","o"},
                {"iw","i"},
                {"ay","a"},
                {"ey","e"},
                {"oy","o"},
                {"uy","u"},
                {"ew","e"},
            };
        
        private readonly Dictionary<string, string> vvExceptions =
            new Dictionary<string, string>() {
                {"aw","w"},
                {"ow","w"},
                {"iw","w"},
                {"ay","y"},
                {"ey","y"},
                {"oy","y"},
                {"uy","y"},
                {"ew","w"},
            };

        private readonly string[] ccvException = { "ch", "dh", "dx", "fh", "gh", "hh", "jh", "kh", "ph", "ng", "sh", "th", "vh", "wh", "zh" };
        private readonly string[] RomajiException = { "a", "e", "i", "o", "u" };
        private static readonly string[] FinalConsonants = { "w", "y", "r", "l", "m", "n", "ng" };


        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                return note.phoneticHint.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);
            }

            if (original == null || original.Length == 0) {
                string lyric = note.lyric.ToLowerInvariant();
                List<string> fallbackSplit = new List<string>();
                string[] vowels = GetVowels();
                string[] consonants = GetConsonants();

                // Handle apostrophes at the start or end
                bool hasLeadingApostrophe = lyric.StartsWith("'");
                bool hasTrailingApostrophe = lyric.EndsWith("'");

                if (hasLeadingApostrophe) {
                    lyric = lyric.Substring(1);
                }
                if (hasTrailingApostrophe && lyric.Length > 0) {
                    lyric = lyric.Substring(0, lyric.Length - 1);
                }

                int ii = 0;
                while (ii < lyric.Length) {
                    string match = null;
                    foreach (var cons in consonants.OrderByDescending(c => c.Length)) {
                        if (lyric.Substring(ii).StartsWith(cons)) {
                            match = cons;
                            break;
                        }
                    }
                    if (match == null) {
                        foreach (var vow in vowels.OrderByDescending(v => v.Length)) {
                            if (lyric.Substring(ii).StartsWith(vow)) {
                                match = vow;
                                break;
                            }
                        }
                    }
                    if (match != null) {
                        fallbackSplit.Add(match);
                        ii += match.Length;
                    } else {
                        fallbackSplit.Add(lyric[ii].ToString());
                        ii++;
                    }
                }
                // Add "q" at the beginning or end if needed
                if (hasLeadingApostrophe) {
                    fallbackSplit.Insert(0, "q");
                }
                if (hasTrailingApostrophe) {
                    fallbackSplit.Add("q");
                    
                }
                original = fallbackSplit.ToArray();
            }
            
            List<string> modified = new List<string>(original);
            List<string> finalPhonemes = new List<string>();
            finalPhonemes = new List<string>(modified);
            List<string> finalProcessedPhonemes = new List<string>();
            IEnumerable<string> phonemes;
            phonemes = finalPhonemes;
            foreach (string s in phonemes) {
                switch (s) {
                    default:
                        finalProcessedPhonemes.Add(s);
                        break;
                }
            }
            return finalProcessedPhonemes.ToArray();
        }

        protected override IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();
            // LOAD DICTIONARY FROM FOLDER
            string path = Path.Combine(PluginDir, YamlFileName);
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, YamlTemplate);
            }
            // LOAD DICTIONARY FROM SINGER FOLDER
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
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());
            //g2ps.Add(new ArpabetPlusG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }
        public override void SetSinger(USinger singer) {
            base.SetSinger(singer);

            if (this.singer != null && this.singer.Loaded) {
                
                consExceptions.Clear();
                if (stop != null) consExceptions.AddRange(stop);
                if (tap != null) consExceptions.AddRange(tap);
                
                consExceptions = consExceptions.Distinct().ToList();
            }
        }

        // prioritize yaml replacements over dictionary replacements
        private string ReplacePhoneme(string phoneme, int tone) {
            // If the original phoneme has an OTO, use it directly.
            if (HasOto(phoneme, tone) || HasOto(ValidateAlias(phoneme), tone)) {
                return phoneme;
            }
            // Otherwise, try to apply the dictionary replacement.
            if (dictionaryReplacements.TryGetValue(phoneme, out var replaced)) {
                return replaced;
            }
            return phoneme;
        }
        protected override List<string> ProcessSyllable(Syllable syllable) {
            syllable.prevV = tails.Contains(syllable.prevV) ? "" : syllable.prevV;
            var replacedPrevV = ReplacePhoneme(syllable.prevV, syllable.tone);
            var prevV = string.IsNullOrEmpty(replacedPrevV) ? "" : replacedPrevV;
            string[] cc = syllable.cc.Select(c => ReplacePhoneme(c, syllable.tone)).ToArray();
            string v = ReplacePhoneme(syllable.v, syllable.vowelTone);
            List<string> vowels = new List<string> { v };
            string basePhoneme;
            var phonemes = new List<string>();
            var lastC = cc.Length - 1;
            var firstC = 0;
            string[] CurrentWordCc = syllable.CurrentWordCc.Select(c => ReplacePhoneme(c, syllable.tone)).ToArray();
            string[] PreviousWordCc = syllable.PreviousWordCc.Select(c => ReplacePhoneme(c, syllable.tone)).ToArray();
            int prevWordConsonantsCount = syllable.prevWordConsonantsCount;

            // Check for missing vowel phonemes
            foreach (var entry in missingVphonemes) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Key, syllable.tone)) {
                    isMissingVPhonemes = true;
                    break;
                }
            }

            // Check for missing consonant phonemes
            foreach (var entry in missingCphonemes) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Value, syllable.tone)) {
                    isMissingCPhonemes = true;
                    break;
                }
            }

            foreach (var entry in yamlFallbacks) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Value, syllable.tone)) {
                    isYamlFallbacks = true;
                    break;
                }
            }

            // STARTING V
            if (syllable.IsStartingV) {
                basePhoneme = AliasFormat(v, "startingV", syllable.vowelTone, "");
            }
            // [V V] or [V C][C V]/[V]
            else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    basePhoneme = $"{prevV} {v}";
                    if (!HasOto(basePhoneme, syllable.vowelTone) && !HasOto(ValidateAlias(basePhoneme), syllable.vowelTone) && vvExceptions.ContainsKey(prevV) && prevV != v) {
                        // VV IS NOT PRESENT, CHECKS VVEXCEPTIONS LOGIC
                        //var vc = $"{prevV}{vvExceptions[prevV]}";
                        var vc = AliasFormat($"{vvExceptions[prevV]}", "vcEx", syllable.vowelTone, prevV);
                        TryAddPhoneme(phonemes, syllable.vowelTone, vc);
                        basePhoneme = AliasFormat($"{vvExceptions[prevV]} {v}", "dynMid", syllable.vowelTone, "");
                    } else {
                        {
                            if (HasOto($"{prevV} {v}", syllable.vowelTone) || HasOto(ValidateAlias($"{prevV} {v}"), syllable.vowelTone)) {
                                basePhoneme = $"{prevV} {v}";
                            } else if (HasOto($"{prevV}{v}", syllable.vowelTone) || HasOto(ValidateAlias($"{prevV}{v}"), syllable.vowelTone)) {
                                basePhoneme = $"{prevV}{v}";
                            } else if (HasOto(v, syllable.vowelTone) || HasOto(ValidateAlias(v), syllable.vowelTone)) {
                                basePhoneme = v;
                            } else {
                                basePhoneme = AliasFormat($"- {v}", "dynMid", syllable.vowelTone, "");
                                TryAddPhoneme(phonemes, syllable.vowelTone, AliasFormat($"{prevV} -", "dynMid", syllable.vowelTone, ""));
                            }
                        }
                    }
                    // EXTEND AS [V]
                } else if (HasOto($"{v}", syllable.vowelTone) && HasOto(ValidateAlias($"{v}"), syllable.vowelTone) || missingVphonemes.ContainsKey(prevV)) {
                    basePhoneme = v;
                } else if (!HasOto(v, syllable.vowelTone) && !HasOto(ValidateAlias(v), syllable.vowelTone) && vvDiphthongExceptions.ContainsKey(prevV)) {
                    basePhoneme = $"{vvDiphthongExceptions[prevV]} {vvDiphthongExceptions[prevV]}";
                } else {
                    // PREVIOUS ALIAS WILL EXTEND as [V V]
                    basePhoneme = null;
                }

                // [- CV/C V] or [- C][CV/C V]
            } else if (syllable.IsStartingCVWithOneConsonant) {
                var rcv = $"- {cc[0]} {v}";
                var rcv1 = $"- {cc[0]}{v}";
                var crv = $"{cc[0]} {v}";
                /// - CV
                if (HasOto(rcv, syllable.vowelTone) && HasOto(ValidateAlias(rcv), syllable.vowelTone) || (HasOto(rcv1, syllable.vowelTone) && HasOto(ValidateAlias(rcv1), syllable.vowelTone))) {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynStart", syllable.vowelTone, "");
                    /// CV
                } else if (HasOto(crv, syllable.vowelTone) && HasOto(ValidateAlias(crv), syllable.vowelTone)) {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynMid", syllable.vowelTone, "");
                    TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""));
                } else {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynMid", syllable.vowelTone, "");
                    TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""));
                }
                // [CCV/CC V] or [C C] + [CV/C V]
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                // TRY [- CCV]/[- CC V] or [- CC][CCV]/[CC V] or [- C][C C][C V]/[CV]
                var rccv = $"- {string.Join("", cc)} {v}";
                var rccv1 = $"- {string.Join("", cc)}{v}";
                var crv = $"{cc.Last()} {v}";
                var crv1 = $"{cc.Last()}{v}";
                var ccv = $"{string.Join("", cc)} {v}";
                var ccv1 = $"{string.Join("", cc)}{v}";
                /// - CCV
                if (HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone) || HasOto(rccv1, syllable.vowelTone) || HasOto(ValidateAlias(rccv1), syllable.vowelTone) && !ccvException.Contains(cc[0])) {
                    basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynStart", syllable.vowelTone, "");
                    lastC = 0;
                } else {
                    /// CCV and CV
                    if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1), syllable.vowelTone)) {
                        basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynMid", syllable.vowelTone, "");
                        lastC = 0;
                    } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone) || HasOto(crv1, syllable.vowelTone) || HasOto(ValidateAlias(crv1), syllable.vowelTone)) {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    } else {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    }
                    // TRY RCC [- CC]
                    for (var i = cc.Length; i > 1; i--) {
                        if (!ccvException.Contains(cc[0])) {
                            if (TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{string.Join("", cc.Take(i))}", "cc_start", syllable.vowelTone, ""))) {
                                firstC = i - 1;
                            }
                        }
                        break;
                    }
                    // [- C]
                    if (phonemes.Count == 0) {
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""));
                    }
                    // try [CC V] or [CCV]
                    var cv = $"{cc.Last()}{v}";
                    for (var i = firstC; i < cc.Length - 1; i++) {
                        /// CCV
                        if (CurrentWordCc.Length >= 2) {
                            if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1), syllable.vowelTone)) {
                                basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynMid", syllable.vowelTone, "");
                                lastC = i;
                                break;
                            }
                            /// C-Last
                        } else if (CurrentWordCc.Length == 1 && PreviousWordCc.Length == 1) {
                            if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone) || HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv), syllable.vowelTone)) {
                                basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                            } else {
                                basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                            }
                        }
                    }
                }
            } else { // VCV
                var vcv = $"{prevV} {cc[0]}{v}";
                var vcv2 = $"{prevV}{cc[0]}{v}";
                var vcvEnd = $"{prevV}{cc[0]} {v}";
                var vccv = $"{prevV} {string.Join("", cc)}{v}";
                var vccv2 = $"{prevV} {string.Join("", cc)}";
                var vccv3 = $"{prevV}{string.Join("", cc)}";
                var crv = $"{cc.Last()} {v}";
                // Use regular VCV if the current word starts with one consonant and the previous word ends with none
                if (syllable.IsVCVWithOneConsonant && (HasOto(vcv, syllable.vowelTone) && HasOto(ValidateAlias(vcv), syllable.vowelTone)) && prevWordConsonantsCount == 0 && CurrentWordCc.Length == 1) {
                    basePhoneme = vcv;
                } else if (syllable.IsVCVWithOneConsonant && (HasOto(vcv2, syllable.vowelTone) && HasOto(ValidateAlias(vcv2), syllable.vowelTone)) && prevWordConsonantsCount == 0 && CurrentWordCc.Length == 1) {
                    basePhoneme = vcv2;
                    // Use end VCV if current word does not start with a consonant but the previous word does end with one
                } else if (syllable.IsVCVWithOneConsonant && prevWordConsonantsCount == 1 && CurrentWordCc.Length == 0 && (HasOto(vcvEnd, syllable.vowelTone) && HasOto(ValidateAlias(vcvEnd), syllable.vowelTone))) {
                    basePhoneme = vcvEnd;
                    // Use regular VCV if end VCV does not exist
                } else if (syllable.IsVCVWithOneConsonant && (!HasOto(vcvEnd, syllable.vowelTone) && !HasOto(ValidateAlias(vcvEnd), syllable.vowelTone)) && (HasOto(vcv, syllable.vowelTone) && HasOto(ValidateAlias(vcv), syllable.vowelTone))) {
                    basePhoneme = vcv;
                    // VCV with multiple consonants, only for current word onset and null previous word ending
                } else if (syllable.IsVCVWithMoreThanOneConsonant && (HasOto(vccv, syllable.vowelTone) && HasOto(ValidateAlias(vccv), syllable.vowelTone)) && prevWordConsonantsCount == 0) {
                    basePhoneme = vccv;
                    lastC = 0;
                } else if (syllable.IsVCVWithMoreThanOneConsonant && (HasOto(vccv3, syllable.vowelTone) && HasOto(ValidateAlias(vccv3), syllable.vowelTone))) {
                    basePhoneme = AliasFormat($"{prevV} {string.Join("", cc)}{v}", "dynMid", syllable.vowelTone, "");
                    lastC = 0;
                } else {
                    var cv = cc.Last() + v;
                    basePhoneme = cv;
                    if ((!HasOto(cv, syllable.vowelTone) && !HasOto(ValidateAlias(cv), syllable.vowelTone)) && (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone))) {
                        basePhoneme = crv;
                    }
                    // try [CC V] or [CCV]
                    for (var i = firstC; i < cc.Length - 1; i++) {
                        var ccv = $"{string.Join("", cc)} {v}";
                        var ccv1 = $"{string.Join("", cc)}{v}";
                        /// CCV
                        if (CurrentWordCc.Length >= 2) {
                            if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1), syllable.vowelTone)) {
                                basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynMid", syllable.vowelTone, "");
                                lastC = i;
                                break;
                            }
                            /// C-Last
                        } else if (CurrentWordCc.Length == 1 && PreviousWordCc.Length == 1) {
                            if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone) || HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv), syllable.vowelTone)) {
                                basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                            } else {
                                basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                            }
                        }
                    }
                    // try [V C], [V CC], [VC C], [V -][- C]
                    for (var i = lastC + 1; i >= 0; i--) {
                        var vr = $"{prevV} -";
                        //var vc_c = $"{prevV}{string.Join(" ", cc.Take(2))}-";
                        //var vc_c2 = $"{prevV}{string.Join(" ", cc.Take(2))}_";
                        var vcc = $"{prevV} {string.Join("", cc.Take(2))}";
                        var vc = $"{prevV} {cc[0]}";
                        // Boolean Triggers
                        bool CCV = false;
                        if (CurrentWordCc.Length >= 2 && !ccvException.Contains(cc[0])) {
                            if (HasOto(AliasFormat($"{string.Join("", cc)} {v}", "dynMid", syllable.vowelTone, ""), syllable.vowelTone)) {
                                CCV = true;
                            }
                        }

                        if (i == 0 && (HasOto(vr, syllable.tone) || HasOto(ValidateAlias(vr), syllable.tone)) && !HasOto(vc, syllable.tone)) {
                            TryAddPhoneme(phonemes, syllable.tone, vr, ValidateAlias(vr));
                            TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""));
                            break;
                        } else if ((HasOto(vcc, syllable.tone) || HasOto(ValidateAlias(vcc), syllable.tone)) && CCV) {
                            TryAddPhoneme(phonemes, syllable.tone, vcc, ValidateAlias(vcc));
                            firstC = 1;
                            break;
                            /*} else if (HasOto(vc_c, syllable.tone) && HasOto(ValidateAlias(vc_c), syllable.tone)) {
                                TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{prevV}{string.Join(" ", cc.Take(2))} -", "dynMid", syllable.vowelTone, ""));
                                firstC = 1;
                                break;
                            } else if (HasOto(vc_c2, syllable.tone) && HasOto(ValidateAlias(vc_c2), syllable.tone)) {
                                TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{prevV}{string.Join(" ", cc.Take(2))} _", "dynMid", syllable.vowelTone, ""));
                                firstC = 1;
                                break;*/
                        } else if (cPV_FallBack && (!HasOto(crv, syllable.vowelTone) && !HasOto(ValidateAlias(crv), syllable.vowelTone))) {
                            TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc));
                            break;
                        } else if (HasOto(vc, syllable.tone) || HasOto(ValidateAlias(vc), syllable.tone)) {
                            TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc));
                            break;
                        } else {
                            continue;
                        }
                    }
                }
            }

            for (var i = firstC; i < lastC; i++) {
                    var ccv = $"{string.Join("", cc.Skip(i + 1))} {v}";
                    var ccv1 = $"{string.Join("", cc.Skip(i + 1))}{v}";
                    var cc1 = $"{string.Join(" ", cc.Skip(i))}";
                    var lcv = $"{cc.Last()} {v}";
                    var cv = $"{cc.Last()}{v}";
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    // [C1 C2]
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = $"{cc[i]} {cc[i + 1]}";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    // CC FALLBACKS
                    if (!HasOto(cc1, syllable.tone) || (!HasOto(ValidateAlias(cc1), syllable.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", syllable.tone))) {
                        var c1 = cc[i];
                        var c2 = cc[i + 1];
                        bool c1IsException = consExceptions.Contains(c1);
                        bool c2IsException = consExceptions.Contains(c2);

                        // Scenario 1: Both are NOT exceptions
                        if (!c1IsException && !c2IsException) {
                            // [C1 -] [- C2]
                            cc1 = AliasFormat($"{c2}", "cc_inB", syllable.vowelTone, "");
                            TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{c1}", "cc_endB", syllable.vowelTone, ""));
                        }
                        // Scenario 2: C1 is an exception, C2 is NOT
                        else if (c1IsException && !c2IsException) {
                            cc1 = AliasFormat($"{c2}", "cc_inB", syllable.vowelTone, "");
                        }
                        // Scenario 3: C1 is NOT an exception, C2 is
                        else if (!c1IsException && c2IsException) {
                            cc1 = AliasFormat($"{c1}", "cc_endB", syllable.vowelTone, "");
                        }
                        // Scenario 4: Both are exceptions
                        else if (c1IsException && c2IsException) {
                            cc1 = "";
                        }
                    }
                    // CCV
                    if (CurrentWordCc.Length >= 2) {
                        if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1), syllable.vowelTone) && !ccvException.Contains(cc[0])) {
                            basePhoneme = AliasFormat($"{string.Join("", cc.Skip(i + 1))} {v}", "dynMid", syllable.vowelTone, "");
                            lastC = i;
                        } else if (HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv), syllable.vowelTone) || HasOto(lcv, syllable.vowelTone) || HasOto(ValidateAlias(lcv), syllable.vowelTone) && HasOto(cc1, syllable.vowelTone) && !HasOto(ccv, syllable.vowelTone)) {
                            basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                        }
                        // [C1 C2C3]
                        if (HasOto($"{cc[i]} {string.Join("", cc.Skip(i + 1))}", syllable.tone)) {
                            cc1 = $"{cc[i]} {string.Join("", cc.Skip(i + 1))}";
                        }
                        // CV
                    } else if (CurrentWordCc.Length == 1 && PreviousWordCc.Length == 1) {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                        // [C1 C2]
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = $"{cc[i]} {cc[i + 1]}";
                        }
                    }
                    // C+V
                    if ((HasOto(v, syllable.vowelTone) || HasOto(ValidateAlias(v), syllable.vowelTone)) && (!HasOto(lcv, syllable.vowelTone) && !HasOto(ValidateAlias(lcv), syllable.vowelTone) && (!HasOto(cv, syllable.vowelTone) && !HasOto(ValidateAlias(cv), syllable.vowelTone)))) {
                        cPV_FallBack = true;
                        basePhoneme = v;
                        cc1 = ValidateAlias(cc1);
                    }

                    if (i + 1 < lastC) {
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        // [C1 C2]
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = $"{cc[i]} {cc[i + 1]}";
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        // CC FALLBACKS
                        if (!HasOto(cc1, syllable.tone) || (!HasOto(ValidateAlias(cc1), syllable.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", syllable.tone))) {
                            var c1 = cc[i];
                            var c2 = cc[i + 1];
                            bool c1IsException = consExceptions.Contains(c1);
                            bool c2IsException = consExceptions.Contains(c2);

                            // Scenario 1: Both are NOT exceptions
                            if (!c1IsException && !c2IsException) {
                                // [C1 -] [- C2]
                                cc1 = AliasFormat($"{c2}", "cc_inB", syllable.vowelTone, "");
                                TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{c1}", "cc_endB", syllable.vowelTone, ""));
                            }
                            // Scenario 2: C1 is an exception, C2 is NOT
                            else if (c1IsException && !c2IsException) {
                                cc1 = AliasFormat($"{c2}", "cc_inB", syllable.vowelTone, "");
                            }
                            // Scenario 3: C1 is NOT an exception, C2 is
                            else if (!c1IsException && c2IsException) {
                                cc1 = AliasFormat($"{c1}", "cc_endB", syllable.vowelTone, "");
                            }
                            // Scenario 4: Both are exceptions
                            else if (c1IsException && c2IsException) {
                                cc1 = "";
                            }
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        // CCV
                        if (CurrentWordCc.Length >= 2) {
                            if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1), syllable.vowelTone) && !ccvException.Contains(cc[0])) {
                                basePhoneme = AliasFormat($"{string.Join("", cc.Skip(i + 1))} {v}", "dynMid", syllable.vowelTone, "");
                                lastC = i;
                            } else if (HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv), syllable.vowelTone) || HasOto(lcv, syllable.vowelTone) || HasOto(ValidateAlias(lcv), syllable.vowelTone) && HasOto(cc1, syllable.vowelTone) && !HasOto(ccv, syllable.vowelTone)) {
                                basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                            }
                            // [C1 C2C3]
                            if (HasOto($"{cc[i]} {string.Join("", cc.Skip(i + 1))}", syllable.tone)) {
                                cc1 = $"{cc[i]} {string.Join("", cc.Skip(i + 1))}";
                            }
                            // CV
                        } else if (CurrentWordCc.Length == 1 && PreviousWordCc.Length == 1) {
                            basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                            // [C1 C2]
                            if (!HasOto(cc1, syllable.tone)) {
                                cc1 = $"{cc[i]} {cc[i + 1]}";
                            }
                        }
                        // C+V
                        if ((HasOto(v, syllable.vowelTone) || HasOto(ValidateAlias(v), syllable.vowelTone)) && (!HasOto(lcv, syllable.vowelTone) && !HasOto(ValidateAlias(lcv), syllable.vowelTone) && (!HasOto(cv, syllable.vowelTone) && !HasOto(ValidateAlias(cv), syllable.vowelTone)))) {
                            cPV_FallBack = true;
                            basePhoneme = v;
                            cc1 = ValidateAlias(cc1);
                        }
                        // C+V
                        if ((HasOto(v, syllable.vowelTone) || HasOto(ValidateAlias(v), syllable.vowelTone)) && (!HasOto(lcv, syllable.vowelTone) && !HasOto(ValidateAlias(lcv), syllable.vowelTone) && (!HasOto(cv, syllable.vowelTone) && !HasOto(ValidateAlias(cv), syllable.vowelTone)))) {
                            cPV_FallBack = true;
                            basePhoneme = v;
                            cc1 = ValidateAlias(cc1);
                        }

                        if (HasOto(cc1, syllable.tone) && HasOto(cc1, syllable.tone) && !cc1.Contains($"{string.Join("", cc.Skip(i))}")) {
                            // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                            TryAddPhoneme(phonemes, syllable.vowelTone, cc1);
                        } else if (TryAddPhoneme(phonemes, syllable.tone, cc1)) {
                            // like [V C1] [C1 C2] [C2 ..]
                            if (cc1.Contains($"{string.Join(" ", cc.Skip(i + 1))}")) {
                                i++;
                            }
                        } else {
                            // like [V C1] [C1] [C2 ..]
                            TryAddPhoneme(phonemes, syllable.tone, cc[i], ValidateAlias(cc[i]));
                        }
                    } else {
                        TryAddPhoneme(phonemes, syllable.tone, cc1);
                    }
                }

            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string prevV = ReplacePhoneme(ending.prevV, ending.tone);
            string[] cc = ending.cc.Select(c => ReplacePhoneme(c, ending.tone)).ToArray();
            string v = ReplacePhoneme(ending.prevV, ending.tone);
            var phonemes = new List<string>();
            var lastC = cc.Length - 1;
            var firstC = 0;
            string t = ending.HasTail ? ReplacePhoneme(ending.tail, ending.tone) : "-";
            
            if (ending.IsEndingV) {
                var vR = $"{v} {t}";
                var vR2 = $"{v}{t}";
                if (HasOto(vR, ending.tone) || HasOto(ValidateAlias(vR), ending.tone) || HasOto(vR2, ending.tone) || HasOto(ValidateAlias(vR2), ending.tone)) {
                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{v}", "ending", ending.tone, "", t), ValidateAlias(AliasFormat($"{v}", "ending", ending.tone, "", t)));
                }
            } else if (ending.IsEndingVCWithOneConsonant) {
                var vc = $"{v} {cc[0]}";
                var vcr = $"{v} {cc[0]}{t}";
                var vcr2 = $"{v}{cc[0]} {t}";
                var vcr3 = $"{v}{cc[0]}{t}";
                if (!RomajiException.Contains(cc[0])) {
                    if (HasOto(vcr, ending.tone) && HasOto(ValidateAlias(vcr), ending.tone) || HasOto(vcr2, ending.tone) && HasOto(ValidateAlias(vcr2), ending.tone) || HasOto(vcr3, ending.tone) && HasOto(ValidateAlias(vcr3), ending.tone)) {
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{v} {cc[0]}", "dynEnd", ending.tone, "", t), ValidateAlias(AliasFormat($"{v} {cc[0]}", "dynEnd", ending.tone, "", t)));
                    } else if (!HasOto(vcr, ending.tone) && !HasOto(ValidateAlias(vcr), ending.tone) || !HasOto(vcr2, ending.tone) && HasOto(ValidateAlias(vcr2), ending.tone) || !HasOto(vcr3, ending.tone) && HasOto(ValidateAlias(vcr3), ending.tone)) {
                        TryAddPhoneme(phonemes, ending.tone, vc);
                        if (vc.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "ending", ending.tone, "", t));
                        }
                    } else {
                        TryAddPhoneme(phonemes, ending.tone, vc);
                        if (vc.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "ending", ending.tone, "", t));
                        }
                    }
                }
            } else {
                for (var i = lastC; i >= 0; i--) {
                    var vr = $"{v} {t}";
                    var vr1 = $"{v} R";
                    var vr2 = $"{v}{t}";
                    var vcc = $"{v} {string.Join("", cc.Take(2))}{t}";
                    var vcc2 = $"{v}{string.Join(" ", cc.Take(2))} {t}";
                    var vcc3 = $"{v}{string.Join(" ", cc.Take(2))}";
                    var vcc4 = $"{v} {string.Join("", cc.Take(2))}";
                    var vc = $"{v} {cc[0]}";
                    if (!RomajiException.Contains(cc[0])) {
                        if (i == 0) {
                            if (HasOto(vr, ending.tone) || HasOto(ValidateAlias(vr), ending.tone) || HasOto(vr2, ending.tone) || HasOto(ValidateAlias(vr2), ending.tone) || HasOto(vr1, ending.tone) || HasOto(ValidateAlias(vr1), ending.tone) && !HasOto(vc, ending.tone)) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{v}", "ending", ending.tone, "", t));
                            }
                            break;
                        } else if (HasOto(vcc, ending.tone) && HasOto(ValidateAlias(vcc), ending.tone) && lastC == 1 && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc);
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc2, ending.tone) && HasOto(ValidateAlias(vcc2), ending.tone) && lastC == 1 && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc2);
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc3, ending.tone) && HasOto(ValidateAlias(vcc3), ending.tone) && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc3);
                            if (vcc3.EndsWith(cc.Last()) && lastC == 1) {
                                if (consonants.Contains(cc.Last())) {
                                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc.Last()}", "ending", ending.tone, "", t));
                                }
                            }
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc4, ending.tone) && HasOto(ValidateAlias(vcc4), ending.tone) && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc4);
                            if (vcc4.EndsWith(cc.Last()) && lastC == 1) {
                                if (consonants.Contains(cc.Last())) {
                                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc.Last()}", "ending", ending.tone, "", t));
                                }
                            }
                            firstC = 1;
                            break;
                        } else if (!!HasOto(vcc, ending.tone) && !HasOto(ValidateAlias(vcc), ending.tone)
                                || !HasOto(vcc2, ending.tone) && HasOto(ValidateAlias(vcc2), ending.tone)
                                || !HasOto(vcc3, ending.tone) && HasOto(ValidateAlias(vcc3), ending.tone)
                                || !HasOto(vcc4, ending.tone) && HasOto(ValidateAlias(vcc4), ending.tone)) {
                            TryAddPhoneme(phonemes, ending.tone, vc);
                            break;
                        } else {
                            TryAddPhoneme(phonemes, ending.tone, vc);
                            break;
                        }
                    }
                }
                for (var i = firstC; i < lastC; i++) {
                    var cc1 = $"{cc[i]} {cc[i + 1]}";
                    if (i < cc.Length - 2) {
                        var cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }

                        if (!HasOto(cc2, ending.tone) && !HasOto($"{cc[i + 1]} {cc[i + 2]}", ending.tone)) {
                            // [C1 -] [- C2]
                            cc2 = AliasFormat($"{cc[i + 2]}", "cc_inB", ending.tone, "");
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_endB", ending.tone, "", t));
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (HasOto(cc1, ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}"), ending.tone))) {
                            // like [C1 C2][C2 ...]
                            TryAddPhoneme(phonemes, ending.tone, cc1);
                        } else if ((HasOto(cc[i], ending.tone) || HasOto(ValidateAlias(cc[i]), ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}"), ending.tone)))) {
                            // like [C1 C2-][C3 ...]
                            TryAddPhoneme(phonemes, ending.tone, cc[i]);
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {cc[i + 2]}{t}", ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}"))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            i++;
                        } else if (!HasOto(cc1, ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            // [C1 -] [- C2]
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_inB", ending.tone, "", t));
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_endB", ending.tone, "", t));
                            i++;
                        } else {
                            // like [C1][C2 ...]
                            TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i]), $"{cc[i]} {t}", ValidateAlias($"{cc[i]} {t}"));
                            TryAddPhoneme(phonemes, ending.tone, cc[i + 1], ValidateAlias(cc[i + 1]), $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}"));
                            i++;
                        }
                    } else {
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = $"{cc[i]} {cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        // [C1 -] [- C2]
                        if (!HasOto(cc1, ending.tone) || !HasOto(ValidateAlias(cc1), ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            cc1 = AliasFormat($"{cc[i + 1]}", "cc_inB", ending.tone, "");
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i]}", "cc_endB", ending.tone, "", t));
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]}{t}", ValidateAlias($"{cc[i]} {cc[i + 1]}{t}"))) {
                            // like [C1 C2-]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]} {t}", ValidateAlias($"{cc[i]} {cc[i + 1]} {t}"))) {
                            // like [C1 C2 -]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]}{t}", ValidateAlias($"{cc[i]}{cc[i + 1]}{t}"))) {
                            // like [C1C2-]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]} {t}", ValidateAlias($"{cc[i]}{cc[i + 1]} {t}"))) {
                            // like [C1C2 -]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            // like [C1 C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        } else if (!HasOto(cc1, ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            // [C1 -] [- C2]
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_inB", ending.tone, ""));
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 2]}", "cc_endB", ending.tone, "", t));
                            i++;
                        }
                    }
                }
            }
            return phonemes;
        }
        private string AliasFormat(string alias, string type, int tone, string prevV, string t = "-") {
            var aliasFormats = new Dictionary<string, string[]> {
            // Define alias formats for different types
                { "dynStart", new string[] { "" } },
                { "dynMid", new string[] { "" } },
                { "dynMid_vv", new string[] { "" } },
                { "dynEnd", new string[] { "" } },
                { "startingV", new string[] { "-", "- ", "_", "" } },
                { "vcEx", new string[] { $"{prevV} ", $"{prevV}" } },
                { "vvExtend", new string[] { "", "_", "-", "- " } },
                { "cv", new string[] { "-", "", "- ", "_" } },
                { "cvStart", new string[] { "-", "- ", "_" } },
                { "ending", new string[] { $" {t}", $"{t}"} },
                { "ending_mix", new string[] { $"{t}", $" {t}", "--" } },
                { "cc", new string[] { "", "-", "- ", "_" } },
                { "cc_start", new string[] { "- ", "-", "_" } },
                { "cc_end", new string[] { $" {t}", $"{t}", "" } },
                { "cc_inB", new string[] { "_", "-", "- " } },
                { "cc_endB", new string[] { "_", $"{t}", $" {t}" } },
                { "cc_mix", new string[] { $" {t}", " R", $"{t}", "", "_", $"{t} ", $"{t}" } },
                { "cc1_mix", new string[] { "", " -", "-", " R", "_", "- ", "-" } },
            };

            // Check if the given type exists in the aliasFormats dictionary
            if (!aliasFormats.ContainsKey(type) && !type.Contains("dynamic")) {
                return alias;
            }

            // Handle dynamic variations when type contains "dynamic"
            if (type.Contains("dynStart")) {
                string consonant = "";
                string vowel = "";
                // If the alias contains a space, split it into consonant and vowel
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' ');
                    consonant = parts[0];
                    vowel = parts[1];
                } else {
                    consonant = alias;
                }

                // Handle the alias with space and without space
                var dynamicVariations = new List<string> {
                    // Variations with space, dash, and underscore
                    $"- {consonant}{vowel}",        // "- CV"
                    $"- {consonant} {vowel}",       // "- C V"
                    $"-{consonant} {vowel}",        // "-C V"
                    $"-{consonant}{vowel}",         // "-CV"
                    $"-{consonant}_{vowel}",        // "-C_V"
                    $"- {consonant}_{vowel}",       // "- C_V"
                };
                // Check each dynamically generated format
                foreach (var variation in dynamicVariations) {
                    if (HasOto(variation, tone) || HasOto(ValidateAlias(variation), tone)) {
                        return variation;
                    }
                }
            }

            if (type.Contains("dynMid")) {
                string consonant = "";
                string vowel = "";
                // If the alias contains a space, split it into consonant and vowel
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' ');
                    consonant = parts[0];
                    vowel = parts[1];
                } else {
                    consonant = alias;
                }
                var dynamicVariations1 = new List<string> {
                    $"{consonant}{vowel}",    // "CV"
                    $"{consonant} {vowel}",    // "C V"
                    $"{consonant}_{vowel}",    // "C_V"
                };
                // Check each dynamically generated format
                foreach (var variation1 in dynamicVariations1) {
                    if (HasOto(variation1, tone) || HasOto(ValidateAlias(variation1), tone)) {
                        return variation1;
                    }
                }
            }

            if (type.Contains("dynEnd")) {
                string consonant = "";
                string vowel = "";
                // If the alias contains a space, split it into consonant and vowel
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' ');
                    consonant = parts[1];
                    vowel = parts[0];
                } else {
                    consonant = alias;
                }
                var dynamicVariations1 = new List<string> {
                    $"{vowel}{consonant} -",    // "VC -"
                    $"{vowel} {consonant}-",    // "V C-"
                    $"{vowel}{consonant}-",    // "VC-"
                    $"{vowel} {consonant} -",    // "V C -"
                };
                // Check each dynamically generated format
                foreach (var variation1 in dynamicVariations1) {
                    if (HasOto(variation1, tone) || HasOto(ValidateAlias(variation1), tone)) {
                        return variation1;
                    }
                }
            }

            // Get the array of possible alias formats for the specified type if not dynamic
            var formatsToTry = aliasFormats[type];
            int counter = 0;
            foreach (var format in formatsToTry) {
                string aliasFormat;
                if (type.Contains("mix") && counter < 4) {
                    aliasFormat = (counter % 2 == 0) ? $"{alias}{format}" : $"{format}{alias}";
                    counter++;
                } else if (type.Contains("end") || type.Contains("End") && !(type.Contains("dynEnd"))) {
                    aliasFormat = $"{alias}{format}";
                } else {
                    aliasFormat = $"{format}{alias}";
                }
                // Check if the formatted alias exists
                if (HasOto(aliasFormat, tone) || HasOto(ValidateAlias(aliasFormat), tone)) {
                    return aliasFormat;
                }
            }
            return alias;
        }

        protected override string ValidateAlias(string alias) {

            // VALIDATE ALIAS DEPENDING ON METHOD
            if (isMissingVPhonemes) {
                foreach (var fb in missingVphonemes.OrderByDescending(f => f.Key.Length)) {
                    alias = alias.Replace(fb.Key, fb.Value);
                }
            }
            if (isMissingCPhonemes) {
                foreach (var fb in missingCphonemes.OrderByDescending(f => f.Key.Length)) {
                    alias = alias.Replace(fb.Key, fb.Value);
                }
            }
            if (isYamlFallbacks) {
                foreach (var fb in yamlFallbacks.OrderByDescending(f => f.Key.Length)) {
                    alias = alias.Replace(fb.Key, fb.Value);
                }
            }

            return base.ValidateAlias(alias);
        }

        bool PhonemeIsPresent(string alias, string phoneme) {
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(phoneme))
                return false;

            // Exact token match
            if (alias == phoneme)
                return true;

            return alias.EndsWith(phoneme);
        }

        private bool PhonemeHasEndingSuffix(string alias, string phoneme) {
            var escapedPhoneme = Regex.Escape(phoneme);
            if (Regex.IsMatch(alias, $@"\b{escapedPhoneme}\b\s*-") ||
                Regex.IsMatch(alias, $@"\b{escapedPhoneme}\b-")) {
                return true;
            }
            if (Regex.IsMatch(alias, $@"\b{escapedPhoneme}\b R")) {
                return true;
            }
            return false;
        }

        protected override double GetTransitionBasicLengthMs(string alias = "") {
            //I wish these were automated instead :')
            double transitionMultiplier = 1.0; // Default multiplier

            var fricative_def = 2.3;
            var aspirate_def = 1.3;
            var semivowel_def = 1.2;
            var liquid_def = 1.5;
            var nasal_def = 1.5;
            var stop_def = 1.8;
            var tap_def = 0.5;
            var affricate_def = 1.5;

            var allConsonants = fricative.Concat(aspirate)
                        .Concat(semivowel)
                        .Concat(liquid)
                        .Concat(nasal)
                        .Concat(stop)
                        .Concat(tap)
                        .Concat(affricate)
                        .Distinct(); // Ensure no duplicates

            foreach (var c in allConsonants) {
                if (PhonemeHasEndingSuffix(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * 0.5;
                }
            }

            foreach (var v in vowels) {
                if (alias.EndsWith("-")) {
                    return base.GetTransitionBasicLengthMs() * 0.5;
                }
            }

            // consonant timings

            var sortedOverrides = PhonemeOverrides.OrderByDescending(kv => kv.Key.Length);
            foreach (var kvp in sortedOverrides) {
                var overridePhoneme = kvp.Key;
                var overrideValue = kvp.Value;
                if (PhonemeIsPresent(alias, overridePhoneme)) {
                    return base.GetTransitionBasicLengthMs() * overrideValue;
                }
            }

            foreach (var c in fricative) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * fricative_def;
                }
            }

            foreach (var c in aspirate) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * aspirate_def;
                }
            }

            foreach (var c in semivowel) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * semivowel_def;
                }
            }

            foreach (var c in liquid) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * liquid_def;
                }
            }
            
            foreach (var c in nasal) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * nasal_def;
                }
            }

            foreach (var c in stop) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * stop_def;
                }
            }
           
            foreach (var c in tap) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * tap_def;
                }
            }

            foreach (var c in affricate) {
                if (PhonemeIsPresent(alias, c)) {
                    return base.GetTransitionBasicLengthMs() * affricate_def;
                }
            }

            return base.GetTransitionBasicLengthMs() * transitionMultiplier;
        }
    }
}
