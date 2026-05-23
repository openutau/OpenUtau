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
    [Phonemizer("English VCCV Phonemizer", "EN VCCV", "cubialpha & Mim", language: "UTAU")]
    // V3 of the phonemizer
    // This is a temporary solution until Cz's comes out with their own.
    // Feel free to use the Lyric Parser plugin for more accurate pronunciations & support of ConVel.

    // Thanks to cubialpha, Cz, Halo/BagelHero, nago, and Anjo for their help.
    // cadlaxa here ^_^
    public class EnglishVCCVPhonemizer : SyllableBasedPhonemizer {
        private const string LatestVersion = "1.1";
        private string[] vowels = "a,@,u,0,8,I,e,3,A,i,E,O,Q,6,o,1ng,9,&,x,1,Y,L,W".Split(",");
        private readonly string[] consonants = "b,ch,d,dh,f,g,h,j,k,l,m,n,ng,p,r,s,sh,t,th,v,w,y,z,zh,dd,hh,sp,st".Split(",");
        private Dictionary<string, string> dictionaryReplacements = ("aa=a;ae=@;ah=u;ao=9;aw=8;ay=I;" +
            "b=b;ch=ch;d=d;dh=dh;eh=e;er=3;ey=A;f=f;g=g;hh=h;hhy=hh;ih=i;iy=E;jh=j;k=k;l=l;m=m;n=n;ng=ng;ow=O;oy=Q;" +
            "p=p;r=r;s=s;sh=sh;t=t;th=th;uh=6;uw=o;v=v;w=w;y=y;z=z;zh=zh;dx=dd;").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        // for fallbacks
        private readonly Dictionary<string, string> replacements = "ax=x".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isReplacements = false;

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
        private readonly string[] ccNoParsing = { "sk", "sm", "sn", "sp", "st", "hhy" };
        private readonly string[] stopCs = { "b", "d", "g", "k", "p", "t" };
        private readonly string[] ucvCs = { "r", "l", "w", "y", "f"};
        private readonly string[] starlightccs = { "rl", "ll", "nn", "mm" };
        private string[] tails = "-".Split(',');

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;
        private List<Replacement> splittingReplacements = new List<Replacement>();
        // Store the merging replacements
        private List<Replacement> mergingReplacements = new List<Replacement>();

        protected override IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, "envccv.yaml");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.envccv_template);
            }
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "envccv.yaml");
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
            string[] tr_dr = new[] { "tr", "dr"};
            foreach (string s in phonemes) {
                switch (s) {
                    case var str when tr_dr.Contains(str) && !HasOto($"{str}{vowels}", note.tone) && !HasOto($"A {str}", note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { s[0].ToString(), s[1].ToString() });
                        break;
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
                    file = Path.Combine(singer.Location, "envccv.yaml");
                }
                else if (!string.IsNullOrEmpty(PluginDir)) {
                    file = Path.Combine(PluginDir, "envccv.yaml");
                }
                else {
                    Log.Error("Singer location and PluginDir are both null or empty. Cannot locate 'envccv.yaml'.");
                    return;
                }
                try {
                    bool shouldWriteTemplate = false;
                    bool shouldBackupOldFile = false;

                    if (File.Exists(file)) {
                        try {
                            // Build YAML deserializer
                            var deserializer = new DeserializerBuilder()
                                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                .Build();

                            using var reader = new StreamReader(file);
                            var config = deserializer.Deserialize<Dictionary<string, object>>(reader);

                            if (config == null || !config.ContainsKey("version")) {
                                shouldWriteTemplate = true;
                                shouldBackupOldFile = true; // No version → backup old
                            }
                            else {
                                string currentVersion = config["version"]?.ToString()?.Trim() ?? "";

                                // If version is missing OR outdated → backup old + write new
                                if (string.IsNullOrWhiteSpace(currentVersion) || currentVersion != LatestVersion) {
                                    shouldWriteTemplate = true;
                                    shouldBackupOldFile = true;
                                }
                            }
                        }
                        catch (Exception ex) {
                            Log.Error(ex, $"Failed to read '{file}', backing up old file and writing a fresh one...");
                            shouldWriteTemplate = true;
                            shouldBackupOldFile = true;
                        }
                    }
                    else {
                        shouldWriteTemplate = true;
                    }

                    // If needed, back up the old file
                    if (shouldBackupOldFile && File.Exists(file)) {
                        try {
                            string backupFile = Path.Combine(
                                Path.GetDirectoryName(file)!,
                                $"envccv_backup.yaml"
                            );
                            File.Move(file, backupFile);
                            Log.Warning($"Old envccv.yaml has been backed up as: {backupFile}");
                        }
                        catch (Exception e) {
                            Log.Error(e, "Failed to back up old envccv.yaml. Proceeding with new template anyway.");
                        }
                    }

                    // Write a fresh template if necessary
                    if (shouldWriteTemplate) {
                        try {
                            File.WriteAllBytes(file, Data.Resources.envccv_template);
                            Log.Information($"'{file}' created or updated to latest version {LatestVersion}");
                        }
                        catch (Exception e) {
                            Log.Error(e, $"Failed to write 'envccv.yaml' to {file}");
                        }
                    }
                }
                catch (Exception ex) {
                    Log.Error(ex, $"Unexpected error while ensuring envccv.yaml at {file}");
                }

                if (File.Exists(file)) {
                    try {
                        var data = Core.Yaml.DefaultDeserializer.Deserialize<CZSampaYAMLData>(File.ReadAllText(file));

                        // Load vowels
                        try {
                            var loadVowels = data.symbols
                                ?.Where(s => s.type == "vowel")
                                .Select(s => s.symbol)
                                .ToList() ?? new List<string>();

                            vowels = vowels.Concat(loadVowels).Distinct().ToArray();
                        } catch (Exception ex) {
                            Log.Error($"Failed to load vowels from envccv.yaml: {ex.Message}");
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
                       Log.Error($"Failed to parse envccv.yaml: {ex.Message}, Exception Type: {ex.GetType()}");
                    }
                }
                ReadDictionaryAndInit();
                this.singer = singer;
            }
        }
        public class CZSampaYAMLData {
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
            string[] PreviousWordCc = syllable.PreviousWordCc.Select(ReplacePhoneme).ToArray();
            string[] CurrentWordCc = syllable.CurrentWordCc.Select(ReplacePhoneme).ToArray();
            int lastC = cc.Length - 1;
            int lastCPrevWord = syllable.prevWordConsonantsCount;

            foreach (var entry in replacements) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Key, syllable.tone)) {
                    isReplacements = true;
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
                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                        basePhoneme = $"{v}";
                    }
                }
                // --------------------------- STARTING CV ------------------------------- //
            } else if (syllable.IsStartingCVWithOneConsonant) {
                //if starting CV -> [-CV], fallback to [CV]
                basePhoneme = $"-{cc[0]}{v}";
                if (!HasOto(basePhoneme, syllable.tone)) {
                    basePhoneme = $"{cc[0]}{v}";
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
                    if (vc == $"i ng") {
                        vc = $"1 ng";
                    }
                    if (HasOto(vc, syllable.vowelTone)) {
                        vc = $"{prevV} {cc.Last()}";
                    } else {
                        vc = $"{prevV}{cc.Last()}";
                    }

                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                        if ($"{cc.Last()}" == "ng")
                            basePhoneme = $"_{v}";
                    }


                    if (lastCPrevWord == 1 && CurrentWordCc.Length == 0)
                        if (($"{PreviousWordCc.Last()}" == "r") || ($"{PreviousWordCc.Last()}" == "l") || ($"{PreviousWordCc.Last()}" == "ng")) {
                            if (HasOto($"{prevV}{PreviousWordCc.Last()}-", syllable.vowelTone) && HasOto($"{PreviousWordCc.Last()} {v}", syllable.vowelTone)) {
                                basePhoneme = $"{PreviousWordCc.Last()} {v}";
                                vc = $"{prevV}{PreviousWordCc.Last()}-";
                            } else
                                vc = $"{prevV}{PreviousWordCc.Last()}-";
                        }

                    if (!HasOto(vc, syllable.vowelTone)) {
                        if ($"{cc.Last()}" == "ng")
                            vc = $"{prevV}ng";
                    }

                    vc = CheckVCExceptions(vc);
                    phonemes.Add(vc);

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
                            if (!HasOto(basePhoneme, syllable.vowelTone)) {
                                basePhoneme = $"_{v}";
                            }

                            var vc = $"{prevV} {ccNoParse}";
                            if ($"{ccNoParse}" == "hhy") {
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
                                    phonemes.Add(parsingVCC);
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
                                vccExceptions = $"1ng {cc[1]}{cc[2]}";

                                if (ing && HasOto(vccExceptions, syllable.vowelTone)) {
                                    vccExceptions = $"1ng {cc[1]}{cc[2]}";
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
                                    if (!HasOto(parsingVCC, syllable.vowelTone)) {
                                        parsingVCC = CheckVCExceptions($"{prevV}{cc[0]}") + "-";
                                        if (!HasOto(parsingVCC, syllable.vowelTone)) {
                                            parsingVCC = $"{prevV} {cc[0]}";
                                        }
                                    }
                                    if (lastCPrevWord == 1 && stopCs.Contains($"{cc[0]}")) {
                                        parsingVCC = $"{prevV}{cc[0]}";
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

                            if (dontParse && i == cc.Length - 3) {
                                parsingCC = $"{cc[i]} {cc[i + 1]}{cc[i + 2]}";
                            }

                            if (i == lastCPrevWord - 1) {
                                parsingCC = $"{cc[i]} {cc[i + 1]}";
                            }


                            if (i == lastCPrevWord - 2) {
                                parsingCC = $"{cc[i]}{cc[i + 1]}";
                                if (!HasOto(parsingCC, syllable.vowelTone)) {
                                    parsingCC = $"{cc[i]}{cc[i + 1]}-";
                                    if (!HasOto(parsingCC, syllable.vowelTone)) {
                                        parsingCC = $"{cc[i]} {cc[i + 1]}-";
                                    }
                                }
                            }
                            if (!HasOto(parsingCC, syllable.vowelTone) && i != lastCPrevWord - 1) {

                                parsingCC = $"{cc[i]}{cc[i + 1]}";
                            }

                            //if (i + 1 != lastCforLoop - 1) {
                            //    parsingCC = $"{cc[i]}{cc[i + 1]}";
                            if (dontParse && i == cc.Length - 2) {
                                parsingCC = "";
                            }
                            //}

                            //ng to nk exception
                            if ($"{cc[i]}" == "ng" && $"{cc[i + 1]}" == "th" && i + 1 != lastCPrevWord) {
                                parsingCC = $"nkth";
                            }

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

                if (!HasOto(basePhoneme, syllable.vowelTone)) { basePhoneme = $"{cc.Last()}{v}"; }
                phonemes.Add(basePhoneme);
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
                // --------------------------- ENDING VC ------------------------------- //
                if (ending.IsEndingVCWithOneConsonant) {

                    if (HasOto(CheckVCExceptions(vc) + "-", ending.tone)) {
                        phonemes.Add(CheckVCExceptions(vc) + "-");
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
                        var currentCc = $"{cc[i]}{cc[i + 1]}-";
                        if (!HasOto(currentCc, ending.tone)) {
                            currentCc = $"{cc[i]}{cc[i + 1]}";
                        }

                        //ng to nk exception
                        if ($"{cc[i]}" == "ng" && $"{cc[i + 1]}" == "th" && i == cc.Length - 2) {
                            currentCc = $"nkth-";
                        }

                        if (!HasOto(currentCc, ending.tone)) {
                            currentCc = $"{cc[i]} {cc[i + 1]}";

                        }
                        if (!HasOto(currentCc, ending.tone)) {
                            currentCc = $"{cc[i]}x";
                            if (i == cc.Length - 2) {
                                phonemes.Add(currentCc);
                                currentCc = $"{cc[i + 1]}x";
                            }
                        }


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
            if (isReplacements) {
                foreach (var syllable in replacements.OrderByDescending(f => f.Key.Length)) {
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
