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

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("English VCCV Phonemizer", "EN VCCV", "cubialpha & Mim", language: "EN")]
    // V3 of the phonemizer
    // This is a temporary solution until Cz's comes out with their own.
    // Feel free to use the Lyric Parser plugin for more accurate pronunciations & support of ConVel.

    // Thanks to cubialpha, Cz, Halo/BagelHero, nago, and Anjo for their help.
    // cadlaxa here ^_^
    public class EnglishVCCVPhonemizer : SyllableBasedPhonemizer {
        protected override string YamlFileName => "envccv.yaml";
        protected override byte[] YamlTemplate => Data.Resources.envccv_template;
        protected override string YamlVersion => "1.2";
        public EnglishVCCVPhonemizer() {
            this.vowels = "a,@,u,0,8,I,e,3,A,i,E,O,Q,6,o,1ng,9,&,x,1,Y,L,W,8n,Ang,9l".Split(',');
            this.consonants = "b,ch,d,dh,f,g,h,j,k,l,m,n,ng,p,r,s,sh,t,th,v,w,y,z,zh,dd,hh,sp,st".Split(',');
            this.dictionaryReplacements = ("ax=x;aa=a;ae=@;ah=u;ao=9;aw=8;ay=I;" +
            "b=b;ch=ch;d=d;dh=dh;eh=e;er=3;ey=A;f=f;g=g;hh=h;hhy=hh;ih=i;iy=E;jh=j;k=k;l=l;m=m;n=n;ng=ng;ow=O;oy=Q;" +
            "p=p;r=r;s=s;sh=sh;t=t;th=th;uh=6;uw=o;v=v;w=w;y=y;z=z;zh=zh;dx=dd;").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        }
        
        private bool isYamlFallbacks = false;

        private readonly Dictionary<string, string> vcExceptions =
            new Dictionary<string, string>() {
                {"i ng","1 ng"},
                {"ing","1ng"},
                {"0 r","0r-"},
                {"9r","0r"},
                {"9r-","0r-"},
                {"er-","Ar-" },
                //{"e r","Ar"},
                {"er","Ar"},
                //{"@ m","&m"},
                {"@m","&m"},
                {"@n","&n"},
                {"@m-","&m-"},
                {"@n-","&n-"},
                {"@ ng","Ang-"},
                {"@ng","Ang"},
                {"ang","9ng"},
                {"a ng","9ng-"},
                //{"a l","9l-"},
                {"al","9l"},
                {"al-","9l-"},
                //{"O l","0l"},
                {"0 l","0l-"},
                {"Ol","0l"},
                //{"6 l","6l"},
                //{"i r","Er"},
                {"ir","Er"},
                {"ir-","Er-"},
                {"6 r","or-"},
            };

        private readonly Dictionary<string, string> vvExceptions =
            new Dictionary<string, string>() {
                {"o","w"},
                {"O","w"},
                {"8","w"},
                {"W","w"},
                {"A","y"},
                {"I","y"},
                {"Y","y"},
                {"E","y"},
                {"Q","y"},
                {"i","y"},
                {"3","r"},
            };

        private readonly Dictionary<string, string> ccFallback =
            new Dictionary<string, string>() {
                {"z","s"},
                {"g","k"},
                {"zh","sh"},
                {"j","ch"},
                {"b","p"},
                {"v","f"},
                {"d","t"},
                {"dh","th"},
            };

        private readonly string[] ccExceptions = { "th", "ch", "dh", "zh", "sh", "ng" };
        private readonly string[] cccExceptions = { "spr", "spl", "skr", "str", "skw", "sky", "spy", "skt" };
        private Dictionary<string, string> vcVowels = new Dictionary<string, string>();
        
        private readonly Dictionary<string, string> vcccExceptions =
            new Dictionary<string, string>() {
                {"spr","sp"},
                {"spl","sp"},
                {"skr","sk"},
                {"str","st"},
                {"skw","sk"},
                {"sky","sk"},
                {"spy","sp"},
                {"skt","sk"},
            };
        //spl, shr, skr, spr, str, thr, skw, thw, sky, spy
        private readonly string[] ccNoParsing = { "sk", "sm", "sn", "sp", "st", "hy" };
        private readonly string[] stopCs = { "b", "d", "g", "k", "p", "t" };
        private readonly string[] ucvCs = { "r", "l", "w", "y", "f"};
        private readonly string[] starlightccs = { "rl", "ll", "nn", "mm", "rf", "mf", "lf" };

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "";
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
            g2ps.Add(new ArpabetG2p());
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
            List<string> finalProcessedPhonemes = new List<string>();
            string[] tr_dr = new[] { "tr", "dr"};
            foreach (string s in original) {
                switch (s) {
                    case var str when tr_dr.Contains(str) && !HasOto(str, note.tone) && !HasOto($"A {str}", note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { s[0].ToString(), s[1].ToString() });
                        break;
                    default:
                        finalProcessedPhonemes.Add(s);
                        break;
                }
            }
            return finalProcessedPhonemes.ToArray();
        }

        // We have a custom property in the YAML, so we have to load it twice
        public override void SetSinger(USinger singer) {
            base.SetSinger(singer);

            if (this.singer == null) return;

            string file = null;
            if (singer != null && singer.Found && singer.Loaded && !string.IsNullOrEmpty(singer.Location)) {
                file = Path.Combine(singer.Location, YamlFileName);
            } else if (!string.IsNullOrEmpty(PluginDir)) {
                file = Path.Combine(PluginDir, YamlFileName);
            }

            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;

            try {
                var data = Core.Yaml.DefaultDeserializer.Deserialize<VcVowelYAMLData>(File.ReadAllText(file));
                if (data?.vcvowels != null) {
                    vcVowels.Clear();
                    foreach (var kvp in data.vcvowels) {
                        if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value)) {
                            vcVowels[kvp.Key] = kvp.Value;
                        }
                    }
                }
            } catch (Exception ex) {
                Log.Error($"Failed to load vcvowels from {YamlFileName}: {ex.Message}");
            }
        }

        private class VcVowelYAMLData {
            public Dictionary<string, string> vcvowels { get; set; } = new Dictionary<string, string>();
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
            var lastC = cc.Length - 1;
            var firstC = 0;
            string[] CurrentWordCc = syllable.CurrentWordCc.Select(c => ReplacePhoneme(c, syllable.tone)).ToArray();
            string[] PreviousWordCc = syllable.PreviousWordCc.Select(c => ReplacePhoneme(c, syllable.tone)).ToArray();
            int prevWordConsonantsCount = syllable.prevWordConsonantsCount;
            int lastCPrevWord = syllable.prevWordConsonantsCount;

            foreach (var entry in yamlFallbacks) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Key, syllable.tone)) {
                    isYamlFallbacks = true;
                    break;
                }
            }
            string basePhoneme = null;
            var phonemes = new List<string>();
            // --------------------------- STARTING V ------------------------------- //
            if (syllable.IsStartingV) {
                // if starting V -> -V
                basePhoneme = $"-{v}";


                // --------------------------- STARTING VV ------------------------------- //
            } else if (syllable.IsVV) {
                // if it's a VV transition, try VV first, then try Vc + cV depending on certain rules, then try V
                //you can input multiple instances of the same V with the phonetic hint
                basePhoneme = $"{prevV}{v}";

                if (!HasOto(basePhoneme, syllable.vowelTone)) {
                    basePhoneme = $"{prevV} {v}";

                    if (vvExceptions.ContainsKey(prevV) && prevV != v) {
                        var vc = $"{prevV} {vvExceptions[prevV]}";
                        if (!HasOto(vc, syllable.vowelTone)) {
                            vc = $"{prevV}{vvExceptions[prevV]}";
                        }
                        phonemes.Add(vc);
                        basePhoneme = $"{vvExceptions[prevV]}{v}";
                    }
                    if (vcVowels.ContainsKey(prevV) && !HasOto(basePhoneme, syllable.vowelTone)) {
                        var vc = $"{prevV}";
                        phonemes.Add(vc);
                        basePhoneme = $"_{v}";
                    }
                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                        basePhoneme = $"{v}";
                    }
                }
                // --------------------------- STARTING CV ------------------------------- //
            } else if (syllable.IsStartingCVWithOneConsonant) {
                //if starting CV -> [-CV], fallback to [CV]
                basePhoneme = $"-{cc[0]}{v}";
                if (!HasOto(basePhoneme, syllable.tone)) {
                    if ($"{cc[0]}" == "h" && $"{v}" == "E")
                        basePhoneme = $"-hhE";
                    else {
                        basePhoneme = $"{cc[0]}{v}";
                    }
                }

                // --------------------------- STARTING CCV ------------------------------- //
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {

                basePhoneme = $"_{cc.Last()}{v}";
                if (!HasOto(basePhoneme, syllable.tone)) {
                    basePhoneme = $"{cc.Last()}{v}";
                }

                // try CCVs

                var ccv = $"";
                if (cc.Length == 2) {
                    ccv = $"-{cc[0]}{cc[1]}{v}";
                    if (HasOto(ccv, syllable.tone)) {
                        basePhoneme = ccv;
                    } else if ($"{cc[0]}" == "h") {
                        ccv = $"-hh{cc[1]}{v}";
                        if (HasOto(ccv, syllable.tone)) {
                            basePhoneme = ccv;
                        }
                    }
                }

                if (cc.Length == 3) {
                    ccv = $"-{cc[0]}{cc[1]}{cc[2]}";
                    if (HasOto(ccv, syllable.tone)) {
                        phonemes.Add(ccv);
                    } else if ($"{cc[0]}" == "h") {
                        ccv = $"-hh{cc[1]}{v}";
                        if (HasOto(ccv, syllable.tone)) {
                            basePhoneme = ccv;
                        }
                    }
                }

                // if there still is no match, add [-CC] + [CC] etc.

                if (!HasOto(ccv, syllable.tone)) {
                    // other CCs
                    for (var i = 0; i < lastC; i++) {
                        var currentCc = $"{cc[i]}{cc[i + 1]}";
                        if (i == 0 && HasOto($"-{cc[i]}{cc[i + 1]}", syllable.tone)) {
                            currentCc = $"-{cc[i]}{cc[i + 1]}";
                        }
                        if (HasOto(currentCc, syllable.tone)) {
                            phonemes.Add(currentCc);
                        }
                    }
                }
            }
                // --------------------------- IS VCV ------------------------------- //
                else {

                //cc = ValidateCC(cc);
                var parsingVCC = $"{prevV}{cc[0]}-";
                var parsingCC = "";

                // if only one Consonant [V C] + [CV], [VC-][CV], or [VC][_V] if certain rules are met
                if (syllable.IsVCVWithOneConsonant) {
                    basePhoneme = $"{cc.Last()}{v}";
                    var vc = $"{prevV} {cc.Last()}";

                    if (HasOto(vc, syllable.vowelTone)) {
                        vc = $"{prevV} {cc.Last()}";
                    } else {
                        vc = $"{prevV}{cc.Last()}";
                    }

                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                        if ($"{cc.Last()}" == "ng")
                            basePhoneme = $"_{v}";
                        if ($"{cc[0]}" == "h" && $"{v}" == "E")
                            basePhoneme = $"hhE";
                    }


                    if (lastCPrevWord == 1 && CurrentWordCc.Length == 0)
                        if (($"{PreviousWordCc.Last()}" == "r") || ($"{PreviousWordCc.Last()}" == "l")) {
                            if (HasOto($"{prevV}{PreviousWordCc.Last()}-", syllable.vowelTone) && HasOto($"{PreviousWordCc.Last()} {v}", syllable.vowelTone)) {
                                basePhoneme = $"{PreviousWordCc.Last()} {v}";
                                vc = $"{prevV}{PreviousWordCc.Last()}-";
                            } else
                                vc = $"{prevV}{PreviousWordCc.Last()}-";
                        }

                    if (!HasOto(vc, syllable.vowelTone)) {
                        if (vcVowels.ContainsKey(prevV))
                            vc = $"{prevV}-";
                        parsingCC = $"{vcVowels[prevV]} {cc[0]}";
                    }

                    vc = CheckVCExceptions(vc);
                    phonemes.Add(vc);
                    if (parsingCC != "") {
                        phonemes.Add(parsingCC);
                    }

                } else if (syllable.IsVCVWithMoreThanOneConsonant) {

                    bool exIng = $"{prevV}" == "i" && $"{cc[0]}" == "ng";
                    bool ex1ng = $"{prevV}" == "1" && $"{cc[0]}" == "ng";
                    bool ex1nk = $"{prevV}" == "1" && $"{cc[0]}" == "n";
                    // defaults to [CV]
                    basePhoneme = $"{cc.Last()}{v}";

                    // logic for consonant clusters of 2, defaults to [VC] + [CV]
                    if (cc.Length == 2) {

                        // sk, sm, sn, sp & st exceptions
                        var ccNoParse = $"{cc[0]}{cc[1]}";
                        bool dontParse = false;
                        if (cc.Length - lastCPrevWord > 1) {
                            for (int i = 0; i < ccNoParsing.Length; i++) {
                                if (ccNoParsing.Contains(ccNoParse)) {
                                    dontParse = true;
                                    break;
                                }
                            }
                        }
                        if (dontParse) {
                            basePhoneme = $"{ccNoParse}{v}";
                            if (ccNoParse == "hy") {
                                basePhoneme = $"hhy{v}";
                            }
                            if (!HasOto(basePhoneme, syllable.vowelTone)) {
                                basePhoneme = $"_{v}";
                            }

                            var vc = $"{prevV} {ccNoParse}";
                            if ($"{ccNoParse}" == "hy") {
                                vc = $"{prevV} hh";
                            }

                            phonemes.Add(vc);

                        }

                        // also [VC C] exceptions
                        var vccExceptions = $"{prevV}{cc[0]} {cc[1]}";
                        // i to 1 conversion
                        if (exIng || ex1ng || ex1nk) {
                            vccExceptions = $"1ng {cc[1]}";
                            // 1nk exception
                            if ($"{cc[1]}" == "k" && lastCPrevWord != 1) {
                                vccExceptions = $"1nk-";
                            }
                        }


                        if (HasOto(vccExceptions, syllable.vowelTone)) {
                            phonemes.Add(vccExceptions);
                        }

                        if (phonemes.Count == 0) {
                            // opera [9 p] + [pr] + [_ru]
                            parsingCC = $"{cc[0]}{cc[1]}";
                            if (HasOto(parsingCC, syllable.vowelTone) && lastCPrevWord != 1 && ucvCs.Contains($"{cc[1]}") && !starlightccs.Contains($"{cc[0]}{cc[1]}")) {
                                parsingVCC = $"{prevV} {cc[0]}";

                                basePhoneme = $"_{cc.Last()}{v}";
                                if (lastCPrevWord == cc.Length) {
                                    parsingVCC = $"{prevV}{cc[0]}-";
                                    if (stopCs.Contains($"{cc.Last()}")) {
                                        basePhoneme = $"-{v}";

                                    }
                                }
                                // sp fix
                                if ($"{cc[0]}" == "s" && $"{cc[1]}" == "p") {
                                    parsingVCC = $"{prevV} sp";
                                }
                                phonemes.Add(parsingVCC);
                                phonemes.Add(parsingCC);
                            } else {
                                // bonehead [On-] + [n h] + [he]
                                parsingCC = $"{cc[0]} {cc[1]}";
                                if (!HasOto(parsingCC, syllable.vowelTone)) {
                                    if (ccFallback.ContainsKey(cc[1]))
                                        parsingCC = $"{cc[0]} {ccFallback[cc[1]]}";
                                }
                                if (HasOto(parsingCC, syllable.vowelTone)) {
                                    //if (HasOto(parsingCC, syllable.vowelTone) && lastCPrevWord !=2) {
                                    if (!HasOto(parsingVCC, syllable.vowelTone)) {
                                        parsingVCC = CheckVCExceptions(parsingVCC);
                                    }
                                    if (!HasOto(parsingVCC, syllable.vowelTone)) {
                                        parsingVCC = $"{prevV} {cc[0]}";
                                    }

                                    // sp fix
                                    if ($"{cc[0]}" == "s" && $"{cc[1]}" == "p") {
                                        parsingVCC = $"{prevV} sp";
                                    }
                                    phonemes.Add(parsingVCC);
                                    phonemes.Add(parsingCC);
                                } else {
                                    // backpack [@k] + [p@]

                                    // sp fix
                                    if ($"{cc[0]}" == "s" && $"{cc[1]}" == "p") {
                                        parsingVCC = $"{prevV} sp";
                                    } else
                                        parsingVCC = $"{prevV}{cc[0]}";
                                    if (!HasOto(parsingVCC, syllable.vowelTone)) {
                                        if (vcVowels.ContainsKey(prevV)) {
                                            parsingVCC = $"{prevV}-";
                                            parsingCC = $"{vcVowels[prevV]}{cc[0]}";
                                            if (!HasOto(parsingCC, syllable.vowelTone) && parsingCC.Contains("ng") && $"{cc[0]}" == "k") {
                                                parsingCC = "nk";
                                            }
                                        }
                                    }
                                    phonemes.Add(parsingVCC);
                                    if (parsingCC != "" && HasOto(parsingCC, syllable.vowelTone)) {
                                        phonemes.Add(parsingCC);
                                    }
                                }
                            }
                        }
                    }

                    // LOGIC FOR MORE THAN 2 CONSONANTS
                    if (cc.Length > 2 && phonemes.Count == 0) {
                        // also [VC CC] exceptions
                        var vccExceptions = $"{prevV}{cc[0]}{cc[1]} {cc[2]}";
                        var startingC = 2;
                        // 1nks exception
                        bool ing = false;
                        if (exIng || ex1ng || ex1nk) {
                            vccExceptions = $"1ng {cc[1]}";
                            ing = true;
                            startingC = 1;
                            if (lastCPrevWord == 2) {
                                vccExceptions = $"1ng{cc[1]}";
                            }
                            if ($"{cc[1]}" == "k" && lastCPrevWord >= 2) {
                                vccExceptions = $"1nk";
                                startingC = 2;
                                if ($"{cc[2]}" == "s" && lastCPrevWord == 3) {
                                    vccExceptions = $"1nks";
                                    startingC = 3;
                                }
                            }
                        }

                        var ccNoParse = $"{cc[cc.Length - 3]}{cc[cc.Length - 2]}{cc[cc.Length - 1]}";
                        bool dontParse = false;
                        var lastCforLoop = cc.Length - 1;

                        // str exceptions
                        if (cccExceptions.Contains($"{ccNoParse}") && cc.Length - 3 >= lastCPrevWord) {
                            var vc = $"{prevV}{cc[0]}-";
                            if (vcVowels.ContainsKey(prevV)) {
                                vc = $"{prevV}-";
                            }
                            if (cc.Length == 3) {
                                var vccE = vcccExceptions[ccNoParse];
                                vc = $"{prevV} {vccE}";
                            }
                            if (cc.Length == 4) {
                                vc = $"{prevV}{cc[0]}";
                            }

                            if (vc == "ing")
                                vc = "1ng";

                            phonemes.Add(vc);
                            startingC = 0;
                            lastCforLoop -= 2;
                        } else {
                            ccNoParse = $"{cc[cc.Length - 2]}{cc[cc.Length - 1]}";
                            var ccSP = $"{cc[0]}{cc[1]}";

                            // sk, sm, sn, sp & st exceptions
                            if (cc.Length - lastCPrevWord > 1) {
                                for (int i = 0; i < ccNoParsing.Length; i++) {
                                    if (ccNoParsing.Contains(ccNoParse)) {
                                        dontParse = true;
                                        break;
                                    }
                                }
                            }
                            if (dontParse) {

                                basePhoneme = $"{cc[cc.Length - 2]}{cc[cc.Length - 1]}{v}";
                                if (ccNoParse == "hy") {
                                    basePhoneme = $"hhy{v}";
                                }
                                vccExceptions = $"1ng {cc[1]}{cc[2]}";
                                if (ccNoParse == "hy") {
                                    vccExceptions = $"1ng hhy";
                                }
                                if (ing && HasOto(vccExceptions, syllable.vowelTone)) {
                                    vccExceptions = $"1ng {cc[1]}{cc[2]}";
                                    if (ccNoParse == "hy") {
                                        vccExceptions = $"1ng hhy";
                                    }
                                    phonemes.Add(vccExceptions);
                                    startingC = 2;
                                } else {

                                    vccExceptions = $"{prevV}{cc[0]}-";

                                    if (vccExceptions == "ing-") {
                                        vccExceptions = "1ng-";
                                    }
                                    phonemes.Add(vccExceptions);
                                    if (HasOto($"{cc[0]} {cc[1]}{cc[2]}", syllable.vowelTone)) {
                                        phonemes.Add($"{cc[0]} {cc[1]}{cc[2]}");
                                        startingC = 2;
                                    } else {
                                        basePhoneme = $"-{cc[cc.Length - 2]}{cc[cc.Length - 1]}{v}";
                                        if (ccNoParse == "hy") {
                                            basePhoneme = $"-hhy{v}";
                                        }
                                        startingC = 0;
                                    }
                                }
                            }

                            if (phonemes.Count == 0) {

                                if (HasOto(vccExceptions, syllable.vowelTone)) {
                                    phonemes.Add(vccExceptions);
                                } else { startingC = 0; }

                                if (phonemes.Count == 0) {
                                    parsingVCC = $"{prevV}{cc[0]}-";
                                    if (cc.Length - lastCPrevWord - 1 > 0 && !dontParse && !HasOto($"{cc[0]} {cc[1]}", syllable.vowelTone)) {
                                        parsingVCC = $"{prevV}{cc[0]}";
                                    }
                                    if (!HasOto(parsingVCC, syllable.vowelTone)) {
                                        parsingVCC = CheckVCExceptions($"{prevV}{cc[0]}") + "-";
                                        if (!HasOto(parsingVCC, syllable.vowelTone)) {
                                            parsingVCC = $"{prevV} {cc[0]}";
                                        }
                                        if (vcVowels.ContainsKey(prevV)) {
                                            parsingVCC = $"{prevV}-";
                                        }
                                    }
                                    if (lastCPrevWord == 1 && stopCs.Contains($"{cc[0]}") && (!HasOto($"{cc[0]} {cc[1]}", syllable.vowelTone) && !vcVowels.ContainsKey(prevV))) {
                                        parsingVCC = $"{prevV}{cc[0]}";
                                        if (vcVowels.ContainsKey(prevV)) {
                                            parsingVCC = $"{prevV}-";
                                        }
                                    }

                                    if (ccSP == "sp") {
                                        parsingVCC = $"{prevV} sp";
                                    }


                                    phonemes.Add(parsingVCC);
                                }
                            }
                        }


                        for (int i = startingC; i < lastCforLoop; i++) {
                            parsingCC = $"{cc[i]}{cc[i + 1]}-";
                            if (vcVowels.ContainsKey(prevV) && phonemes.Count == 1) {
                                var vcVowelscc = $"{vcVowels[prevV]}{cc[i]}-";
                                if (i == lastCPrevWord - 1 && !HasOto($"{cc[i]} {cc[i + 1]}", syllable.vowelTone)) {
                                    vcVowelscc = $"{vcVowels[prevV]}{cc[i]}";
                                }
                                vcVowelscc = vcVowelscc.Replace("ngk", "nk");
                                phonemes.Add($"{vcVowelscc}");
                            }
                            if (dontParse && i == cc.Length - 3) {
                                parsingCC = $"{cc[i]} {cc[i + 1]}{cc[i + 2]}";
                                if (vcVowels.ContainsKey(prevV)) {
                                    parsingCC = $"{vcVowels[prevV]} {cc[i]}{cc[i + 1]}"; 
                                }
                            }

                            if (i == lastCPrevWord - 1) {
                                parsingCC = $"{cc[i]} {cc[i + 1]}";
                                if (vcVowels.ContainsKey(prevV) &&  i > 0 && !phonemes.Contains($"{cc[i - 1]}{cc[i]}")) {
                                    parsingCC = $"{cc[i - 1]}{cc[i]}";
                                }
                            }


                            if (i == lastCPrevWord - 2) {
                                parsingCC = $"{cc[i]}{cc[i + 1]}";
                                if (vcVowels.ContainsKey(prevV) && phonemes.Count < i + 1) {
                                    parsingCC = $"{vcVowels[prevV]}{cc[i]}";
                                }
                                if (HasOto($"{cc[i + 1]} {cc[i + 2]}", syllable.vowelTone)) {
                                    parsingCC = $"{cc[i]}{cc[i + 1]}-";
                                }
                                if (!HasOto(parsingCC, syllable.vowelTone)) {
                                    parsingCC = $"{cc[i]}{cc[i + 1]}-";
                                    if (vcVowels.ContainsKey(prevV)) {
                                        parsingCC = $"{vcVowels[prevV]}{cc[i]}-";
                                    }
                                    if (!HasOto(parsingCC, syllable.vowelTone)) {
                                        parsingCC = $"{cc[i]} {cc[i + 1]}";
                                    }
                                }
                            }
                            if (!HasOto(parsingCC, syllable.vowelTone) && i != lastCPrevWord - 1) {

                                parsingCC = $"{cc[i]}{cc[i + 1]}";
                                if (HasOto($"{cc[i]} {cc[i + 1]}", syllable.vowelTone)) {
                                    parsingCC = $"{cc[i]} {cc[i + 1]}";
                                }
                            }

                            //if (i + 1 != lastCforLoop - 1) {
                            //    parsingCC = $"{cc[i]}{cc[i + 1]}";
                            if (dontParse && i == cc.Length - 2) {
                                parsingCC = "";
                            }
                            //}

                            //ng to nk exception
                            parsingCC = parsingCC.Replace("ngk", "nk");

                            if (parsingCC != "" && HasOto(parsingCC, syllable.vowelTone)) {
                                phonemes.Add(parsingCC);
                            }
                        }

                        if (cc.Length - lastCPrevWord - 1 > 0 && !dontParse) {
                            basePhoneme = $"_{cc.Last()}{v}";
                        }

                        //if (ccNoParse == "str") {
                        if (cccExceptions.Contains($"{ccNoParse}")) {
                            phonemes.Add(ccNoParse);
                        }
                    }

                }
            }
                if (basePhoneme != null && !HasOto(basePhoneme, syllable.vowelTone)) { 
                basePhoneme = cc.Length > 0 ? $"{cc.Last()}{v}" : v; 
            }
            
            if (basePhoneme != null) {
                phonemes.Add(basePhoneme);
            }
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc.Select(c => ReplacePhoneme(c, ending.tone)).ToArray();
            string v = ReplacePhoneme(ending.prevV, ending.tone);
            int lastC = cc.Length - 1;

            var phonemes = new List<string>();
            // --------------------------- ENDING V ------------------------------- //
            if (ending.IsEndingV) {
                // try V- else no ending
                TryAddPhoneme(phonemes, ending.tone, $"{v}-");

            } else {
                var vc = $"{v}{cc[0]}";
                var currentCc = "";
                // --------------------------- ENDING VC ------------------------------- //
                if (ending.IsEndingVCWithOneConsonant) {

                    vc = CheckVCExceptions(vc) + "-";
                    if (!HasOto(vc, ending.tone)) {
                        if (vcVowels.ContainsKey(ending.prevV))
                            vc = $"{v}-";
                        currentCc = $"{vcVowels[v]}{cc[0]}-";
                        if (currentCc == $"ngk-")
                            currentCc = $"nk-";
                    }
                    phonemes.Add(vc);
                    
                    if (currentCc != "") {
                        phonemes.Add(currentCc);
                    }
                } else {
                    vc = $"{v}{cc[0]}";
                    vc = CheckVCExceptions(vc) + "-";
                    
                    // "1nks" exception
                    var startingC = 0;
                    var vcc = "";
                    var newV = v;
                    if ($"{v}" == "i" && $"{cc[0]}" == "ng") {
                        newV = "1";
                    }

                    if (cc.Length > 2) {
                        vcc = $"{newV}{cc[0]}{cc[1]}{cc[2]}-";
                        vc = vcc;
                        startingC = 2;
                        if (vcc == "1ngks-") {
                            vcc = "1nks-";
                        }

                        if (!HasOto(vcc, ending.tone)) {
                            vcc = $"{cc[0]}{cc[1]}{cc[2]}-";
                            vc = $"{newV}{cc[0]}-";
                            startingC = 2;
                        }
                    }


                    if (!HasOto(vcc, ending.tone) || vcc == "") {
                        vcc = $"{newV}{cc[0]}{cc[1]}-";
                        vc = vcc;
                        startingC = 1;
                        if (vcc == "1ngk-") {
                            vcc = "1nk-";
                        }
                    }

                    if (!HasOto(vcc, ending.tone)) {
                        vcc = $"{newV}{cc[0]}-";
                        vc = vcc;
                        startingC = 0;
                    }

                    //sp fix
                    var spCheck = $"{cc[0]}{cc[1]}";
                    if (spCheck == "sp") {
                        vcc = $"{newV} {cc[0]}{cc[1]}";
                        vc = vcc;
                        startingC = 1;
                    }
                    if (vcVowels.ContainsKey(v)) {
                        vc = $"{v}-";
                        vcc = vc;
                        startingC = 0;
                    }
                    if (HasOto(vcc, ending.tone)) {
                        if (HasOto(vc, ending.tone)) {
                            phonemes.Add(vc);
                        }
                        if (vc != vcc && vcc != "") {
                            phonemes.Add(vcc);
                        }
                    }


                    // --------------------------- ENDING VCC ------------------------------- //


                    for (var i = startingC; i < cc.Length - 1; i++) {
                        currentCc = $"{cc[i]}{cc[i + 1]}-";
                        if (vcVowels.ContainsKey(v) && phonemes.Count == 1) {
                            var vcVowelscc = $"{vcVowels[v]}{cc[i]}-";
                            vcVowelscc = vcVowelscc.Replace("ngk", "nk");
                            phonemes.Add($"{vcVowelscc}");
                        }
                        if (!HasOto(currentCc, ending.tone)) {
                            currentCc = $"{cc[i]}{cc[i + 1]}";
                            if (vcVowels.ContainsKey(v) && phonemes.Count == 1) {
                                phonemes.Add($"{vcVowels[v]}{cc[i]}");
                            }
                        }
                        

                        if (!HasOto(currentCc, ending.tone)) {
                            currentCc = $"{cc[i]} {cc[i + 1]}";
                            if (vcVowels.ContainsKey(v) && phonemes.Count == 1) {
                                phonemes.Add($"{vcVowels[v]} {cc[i]}");
                            }
                        }
                        if (!HasOto(currentCc, ending.tone)) {
                            currentCc = $"{cc[i]}x";
                            if (vcVowels.ContainsKey(v) && phonemes.Count == 1) {
                                phonemes.Add($"{vcVowels[v]}x");
                            }
                            if (i == cc.Length - 2) {
                                phonemes.Add(currentCc);
                                currentCc = $"{cc[i + 1]}x";
                                if (vcVowels.ContainsKey(v) && phonemes.Count == 1) {
                                    phonemes.Add($"{cc[i]}x");
                                }
                            }
                        }
                        //ng to nk exception
                        currentCc = currentCc.Replace("ngk", "nk");

                        if (HasOto(currentCc, ending.tone)) {
                            phonemes.Add(currentCc);
                        }
                    }

                
                }
            }

            // ---------------------------------------------------------------------------------- //

            return phonemes;
        }


        private string CheckVCExceptions(string vc) {
            if (vcExceptions.ContainsKey(vc)) {
                vc = vcExceptions[vc];
            }
            return vc;
        }
        protected override string ValidateAlias(string alias) {
            //foreach (var consonant in new[] { "h" }) {
            //    alias = alias.Replace(consonant, "hh");
            //}
            if (isYamlFallbacks) {
                foreach (var syllable in yamlFallbacks.OrderByDescending(f => f.Key.Length)) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }
            foreach (var consonant in new[] { "6r" }) {
                alias = alias.Replace(consonant, "3");
            }

            return alias;
        }
    }
}
