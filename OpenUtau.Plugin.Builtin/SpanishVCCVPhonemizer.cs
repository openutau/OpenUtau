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

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Spanish VCCV Phonemizer", "ES VCCV", "Lotte V", language: "ES")]
    public class SpanishVCCVPhonemizer : SyllableBasedPhonemizer {
        /// <summary>
        /// Based on the nJokis method.
        /// Supports automatic consonant substitutes, such as seseo, through ValidateAlias.
        ///</summary>
        protected override string YamlFileName => "njokis_vccv.yaml";
        protected override byte[] YamlTemplate => Data.Resources.njokis_template;
        protected override string YamlVersion => "1.2";

        public SpanishVCCVPhonemizer() {
            this.vowels = "a,e,i,o,u,BB,DD,ff,GG,ll,mm,nn,rrr,ss,xx".Split(',');
            this.consonants = "b,B,ch,d,D,E,f,g,G,h,I,jj,k,l,L,m,n,nJ,p,r,rr,s,sh,t,U,w,x,y,z".Split(',');
            this.dictionaryReplacements = ("a=a;e=e;i=i;o=o;u=u;" +
                "b=b;ch=ch;d=d;f=f;g=g;gn=nJ;k=k;l=l;ll=jj;m=m;n=n;p=p;r=r;rr=rr;s=s;t=t;w=w;x=x;y=y;z=z;I=I;U=U;B=B;D=D;G=G;Y=y").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        }

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_es.txt";
        protected override IG2p[] GetBaseG2ps() {
            return new IG2p[] { new SpanishG2p() };
        }

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (original == null) {
                return null;
            }
            List<string> finalProcessedPhonemes = new List<string>();

            for (int i = 0; i < original.Length; i++) {
                if (dictionaryReplacements.TryGetValue(original[i], out string replaced)) {
                    original[i] = replaced;
                }
            }
            
            // Splits diphthongs and affricates if not present in the bank
            string[] diphthongs = new[] { "aI", "eI", "oI", "uI", "aU", "eU", "oU" };
            
            foreach (string s in original) {
                switch (s) {
                    case var str when diphthongs.Contains(str) && !HasOto($"b{str}", note.tone) && !HasOto($"- {str}", note.tone):
                        finalProcessedPhonemes.AddRange(new string[] {s[0].ToString(), s[1].ToString() });
                        break;
                    default:
                        finalProcessedPhonemes.Add(s);
                        break;
                }
            }
            return finalProcessedPhonemes.ToArray();
        }

        private readonly string[] AllowedLPreceding = { "f", "p", "t", "k", "b", "g", "B", "G" };
        private readonly string[] AllowedRPreceding = { "f", "p", "t", "k", "b", "d", "g", "B", "D", "G" };
        private bool CanUseUnderscoreCv(string[] cc) {
            if (cc == null || cc.Length < 2) return true;
            
            string lastC = cc.Last();
            string prevC = cc[cc.Length - 2];

            if (lastC == "l") {
                return AllowedLPreceding.Contains(prevC);
            }
            if (lastC == "r") {
                return AllowedRPreceding.Contains(prevC);
            }
            return true;
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
                var rcv = $"- {v}";
                var rcv2 = $"-{v}";
                if (HasOto(rcv, syllable.vowelTone) || HasOto(ValidateAlias(rcv), syllable.vowelTone)) {
                    basePhoneme = rcv;
                } else {
                    basePhoneme = rcv2;
                }
            } else if (syllable.IsVV) {
                var vv = $"{prevV} {v}";
                var vv2 = $"{prevV}{v}";
                if (!CanMakeAliasExtension(syllable)) {
                    if (HasOto($"{prevV} {v}", syllable.vowelTone) || HasOto(ValidateAlias($"{prevV} {v}"), syllable.vowelTone)) {
                        basePhoneme = $"{prevV} {v}";
                    } else if (HasOto($"{prevV}{v}", syllable.vowelTone) || HasOto(ValidateAlias($"{prevV}{v}"), syllable.vowelTone)) {
                        basePhoneme = $"{prevV}{v}";
                    } 
                    
                    // Diphthong Fallbacks
                    else if (diphthongSplits.ContainsKey(prevV) || diphthongTails.ContainsKey(prevV)) {
                        string cv = "";
                        if (diphthongSplits.ContainsKey(prevV)) {
                            var splitOverride = diphthongSplits[prevV];
                            var vc = splitOverride[0].Replace("{v}", v);
                            cv = splitOverride[1].Replace("{v}", v);
                            TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc));
                        } 
                        else { // Default YAML diphthong logic
                            var tail = diphthongTails[prevV];
                            var vcSpace = $"{prevV} {tail}";
                            var vcNoSpace = $"{prevV}{tail}";
                            cv = $"{tail}{v}";
                            TryAddPhoneme(phonemes, syllable.tone, vcSpace, ValidateAlias(vcSpace), vcNoSpace, ValidateAlias(vcNoSpace));
                        }
                        if (HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv), syllable.vowelTone)) {
                            basePhoneme = cv;
                        } else if  (!HasOto(cv, syllable.vowelTone) || !HasOto(ValidateAlias(cv), syllable.vowelTone)) {
                            basePhoneme = $"{diphthongTails[prevV]} {v}";
                        } else if (HasOto(v, syllable.vowelTone) || HasOto(ValidateAlias(v), syllable.vowelTone)) {
                            basePhoneme = v;
                        } else {
                            basePhoneme = ValidateAlias($"- {v}");
                            phonemes.Add($"{prevV} -");
                        }
                    } else {
                        if (HasOto(v, syllable.vowelTone) || HasOto(ValidateAlias(v), syllable.vowelTone)) {
                            basePhoneme = v;
                        } else {
                            basePhoneme = ValidateAlias($"- {v}");
                            phonemes.Add($"{prevV} -");
                        }
                    }
                } else {
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                // TODO: move to config -CV or -C CV
                // TODO: move to config -CV or -C CV
                var rcv = $"-{cc[0]}{v}";
                var cv = $"{cc[0]}{v}";
                if (HasOto(rcv, syllable.vowelTone) || HasOto(ValidateAlias(rcv), syllable.vowelTone)) {
                    basePhoneme = rcv;
                } else {
                    basePhoneme = cv;
                    if (consonants.Contains(cc[0])) {
                        TryAddPhoneme(phonemes, syllable.tone, $"-{cc[0]}", ValidateAlias($"-{cc[0]}"));
                    }
                }
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                // try RCCV
                var rccv = $"-{string.Join("", cc)}{v}";
                var ccv = $"{string.Join("", cc)}{v}";
                var _cv = $"_{cc.Last()}{v}";
                var singleCv = $"{cc.Last()}{v}";

                var validRccv = HasOto(rccv, syllable.vowelTone) ? rccv : ValidateAlias(rccv);
                var validCcv = HasOto(ccv, syllable.vowelTone) ? ccv : ValidateAlias(ccv);

                // -CCV / -CCCV
                if ((HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone)) 
                    && validRccv != singleCv && validRccv != ValidateAlias(singleCv)) {
                    basePhoneme = validRccv;
                    firstC = cc.Length - 1;
                    lastC = cc.Length - 1;
                } 
                // CCV / CCCV
                else if ((HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) 
                    && validCcv != singleCv && validCcv != ValidateAlias(singleCv)) {
                    basePhoneme = validCcv;
                    lastC = 0;

                    bool clusterAdded = false;
                    for (var i = cc.Length - 1; i > 1; i--) {
                        var ccs = string.Join("", cc.Take(i));
                        if (TryAddPhoneme(phonemes, syllable.tone, $"-{ccs}", ValidateAlias($"-{ccs}"))) {
                            firstC = i - 1;
                            clusterAdded = true;
                            break;
                        }
                    }
                    if (!clusterAdded) {
                        TryAddPhoneme(phonemes, syllable.tone, 
                            $"- {cc[0]}", ValidateAlias($"- {cc[0]}"), 
                            $"-{cc[0]}", ValidateAlias($"-{cc[0]}"));
                        firstC = 0;
                    }
                } 
                // Fallback: Split version with _CV
                else {
                    if (HasOto(_cv, syllable.vowelTone)) {
                        basePhoneme = _cv;
                    } else if (HasOto(ValidateAlias(_cv), syllable.vowelTone)) {
                        basePhoneme = ValidateAlias(_cv);
                    } else if (HasOto(singleCv, syllable.vowelTone)) {
                        basePhoneme = singleCv;
                    } else {
                        basePhoneme = ValidateAlias(singleCv);
                    }

                    bool clusterAdded = false;
                        for (var i = cc.Length; i > 1; i--) {
                            var ccs = string.Join("", cc.Take(i));
                            if (TryAddPhoneme(phonemes, syllable.tone, $"-{ccs}", ValidateAlias($"-{ccs}"))) {
                                firstC = i - 1;
                                clusterAdded = true;
                                break;
                            }
                        }
                        if (!clusterAdded) {
                            TryAddPhoneme(phonemes, syllable.tone, 
                                $"- {cc[0]}", ValidateAlias($"- {cc[0]}"), 
                                $"-{cc[0]}", ValidateAlias($"-{cc[0]}"));
                            firstC = 0;
                        }
                }
            } else {
                var cv = cc.Last() + v;
                var _cv = $"_{cc.Last()}{v}";
                bool canUseUnderscore = CanUseUnderscoreCv(cc);

                if (canUseUnderscore && HasOto(_cv, syllable.vowelTone) && cc.Length >= 2) {
                    basePhoneme = _cv;
                } else if (canUseUnderscore && HasOto(ValidateAlias(_cv), syllable.vowelTone) && cc.Length >= 2) {
                    basePhoneme = ValidateAlias(_cv);
                } else if (HasOto(cv, syllable.vowelTone)) {
                    basePhoneme = cv;
                } else {
                    basePhoneme = ValidateAlias(cv);
                }

                if (cc.Length - firstC > 1) {
                    for (var i = firstC; i < cc.Length - 1; i++) {
                        var ccv = $"{string.Join("", cc.Skip(i))}{v}";
                        var rccv = $"-{string.Join("", cc)}{v}";
                        var singleCv = $"{cc.Last()}{v}";

                        var validCcv = HasOto(ccv, syllable.vowelTone) ? ccv : ValidateAlias(ccv);
                        var validRccv = HasOto(rccv, syllable.vowelTone) ? rccv : ValidateAlias(rccv);

                        if (cc.Length == 2 && (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) 
                            && validCcv != singleCv && validCcv != ValidateAlias(singleCv)) {
                            lastC = i;
                            basePhoneme = validCcv;
                            break;
                        } else if ((HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone)) 
                            && validRccv != singleCv && validRccv != ValidateAlias(singleCv)) {
                            lastC = i;
                            basePhoneme = validRccv;
                            break;
                        }
                    }
                }

                for (var i = lastC + 1; i >= 0; i--) {
                    var vcc = $"{prevV} {string.Join("", cc.Take(i))}";
                    var vcc2 = $"{prevV}{string.Join(" ", cc.Take(i))}";
                    var vc = $"{prevV} {cc[0]}";
                    var vc2 = $"{prevV}{cc[0]}";
                    if (i == 0) {
                        TryAddPhoneme(phonemes, syllable.tone, ValidateAlias(vcc), ValidateAlias(vc));
                    } else if (HasOto(vcc, syllable.tone)) {
                        phonemes.Add(vcc);
                        break;
                    } else if (!HasOto(vcc, syllable.tone) && HasOto(ValidateAlias(vcc), syllable.tone)) {
                        phonemes.Add(ValidateAlias(vcc));
                        break;
                    } else if (HasOto(vcc2, syllable.tone)) {
                        phonemes.Add(vcc2);
                        break;
                    } else if (!HasOto(vcc2, syllable.tone) && HasOto(ValidateAlias(vcc2), syllable.tone)) {
                        phonemes.Add(ValidateAlias(vcc2));
                        break;
                    } else if (HasOto(vc, syllable.tone)) {
                        phonemes.Add(vc);
                        break;
                    } else if (!HasOto(vc, syllable.tone) && HasOto(ValidateAlias(vc), syllable.tone)) {
                        phonemes.Add(ValidateAlias(vc));
                        break;
                    } else if (HasOto(vc2, syllable.tone) && HasOto(ValidateAlias(vc2), syllable.tone)) {
                        phonemes.Add(vc2);
                        break;
                    }
                }
            }

            for (var i = firstC; i < lastC; i++) {
                var rccv = $"-{string.Join("", cc)}{v}";

                if (!HasOto(rccv, syllable.vowelTone) && !HasOto(ValidateAlias(rccv), syllable.vowelTone)) {
                    string cc1;
                    if (cc.Length >= 3 || i == cc.Length - 3) {
                        // CCV / final CCCV transition: try [_cc_] first, fallback to [c c] / [cc]
                        cc1 = $"_{cc[i]}{cc[i + 1]}_";
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = $"{cc[i]} {cc[i + 1]}";
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = $"{cc[i]}{cc[i + 1]}";
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                    } else {
                        // Earlier CCCV transition: try [c c] / [cc] first, fallback to [_cc_]
                        cc1 = $"{cc[i]} {cc[i + 1]}";
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = $"{cc[i]}{cc[i + 1]}";
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                    }

                    TryAddPhoneme(phonemes, syllable.tone, cc1);
                }
            }

            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc.Select(c => ReplacePhoneme(c, ending.tone)).ToArray();
            string v = ReplacePhoneme(ending.prevV, ending.tone);
            string t = ending.HasTail ? ReplacePhoneme(ending.tail, ending.tone) : "-";

            var phonemes = new List<string>();
            if (ending.IsEndingV) {
                TryAddPhoneme(phonemes, ending.tone, $"{v}{t}", ValidateAlias($"{v}{t}"));
            } else if (ending.IsEndingVCWithOneConsonant) {
                var vcr = $"{v}{cc[0]}{t}";
                if (HasOto(vcr, ending.tone)) {
                    phonemes.Add(vcr);
                } else {
                    phonemes.Add($"{v} {cc[0]}");
                    TryAddPhoneme(phonemes, ending.tone, $"{cc[0]}{t}", ValidateAlias($"{cc[0]}{t}"));
                }
            } else {
                var vcc1 = $"{v} {string.Join("", cc)}";
                var vcc2 = $"{v}{string.Join(" ", cc)}";
                var vc1 = $"{v}{cc[0]}";
                var vc2 = $"{v} {cc[0]}";
                if (HasOto(vcc1, ending.tone)) {
                    phonemes.Add(vcc1);
                } else if (!HasOto(vcc1, ending.tone) && HasOto(ValidateAlias(vcc1), ending.tone)) {
                    phonemes.Add(ValidateAlias(vcc1));
                } else if (!HasOto(ValidateAlias(vcc1), ending.tone) && HasOto(vcc2, ending.tone)) {
                    phonemes.Add(vcc2);
                } else if (!HasOto(vcc2, ending.tone) && HasOto(ValidateAlias(vcc2), ending.tone)) {
                    phonemes.Add(ValidateAlias(vcc2));
                } else if (!HasOto(ValidateAlias(vcc2), ending.tone) && HasOto(vc1, ending.tone)) {
                    phonemes.Add(vc1);
                } else if (!HasOto(vc1, ending.tone) && HasOto(ValidateAlias(vc1), ending.tone)) {
                    phonemes.Add(ValidateAlias(vc1));
                } else if (!HasOto(ValidateAlias(vc1), ending.tone) && HasOto(vc2, ending.tone)) {
                    phonemes.Add(vc2);
                } else if (!HasOto(vc2, ending.tone) && HasOto(ValidateAlias(vc2), ending.tone)) {
                    phonemes.Add(ValidateAlias(vc2));
                }
                for (var i = 0; i < cc.Length - 1; i++) {
                    var cc1 = $"{cc[i]} {cc[i + 1]}";
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = $"{cc[i]}{cc[i + 1]}";
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1));
                    TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()}{t}", ValidateAlias($"{cc.Last()}{t}"));
                }
            }
            return phonemes;
        }
        protected override string GetHardcodedFallback(string alias, int tone, HashSet<string> suppressedTokens) {
            string candidate = alias;

            var rules = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "I", "y" }, { "U", "w" }, 
                { "BB", "B" }, { "DD", "D" },
                { "ff", "f" }, 
                { "GG", "G" },
                { "ll", "l" }, { "mm", "m" }, { "nn", "n" },
                { "rrr", "rr" },
                { "ss", "s" },
                { "E", "e" },
                { " k", " t" }, { " p", " t" }, { " ch", " t" },
                { "b", "B" }, { "d", "D" }, { "g", "G" },
                { "z", "s" },
                { "jj", "sh" },
                { "x", "h" }
            };

            // Apply dictionary substitutions while honoring YAML suppression
            foreach (var rule in rules.OrderByDescending(r => r.Key.Length)) {
                string tokenKey = rule.Key.Trim();
                if (suppressedTokens != null && suppressedTokens.Contains(tokenKey)) continue;

                candidate = candidate.Replace(rule.Key, rule.Value, StringComparison.Ordinal);
            }

            // Secondary adjustments
            if (suppressedTokens == null || !suppressedTokens.Contains("h")) {
                candidate = candidate.Replace("h", "x", StringComparison.Ordinal);
            }
            candidate = candidate.Replace("xx", "x", StringComparison.Ordinal);

            if (suppressedTokens == null || !suppressedTokens.Contains("jj")) {
                candidate = candidate.Replace("jj", "L", StringComparison.Ordinal);
            }

            if (!string.Equals(candidate, alias, StringComparison.Ordinal) && HasOto(candidate, tone)) {
                return candidate;
            }

            return null;
        }

        protected override bool NoGap => true;

        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            double otoLength = GetTransitionBasicLengthMsByOto(alias, tone, attr);

            return otoLength;
        }
    }
}
