#pragma warning disable CS0618, CS0649, CS8632, CS0108
#nullable enable
#pragma warning disable CS8632
// ==========================================
// Made And Checked By DELTA SYNTH & Gemini AI
// Original by Patiphat Wongyai
// Version: v.4.6
// History/Summary: Implemented Safety Position Buffer (prevPos + 10) to prevent phoneme overlapping. Multi-syllable + note support.
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
    [Phonemizer("Thai Arpasing Phonemizer", "TH Arpasing Delta", "DELTA SYNTH", language: "TH")]
    public class ThaiArpasingPhonemizer : Phonemizer {

        static readonly string[] vowels = new string[] { "ah", "ih", "uh", "eh", "oh", "ae", "ao", "er", "ue", "eu", "ia", "ua", "ay", "aw", "am" };
        static readonly string[] diphthongs = new string[] { "r", "l", "w" };
        static readonly string[] consonants = new string[] { "b", "ch", "d", "f", "h", "j", "k", "kh", "l", "m", "n", "ng", "p", "ph", "r", "s", "t", "th", "w", "y" };
        static readonly string[] endingConsonants = new string[] { "b", "ch", "d", "f", "h", "j", "k", "kh", "l", "m", "n", "ng", "p", "ph", "r", "s", "t", "th", "w", "y" };

        private static readonly List<(string Key, string Value)> VowelMapping = new List<(string Key, string Value)> {
            ("เcือะ", "ue"), ("เcือx", "ue"), ("แcะ", "ae"), ("แcx", "ae"), ("เcอะ", "er"), ("เcอx", "er"), ("เcอ", "er"),
            ("เcย", "er"), ("ไc", "ay"), ("ใc", "ay"), ("ไcx", "ay"), ("ใcx", "ay"),
            ("เcาะ", "ao"), ("cอx", "ao"), ("cืx", "eu"), ("cึx", "eu"), ("cือ", "eu"),
            ("cะ", "ah"), ("cัx", "ah"), ("cาx", "ah"), ("cรรx", "ah"), 
            ("เcา", "aw"), ("เcาx", "aw"), 
            ("เcะ", "eh"), ("เcx", "eh"),
            ("cิx", "ih"), ("cีx", "ih"), ("เcียะ", "ia"), ("เcียx", "ia"), ("โcะ", "oh"), ("โcx", "oh"),
            ("cุx", "uh"), ("cูx", "uh"), ("cัวะ", "ua"), ("cัว", "ua"), ("cวx", "ua"),
            ("cำ", "am"), ("cำx", "am"), 
            ("เcิx", "er"), ("เcิ", "er")
        };

        private static readonly Dictionary<char, string> CMapping = new Dictionary<char, string> {
            {'ก', "k"}, {'ข', "kh"}, {'ค', "kh"}, {'ฆ', "kh"}, {'ฅ', "kh"}, {'ฃ', "kh"},
            {'จ', "j"}, {'ฉ', "ch"}, {'ช', "ch"}, {'ฌ', "ch"},
            {'ฎ', "d"}, {'ด', "d"}, {'ต', "t"}, {'ฏ', "t"},
            {'ถ', "th"}, {'ฐ', "th"}, {'ฑ', "th"}, {'ฒ', "th"}, {'ธ', "th"}, {'ท', "th"},
            {'บ', "b"}, {'ป', "p"}, {'พ', "ph"}, {'ผ', "ph"}, {'ภ', "ph"}, {'ฟ', "f"}, {'ฝ', "f"},
            {'ห', "h"}, {'ฮ', "h"}, {'ม', "m"}, {'น', "n"}, {'ณ', "n"}, {'ร', "r"}, {'ล', "l"}, {'ฤ', "r"},
            {'ส', "s"}, {'ศ', "s"}, {'ษ', "s"}, {'ซ', "s"},
            {'ง', "ng"}, {'ย', "y"}, {'ญ', "y"}, {'ว', "w"}, {'ฬ', "r"}
        };

        private static readonly Dictionary<char, string> XMapping = new Dictionary<char, string> {
            {'บ', "b"}, {'ป', "b"}, {'พ', "b"}, {'ฟ', "b"}, {'ภ', "b"},
            {'ด', "d"}, {'จ', "d"}, {'ช', "d"}, {'ซ', "d"}, {'ฎ', "d"}, {'ฏ', "d"}, {'ฐ', "d"},
            {'ฑ', "d"}, {'ฒ', "d"}, {'ต', "d"}, {'ถ', "d"}, {'ท', "d"}, {'ธ', "d"}, {'ศ', "d"}, {'ษ', "d"}, {'ส', "d"},
            {'ก', "k"}, {'ข', "k"}, {'ค', "k"}, {'ฆ', "k"},
            {'ว', "w"}, {'ย', "y"}, {'ง', "ng"}, {'ม', "m"},
            {'น', "n"}, {'ญ', "n"}, {'ณ', "n"}, {'ร', "n"}, {'ล', "n"}, {'ฬ', "n"}
        };

        private static readonly HashSet<string> TrueClusters = new HashSet<string> {
            "กร", "กล", "กว", "ขร", "ขล", "ขว", "คร", "คล", "คว", "ปร", "ปล", "พร", "พล", "ตร", "ผล", "บร", "บล", "ฟร", "ฟล", "ดร", "ทร", "หง", "หญ", "หน", "หม", "หย", "หร", "หล", "หว", "อย", "สร", "ศร", "ซร", "จร"
        };

        private static readonly Regex ToneRegex = new Regex("[่้๊๋็]", RegexOptions.Compiled);
        private static readonly Regex KaranRegex1 = new Regex("[ก-ฮ][ิุ]?์", RegexOptions.Compiled);
        private static readonly Regex KaranRegex2 = new Regex("[ก-ฮ]์", RegexOptions.Compiled);
        private static readonly Regex ThaiCharRegex = new Regex("[ก-ฮ]", RegexOptions.Compiled);
        private static readonly Regex ValidVowelRegex = new Regex("[ะาิีึืุูเแโไใโัำ]", RegexOptions.Compiled);
        private static readonly List<(string Key, Regex Pattern, string Replacement)> CompiledVowelMappings = new List<(string, Regex, string)>();

        static ThaiArpasingPhonemizer() {
            foreach (var mapping in VowelMapping) {
                string pattern = "^" + mapping.Key.Replace("c", "([ก-ฮ][ลรว]?|อ[ย]?|ห[ก-ฮ]?)").Replace("x", "([ก-ฮ]{0,2})");
                CompiledVowelMappings.Add((mapping.Key, new Regex(pattern, RegexOptions.Compiled), mapping.Value));
            }
        }

        private Dictionary<string, string> CustomDictionary = new Dictionary<string, string> {
            {"บวร", "baw wawn"},
            {"ศร", "sawn"},
            {"โสน", "sah noh"},
            {"เบื้อง", "beuang"},
            {"คือ", "kheu"},
            {"เรือ", "reuah"},
            {"บ่", "bao"},
            {"ก็", "kao"},
            {"เอื้อ", "euah"},
            {"ตึก", "tuek"},
            {"ปู่", "puh"},
            {"ครุธ", "khrut"},
            {"ครุฑ", "khrut"},
            {"สถิตย์", "sah thit"},
            {"ธ", "thah"},
            {"ณ", "nah"},
            {"ฤาษี", "rue sih"},
            {"ฤทธิ์", "rit"},
            {"ฤกดิ์", "ruek"},
            {"ศักดิ์", "sak"},
            {"เพียง", "ph ia ng"}, {"หลง", "l oh ng"}, {"เหี่ยว", "h ia w"}, {"เพรียว", "ph r ia w"}, // สระลดรูปและคำควบกล้ำเพิ่มเติม (v17.3)
            {"เกิน", "k er n"}, {"เคย", "kh er y"}, {"เธอ", "th er"}, {"เพ้อ", "ph er"}, {"เจอ", "j er"}, {"เดิน", "d er n"}, // สระลดรูปเพิ่มเติม (v17.3)
            {"ฤดู", "r eu d uh"}, {"อยู่", "y uh"}, {"หมด", "m oh d"}, {"คง", "kh oh ng"}, {"หม่น", "m oh n"} // สระลดรูปเพิ่มเติม (v17.3)
        };
        private bool isDictLoaded = false;
        private USinger? singer;

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            LoadCustomDictionary();
        }

        private void LoadCustomDictionary() {
            if (isDictLoaded) return;
            try {
                string[] dictPaths = {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries", "dsdict-th.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "words_th.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "words_th_dict.txt")
                };
                foreach (var path in dictPaths) {
                    LoadDictFile(path);
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to load custom dictionary.");
            }
            isDictLoaded = true;
        }

        private void LoadDictFile(string path) {
            if (File.Exists(path)) {
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (var line in lines) {
                    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(new[] { '=', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) {
                        CustomDictionary[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
        }

        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    oto = otoCandidacy;
                    return true;
                }
            }
            return false;
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var firstNote = notes[0];
            var currentLyric = string.IsNullOrEmpty(firstNote.phoneticHint) ? firstNote.lyric.Normalize() : firstNote.phoneticHint.Normalize();
            var syllables = WordToPhonemes(currentLyric);
            if (syllables.Length == 0) syllables = new[] { currentLyric };

            var phonemes = new List<Phoneme>();
            
            bool isStart = prevNeighbour == null
                || string.IsNullOrWhiteSpace(prevNeighbour.Value.lyric)
                || prevNeighbour.Value.lyric == "-"
                || prevNeighbour.Value.lyric.ToLower() == "r";
                
            string prevPhoneme = "-";
            if (!isStart && prevNeighbour != null) {
                var prevTh = ParseInput(prevNeighbour.Value.lyric);
                prevPhoneme = prevTh.EndingConsonant ?? prevTh.Vowel ?? "-";
            }

            int totalNotesDuration = notes.Sum(n => n.duration);
            int currentNoteIndex = 0;
            int currentTickPosition = 0;
            int lastNoteIndex = -1;

            for (int sIdx = 0; sIdx < syllables.Length; sIdx++) {
                string syllable = syllables[sIdx];
                List<string> aliases = new List<string>();
                
                Note currentNote = notes[currentNoteIndex];
                int noteStartPos = currentNote.position - firstNote.position;
                int noteEndPos = noteStartPos + currentNote.duration;

                if (sIdx > 0 && currentNoteIndex < notes.Length - 1) {
                    currentNoteIndex++;
                    currentNote = notes[currentNoteIndex];
                    noteStartPos = currentNote.position - firstNote.position;
                    noteEndPos = noteStartPos + currentNote.duration;
                }
                
                int noteDuration = currentNote.duration;
                int remainingSyllablesOnNote = 1;
                for (int nextSIdx = sIdx + 1; nextSIdx < syllables.Length; nextSIdx++) {
                    if (nextSIdx > 0 && currentNoteIndex < notes.Length - 1) break; 
                    remainingSyllablesOnNote++;
                }
                
                int sylDuration = noteDuration / remainingSyllablesOnNote;
                if (sIdx > 0 && currentNoteIndex == lastNoteIndex) {
                    noteStartPos = currentTickPosition;
                    noteEndPos = noteStartPos + sylDuration;
                    noteDuration = sylDuration;
                } else if (remainingSyllablesOnNote > 1) {
                    noteEndPos = noteStartPos + sylDuration;
                    noteDuration = sylDuration;
                }
                lastNoteIndex = currentNoteIndex;

                bool forceClose = false;
                bool isEnd = false;
                if (sIdx == syllables.Length - 1) {
                    isEnd = nextNeighbour == null || nextNeighbour.Value.lyric == "-" || nextNeighbour.Value.lyric.ToLower() == "r";
                    if (syllable.EndsWith("-") && syllable.Length > 1) {
                        forceClose = true;
                        isEnd = true;
                        syllable = syllable.Substring(0, syllable.Length - 1);
                    }
                }

                string? C = null, Dip = null, V = null, X = null;
                if (syllable == "-") {
                    aliases.Add($"{prevPhoneme} -");
                } else {
                    (C, Dip, V, X) = ParseInput(syllable);

                    string lead = C ?? V ?? "";
                    if (!string.IsNullOrEmpty(lead)) {
                        if (sIdx == 0 && isStart) aliases.Add($"- {lead}");
                        else aliases.Add($"{prevPhoneme} {lead}");
                    }

                    if (!string.IsNullOrEmpty(C)) {
                        if (!string.IsNullOrEmpty(Dip)) {
                            aliases.Add($"{C} {Dip}");
                            aliases.Add($"{Dip} {V}");
                        } else if (!string.IsNullOrEmpty(V)) {
                            aliases.Add($"{C} {V}");
                        }
                    }

                    if (!string.IsNullOrEmpty(X) && !string.IsNullOrEmpty(V)) {
                        aliases.Add($"{V} {X}");
                    }

                    if (isEnd || forceClose) {
                        string tail = X ?? V ?? "";
                        if (!string.IsNullOrEmpty(tail)) aliases.Add($"{tail} -");
                    }
                    
                    prevPhoneme = X ?? V ?? "-";
                }

                if (aliases.Count == 0) aliases.Add(string.IsNullOrEmpty(syllable) ? "ah" : syllable);

                for (int i = 0; i < aliases.Count; i++) {
                    string alias = aliases[i];
                    if (!checkOtoUntilHit(new[] { alias }, currentNote, out var oto)) {
                        var parts = alias.Split(' ');
                        if (parts.Length == 2 && parts[0] != "-") alias = parts[1] == "-" ? parts[0] : parts[1];
                    }

                    int position = 0;
                    if (i == 0) {
                        // ควบคุมรอยต่อ [C C] และการเริ่มเสียง ให้มีช่วง pre-utterance
                        position = -Math.Min((int)(noteDuration * 0.12), 100);
                    } else if (alias.EndsWith("-")) {
                        // ปรับการผลักเสียงช่วงพัก (-)
                        position = Math.Max((int)(noteDuration * 0.90), noteDuration - 15); 
                    } else if (X != null && alias == $"{V} {X}") {
                        position = Math.Max((int)(noteDuration * 0.70), noteDuration - 120); 
                    } else if (Dip != null && i == 1) {
                        position = Math.Min((int)(noteDuration * 0.05), 30);
                    } else if (Dip != null && i == 2) {
                        position = Math.Min((int)(noteDuration * 0.10), 60);
                    } else if (i == aliases.Count - 1 && aliases.Count >= 3) {
                        position = Math.Max((int)(noteDuration * 0.80), noteDuration - 60);
                    } else if (i == 1) {
                        position = Math.Min((int)(noteDuration * 0.12), 80);
                    } else {
                        position = Math.Min((int)(noteDuration * 0.20), 110);
                    }

                    int absolutePosition = position + noteStartPos;
                    int lastPos = phonemes.Count > 0 ? phonemes.Last().position : -120;
                    if (absolutePosition <= lastPos) {
                        absolutePosition = lastPos + 10;
                    }
                    // ยอมให้ตำแหน่งติดลบได้เพื่อ pre-utterance ของ [C C] (v17.3)
                    absolutePosition = Math.Min(absolutePosition, noteEndPos - 10);
                    phonemes.Add(new Phoneme { phoneme = alias, position = absolutePosition });
                    currentTickPosition = absolutePosition;
                }
            }

            int gap = prevNeighbour == null ? 9999 : firstNote.position - (prevNeighbour.Value.position + prevNeighbour.Value.duration);
            if (gap >= 120) {
                bool hasOpening = phonemes.Count > 0 && (phonemes[0].phoneme.StartsWith("- ") || phonemes[0].phoneme.StartsWith("-"));
                if (!hasOpening) {
                    var attr = firstNote.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
                    if (singer.TryGetMappedOto("Breath", firstNote.tone + attr.toneShift, attr.voiceColor, out var breathOto)) {
                        int insertPos = -60;
                        if (phonemes.Count > 0 && insertPos >= phonemes[0].position) {
                            insertPos = phonemes[0].position - 10;
                        }
                        phonemes.Insert(0, new Phoneme { phoneme = breathOto.Alias, position = insertPos });
                    }
                }
            }

            return new Result { phonemes = phonemes.ToArray() };
        }

        (string? Consonant, string? Dipthong, string? Vowel, string? EndingConsonant) ParseInput(string input) {
            if (string.IsNullOrEmpty(input)) return (null, null, null, null);
            input = input.Replace(" ", ""); // Remove spaces to correctly parse phonemes
            string? consonant = null, diphthong = null, vowel = null, endingConsonant = null;

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

            int vowelEndIdx = startIdx + (vowel?.Length ?? 0);
            if (vowelEndIdx < input.Length) {
                string remainder = input.Substring(vowelEndIdx);
                foreach (var con in endingConsonants) {
                    if (remainder.EndsWith(con) && (endingConsonant == null || endingConsonant.Length < con.Length)) endingConsonant = con;
                }
            }

            return (consonant, diphthong, vowel, endingConsonant);
        }

        public string[] WordToPhonemes(string input) {
            if (string.IsNullOrEmpty(input)) return new string[0];
            
            if (CustomDictionary.ContainsKey(input)) {
                return new[] { CustomDictionary[input] };
            }

            input = input.Replace(" ", "");
            if (string.IsNullOrEmpty(input)) return new string[0];

            input = input.Replace("ก็", "ก้อ").Replace("บ่", "บ่อ").Replace("ฤทธิ์", "ริด").Replace("อังกฤษ", "อังกิด").Replace("\u0E40\u0E40", "\u0E41");
            input = KaranRegex1.Replace(input, "");
            input = KaranRegex2.Replace(input, "");
            input = ToneRegex.Replace(input, "");

            if (!ThaiCharRegex.IsMatch(input)) return new[] { input };

            List<string> syllables = new List<string>();
            int safetyCounter = 0;
            while (input.Length > 0 && safetyCounter < 10) {
                safetyCounter++;
                bool matched = false;

                foreach (var mapping in CompiledVowelMappings) {
                    var match = mapping.Pattern.Match(input);
                    if (match.Success) {
                        string c = match.Groups[1].Value;
                        string x = match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty;

                        if (mapping.Key == "cรรx" && string.IsNullOrEmpty(x)) x = "น";
                        else if (mapping.Key.EndsWith("x") && string.IsNullOrEmpty(x)) {
                            if (!TrueClusters.Contains(c) && c.Length > 1) {
                                x = c.Substring(c.Length - 1);
                                c = c.Substring(0, c.Length - 1);
                            }
                        }

                        if (x.Length > 1) x = x.Substring(0, 1);
                        if (c.Length >= 2 && (c.StartsWith("ห") || c.StartsWith("อ"))) c = c.Substring(1);

                        string cConverted = ConvertC(c);
                        string xConverted = ConvertX(x);

                        string phoneme;
                        if (mapping.Replacement == "ah" && input.Contains("ั") && x == "ว") phoneme = cConverted + "ua";
                        else if (mapping.Key == "เcย") phoneme = cConverted + "er" + "y";
                        else if (mapping.Replacement == "eh" && x == "ย") phoneme = cConverted + "er" + xConverted;
                        else phoneme = cConverted + mapping.Replacement + xConverted;
                        
                        syllables.Add(phoneme);
                        input = input.Substring(match.Length);
                        matched = true;
                        break;
                    }
                }

                if (!matched) {
                    if (!ValidVowelRegex.IsMatch(input)) {
                        if (input.Contains("ว") && input.Length >= 3) {
                            int wIdx = input.IndexOf('ว');
                            string xStr = input.Substring(wIdx + 1);
                            if (xStr.Length > 1) xStr = xStr.Substring(0, 1);
                            syllables.Add(ConvertC(input.Substring(0, wIdx)) + "ua" + ConvertX(xStr));
                            input = "";
                        } else if (input.Length >= 2) {
                            string cStr = input[0].ToString();
                            string xStr = input.Substring(1);
                            if (input.Length >= 3 && TrueClusters.Contains(input.Substring(0, 2))) {
                                cStr = input.Substring(0, 2);
                                xStr = input.Substring(2);
                            }
                            if (xStr.Length > 1) xStr = xStr.Substring(0, 1);
                            syllables.Add(ConvertC(cStr) + "oh" + ConvertX(xStr));
                            input = "";
                        } else {
                            syllables.Add(input);
                            input = "";
                        }
                    } else {
                        syllables.Add(input);
                        input = "";
                    }
                }
            }

            return syllables.ToArray();
        }

        private string ConvertC(string input) {
            if (string.IsNullOrEmpty(input)) return "";
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
            if (string.IsNullOrEmpty(input)) return "";
            char firstChar = input[0];
            return XMapping.ContainsKey(firstChar) ? XMapping[firstChar] : input;
        }
    }
}
