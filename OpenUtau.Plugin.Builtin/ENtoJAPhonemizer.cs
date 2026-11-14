using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("English to Japanese Phonemizer", "EN to JA", "TUBS, Cadlaxa", language: "EN")]
    public partial class ENtoJAPhonemizer : SyllableBasedPhonemizer {
        protected override string YamlFileName => "en2ja.yaml";
        protected override byte[] YamlTemplate => Data.Resources.en2ja_template;
        protected override string YamlVersion => "1.2.1";

        public bool useLegacy = false;

        public Dictionary<string, bool> TransitionalClusterSettings = new Dictionary<string, bool>();
        public Dictionary<string, List<string>> WanaKanaDictionary = new Dictionary<string, List<string>>();

        public ENtoJAPhonemizer() {
            this.vowels = ModernVowels;
            this.consonants = ModernConsonants;
        }

        protected override string[] GetVowels() => this.vowels ?? (useLegacy ? LegacyVowels : ModernVowels);
        protected override string[] GetConsonants() => this.consonants ?? (useLegacy ? LegacyConsonants : ModernConsonants);
        protected override string GetDictionaryName() => useLegacy ? "cmudict-0_7b.txt" : "";
        protected override bool EnablePhonemeTokenization => !useLegacy;

        protected override IG2p LoadBaseDictionary() {
            if (useLegacy) return new ArpabetG2p();
            return base.LoadBaseDictionary();
        }

        protected override IG2p[] GetBaseG2ps() {
            if (useLegacy) return Array.Empty<IG2p>();
            return new IG2p[] { new ArpabetPlusG2p() };
        }

        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() {
            if (useLegacy) return DictionaryPhonemesReplacementLegacy;
            return base.GetDictionaryPhonemesReplacement();
        }

        public override void SetSinger(USinger singer) {
            useLegacy = false;
            WanaKanaDictionary.Clear();
            TransitionalClusterSettings.Clear();

            if (singer != null && singer.Loaded) {
                string globalFile = Path.Combine(PluginDir, YamlFileName);
                string singerFile = Path.Combine(singer.Location, YamlFileName);

                var filesToParse = new List<string>();
                if (File.Exists(globalFile)) filesToParse.Add(globalFile);
                if (File.Exists(singerFile) && globalFile != singerFile) filesToParse.Add(singerFile);

                foreach (var file in filesToParse) {
                    try {
                        var data = Core.Yaml.DefaultDeserializer.Deserialize<ChildYAMLData>(File.ReadAllText(file));
                        if (data?.uselegacy.HasValue == true) useLegacy = data.uselegacy.Value;
                        if (data?.use_legacy.HasValue == true) useLegacy = data.use_legacy.Value;
                    } catch { }
                }
            }

            base.SetSinger(singer);

            if (this.singer != null && this.singer.Loaded) {
                string globalFile = Path.Combine(PluginDir, YamlFileName);
                string singerFile = Path.Combine(this.singer.Location, YamlFileName);

                var filesToParse = new List<string>();
                if (File.Exists(globalFile)) filesToParse.Add(globalFile);
                if (File.Exists(singerFile) && globalFile != singerFile) filesToParse.Add(singerFile);

                foreach (var file in filesToParse) {
                    try {
                        var data = Core.Yaml.DefaultDeserializer.Deserialize<ChildYAMLData>(File.ReadAllText(file));

                        if (!useLegacy) {
                            if (data?.wanakana != null) {
                                foreach (var entry in data.wanakana.Reverse()) {
                                    string key = string.Join("", entry.FromList);
                                    string value = string.Join(" ", entry.ToList);

                                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;

                                    if (!WanaKanaDictionary.ContainsKey(key)) {
                                        WanaKanaDictionary.Add(key, new List<string>());
                                    }
                                    if (!WanaKanaDictionary[key].Contains(value)) {
                                        WanaKanaDictionary[key].Insert(0, value);
                                    }
                                    if (!WanaKanaDictionary[key].Contains(key)) {
                                        WanaKanaDictionary[key].Add(key);
                                    }
                                }
                            }
                            if (data?.transitionalclusters != null) {
                                foreach (var tc in data.transitionalclusters) {
                                    if (!string.IsNullOrEmpty(tc.symbol)) {
                                        TransitionalClusterSettings[tc.symbol] = tc.value;
                                    }
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Log.Error($"Failed to parse yaml data from {file}: {ex.Message}");
                    }
                }

                if (useLegacy) {
                    yamlFallbacks.Clear();
                    mergingReplacements.Clear();
                    splittingReplacements.Clear();
                    this.vowels = LegacyVowels;
                    this.consonants = LegacyConsonants;
                } else {
                    this.vowels = ModernVowels.Union(this.vowels ?? Array.Empty<string>()).Distinct().ToArray();
                    this.consonants = ModernConsonants.Union(this.consonants ?? Array.Empty<string>()).Distinct().ToArray();
                }
            }
        }

        protected override string[] GetSymbols(Note note) => useLegacy ? GetLegacySymbols(note) : GetModernSymbols(note);
        protected override List<string> ProcessSyllable(Syllable syllable) => useLegacy ? ProcessLegacySyllable(syllable) : ProcessModernSyllable(syllable);
        protected override List<string> ProcessEnding(Ending ending) => useLegacy ? ProcessLegacyEnding(ending) : ProcessModernEnding(ending);
        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) => useLegacy ? GetLegacyTransitionLength(alias, tone, attr) : GetModernTransitionLength(alias, tone, attr);
        protected override string ValidateAlias(string alias, int tone = 0) => useLegacy ? ValidateLegacyAlias(alias, tone) : ValidateModernAlias(alias, tone);

        public bool IsTransitionalCEnabled(string symbol) {
            if (TransitionalClusterSettings.TryGetValue(symbol, out bool isEnabled)) return isEnabled;
            return true;
        }

        #region YAML Data Classes

        public class ChildYAMLData : YAMLData {
            public bool? uselegacy { get; set; }
            public bool? use_legacy { get; set; }
            public WanaKanaData[] wanakana { get; set; } = Array.Empty<WanaKanaData>();
            public TransitionalClusterData[] transitionalclusters { get; set; } = Array.Empty<TransitionalClusterData>();
        }

        public class WanaKanaData {
            public object roma { get; set; }
            public object kana { get; set; }

            public List<string> FromList {
                get {
                    if (roma is string s) return new List<string> { s };
                    if (roma is IEnumerable<object> list) return list.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                    if (roma != null) return new List<string> { roma.ToString() };
                    return new List<string>();
                }
            }

            public List<string> ToList {
                get {
                    if (kana is string s) return new List<string> { s };
                    if (kana is IEnumerable<object> list) return list.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
                    if (kana != null) return new List<string> { kana.ToString() };
                    return new List<string>();
                }
            }
        }

        public class TransitionalClusterData {
            public string symbol { get; set; }
            public bool value { get; set; } = true;
        }

        #endregion
    }
}