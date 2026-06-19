using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("German VCCV Phonemizer", "DE VCCV", "Lotte V", language: "DE")]
    public class GermanVCCVPhonemizer : SyllableBasedPhonemizer {
        /// <summary>
        /// German VCCV phonemizer.
        /// Based on UTAU Felix's VCCV voicebank.
        /// Pronunciation reference: https://docs.google.com/spreadsheets/d/12E62ImRDOXyS6g6BFJHT9pOU2cFmNrch1UEmuTZqeak/edit?pli=1#gid=0
        /// </summary>
        /// 
        protected override string YamlFileName => "de_vccv.yaml";
        protected override byte[] YamlTemplate => Data.Resources.de_vccv_template;
        public GermanVCCVPhonemizer() {
            this.vowels = "a,6,e,E,2,9,i,I,y,Y,u,U,o,O,@,aU,OY,aI".Split(',');
            this.consonants = "-,b,C,d,f,g,h,j,k,kh,l,m,n,N,p,ph,R;,s,S,t,th,v,x,z,Z,dZ,ks,pf,st,St,tS,w".Split(',');
            this.dictionaryReplacements = ("aa=a,ae=E,ah=@,ao=O,aw=aU,ax=@,ay=aI," +
            "b=b,cc=C,ch=tS,d=d,dh=z," + "ee=e,eh=E,er=6,ex=6," + "f=f,g=g,hh=h,ih=I,iy=i,jh=dZ,k=k,l=l,m=m,n=n,ng=N," +
            "oe=9,ohh=2,ooh=o,oy=OY," + "p=p,pf=pf,q=-,r=R;,rr=R;,s=s,sh=S,t=t," + "th=s,ts=ts," + "ue=y,uh=U,uw=u," + "v=v,w=w,x=x,y=j," +
            "yy=Y," + "z=z,zh=Z").Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        }
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_de.txt";
        private bool isYamlFallbacks = false;

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

            // Load base g2p.
            g2ps.Add(new GermanG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (original == null) {
                return null;
            }
            List<string> finalProcessedPhonemes = new List<string>();

            string[] diphthongs = new[] { "aU", "OY", "aI" };
            foreach (string s in original) {
                if (diphthongs.Contains(s)) {
                    finalProcessedPhonemes.AddRange(new string[] { s[0].ToString(), s[1] + '^'.ToString() });
                } else {
                    finalProcessedPhonemes.Add(s);
                }
            }
            return finalProcessedPhonemes.ToArray();
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

            foreach (var entry in yamlFallbacks) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Key, syllable.tone)) {
                    isYamlFallbacks = true;
                    break;
                }
            }

            if (syllable.IsStartingV) {
                basePhoneme = $"- {v}"; ;
            } else if (syllable.IsVV) {
                var vv = $"{prevV} {v}";
                if (!CanMakeAliasExtension(syllable)) {
                    if (HasOto(vv, syllable.vowelTone) || HasOto(ValidateAlias(vv), syllable.vowelTone)) {
                        basePhoneme = vv;
                    } else {
                        basePhoneme = $"-{v}";
                        phonemes.Add($"{prevV} -");
                    }
                } else {
                    // the previous alias will be extended
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                var rcv = $"- {cc[0]}{v}";
                if (HasOto(rcv, syllable.vowelTone)) {
                    basePhoneme = rcv;
                } else {
                    basePhoneme = $"{cc[0]} {v}";
                }
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                var rccv = $"- {string.Join("", cc)}{v}";
                if (HasOto(rccv, syllable.vowelTone)) {
                    basePhoneme = rccv;
                } else {
                    basePhoneme = $"{cc.Last()}{v}";
                    // try RCC, with or without schwa
                    for (var i = cc.Length; i > 1; i--) {
                        if (TryAddPhoneme(phonemes, syllable.tone, $"- {string.Join("", cc.Take(i))}", $"- {string.Join("", cc.Take(i))}@")) {
                            firstC = i - 1;
                            break;
                        }
                    }
                    if (phonemes.Count == 0) {
                        // try RC with schwa if no RCC
                        TryAddPhoneme(phonemes, syllable.tone, $"- {cc[0]}@");
                    }
                }
            } else {
                var crv = $"{cc.Last()} {v}";
                if (HasOto(crv, syllable.vowelTone) || HasOto(ValidateAlias(crv), syllable.vowelTone)) {
                    basePhoneme = crv;
                } else {
                    basePhoneme = $"{cc.Last()}{v}";
                }
                // try CCV
                if (cc.Length - firstC > 1) {
                    for (var i = firstC; i < cc.Length; i++) {
                        var ccv = $"{string.Join("", cc.Skip(i))}{v}";
                        if (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                            lastC = i;
                            basePhoneme = ccv;
                            break;
                        }
                    }
                }
                // try vcc
                for (var i = lastC + 1; i >= 0; i--) {
                    var vcc = $"{prevV} {string.Join("", cc.Take(3))}";
                    var vcc2 = $"{prevV}{string.Join("", cc.Take(2))}";
                    var vc = $"{prevV} {cc[0]}";
                    var vc2 = $"{prevV}{cc[0]}";
                    if (i == 0) {
                        phonemes.Add(vc);
                    } else if (HasOto(vcc, syllable.tone) || HasOto(ValidateAlias(vcc), syllable.tone)) {
                        phonemes.Add(vcc);
                        firstC = 1;
                        break;
                    } else if (HasOto(vcc2, syllable.tone) || HasOto(ValidateAlias(vcc2), syllable.tone)) {
                        phonemes.Add(vcc2);
                        firstC = 1;
                        break;
                    } else if (HasOto(vc2, syllable.tone) || HasOto(ValidateAlias(vc2), syllable.tone)) {
                        phonemes.Add(vc2);
                        break;
                    } else if (HasOto(vc, syllable.tone) || HasOto(ValidateAlias(vc), syllable.tone)) {
                        phonemes.Add(vc);
                        break;
                    } else {
                        continue;
                    }
                }
            }
            for (var i = firstC; i < lastC; i++) {
                // we could use some CCV, so lastC is used
                // we could use -CC so firstC is used
                var rccv = $"- {string.Join("", cc)}{v}";
                var cc1 = $"{string.Join("", cc.Skip(i))}";
                var ccv = string.Join("", cc.Skip(i)) + v;
                if (!HasOto(rccv, syllable.vowelTone)) {
                    if (!HasOto(cc1, syllable.tone)) {
                        // joined CC
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        // [C1][C2] -
                        cc1 = $"{cc[i]}{cc[i + 1]} -";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        // [C1][C2]
                        cc1 = $"{cc[i]}{cc[i + 1]}";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        // [C1] [C2]
                        cc1 = $"{cc[i]} {cc[i + 1]}";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        // use schwa when no CC
                        cc1 = $"{cc[i]}{cc[i + 1]}@";
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (HasOto(ccv, syllable.vowelTone)) {
                        basePhoneme = ccv;
                    }
                    if (i + 1 < lastC) {
                        var cc2 = $"{string.Join("", cc.Skip(i))}";
                        if (!HasOto(cc2, syllable.tone)) {
                            // joined CC
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            // [C2][C3] -
                            cc2 = $"{cc[i + 1]}{cc[i + 2]} -";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            // [C2][C3]
                            cc2 = $"{cc[i + 1]}{cc[i + 2]}";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            // [C2] [C3]
                            cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            // use schwa when no CC
                            cc2 = $"{cc[i + 1]}{cc[i + 2]}@";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (HasOto(ccv, syllable.vowelTone)) {
                            basePhoneme = ccv;
                        }
                        if (TryAddPhoneme(phonemes, syllable.tone, $"{cc[i]}{cc[i + 1]}{cc[i + 2]} -")) {
                            // if it exists, use [C1][C2][C3] -
                            i++;
                        } else if (HasOto(cc1, syllable.tone) && HasOto(cc2, syllable.tone)) {
                            // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                            phonemes.Add(cc1);
                        } else if (TryAddPhoneme(phonemes, syllable.tone, cc1)) {
                            //    // like [V C1] [C1 C2] [C2 ..]
                        }
                    } else {
                        TryAddPhoneme(phonemes, syllable.tone, cc1);
                    }
                }
            }
            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc.Select(c => ReplacePhoneme(c, ending.tone)).ToArray();
            string v = ReplacePhoneme(ending.prevV, ending.tone);
            string t = ending.HasTail ? ReplacePhoneme(ending.tail, ending.tone) : "-";
            var lastC = cc.Length - 1;
            var firstC = 0;

            var phonemes = new List<string>();
            if (ending.IsEndingV) {
                var vr = $"{v}{t}";
                if (HasOto(vr, ending.tone)) {
                    phonemes.Add(vr);
                } else {
                    phonemes.Add($"{v} {t}");
                }
            } else if (ending.IsEndingVCWithOneConsonant) {
                phonemes.Add($"{v}{cc[0]}");
            } else {
                for (var i = lastC; i >= 0; i--) {
                    var vcc = $"{v}{string.Join("", cc.Take(2))}";
                    if (HasOto(vcc, ending.tone)) {
                        phonemes.Add(vcc);
                        firstC = 1;
                        break;
                    } else {
                        phonemes.Add($"{v}{cc[0]}");
                        break;
                    }
                }
                for (var i = firstC; i < lastC; i++) {
                    // all CCs except the first one are /C1C2/, the last one is /C1 C2-/
                    // but if there is no /C1C2/, we try /C1 C2-/, vise versa for the last one
                    var cc1 = $"{cc[i]}{cc[i + 1]} {t}";
                    // in most cases, use [C1][C2] -
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = $"{cc[i]} {cc[i + 1]}";
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = $"{cc[i]}{cc[i + 1]}";
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = $"{cc[i]}{cc[i + 1]}@";
                    }
                    if (!HasOto(cc1, ending.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (i < cc.Length - 2) {
                        var cc2 = $"{cc[i + 1]}{cc[i + 2]} {t}";
                        // in most cases, use [C2][C3] -
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = $"{cc[i + 1]} {cc[i + 2]}";
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
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = $"{cc[i + 1]}{cc[i + 2]}@";
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]}{cc[i + 2]} {t}", ValidateAlias($"{cc[i]}{cc[i + 1]}{cc[i + 2]} {t}"))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (HasOto(cc1, ending.tone) && HasOto(cc2, ending.tone)) {
                            // like [C1 C2][C2 ...]
                            phonemes.Add(cc1);
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]}{cc[i + 2]} {t}", ValidateAlias($"{cc[i + 1]}{cc[i + 2]} {t}"))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]}{cc[i + 2]}", ValidateAlias($"{cc[i + 1]}{cc[i + 2]}"))) {
                            // like [C1C2][C2 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]}{cc[i + 2]}@", ValidateAlias($"{cc[i + 1]}{cc[i + 2]}@"))) {
                            // like [C1C2][C2 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            i++;
                        }
                    } else {
                        if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            // like [C1 C2-]
                            i++;
                        }
                    }
                }
            }
            return phonemes;
        }

        protected override string ValidateAlias(string alias) {
            foreach (var VV in new[] { "a 6", "a6" }) {
                alias = alias.Replace(VV, "a a");
            }
            foreach (var CC in new[] { "n S" }) {
                alias = alias.Replace(CC, "n tS");
            }
            foreach (var CC in new[] { "l S" }) {
                alias = alias.Replace(CC, "l tS");
            }
            foreach (var CC in new[] { "nS" }) {
                alias = alias.Replace(CC, "ntS");
            }
            foreach (var CC in new[] { "lS" }) {
                alias = alias.Replace(CC, "ltS");
            }
            foreach (var CC in new[] { "n s" }) {
                alias = alias.Replace(CC, "n ts");
            }
            foreach (var CC in new[] { "l s" }) {
                alias = alias.Replace(CC, "l ts");
            }
            foreach (var CC in new[] { "ns" }) {
                alias = alias.Replace(CC, "nts");
            }
            foreach (var CC in new[] { "ls" }) {
                alias = alias.Replace(CC, "lts");
            }
            foreach (var CC in new[] { "st" }) {
                alias = alias.Replace(CC, "tst");
            }
            // Split diphthongs adjuster
            if (alias.Contains("U^")) {
                alias = alias.Replace("U^", "U");
            }
            if (alias.Contains("I^")) {
                alias = alias.Replace("I^", "I");
            }
            if (alias.Contains("Y^")) {
                alias = alias.Replace("Y^", "Y");
            }

            if (isYamlFallbacks) {
                foreach (var syllable in yamlFallbacks.OrderByDescending(f => f.Key.Length)) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            return alias;
        }

        protected override double GetTransitionBasicLengthMs(string alias = "") {
            return base.GetTransitionBasicLengthMs();
        }
    }
}