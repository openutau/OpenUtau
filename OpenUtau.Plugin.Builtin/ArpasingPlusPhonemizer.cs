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

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Arpasing+ Phonemizer", "EN ARPA+", "Cadlaxa", language: "EN")]
    // Custom ARPAsing Phonemizer for OU
    // main focus of this Phonemizer is to bring fallbacks to existing available alias from
    // all ARPAsing banks
    public class ArpasingPlusPhonemizer : SyllableBasedPhonemizer {
        protected override string YamlFileName => "arpasing.yaml";
        protected override byte[] YamlTemplate => Data.Resources.arpasing_template;
        public ArpasingPlusPhonemizer() {
            this.vowels = new string[] {
                "aa", "ax", "ae", "ah", "ao", "aw", "ay", "eh", "er", "ey", "ih", "iy", "ow", "oy", "uh", "uw", "a", "e", "i", "o", "u", "ai", "ei", "oi", "au", "ou", "ix", "ux",
                "aar", "ar", "axr", "aer", "ahr", "aor", "or", "awr", "aur", "ayr", "air", "ehr", "eyr", "eir", "ihr", "iyr", "ir", "owr", "our", "oyr", "oir", "uhr", "uwr", "ur",
                "aal", "al", "axl", "ael", "ahl", "aol", "ol", "awl", "aul", "ayl", "ail", "ehl", "el", "eyl", "eil", "ihl", "iyl", "il", "owl", "oul", "oyl", "oil", "uhl", "uwl", "ul",
                "aan", "an", "axn", "aen", "ahn", "aon", "on", "awn", "aun", "ayn", "ain", "ehn", "en", "eyn", "ein", "ihn", "iyn", "in", "own", "oun", "oyn", "oin", "uhn", "uwn", "un",
                "aang", "ang", "axng", "aeng", "ahng", "aong", "ong", "awng", "aung", "ayng", "aing", "ehng", "eng", "eyng", "eing", "ihng", "iyng", "ing", "owng", "oung", "oyng", "oing", "uhng", "uwng", "ung",
                "aam", "am", "axm", "aem", "ahm", "aom", "om", "awm", "aum", "aym", "aim", "ehm", "em", "eym", "eim", "ihm", "iym", "im", "owm", "oum", "oym", "oim", "uhm", "uwm", "um", "oh",
                "eu", "oe", "yw", "yx", "wx", "ox", "ex", "ea", "ia", "oa", "ua", "ean", "eam", "eang"
            };
            this.consonants = "b,ch,d,dh,dr,dx,f,g,hh,jh,k,l,m,n,ng,p,q,r,s,sh,t,th,tr,v,w,y,z".Split(',');
            this.diphthongTails = new Dictionary<string, string>() {
                { "ay", "y" },
                { "ey", "y" },
                { "oy", "y" },
                { "aw", "w" },
                { "ow", "w" },
                { "er", "r" },
                { "iy", "y" },
            };
        }
        
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "";
        List<string> consExceptions = new List<string>();

        // For banks with missing vowels
        private readonly Dictionary<string, string> missingVphonemes = "ax=ah,aa=ah,ae=ah,iy=ih,uh=uw,ix=ih,ux=uh,oh=ao,eu=uh,oe=ax,uy=uw,yw=uw,yx=iy,wx=uw,ea=eh,ia=iy,oa=ao,ua=uw,R=-,N=n,mm=m,ll=l".Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        // For banks with missing custom consonants
        private readonly Dictionary<string, string> missingCphonemes = "nx=n,tx=t,dx=d,zh=sh,z=s,ng=n,cl=q,vf=q,dd=d,lx=l".Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isYamlFallbacks = false;
        private bool vc_FallBack = false;
        private bool phoneticHint = false;

        private readonly Dictionary<string, string> vcFallBacks =
            new Dictionary<string, string>() {
                {"aw","uw"},
                {"ow","uw"},
                {"uh","uw"},
                {"ay","iy"},
                {"ey","iy"},
                {"oy","iy"},
                {"aa","ah"},
                {"ae","ah"},
                {"ao","ah"},
                //{"eh","ah"},
                //{"er","ah"},
            };

        private readonly string[] ccvException = { "ng", "dh" };
        private readonly string[] RomajiException = { "a", "e", "i", "o", "u" };

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            phoneticHint = !string.IsNullOrEmpty(note.phoneticHint);
            if (original == null) {
                return null;
            }
            List<string> finalProcessedPhonemes = new List<string>();

            // SPLITS UP DR AND TR
            string[] tr = new[] { "tr" };
            string[] dr = new[] { "dr" };
            string[] wh = new[] { "wh" };
            string[] av_c = new[] { "al", "am", "an", "ang", "ar" };
            string[] ev_c = new[] { "el", "em", "en", "eng", "err" };
            string[] iv_c = new[] { "il", "im", "in", "ing", "ir" };
            string[] ov_c = new[] { "ol", "om", "on", "ong", "or" };
            string[] uv_c = new[] { "ul", "um", "un", "ung", "ur" };
            var consonatsV1 = new List<string> { "l", "m", "n", "r" };
            var consonatsV2 = new List<string> { "mm", "nn", "ng" };
            // SPLITS UP 2 SYMBOL VOWELS AND 1 SYMBOL CONSONANT
            List<string> vowel3S = new List<string>();
            foreach (string V1 in vowels) {
                foreach (string C1 in consonatsV1) {
                    vowel3S.Add($"{V1}{C1}");
                }
            }
            // SPLITS UP 2 SYMBOL VOWELS AND 2 SYMBOL CONSONANT
            List<string> vowel4S = new List<string>();
            foreach (string V1 in vowels) {
                foreach (string C1 in consonatsV2) {
                    vowel3S.Add($"{V1}{C1}");
                }
            }
            
            foreach (string s in original) {
                switch (s) {
                    case var str when dr.Contains(str) && !HasOto($"{str} {vowels}", note.tone) && !HasOto($"ay {str}", note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "d", s[1].ToString() });
                        break;
                    case var str when tr.Contains(str) && !HasOto($"{str} {vowels}", note.tone) && !HasOto($"ay {str}", note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "t", s[1].ToString() });
                        break;
                    case var str when wh.Contains(str) && !HasOto($"{str} {vowels}", note.tone) && !HasOto($"ay {str}", note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "hh", s[1].ToString() });
                        break;
                    case var str when av_c.Contains(str) && !HasOto($"b {str}", note.tone) && !HasOto(ValidateAlias(str, note.tone), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "aa", s[1].ToString() });
                        break;
                    case var str when ev_c.Contains(str) && !HasOto($"b {str}", note.tone) && !HasOto(ValidateAlias(str, note.tone), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "eh", s[1].ToString() });
                        break;
                    case var str when iv_c.Contains(str) && !HasOto($"b {str}", note.tone) && !HasOto(ValidateAlias(str, note.tone), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "iy", s[1].ToString() });
                        break;
                    case var str when ov_c.Contains(str) && !HasOto($"b {str}", note.tone) && !HasOto(ValidateAlias(str, note.tone), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "ao", s[1].ToString() });
                        break;
                    case var str when uv_c.Contains(str) && !HasOto($"b {str}", note.tone) && !HasOto(ValidateAlias(str, note.tone), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "uw", s[1].ToString() });
                        break;
                    case var str when vowel3S.Contains(str) && !HasOto($"b {str}", note.tone) && !HasOto(ValidateAlias(str, note.tone), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { s.Substring(0, 2), s[2].ToString() });
                        break;
                    case var str when vowel4S.Contains(str) && !HasOto($"b {str}", note.tone) && !HasOto(ValidateAlias(str, note.tone), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { s.Substring(0, 2), s.Substring(2, 2) });
                        break;
                    default:
                        finalProcessedPhonemes.Add(s);
                        break;
                }
            }
            return finalProcessedPhonemes.ToArray();
        }

        protected override IG2p[] GetBaseG2ps() {
            return new IG2p[] { new ArpabetPlusG2p() };
        }

        public override void SetSinger(USinger singer) {
            base.SetSinger(singer);

            if (this.singer != singer || this.localYamlGeneration != globalYamlGeneration) {
                this.singer = singer;
                this.localYamlGeneration = globalYamlGeneration;
                
                consExceptions.Clear();
                if (stop != null) consExceptions.AddRange(stop);
                if (tap != null) consExceptions.AddRange(tap);
                
                consExceptions = consExceptions.Distinct().ToList();
            }
        }

        // prioritize yaml replacements over dictionary replacements
        private string ReplacePhoneme(string phoneme, int tone) {
            if (dictionaryReplacements.TryGetValue(phoneme, out var replaced)) {
                return replaced;
            }
            if (HasOto(phoneme, tone) || HasOto(ValidateAlias(phoneme), tone)) {
                return phoneme;
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

            bool isAtomicCluster = cc.Length == 2 && ccvException.Contains(cc[0]);

            // Check for missing YAML fallback phonemes
            foreach (var entry in yamlFallbacks) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Value, syllable.tone)) {
                    isYamlFallbacks = true;
                    break;
                }
            }

            // For VC Fallback phonemes
            foreach (var entry in vcFallBacks) {
                if (!HasOto($"{entry.Key} {cc}", syllable.tone) || (!HasOto($"ao {cc}", syllable.tone))) {
                    vc_FallBack = true;
                }
            }

            // STARTING V
            if (syllable.IsStartingV) {
                basePhoneme = AliasFormat(v, "startingV", syllable.vowelTone, "");
            }
            // [V V] or [V C][C V]/[V]
            else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    
                    string vvSpace = $"{prevV} {v}";
                    string vvNoSpace = $"{prevV}{v}";
                    string validVvSpace = ValidateAlias(vvSpace, syllable.vowelTone);
                    string validVvNoSpace = ValidateAlias(vvNoSpace, syllable.vowelTone);

                    // VV with space
                    if (HasOto(vvSpace, syllable.vowelTone)) {
                        basePhoneme = vvSpace;
                    } else if (HasOto(validVvSpace, syllable.vowelTone)) {
                        basePhoneme = validVvSpace;
                    } 
                    // VV without space
                    else if (HasOto(vvNoSpace, syllable.vowelTone)) {
                        basePhoneme = vvNoSpace;
                    } else if (HasOto(validVvNoSpace, syllable.vowelTone)) {
                        basePhoneme = validVvNoSpace;
                    } 
                    
                    // Diphthong Fallbacks & Splitting
                    else if (diphthongSplits.ContainsKey(prevV) || diphthongTails.ContainsKey(prevV)) {
                        string cv = "";
                        if (diphthongSplits.ContainsKey(prevV)) {
                            var splitOverride = diphthongSplits[prevV];
                            var vc = AliasFormat(splitOverride[0].Replace("{v}", v), "vcEx", syllable.tone, prevV);
                            cv = AliasFormat(splitOverride[1].Replace("{v}", v), "dynMid", syllable.vowelTone, "");
                            TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc, syllable.tone));
                        } 
                        else { // Default YAML diphthong logic
                            var tail = diphthongTails[prevV];
                            var vcSpace = AliasFormat($"{prevV} {tail}", "vcEx", syllable.tone, prevV);
                            var vcNoSpace = AliasFormat($"{prevV}{tail}", "vcEx", syllable.tone, prevV);
                            cv = AliasFormat($"{tail} {v}", "dynMid", syllable.vowelTone, "");
                            TryAddPhoneme(phonemes, syllable.tone, vcSpace, ValidateAlias(vcSpace, syllable.tone), vcNoSpace, ValidateAlias(vcNoSpace, syllable.tone));
                        }

                        string validCv = ValidateAlias(cv, syllable.vowelTone);
                        string validV = ValidateAlias(v, syllable.vowelTone);
                        if (HasOto(cv, syllable.vowelTone)) {
                            basePhoneme = cv;
                        } else if (HasOto(validCv, syllable.vowelTone)) {
                            basePhoneme = validCv;
                        } else if (HasOto(v, syllable.vowelTone)) {
                            basePhoneme = v;
                        } else if (HasOto(validV, syllable.vowelTone)) {
                            basePhoneme = validV;
                        } else {
                            basePhoneme = ValidateAlias(AliasFormat($"- {v}", "dynMid", syllable.vowelTone, ""), syllable.vowelTone);
                            phonemes.Add(ValidateAlias(AliasFormat($"{prevV} -", "dynMid", syllable.tone, ""), syllable.tone));
                        }
                    } else {
                        string validV = ValidateAlias(v, syllable.vowelTone);
                        if (HasOto(v, syllable.vowelTone)) {
                            basePhoneme = v;
                        } else if (HasOto(validV, syllable.vowelTone)) {
                            basePhoneme = validV;
                        } else {
                            basePhoneme = ValidateAlias(AliasFormat($"- {v}", "dynMid", syllable.vowelTone, ""), syllable.vowelTone);
                            phonemes.Add(ValidateAlias(AliasFormat($"{prevV} -", "dynMid", syllable.tone, ""), syllable.tone));
                        }
                    }
                } 
                else {
                    basePhoneme = null;
                }
                // [- CV/C V] or [- C][CV/C V]
            } else if (syllable.IsStartingCVWithOneConsonant) {
                var rcv = $"- {cc[0]} {v}";
                var rcv1 = $"- {cc[0]}{v}";
                var crv = $"{cc[0]} {v}";
                /// - CV
                if (HasOto(rcv, syllable.vowelTone) || HasOto(ValidateAlias(rcv, syllable.vowelTone), syllable.vowelTone) || (HasOto(rcv1, syllable.vowelTone) || HasOto(ValidateAlias(rcv1, syllable.vowelTone), syllable.vowelTone))) {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynStart", syllable.vowelTone, "");
                    /// CV
                } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv, syllable.vowelTone), syllable.vowelTone)) {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynMid", syllable.vowelTone, "");
                    TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), syllable.vowelTone));
                } else {
                    basePhoneme = AliasFormat($"{cc[0]} {v}", "dynMid", syllable.vowelTone, "");
                    TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), syllable.vowelTone));
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
                if (HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv, syllable.vowelTone), syllable.vowelTone) || HasOto(rccv1, syllable.vowelTone) || HasOto(ValidateAlias(rccv1, syllable.vowelTone), syllable.vowelTone) && !isAtomicCluster) {
                    basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynStart", syllable.vowelTone, "");
                    lastC = 0;
                } else {
                    /// CCV and CV
                    if (!phoneticHint && (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv, syllable.vowelTone), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1, syllable.vowelTone), syllable.vowelTone))) {
                        basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynMid", syllable.vowelTone, "");
                        lastC = 0;
                    } else if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv, syllable.vowelTone), syllable.vowelTone) || HasOto(crv1, syllable.vowelTone) || HasOto(ValidateAlias(crv1, syllable.vowelTone), syllable.vowelTone)) {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    } else {
                        basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                    }
                    // TRY RCC [- CC]
                    if (!phoneticHint) {
                        for (var i = cc.Length; i > 1; i--) {
                            if (TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{string.Join("", cc.Take(i))}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{string.Join("", cc.Take(i))}", "cc_start", syllable.vowelTone, ""), syllable.vowelTone))) {
                                firstC = i - 1;
                                break;
                            }
                        }
                    }
                    // [- C]
                    // todo: deincremental search for starting consonant clusters [str] → [st] → [s]
                    if (phonemes.Count == 0) {
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), ValidateAlias(AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""), syllable.vowelTone));
                    }
                }
            } else {
                var crv = $"{cc.Last()} {v}";
                var cv = $"{cc.Last()}{v}";
                /// CV
                if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv, syllable.vowelTone), syllable.vowelTone) || HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv, syllable.vowelTone), syllable.vowelTone)) {
                    basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                } else {
                    basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                }
                // try [CC V] or [CCV]
                for (var i = firstC; i < cc.Length - 1; i++) {
                    var ccv = $"{string.Join("", cc)} {v}";
                    var ccv1 = $"{string.Join("", cc)}{v}";
                    /// CCV
                    if (CurrentWordCc.Length >= 2 && !isAtomicCluster) {
                        if (!phoneticHint && (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv, syllable.vowelTone), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1, syllable.vowelTone), syllable.vowelTone))) {
                            basePhoneme = AliasFormat($"{string.Join("", cc)} {v}", "dynMid", syllable.vowelTone, "");
                            lastC = i;
                            break;
                        }
                        /// C-Last
                    } else if (CurrentWordCc.Length == 1 && PreviousWordCc.Length == 1) {
                        if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv, syllable.vowelTone), syllable.vowelTone) || HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv, syllable.vowelTone), syllable.vowelTone)) {
                            basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                        } else {
                            basePhoneme = AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, "");
                        }
                    }
                }

                // try [V C], [V CC], [VC C], [V -][- C]
                for (var i = lastC + 1; i >= 0; i--) {
                    var vr = $"{prevV} -";
                    var vcc = $"{prevV} {string.Join("", cc.Take(2))}";
                    var vc = $"{prevV} {cc[0]}";
                    // Boolean Triggers
                    bool CCV = false;
                    if (!phoneticHint && CurrentWordCc.Length >= 2 && !isAtomicCluster) {
                        if (HasOto(AliasFormat($"{string.Join("", cc)} {v}", "dynMid", syllable.vowelTone, ""), syllable.vowelTone)) {
                            CCV = true;
                        }
                    }

                    bool lastVC = false;
                    for (int len = cc[0].Length; len > 0; len--) {
                        string c = cc[0].Substring(0, len);   // shr → sh → s
                        string vcTry = $"{prevV} {c}";

                        bool hasVC =
                            HasOto(vc, syllable.tone) ||
                            HasOto(ValidateAlias(vc, syllable.tone), syllable.tone);

                        if (!hasVC && (HasOto(vcTry, syllable.tone) || HasOto(ValidateAlias(vcTry, syllable.tone), syllable.tone))) {
                            phonemes.Add(vcTry);
                            lastVC = true;
                            break;
                        }
                    }
                    if (lastVC) {
                        break;
                    }

                    if (!lastVC && i == 0 && (HasOto(vr, syllable.tone) || HasOto(ValidateAlias(vr, syllable.tone), syllable.tone)) && !HasOto(vc, syllable.tone)) {
                        phonemes.Add(vr);
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.vowelTone, ""));
                        break;
                    } else if ((HasOto(vcc, syllable.tone) || HasOto(ValidateAlias(vcc, syllable.tone), syllable.tone)) && CCV) {
                        phonemes.Add(vcc);
                        firstC = 1;
                        break;
                    } else if (HasOto(vc, syllable.tone) || HasOto(ValidateAlias(vc, syllable.tone), syllable.tone)) {
                        TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc, syllable.tone));
                        break;
                    } else {
                        continue;
                    }
                }
            }
            for (var i = firstC; i < lastC; i++) {
                var ccv = $"{string.Join("", cc.Skip(i + 1))} {v}";
                var ccv1 = $"{string.Join("", cc.Skip(i + 1))}{v}";
                var cc1 = $"{string.Join(" ", cc.Skip(i))}";
                var lcv = $"{cc.Last()} {v}";
                var cv = $"{cc.Last()}{v}";

                for (int len = cc[i + 1].Length; len > 0; len--) {
                    string c = cc[i + 1].Substring(0, len);   // shr → sh → s
                    string ccTry = $"{cc[i]} {c}";

                    if (HasOto(ccTry, syllable.tone) && !(HasOto(cc1, syllable.tone) || HasOto(ValidateAlias(cc1, syllable.tone), syllable.tone))) {
                        cc1 = ccTry;
                        break;
                    }
                }

                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1, syllable.tone);
                }
                // [C1 C2]
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = $"{cc[i]} {cc[i + 1]}";
                }
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1, syllable.tone);
                }
                // CC FALLBACKS
                if (!HasOto(cc1, syllable.tone) || (!HasOto(ValidateAlias(cc1, syllable.tone), syllable.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", syllable.tone))) {
                    var c1 = cc[i];
                    var c2 = cc[i + 1];
                    bool c1IsException = consExceptions.Contains(c1);
                    bool c2IsException = consExceptions.Contains(c2);

                    // Scenario 1: Both are NOT exceptions
                    if (!c1IsException && !c2IsException) {
                        cc1 = AliasFormat($"{c2}", "cc_inB", syllable.vowelTone, "");
                        TryAddPhoneme(phonemes, syllable.tone, ValidateAlias(AliasFormat($"{c1}", "cc_endB", syllable.vowelTone, ""), syllable.vowelTone));
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
                    cc1 = ValidateAlias(cc1, syllable.tone);
                }
                // CCV
                if (CurrentWordCc.Length >= 2) {
                    bool canGlide = true;
                    if (!phoneticHint && (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv, syllable.vowelTone), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1, syllable.vowelTone), syllable.vowelTone) && !isAtomicCluster)) {
                        basePhoneme = (AliasFormat($"{string.Join("", cc.Skip(i + 1))} {v}", "dynMid", syllable.vowelTone, ""));
                        lastC = i;
                        canGlide = false;
                    } else if (HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv, syllable.vowelTone), syllable.vowelTone) || HasOto(lcv, syllable.vowelTone) || HasOto(ValidateAlias(lcv, syllable.vowelTone), syllable.vowelTone) && HasOto(cc1, syllable.vowelTone) && !HasOto(ccv, syllable.vowelTone)) {
                        basePhoneme = (AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, ""));
                    }
                    // [C1 C2C3]
                    if (!phoneticHint && (HasOto($"{cc[i]} {string.Join("", cc.Skip(i + 1))}", syllable.tone))) {
                        cc1 = $"{cc[i]} {string.Join("", cc.Skip(i + 1))}";
                        lastC = i;
                    }
                    if (canGlide) {
                        if (liquid.Contains(cc[i + 1]) || semivowel.Contains(cc[i + 1])
                            || liquid.Contains(ValidateAlias(cc[i + 1])) || semivowel.Contains(ValidateAlias(cc[i + 1]))) {
                            glides(cc1);
                        }
                    }
                    // CV
                } else if (CurrentWordCc.Length == 1 && PreviousWordCc.Length == 1) {
                    basePhoneme = (AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, ""));
                    // [C1 C2]
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = $"{cc[i]} {cc[i + 1]}";
                    }
                }

                if (i + 1 < lastC) {
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1, syllable.tone);
                    }
                    // [C1 C2]
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = $"{cc[i]} {cc[i + 1]}";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1, syllable.tone);
                    }
                    // CC FALLBACKS
                    if (!HasOto(cc1, syllable.tone) || (!HasOto(ValidateAlias(cc1, syllable.tone), syllable.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", syllable.tone))) {
                        var c1 = cc[i];
                        var c2 = cc[i + 1];
                        bool c1IsException = consExceptions.Contains(c1);
                        bool c2IsException = consExceptions.Contains(c2);

                        // Scenario 1: Both are NOT exceptions
                        if (!c1IsException && !c2IsException) {
                            // [C1 -] [- C2]
                            cc1 = AliasFormat($"{c2}", "cc_inB", syllable.vowelTone, "");
                            TryAddPhoneme(phonemes, syllable.tone, ValidateAlias(AliasFormat($"{c1}", "cc_endB", syllable.vowelTone, ""), syllable.vowelTone));
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
                        cc1 = ValidateAlias(cc1, syllable.tone);
                    }
                    // CCV
                    if (CurrentWordCc.Length >= 2) {
                        bool canGlide = true;
                        if (!phoneticHint && (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv, syllable.vowelTone), syllable.vowelTone) || HasOto(ccv1, syllable.vowelTone) || HasOto(ValidateAlias(ccv1, syllable.vowelTone), syllable.vowelTone) && !isAtomicCluster)) {
                            basePhoneme = (AliasFormat($"{string.Join("", cc.Skip(i + 1))} {v}", "dynMid", syllable.vowelTone, ""));
                            lastC = i;
                            canGlide = false;
                        } else if (HasOto(cv, syllable.vowelTone) || HasOto(ValidateAlias(cv, syllable.vowelTone), syllable.vowelTone) || HasOto(lcv, syllable.vowelTone) || HasOto(ValidateAlias(lcv, syllable.vowelTone), syllable.vowelTone) && HasOto(cc1, syllable.vowelTone) && !HasOto(ccv, syllable.vowelTone)) {
                            basePhoneme = (AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, ""));
                        }
                        // [C1 C2C3]
                        if (!phoneticHint && (HasOto($"{cc[i]} {string.Join("", cc.Skip(i + 1))}", syllable.tone))) {
                            cc1 = $"{cc[i]} {string.Join("", cc.Skip(i + 1))}";
                        }
                        if (canGlide) {
                            if (liquid.Contains(cc[i + 1]) || semivowel.Contains(cc[i + 1])
                                || liquid.Contains(ValidateAlias(cc[i + 1])) || semivowel.Contains(ValidateAlias(cc[i + 1]))) {
                                glides(cc1);
                            }
                        }
                        // CV
                    } else if (CurrentWordCc.Length == 1 && PreviousWordCc.Length == 1) {
                        basePhoneme = (AliasFormat($"{cc.Last()} {v}", "dynMid", syllable.vowelTone, ""));
                        // [C1 C2]
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = $"{cc[i]} {cc[i + 1]}";
                            lastC = i;
                        }
                    }
                    if (HasOto(cc1, syllable.tone) && HasOto(cc1, syllable.tone) && !cc1.Contains($"{string.Join("", cc.Skip(i))}")) {
                        // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                        phonemes.Add(cc1);
                    } else if (TryAddPhoneme(phonemes, syllable.tone, cc1, ValidateAlias(cc1, syllable.tone))) {
                        // like [V C1] [C1 C2] [C2 ..]
                        if (cc1.Contains($"{string.Join(" ", cc.Skip(i + 1))}")) {
                            i++;
                        }
                    } else {
                        // singular cc
                        if (PreviousWordCc.Contains(cc1) == CurrentWordCc.Contains(cc1)) {
                            cc1 = ValidateAlias(cc1, syllable.tone);
                        } else {
                            TryAddPhoneme(phonemes, syllable.tone, cc1, cc[i], ValidateAlias(cc[i], syllable.tone));
                        }
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
                var vR = $"{prevV} {t}";
                var vR2 = $"{prevV}{t}";
                if (HasOto(vR, ending.tone) || HasOto(ValidateAlias(vR, ending.tone), ending.tone) || HasOto(vR2, ending.tone) || HasOto(ValidateAlias(vR2, ending.tone), ending.tone)) {
                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{prevV}", "ending", ending.tone, "", t), ValidateAlias(AliasFormat($"{prevV}", "ending", ending.tone, "", t), ending.tone));
                }
            } else if (ending.IsEndingVCWithOneConsonant) {
                var vc = $"{prevV} {cc[0]}";
                var vcr = $"{prevV} {cc[0]}{t}";
                var vcr2 = $"{prevV}{cc[0]} {t}";
                var vcr3 = $"{prevV} {cc[0]} {t}";
                var vcr4 = $"{prevV}{cc[0]}{t}";
                if (!RomajiException.Contains(cc[0])) {
                    if (HasOto(vcr, ending.tone) && HasOto(ValidateAlias(vcr, ending.tone), ending.tone) || (HasOto(vcr2, ending.tone) && HasOto(ValidateAlias(vcr2, ending.tone), ending.tone))) {
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{v} {cc[0]}", "dynEnd", ending.tone, "", t), ValidateAlias(AliasFormat($"{v} {cc[0]}", "dynEnd", ending.tone, "", t), ending.tone));
                    } else if (HasOto(vcr3, ending.tone) && HasOto(ValidateAlias(vcr3, ending.tone), ending.tone)) {
                        TryAddPhoneme(phonemes, ending.tone, vcr3, ValidateAlias(vcr3, ending.tone));
                    } else if (HasOto(vcr4, ending.tone) && HasOto(ValidateAlias(vcr4, ending.tone), ending.tone)) {
                        TryAddPhoneme(phonemes, ending.tone, vcr4, ValidateAlias(vcr4, ending.tone));
                    } else if (HasOto(vc, ending.tone) && HasOto(ValidateAlias(vc, ending.tone), ending.tone)) {
                        TryAddPhoneme(phonemes, ending.tone, vc, ValidateAlias(vc, ending.tone));
                        if (vc.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "ending", ending.tone, "", t), ValidateAlias(AliasFormat($"{cc[0]}", "ending", ending.tone, "", t), ending.tone));
                        }
                    } else {
                        for (int len = cc[0].Length; len > 0; len--) {
                            string c = cc[0].Substring(0, len);   // shr → sh → s
                            string vcTry = $"{prevV} {c}";
                            if ( HasOto(vcTry, ending.tone) || HasOto(ValidateAlias(vcTry, ending.tone), ending.tone)) {
                                TryAddPhoneme(phonemes, ending.tone, vcTry, ValidateAlias(vcTry, ending.tone));
                                break;
                            }
                        }
                        if (vc.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "ending", ending.tone, "", t), ValidateAlias(AliasFormat($"{cc[0]}", "ending", ending.tone, "", t), ending.tone));
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
                            if (HasOto(vr, ending.tone) || HasOto(ValidateAlias(vr, ending.tone), ending.tone) || HasOto(vr2, ending.tone) || HasOto(ValidateAlias(vr2, ending.tone), ending.tone) || HasOto(vr1, ending.tone) || HasOto(ValidateAlias(vr1, ending.tone), ending.tone) && !HasOto(vc, ending.tone)) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{v}", "ending", ending.tone, "", t), ValidateAlias(AliasFormat($"{v}", "ending", ending.tone, "", t), ending.tone));
                            }
                            break;
                        } else if (HasOto(vcc, ending.tone) && HasOto(ValidateAlias(vcc, ending.tone), ending.tone) && lastC == 1 && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc, ValidateAlias(vcc, ending.tone));
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc2, ending.tone) && HasOto(ValidateAlias(vcc2, ending.tone), ending.tone) && lastC == 1 && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc2, ValidateAlias(vcc2, ending.tone));
                            firstC = 1;
                            break;
                        } else if (!phoneticHint && (HasOto(vcc3, ending.tone) && HasOto(ValidateAlias(vcc3, ending.tone), ending.tone) && !ccvException.Contains(cc[0]))) {
                            TryAddPhoneme(phonemes, ending.tone, vcc3, ValidateAlias(vcc3, ending.tone));
                            if (vcc3.EndsWith(cc.Last()) && lastC == 1) {
                                if (consonants.Contains(cc.Last())) {
                                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc.Last()}", "ending", ending.tone, "", t), ValidateAlias(AliasFormat($"{cc.Last()}", "ending", ending.tone, "", t), ending.tone));
                                }
                            }
                            firstC = 1;
                            break;
                        } else if (!phoneticHint && (HasOto(vcc4, ending.tone) && HasOto(ValidateAlias(vcc4, ending.tone), ending.tone) && !ccvException.Contains(cc[0]))) {
                            TryAddPhoneme(phonemes, ending.tone, vcc4, ValidateAlias(vcc4, ending.tone));
                            if (vcc4.EndsWith(cc.Last()) && lastC == 1) {
                                if (consonants.Contains(cc.Last())) {
                                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc.Last()}", "ending", ending.tone, "", t), ValidateAlias(AliasFormat($"{cc.Last()}", "ending", ending.tone, "", t), ending.tone));
                                }
                            }
                            firstC = 1;
                            break;
                        } else if (!!HasOto(vcc, ending.tone) && !HasOto(ValidateAlias(vcc, ending.tone), ending.tone)
                                || !HasOto(vcc2, ending.tone) && HasOto(ValidateAlias(vcc2, ending.tone), ending.tone)
                                || !HasOto(vcc3, ending.tone) && HasOto(ValidateAlias(vcc3, ending.tone), ending.tone)
                                || !HasOto(vcc4, ending.tone) && HasOto(ValidateAlias(vcc4, ending.tone), ending.tone)) {
                            TryAddPhoneme(phonemes, ending.tone, vc, ValidateAlias(vc, ending.tone));
                            break;
                        } else {
                            for (int len = cc[0].Length; len > 0; len--) {
                                string c = cc[0].Substring(0, len);   // shr → sh → s
                                string vcTry = $"{prevV} {c}";
                                if (HasOto(vcTry, ending.tone) || HasOto(ValidateAlias(vcTry, ending.tone), ending.tone)) {
                                    TryAddPhoneme(phonemes, ending.tone, vcTry, ValidateAlias(vcTry, ending.tone));
                                    break;
                                }
                            }
                            break;
                        }
                    }
                }
                for (var i = firstC; i < lastC; i++) {
                    var cc1 = $"{cc[i]} {cc[i + 1]}";
                    if (i < cc.Length - 2) {
                        var cc2 = $"{cc[i + 1]} {cc[i + 2]}";

                        for (int len = cc[i + 2].Length; len > 0; len--) {
                            string c = cc[i + 2].Substring(0, len);   // shr → sh → s
                            string ccTry = $"{cc[i + 1]} {c}";

                            if (HasOto(ccTry, ending.tone) && !(HasOto(cc1, ending.tone) || HasOto(ValidateAlias(cc1, ending.tone), ending.tone))) {
                                cc1 = ccTry;
                                break;
                            }
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1, ending.tone);
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2, ending.tone);
                        }

                        if (!HasOto(cc2, ending.tone) && !HasOto($"{cc[i + 1]} {cc[i + 2]}", ending.tone)) {
                            // [C1 -] [- C2]
                            cc2 = AliasFormat($"{cc[i + 2]}", "cc_inB", ending.tone, "");
                            TryAddPhoneme(phonemes, ending.tone, ValidateAlias(AliasFormat($"{cc[i + 1]}", "cc_endB", ending.tone, ""), ending.tone));
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1, ending.tone);
                        }

                        if (HasOto(cc1, ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone), ending.tone))) {
                            // like [C1 C2][C2 ...]
                            phonemes.Add(cc1);
                        } else if ((HasOto(cc[i], ending.tone) || HasOto(ValidateAlias(cc[i], ending.tone), ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone), ending.tone)))) {
                            // like [C1 C2-][C3 ...]
                            phonemes.Add(cc[i]);
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {cc[i + 2]}{t}", ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]}{cc[i + 2]}", ValidateAlias($"{cc[i + 1]}{cc[i + 2]}", ending.tone))) {
                            // like [C1C2][C2 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1, ending.tone))) {
                            i++;
                        } else if (!HasOto(cc1, ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            // [C1 -] [- C2]
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_inB", ending.tone, ""));
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_endB", ending.tone, ""));
                            i++;
                        } else {
                            // like [C1][C2 ...]
                            TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i], ending.tone), $"{cc[i]} {t}", ValidateAlias($"{cc[i]} {t}", ending.tone));
                            TryAddPhoneme(phonemes, ending.tone, cc[i + 1], ValidateAlias(cc[i + 1], ending.tone), $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}", ending.tone));
                            i++;
                        }
                        // CC that ends with 3 clusters
                        for (int clusterLength = 3; clusterLength >= 2; clusterLength--) {
                            if (i + clusterLength > cc.Length) {
                                continue;
                            }
                            var cluster = new string[clusterLength];
                            for (int k = 0; k < clusterLength; k++) {
                                cluster[k] = cc[i + k].ToString();
                            }
                            // Generate all possible spacing patterns for the consonants.
                            var consonantPatterns = new List<string>();
                            consonantPatterns.Add(string.Join("", cluster));

                            // 3 CC.
                            if (!phoneticHint && clusterLength == 3) {
                                consonantPatterns.Add($"{cluster[0]} {cluster[1]}{cluster[2]}");
                                consonantPatterns.Add($"{cluster[0]}{cluster[1]} {cluster[2]}");
                                consonantPatterns.Add($"{cluster[0]} {cluster[1]} {cluster[2]}");
                            }
                            // 2 CC.
                            else if (clusterLength == 2) {
                                consonantPatterns.Add($"{cluster[0]} {cluster[1]}");
                                consonantPatterns.Add($"{cluster[0]}{cluster[1]}");
                            }

                            foreach (var consPattern in consonantPatterns) {
                                string[] hyphenPatterns = { $"{t}", $" {t}" };
                                foreach (var hyphenPattern in hyphenPatterns) {
                                    string endingcc = $"{consPattern}{hyphenPattern}";

                                    if (TryAddPhoneme(phonemes, ending.tone, endingcc, ValidateAlias(endingcc, ending.tone))) {
                                        i += clusterLength - 1;
                                    }
                                }
                            }
                        }
                    } else {
                        for (int len = cc[i + 1].Length; len > 0; len--) {
                            string c = cc[i + 1].Substring(0, len);   // shr → sh → s
                            string ccTry = $"{cc[i]} {c}";

                            if (HasOto(ccTry, ending.tone) && !(HasOto(cc1, ending.tone) || HasOto(ValidateAlias(cc1, ending.tone), ending.tone))) {
                                cc1 = ccTry;
                                break;
                            }
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1, ending.tone);
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = $"{cc[i]} {cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1, ending.tone);
                        }
                        // [C1 -] [- C2]
                        if (!HasOto(cc1, ending.tone) || !HasOto(ValidateAlias(cc1, ending.tone), ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            cc1 = AliasFormat($"{cc[i + 1]}", "cc_inB", ending.tone, "");
                            TryAddPhoneme(phonemes, ending.tone, ValidateAlias(AliasFormat($"{cc[i]}", "cc_endB", ending.tone, ""), ending.tone));
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1, ending.tone);
                        }
                        // CC that ends with 2 clusters
                        if (!phoneticHint && (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]}{t}", ValidateAlias($"{cc[i]} {cc[i + 1]}{t}", ending.tone)))) {
                            // like [C1 C2-]
                            i++;
                        } else if (!phoneticHint && (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]} {t}", ValidateAlias($"{cc[i]} {cc[i + 1]} {t}", ending.tone)))) {
                            // like [C1 C2 -]
                            i++;
                        } else if (!phoneticHint && (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]}{t}", ValidateAlias($"{cc[i]}{cc[i + 1]}{t}", ending.tone)))) {
                            // like [C1C2-]
                            i++;
                        } else if (!phoneticHint && (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]} {t}", ValidateAlias($"{cc[i]}{cc[i + 1]} {t}", ending.tone)))) {
                            // like [C1C2 -]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1, ending.tone))) {
                            // like [C1 C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}", ending.tone), cc[i + 1], ValidateAlias(cc[i + 1], ending.tone));
                            i++;
                        } else if (!HasOto(cc1, ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            // [C1 -] [- C2]
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_inB", ending.tone, ""));
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 2]}", "cc_endB", ending.tone, ""));
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
                // If the alias contains a space, split it into consonant and vowel
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
                    } else if (HasOto(ValidateAlias(variation, tone), tone)) {
                        return ValidateAlias(variation, tone);
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
                    } else if (HasOto(ValidateAlias(variation1, tone), tone)) {
                        return ValidateAlias(variation1, tone);
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
                    } else if (HasOto(ValidateAlias(variation1, tone), tone)) {
                        return ValidateAlias(variation1, tone);
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

                if (HasOto(aliasFormat, tone)) {
                    return aliasFormat;
                } else if (HasOto(ValidateAlias(aliasFormat, tone), tone)) {
                    return ValidateAlias(aliasFormat, tone);
                }
            }
            return alias;
        }

        protected string ValidateAlias(string alias, int tone) {
            if (HasOto(alias, tone)) return alias;

            // YAML Fallbacks
            if (yamlFallbacks != null && yamlFallbacks.Count > 0) {
                string originalYaml = alias;
                foreach (var fb in yamlFallbacks.OrderByDescending(f => f.Key.Length)) {
                    alias = alias.Replace(fb.Key, fb.Value);
                }
                // If YAML changed something, test the NEW string!
                if (alias != originalYaml && HasOto(alias, tone)) return alias;
            }

            // Apply Vowel-Only global fallbacks
            string vAlias = alias;
            if (missingVphonemes != null) {
                foreach (var fb in missingVphonemes.OrderByDescending(f => f.Key.Length)) {
                    vAlias = vAlias.Replace(fb.Key, fb.Value);
                }
            }
            if (vAlias != alias && HasOto(vAlias, tone)) return vAlias;

            // Apply Consonant-Only global fallbacks
            string cAlias = alias;
            if (missingCphonemes != null) {
                foreach (var fb in missingCphonemes.OrderByDescending(f => f.Key.Length)) {
                    cAlias = cAlias.Replace(fb.Key, fb.Value);
                }
            }
            if (cAlias != alias && HasOto(cAlias, tone)) return cAlias;

            // contextual array fallbacks
            string contextualAlias = ApplyContextualFallbacks(alias, tone);
            if (contextualAlias != alias) return contextualAlias;

            return base.ValidateAlias(alias);
        }

        // VV FALLBACKS, START and END
        private readonly Dictionary<string, string[]> vvVowel1Fallbacks = new Dictionary<string, string[]> {
            { "aa", new[] { "ah", "ay", "aw", "ae", "ao" } },
            { "ae", new[] { "eh", "aw", "ay", "ah", "aa" } },
            { "ah", new[] { "aa", "aw", "ay", "ae", "ao" } },
            { "ao", new[] { "aa", "ow", "ah", "ay", "ae" } },
            { "ax", new[] { "ah", "uh", "aa" } },
            { "eh", new[] { "ey", "ax" } },
            { "er", new[] { "r", "ah", "ax" } },
            { "ih", new[] { "iy", "eh" } },
            { "iy", new[] { "ih" } },
            { "uh", new[] { "uw" } },
            { "uw", new[] { "uh" } },
            { "aw", new[] { "ae", "aa", "ah", "ay" } },
            { "ay", new[] { "ah", "ae", "aw", "aa" } },
            { "ey", new[] { "eh", "ae" } },
            { "oy", new[] { "ow", "ao" } },
            { "ow", new[] { "oy", "ao" } }
        };

        private readonly Dictionary<string, string[]> vvVowel2Fallbacks = new Dictionary<string, string[]> {
            { "aa", new[] { "ah", "ay", "aw", "ae", "ao" } },
            { "ae", new[] { "eh", "aw", "ay", "ah", "aa" } },
            { "ah", new[] { "aa", "aw", "ay", "ae", "ao" } },
            { "ao", new[] { "aa", "ow", "ah", "ay", "ae" } },
            { "ax", new[] { "ah", "uh", "aa" } },
            { "eh", new[] { "ey", "ax" } },
            { "er", new[] { "r", "ah", "ax" } },
            { "ih", new[] { "iy", "eh" } },
            { "uh", new[] { "uw" } },
        };

        // CV FALLBACKS
        private readonly Dictionary<string, string[]> cvConsonantFallbacks = new Dictionary<string, string[]> {
            { "b", new[] { "p", "d", "v" } },
            { "ch", new[] { "sh", "jh"} },
            { "d", new[] { "p", "d", "v" } },
            { "dh", new[] { "d", "v"} },
            { "dx", new[] { "d" } },
            { "f", new[] { "hh", "p", "th" } },
            { "g", new[] { "k" } },
            { "hh", new[] { "f" } },
            { "jh", new[] { "ch" } },
            { "k", new[] { "g" } },
            { "l", new[] { "r" } },
            { "m", new[] { "n" } },
            { "n", new[] { "m" } },
            { "ng", new[] { "n" } },
            { "p", new[] { "b", "d" } },
            { "q", new[] { "-" } },
            { "r", new[] { "er", "w", "l" } },
            { "s", new[] { "z", "f" } },
            { "sh", new[] { "s", "zh" } },
            { "t", new[] { "d", "k" } },
            { "th", new[] { "s", "th" } },
            { "v", new[] { "b", "f", "zh" } },
            { "w", new[] { "uw", "uh" } },
            { "y", new[] { "iy" } },
            { "z", new[] { "s" } },
            { "zh", new[] { "sh", "jh", "ch"} },
        };

        private readonly Dictionary<string, string[]> cvVowelFallbacks = new Dictionary<string, string[]> {
            { "aa", new[] { "ah", "ay", "aw", "ae", "ao" } },
            { "ae", new[] { "eh", "aw", "ay", "ah", "aa" } },
            { "ah", new[] { "aa", "aw", "ay", "ae", "ao" } },
            { "ao", new[] { "aa", "ow", "ah", "ay", "ae" } },
            { "ax", new[] { "ah", "uh", "aa" } },
            { "eh", new[] { "ey", "ax" } },
            { "ih", new[] { "iy", "eh" } },
            { "iy", new[] { "ih" } },
            { "uh", new[] { "uw" } },
            { "uw", new[] { "uh" } },
            { "aw", new[] { "ae", "aa", "ah", "ay" } },
            { "ay", new[] { "ah", "ae", "aw", "aa" } },
            { "ey", new[] { "eh", "ae" } },
            { "oy", new[] { "ow", "ao" } },
            { "ow", new[] { "oy", "ao" } }
        };

        // VC FALLBACKS
        private readonly Dictionary<string, string[]> vcVowelFallbacks = new Dictionary<string, string[]> {
            { "aa", new[] { "ah", "ae", "ao" } },
            { "ae", new[] { "eh", "ah", "aa" } },
            { "ah", new[] { "aa", "ae", "ao" } },
            { "ao", new[] { "aa", "ow", "ah", "ae" } },
            { "ax", new[] { "ah", "aa", "uh" } },
            { "eh", new[] { "ah", "ey" } },
            { "er", new[] { "r", "ah", "ax" } },
            { "ih", new[] { "iy", "eh" } },
            { "iy", new[] { "ih" } },
            { "uh", new[] { "uw" } },
            { "uw", new[] { "uh" } },
            { "aw", new[] { "uw", "uh" } },
            { "ay", new[] { "iy", "ih", "y" } },
            { "ey", new[] { "iy", "ih", "y" } },
            { "oy", new[] { "iy", "ih", "y" } },
            { "ow", new[] { "uw", "uh" } }
        };

        private readonly Dictionary<string, string[]> vcConsonantFallbacks = new Dictionary<string, string[]> {
            { "b", new[] { "p", "d", "v" } },
            { "ch", new[] { "t", "k", "q", "p", "g" } },
            { "d", new[] { "p", "b", "g" } },
            { "dh", new[] { "d", "v"} },
            { "dx", new[] { "d", "t", "r" } },
            { "f", new[] { "s", "p", "th" } },
            { "g", new[] { "k", "p", "b" } },
            { "hh", new[] { "f", "th" } },
            { "jh", new[] { "d", "b", "g" } },
            { "k", new[] { "t", "d", "g" } },
            { "l", new[] { "r" } },
            { "m", new[] { "n" } },
            { "n", new[] { "m" } },
            { "ng", new[] { "n", "m" } },
            { "p", new[] { "b", "d", "g" } },
            { "q", new[] { "t", "-" } },
            { "r", new[] { "l", "w" } },
            { "s", new[] { "z", "f" } },
            { "sh", new[] { "s", "zh" } },
            { "t", new[] { "d", "k" } },
            { "th", new[] { "s", "th" } },
            { "v", new[] { "f", "b", "zh" } },
            { "w", new[] { "uw", "uh" } },
            { "y", new[] { "iy" } },
            { "z", new[] { "s" } },
            { "zh", new[] { "sh", "jh", "ch"} },
        };

        // CC FALLBACKS
        private readonly Dictionary<string, string[]> ccConsonant1Fallbacks = new Dictionary<string, string[]> {
            { "b", new[] { "p", "d", "v" } },
            { "d", new[] { "p", "b", "g" } },
            { "dh", new[] { "d", "v"} },
            { "dx", new[] { "d", "t", "r" } },
            { "f", new[] { "s", "p", "th" } },
            { "g", new[] { "k", "p", "b" } },
            { "hh", new[] { "f", "th" } },
            { "k", new[] { "g", "d", "t" } },
            { "l", new[] { "r" } },
            { "m", new[] { "n" } },
            { "n", new[] { "m" } },
            { "ng", new[] { "n", "m" } },
            { "p", new[] { "b", "d", "g" } },
            { "q", new[] { "t", "-" } },
            { "r", new[] { "w", "l" } },
            { "s", new[] { "z", "f" } },
            { "sh", new[] { "s", "zh" } },
            { "t", new[] { "d", "k" } },
            { "th", new[] { "s", "th" } },
            { "v", new[] { "f", "b", "zh" } },
            { "w", new[] { "uw", "uh" } },
            { "y", new[] { "iy", "ih" } },
            { "z", new[] { "s" } },
            { "zh", new[] { "sh", "jh", "ch"} },
        };

        private readonly Dictionary<string, string[]> ccConsonant2Fallbacks = new Dictionary<string, string[]> {
            { "b", new[] { "p", "d", "v" } },
            { "ch", new[] { "sh", "jh"} },
            { "d", new[] { "p", "d", "v" } },
            { "dh", new[] { "d", "v"} },
            { "dx", new[] { "d" } },
            { "f", new[] { "hh", "p", "th" } },
            { "g", new[] { "k" } },
            { "hh", new[] { "f" } },
            { "jh", new[] { "ch" } },
            { "k", new[] { "g" } },
            { "l", new[] { "r" } },
            { "m", new[] { "n" } },
            { "n", new[] { "m" } },
            { "ng", new[] { "n" } },
            { "p", new[] { "b", "d" } },
            { "q", new[] { "-" } },
            { "r", new[] { "w", "l" } },
            { "s", new[] { "z", "f" } },
            { "sh", new[] { "s", "zh" } },
            { "t", new[] { "d", "k" } },
            { "th", new[] { "s", "th" } },
            { "v", new[] { "b", "f", "zh" } },
            { "w", new[] { "uw", "uh" } },
            { "y", new[] { "iy" } },
            { "z", new[] { "s" } },
            { "zh", new[] { "jh", "ch"} },
        };

        private string ApplyContextualFallbacks(string alias, int tone) {
            string p1 = null;
            string p2 = null;
            bool hasSpace = alias.Contains(' ');

            if (hasSpace) {
                var parts = alias.Split(' ');
                if (parts.Length == 2) {
                    p1 = parts[0];
                    p2 = parts[1];
                }
            } else {
                var allPhonemes = vowels.Concat(consonants).Concat(new[] { "-", "R" }).OrderByDescending(p => p.Length);
                foreach (var ph1 in allPhonemes) {
                    if (alias.StartsWith(ph1)) {
                        string remainder = alias.Substring(ph1.Length);
                        if (vowels.Contains(remainder) || consonants.Contains(remainder) || remainder == "-" || remainder == "R") {
                            p1 = ph1;
                            p2 = remainder;
                            break;
                        }
                    }
                }
            }

            if (p1 == null || p2 == null) return alias;

            int GetPhType(string ph) {
                if (tails.Contains(ph)) return 0; // Rest
                if (vowels.Contains(ph)) return 1;    // Vowel
                if (consonants.Contains(ph)) return 2; // Consonant
                return -1; // Unknown
            }

            int type1 = GetPhType(p1);
            int type2 = GetPhType(p2);
            var dict1 = new Dictionary<string, string[]>();
            var dict2 = new Dictionary<string, string[]>();

            if (type1 == 2 && type2 == 1) { // CV
                dict1 = cvConsonantFallbacks; dict2 = cvVowelFallbacks;
            } 
            else if (type1 == 1 && type2 == 2) { // VC
                dict1 = vcVowelFallbacks; dict2 = vcConsonantFallbacks;
            } 
            else if (type1 == 2 && type2 == 2) { // CC
                dict1 = ccConsonant1Fallbacks; dict2 = ccConsonant2Fallbacks;
            } 
            else if (type1 == 1 && type2 == 1) { // VV
                dict1 = vvVowel1Fallbacks; dict2 = vvVowel2Fallbacks;
            }
            else if (type1 == 0 && type2 == 1) { // Starting Vowel (- V)
                dict2 = cvVowelFallbacks; // Fallback the vowel normally
            }
            else if (type1 == 1 && type2 == 0) { // Ending Vowel (V -)
                dict1 = vcVowelFallbacks; // Fallback the vowel normally
            }
            else if (type1 == 0 && type2 == 2) { // Starting Consonant (- C)
                dict2 = cvConsonantFallbacks; 
            }
            else if (type1 == 2 && type2 == 0) { // Ending Consonant (C -)
                dict1 = vcConsonantFallbacks; 
            }
            return FindValidCombination(p1, p2, dict1, dict2, tone, hasSpace) ?? alias;
        }

        private string FindValidCombination(string part1, string part2, Dictionary<string, string[]> dict1, Dictionary<string, string[]> dict2, int tone, bool hasSpace) {
            var p1Options = new List<string> { part1 };
            if (dict1.TryGetValue(part1, out var fallbacks1)) {
                p1Options.AddRange(fallbacks1);
            }
            var p2Options = new List<string> { part2 };
            if (dict2.TryGetValue(part2, out var fallbacks2)) {
                p2Options.AddRange(fallbacks2);
            }

            // Iterate through all possible combinations
            foreach (var opt1 in p1Options) {
                foreach (var opt2 in p2Options) {
                    if (opt1 == part1 && opt2 == part2) continue; 
                    string tryAlias = hasSpace ? $"{opt1} {opt2}" : $"{opt1}{opt2}";
                    
                    if (HasOto(tryAlias, tone)) {
                        return tryAlias;
                    }
                }
            }
            return null; 
        }

        // Endings has 50 ticks gap
        protected override bool NoGap => true;
        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            double otoLength = GetTransitionBasicLengthMsByOto(alias, tone, attr);

            var sortedOverrides = PhonemeOverrides.OrderByDescending(kv => kv.Key.Length);
            foreach (var kvp in sortedOverrides) {
                var symbol = kvp.Key;
                var value = kvp.Value;

                if (Regex.IsMatch(alias, $@"(?<![a-zA-Z]){Regex.Escape(symbol)}(?![a-zA-Z])")) {
                    return GetTransitionBasicLengthMsByConstant() * value;
                }
            }

            return otoLength;
        }
    }
} 