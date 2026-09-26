using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Melanchall.DryWetMidi.Interaction;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Thai VCCV Phonemizer", "TH VCCV", "PRINTmov", language: "TH")]
    public class ThaiVCCVPhonemizer : Phonemizer {

        readonly string[] vowels = new string[] {
            "a", "i", "u", "e", "o", "@", "Q", "3", "6", "1", "ia", "ua", "I", "aw", "am"
        };

        readonly string[] diphthongs = new string[] {
            "r", "l", "w"
        };

        readonly string[] consonants = new string[] {
            "b", "ch", "d", "f", "h", "j", "k", "kh", "l", "m", "n", "g", "p", "ph", "r", "s", "t", "th", "w", "y", "-"
        };

        readonly string[] endingConsonants = new string[] {
            "n", "m", "y", "w", "g", "k", "b", "d"
        };

        private readonly Dictionary<string, string> VowelMapping = new Dictionary<string, string> {
            {"เcือะ", "6"}, {"เcือx", "6"}, {"แcะ", "@"}, {"แcx", "@"}, {"เcอะ", "3"}, {"เcอ", "3"}, {"ไc", "I"}, {"ใc", "I"}, {"เcาะ", "Q"}, {"cอx", "Q"},
            {"cืx", "1"}, {"cึx", "1"}, {"cือ", "1"}, {"cะ", "a"}, {"cัx", "a"}, {"cาx", "a"}, {"เcา", "aw"}, {"เcะ", "e"}, {"เcx", "e"}, {"cิx", "i"}, {"cีx", "i"},
            {"เcียะ", "ia"}, {"เcียx", "ia"}, {"โcะ", "o"}, {"โcx", "o"}, {"cุx", "u"}, {"cูx", "u"}, {"cัวะ", "ua"}, {"cัว", "ua"}, {"cำ", "am"}, {"เcิx", "3"}, {"เcิ", "3"}
        };

        private readonly Dictionary<char, string> CMapping = new Dictionary<char, string> {
            {'ก', "k"}, {'ข', "kh"}, {'ค', "kh"}, {'ฆ', "kh"}, {'ฅ', "kh"}, {'ฃ', "kh"},
            {'จ', "j"}, {'ฉ', "ch"}, {'ช', "ch"}, {'ฌ', "ch"},
            {'ฎ', "d"}, {'ด', "d"},
            {'ต', "t"}, {'ฏ', "t"},
            {'ถ', "th"}, {'ฐ', "th"}, {'ฑ', "th"}, {'ธ', "th"}, {'ท', "th"},
            {'บ', "b"}, {'ป', "p"}, {'พ', "ph"}, {'ผ', "ph"}, {'ภ', "ph"}, {'ฟ', "f"}, {'ฝ', "f"},
            {'ห', "h"}, {'ฮ', "h"},
            {'ม', "m"}, {'น', "n"}, {'ณ', "n"}, {'ร', "r"}, {'ล', "l"}, {'ฤ', "r"},
            {'ส', "s"}, {'ศ', "s"}, {'ษ', "s"}, {'ซ', "s"},
            {'ง', "g"}, {'ย', "y"}, {'ญ', "y"}, {'ว', "w"}, {'ฬ', "r"},
            {'อ', "-"} 
        };

        private readonly Dictionary<char, string> XMapping = new Dictionary<char, string> {
            {'บ', "b"}, {'ป', "b"}, {'พ', "b"}, {'ฟ', "b"}, {'ภ', "b"},
            {'ด', "d"}, {'จ', "d"}, {'ช', "d"}, {'ซ', "d"}, {'ฎ', "d"}, {'ฏ', "d"}, {'ฐ', "d"},
            {'ฑ', "d"}, {'ฒ', "d"}, {'ต', "d"}, {'ถ', "d"}, {'ท', "d"}, {'ธ', "d"}, {'ศ', "d"}, {'ษ', "d"}, {'ส', "d"},
            {'ก', "k"}, {'ข', "k"}, {'ค', "k"}, {'ฆ', "k"},
            {'ว', "w"},
            {'ย', "y"},
            {'น', "n"}, {'ญ', "n"}, {'ณ', "n"}, {'ร', "n"}, {'ล', "n"}, {'ฬ', "n"},
            {'ง', "g"},
            {'ม', "m"}
        };

        // [DELTA SYNTH] Compiled Regexes for massive performance optimization (Zero-Allocation inside loops).
        private static readonly Regex InvalidCharRegex1 = new Regex(".์", RegexOptions.Compiled);
        private static readonly Regex InvalidCharRegex2 = new Regex("[่้๊๋็]", RegexOptions.Compiled);
        private readonly List<(Regex pattern, string value)> _compiledVowelMappings = new List<(Regex, string)>();

        private USinger singer;

        public ThaiVCCVPhonemizer() {
            // Pre-compile all regex patterns once during initialization to drastically reduce CPU overhead.
            foreach (var mapping in VowelMapping) {
                string pattern = "^" + mapping.Key
                    .Replace("c", "([ก-ฮ][ลรว]?|อ[ย]?|ห[ก-ฮ]?)")
                    .Replace("x", "([ก-ฮ]?)") + "$";
                _compiledVowelMappings.Add((new Regex(pattern, RegexOptions.Compiled), mapping.Value));
            }
        }

        public override void SetSinger(USinger singer) => this.singer = singer;

        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;

            foreach (string test in input) {
                if (singer.TryGetMappedOto(test, note.tone + (attr.toneShift ?? GetParentToneShift()), attr.voiceColor ?? GetParentVoiceColor(), out var otoCandidacy)) {
                    oto = otoCandidacy;
                    return true;
                }
            }
            return false;
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentLyric = note.lyric.Normalize();
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                currentLyric = note.phoneticHint.Normalize();
            }

            var phonemes = new List<Phoneme>();
            List<string> tests = new List<string>();

            string prevTemp = "";
            if (prevNeighbour != null) {
                prevTemp = prevNeighbour.Value.lyric;
            }
            var prevTh = ParseInput(prevTemp);
            var noteTh = ParseInput(currentLyric);

            if (noteTh.Consonant != null && noteTh.Dipthong == null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new string[] { noteTh.Consonant + noteTh.Vowel }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            } else if (noteTh.Consonant != null && noteTh.Dipthong != null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new string[] { noteTh.Consonant + noteTh.Dipthong + noteTh.Vowel }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                } else {
                    if (checkOtoUntilHit(new string[] { noteTh.Consonant + noteTh.Dipthong }, note, out tempOto)) {
                        tests.Add(tempOto.Alias);
                    }
                    if (checkOtoUntilHit(new string[] { noteTh.Dipthong + noteTh.Vowel }, note, out tempOto)) {
                        tests.Add(tempOto.Alias);
                    }
                }
            }

            if (noteTh.Consonant == null && noteTh.Vowel != null) {
                if (prevTh.EndingConsonant != null && checkOtoUntilHit(new string[] { prevTh.EndingConsonant + noteTh.Vowel }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                } else if (prevTh.Vowel != null && checkOtoUntilHit(new string[] { prevTh.Vowel + noteTh.Vowel }, note, out tempOto)) {
                    tests.Add(tempOto.Alias);
                } else if (checkOtoUntilHit(new string[] { noteTh.Vowel }, note, out tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            }

            if (noteTh.EndingConsonant != null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new string[] { noteTh.Vowel + noteTh.EndingConsonant }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            } else if (nextNeighbour != null && noteTh.Vowel != null) {
                var nextTh = ParseInput(nextNeighbour.Value.lyric);
                if (checkOtoUntilHit(new string[] { noteTh.Vowel + " " + nextTh.Consonant }, note, out var tempOto)) {
                    tests.Add(tempOto.Alias);
                }
            }

            if (prevNeighbour == null && tests.Count >= 1) {
                if (checkOtoUntilHit(new string[] { "-" + tests[0] }, note, out var tempOto)) {
                    tests[0] = tempOto.Alias;
                }
            }

            if (nextNeighbour == null && tests.Count >= 1) {
                if (noteTh.EndingConsonant == null) {
                    if (checkOtoUntilHit(new string[] { noteTh.Vowel + "-" }, note, out var tempOto)) {
                        tests.Add(tempOto.Alias);
                    }
                } else {
                    if (checkOtoUntilHit(new string[] { tests[tests.Count - 1] + "-" }, note, out var tempOto)) {
                        tests[tests.Count - 1] = tempOto.Alias;
                    }
                }
            }

            if (tests.Count <= 0) {
                if (checkOtoUntilHit(new string[] { currentLyric }, note, out var tempOto)) {
                    tests.Add(currentLyric);
                }
            }

            // [DELTA SYNTH] Used tests.ToArray() only once to eliminate redundant memory allocations.
            var testsArray = tests.ToArray();
            if (checkOtoUntilHit(testsArray, note, out var oto)) {
                var noteDuration = notes.Sum(n => n.duration);

                for (int i = 0; i < testsArray.Length; i++) {
                    int position = 0;
                    int vcPosition = noteDuration - 120;

                    if (nextNeighbour != null && testsArray[i].Contains(" ")) {
                        var nextLyric = nextNeighbour.Value.lyric.Normalize();
                        if (!string.IsNullOrEmpty(nextNeighbour.Value.phoneticHint)) {
                            nextLyric = nextNeighbour.Value.phoneticHint.Normalize();
                        }
                        var nextTh = ParseInput(nextLyric);
                        var nextCheck = nextTh.Vowel;
                        if (nextTh.Consonant != null) {
                            nextCheck = nextTh.Consonant + nextTh.Vowel;
                        }
                        if (nextTh.Dipthong != null) {
                            nextCheck = nextTh.Consonant + nextTh.Dipthong + nextTh.Vowel;
                        }
                        var nextAttr = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                        if (singer.TryGetMappedOto(nextCheck, nextNeighbour.Value.tone + (nextAttr.toneShift ?? GetParentToneShift()), nextAttr.voiceColor ?? GetParentVoiceColor(), out var nextOto)) {
                            if (oto.Overlap > 0) {
                                vcPosition = noteDuration - MsToTick(nextOto.Overlap) - MsToTick(nextOto.Preutter);
                            }
                        }
                    }

                    // [DELTA SYNTH] Consonant Expansion & Automated Prep Feature
                    if (noteTh.Dipthong == null || testsArray.Length <= 2) {
                        if (i == 1) {
                            position = Math.Max((int)(noteDuration * 0.70), vcPosition);
                        }
                    } else {
                        if (i == 1) {
                            position = Math.Min((int)(noteDuration * 0.15), 90);
                        } else if (i == 2) {
                            position = Math.Max((int)(noteDuration * 0.70), vcPosition);
                        }
                    }

                    phonemes.Add(new Phoneme { phoneme = testsArray[i], position = position });
                }
            }

            return new Result {
                phonemes = phonemes.ToArray()
            };
        }

        (string Consonant, string Dipthong, string Vowel, string EndingConsonant) ParseInput(string input) {
            input = WordToPhonemes(input);

            string consonant = null;
            string diphthong = null;
            string vowel = null;
            string endingConsonant = null;

            if (input == null) {
                return (null, null, null, null);
            }

            foreach (var con in consonants) {
                if (input.StartsWith(con)) {
                    if (consonant == null || consonant.Length < con.Length) {
                        consonant = con;
                    }
                }
            }

            int startIdx = consonant?.Length ?? 0;
            foreach (var dip in diphthongs) {
                if (startIdx < input.Length && input.Substring(startIdx).StartsWith(dip)) {
                    if (diphthong == null || diphthong.Length < dip.Length) {
                        diphthong = dip;
                    }
                }
            }

            startIdx += diphthong?.Length ?? 0;
            foreach (var vow in vowels) {
                if (startIdx < input.Length && input.Substring(startIdx).StartsWith(vow)) {
                    if (vowel == null || vowel.Length < vow.Length) {
                        vowel = vow;
                    }
                }
            }

            foreach (var con in endingConsonants) {
                if (input.EndsWith(con)) {
                    if (endingConsonant == null || endingConsonant.Length < con.Length) {
                        endingConsonant = con;
                    }
                }
            }

            return (consonant, diphthong, vowel, endingConsonant);
        }

        public string WordToPhonemes(string input) {
            input = input.Replace(" ", "");
            input = RemoveInvalidLetters(input);
            if (!Regex.IsMatch(input, "[ก-ฮ]")) {
                return input;
            }
            
            // [DELTA SYNTH] Optimized regex matching. Utilizes pre-compiled regex objects safely.
            foreach (var mapping in _compiledVowelMappings) {
                var match = mapping.pattern.Match(input);
                if (match.Success) {
                    string c = match.Groups[1].Value;
                    string x = match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty;
                    string cConverted = ConvertC(c);
                    string xConverted = ConvertX(x);
                    if (mapping.value == "a" && input.Contains("ั") && x == "ว") {
                        return cConverted + "ua";
                    }
                    if (mapping.value == "e" && x == "ย") {
                        return cConverted + "3" + xConverted;
                    }
                    return cConverted + mapping.value + xConverted;
                }
            }

            if (input.Length == 1) {
                return ConvertC(input) + "Q";
            } else if (input.Length == 2) {
                return ConvertC(input[0].ToString()) + "o" + ConvertX(input[1].ToString());
            } else if (input.Length == 3) {
                if (input[1] == 'ว') {
                    return ConvertC(input[0].ToString()) + "ua" + ConvertX(input[2].ToString());
                } else {
                    return ConvertC(input.Substring(0, 2)) + "o" + ConvertX(input[2].ToString());
                }
            } else if (input.Length >= 4) {
                if (input[2] == 'ว') {
                    return ConvertC(input.Substring(0, 2)) + "ua" + ConvertX(input[3].ToString());
                }
            }
            return input;
        }

        private string ConvertC(string input) {
            if (string.IsNullOrEmpty(input)) return "";
            
            // Silent Leading Consonant Handle (ห นำ, อ นำ)
            if (input.Length >= 2 && (input.StartsWith("ห") || input.StartsWith("อ"))) {
                input = input.Substring(1);
            }

            char firstChar = input[0];
            char? secondChar = input.Length > 1 ? input[1] : (char?)null;
            
            if (CMapping.ContainsKey(firstChar)) {
                string firstCharConverted = CMapping[firstChar];
                if (secondChar != null && CMapping.ContainsKey((char)secondChar)) {
                    return firstCharConverted + CMapping[(char)secondChar];
                }
                return firstCharConverted;
            }
            return input;
        }

        private string ConvertX(string input) {
            if (string.IsNullOrEmpty(input)) return input;
            char firstChar = input[0];
            if (XMapping.ContainsKey(firstChar)) {
                return XMapping[firstChar];
            }
            return input;
        }

        private string RemoveInvalidLetters(string input) {
            // [DELTA SYNTH] Uses static compiled regex to prevent recreating regex instances per character check.
            input = InvalidCharRegex1.Replace(input, "");
            input = InvalidCharRegex2.Replace(input, "");
            return input;
        }

    }
}
