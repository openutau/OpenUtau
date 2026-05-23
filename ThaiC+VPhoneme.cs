// ==========================================
// Made And Checked By DELTA SYNTH & Gemini AI
// Original Authors: Lucky (Registered by DELTA SYNTH)
// Version: 2.3 (Strict C+V Engine & Alias Alignment)
// ==========================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Thai C+V Phonemizer v2.3", "TH C+V Delta", "DELTA SYNTH", language: "TH")]
    public class ThaiCVPhonemizer : Phonemizer {

        private Dictionary<string, string> CustomDictionary = new Dictionary<string, string>();
        private bool isDictLoaded = false;
        private USinger singer;

        readonly string[] vowels = new string[] {
            "a", "i", "u", "e", "o", "@", "Q", "3", "6", "1", "ia", "ua", "I", "8"
        };

        readonly string[] diphthongs = new string[] {
            "r", "l", "w"
        };

        readonly string[] consonants = new string[] {
            "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y"
        };

        readonly string[] endingConsonants = new string[] {
            "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y"
        };

        private readonly Dictionary<string, string> VowelMapping = new Dictionary<string, string> {
            {"เcือะ", "6"}, {"เcือx", "6"}, {"แcะ", "@"}, {"แcx", "@"}, {"เcอะ", "3"}, {"เcอ", "3"}, {"ไc", "a"}, {"ใc", "a"}, {"เcาะ", "Q"}, {"cอx", "Q"},
            {"cืx", "1"}, {"cึx", "1"}, {"cือ", "1"}, {"cะ", "a"}, {"cัx", "a"}, {"cาx", "a"}, {"cรรx", "a"}, {"เcา", "a"}, {"เcะ", "e"}, {"เcx", "e"}, {"cิx", "i"}, {"cีx", "i"},
            {"เcียะ", "ia"}, {"เcียx", "ia"}, {"โcะ", "o"}, {"โcx", "o"}, {"cุx", "u"}, {"cูx", "u"}, {"cัวะ", "ua"}, {"cัว", "ua"}, {"cำ", "a"}, {"เcิx", "3"}, {"เcิ", "3"}
        };

        private readonly Dictionary<char, string> CMapping = new Dictionary<char, string> {
            {'ก', "k"}, {'ข', "kh"}, {'ค', "kh"}, {'ฆ', "kh"}, {'ฅ', "kh"}, {'ฃ', "kh"},
            {'จ', "j"}, {'ฉ', "ch"}, {'ช', "ch"}, {'ฌ', "ch"},
            {'ฎ', "d"}, {'ด', "d"}, {'ต', "t"}, {'ฏ', "t"},
            {'ถ', "th"}, {'ฐ', "th"}, {'ฑ', "th"}, {'ฒ', "th"}, {'ธ', "th"}, {'ท', "th"},
            {'บ', "b"}, {'ป', "p"}, {'พ', "ph"}, {'ผ', "ph"}, {'ภ', "ph"}, {'ฟ', "f"}, {'ฝ', "f"},
            {'ห', "h"}, {'ฮ', "h"}, {'ม', "m"}, {'น', "n"}, {'ณ', "n"}, {'ร', "r"}, {'ล', "l"}, {'ฤ', "r"},
            {'ส', "s"}, {'ศ', "s"}, {'ษ', "s"}, {'ซ', "s"},
            {'ง', "g"}, {'ย', "y"}, {'ญ', "y"}, {'ว', "w"}, {'ฬ', "r"}
        };

        private readonly Dictionary<char, string> XMapping = new Dictionary<char, string> {
            {'บ', "b"}, {'ป', "b"}, {'พ', "b"}, {'ฟ', "b"}, {'ภ', "b"},
            {'ด', "d"}, {'จ', "d"}, {'ช', "d"}, {'ซ', "d"}, {'ฎ', "d"}, {'ฏ', "d"}, {'ฐ', "d"},
            {'ฑ', "d"}, {'ฒ', "d"}, {'ต', "d"}, {'ถ', "d"}, {'ท', "d"}, {'ธ', "d"}, {'ศ', "d"}, {'ษ', "d"}, {'ส', "d"},
            {'ก', "k"}, {'ข', "k"}, {'ค', "k"}, {'ฆ', "k"},
            {'ว', "w"}, {'ย', "y"}, {'ง', "g"}, {'ม', "m"},
            {'น', "n"}, {'ญ', "n"}, {'ณ', "n"}, {'ร', "n"}, {'ล', "n"}, {'ฬ', "n"}
        };

        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    oto = otoCandidacy;
                    return true;
                }
            }
            return false;
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentLyric = note.lyric.Normalize();
            if (!string.IsNullOrEmpty(note.phoneticHint)) currentLyric = note.phoneticHint.Normalize();

            var phonemes = new List<Phoneme>();
            List<string> aliases = new List<string>();

            bool isStart = prevNeighbour == null || prevNeighbour.Value.lyric == "-" || prevNeighbour.Value.lyric.ToLower() == "r";
            bool forceClose = false;

            if (currentLyric.EndsWith("-") && currentLyric.Length > 1) {
                forceClose = true;
                currentLyric = currentLyric.Substring(0, currentLyric.Length - 1);
            }
            bool isEnd = nextNeighbour == null || forceClose || nextNeighbour.Value.lyric == "-" || nextNeighbour.Value.lyric.ToLower() == "r";

            var (C, Dip, V, X) = ParseInput(currentLyric);
            bool hasCluster = !string.IsNullOrEmpty(Dip);
            bool hasEnding = !string.IsNullOrEmpty(X);

            if (currentLyric == "-") {
                aliases.Add("-");
            } else {
                if (!string.IsNullOrEmpty(C)) {
                    string startC = isStart ? $"-{C}" : C;
                    if (!checkOtoUntilHit(new string[] { startC }, note, out var _)) startC = C; // Fallback
                    aliases.Add(startC);
                    if (hasCluster) aliases.Add(Dip);
                    if (!string.IsNullOrEmpty(V)) aliases.Add(V);
                } else if (!string.IsNullOrEmpty(V)) {
                    string startV = isStart ? $"-{V}" : V;
                    if (!checkOtoUntilHit(new string[] { startV }, note, out var _)) startV = V;
                    aliases.Add(startV);
                }

                if (hasEnding) {
                    string endX = isEnd ? $"{X}-" : X;
                    if (!checkOtoUntilHit(new string[] { endX }, note, out var _)) endX = X;
                    aliases.Add(endX);
                } else if (isEnd && !string.IsNullOrEmpty(V) && aliases.Count > 0) {
                    string endV = $"{V}-";
                    if (checkOtoUntilHit(new string[] { endV }, note, out var _)) aliases[aliases.Count - 1] = endV;
                }
            }

            if (aliases.Count == 0) aliases.Add(currentLyric == "" ? "a" : currentLyric);

            int noteDuration = notes.Sum(n => n.duration);
            for (int i = 0; i < aliases.Count; i++) {
                string alias = aliases[i];
                int position = 0;

                bool isEndingAlias = alias.EndsWith("-") || alias == "-";
                bool isVCAlias = alias == X;
                bool isCVAlias = alias == V;
                bool isCCVAlias = hasCluster && alias == Dip;
                bool isStartAlias = alias.StartsWith("-");

                if (isStartAlias || (!isEndingAlias && !isVCAlias && !isCVAlias && !isCCVAlias)) {
                    position = 0;
                } else if (isEndingAlias) {
                    position = Math.Max((int)(noteDuration * 0.80), noteDuration - 20);
                } else if (isVCAlias) {
                    position = (int)(noteDuration * 0.80);
                } else if (isCVAlias) {
                    position = hasCluster ? (int)(noteDuration * 0.20) : (int)(noteDuration * 0.10);
                } else if (isCCVAlias) {
                    position = (int)(noteDuration * 0.10);
                } else {
                    position = 0;
                }

                position = Math.Max(0, Math.Min(position, Math.Max(0, noteDuration - 10)));
                phonemes.Add(new Phoneme { phoneme = alias, position = position });
            }

            return new Result { phonemes = phonemes.ToArray() };
        }

        (string Consonant, string Dipthong, string Vowel, string EndingConsonant) ParseInput(string input) {
            input = WordToPhonemes(input);
            string consonant = null, diphthong = null, vowel = null, endingConsonant = null;
            if (string.IsNullOrEmpty(input)) return (null, null, null, null);

            foreach (var con in consonants) {
                if (input.StartsWith(con) && (consonant == null || consonant.Length < con.Length)) consonant = con;
            }

            int startIdx = consonant?.Length ?? 0;
            foreach (var dip in diphthongs) {
                if (input.Substring(startIdx).StartsWith(dip) && (diphthong == null || diphthong.Length < dip.Length)) diphthong = dip;
            }

            startIdx += diphthong?.Length ?? 0;
            foreach (var vow in vowels) {
                if (input.Substring(startIdx).StartsWith(vow) && (vowel == null || vowel.Length < vow.Length)) vowel = vow;
            }

            foreach (var con in endingConsonants) {
                if (input.EndsWith(con) && (endingConsonant == null || endingConsonant.Length < con.Length)) endingConsonant = con;
            }

            return (consonant, diphthong, vowel, endingConsonant);
        }

        public string WordToPhonemes(string input) {
            input = input.Replace(" ", "");
            if (ThaiDictionaryLoader.Dictionary.ContainsKey(input)) return ThaiDictionaryLoader.Dictionary[input];

            input = input.Replace("ก็", "ก้อ").Replace("บ่", "บ่อ").Replace("ฤทธิ์", "ริด").Replace("อังกฤษ", "อังกิด");
            input = input.Replace("\u0E40\u0E40", "\u0E41"); 
            input = Regex.Replace(input, "[ก-ฮ][ิุ]?์", ""); 
            input = Regex.Replace(input, "[ก-ฮ]์", "");
            input = Regex.Replace(input, "[่้๊๋็]", "");
            
            if (!Regex.IsMatch(input, "[ก-ฮ]")) return input;

            foreach (var mapping in VowelMapping) {
                string pattern = "^" + mapping.Key.Replace("c", "([ก-ฮ][ลรว]?|อ[ย]?|ห[ก-ฮ]?)").Replace("x", "([ก-ฮ]*)") + "$";
                var match = Regex.Match(input, pattern);
                if (match.Success) {
                    string c = match.Groups[1].Value;
                    string x = match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty;

                    if (mapping.Key == "ไc" || mapping.Key == "ใc") x = "ย";
                    if (mapping.Key == "เcา") x = "ว";
                    if (mapping.Key == "cำ") x = "ม";

                    if (mapping.Key == "cรรx" && x == "") x = "น"; 
                    else if (mapping.Key.EndsWith("x") && x == "") {
                        string[] trueClusters = { "กร", "กล", "กว", "ขร", "ขล", "ขว", "คร", "คล", "คว", "ปร", "ปล", "พร", "พล", "ตร", "ผล", "บร", "บล", "ฟร", "ฟล", "ดร", "ทร" };
                        if (!trueClusters.Contains(c) && c.Length > 1) {
                            x = c.Substring(c.Length - 1); 
                            c = c.Substring(0, c.Length - 1); 
                        }
                    }

                    if (x.Length > 1) x = x.Substring(0, 1);
                    if (c.Length >= 2 && (c.StartsWith("ห") || c.StartsWith("อ"))) c = c.Substring(1);
                    
                    string cConverted = ConvertC(c);
                    string xConverted = ConvertX(x);
                    
                    if (mapping.Value == "a" && input.Contains("ั") && x == "ว") return cConverted + "ua";
                    if (mapping.Value == "e" && x == "ย") return cConverted + "3" + xConverted;
                    return cConverted + mapping.Value + xConverted;
                }
            }
            
            if (!Regex.IsMatch(input, "[ะาิีึืุูเแโไใโั]")) {
                if (input.Contains("ว") && input.Length >= 3) {
                    int wIdx = input.IndexOf('ว');
                    string cStr = input.Substring(0, wIdx);
                    string xStr = input.Substring(wIdx + 1);
                    if (xStr.Length > 1) xStr = xStr.Substring(0, 1);
                    return ConvertC(cStr) + "ua" + ConvertX(xStr);
                } else if (input.Length >= 2) {
                    string cStr = input[0].ToString();
                    string xStr = input.Substring(1);

                    string[] clusters = { "กร", "กล", "กว", "ขร", "ขล", "ขว", "คร", "คล", "คว", "ปร", "ปล", "พร", "พล", "ตร", "ผล", "บร", "บล", "ฟร", "ฟล", "ดร", "ทร", "หง", "หญ", "หน", "หม", "หย", "หร", "หล", "หว", "อย" };
                    if (input.Length >= 3 && clusters.Contains(input.Substring(0, 2))) {
                        cStr = input.Substring(0, 2);
                        xStr = input.Substring(2);
                    }
                    if (xStr.Length > 1) xStr = xStr.Substring(0, 1);
                    return ConvertC(cStr) + "o" + ConvertX(xStr);
                }
            }
            return input;
        }

        private string ConvertC(string input) {
            if (string.IsNullOrEmpty(input) || input == "อ") return "";
            if (input == "ทร" || input == "สร" || input == "ศร" || input == "ซร") return "s";
            if (input == "จร") return "j";
            if (input.Length >= 2 && (input.StartsWith("ห") || input.StartsWith("อ"))) input = input.Substring(1);
            char firstChar = input[0];
            char? secondChar = input.Length > 1 ? input[1] : (char?)null;
            if (CMapping.ContainsKey(firstChar)) {
                string firstCharConverted = CMapping[firstChar];
                if (secondChar != null && CMapping.ContainsKey((char)secondChar)) return firstCharConverted + CMapping[(char)secondChar];
                return firstCharConverted;
            }
            return input;
        }

        private string ConvertX(string input) {
            if (string.IsNullOrEmpty(input)) return input;
            char firstChar = input[0];
            return XMapping.ContainsKey(firstChar) ? XMapping[firstChar] : input;
        }
    }
}