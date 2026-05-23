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
    [Phonemizer("Spanish VCCV Phonemizer", "ES VCCV", "Lotte V", language: "UTAU")]
    public class SpanishVCCVPhonemizer : SyllableBasedPhonemizer {
        /// <summary>
        /// Based on the nJokis method.
        /// Supports automatic consonant substitutes, such as seseo, through ValidateAlias.
        ///</summary>

        private string[] vowels = "a,e,i,o,u,BB,DD,ff,GG,ll,mm,nn,rrr,ss,xx".Split(',');
        private readonly string[] consonants = "b,B,ch,d,D,E,f,g,G,h,I,jj,k,l,L,m,n,nJ,p,r,rr,s,sh,t,U,w,x,y,z".Split(',');
        private readonly string[] shortConsonants = "r".Split(",");
        private readonly string[] longConsonants = "ch,k,p,s,sh,t".Split(",");
        private Dictionary<string, string> dictionaryReplacements = ("a=a;e=e;i=i;o=o;u=u;" +
                "b=b;ch=ch;d=d;f=f;g=g;gn=nJ;k=k;l=l;ll=jj;m=m;n=n;p=p;r=r;rr=rr;s=s;t=t;w=w;x=x;y=y;z=z;I=I;U=U;B=B;D=D;G=G;Y=y").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_es.txt";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;
        // Store the splitting replacements
        private List<Replacement> splittingReplacements = new List<Replacement>();
        // Store the merging replacements
        private List<Replacement> mergingReplacements = new List<Replacement>();
        private string[] tails = "-".Split(',');

        private readonly Dictionary<string, string> replacements = "D=d".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isReplacements = false;

        protected override IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, "njokis_vccv.yaml");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.njokis_template);
            }
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "njokis_vccv.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }
            g2ps.Add(new SpanishG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (tails.Contains(note.lyric)) {
                return new string[] { note.lyric };
            }
            if (original == null) {
                return null;
            }
            List<string> modified = new List<string>(original);
            List<string> finalPhonemes = new List<string>();
            int i = 0;
            bool hasReplacements = mergingReplacements.Any() == true || splittingReplacements.Any() == true; // Check for any replacements
            if (hasReplacements) {
                finalPhonemes = new List<string>();
                while (i < modified.Count) {
                    bool replaced = false;
                    foreach (var rule in mergingReplacements.Concat(splittingReplacements)) {
                        if (rule.from is string[] fromArray && i + fromArray.Length <= modified.Count) {
                            bool match = true;
                            for (int j = 0; j < fromArray.Length; j++) {
                                if (modified[i + j] != fromArray[j]) {
                                    match = false;
                                    break;
                                }
                            }
                            if (match) {
                                if (rule.to is string toString) {
                                    finalPhonemes.Add(toString);
                                } else if (rule.to is string[] toArray) {
                                    finalPhonemes.AddRange(toArray);
                                }
                                i += fromArray.Length;
                                replaced = true;
                                break;
                            }
                        }
                    }

                    if (!replaced && splittingReplacements.Any()) {
                        string currentPhoneme = modified[i];
                        bool singleReplaced = false;
                        foreach (var rule in splittingReplacements) {
                            if (rule.from.ToString() == currentPhoneme && rule.to is string[] toArray) {
                                finalPhonemes.AddRange(toArray);
                                singleReplaced = true;
                                break;
                            }
                        }
                        if (!singleReplaced) {
                            finalPhonemes.Add(ReplacePhoneme(modified[i], note.tone));
                        }
                        i++;
                    } else if (!replaced) {
                        finalPhonemes.Add(ReplacePhoneme(modified[i], note.tone));
                        i++;
                    }
                }
            } else {
                finalPhonemes = new List<string>(modified);
            }
            List<string> finalProcessedPhonemes = new List<string>();
            IEnumerable<string> phonemes;
            if (hasReplacements) {
                phonemes = finalPhonemes;
            } else {
                phonemes = original;
            }
            foreach (string s in phonemes) {
                switch (s) {
                    default:
                        finalProcessedPhonemes.Add(s);
                        break;
                }
            }
            return finalProcessedPhonemes.ToArray();
        }

        public override void SetSinger(USinger singer) {
            if (this.singer != singer) {
                string file;
                if (singer != null && singer.Found && singer.Loaded && !string.IsNullOrEmpty(singer.Location)) {
                    file = Path.Combine(singer.Location, "njokis_vccv.yaml");
                    if (!File.Exists(file)) {
                        try {
                            File.WriteAllBytes(file, Data.Resources.njokis_template);
                        } catch (Exception e) {
                            Log.Error(e, $"Failed to write 'njokis_vccv.yaml' to singer folder at {file}");
                        }
                    }
                } else if (!string.IsNullOrEmpty(PluginDir)) {
                    file = Path.Combine(PluginDir, "njokis_vccv.yaml");
                } else {
                    Log.Error("Singer location and PluginDir are both null or empty. Cannot locate 'njokis_vccv.yaml'.");
                    return; // Exit early to avoid null file issues
                }

                if (File.Exists(file)) {
                    try {
                        var data = Core.Yaml.DefaultDeserializer.Deserialize<NjokisYAMLData>(File.ReadAllText(file));

                        // Load vowels
                        try {
                            var loadVowels = data.symbols
                                ?.Where(s => s.type == "vowel")
                                .Select(s => s.symbol)
                                .ToList() ?? new List<string>();

                            vowels = vowels.Concat(loadVowels).Distinct().ToArray();
                        } catch (Exception ex) {
                            Log.Error($"Failed to load vowels from njokis_vccv.yaml: {ex.Message}");
                        }
                        // Load replacements
                        try {
                            if (data?.replacements != null && data.replacements.Any() == true) {
                                mergingReplacements = new List<Replacement>();
                                splittingReplacements = new List<Replacement>();

                                foreach (var replacement in data.replacements) {
                                    try {
                                        if (replacement.from != null && replacement.to != null) {
                                            if (replacement.from is IEnumerable<object> fromList) {
                                                // 'from' is a list (e.g., [ae, n])
                                                string[] fromArray = fromList.Select(item => item.ToString()).ToArray();
                                                if (replacement.to is string toString) {
                                                    mergingReplacements.Add(new Replacement { from = fromArray, to = toString });
                                                } else if (replacement.to is IEnumerable<object> toList) {
                                                    splittingReplacements.Add(new Replacement { from = fromArray, to = toList.Select(item => item.ToString()).ToArray() });
                                                } else {
                                                    Log.Error($"Error: Invalid 'to' type in replacement: {replacement}");
                                                }
                                            } else if (replacement.from is string fromString) {
                                                // 'from' is a single string (e.g., tr, aw, ae, m, ng)
                                                if (replacement.to is string toString) {
                                                    dictionaryReplacements[fromString] = toString;
                                                } else if (replacement.to is IEnumerable<object> toList) {
                                                    splittingReplacements.Add(new Replacement { from = fromString, to = toList.Select(item => item.ToString()).ToArray() });
                                                } else {
                                                    Log.Error($"Error: Invalid 'to' type in replacement: {replacement}");
                                                }
                                            } else {
                                                Log.Error($"Error: Invalid 'from' type in replacement: {replacement}");
                                            }
                                        } else {
                                            Log.Error($"Error: 'from' or 'to' is null in replacement: {replacement}");
                                        }
                                    } catch (Exception ex) {
                                        Log.Error($"Failed to process replacement entry: {replacement}. Error: {ex.Message}");
                                    }
                                }
                            } else {
                                mergingReplacements = new List<Replacement>();
                                splittingReplacements = new List<Replacement>();
                            }
                        } catch (Exception ex) {
                            Log.Error($"Failed to load replacements from en-vccv.yaml: {ex.Message}");
                        }
                        // Load fallbacks
                        try {
                            if (data?.fallbacks?.Any() == true) {
                                foreach (var df in data.fallbacks) {
                                    if (!string.IsNullOrEmpty(df.from) && !string.IsNullOrEmpty(df.to)) {
                                        // Overwrite or add
                                        replacements[df.from] = df.to;
                                    } else {
                                        Log.Warning("Ignored YAML fallback with missing 'from' or 'to' value.");
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            Log.Error($"Failed to load fallbacks from YAML: {ex.Message}");
                        }

                    } catch (Exception ex) {
                        Log.Error($"Failed to parse njokis_vccv.yaml: {ex.Message}, Exception Type: {ex.GetType()}");
                    }
                }
                ReadDictionaryAndInit();
                this.singer = singer;
            }
        }
        public class NjokisYAMLData {
            public SymbolData[] symbols { get; set; } = Array.Empty<SymbolData>();
            public Replacement[] replacements { get; set; } = Array.Empty<Replacement>();
            public Fallbacks[] fallbacks { get; set; } = Array.Empty<Fallbacks>();

            public struct SymbolData {
                public string symbol { get; set; }
                public string type { get; set; }
            }
            public struct Fallbacks {
                public string from { get; set; }
                public string to { get; set; }
            }
        }
        // can split or merge
        public class Replacement {
            public object from { get; set; }
            public object to { get; set; }

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
            string v = ReplacePhoneme(syllable.v, syllable.vowelTone);
            string[] cc = syllable.cc.Select(ReplacePhoneme).ToArray();
            string basePhoneme;
            var phonemes = new List<string>();
            var lastC = cc.Length - 1;
            var firstC = 0;

            foreach (var entry in replacements) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Key, syllable.tone)) {
                    isReplacements = true;
                    break;
                }
            }

            if (syllable.IsStartingV) {
                var rcv = $"- {v}";
                var rcv2 = $"-{v}";
                if (HasOto(rcv, syllable.vowelTone)) {
                    basePhoneme = rcv;
                } else {
                    basePhoneme = rcv2;
                }
            } else if (syllable.IsVV) {
                var vv = $"{prevV} {v}";
                var vv2 = $"{prevV}{v}";
                if (!CanMakeAliasExtension(syllable)) {
                    if (HasOto(vv, syllable.vowelTone)) {
                        basePhoneme = vv;
                    } else if (!HasOto(vv, syllable.vowelTone) && HasOto(ValidateAlias(vv2), syllable.vowelTone)) {
                        basePhoneme = ValidateAlias(vv2);
                    } else {
                        basePhoneme = v;
                    }
                } else {
                    // the previous alias will be extended
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                // TODO: move to config -CV or -C CV
                // TODO: move to config -CV or -C CV
                var rcv = $"-{cc[0]}{v}";
                var cv = $"{cc[0]}{v}";
                if (HasOto(rcv, syllable.vowelTone)) {
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
                if (HasOto(rccv, syllable.vowelTone)) {
                    basePhoneme = rccv;
                } else if (HasOto(ValidateAlias(rccv), syllable.vowelTone)) {
                    basePhoneme = ValidateAlias(rccv);
                } else {
                    var ccv = $"{string.Join("", cc)}{v}";
                    var _cv = $"_{cc.Last()}{v}";
                    if (HasOto(ccv, syllable.vowelTone)) {
                        basePhoneme = ccv;
                    } else if (HasOto(ValidateAlias(ccv), syllable.tone) && !ValidateAlias(ccv).StartsWith("B") && !ValidateAlias(ccv).StartsWith("D") && !ValidateAlias(ccv).StartsWith("G")) {
                        basePhoneme = ValidateAlias(ccv);
                    } else if (!HasOto(ValidateAlias(ccv), syllable.tone) && !HasOto(ValidateAlias(ValidateAlias(ccv)), syllable.tone) && HasOto(_cv, syllable.tone)) {
                        basePhoneme = _cv;
                    } else {
                        basePhoneme = $"{cc.Last()}{v}";
                    }
                    // try RCC
                    for (var i = cc.Length; i > 1; i--) {
                        if (HasOto(_cv, syllable.vowelTone)) {
                            TryAddPhoneme(phonemes, syllable.tone, $"-{string.Join("", cc.Take(i))}", ValidateAlias($"-{string.Join("", cc.Take(i))}"), $"-{cc[0]}", ValidateAlias($"-{cc[0]}"));
                            if (!HasOto($"-{string.Join("", cc.Take(i))}", syllable.tone) && !HasOto(ValidateAlias($"-{string.Join("", cc.Take(i))}"), syllable.tone) && !HasOto(ccv, syllable.vowelTone) && !HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                                TryAddPhoneme(phonemes, syllable.tone, $"{string.Join("", cc.Take(i))}", ValidateAlias($"{string.Join("", cc.Take(i))}"));
                            }
                            firstC = i;
                        } else {
                            TryAddPhoneme(phonemes, syllable.tone, $"-{cc[0]}", ValidateAlias($"-{cc[0]}"));
                            firstC = i;
                            break;
                        }
                    }
                }
            } else {
                var cv = cc.Last() + v;
                basePhoneme = cv;
                if (!HasOto(cv, syllable.vowelTone)) {
                    cv = ValidateAlias(cv);
                    basePhoneme = cv;
                }
                // try CCV
                if (cc.Length - firstC > 1) {
                    for (var i = firstC; i < cc.Length; i++) {
                        var ccv = $"{string.Join("", cc.Skip(i))}{v}";
                        var rccv = $"-{string.Join("", cc)}{v}";
                        if (HasOto(ccv, syllable.vowelTone)) {
                            lastC = i;
                            basePhoneme = ccv;
                            break;
                        } else if (!HasOto(ccv, syllable.vowelTone) && HasOto(ValidateAlias(ccv), syllable.vowelTone)) {
                            ccv = ValidateAlias(ccv);
                            lastC = i;
                            basePhoneme = ccv;
                            break;
                        } else if (!HasOto(ValidateAlias(ccv), syllable.vowelTone) && HasOto(rccv, syllable.vowelTone)) {
                            lastC = i;
                            basePhoneme = rccv;
                            break;
                        } else if (!HasOto(rccv, syllable.vowelTone) && HasOto(ValidateAlias(rccv), syllable.vowelTone)) {
                            rccv = ValidateAlias(rccv);
                            lastC = i;
                            basePhoneme = rccv;
                            break;
                        }
                    }
                }
                for (var i = lastC + 1; i >= 0; i--) {
                    var vcc = $"{prevV} {string.Join("", cc.Take(i))}";
                    var vcc2 = $"{prevV}{string.Join("", cc.Take(i))}";
                    var vcc3 = $"{prevV}{string.Join(" ", cc.Take(2))}";
                    var vcc4 = $"{prevV}{string.Join(" ", cc.Take(1))}";
                    var vcc5 = $"{prevV} {string.Join("", cc.Take(2))}";
                    var cc1 = $"{string.Join(" ", cc.Take(2))}";
                    var cc2 = $"{string.Join("", cc.Take(2))}";
                    var vc = $"{prevV} {cc[0]}";
                    var vc2 = $"{prevV}{cc[0]}";
                    if (i == 0) {
                        TryAddPhoneme(phonemes, syllable.tone, ValidateAlias(vcc), ValidateAlias(vc));
                    } else if (HasOto(vcc, syllable.tone)) {
                        phonemes.Add(vcc);
                        break;
                    } else if (!HasOto(vcc, syllable.tone) && HasOto(ValidateAlias(vcc), syllable.tone)) {
                        vcc = ValidateAlias(vcc);
                        phonemes.Add(vcc);
                        break;
                    } else if (HasOto(vcc2, syllable.tone)) {
                        phonemes.Add(vcc2);
                        break;
                    } else if (!HasOto(vcc2, syllable.tone) && HasOto(ValidateAlias(vcc2), syllable.tone)) {
                        vcc2 = ValidateAlias(vcc2);
                        phonemes.Add(vcc2);
                        break;
                    } else if (HasOto(vcc3, syllable.tone) && !HasOto(cc1, syllable.tone) && !HasOto(ValidateAlias(cc1), syllable.tone) && !HasOto(cc2, syllable.tone) && !HasOto(ValidateAlias(cc2), syllable.tone)) {
                        phonemes.Add(vcc3);
                        break;
                    } else if (!HasOto(vcc3, syllable.tone) && HasOto(ValidateAlias(vcc3), syllable.tone) && !HasOto(cc1, syllable.tone) && !HasOto(ValidateAlias(cc1), syllable.tone) && !HasOto(cc2, syllable.tone) && !HasOto(ValidateAlias(cc2), syllable.tone)) {
                        vcc3 = ValidateAlias(vcc3);
                        phonemes.Add(vcc3);
                        break;
                    } else if (HasOto(vcc4, syllable.tone)) {
                        phonemes.Add(vcc4);
                        break;
                    } else if (!HasOto(vcc4, syllable.tone) && HasOto(ValidateAlias(vcc4), syllable.tone)) {
                        vcc4 = ValidateAlias(vcc4);
                        phonemes.Add(vcc4);
                        break;
                    } else if (HasOto(vcc5, syllable.tone)) {
                        phonemes.Add(vcc5);
                        break;
                    } else if (!HasOto(vcc5, syllable.tone) && HasOto(ValidateAlias(vcc5), syllable.tone)) {
                        vcc5 = ValidateAlias(vcc5);
                        phonemes.Add(vcc5);
                        break;
                    } else if (HasOto(vc, syllable.tone)) {
                        phonemes.Add(vc);
                        break;
                    } else if (!HasOto(vc, syllable.tone) && HasOto(ValidateAlias(vc), syllable.tone)) {
                        vc = ValidateAlias(vc);
                        phonemes.Add(vc);
                        break;
                    } else if (HasOto(vc2, syllable.tone) && HasOto(ValidateAlias(vc2), syllable.tone)) {
                        phonemes.Add(vc2);
                        break;
                    } else {
                        continue;
                    }
                }
            }
            for (var i = firstC; i < lastC; i++) {
                // we could use some CCV, so lastC is used
                // we could use -CC so firstC is used
                var cc1 = $"{string.Join("", cc.Skip(i))}";
                var ccv = string.Join("", cc.Skip(i)) + v;
                var ucv = $"_{cc.Last()}{v}";
                var rccv = $"-{string.Join("", cc)}{v}";
                if (!HasOto(rccv, syllable.vowelTone) && !HasOto(ValidateAlias(rccv), syllable.vowelTone) && !vowels.Contains(cc1)) {
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = $"_{cc[i]}{cc[i + 1]}_";
                    }
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
                    if (HasOto(ccv, syllable.vowelTone)) {
                        basePhoneme = ccv;
                        if (!HasOto(ccv, syllable.vowelTone)) {
                            ccv = ValidateAlias(ccv);
                            basePhoneme = ccv;
                        }
                    } else if (HasOto(ucv, syllable.vowelTone) && HasOto(cc1, syllable.vowelTone) && !cc1.Contains($"{cc[i]} {cc[i + 1]}")) {
                        basePhoneme = ucv;
                        if (!HasOto(ucv, syllable.vowelTone)) {
                            ucv = ValidateAlias(ucv);
                            basePhoneme = ucv;
                        }
                    }
                    var cc2 = $"{string.Join("", cc.Skip(i))}";
                    if (i + 1 < lastC && !vowels.Contains(cc2)) {
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = $"_{cc[i + 1]}{cc[i + 2]}_";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = $"{cc[i + 1]}{cc[i + 2]}";
                        }
                        if (!HasOto(cc2, syllable.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (cc2.Contains($"{string.Join("", cc.Skip(i))}") || cc2.Contains(ValidateAlias($"{string.Join("", cc.Skip(i))}"))) {
                            i++;
                        }
                        if (HasOto(ccv, syllable.vowelTone)) {
                            basePhoneme = ccv;
                            if (!HasOto(ccv, syllable.vowelTone)) {
                                ccv = ValidateAlias(ccv);
                                basePhoneme = ccv;
                            }
                        } else if (HasOto(ucv, syllable.vowelTone) && HasOto(cc2, syllable.vowelTone) && !cc2.Contains($"{cc[i + 1]} {cc[i + 2]}")) {
                            basePhoneme = ucv;
                            if (!HasOto(ucv, syllable.vowelTone)) {
                                ucv = ValidateAlias(ucv);
                                basePhoneme = ucv;
                            }
                        }
                        if (HasOto(cc1, syllable.tone) && HasOto(cc2, syllable.tone) && !cc1.Contains($"{string.Join("", cc.Skip(i))}") && !cc1.Contains(ValidateAlias($"{string.Join("", cc.Skip(i))}"))) {
                            // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                            phonemes.Add(cc1);
                        } else if (TryAddPhoneme(phonemes, syllable.tone, cc1)) {
                            // like [V C1] [C1 C2] [C2 ..]
                            if (cc1.Contains($"{string.Join("", cc.Skip(i))}") || cc1.Contains(ValidateAlias($"{string.Join("", cc.Skip(i))}"))) {
                                i++;
                            }
                        }
                    } else {
                        // like [V C1] [C1 C2]  [C2 ..] or like [V C1] [C1 -] [C3 ..]
                        TryAddPhoneme(phonemes, syllable.tone, cc1);
                        if (cc1.Contains($"{string.Join("", cc.Skip(i))}") || cc1.Contains(ValidateAlias($"{string.Join("", cc.Skip(i))}"))) {
                            i++;
                        }
                    }
                }
            }
            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc.Select(c => ReplacePhoneme(c, ending.tone)).ToArray();
            string v = ReplacePhoneme(ending.prevV, ending.tone);

            var phonemes = new List<string>();
            if (ending.IsEndingV) {
                TryAddPhoneme(phonemes, ending.tone, $"{v}-", ValidateAlias($"{v}-"));
            } else if (ending.IsEndingVCWithOneConsonant) {
                var vcr = $"{v}{cc[0]}-";
                if (HasOto(vcr, ending.tone)) {
                    phonemes.Add(vcr);
                } else {
                    phonemes.Add($"{v} {cc[0]}");
                    TryAddPhoneme(phonemes, ending.tone, $"{cc[0]}-", ValidateAlias($"{cc[0]}-"));
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
                    TryAddPhoneme(phonemes, ending.tone, $"{cc.Last()}-", ValidateAlias($"{cc.Last()}-"));
                }
            }
            return phonemes;
        }
        protected override string ValidateAlias(string alias) {
            //foreach (var consonant in new[] { "w" }) {
            //    alias = alias.Replace("w", "u");
            //}
            //foreach (var consonant in new[] { "y" }) {
            //    alias = alias.Replace("y", "i");
            // }
            if (isReplacements) {
                foreach (var syllable in replacements.OrderByDescending(f => f.Key.Length)) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            var rules = new Dictionary<string, string> {
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

            foreach (var rule in rules.OrderByDescending(rule => rule.Key.Length)) {
                alias = alias.Replace(rule.Key, rule.Value);
            }
            alias = alias.Replace("h", "x").Replace("xx", "x");

            foreach (var consonant in new[] { "jj" }) {
                alias = alias.Replace("jj", "L");
            }
            return base.ValidateAlias(alias);
        }

        protected override double GetTransitionBasicLengthMs(string alias = "") {
            foreach (var c in shortConsonants) {
                if (alias.Contains(c) && !alias.Contains("rr") && !alias.StartsWith(c) && !alias.Contains("ar") && !alias.Contains("er") && !alias.Contains("ir") && !alias.Contains("or") && !alias.Contains("ur")) {
                    return base.GetTransitionBasicLengthMs() * 0.50;
                }
            }
            foreach (var c in longConsonants) {
                if (alias.Contains(c) && !alias.StartsWith(c) && !alias.StartsWith("-" + c)) {
                    return base.GetTransitionBasicLengthMs() * 2.0;
                }
            }
            return base.GetTransitionBasicLengthMs();
        }
    }
}
