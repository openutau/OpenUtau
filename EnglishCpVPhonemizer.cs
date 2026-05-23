using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Classic;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("English C+V Phonemizer", "EN C+V", "Cadlaxa", language: "UTAU")]
    // Custom C+V Phonemizer for OU
    // Arpabet only but in the future update, it can be customize the phoneme set
    public class EnglishCpVPhonemizer : SyllableBasedPhonemizer {
        private const string LatestVersion = "1.1";
        private string[] vowels = {
        "aa", "ax", "ae", "ah", "ao", "aw", "ay", "eh", "er", "ey", "ih", "iy", "ow", "oy", "uh", "uw", "a", "e", "i", "o", "u", "ai", "ei", "oi", "au", "ou", "ix", "ux",
        "aar", "ar", "axr", "aer", "ahr", "aor", "or", "awr", "aur", "ayr", "air", "ehr", "eyr", "eir", "ihr", "iyr", "ir", "owr", "our", "oyr", "oir", "uhr", "uwr", "ur",
        "aal", "al", "axl", "ael", "ahl", "aol", "ol", "awl", "aul", "ayl", "ail", "ehl", "el", "eyl", "eil", "ihl", "iyl", "il", "owl", "oul", "oyl", "oil", "uhl", "uwl", "ul",
        "aan", "an", "axn", "aen", "ahn", "aon", "on", "awn", "aun", "ayn", "ain", "ehn", "en", "eyn", "ein", "ihn", "iyn", "in", "own", "oun", "oyn", "oin", "uhn", "uwn", "un",
        "aang", "ang", "axng", "aeng", "ahng", "aong", "ong", "awng", "aung", "ayng", "aing", "ehng", "eng", "eyng", "eing", "ihng", "iyng", "ing", "owng", "oung", "oyng", "oing", "uhng", "uwng", "ung",
        "aam", "am", "axm", "aem", "ahm", "aom", "om", "awm", "aum", "aym", "aim", "ehm", "em", "eym", "eim", "ihm", "iym", "im", "owm", "oum", "oym", "oim", "uhm", "uwm", "um", "oh",
        "eu", "oe", "yw", "yx", "wx", "ox", "ex", "ea", "ia", "oa", "ua", "ean", "eam", "eang", "N", "nn", "mm", "ll"
        };
        private static string[] diphthongs = { "ay", "ey", "oy", "aw", "ow" };
        private static string[] c_cR = { "n" };
        private static string[] consonants = "b,ch,d,dh,dr,dx,f,g,hh,jh,k,l,m,n,ng,p,q,r,s,sh,t,th,tr,v,w,y,z".Split(',');
        private static string[] affricate = "".Split(',');
        private static string[] fricative = "".Split(',');
        private static string[] aspirate = "".Split(',');
        private static string[] semivowel = "".Split(',');
        private static string[] liquid = "".Split(',');
        private static string[] nasal = "".Split(',');
        private static string[] stop = "".Split(',');
        private static string[] tap = "".Split(',');
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "";
        private Dictionary<string, string> dictionaryReplacements;
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;
        // Store the splitting replacements
        private List<Replacement> splittingReplacements = new List<Replacement>();
        // Store the merging replacements
        private List<Replacement> mergingReplacements = new List<Replacement>();

        // For banks with missing vowels
        private readonly Dictionary<string, string> missingVphonemes = "ax=ah,aa=ah,ae=ah,iy=ih,uh=uw,ix=ih,ux=uh,oh=ao,eu=uh,oe=ax,uy=uw,yw=uw,yx=iy,wx=uw,ea=eh,ia=iy,oa=ao,ua=uw,R=-,N=n,mm=m,ll=l".Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isMissingVPhonemes = false;

        // For banks with missing custom consonants
        private readonly Dictionary<string, string> missingCphonemes = "nx=n,tx=t,dx=d,zh=sh,z=s,ng=n,cl=q,vf=q,dd=d,lx=l".Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isMissingCPhonemes = false;

        // TIMIT symbols
        private readonly Dictionary<string, string> timitphonemes = "axh=ax,bcl=b,dcl=d,eng=ng,gcl=g,hv=hh,kcl=k,pcl=p,tcl=t".Split(',')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private bool isTimitPhonemes = false;

        private static Dictionary<string, string> DiphthongExceptions = diphthongs.ToDictionary(
            key => key,
            value => value.Last().ToString()
        );

        private Dictionary<string, double> PhonemeOverrides = new Dictionary<string, double>();

        private readonly string[] ccvException = { "ch", "dh", "dx", "fh", "gh", "hh", "jh", "kh", "ph", "ng", "sh", "th", "vh", "wh", "zh" };
        private readonly string[] RomajiException = { "a", "e", "i", "o", "u" };
        private string[] tails = "-,R".Split(',');
        private bool isTails = false;

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (tails.Contains(note.lyric)) {
                isTails = true;
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

            // SPLITS UP DR AND TR
            string[] tr = new[] { "tr" };
            string[] dr = new[] { "dr" };
            string[] wh = new[] { "wh" };
            string[] av_c = new[] { "al", "am", "an", "ang", "ar" };
            string[] ev_c = new[] { "el", "em", "en", "eng", "err" };
            string[] iv_c = new[] { "il", "im", "in", "ing", "ir" };
            string[] ov_c = new[] { "ol", "om", "on", "ong", "or" };
            string[] uv_c = new[] { "ul", "um", "un", "ung", "ur" };
            string[] ccv = new[] { "bw", "by", "dw", "dy", "fw", "fy", "gw", "gy", "hw", "hy", "kw", "ky"
                                   , "lw", "ly", "mw", "my", "nw", "ny", "pw", "py", "rw", "ry", "sw", "sy"
                                    , "tw", "ty", "ts", "vw", "vy", "zw", "zy"};
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
            IEnumerable<string> phonemes;
            if (hasReplacements) {
                phonemes = finalPhonemes;
            } else {
                phonemes = original;
            }
            foreach (string s in phonemes) {
                switch (s) {
                    case var str when dr.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "jh", s[1].ToString() });
                        break;
                    case var str when tr.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "ch", s[1].ToString() });
                        break;
                    case var str when wh.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "hh", s[1].ToString() });
                        break;
                    case var str when av_c.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "aa", s[1].ToString() });
                        break;
                    case var str when ev_c.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "eh", s[1].ToString() });
                        break;
                    case var str when iv_c.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "iy", s[1].ToString() });
                        break;
                    case var str when ov_c.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "ao", s[1].ToString() });
                        break;
                    case var str when uv_c.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { "uw", s[1].ToString() });
                        break;
                    case var str when vowel3S.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { s.Substring(0, 2), s[2].ToString() });
                        break;
                    case var str when vowel4S.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { s.Substring(0, 2), s.Substring(2, 2) });
                        break;
                    case var str when ccv.Contains(str) && !HasOto($"{str}", note.tone) && !HasOto(ValidateAlias(str), note.tone):
                        finalProcessedPhonemes.AddRange(new string[] { s[0].ToString(), s[1].ToString() });
                        break;
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
            string path = Path.Combine(PluginDir, "en-cPv.yaml");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.en_cPv_template);
            }
            // LOAD DICTIONARY FROM SINGER FOLDER
            if (singer != null || singer.Found || singer.Loaded) {
                string file = Path.Combine(singer.Location, "en-cPv.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());
            g2ps.Add(new ArpabetPlusG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }

        public override void SetSinger(USinger singer) {
            if (this.singer != singer) {
                string file;
                if (singer != null && singer.Found && singer.Loaded && !string.IsNullOrEmpty(singer.Location)) {
                    file = Path.Combine(singer.Location, "en-cPv.yaml");
                } else if (!string.IsNullOrEmpty(PluginDir)) {
                    file = Path.Combine(PluginDir, "en-cPv.yaml");
                } else {
                    Log.Error("Singer location and PluginDir are both null or empty. Cannot locate 'en-cPv.yaml'.");
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
                            } else {
                                string currentVersion = config["version"]?.ToString()?.Trim() ?? "";

                                // If version is missing OR outdated → backup old + write new
                                if (string.IsNullOrWhiteSpace(currentVersion) || currentVersion != LatestVersion) {
                                    shouldWriteTemplate = true;
                                    shouldBackupOldFile = true;
                                }
                            }
                        } catch (Exception ex) {
                            Log.Error(ex, $"Failed to read '{file}', backing up old file and writing a fresh one...");
                            shouldWriteTemplate = true;
                            shouldBackupOldFile = true;
                        }
                    } else {
                        shouldWriteTemplate = true;
                    }

                    // If needed, back up the old file
                    if (shouldBackupOldFile && File.Exists(file)) {
                        try {
                            string backupFile = Path.Combine(
                                Path.GetDirectoryName(file)!,
                                $"en-cPv_backup.yaml"
                            );
                            File.Move(file, backupFile);
                            Log.Warning($"Old en-cPv.yaml has been backed up as: {backupFile}");
                        } catch (Exception e) {
                            Log.Error(e, "Failed to back up old en-cPv.yaml. Proceeding with new template anyway.");
                        }
                    }

                    // Write a fresh template if necessary
                    if (shouldWriteTemplate) {
                        try {
                            File.WriteAllBytes(file, Data.Resources.en_cPv_template);
                            Log.Information($"'{file}' created or updated to latest version {LatestVersion}");
                        } catch (Exception e) {
                            Log.Error(e, $"Failed to write 'en-cPv.yaml' to {file}");
                        }
                    }
                } catch (Exception ex) {
                    Log.Error(ex, $"Unexpected error while ensuring en-cPv.yaml at {file}");
                }

                if (File.Exists(file)) {
                    try {
                        var data = Core.Yaml.DefaultDeserializer.Deserialize<ArpabetYAMLData>(File.ReadAllText(file));

                        // Load vowels and diphthongs
                        try {
                            var loadedVowels = data.symbols
                                ?.Where(s => s.type == "vowel")
                                .Select(s => s.symbol)
                                .ToList() ?? new List<string>();

                            var loadedDiphthongs = data.symbols
                                ?.Where(s => s.type == "diphthong")
                                .Select(s => s.symbol)
                                .ToList() ?? new List<string>();

                            // Combine vowels and diphthongs, then remove duplicates
                            vowels = vowels.Concat(loadedVowels)
                                        .Concat(loadedDiphthongs)
                                        .Distinct()
                                        .ToArray();

                            // Load diphthongs specifically to their own list
                            diphthongs = loadedDiphthongs.ToArray();

                        } catch (Exception ex) {
                            Log.Error($"Failed to load vowels and diphthongs from en-cPv.yaml: {ex.Message}");
                        }
                        // Load tails
                        try {
                            var loadTails = data.symbols
                                ?.Where(s => s.type == "tail")
                                .Select(s => s.symbol)
                                .ToList() ?? new List<string>();

                            tails = tails.Concat(loadTails).Distinct().ToArray();
                        } catch (Exception ex) {
                            Log.Error($"Failed to load tails from en-cPv.yaml: {ex.Message}");
                        }
                        // Load the various consonant types for double consonant endings
                        var fricatives = data.symbols
                            ?.Where(s => s.type == "fricative")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();

                        var aspirates = data.symbols
                            ?.Where(s => s.type == "aspirate")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();

                        var semivowels = data.symbols
                            ?.Where(s => s.type == "semivowel")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();

                        var liquids = data.symbols
                            ?.Where(s => s.type == "liquid")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();

                        var nasals = data.symbols
                            ?.Where(s => s.type == "nasal")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();
                        /// others
                        var stops = data.symbols
                            ?.Where(s => s.type == "stop")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();

                        var taps = data.symbols
                            ?.Where(s => s.type == "tap")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();

                        var affricates = data.symbols
                            ?.Where(s => s.type == "affricate")
                            .Select(s => s.symbol)
                            .ToList() ?? new List<string>();

                        PhonemeOverrides = data.timings
                            ?.ToDictionary(t => t.symbol, t => t.value)
                            ?? new Dictionary<string, double>();

                        // Combine all the consonant types into one list
                        c_cR = fricatives
                            .Concat(aspirates)
                            .Concat(semivowels)
                            .Concat(liquids)
                            .Concat(nasals)
                            .Distinct()
                            .ToArray();

                        // Load consonant types into their respective lists
                        fricative = fricatives.Distinct().ToArray();
                        aspirate = aspirates.Distinct().ToArray();
                        semivowel = semivowels.Distinct().ToArray();
                        liquid = liquids.Distinct().ToArray();
                        nasal = nasals.Distinct().ToArray();
                        stop = stops.Distinct().ToArray();
                        tap = taps.Distinct().ToArray();
                        affricate = affricates.Distinct().ToArray();
                        /*consonants = fricatives
                            .Concat(aspirates)
                            .Concat(semivowels)
                            .Concat(liquids)
                            .Concat(nasals)
                            .Concat(stop)
                            .Concat(tap)
                            .Concat(affricate)
                            .Distinct()
                            .ToArray();*/
                        // Load diphthong exceptions
                        try {
                            var allDiphthongs = data.symbols
                                ?.Where(s => s.type == "diphthong")
                                .Select(s => s.symbol)
                                .ToList() ?? new List<string>();

                            // Load explicit exceptions from the new YAML section
                            var loadedDiphthongExceptions = data.diphthongs
                                ?.ToDictionary(d => d.from, d => d.to) ?? new Dictionary<string, string>();

                            // Initialize the DiphthongExceptions dictionary with explicit exceptions
                            DiphthongExceptions = new Dictionary<string, string>(loadedDiphthongExceptions);

                            // Create default mappings for diphthongs without an explicit exception [aw=aw], [ay=ay] etc
                            foreach (var diphthong in allDiphthongs) {
                                if (!DiphthongExceptions.ContainsKey(diphthong)) {
                                    DiphthongExceptions.Add(diphthong, diphthong);
                                }
                            }
                        } catch (Exception ex) {
                            Log.Error($"Failed to load diphthongs and exceptions: {ex.Message}");
                        }
                        // Load replacements (errors out if there's no replacements)
                        try {
                            if (data?.replacements != null && data.replacements.Any() == true) {
                                dictionaryReplacements = new Dictionary<string, string>();
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
                                dictionaryReplacements = new Dictionary<string, string>();
                                mergingReplacements = new List<Replacement>();
                                splittingReplacements = new List<Replacement>();
                            }
                        } catch (Exception ex) {
                            Log.Error($"Failed to load replacements from en-cPv.yaml: {ex.Message}");
                        }
                        // Load fallbacks
                        try {
                            if (data?.fallbacks?.Any() == true) {
                                foreach (var df in data.fallbacks) {
                                    if (!string.IsNullOrEmpty(df.from) && !string.IsNullOrEmpty(df.to)) {
                                        // Overwrite or add
                                        missingVphonemes[df.from] = df.to;
                                    } else {
                                        Log.Warning("Ignored YAML fallback with missing 'from' or 'to' value.");
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            Log.Error($"Failed to load fallbacks from YAML: {ex.Message}");
                        }

                    } catch (Exception ex) {
                        Log.Error($"Failed to parse en-cPv.yaml: {ex.Message}, Exception Type: {ex.GetType()}");
                    }
                }
                ReadDictionaryAndInit();
                this.singer = singer;
            }
        }

        public class ArpabetYAMLData {
            public SymbolData[] symbols { get; set; } = Array.Empty<SymbolData>();
            public Replacement[] replacements { get; set; } = Array.Empty<Replacement>();
            public Fallbacks[] fallbacks { get; set; } = Array.Empty<Fallbacks>();
            public Fallbacks[] diphthongs { get; set; } = Array.Empty<Fallbacks>();
            public Timings[] timings { get; set; } = Array.Empty<Timings>();

            public struct SymbolData {
                public string symbol { get; set; }
                public string type { get; set; }
            }
            public struct Fallbacks {
                public string from { get; set; }
                public string to { get; set; }
            }
            public struct Timings {
                public string symbol { get; set; }
                public double value { get; set; }
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
            //string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;
            string basePhoneme;
            var phonemes = new List<string>();
            var lastC = cc.Length - 1;
            var firstC = 0;
            string[] CurrentWordCc = syllable.CurrentWordCc.Select(ReplacePhoneme).ToArray();
            string[] PreviousWordCc = syllable.PreviousWordCc.Select(ReplacePhoneme).ToArray();
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

            // Check for missing TIMIT phonemes
            foreach (var entry in timitphonemes) {
                if (!HasOto(entry.Key, syllable.tone) && !HasOto(entry.Value, syllable.tone)) {
                    isTimitPhonemes = true;
                    break;
                }
            }

            // STARTING V
            if (syllable.IsStartingV) {
                // TRIES - V THEN -V AND SO ON
                basePhoneme = AliasFormat(v, "startingV", syllable.vowelTone, "");
            }
            // [V V] or [V C][- C/C][V]/[V]
            else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    basePhoneme = $"{prevV} {v}";
                    if (!HasOto(basePhoneme, syllable.vowelTone) && !HasOto(ValidateAlias(basePhoneme), syllable.vowelTone) && DiphthongExceptions.ContainsKey(prevV)) {
                        // VV IS NOT PRESENT, CHECKS DiphthongExceptions LOGIC
                        var vc = $"{prevV} {DiphthongExceptions[prevV]}";
                        if (!HasOto(vc, syllable.vowelTone) && !HasOto(ValidateAlias(vc), syllable.vowelTone)) {
                            vc = AliasFormat($"{DiphthongExceptions[prevV]}", "diph_mix", syllable.vowelTone, "");
                        }
                        TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc));
                        basePhoneme = AliasFormat(v, "vv", syllable.vowelTone, "");
                    } else {
                        {
                            if (!HasOto($"{prevV} {v}", syllable.vowelTone) || !HasOto(ValidateAlias($"{prevV} {v}"), syllable.vowelTone)) {
                                basePhoneme = AliasFormat(v, "vv", syllable.vowelTone, "");
                            } else {
                                basePhoneme = $"{prevV} {v}";
                            }
                        }
                    }
                    // EXTEND AS [V]
                } else if (!HasOto($"{prevV} {v}", syllable.vowelTone) || !HasOto(ValidateAlias($"{prevV} {v}"), syllable.vowelTone) || missingVphonemes.ContainsKey(prevV)) {
                    basePhoneme = AliasFormat(v, "vv", syllable.vowelTone, "");
                } else if (HasOto($"{prevV} {v}", syllable.vowelTone) || HasOto(ValidateAlias($"{prevV} {v}"), syllable.vowelTone) || missingVphonemes.ContainsKey(prevV)) {
                    basePhoneme = AliasFormat(v, "vv", syllable.vowelTone, "");
                } else {
                    // PREVIOUS ALIAS WILL EXTEND as [V V]
                    basePhoneme = null;
                }

            } else if (syllable.IsStartingCVWithOneConsonant) {
                /// [- C/-C/C]
                basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.tone, ""));

            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                //// CCV with multiple starting consonants with cc's support
                basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                // TRY RCC [- CC] [-CC] [CC]
                for (var i = cc.Length; i > 1; i--) {
                    if (TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{string.Join("", cc.Take(i))}", "cc_start", syllable.tone, ""))) {
                        firstC = i - 1;
                    }
                    break;
                }
                // [- C] [-C] [C]
                if (phonemes.Count == 0) {
                    TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc_start", syllable.tone, ""));
                }
                /// VCV PART (not starting but in the middle phrase)
            } else {
                // [V] to [-V] to [- V]
                basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                if (isTails && basePhoneme.Contains("-")) {
                    if (HasOto($"{cc.Last()} -", syllable.tone) || HasOto($"{cc.Last()}-", syllable.tone)
                    || HasOto($"{cc.Last()}_", syllable.tone)) {
                        // like [C1 -]
                        basePhoneme = AliasFormat($"{cc.Last()}", "cc_end", syllable.tone, "");
                    } else {
                        basePhoneme = null;
                    }
                }
                // try [V C], [V CC], [VC C], [V -][- C]
                for (var i = lastC + 1; i >= 0; i--) {
                    var vr = $"_{prevV}";
                    var vr1 = $"{prevV}-";
                    var vc = $"{prevV} {cc[0]}";
                    bool CCV = false;
                    if (syllable.CurrentWordCc.Length >= 2 && !ccvException.Contains(cc[0])) {
                        if (HasOto($"{string.Join("", cc)}", syllable.vowelTone) || HasOto($"- {string.Join("", cc)}", syllable.vowelTone) || HasOto($"-{string.Join("", cc)}", syllable.vowelTone)) {
                            CCV = true;
                        }
                    }
                    if (i == 0 && !HasOto(vc, syllable.tone)) {
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc", syllable.tone, ""));
                        break;
                        /// use vowel ending
                    } else if (DiphthongExceptions.ContainsKey(prevV) && ((HasOto(vr, syllable.tone) || HasOto(ValidateAlias(vr), syllable.tone) || (HasOto(vr1, syllable.tone) || HasOto(ValidateAlias(vr1), syllable.tone)) && !HasOto(vc, syllable.tone)))) {
                        TryAddPhoneme(phonemes, syllable.vowelTone, AliasFormat($"{DiphthongExceptions[prevV]}", "diph_mix", syllable.vowelTone, ""));
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc", syllable.tone, ""));
                        break;
                        /// use consonants for diphthongs if the vb doesn't have vowel endings
                    } else if (DiphthongExceptions.ContainsKey(prevV) && (!(HasOto(vr, syllable.tone) || HasOto(ValidateAlias(vr), syllable.tone) || (HasOto(vr1, syllable.tone) || HasOto(ValidateAlias(vr1), syllable.tone)) && !HasOto(vc, syllable.tone)))) {
                        TryAddPhoneme(phonemes, syllable.vowelTone, AliasFormat($"{DiphthongExceptions[prevV]}", "diph_mix", syllable.vowelTone, ""));
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc", syllable.tone, ""));

                        break;
                    } else if (HasOto(vc, syllable.tone) || HasOto(ValidateAlias(vc), syllable.tone)) {
                        TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc));
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{cc[0]}", "cc", syllable.tone, ""));
                        break;
                        /// CC+V
                    } else if (CCV) {
                        TryAddPhoneme(phonemes, syllable.tone, vc, ValidateAlias(vc));
                        TryAddPhoneme(phonemes, syllable.tone, AliasFormat($"{string.Join("", cc)}", "cc", syllable.tone, ""));
                        firstC = 1;
                        break;
                    } else {
                        continue;
                    }
                }
            }

            for (var i = firstC; i < lastC; i++) {
                var cc1 = $"{cc.Skip(i)}";
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                // [C1][C2]
                if (!HasOto(cc1, syllable.tone) || !HasOto(ValidateAlias(cc1), syllable.tone)) {
                    cc1 = AliasFormat($"{cc[i + 1]}", "cc", syllable.tone, "");
                }
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                // CCV
                if (syllable.CurrentWordCc.Length >= 2) {
                    if ((HasOto($"{v}", syllable.vowelTone) || HasOto($"- {v}", syllable.vowelTone) || HasOto($"-{v}", syllable.vowelTone)) && HasOto(AliasFormat($"{string.Join("", cc.Skip(i + 1))}", "cc", syllable.tone, ""), syllable.vowelTone)) {
                        basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                        lastC = i;
                    } else if ((HasOto(AliasFormat(v, "cv", syllable.tone, ""), syllable.vowelTone)) && HasOto(cc1, syllable.vowelTone)) {
                        basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                    }
                    // [C2C3]
                    if (HasOto(AliasFormat($"{string.Join("", cc.Skip(i + 1))}", "cc", syllable.tone, ""), syllable.vowelTone)) {
                        cc1 = AliasFormat($"{string.Join("", cc.Skip(i + 1))}", "cc", syllable.tone, "");
                    }
                    // CV
                } else if (syllable.CurrentWordCc.Length == 1 && syllable.PreviousWordCc.Length == 1) {
                    basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                    // [C1] [C2]
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = AliasFormat($"{cc[i + 1]}", "cc", syllable.tone, "");

                    }
                }
                if (i + 1 < lastC) {
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    // [C1][C2]
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = AliasFormat($"{cc[i + 1]}", "cc", syllable.tone, "");
                    }
                    if (!HasOto(cc1, syllable.tone)) {
                        cc1 = ValidateAlias(cc1);
                    }
                    // CCV
                    if (syllable.CurrentWordCc.Length >= 2) {
                        if ((HasOto($"{v}", syllable.vowelTone) || HasOto($"- {v}", syllable.vowelTone) || HasOto($"-{v}", syllable.vowelTone)) && HasOto(AliasFormat($"{string.Join("", cc.Skip(i + 1))}", "cc", syllable.tone, ""), syllable.vowelTone)) {
                            basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                            lastC = i;
                        } else if ((HasOto(AliasFormat(v, "cv", syllable.tone, ""), syllable.vowelTone)) && HasOto(cc1, syllable.vowelTone)) {
                            basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                        }
                        // [C2C3]
                        if (HasOto(AliasFormat($"{string.Join("", cc.Skip(i + 1))}", "cc", syllable.tone, ""), syllable.vowelTone)) {
                            cc1 = AliasFormat($"{string.Join("", cc.Skip(i + 1))}", "cc", syllable.tone, "");
                        }
                        // CV
                    } else if (syllable.CurrentWordCc.Length == 1 && syllable.PreviousWordCc.Length == 1) {
                        basePhoneme = AliasFormat(v, "cv", syllable.vowelTone, "");
                        // [C1] [C2]
                        if (!HasOto(cc1, syllable.tone)) {
                            cc1 = AliasFormat($"{cc[i + 1]}", "cc", syllable.tone, "");

                        }
                    }
                    if (TryAddPhoneme(phonemes, syllable.tone, $"{cc[i]}{cc[i + 1]}{cc[i + 2]} -")) {
                        // if it exists, use [C1][C2][C3] -
                        i += 2;
                    } else if (HasOto(cc1, syllable.tone) && HasOto(cc1, syllable.tone)) {
                        // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                        phonemes.Add(cc1);
                    } else if (TryAddPhoneme(phonemes, syllable.tone, cc1)) {
                        // like [V C1] [C1 C2] [C2 ..]
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
            if (tails.Contains(ending.prevV)) {
                return new List<string>();
            }
            if (ending.IsEndingV) {
                var vR = $"{v} -";
                var vR1 = $"{v} R";
                var vR2 = $"{v}-";
                var endV = AliasFormat(v, "ending", ending.tone, "");
                if (HasOto(vR, ending.tone) || HasOto(ValidateAlias(vR), ending.tone) || (HasOto(vR1, ending.tone) || HasOto(ValidateAlias(vR1), ending.tone) || (HasOto(vR2, ending.tone) || HasOto(ValidateAlias(vR2), ending.tone)))) {
                    TryAddPhoneme(phonemes, ending.tone, AliasFormat(v, "ending", ending.tone, ""));
                    /// split diphthong vowels
                } else if (DiphthongExceptions.ContainsKey(prevV) && !(HasOto(vR, ending.tone) && HasOto(ValidateAlias(vR), ending.tone) && (HasOto(vR2, ending.tone) || HasOto(ValidateAlias(vR2), ending.tone)))) {
                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{DiphthongExceptions[prevV]}", "cv", ending.tone, ""));
                }
            } else if (ending.IsEndingVCWithOneConsonant) {
                var vc = $"{v} {cc[0]}";
                var vcr = $"{v} {cc[0]}-";
                var vcr2 = $"{v}{cc[0]} -";
                var vr = $"_{v}";
                var vr1 = $"{v}-";
                if (!RomajiException.Contains(cc[0])) {
                    if (HasOto(vcr, ending.tone) || HasOto(ValidateAlias(vcr), ending.tone)) {
                        TryAddPhoneme(phonemes, ending.tone, vcr);
                    } else if (!HasOto(vcr, ending.tone) && !HasOto(ValidateAlias(vcr), ending.tone) && (HasOto(vcr2, ending.tone) || HasOto(ValidateAlias(vcr2), ending.tone))) {
                        TryAddPhoneme(phonemes, ending.tone, vcr2);
                        // double the consonants if has [C -]/[C-]
                    } else if (DiphthongExceptions.ContainsKey(prevV) && (c_cR.Contains(cc.Last())) && ((HasOto(AliasFormat(v, "ending_mix", ending.tone, ""), ending.tone) && (HasOto($"{c_cR[0]} -", ending.tone) || (HasOto($"{c_cR[0]}-", ending.tone)))))) {
                        // ex: [ow][ow-][z][z -]
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{DiphthongExceptions[prevV]}", "diph_mix", ending.tone, ""));
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc1_mix", ending.tone, ""));
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                    } else if (DiphthongExceptions.ContainsKey(prevV) && ((HasOto(AliasFormat(v, "ending_mix", ending.tone, ""), ending.tone)) && !HasOto(vc, ending.tone))) {
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{DiphthongExceptions[prevV]}", "diph_mix", ending.tone, ""));
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                        /// use consonants for diphthongs if the vb doesn't have vowel endings
                    } else if (DiphthongExceptions.ContainsKey(prevV) && (!(HasOto(AliasFormat(v, "ending_mix", ending.tone, ""), ending.tone) && !HasOto(vc, ending.tone)))) {
                        TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{DiphthongExceptions[prevV]}", "diph_mix", ending.tone, ""));
                        if (c_cR.Contains(cc.Last())) {
                            if (HasOto(AliasFormat($"{c_cR[0]}", "cc_mix", ending.tone, ""), ending.tone)) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc1_mix", ending.tone, ""));
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                            } else if (!(HasOto(AliasFormat($"{c_cR[0]}", "cc_mix", ending.tone, ""), ending.tone))) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc1_mix", ending.tone, ""));
                            } else {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", $"{cc[0]}-");
                            }
                        } else if (!c_cR.Contains(cc.Last())) {
                            if (HasOto(AliasFormat($"{c_cR[0]}", "cc_mix", ending.tone, ""), ending.tone)) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                            } else if (!(HasOto(AliasFormat($"{c_cR[0]}", "cc_mix", ending.tone, ""), ending.tone))) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc1_mix", ending.tone, ""));
                            } else {
                                TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", $"{cc[0]}-");
                            }
                        }
                        /// add additional c to those consonants on the top
                    } else if (c_cR.Contains(cc.Last())) {
                        if (HasOto(vc, ending.tone) || HasOto(ValidateAlias(vc), ending.tone)) {
                            TryAddPhoneme(phonemes, ending.tone, vc);
                            //TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc1_mix", ending.tone, ""));
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                        } else if (HasOto($"{c_cR[0]} -", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]} -"), ending.tone) || (HasOto($"{c_cR[0]}-", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]}-"), ending.tone))) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc1_mix", ending.tone, ""));
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                        } else if (!(HasOto($"{c_cR[0]} -", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]} -"), ending.tone) || (HasOto($"{c_cR[0]}-", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]}-"), ending.tone)))) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                        } else {
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", $"{cc[0]}-");
                        }
                    } else {
                        TryAddPhoneme(phonemes, ending.tone, vc);
                        if (vc.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                        } else {
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", $"{cc[0]}-");
                        }
                    }
                }
            } else {
                for (var i = lastC; i >= 0; i--) {
                    var vr = $"_{v}";
                    var vr1 = $"{v}-";
                    var vcc = $"{v} {string.Join("", cc.Take(2))}-";
                    var vcc2 = $"{v}{string.Join(" ", cc.Take(2))} -";
                    var vcc3 = $"{v}{string.Join(" ", cc.Take(2))}";
                    var vcc4 = $"{v} {string.Join("", cc.Take(2))}";
                    var vc = $"{v} {cc[0]}";
                    if (!RomajiException.Contains(cc[0])) {
                        if (i == 0) {
                            if (HasOto(vr, ending.tone) || HasOto(ValidateAlias(vr), ending.tone) && !HasOto(vc, ending.tone)) {
                                TryAddPhoneme(phonemes, ending.tone, vr);
                            }
                            break;
                        } else if ((HasOto(vcc, ending.tone) || HasOto(ValidateAlias(vcc), ending.tone)) && lastC == 1 && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc);
                            firstC = 1;
                            break;
                        } else if ((HasOto(vcc2, ending.tone) || HasOto(ValidateAlias(vcc2), ending.tone)) && lastC == 1 && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc2);
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc3, ending.tone) || HasOto(ValidateAlias(vcc3), ending.tone) && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc3);
                            if (vcc3.EndsWith(cc.Last()) && lastC == 1) {
                                if (consonants.Contains(cc.Last())) {
                                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                                }
                            }
                            firstC = 1;
                            break;
                        } else if (HasOto(vcc4, ending.tone) || HasOto(ValidateAlias(vcc4), ending.tone) && !ccvException.Contains(cc[0])) {
                            TryAddPhoneme(phonemes, ending.tone, vcc4);
                            if (vcc4.EndsWith(cc.Last()) && lastC == 1) {
                                if (consonants.Contains(cc.Last())) {
                                    TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[0]}", "cc_mix", ending.tone, ""));
                                }
                            }
                            firstC = 1;
                            break;
                        } else if (DiphthongExceptions.ContainsKey(prevV) && (HasOto(vr, ending.tone) || HasOto(ValidateAlias(vr), ending.tone)) || (HasOto(vr1, ending.tone) || HasOto(ValidateAlias(vr1), ending.tone)) && !HasOto(vc, ending.tone)) {
                            TryAddPhoneme(phonemes, ending.tone, vr1, vr);
                            break;
                            /// use consonants for diphthongs if the vb doesn't have vowel endings
                        } else if (DiphthongExceptions.ContainsKey(prevV) && (!(HasOto(vr, ending.tone) || HasOto(ValidateAlias(vr), ending.tone) || (HasOto(vr1, ending.tone) || HasOto(ValidateAlias(vr1), ending.tone)) && !HasOto(vc, ending.tone)))) {
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{DiphthongExceptions[prevV]}", "diph_mix", ending.tone, ""));
                            break;
                        } else {
                            TryAddPhoneme(phonemes, ending.tone, vc);
                            break;
                        }
                    }
                }
                for (var i = firstC; i < lastC; i++) {
                    var cc1 = $"{cc[i]}";
                    if (i < cc.Length - 2) {
                        var cc2 = $"{cc[i + 1]}";
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        // CC FALLBACKS
                        if (!HasOto(cc1, ending.tone) || !HasOto(ValidateAlias(cc1), ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            // [C1] [C2]
                            cc1 = $"{cc[i + 1]}";
                        } else if (!HasOto(cc1, ending.tone) || !HasOto(ValidateAlias(cc1), ending.tone) && !HasOto($"{cc[i + 1]}", ending.tone)) {
                            // [- C1] [- C2]
                            cc1 = $"- {cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (HasOto(cc1, ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}-", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}-"), ending.tone))) {
                            // like [C1 C2][C2 ...]
                            phonemes.Add(cc1);
                        } else if ((HasOto(cc[i], ending.tone) || HasOto(ValidateAlias(cc[i]), ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}-", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}-"), ending.tone)))) {
                            // like [C1 C2-][C3 ...]
                            phonemes.Add(cc[i]);
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {cc[i + 2]}-", ValidateAlias($"{cc[i + 1]} {cc[i + 2]}-"))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            i++;
                        } else {
                            // like [C1][C2 ...]
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i]}", "cc1_mix", ending.tone, ""));
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc1_mix", ending.tone, ""));
                            i++;
                        }
                    } else {
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        // CC FALLBACKS
                        if (!HasOto(cc1, ending.tone) || !HasOto(ValidateAlias(cc1), ending.tone)) {
                            // [C1] [C2]
                            cc1 = AliasFormat($"{cc[i + 1]}", "cc_end", ending.tone, "");
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        // CC FALLBACKS
                        if (!HasOto(cc1, ending.tone) || !HasOto(ValidateAlias(cc1), ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            // [C1] [C2]
                            cc1 = AliasFormat($"{cc[i + 1]}", "cc1_mix", ending.tone, ""); ;
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]}-", ValidateAlias($"{cc[i]} {cc[i + 1]}-"))) {
                            // like [C1 C2-]
                            i++;
                        } else if (c_cR.Contains(cc.Last())) {
                            if (HasOto($"{c_cR[0]} -", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]} -"), ending.tone) || (HasOto($"{c_cR[0]}-", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]}-"), ending.tone))) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i]}", "cc1_mix", ending.tone, ""));
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc1_mix", ending.tone, ""));
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_mix", ending.tone, ""));
                                i++;
                            } else if (!(HasOto($"{c_cR[0]} -", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]} -"), ending.tone) || (HasOto($"{c_cR[0]}-", ending.tone) || HasOto(ValidateAlias($"{c_cR[0]}-"), ending.tone)))) {
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i]}", "cc1_mix", ending.tone, ""));
                                TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_mix", ending.tone, ""));
                                i++;
                            }
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            // like [C1 C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, AliasFormat($"{cc[i + 1]}", "cc_mix", ending.tone, ""));
                            i++;

                        } else if (!HasOto(cc1, ending.tone) && !HasOto($"{cc[i]} {cc[i + 1]}", ending.tone)) {
                            // [C1 -] [- C2]
                            TryAddPhoneme(phonemes, ending.tone, $"- {cc[i + 1]}", ValidateAlias($"- {cc[i + 1]}"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            phonemes.Add($"{cc[i]} -");
                            i++;
                        }

                    }
                }
            }
            return phonemes;
        }
        private string AliasFormat(string alias, string type, int tone, string prevV) {
            var aliasFormats = new Dictionary<string, string[]> {
                // Define alias formats for different types
                { "startingV", new string[] { "-", "- ", "_", "" } },
                { "vv", new string[] { "-", "", "_", "- " } },
                { "vvExtend", new string[] { "", "_", "-", "- " } },
                { "cv", new string[] { "-", "", "- ", "_" } },
                { "ending", new string[] { " -", "-", " R" } },
                { "ending_mix", new string[] { "-", " -", "R", " R", "_", "--" } },
                { "cc", new string[] { "", "-", "- ", "_" } },
                { "cc_start", new string[] { "- ", "-", "", "_" } },
                { "cc_end", new string[] { " -", "-", "" } },
                { "cc_mix", new string[] { " -", " R", "-", "", "_", "- ", "-" } },
                { "cc1_mix", new string[] { "", " -", "-", " R", "_", "- ", "-" } },
                { "diph_mix", new string[] { "-", "_", "", " -", "", "_", "- ", "-" } },

            };

            // Check if the given type exists in the aliasFormats dictionary
            if (!aliasFormats.ContainsKey(type)) {
                return alias;
            }
            // Get the array of possible alias formats for the specified type
            var formatsToTry = aliasFormats[type];
            int counter = 0;
            foreach (var format in formatsToTry) {
                string aliasFormat;
                if (type.Contains("mix") && counter < 4) {
                    // Alternate between alias + format and format + alias for the first 4 iterations
                    aliasFormat = (counter % 2 == 0) ? alias + format : format + alias;
                    counter++;
                } else if (type.Contains("end")) {
                    aliasFormat = alias + format;
                } else {
                    aliasFormat = format + alias;
                }
                // Check if the formatted alias exists using HasOto and ValidateAlias
                if (HasOto(aliasFormat, tone) || HasOto(ValidateAlias(aliasFormat), tone)) {
                    alias = aliasFormat;
                    return alias;
                }
            }
            return alias;
        }

        protected override string ValidateAlias(string alias) {

            // VALIDATE ALIAS DEPENDING ON METHOD
            if (isTimitPhonemes) {
                foreach (var fb in timitphonemes.OrderByDescending(f => f.Key.Length)) {
                    alias =  alias.Replace(fb.Key, fb.Value);
                }
            }
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
            return alias;

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

            

            // consonant timings

            var sortedOverrides = PhonemeOverrides.OrderByDescending(kv => kv.Key.Length);
            foreach (var kvp in sortedOverrides) {
                var overridePhoneme = kvp.Key;
                var overrideValue = kvp.Value;
                if (PhonemeIsPresent(alias, overridePhoneme)) {
                    return base.GetTransitionBasicLengthMs() * overrideValue;
                }
            }

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
