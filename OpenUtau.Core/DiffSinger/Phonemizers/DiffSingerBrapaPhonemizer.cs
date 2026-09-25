using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger BRAPA Phonemizer", "DIFFS BRAPA", language: "PT", author: "HAI-D")]
    public class DiffSingerBrapaPhonemizer : DiffSingerRefinedPhonemizer {
        
        #region Base Class Overrides

        protected override string GetDictionaryName() => "dsdict-brapa.yaml";
        public override string GetLangCode() => "pt";
        protected override IG2p LoadBaseG2p() => new BrapaG2p();

        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "ae", "ah", "an", "ax", "e", "eh", "en", "i", "i0", "in",
            "o", "oh", "on", "u", "u0", "un"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "ch", "d", "dj", "f", "g", "h", "h9", "hr", "j", "k", "l", "lh", "m", "n",
            "ng", "nh", "p", "r", "r9", "rh", "rr", "rw", "s", "s9", "sh", "t", "v", "w", "wn",
            "x", "y", "yn", "z", "z9", "cl", "vf"
        };

        #endregion

        #region Phonetic Property Definitions

        protected string[] GetBaseG2pNasalVowels() => new string[] {
            "an", "en", "in", "on", "un"
        };
        protected string[] GetBaseG2pVoicedConsonants() => new string[] {
            "b", "d", "g", "d", "dj", "j", "l", "lh", "m", "n", "ng", "nh", "v", "z"
        };
        protected string[] GetBaseG2pVoicedPlosives() => new string[] {
            "b", "d", "g", "dj"
        };

        #endregion

        #region Core Processing Methods

        // Edits phonemes for a word, applying phonetic rules and transformations
        protected override List<phPerNote> EditPhonemesForWord(List<phPerNote> wordPhonemes, Note[] wordNotes, List<phPerNote>? previousWordPhonemes, List<phPerNote>? nextWordPhonemes, Note[]? previousWordNotes, Note[]? nextWordNotes) {
            // Input validation
            if (wordPhonemes == null) throw new ArgumentNullException(nameof(wordPhonemes));
            if (wordNotes == null) throw new ArgumentNullException(nameof(wordNotes));

            // Process each note in the word
            var processedWordPhonemes = new List<phPerNote>();

            // Edit phoneme sequence per note
            for (int noteIndex = 0; noteIndex < wordPhonemes.Count; noteIndex++) {
                var currentNoteGroup = wordPhonemes[noteIndex];
                var currentNote = wordNotes[0];

                // Skip transformations if phonetic hint is provided or starts with '/'
                if (!string.IsNullOrEmpty(currentNote.phoneticHint) || currentNote.lyric.StartsWith("/")) {
                    processedWordPhonemes.Add(new phPerNote(currentNoteGroup.Position, currentNoteGroup.Tone, new List<dsRefPhoneme>(currentNoteGroup.Phonemes)));
                    continue;
                }

                // Process each phoneme in this note
                var processedPhonemes = new List<dsRefPhoneme>();
                bool isLastNoteOfWord = noteIndex == wordPhonemes.Count - 1;

                for (int phonemeIndex = 0; phonemeIndex < currentNoteGroup.Phonemes.Count; phonemeIndex++) {
                    var phoneme = currentNoteGroup.Phonemes[phonemeIndex];
                    var processedPhoneme = phoneme;

                    // Apply word-level phonetic rules if this is the last phoneme of the current word
                    // (for interaction with the next word)
                    if (isLastNoteOfWord && phonemeIndex == currentNoteGroup.Phonemes.Count - 1 &&
                        nextWordPhonemes != null && nextWordPhonemes.Count > 0) {
                        processedPhoneme = ApplyWordBoundaryRulesToPhoneme(phoneme, wordPhonemes, nextWordPhonemes, currentNoteGroup.Phonemes, phonemeIndex);
                    }

                    processedPhonemes.Add(processedPhoneme);
                }

                processedWordPhonemes.Add(new phPerNote(currentNoteGroup.Position, currentNoteGroup.Tone, processedPhonemes));
            }

            return processedWordPhonemes;
        }

        // Applies duration-based phoneme replacements based on note duration
        protected override string ApplyDurationBasedReplacements(string phoneme, double durationMs, Note note) {
            // Input validation
            if (string.IsNullOrEmpty(phoneme)) return phoneme;
            
            // Skip processing for special cases
            if (note.lyric?.StartsWith("/") == true || !string.IsNullOrEmpty(note.phoneticHint)) {
                return phoneme;
            }

            // Check if phoneme needs language code prefix
            var langCode = GetLangCode() + "/";
            var hasLangCode = phoneme.StartsWith(langCode);
            var setLangCode = hasLangCode ? langCode : string.Empty;
            
            // Apply duration-based replacements for short phonemes
            if (durationMs > 0 && durationMs <= 45) {
                if (phoneme == setLangCode + "a") {
                    if (IsValidPhoneme(setLangCode + "ax")) {
                        return setLangCode + "ax";
                    }
                    return GetReplacement("ax");
                }
                if (phoneme == setLangCode + "i") {
                    if (IsValidPhoneme(setLangCode + "i0")) {
                        return setLangCode + "i0";
                    }
                    return GetReplacement("i0");
                }
                if (phoneme == setLangCode + "u") {
                    if (IsValidPhoneme(setLangCode + "u0")) {
                        return setLangCode + "u0";
                    }
                    return GetReplacement("u0");
                }
            }

            return phoneme;
        }

        #endregion

        #region Word Boundary Processing

        // Applies phonetic rules to a single phoneme at word boundaries
        private dsRefPhoneme ApplyWordBoundaryRulesToPhoneme(dsRefPhoneme phoneme, List<phPerNote> currentWordPhonemes, List<phPerNote> nextWordPhonemes, List<dsRefPhoneme> currentNotePhonemes, int currentPhonemeIndex) {
            if (string.IsNullOrEmpty(phoneme.Symbol)) return phoneme;
            
            var langCode = GetLangCode() + "/";

            // Determine language code prefix to use based on all phonemes in the current word
            bool wordUsesLangCode = DoesWordUseLanguageCode(currentWordPhonemes);
            var langCodePrefix = wordUsesLangCode ? langCode : string.Empty;
            
            // Get the first phoneme of the next word
            var nextWordFirstPhoneme = GetFirstPhonemeFromWord(nextWordPhonemes);
            if (nextWordFirstPhoneme == null) return phoneme;

            string transformedPhoneme = null;

            // Apply Sandhi rule: "s9" to "z" if current phoneme is "s9" and next word's first phoneme is vowel
            if (phoneme.Symbol == GetReplacement("s9") && IsVowel(nextWordFirstPhoneme.Value.Symbol)) {
                transformedPhoneme = langCodePrefix + "z";
            }
            // Apply Sandhi rule: "s9" to "z9" if current phoneme is "s9" and next word's first phoneme is voiced consonant
            else if (phoneme.Symbol == GetReplacement("s9") && IsVoicedConsonant(nextWordFirstPhoneme.Value.Symbol)) {
                transformedPhoneme = GetReplacement("z9");
            }
            // Apply Rhotic rule: "r9" to "r" if current phoneme is "r9" and next word's first phoneme is vowel
            else if (phoneme.Symbol == GetReplacement("r9") && IsVowel(nextWordFirstPhoneme.Value.Symbol)) {
                transformedPhoneme = langCodePrefix + "r";
            }
            // Apply cl rule: "cl" to "ng" if current phoneme is "cl", previous phoneme is nasal vowel, and next word's first phoneme is voiced plosive
            else if ((phoneme.Symbol == "cl" || phoneme.Symbol == langCode + "cl") &&
                     currentPhonemeIndex > 0 && IsNasalVowel(currentNotePhonemes[currentPhonemeIndex - 1].Symbol) &&
                     IsVoicedPlosive(nextWordFirstPhoneme.Value.Symbol)) {
                transformedPhoneme = langCodePrefix + "ng";
            }

            if (transformedPhoneme != null) {
                return new dsRefPhoneme(transformedPhoneme, phoneme.Speaker);
            }
            
            return phoneme;
        }

        // Determines if any phoneme in the word uses the language code prefix
        private bool DoesWordUseLanguageCode(List<phPerNote> wordPhonemes) {
            var langCode = GetLangCode() + "/";
            
            foreach (var notePhonemes in wordPhonemes) {
                foreach (var phoneme in notePhonemes.Phonemes) {
                    if (phoneme.Symbol?.StartsWith(langCode) == true) {
                        return true;
                    }
                }
            }
            
            return false;
        }

        // Gets the first non-null phoneme from the beginning of a word
        private dsRefPhoneme? GetFirstPhonemeFromWord(List<phPerNote> wordPhonemes) {
            if (wordPhonemes.Count == 0) return null;
            
            // Check each note in order to find the first valid phoneme
            for (int noteIndex = 0; noteIndex < wordPhonemes.Count; noteIndex++) {
                var notePhonemes = wordPhonemes[noteIndex].Phonemes;
                
                // Find the first non-empty phoneme in this note
                foreach (var phoneme in notePhonemes) {
                    if (!string.IsNullOrEmpty(phoneme.Symbol)) {
                        return phoneme;
                    }
                }
            }
            
            return null;
        }

        #endregion

        #region Phonetic Property Checkers

        // Checks if a phoneme is a vowel
        private bool IsVowel(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return false;
            
            var vowels = GetBaseG2pVowels();
            var cleanPhoneme = RemoveLangCode(phoneme);
            
            return vowels.Contains(cleanPhoneme);
        }

        // Checks if a phoneme is a nasal vowel
        private bool IsNasalVowel(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return false;
            
            var nasalVowels = GetBaseG2pNasalVowels();
            var cleanPhoneme = RemoveLangCode(phoneme);
            
            return nasalVowels.Contains(cleanPhoneme);
        }

        /// Checks if a phoneme is a voiced consonant
        private bool IsVoicedConsonant(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return false;
            
            var voicedConsonants = GetBaseG2pVoicedConsonants();
            var cleanPhoneme = RemoveLangCode(phoneme);
            
            return voicedConsonants.Contains(cleanPhoneme);
        }

        // Checks if a phoneme is a voiced plosive
        private bool IsVoicedPlosive(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return false;
            
            var voicedPlosives = GetBaseG2pVoicedPlosives();
            var cleanPhoneme = RemoveLangCode(phoneme);
            
            return voicedPlosives.Contains(cleanPhoneme);
        }
        
        #endregion

        #region Utility Methods

        // Removes language code prefix from phoneme symbol efficiently
        private string RemoveLangCode(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return phoneme;
            
            var langCode = GetLangCode() + "/";
            return phoneme.StartsWith(langCode)
                ? phoneme.Substring(langCode.Length)
                : phoneme;
        }

        #endregion

    }
}
