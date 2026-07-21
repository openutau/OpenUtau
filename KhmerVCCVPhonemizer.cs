#pragma warning disable CS0618, CS0649, CS8632, CS0108
#nullable enable
#pragma warning disable CS8632
// ==========================================
// Made And Checked By DELTA SYNTH & Gemini AI
// Original by Patiphat Wongyai
// Version: v.17.1
// History/Summary: ยกระดับ Phonemizer แก้ไขบัคสระไอ สระใอ สระเอา สระอำ ให้รองรับตัวสะกด ขยายพื้นที่พยัญชนะเพื่อความเป็นธรรมชาติ และเพิ่มเอื้อนจังหวะเร็ว
// พร้อมเพิ่มระบบแยกพยางค์ (สูงสุด 4 พยางค์) และรองรับการกระจายพยางค์ไปที่โน้ตเนื้อร้อง + โดยอัตโนมัติ
// v.17.1: แก้บัค Auto-Melisma ที่เพิ่ม Vowel ซ้ำทั้งที่มีอยู่แล้วใน tests list ทำให้เสียงซ้ำ
// ==========================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Khmer VCCV Phonemizer", "KM VCCV Delta", "DELTA SYNTH", language: "KM")]
    public class KhmerVCCVPhonemizer : Phonemizer {

        static readonly string[] vowels = new string[] { "a", "i", "u", "e", "o", "@", "Q", "3", "6", "1", "ia", "ua" };
        static readonly string[] diphthongs = new string[] { "r", "l", "w" };
        static readonly string[] consonants = new string[] { "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y" };
        static readonly string[] endingConsonants = new string[] { "b", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "p", "ph", "r", "s", "t", "th", "w", "y" };

        private static readonly List<(string Key, string Value)> VowelMapping = new List<(string Key, string Value)> {
            ("เcือะ", "6"), ("เcือx", "6"), ("แcะ", "@"), ("แcx", "@"), ("เcอะ", "3"), ("เcิร์x", "3"), ("เcอ", "3"), 
            ("ไc", "ay"), ("ใc", "ay"), ("ไcx", "ay"), ("ใcx", "ay"),
            ("เcาะ", "Q"), ("cอx", "Q"), ("แccx", "@"), 
            ("cืx", "1"), ("cึx", "1"), ("cือ", "1"), ("cะ", "a"), ("cัx", "a"), ("cาx", "a"), ("cรรx", "a"), 
            ("เcา", "aw"), ("เcาx", "aw"), 
            ("เcะ", "e"), ("เcx", "e"), ("cิx", "i"), ("cีx", "i"),
            ("เcียะ", "ia"), ("เcียx", "ia"), ("โcะ", "o"), ("โcx", "o"), ("cุx", "u"), ("cูx", "u"), 
            ("cัวะ", "ua"), ("cัว", "ua"), ("cวx", "ua"), ("เcิx", "3"), ("เcิ", "3"),
            ("cำ", "am"), ("cำx", "am")
        };

        private static readonly Dictionary<char, string> CMapping = new Dictionary<char, string> {
            {'ก', "k"}, {'ข', "kh"}, {'ค', "kh"}, {'ฆ', "kh"}, {'ฅ', "kh"}, {'ฃ', "kh"},
            {'จ', "j"}, {'ฉ', "ch"}, {'ช', "ch"}, {'ฌ', "ch"}, {'ฎ', "d"}, {'ด', "d"}, {'ต', "t"}, {'ฏ', "t"},
            {'ถ', "th"}, {'ฐ', "th"}, {'ฑ', "th"}, {'ฒ', "th"}, {'ธ', "th"}, {'ท', "th"},
            {'บ', "b"}, {'ป', "p"}, {'พ', "ph"}, {'ผ', "ph"}, {'ภ', "ph"}, {'ฟ', "f"}, {'ฝ', "f"},
            {'ห', "h"}, {'ฮ', "h"}, {'ม', "m"}, {'น', "n"}, {'ณ', "n"}, {'ร', "r"}, {'ล', "l"}, {'ฤ', "r"},
            {'ส', "s"}, {'ศ', "s"}, {'ษ', "s"}, {'ซ', "s"}, {'ง', "g"}, {'ย', "y"}, {'ญ', "y"}, {'ว', "w"}, {'ฬ', "r"}
        };

        private static readonly Dictionary<char, string> XMapping = new Dictionary<char, string> {
            {'บ', "b"}, {'ป', "b"}, {'พ', "b"}, {'ฟ', "b"}, {'ภ', "b"},
            {'ด', "d"}, {'จ', "d"}, {'ช', "d"}, {'ซ', "d"}, {'ฎ', "d"}, {'ฏ', "d"}, {'ฐ', "d"}, {'ฑ', "d"}, {'ฒ', "d"}, {'ต', "d"}, {'ถ', "d"}, {'ท', "d"}, {'ธ', "d"}, {'ศ', "d"}, {'ษ', "d"}, {'ส', "d"},
            {'ก', "k"}, {'ข', "k"}, {'ค', "k"}, {'ฆ', "k"}, {'ว', "w"}, {'ย', "y"}, {'น', "n"}, {'ญ', "n"}, {'ณ', "n"}, {'ร', "n"}, {'ล', "n"}, {'ฬ', "n"}, {'ง', "g"}, {'ม', "m"}
        };

        private static readonly HashSet<string> TrueClusters = new HashSet<string> {
            "กร", "กล", "กว", "ขร", "ขล", "ขว", "คร", "คล", "คว", "ปร", "ปล", "พร", "พล", "ตร", "ผล", "บร", "บล", "ฟร", "ฟล", "ดร", "ทร", "หง", "หญ", "หน", "หม", "หย", "หร", "หล", "หว", "อย", "สร", "ศร", "ซร", "จร"
        };

        private static readonly Regex ToneRegex = new Regex("[่้๊๋็]", RegexOptions.Compiled);
        private static readonly Regex KaranRegex = new Regex(".์", RegexOptions.Compiled);
        private static readonly Regex ThaiCharRegex = new Regex("[ก-ฮ]", RegexOptions.Compiled);
        private static readonly List<(string Key, Regex Pattern, string Replacement)> CompiledVowelMappings = new List<(string, Regex, string)>();

        static KhmerVCCVPhonemizer() {
            foreach (var mapping in VowelMapping) {
                // Remove the "$" to match prefix for multi-syllable support
                string pattern = "^" + mapping.Key.Replace("c", "([ก-ฮ][ลรว]?|อ[ย]?|ห[ก-ฮ]?)").Replace("x", "([ก-ฮ]{0,2})");
                CompiledVowelMappings.Add((mapping.Key, new Regex(pattern, RegexOptions.Compiled), mapping.Value));
            }
        }

        private Dictionary<string, string> CustomDictionary = new Dictionary<string, string> {
            {"เสมอ", "sa m3"},
            {"สม่ำเสมอ", "sa mam sa m3"},
            {"บวร", "bawQn"},
            {"ศร", "sQn"},
            {"โสน", "sano"},
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
            {"ฤาษี", "r1si"},
            {"ฤทธิ์", "rit"},
            {"ฤกดิ์", "r3k"},
            {"ศักดิ์", "sak"},
            {"พรรค", "phak"},
            {"สวรรค์", "sawam"},
            {"ธรรม", "tham"},
            {"เปลี่ยน", "prian"},
            {"จันทรา", "janthra"},
            {"รับ", "rab"},
            {"ตน", "ton"},
            {"ปราถนา", "pradthana"},
            {"ปรารถ", "prarod"},
            {"ทวน", "thuan"},
            {"ขวัญ", "khuan"},
            {"อาทิตย์", "athid"}
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
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "words_th_dict.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "words_th_vccv.txt")
                };

                foreach (var path in dictPaths) {
                    if (File.Exists(path)) {
                        var lines = File.ReadAllLines(path, Encoding.UTF8);
                        foreach (var line in lines) {
                            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(new[] { '=', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            // Do not remove spaces so that multi-syllable definitions remain intact
                            if (parts.Length >= 2) CustomDictionary[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
            } catch (Exception ex) { Log.Error(ex, "Failed to load custom dictionary"); }
            isDictLoaded = true;
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
            var phonemes = new List<Phoneme>();
            var firstNote = notes[0];
            var currentLyric = string.IsNullOrEmpty(firstNote.phoneticHint) ? firstNote.lyric.Normalize() : firstNote.phoneticHint.Normalize();

            if (currentLyric == "-") {
                List<string> tests = new List<string>();
                string prevTemp = prevNeighbour != null ? prevNeighbour.Value.lyric : "";
                var prevThStrs = WordToPhonemes(prevTemp);
                var prevTh = ParseInput(prevThStrs.Length > 0 ? prevThStrs.Last() : "");

                if (prevNeighbour != null) {
                    string endSound = prevTh.Vowel;
                    if (endSound != null && checkOtoUntilHit(new[] { endSound + " -", endSound + "-" }, firstNote, out var tempOto)) tests.Add(tempOto.Alias);
                }
                if (tests.Count == 0 && checkOtoUntilHit(new[] { "-" }, firstNote, out var fallbackOto)) tests.Add(fallbackOto.Alias);
                
                if (tests.Count > 0) phonemes.Add(new Phoneme { phoneme = tests[0], position = 0 });
                return new Result { phonemes = phonemes.ToArray() };
            }

            bool forceClose = false;
            if (currentLyric.EndsWith("-") && currentLyric.Length > 1) {
                forceClose = true;
                currentLyric = currentLyric.Substring(0, currentLyric.Length - 1);
            }

            var syllables = WordToPhonemes(currentLyric);
            if (syllables.Length == 0) return new Result { phonemes = phonemes.ToArray() };

            string prevTempNote = prevNeighbour != null ? prevNeighbour.Value.lyric : "";
            var prevSyllables = WordToPhonemes(prevTempNote);
            var prevThLast = ParseInput(prevSyllables.Length > 0 ? prevSyllables.Last() : "");

            int totalNotesDuration = notes.Sum(n => n.duration);
            int currentNoteIndex = 0;
            int currentTickPosition = 0;

            for (int sIdx = 0; sIdx < syllables.Length; sIdx++) {
                string syllable = syllables[sIdx];
                var noteTh = ParseInput(syllable);

                Note currentNote = notes[currentNoteIndex];
                int noteStartPos = currentNote.position - firstNote.position;
                int noteEndPos = noteStartPos + currentNote.duration;

                // Move to next note if this is not the first syllable and there is a + note available
                if (sIdx > 0 && currentNoteIndex < notes.Length - 1) {
                    currentNoteIndex++;
                    currentNote = notes[currentNoteIndex];
                    noteStartPos = currentNote.position - firstNote.position;
                    noteEndPos = noteStartPos + currentNote.duration;
                }
                
                int noteDuration = currentNote.duration;
                // If multiple syllables on the same note, calculate equal durations
                int remainingSyllablesOnNote = 1;
                for (int nextSIdx = sIdx + 1; nextSIdx < syllables.Length; nextSIdx++) {
                    if (nextSIdx > 0 && currentNoteIndex < notes.Length - 1) break; // It will move to next note
                    remainingSyllablesOnNote++;
                }
                
                // Effective duration for this syllable
                int sylDuration = noteDuration / remainingSyllablesOnNote;
                if (sIdx > 0 && currentNoteIndex == 0) {
                    // Spread within a single note
                    noteStartPos = currentTickPosition;
                    noteEndPos = noteStartPos + sylDuration;
                    noteDuration = sylDuration;
                }

                Note? nextSylNeighbour = null;
                if (sIdx == syllables.Length - 1) {
                    nextSylNeighbour = nextNeighbour;
                } else if (currentNoteIndex < notes.Length - 1) {
                    nextSylNeighbour = notes[currentNoteIndex + 1];
                }

                List<string> tests = new List<string>();

                if (noteTh.Consonant != null) {
                    if (noteTh.Dipthong == null && noteTh.Vowel != null) {
                        if (checkOtoUntilHit(new[] { noteTh.Consonant + noteTh.Vowel, noteTh.Consonant + " " + noteTh.Vowel }, currentNote, out var tempOto)) tests.Add(tempOto.Alias);
                    } else if (noteTh.Dipthong != null && noteTh.Vowel != null) {
                        if (checkOtoUntilHit(new[] { noteTh.Consonant + noteTh.Dipthong + noteTh.Vowel }, currentNote, out var tempOto)) {
                            tests.Add(tempOto.Alias);
                        } else {
                            if (checkOtoUntilHit(new[] { noteTh.Consonant + " " + noteTh.Dipthong, noteTh.Consonant + noteTh.Dipthong }, currentNote, out tempOto)) tests.Add(tempOto.Alias);
                            else if (checkOtoUntilHit(new[] { noteTh.Consonant }, currentNote, out tempOto)) tests.Add(tempOto.Alias);
                            if (checkOtoUntilHit(new[] { noteTh.Dipthong + noteTh.Vowel }, currentNote, out tempOto)) tests.Add(tempOto.Alias);
                        }
                    }
                }

                if (noteTh.Consonant == null && noteTh.Vowel != null) {
                    if (sIdx == 0 && prevThLast.EndingConsonant != null) {
                        if (checkOtoUntilHit(new[] { prevThLast.EndingConsonant + " " + noteTh.Vowel, prevThLast.EndingConsonant + noteTh.Vowel }, currentNote, out var tempOto)) tests.Add(tempOto.Alias);
                        else if (checkOtoUntilHit(new[] { noteTh.Vowel }, currentNote, out tempOto)) tests.Add(tempOto.Alias);
                    } else if (sIdx == 0 && prevThLast.Vowel != null) {
                        if (checkOtoUntilHit(new[] { noteTh.Vowel }, currentNote, out var tempOto)) tests.Add(tempOto.Alias);
                    } else {
                        if (checkOtoUntilHit(new[] { noteTh.Vowel }, currentNote, out var tempOto)) tests.Add(tempOto.Alias);
                    }
                }

                if (noteTh.EndingConsonant != null && noteTh.Vowel != null) {
                    if (checkOtoUntilHit(new[] { noteTh.EndingConsonant }, currentNote, out var tempOto)) tests.Add(tempOto.Alias);
                    else if (checkOtoUntilHit(new[] { noteTh.Vowel + noteTh.EndingConsonant, noteTh.Vowel + " " + noteTh.EndingConsonant }, currentNote, out tempOto)) tests.Add(tempOto.Alias);
                } else if (nextSylNeighbour != null && noteTh.Vowel != null && noteTh.EndingConsonant == null) {
                    var nextSylStrs = WordToPhonemes(string.IsNullOrEmpty(nextSylNeighbour.Value.phoneticHint) ? nextSylNeighbour.Value.lyric.Normalize() : nextSylNeighbour.Value.phoneticHint.Normalize());
                    var nextTh = ParseInput(nextSylStrs.Length > 0 ? nextSylStrs[0] : "");
                    if (nextTh.Consonant != null) {
                        if (checkOtoUntilHit(new[] { noteTh.Vowel + " " + nextTh.Consonant, noteTh.Vowel + nextTh.Consonant }, currentNote, out var tempOto)) tests.Add(tempOto.Alias);
                    }
                }

                if (sIdx == 0 && prevNeighbour == null && tests.Count >= 1) {
                    if (checkOtoUntilHit(new[] { "- " + tests[0], "-" + tests[0] }, currentNote, out var tempOto)) tests[0] = tempOto.Alias;
                    else if (noteTh.Consonant == null && noteTh.Vowel != null) {
                        if (checkOtoUntilHit(new[] { "- " + noteTh.Vowel, "-" + noteTh.Vowel }, currentNote, out tempOto)) tests[0] = tempOto.Alias;
                    }
                }

                if (forceClose && sIdx == syllables.Length - 1 && tests.Count >= 1) {
                    if (noteTh.EndingConsonant == null) {
                        if (checkOtoUntilHit(new[] { noteTh.Vowel + " -", noteTh.Vowel + "-" }, currentNote, out var tempOto)) tests.Add(tempOto.Alias);
                    }
                }

                if (tests.Count <= 0 && checkOtoUntilHit(new[] { syllable }, currentNote, out var fallbackOto)) tests.Add(syllable);


                if (checkOtoUntilHit(tests.ToArray(), currentNote, out var oto)) {
                    bool firstHasVowel = tests.Count > 0 && vowels.Any(v => tests[0].Contains(v));

                    if (sIdx == 0 && currentLyric != "-" && prevNeighbour == null && checkOtoUntilHit(new[] { "breath" }, currentNote, out var breathOto)) {
                        int space = prev != null ? currentNote.position - (prev.Value.position + prev.Value.duration) : currentNote.position;
                        int breathPosition = space > 0 ? -Math.Min(space, 240) : -120;
                        if (breathPosition > -120) breathPosition = -120;
                        phonemes.Add(new Phoneme { phoneme = breathOto.Alias, position = breathPosition });
                    }

                    int mainVowelIndex = -1;
                    for (int i = 0; i < tests.Count; i++) {
                        if (vowels.Any(v => tests[i].Contains(v))) {
                            mainVowelIndex = i;
                            break;
                        }
                    }
                    if (mainVowelIndex == -1) mainVowelIndex = 0;

                    for (int i = 0; i < tests.Count; i++) {
                        int position = 0;
                        int vcPosition = noteDuration - 120;

                        if (nextSylNeighbour != null && tests[i].Contains(" ")) {
                            var nextSylStrs = WordToPhonemes(string.IsNullOrEmpty(nextSylNeighbour.Value.phoneticHint) ? nextSylNeighbour.Value.lyric.Normalize() : nextSylNeighbour.Value.phoneticHint.Normalize());
                            var nextTh = ParseInput(nextSylStrs.Length > 0 ? nextSylStrs[0] : "");
                            var nextCheck = nextTh.Vowel;
                            if (nextTh.Consonant != null) nextCheck = nextTh.Consonant + nextTh.Vowel;
                            if (nextTh.Dipthong != null) nextCheck = nextTh.Consonant + nextTh.Dipthong + nextTh.Vowel;

                            var nextAttr = nextSylNeighbour.Value.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
                            if (singer.TryGetMappedOto(nextCheck, nextSylNeighbour.Value.tone + nextAttr.toneShift, nextAttr.voiceColor, out var nextOto) && oto.Overlap > 0) {
                                vcPosition = noteDuration - timeAxis.MsPosToTickPos(nextOto.Overlap) - timeAxis.MsPosToTickPos(nextOto.Preutter);
                            }
                        }

                        if (i < mainVowelIndex) {
                            int offset = (mainVowelIndex - i) * Math.Max(80, (int)(noteDuration * 0.10));
                            position = -offset;
                        } else if (i == mainVowelIndex) {
                            position = 0;
                        } else {
                            if (tests[i].EndsWith("-") && tests.Count > 1) {
                                position = Math.Max((int)(noteDuration * 0.90), noteDuration - 18);
                            } else if (noteTh.EndingConsonant != null && i == tests.Count - 1) {
                                position = (int)(noteDuration * 0.75);
                            } else {
                                position = Math.Max((int)(noteDuration * 0.60), vcPosition);
                                if (tests.Count > 2 && i == tests.Count - 2 && tests[tests.Count - 1].EndsWith("-")) {
                                    position = Math.Max((int)(noteDuration * 0.60), vcPosition - 60);
                                }
                            }
                        }

                        int absolutePosition = noteStartPos + position;
                        int lastPos = phonemes.Count > 0 ? phonemes.Last().position : -120;
                        if (absolutePosition <= lastPos) {
                            absolutePosition = lastPos + 10;
                        }
                        absolutePosition = Math.Min(absolutePosition, noteEndPos - 10);

                        phonemes.Add(new Phoneme { phoneme = tests[i], position = absolutePosition });
                        currentTickPosition = absolutePosition;
                    }
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

            foreach (var con in consonants) if (input.StartsWith(con) && (consonant == null || consonant.Length < con.Length)) consonant = con;
            int startIdx = consonant?.Length ?? 0;

            foreach (var dip in diphthongs) if (input.Substring(startIdx).StartsWith(dip) && (diphthong == null || diphthong.Length < dip.Length)) diphthong = dip;
            startIdx += diphthong?.Length ?? 0;

            foreach (var vow in vowels) if (input.Substring(startIdx).StartsWith(vow) && (vowel == null || vowel.Length < vow.Length)) vowel = vow;

            int vowelEndIdx = startIdx + (vowel?.Length ?? 0);
            if (vowelEndIdx < input.Length) {
                string remainder = input.Substring(vowelEndIdx);
                foreach (var con in endingConsonants) if (remainder.EndsWith(con) && (endingConsonant == null || endingConsonant.Length < con.Length)) endingConsonant = con;
            }

            return (consonant, diphthong, vowel, endingConsonant);
        }

        public string[] WordToPhonemes(string input) {
            if (string.IsNullOrEmpty(input)) return new string[0];
            
            // Check dictionary with spaces
            if (CustomDictionary.ContainsKey(input)) {
                return CustomDictionary[input].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }

            // Remove spaces for regex parsing
            input = input.Replace(" ", "");
            if (string.IsNullOrEmpty(input)) return new string[0];

            input = input.Replace("\u0E40\u0E40", "\u0E41");
            input = KaranRegex.Replace(input, "");
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

                        if (c.Length >= 2 && (c.StartsWith("ห") || c.StartsWith("อ"))) c = c.Substring(1);
                        string cConverted = ConvertC(c);
                        string xConverted = ConvertX(x);

                        string phoneme;
                        if (mapping.Replacement == "a" && input.Contains("ั") && x == "ว") phoneme = cConverted + "ua";
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
                        syllables.Add(ConvertC(input));
                        input = "";
                    } else if (input.Length >= 2) {
                        if (input.Length == 3 && input[1] == 'ว') {
                            syllables.Add(ConvertC(input[0].ToString()) + "ua" + ConvertX(input[2].ToString()));
                            input = input.Substring(3);
                        } else if (input.Length == 4 && input[2] == 'ว') {
                            syllables.Add(ConvertC(input.Substring(0, 2)) + "ua" + ConvertX(input[3].ToString()));
                            input = input.Substring(4);
                        } else {
                            syllables.Add(ConvertC(input[0].ToString()) + "o" + ConvertX(input[1].ToString()));
                            input = input.Substring(2);
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
            return XMapping.ContainsKey(firstChar) ? XMapping[firstChar] : input;
        }
    }
}
