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
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;

namespace OpenUtau.Plugin.Builtin {
    // IDN100% Indonesian VCCV Phonemizer
    [Phonemizer("Indonesian VCCV phonemizer", "ID VCCV", "Cadlaxa", language: "ID")]
    public class IDNVCCVPhonemizer : SyllableBasedPhonemizer {
        protected override string YamlFileName => "idn-vccv.yaml";
        protected override string YamlVersion => "1.0";
        protected override byte[] YamlTemplate => Data.Resources.idvccv_template;
        public IDNVCCVPhonemizer() {
            this.vowels = "a,e,i,o,u,ax".Split(',');
            this.consonants = Array.Empty<string>();
            this.dictionaryReplacements = "@=ax".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        }
        private Dictionary<string, double> PhonemeOverrides = new Dictionary<string, double>();

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants; 
        protected override string GetDictionaryName() => "";
        private readonly Dictionary<string, string> vvExceptions =
            new Dictionary<string, string>() {
                {"ai","y"},
                {"ei","y"},
                {"oi","y"},
                {"au","w"},
                {"ou","w"},
                {"ay","y"},
                {"ey","y"},
                {"oy","y"},
                {"aw","w"},
                {"ow","w"},
            };
        protected override IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, YamlFileName);
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, YamlTemplate);
            }
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());

            // Load dictionary from singer folder.
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
            g2ps.Add(new IndonesianG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }
    
        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);

            if (original == null) {
                return null;
            }
           List<string> finalProcessedPhonemes = new List<string>();

            foreach (string s in original) {
                finalProcessedPhonemes.Add(s);
            }
            return finalProcessedPhonemes.ToArray();
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

            
            if (syllable.IsStartingV) {
                basePhoneme = AliasFormat(v, "startingV", syllable.vowelTone, "");
            }
            else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    basePhoneme = $"{prevV} {v}";
                    if (!HasOto(basePhoneme, syllable.vowelTone) && !HasOto(ValidateAlias(basePhoneme), syllable.vowelTone) && vvExceptions.ContainsKey(prevV) && prevV != v) {
                        var vc = AliasFormat($"{vvExceptions[prevV]}", "vcEx", syllable.vowelTone, prevV);
                        phonemes.Add(vc);
                        basePhoneme = ValidateAlias(AliasFormat($"{vvExceptions[prevV]} {v}", "dynMid", syllable.vowelTone, ""));
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
                                phonemes.Add(AliasFormat($"{prevV} -", "dynMid", syllable.vowelTone, ""));
                            }
                        }
                    }
                } else {
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                var rcv = $"- {cc[0]} {v}";
                var rcv1 = $"- {cc[0]}{v}";
                var crv = $"{cc[0]} {v}";
                /// - CV
                if (HasOto(rcv, syllable.vowelTone) || HasOto(ValidateAlias(rcv), syllable.vowelTone) || (HasOto(rcv1, syllable.vowelTone) || HasOto(ValidateAlias(rcv1), syllable.vowelTone))) {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynStart", syllable.vowelTone, "");
                    /// CV
                } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone)) {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynMid", syllable.vowelTone, "");
                    TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, "")));
                } else {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynMid", syllable.vowelTone, "");
                    TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, "")));
                }
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                // try RCCV
                var rccv = $"- {string.Join("", cc)} {v}";
                var rccv1 = $"- {string.Join("", cc)}{v}";
                var crv = $"{cc.Last()} {v}";
                var crv1 = $"{cc.Last()}{v}";
                var ccv = $"{string.Join("", cc)} {v}";
                var ccv1 = $"{string.Join("", cc)}{v}";
                var ucv = $"_{cc.Last()}{v}";
                var cv = $"{cc.Last()}{v}";
                if (HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone) || HasOto(rccv1, syllable.vowelTone) || HasOto(ValidateAlias(rccv1), syllable.vowelTone)) {
                    basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynStart", syllable.vowelTone, "");
                    lastC = 0;
                } else {
                    if (HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) {
                        basePhoneme = ucv;
                    } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone) || HasOto(crv1, syllable.vowelTone) || HasOto(ValidateAlias(crv1), syllable.vowelTone)) {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    } else {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    }
                    // TRY RCC [- CC]
                    for (var i = cc.Length; i > 1; i--) {
                        if (TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{string.Join("", cc.Take(i))}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{string.Join("", cc.Take(i))}", "cc_start", syllable.vowelTone, "")))) {
                            firstC = i - 1;
                            break;
                        }
                    }
                    
                    // [- C]
                    // todo: deincremental search for starting consonant clusters [str] → [st] → [s]
                    if (phonemes.Count == 0) {
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, "")));
                    }
                    // try CCV
                    for (var i = firstC; i < cc.Length - 1; i++) {
                        if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                            basePhoneme = ccv;
                            lastC = i;
                            break;
                        } else {
                            if (HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) {
                                basePhoneme = ucv;
                                break;
                            } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone) || HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv), syllable.vowelTone)) {
                                basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                                break;
                            } else {
                                basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                                break;

                            }
                        }
                    }
                }
            } else { // VCV
                var vcv = $"{prevV} {cc[0]}{v}";
                var vcvEnd = $"{prevV}{cc[0]} {v}";
                var vccv = $"{prevV} {string.Join("", cc)}{v}";
                var crv = $"{cc.Last()} {v}";
                // Use regular VCV if the current word starts with one consonant and the previous word ends with none
                if (syllable.IsVCVWithOneConsonant && (HasOto(vcv, syllable.vowelTone) || HasOto(ValidateAlias(vcv), syllable.vowelTone)) && prevWordConsonantsCount == 0 && CurrentWordCc.Length == 1) {
                    basePhoneme = vcv;
                    // Use end VCV if current word does not start with a consonant but the previous word does end with one
                } else if (syllable.IsVCVWithOneConsonant && prevWordConsonantsCount == 1 && CurrentWordCc.Length == 0 && (HasOto(vcvEnd, syllable.vowelTone) || HasOto(ValidateAlias(vcvEnd), syllable.vowelTone))) {
                    basePhoneme = vcvEnd;
                    // Use regular VCV if end VCV does not exist
                } else if (syllable.IsVCVWithOneConsonant && !HasOto(vcvEnd, syllable.vowelTone) && !HasOto(ValidateAlias(vcvEnd), syllable.vowelTone) && (HasOto(vcv, syllable.vowelTone) || HasOto(ValidateAlias(vcv), syllable.vowelTone))) {
                    basePhoneme = vcv;
                    // VCV with multiple consonants, only for current word onset and null previous word ending
                    // TODO: multi-VCV for words ending with one or more consonants?
                } else if (syllable.IsVCVWithMoreThanOneConsonant && (HasOto(vccv, syllable.vowelTone) || HasOto(ValidateAlias(vccv), syllable.vowelTone)) && prevWordConsonantsCount == 0) {
                    basePhoneme = vccv;
                    lastC = 0;
                } else {
                    var cv = $"{cc.Last()}{v}";
                    /// CV
                    if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone) || HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv), syllable.vowelTone)) {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    } else {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    }
                    // try CCV
                    if ((cc.Length - firstC > 1) && CurrentWordCc.Length >= 2) {
                        for (var i = firstC; i < cc.Length; i++) {
                            var ccv = $"{string.Join("", cc.Skip(0))}{v}";
                            var rccv = $"- {string.Join("", cc.Skip(0))}{v}";
                            var ucv = $"_{cc.Last()}{v}";
                            if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                                lastC = 0;
                                basePhoneme = ccv;
                                break;
                            } else if (!HasOto(ccv, syllable.vowelTone) && !HasOto(ValidateAlias(ccv), syllable.vowelTone) && (HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone))) {
                                lastC = 0;
                                basePhoneme = rccv;
                                break;
                            } else if ((!HasOto(rccv, syllable.vowelTone) || !HasOto(ValidateAlias(rccv), syllable.vowelTone)) && (HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone))) {
                                basePhoneme = ucv;
                                break;
                            }
                        }
                    }
                    FoundMatch:;
                    // try vcc
                    for (var i = lastC + 1; i >= 0; i--) {
                        var vr = $"{prevV} -";
                        var vcc = $"{prevV} {string.Join("", cc.Take(2))}";
                        var vcc2 = $"{prevV}{string.Join(" ", cc.Take(2))}";
                        var vcc3 = $"{prevV}{string.Join(" ", cc.Take(3))}";
                        var vc = $"{prevV} {cc[0]}";
                        if (i == 0) {
                            if (HasOto(vr, syllable.tone) || HasOto(ValidateAlias(vr), syllable.tone)) {
                                phonemes.Add(vr);
                            }
                        } else if ((HasOto(vcc, syllable.tone) || HasOto(ValidateAlias(vcc), syllable.tone)) && !affricate.Contains(string.Join("", cc.Take(2)))) {
                            phonemes.Add(vcc);
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc2, syllable.tone) || HasOto(ValidateAlias(vcc2), syllable.tone)) {
                            phonemes.Add(vcc2);
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc3, syllable.tone) || HasOto(ValidateAlias(vcc3), syllable.tone)) {
                            phonemes.Add(vcc3);
                            firstC = 1;
                            break;
                        } else if (HasOto(vc, syllable.tone) || HasOto(ValidateAlias(vc), syllable.tone)) {
                            phonemes.Add(vc);
                            break;
                        } else {
                            continue;
                        }
                    }
                }
            }

            for (var i = firstC; i < lastC; i++) {
                var cc1 = $"{cc[i]} {cc[i + 1]}";
                var ccv = string.Join("", cc.Skip(i + 1)) + v;
                var rccv = $"- {string.Join("", cc.Skip(i + 1)) + v}";
                var ucv = $"_{cc.Last()}{v}";
                var crv = $"{cc.Last()} {v}";
                var cv = $"{cc.Last()}{v}";
                // Use [C1C2...] when current word starts with 2 consonants or more
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                if (CurrentWordCc.Length >= 2 && !PreviousWordCc.Contains(cc1)) {
                    cc1 = $"{string.Join("", cc.Skip(i))}";
                }
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                // Use [C1C2] when current word has 2 consonants or more and [C1C2C3...] does not exist
                if (!HasOto(cc1, syllable.tone) && CurrentWordCc.Length >= 2 && CurrentWordCc.Contains(cc1)) {
                    cc1 = $"{cc[i]}{cc[i + 1]}";
                }
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                // Use [C1 C2] when either [C1C2] does not exist, or current word has 1 consonant or less and previous word has 1 consonant or more
                if ((!HasOto(cc1, syllable.tone)) || PreviousWordCc.Contains(cc1)) {
                    cc1 = $"{cc[i]} {cc[i + 1]}";
                }
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                // Use UCV if it exists
                if ((HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) && !cc1.Contains($"{cc[i]} {cc[i + 1]}")) {
                    basePhoneme = ucv;
                }
                if (i + 1 < lastC) {
                    var cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                    if (!HasOto(cc2, syllable.tone)) {
                        cc2 = ValidateAlias(cc2);
                    }
                    // Use [C2C3...] when current word starts with 2 consonants or more
                    if (CurrentWordCc.Length >= 2 && !PreviousWordCc.Contains(cc2)) {
                        cc2 = $"{string.Join("", cc.Skip(i))}";
                    }
                    if (!HasOto(cc2, syllable.tone)) {
                        cc2 = ValidateAlias(cc2);
                    }
                    // Use [C2C3] when current word has 2 consonants or more and [C2C3C4...] does not exist
                    if (!HasOto(cc2, syllable.tone) && CurrentWordCc.Length >= 2 && CurrentWordCc.Contains(cc2)) {
                        cc2 = $"{cc[i + 1]}{cc[i + 2]}";
                    }
                    if (!HasOto(cc2, syllable.tone)) {
                        cc2 = ValidateAlias(cc2);
                    }
                    // Use [C2 C3] when either [C2C3] does not exist, or current word has 1 consonant or less and previous word has 2 consonants or more
                    if ((!HasOto(cc2, syllable.tone)) || PreviousWordCc.Contains(cc2)) {
                        cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                    }
                    if (!HasOto(cc2, syllable.tone)) {
                        cc2 = ValidateAlias(cc2);
                    }
                    //Use CCV if it exists
                    if ((HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) && CurrentWordCc.Length >= 2 && !PreviousWordCc.Contains(string.Join("", cc.Skip(i + 1)))) {
                        lastC = i;
                        basePhoneme = ccv;
                        // Use RCCV if it exists
                    } else if ((HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone)) && CurrentWordCc.Length >= 2 && !PreviousWordCc.Contains(string.Join("", cc.Skip(i + 1)))) {
                        lastC = i;
                        basePhoneme = rccv;
                        // Use _CV if it exists
                    } else if ((HasOto(ucv, syllable.vowelTone) || HasOto(ValidateAlias(ucv), syllable.vowelTone)) && HasOto(cc2, syllable.vowelTone) && !cc2.Contains($"{cc[i + 1]} {cc[i + 2]}") && CurrentWordCc.Length >= 2) {
                        basePhoneme = ucv;
                        // Use spaced CV if it exists
                    } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone)) {
                        basePhoneme = crv;
                        // Use normal CV
                    } else {
                        basePhoneme = cv;
                    }
                    if (HasOto(cc1, syllable.tone) && HasOto(cc2, syllable.tone) && !cc1.Contains($"{string.Join("", cc.Skip(i))}")) {
                        // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                        phonemes.Add(cc1);
                    } else if (TryAddPhoneme(phonemes, syllable.tone, cc1)) {
                        // like [V C1] [C1 C2] [C2 ..]
                        if (cc1.Contains($"{string.Join("", cc.Skip(i))}")) {
                            i++;
                        }
                    } else {
                        // singular cc
                        if ((PreviousWordCc.Contains(cc1) == CurrentWordCc.Contains(cc1)) && !affricate.Contains(cc1)) {
                            cc1 = ValidateAlias(cc1);
                        } else {
                            TryAddPhoneme(phonemes, syllable.tone, cc1, cc[i], ValidateAlias(cc[i]));
                        }
                    }
                } else {
                    // like [V C1] [C1 C2]  [C2 ..] or like [V C1] [C1 -] [C3 ..]
                    TryAddPhoneme(phonemes, syllable.tone, cc1, cc[i], ValidateAlias(cc[i]));
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

            var vr = $"{prevV} {t}";

            if (ending.IsEndingV) {
                var vR = $"{prevV} {t}";
                var vR2 = $"{prevV}{t}";
                if (HasOto(vR, ending.tone) || HasOto(ValidateAlias(vR), ending.tone) || HasOto(vR2, ending.tone) || HasOto(ValidateAlias(vR2), ending.tone)) {
                    phonemes.Add(AliasFormat($"{prevV}", "ending", ending.tone, "", t));
                }
            } else if (ending.IsEndingVCWithOneConsonant) {
                var vcr1 = $"{v} {cc[0]} {t}";
                var vcr2 = $"{v} {cc[0]}{t}";
                var vcr3 = $"{v}{cc[0]} {t}";
                var vcr4 = $"{v}{cc[0]}{t}";

                if (HasOto(vcr1, ending.tone) || HasOto(ValidateAlias(vcr1), ending.tone)) {
                    phonemes.Add(HasOto(vcr1, ending.tone) ? vcr1 : ValidateAlias(vcr1));
                } else if (HasOto(vcr2, ending.tone) || HasOto(ValidateAlias(vcr2), ending.tone)) {
                    phonemes.Add(HasOto(vcr2, ending.tone) ? vcr2 : ValidateAlias(vcr2));
                } else if (HasOto(vcr3, ending.tone) || HasOto(ValidateAlias(vcr3), ending.tone)) {
                    phonemes.Add(HasOto(vcr3, ending.tone) ? vcr3 : ValidateAlias(vcr3));
                } else if (HasOto(vcr4, ending.tone) || HasOto(ValidateAlias(vcr4), ending.tone)) {
                    phonemes.Add(HasOto(vcr4, ending.tone) ? vcr4 : ValidateAlias(vcr4));
                } else {
                    var vcSpace = $"{v} {cc[0]}";
                    phonemes.Add(vcSpace); 
                    TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} {t}", ValidateAlias($"{cc[0]} {t}"));
                }
            } else {
                for (var i = lastC; i >= 0; i--) {
                    var vcc = $"{v} {string.Join("", cc.Take(2))}{t}";
                    var vcc2 = $"{v}{string.Join(" ", cc.Take(2))}{t}";
                    var vcc6 = $"{v}{string.Join(" ", cc.Take(3))}{t}";
                    var vcc3 = $"{v}{string.Join(" ", cc.Take(2))}";
                    var vcc5 = $"{v}{string.Join(" ", cc.Take(3))}";
                    var vcc4 = $"{v} {string.Join("", cc.Take(2))}";
                    var vc = $"{v} {cc[0]}";
                    if (i == 0) {
                        if (HasOto(vr, ending.tone) || HasOto(ValidateAlias(vr), ending.tone)) {
                            phonemes.Add(vr);
                        }
                    } else if ((HasOto(vcc, ending.tone) || HasOto(ValidateAlias(vcc), ending.tone)) && lastC == 1) {
                        phonemes.Add(vcc);
                        firstC = 1;
                        break;
                    } else if ((HasOto(vcc2, ending.tone) || HasOto(ValidateAlias(vcc2), ending.tone)) && lastC == 1) {
                        phonemes.Add(vcc2);
                        firstC = 1;
                        break;
                    } else if ((HasOto(vcc6, ending.tone) || HasOto(ValidateAlias(vcc6), ending.tone)) && lastC == 1) {
                        phonemes.Add(vcc6);
                        firstC = 1;
                        break;
                    } else if (HasOto(vcc3, ending.tone) || HasOto(ValidateAlias(vcc3), ending.tone)) {
                        phonemes.Add(vcc3);
                        if (vcc3.EndsWith(cc.Last()) && lastC == 1) {
                            if (affricate.Contains(cc.Last())) {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()} {t}", ValidateAlias($"{cc.Last()} {t}"), cc.Last(), ValidateAlias(cc.Last()));
                            } else {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()} {t}", ValidateAlias($"{cc.Last()} {t}"));
                            }
                        }
                        firstC = 1;
                        break;
                    } else if (HasOto(vcc4, ending.tone) || HasOto(ValidateAlias(vcc4), ending.tone)) {
                        phonemes.Add(vcc4);
                        if (vcc4.EndsWith(cc.Last()) && lastC == 1) {
                            if (affricate.Contains(cc.Last())) {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()} {t}", ValidateAlias($"{cc.Last()} {t}"), cc.Last(), ValidateAlias(cc.Last()));
                            } else {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()} {t}", ValidateAlias($"{cc.Last()} {t}"));
                            }
                        }
                        firstC = 1;
                        break;
                    } else if (HasOto(vcc5, ending.tone) || HasOto(ValidateAlias(vcc5), ending.tone)) {
                        phonemes.Add(vcc5);
                        if (vcc4.EndsWith(cc.Last()) && lastC == 1) {
                            if (affricate.Contains(cc.Last())) {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()} {t}", ValidateAlias($"{cc.Last()} {t}"), cc.Last(), ValidateAlias(cc.Last()));
                            } else {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()} {t}", ValidateAlias($"{cc.Last()} {t}"));
                            }
                        }
                        firstC = 1;
                        break;
                    } else {
                        phonemes.Add(vc);
                        break;
                    }
                }


                for (var i = firstC; i < lastC; i++) {
                    var cc1 = $"{cc[i]} {cc[i + 1]}";
                    if (i < cc.Length - 2) {
                        var cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = $"{cc[i]}{cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = $"{cc[i + 1]}{cc[i + 2]}";
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]}{cc[i + 2]}{t}", ValidateAlias($"{cc[i]} {cc[i + 1]}{cc[i + 2]}{t}"))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (HasOto(cc1, ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}"), ending.tone))) {
                            // like [C1 C2][C2 ...]
                            phonemes.Add(cc1);
                        } else if ((HasOto(cc[i], ending.tone) || HasOto(ValidateAlias(cc[i]), ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}"), ending.tone)))) {
                            // like [C1 C2-][C3 ...]
                            phonemes.Add(cc[i]);
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {cc[i + 2]}{t}", ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}"))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]}{cc[i + 2]}", ValidateAlias($"{cc[i + 1]}{cc[i + 2]}"))) {
                            // like [C1C2][C2 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
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
                            cc1 = $"{cc[i]}{cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]}{t}", ValidateAlias($"{cc[i]} {cc[i + 1]}{t}"))) {
                            // like [C1 C2-]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            // like [C1 C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]}", ValidateAlias($"{cc[i]}{cc[i + 1]}"))) {
                            // like [C1C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        } else {
                            // like [C1][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i]), $"{cc[i]} {t}", ValidateAlias($"{cc[i]} {t}"));
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        }
                    }
                }
            }
            return phonemes;
        }

        private string AliasFormat(string alias, string type, int tone, string prevV, string t = "-") {
            var aliasFormats = new Dictionary<string, string[]> {
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

            if (!aliasFormats.ContainsKey(type) && !type.Contains("dynamic")) {
                return alias;
            }

            if (type.Contains("dynStart")) {
                string consonant = "";
                string vowel = "";
                if (alias.Contains(" ")) {
                    var parts = alias.Split(' ');
                    consonant = parts[0];
                    vowel = parts[1];
                } else {
                    consonant = alias;
                }

                var dynamicVariations = new List<string> {
                    $"- {consonant}{vowel}",        // "- CV"
                    $"- {consonant} {vowel}",       // "- C V"
                    $"-{consonant} {vowel}",        // "-C V"
                    $"-{consonant}{vowel}",         // "-CV"
                    $"-{consonant}_{vowel}",        // "-C_V"
                    $"- {consonant}_{vowel}",       // "- C_V"
                };
                foreach (var variation in dynamicVariations) {
                    if (HasOto(variation, tone)) {
                        return variation;
                    } else if (HasOto(ValidateAlias(variation), tone)) {
                        return ValidateAlias(variation);
                    }
                }
            }

            if (type.Contains("dynMid")) {
                string consonant = "";
                string vowel = "";
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
                foreach (var variation1 in dynamicVariations1) {
                    if (HasOto(variation1, tone)) {
                        return variation1;
                    } else if (HasOto(ValidateAlias(variation1), tone)) {
                        return ValidateAlias(variation1);
                    }
                }
            }

            if (type.Contains("dynEnd")) {
                string consonant = "";
                string vowel = "";
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
                foreach (var variation1 in dynamicVariations1) {
                    if (HasOto(variation1, tone)) {
                        return variation1;
                    } else if (HasOto(ValidateAlias(variation1), tone)) {
                        return ValidateAlias(variation1);
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
                if (HasOto(aliasFormat, tone)) {
                    return aliasFormat;
                } else if (HasOto(ValidateAlias(aliasFormat), tone)) {
                    return ValidateAlias(aliasFormat);
                }
            }
            return alias;
        }
        protected override bool NoGap => true;
        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            double otoLength = GetTransitionBasicLengthMsByOto(alias, tone, attr);

            var sortedOverrides = PhonemeOverrides.OrderByDescending(kv => kv.Key.Length);
            foreach (var kvp in sortedOverrides) {
                var symbol = kvp.Key;
                var value = kvp.Value;

                if (alias.Contains(symbol)) {
                    return GetTransitionBasicLengthMsByConstant() * value;
                }
            }
            
            return otoLength;

        }
    }
}
