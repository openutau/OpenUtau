#pragma warning disable CS0618, CS0649, CS8632, CS0108
#nullable enable
#pragma warning disable CS8632
// ==========================================
// Made And Checked By DELTA SYNTH & Gemini AI
// Original by Patiphat Wongyai
// Version: v.2.4
// History/Summary: Implemented Safety Position Buffer (prevPos + 10) to prevent phoneme overlapping ("ghostly voice" bug). Multi-syllable + note support.
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
    [Phonemizer("Thai VCV Phonemizer", "TH VCV Printmov", "DELTA SYNTH", language: "TH")]
    public class ThaiVCVPhonemizer : Phonemizer {

        static readonly string[] vowels = new string[] {
            "a", "i", "u", "e", "o", "@", "Q", "3", "6", "1", "ia", "ua", "ay", "aw", "am"
        };

        static readonly string[] diphthongs = new string[] {
            "r", "l", "w"
        };

        static readonly string[] consonants = new string[] {
            "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y", "-"
        };

        static readonly string[] endingConsonants = new string[] {
            "b", "d", "g", "k", "m", "n", "w", "y"
        };

        private static readonly List<(string Key, string Value)> VowelMapping = new List<(string Key, string Value)> {
            ("เcือะ", "6"), ("เcือx", "6"), ("แcะ", "@"), ("แcx", "@"), ("เcอะ", "3"), ("เcิร์x", "3"), ("เcอx", "3"), ("เcอ", "3"),
            ("เcย", "3"), ("ไc", "ay"), ("ใc", "ay"), ("เcาะ", "Q"), ("cอx", "Q"),
            ("cืx", "1"), ("cึx", "1"), ("cือ", "1"), ("cะ", "a"), ("cัx", "a"), ("cาx", "a"), ("cรรx", "a"),
            ("เcา", "aw"), ("เcะ", "e"), ("เcx", "e"), ("cิx", "i"), ("cีx", "i"),
            ("เcียะ", "ia"), ("เcียx", "ia"), ("โcะ", "o"), ("โcx", "o"),
            ("cุx", "u"), ("cูx", "u"), ("cัวะ", "ua"), ("cัว", "ua"), ("cำ", "am"), ("เcิx", "3"), ("เcิ", "3")
        };

        private static readonly Dictionary<char, string> CMapping = new Dictionary<char, string> {
            {'ก', "k"}, {'ข', "kh"}, {'ค', "kh"}, {'ฆ', "kh"}, {'ฅ', "kh"}, {'ฃ', "kh"},
            {'จ', "j"}, {'ฉ', "ch"}, {'ช', "ch"}, {'ฌ', "ch"},
            {'ฎ', "d"}, {'ด', "d"},
            {'ต', "t"}, {'ฏ', "t"},
            {'ถ', "th"}, {'ฐ', "th"}, {'ฑ', "th"}, {'ฒ', "th"}, {'ธ', "th"}, {'ท', "th"},
            {'บ', "b"}, {'ป', "p"}, {'พ', "ph"}, {'ผ', "ph"}, {'ภ', "ph"}, {'ฟ', "f"}, {'ฝ', "f"},
            {'ห', "h"}, {'ฮ', "h"},
            {'ม', "m"}, {'น', "n"}, {'ณ', "n"}, {'ร', "r"}, {'ล', "l"}, {'ฤ', "r"},
            {'ส', "s"}, {'ศ', "s"}, {'ษ', "s"}, {'ซ', "s"},
            {'ง', "g"}, {'ย', "y"}, {'ญ', "y"}, {'ว', "w"}, {'ฬ', "r"}
        };

        private static readonly Dictionary<char, string> XMapping = new Dictionary<char, string> {
            {'บ', "b"}, {'ป', "b"}, {'พ', "b"}, {'ฟ', "b"}, {'ภ', "b"},
            {'ด', "d"}, {'จ', "d"}, {'ช', "d"}, {'ซ', "d"}, {'ฎ', "d"}, {'ฏ', "d"}, {'ฐ', "d"},
            {'ฑ', "d"}, {'ฒ', "d"}, {'ต', "d"}, {'ถ', "d"}, {'ท', "d"}, {'ธ', "d"}, {'ศ', "d"}, {'ษ', "d"}, {'ส', "d"},
            {'ก', "k"}, {'ข', "k"}, {'ค', "k"}, {'ฆ', "k"},
            {'ว', "w"}, {'ย', "y"}, {'น', "n"}, {'ญ', "n"}, {'ณ', "n"}, {'ร', "n"}, {'ล', "n"}, {'ฬ', "n"},
            {'ง', "g"}, {'ม', "m"}
        };

        private static readonly HashSet<string> TrueClusters = new HashSet<string> {
            "กร", "กล", "กว", "ขร", "ขล", "ขว", "คร", "คล", "คว", "ปร", "ปล", "พร", "พล", "ตร", "ผล", "บร", "บล", "ฟร", "ฟล", "ดร", "ทร", "หง", "หญ", "หน", "หม", "หย", "หร", "หล", "หว", "อย"
        };

        private static readonly Regex ToneRegex = new Regex("[่้๊๋็]", RegexOptions.Compiled);
        private static readonly Regex KaranRegex = new Regex(".์", RegexOptions.Compiled);
        private static readonly Regex ThaiCharRegex = new Regex("[ก-ฮ]", RegexOptions.Compiled);
        private static readonly List<(string Key, Regex Pattern, string Replacement)> CompiledVowelMappings = new List<(string, Regex, string)>();

        static ThaiVCVPhonemizer() {
            foreach (var mapping in VowelMapping) {
                string pattern = "^" + mapping.Key.Replace("c", "([ก-ฮ][ลรว]?|อ[ย]?|ห[ก-ฮ]?)").Replace("x", "([ก-ฮ]{0,2})");
                CompiledVowelMappings.Add((mapping.Key, new Regex(pattern, RegexOptions.Compiled), mapping.Value));
            }
        }

        private Dictionary<string, string> CustomDictionary = new Dictionary<string, string> {
            {"บวร", "bQ wQn"},
            {"ศร", "sQn"},
            {"โสน", "sa no"},
            {"เบื้อง", "b3aN"},
            {"คือ", "kh1"},
            {"เรือ", "r6"},
            {"บ่", "bQ"},
            {"ก็", "kQ"},
            {"เอื้อ", "q6"},
            {"ตึก", "t1k"},
            {"ปู่", "pu"},
            {"ครุธ", "khrut"},
            {"ครุฑ", "khrut"},
            {"สถิตย์", "sathit"},
            {"ธ", "tha"},
            {"ณ", "na"},
            {"ฤาษี", "r1 si"},
            {"ฤทธิ์", "rit"},
            {"ฤกดิ์", "r3k"},
            {"ศักดิ์", "sak"}
        };
        private bool isDictLoaded = false;
        private USinger? singer;

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            LoadCustomDictionary();
        }

        private string ConvertDiffsingerToSyllables(string dsPhonemes) {
            var phonemes = dsPhonemes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> syllables = new List<string>();
            string currentSyllable = "";
            var dictVowels = new HashSet<string> { "a", "i", "u", "e", "o", "@", "Q", "3", "6", "1", "ia", "ua" };
            var diphthongs = new HashSet<string> { "r", "l", "w" };
            var endingConsonants = new HashSet<string> { "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y" };

            for (int i = 0; i < phonemes.Length; i++) {
                string p = phonemes[i];
                if (p == "K") p = "k";
                else if (p == "D") p = "d";
                else if (p == "B") p = "b";
                else if (p == "N") p = "n";
                else if (p == "M") p = "m";
                else if (p == "Y") p = "y";
                else if (p == "W") p = "w";
                
                // Map Diffsinger vowels to VCCV equivalents
                if (p == "A") p = "@";
                else if (p == "E") p = "3";
                else if (p == "O") p = "Q";
                else if (p == "U") p = "1";
                else if (p == "Ua") p = "6";
                else if (p == "au") p = "aw";
                
                if (p == "I") {
                    currentSyllable += "ay";
                    syllables.Add(currentSyllable);
                    currentSyllable = "";
                    continue;
                }
                if (p == "aw") {
                    currentSyllable += "aw";
                    syllables.Add(currentSyllable);
                    currentSyllable = "";
                    continue;
                }

                currentSyllable += p;
                
                if (dictVowels.Contains(p)) {
                    bool isCoda = false;
                    if (i + 1 < phonemes.Length) {
                        string nextP = phonemes[i + 1];
                        if (nextP == "K") nextP = "k";
                        else if (nextP == "D") nextP = "d";
                        else if (nextP == "B") nextP = "b";
                        else if (nextP == "N") nextP = "n";
                        else if (nextP == "M") nextP = "m";
                        else if (nextP == "Y") nextP = "y";
                        else if (nextP == "W") nextP = "w";

                        if (endingConsonants.Contains(nextP)) {
                            if (i + 2 >= phonemes.Length) {
                                isCoda = true;
                            } else {
                                string nextNextP = phonemes[i + 2];
                                if (dictVowels.Contains(nextNextP) || nextNextP == "I" || nextNextP == "aw") {
                                    isCoda = false;
                                } else if (diphthongs.Contains(nextNextP) && i + 3 < phonemes.Length && (dictVowels.Contains(phonemes[i + 3]) || phonemes[i + 3] == "I" || phonemes[i + 3] == "aw")) {
                                    isCoda = false;
                                } else {
                                    isCoda = true;
                                }
                            }
                        }
                    }
                    
                    if (isCoda) {
                        string nextP = phonemes[i + 1];
                        if (nextP == "K") nextP = "k";
                        else if (nextP == "D") nextP = "d";
                        else if (nextP == "B") nextP = "b";
                        else if (nextP == "N") nextP = "n";
                        else if (nextP == "M") nextP = "m";
                        else if (nextP == "Y") nextP = "y";
                        else if (nextP == "W") nextP = "w";
                        currentSyllable += nextP;
                        i++;
                    }
                    
                    syllables.Add(currentSyllable);
                    currentSyllable = "";
                }
            }
            
            if (!string.IsNullOrEmpty(currentSyllable)) {
                syllables.Add(currentSyllable);
            }
            
            return string.Join(" ", syllables);
        }

        private void LoadCustomDictionary() {
            if (isDictLoaded) return;
            try {
                string[] dictPaths = {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionaries", "dsdict-th.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "words_th.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "words_th_dict.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "TH_VCCV_Dict.txt")
                };

                foreach (var path in dictPaths) {
                    if (File.Exists(path)) {
                        var lines = File.ReadAllLines(path, Encoding.UTF8);
                        bool isTHVCCVDict = Path.GetFileName(path) == "TH_VCCV_Dict.txt";
                        foreach (var line in lines) {
                            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(new[] { '=', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2) {
                                string word = parts[0].Trim();
                                string phonemes = parts[1].Trim();
                                if (isTHVCCVDict) {
                                    phonemes = ConvertDiffsingerToSyllables(phonemes);
                                }
                                CustomDictionary[word] = phonemes;
                            }
                        }
                    }
                }
            } catch (Exception ex) { Log.Error(ex, "Failed to load custom dictionary."); }
            isDictLoaded = true;
        }

        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            foreach (string test in input) {
                if (test == null) continue;
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
            
            string prevSound = "-";
            if (prevNeighbour != null) {
                string prevLyric = string.IsNullOrEmpty(prevNeighbour.Value.phoneticHint) ? prevNeighbour.Value.lyric.Normalize() : prevNeighbour.Value.phoneticHint.Normalize();
                if (prevLyric != "-" && prevLyric.ToLower() != "r") {
                    var prevTh = ParseInput(prevLyric);
                    prevSound = prevTh.EndingConsonant ?? prevTh.Vowel ?? "-";
                }
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

                if (syllable == "-") {
                    if (checkOtoUntilHit(new string[] { $"{prevSound} -", "-" }, currentNote, out var oto)) aliases.Add(oto.Alias);
                    else aliases.Add("-");
                } else {
                    var (C, Dip, V, X) = ParseInput(syllable);

                    string coreCV = $"{C}{Dip}{V}";
                    string fallbackCV = $"{C}{V}";
                    if (string.IsNullOrEmpty(C) && string.IsNullOrEmpty(Dip)) coreCV = fallbackCV = V;

                    if (checkOtoUntilHit(new string[] { $"{prevSound} {coreCV}", $"{prevSound} {fallbackCV}" }, currentNote, out var otoVCV)) {
                        aliases.Add(otoVCV.Alias);
                    } else if (checkOtoUntilHit(new string[] { coreCV, fallbackCV, V }, currentNote, out var otoFallback)) {
                        aliases.Add(otoFallback.Alias); 
                    }

                    if (!string.IsNullOrEmpty(X) && !string.IsNullOrEmpty(V)) {
                        if (checkOtoUntilHit(new string[] { $"{V} {X}", X }, currentNote, out var otoX)) {
                            aliases.Add(otoX.Alias);
                        }
                    }

                    if (isEnd || forceClose) {
                        string tail = string.IsNullOrEmpty(X) ? V : X;
                        if (checkOtoUntilHit(new string[] { $"{tail} -", "-" }, currentNote, out var otoEnd)) {
                            aliases.Add(otoEnd.Alias);
                        }
                    }
                    
                    prevSound = string.IsNullOrEmpty(X) ? V : X;
                    if (prevSound == null) prevSound = "-";
                }
                
                if (aliases.Count == 0) aliases.Add(string.IsNullOrEmpty(syllable) ? "a" : syllable);
                
                bool firstHasVowel = aliases.Count > 0 && vowels.Any(v => aliases[0].Contains(v));
                int vcPosition = Math.Max(0, noteDuration - 120);

                for (int i = 0; i < aliases.Count; i++) {
                    string alias = aliases[i];
                    int position = 0;
                    if (i == 0) {
                        position = 0;
                    } else {
                        if (!firstHasVowel && i == 1) {
                            position = Math.Min((int)(noteDuration * 0.025), 20);
                        } else if (alias == "-") {
                            position = Math.Max((int)(noteDuration * 0.90), noteDuration - 18);
                        } else if (alias.EndsWith("-")) {
                            position = Math.Max((int)(noteDuration * 0.90), noteDuration - 18);
                        } else if (i == aliases.Count - 1 && aliases.Count >= 3) {
                            position = Math.Max((int)(noteDuration * 0.75), vcPosition);
                        } else {
                            position = Math.Max((int)(noteDuration * 0.75), vcPosition);
                        }
                    }
                    
                    int absolutePosition = position + noteStartPos;
                    int lastPos = phonemes.Count > 0 ? phonemes.Last().position : -120;
                    if (absolutePosition <= lastPos) {
                        absolutePosition = lastPos + 10;
                    }
                    absolutePosition = Math.Max(noteStartPos, Math.Min(absolutePosition, noteEndPos - 10));
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
            string? consonant = null, diphthong = null, vowel = null, endingConsonant = null;

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
                return CustomDictionary[input].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            input = input.Replace(" ", "");
            if (string.IsNullOrEmpty(input)) return new string[0];

            input = input.Replace("\u0E40\u0E40", "\u0E41");
            input = RemoveInvalidLetters(input);
            if (input == "ฤ" || input == "ฤๅ") return new[] { "r1" };
            if (input == "ก็") return new[] { "kQ" };

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

                        if (mapping.Key == "ไc" || mapping.Key == "ใc") x = "ย";
                        if (mapping.Key == "เcา") x = "ว";
                        if (mapping.Key == "cำ") x = "ม";

                        if (mapping.Key == "cรรx" && string.IsNullOrEmpty(x)) x = "น";
                        else if (mapping.Key.EndsWith("x") && string.IsNullOrEmpty(x) && !mapping.Key.StartsWith("c")) {
                            if (!TrueClusters.Contains(c) && c.Length > 1) {
                                x = c.Substring(c.Length - 1);
                                c = c.Substring(0, c.Length - 1);
                            }
                        }

                        if (c.Length >= 2 && (c.StartsWith("ห") || c.StartsWith("อ"))) c = c.Substring(1);
                        if (x.Length > 1) x = x.Substring(0, 1);

                        string cConverted = ConvertC(c);
                        string xConverted = ConvertX(x);

                        string phoneme;
                        if (mapping.Replacement == "a" && input.Contains("ั") && x == "ว") phoneme = cConverted + "ua";
                        else if (mapping.Key == "เcย") phoneme = cConverted + "3y";
                        else if (mapping.Replacement == "e" && x == "ย") phoneme = cConverted + "3" + xConverted;
                        else phoneme = cConverted + mapping.Replacement + xConverted;
                        
                        syllables.Add(phoneme);
                        input = input.Substring(match.Length);
                        matched = true;
                        break;
                    }
                }

                if (!matched) {
                    if (input.Length == 1) {
                        if (input == "ธ" || input == "ณ") syllables.Add(ConvertC(input) + "a");
                        else syllables.Add(ConvertC(input) + "Q");
                        input = "";
                    } else if (input.Length == 2) {
                        if (input[1] == 'ร') syllables.Add(ConvertC(input[0].ToString()) + "Qn");
                        else syllables.Add(ConvertC(input[0].ToString()) + "o" + ConvertX(input[1].ToString()));
                        input = "";
                    } else if (input.Length == 3) {
                        if (input[1] == 'ว') syllables.Add(ConvertC(input[0].ToString()) + "ua" + ConvertX(input[2].ToString()));
                        else syllables.Add(ConvertC(input.Substring(0, 2)) + "o" + ConvertX(input[2].ToString()));
                        input = "";
                    } else if (input.Length == 4) {
                        if (input[2] == 'ว') syllables.Add(ConvertC(input.Substring(0, 2).ToString()) + "ua" + ConvertX(input[3].ToString()));
                        else {
                            syllables.Add(input);
                        }
                        input = "";
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
            if (input == "อ") return "";
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
            if (XMapping.ContainsKey(firstChar)) return XMapping[firstChar];
            return input;
        }

        private string RemoveInvalidLetters(string input) {
            input = KaranRegex.Replace(input, "");
            input = ToneRegex.Replace(input, "");
            return input;
        }
    }
}
