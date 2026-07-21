/*
 * Made And Checked By DELTA SYNTH & Gemini AI
 * Original by Patiphat Wongyai
 * Version: v1.0
 * History/Summary: Completely rewritten Indonesian VCCV Phonemizer to use Native G2P, removing the dependency on SyllableBasedPhonemizer and external CMU dictionaries. Implements Safety Position Buffer and Auto-Breath.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Indonesian VCCV Phonemizer", "ID VCCV Delta", "DELTA SYNTH", language: "ID")]
    // Version: v
    public class IndonesianVCCVPhonemizer : Phonemizer {
        
        static readonly string[] vowels = new string[] { "a", "i", "u", "e", "o", "E", "@", "ai", "au", "oi", "ei" };
        static readonly string[] consonants = new string[] { "b", "c", "ch", "d", "f", "g", "h", "j", "k", "kh", "l", "m", "n", "ng", "ny", "p", "q", "r", "s", "sy", "t", "v", "w", "x", "y", "z" };

        private Dictionary<string, string> CustomDictionary = new Dictionary<string, string>();
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
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Dictionary", "words_id.txt")
                };

                foreach (var path in dictPaths) {
                    if (File.Exists(path)) {
                        var lines = File.ReadAllLines(path, Encoding.UTF8);
                        foreach (var line in lines) {
                            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                            var parts = line.Split(new[] { '=', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2) CustomDictionary[parts[0].Trim().ToLower()] = parts[1].Trim().ToLower().Replace(" ", "");
                        }
                    }
                }
            } catch (Exception ex) { Log.Error(ex, "Failed to load custom dictionary for ID"); }
            isDictLoaded = true;
        }

        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            foreach (string test in input) {
                if (singer != null && singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    oto = otoCandidacy;
                    return true;
                }
            }
            return false;
        }

        (string? Consonant, string? Vowel, string? EndingConsonant) ParseInput(string input) {
            if (string.IsNullOrEmpty(input)) return (null, null, null);
            input = input.ToLower().Replace(" ", "");
            
            if (CustomDictionary.ContainsKey(input)) {
                input = CustomDictionary[input];
            }

            string? consonant = null, vowel = null, endingConsonant = null;

            foreach (var con in consonants) {
                if (input.StartsWith(con) && (consonant == null || consonant.Length < con.Length)) {
                    consonant = con;
                }
            }
            int startIdx = consonant?.Length ?? 0;

            foreach (var vow in vowels) {
                if (startIdx < input.Length && input.Substring(startIdx).StartsWith(vow) && (vowel == null || vowel.Length < vow.Length)) {
                    vowel = vow;
                }
            }
            int vowelEndIdx = startIdx + (vowel?.Length ?? 0);

            if (vowelEndIdx < input.Length) {
                string remainder = input.Substring(vowelEndIdx);
                foreach (var con in consonants) {
                    if (remainder.EndsWith(con) && (endingConsonant == null || endingConsonant.Length < con.Length)) {
                        endingConsonant = con;
                    }
                }
                if (endingConsonant == null) endingConsonant = remainder; // Fallback
            }

            return (consonant, vowel, endingConsonant);
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentLyric = string.IsNullOrEmpty(note.phoneticHint) ? note.lyric.Normalize() : note.phoneticHint.Normalize();

            var phonemes = new List<Phoneme>();
            List<string> tests = new List<string>();

            string prevTemp = prevNeighbour != null ? prevNeighbour.Value.lyric : "";
            var prevId = ParseInput(prevTemp);
            bool forceClose = false;

            if (currentLyric == "-") {
                if (prevNeighbour != null) {
                    string? endSound = prevId.Vowel;
                    if (endSound != null && checkOtoUntilHit(new[] { endSound + " -", endSound + "-" }, note, out var tempOto)) tests.Add(tempOto.Alias);
                }
                if (tests.Count == 0 && checkOtoUntilHit(new[] { "-" }, note, out var fallbackOto)) tests.Add(fallbackOto.Alias);
            } else {
                if (currentLyric.EndsWith("-") && currentLyric.Length > 1) {
                    forceClose = true;
                    currentLyric = currentLyric.Substring(0, currentLyric.Length - 1);
                }

                var noteId = ParseInput(currentLyric);

                if (noteId.Consonant != null) {
                    if (noteId.Vowel != null) {
                        if (checkOtoUntilHit(new[] { noteId.Consonant + noteId.Vowel, noteId.Consonant + " " + noteId.Vowel }, note, out var tempOto)) tests.Add(tempOto.Alias);
                        else if (checkOtoUntilHit(new[] { noteId.Consonant }, note, out tempOto)) tests.Add(tempOto.Alias);
                    } else {
                        if (checkOtoUntilHit(new[] { noteId.Consonant }, note, out var tempOto)) tests.Add(tempOto.Alias);
                    }
                }

                if (noteId.Consonant == null && noteId.Vowel != null) {
                    if (prevId.EndingConsonant != null) {
                        if (checkOtoUntilHit(new[] { prevId.EndingConsonant + " " + noteId.Vowel, prevId.EndingConsonant + noteId.Vowel }, note, out var tempOto)) tests.Add(tempOto.Alias);
                        else if (checkOtoUntilHit(new[] { noteId.Vowel }, note, out tempOto)) tests.Add(tempOto.Alias);
                    } else if (prevId.Vowel != null) {
                        if (checkOtoUntilHit(new[] { prevId.Vowel + " " + noteId.Vowel, noteId.Vowel }, note, out var tempOto)) tests.Add(tempOto.Alias);
                    } else {
                        if (checkOtoUntilHit(new[] { "- " + noteId.Vowel, "-" + noteId.Vowel, noteId.Vowel }, note, out var tempOto)) tests.Add(tempOto.Alias);
                    }
                }

                if (noteId.EndingConsonant != null && noteId.Vowel != null) {
                    if (checkOtoUntilHit(new[] { noteId.Vowel + " " + noteId.EndingConsonant, noteId.Vowel + noteId.EndingConsonant }, note, out var tempOto)) tests.Add(tempOto.Alias);
                    else if (checkOtoUntilHit(new[] { noteId.EndingConsonant }, note, out tempOto)) tests.Add(tempOto.Alias);
                } else if (nextNeighbour != null && noteId.Vowel != null && noteId.EndingConsonant == null) {
                    var nextId = ParseInput(nextNeighbour.Value.lyric);
                    if (nextId.Consonant != null) {
                        if (checkOtoUntilHit(new[] { noteId.Vowel + " " + nextId.Consonant, noteId.Vowel + nextId.Consonant }, note, out var tempOto)) tests.Add(tempOto.Alias);
                    }
                }

                if (prevNeighbour == null && tests.Count >= 1) {
                    if (checkOtoUntilHit(new[] { "- " + tests[0], "-" + tests[0] }, note, out var tempOto)) tests[0] = tempOto.Alias;
                }

                if (forceClose && tests.Count >= 1) {
                    if (noteId.EndingConsonant == null) {
                        if (checkOtoUntilHit(new[] { noteId.Vowel + " -", noteId.Vowel + "-" }, note, out var tempOto)) tests.Add(tempOto.Alias);
                    }
                }

                if (tests.Count <= 0 && checkOtoUntilHit(new[] { currentLyric }, note, out var fallbackOto)) tests.Add(currentLyric);
            }

            if (checkOtoUntilHit(tests.ToArray(), note, out var oto)) {
                var noteDuration = notes.Sum(n => n.duration);

                if (currentLyric != "-" && prevNeighbour == null && checkOtoUntilHit(new[] { "breath" }, note, out var breathOto)) {
                    int space = prev != null ? note.position - (prev.Value.position + prev.Value.duration) : note.position;
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

                    if (nextNeighbour != null && tests[i].Contains(" ")) {
                        var nextLyric = string.IsNullOrEmpty(nextNeighbour.Value.phoneticHint) ? nextNeighbour.Value.lyric.Normalize() : nextNeighbour.Value.phoneticHint.Normalize();
                        var nextId = ParseInput(nextLyric);
                        var nextCheck = nextId.Vowel;
                        if (nextId.Consonant != null) nextCheck = nextId.Consonant + nextId.Vowel;

                        var nextAttr = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
                        if (singer != null && singer.TryGetMappedOto(nextCheck ?? "", nextNeighbour.Value.tone + nextAttr.toneShift, nextAttr.voiceColor, out var nextOto) && oto.Overlap > 0) {
                            vcPosition = noteDuration - timeAxis.MsPosToTickPos(nextOto.Overlap) - timeAxis.MsPosToTickPos(nextOto.Preutter);
                        }
                    }

                    if (i < mainVowelIndex) {
                        int offset = (mainVowelIndex - i) * Math.Max(60, (int)(noteDuration * 0.05));
                        position = -offset;
                    } else if (i == mainVowelIndex) {
                        position = 0;
                    } else {
                        if (tests[i].EndsWith("-") && tests.Count > 1) {
                            position = Math.Max((int)(noteDuration * 0.90), noteDuration - 18);
                        } else if (ParseInput(currentLyric).EndingConsonant != null && i == tests.Count - 1) {
                            position = (int)(noteDuration * 0.75);
                        } else {
                            position = Math.Max((int)(noteDuration * 0.70), vcPosition);
                            if (tests.Count > 2 && i == tests.Count - 2 && tests[tests.Count - 1].EndsWith("-")) {
                                position = Math.Max((int)(noteDuration * 0.65), vcPosition - 60);
                            }
                        }
                    }

                    int lastPos = phonemes.Count > 0 ? phonemes.Last().position : -120;
                    if (position <= lastPos) {
                        position = lastPos + 10;
                    }
                    position = Math.Min(position, noteDuration - 10);

                    phonemes.Add(new Phoneme { phoneme = tests[i], position = position });
                }
            }

            int gap = prevNeighbour == null ? 9999 : note.position - (prevNeighbour.Value.position + prevNeighbour.Value.duration);
            if (gap >= 120) {
                bool hasOpening = phonemes.Count > 0 && (phonemes[0].phoneme.StartsWith("- ") || phonemes[0].phoneme.StartsWith("-"));
                if (!hasOpening) {
                    var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
                    if (singer != null && singer.TryGetMappedOto("Breath", note.tone + attr.toneShift, attr.voiceColor, out var breathOto)) {
                        phonemes.Insert(0, new Phoneme { phoneme = breathOto.Alias, position = -60 });
                    }
                }
            }
            
            return new Result { phonemes = phonemes.ToArray() };
        }
    }
}

