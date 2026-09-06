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
    // IDN100% Indonesian CVVC Phonemizer
    [Phonemizer("Indonesian CVVC phonemizer", "ID CVVC", "Cadlaxa", language: "ID")]
    public class IDNCVVCPhonemizer : SyllableBasedPhonemizer {
        protected override string YamlFileName => "idn-cvvc.yaml";
        protected override string YamlVersion => "1.0";
        protected override byte[] YamlTemplate => Data.Resources.idcvvc_template;
        public IDNCVVCPhonemizer() {
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
        private string[] doubleConsonants = "bl,br,dr,fl,fr,gl,gr,kw,ny,pl,pr,tr,vl".Split(",");

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
            Func<string[], string[]> patterns;
            int tone = syllable.tone;

            if (syllable.IsStartingV) {
                patterns = (e) => new[] { $"- {e[0]}", $"-{e[0]}", $"{e[0]}" };
                basePhoneme = Try(tone, patterns, v) ?? v;

            } else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    basePhoneme = $"{prevV} {v}";
                    if (!HasOto(basePhoneme, syllable.vowelTone) && !HasOto(ValidateAlias(basePhoneme), syllable.vowelTone) && vvExceptions.ContainsKey(prevV) && prevV != v) {
                        var vc = $"{prevV} {vvExceptions[prevV]}";
                        phonemes.Add(vc);
                        basePhoneme = $"{vvExceptions[prevV]}{v}";
                    } else {
                        {
                            if (HasOto($"{prevV} {v}", syllable.vowelTone) || HasOto(ValidateAlias($"{prevV} {v}"), syllable.vowelTone)) {
                                basePhoneme = $"{prevV} {v}";
                            } else if (HasOto($"{prevV}{v}", syllable.vowelTone) || HasOto(ValidateAlias($"{prevV}{v}"), syllable.vowelTone)) {
                                basePhoneme = $"{prevV}{v}";
                            } else if (HasOto(v, syllable.vowelTone) || HasOto(ValidateAlias(v), syllable.vowelTone)) {
                                basePhoneme = v;
                            }
                        }
                    }
                    // EXTEND AS [V]
                } else if (HasOto($"{v}", syllable.vowelTone) && HasOto(ValidateAlias($"{v}"), syllable.vowelTone)) {
                    basePhoneme = v;
                } else {
                    // PREVIOUS ALIAS WILL EXTEND as [V V]
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                patterns = (e) => new[] { $"- {e[0]}{e[1]}", $"-{e[0]}{e[1]}", $"{e[0]}{e[1]}" };
                basePhoneme = Try(tone, patterns, cc[0], v) ?? $"{cc[0]}{v}";

            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                patterns = (e) => new[] { $"- {e[0]}{e[1]}{e[2]}", $"{e[0]}{e[1]}{e[2]}" };
                string? result = Try(tone, patterns, cc[0], cc.Last(), v);
                if (result != null) {
                    basePhoneme = result;
                } else {
                    patterns = (e) => new[] { $"- {e[0]}", $"-{e[0]}", $"{e[0]}" };
                    phonemes.Add(Try(tone, patterns, cc[0]) ?? cc[0]);
                    patterns = (e) => new[] { $"{e[0]}{e[1]}" };
                    basePhoneme = Try(tone, patterns, cc.Last(), v) ?? $"{cc.Last()}{v}";
                }

            } else if (syllable.IsVCVWithOneConsonant) {
                if (doubleConsonants.Contains(cc[0])) {
                    string oldCC = cc[0];
                    string newCC = oldCC[0].ToString();
                    patterns = (e) => new[] { $"{e[0]} {e[1]}" };
                    phonemes.Add(Try(tone, patterns, prevV, newCC) ?? $"{prevV} {newCC}");
                } else {
                    patterns = (e) => new[] { $"{e[0]} {e[1]}" };
                    phonemes.Add(Try(tone, patterns, prevV, cc[0]) ?? $"{prevV} {cc[0]}");
                }
                patterns = (e) => new[] { $"{e[0]}{e[1]}" };
                basePhoneme = Try(tone, patterns, cc[0], v) ?? $"{cc[0]}{v}";

            } else {
                patterns = (e) => new[] { $"{e[0]}{e[1]}", $"{e[0]} {e[1]}" };
                phonemes.Add(Try(tone, patterns, prevV, cc[0]) ?? $"{prevV} {cc[0]}");
                patterns = (e) => new[] { $"{e[0]}{e[1]}" };
                basePhoneme = Try(tone, patterns, cc.Last(), v) ?? $"{cc.Last()}{v}";
            }
            // for y/w cc fallback
            for (var i = firstC; i < lastC; i++) {
                var cc1 = $"{string.Join(" ", cc.Skip(i))}";
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
                    if (HasOto(cc1, syllable.tone) && HasOto(cc1, syllable.tone) && !cc1.Contains($"{string.Join("", cc.Skip(i))}")) {
                        // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                        phonemes.Add(cc1);
                    } else if (TryAddPhoneme(phonemes, syllable.tone, cc1, ValidateAlias(cc1))) {
                        // like [V C1] [C1 C2] [C2 ..]
                        if (cc1.Contains($"{string.Join(" ", cc.Skip(i + 1))}")) {
                            i++;
                        }
                    } else {
                        // singular cc
                        if (PreviousWordCc.Contains(cc1) == CurrentWordCc.Contains(cc1)) {
                            cc1 = ValidateAlias(cc1);
                        } else {
                            TryAddPhoneme(phonemes, syllable.tone, cc1, cc[i], ValidateAlias(cc[i]));
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
            Func<string[], string[]> patterns;
            int tone = ending.tone;
            string t = ending.HasTail ? ReplacePhoneme(ending.tail, ending.tone) : "-";

            var vr = $"{prevV} {t}";

            if (ending.IsEndingV) {
                patterns = (e) => new[] { $"{e[0]} {t}", $"{e[0]} R" };
                string? result = Try(tone, patterns, v);
                if (result != null) phonemes.Add(result);

            } else {
                if (ending.IsEndingVCWithOneConsonant) {
                    patterns = (e) => new[] { $"{e[0]}{e[1]} {t}", $"{e[0]}{e[1]} R", $"{e[0]}{e[1]}", $"{e[0]} {e[1]}" };
                    phonemes.Add(Try(tone, patterns, v, cc[0]) ?? $"{v}{cc[0]}");

                } else {
                    patterns = (e) => new[] { $"{e[0]}{e[1]} {t}", $"{e[0]}{e[1]} R", $"{e[0]}{e[1]}", $"{e[0]} {e[1]}" };
                    phonemes.Add(Try(tone, patterns, v, cc[0]) ?? $"{v}{cc[0]} {t}");
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

                        if (HasOto(cc1, ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}{t}", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}{t}"), ending.tone))) {
                            // like [C1 C2][C2 ...]
                            phonemes.Add(cc1);
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
                            cc1 = $"{cc[i]} {cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            // like [C1 C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {t}", ValidateAlias($"{cc[i + 1]} {t}"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        }
                    }
                }
            }
            return phonemes;
        }

        string? CheckOto(int tone, params string[] entries) {
            foreach (string entry in entries) {
                if (HasOto(entry, tone)) return entry;
            }
            return null;
        }

        string? Try(int tone, Func<string[], string[]> patterns, params string[] entries) {
            foreach (string schwa in "ax,ex,@,eu".Split(",")) {
                var newEntries = entries.Select((entry) => entry == "ax" ? schwa : entry).ToArray();
                string[] rawAliases = patterns(newEntries);
                string[] validatedAliases = rawAliases
                    .Select(alias => ValidateAlias(alias))
                    .ToArray();

                string? result = CheckOto(tone, validatedAliases);
                if (result != null) return result;
            }
            return null;
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
