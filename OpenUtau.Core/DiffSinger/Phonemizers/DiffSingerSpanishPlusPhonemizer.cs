using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
 
namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger Spanish+ Phonemizer", "DIFFS ES+", language: "ES")]
    public class DiffSingerSpanishPlusPhonemizer : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName()=>"dsdict-es.yaml";
        public override string GetLangCode()=>"es";
        protected override IG2p LoadBaseG2p() => new SpanishG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "e", "i", "o", "u"
        };
 
        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "B", "ch", "d", "D", "f", "g", "G", "gn", "I", "k", "l",
            "ll", "m", "n", "p", "r", "rr", "s", "t", "U", "w", "x", "y", "Y", "z"
        };
 
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (notes[0].lyric == "-") {
                return MakeSimpleResult("SP");
            }
            if (notes[0].lyric == "br") {
                return MakeSimpleResult("AP");
            }
 
            if (!partResult.TryGetValue(notes[0].position, out var phonemes)) {
                throw new Exception("Result not found in the part");
            }
 
            var processedPhonemes = new List<Phoneme>();
            var langCode = GetLangCode() + "/";
 
            // If the lyric starts with ', the G2p will have produced junk phonemes for the '
            // character (e.g. 'd' in Spanish G2p). We need to:
            //   1. Skip all leading consonant phonemes that the G2p generated from the '
            //   2. Replace them with the correct glottal phoneme at that same position
            // This makes the glottal behave like any other consonant before a vowel.
            bool hasGlottalPrefix = notes[0].lyric.StartsWith("'");
            int startIndex = 0;
 
            if (hasGlottalPrefix) {
                // Find the first vowel — everything before it is junk from the ' character
                var vowelSet = GetBaseG2pVowels().ToHashSet();
                while (startIndex < phonemes.Count) {
                    string ph = phonemes[startIndex].Item1.Replace(langCode, "");
                    if (vowelSet.Contains(ph)) break;
                    startIndex++;
                }
 
                string glottal = ResolveGlottalPhoneme(prevNeighbour);
                if (glottal != null && startIndex < phonemes.Count) {
                    int glottalPosition;
                    if (startIndex > 0) {
                        // Junk consonants existed — steal the position of the first one
                        glottalPosition = phonemes[0].Item2;
                    } else {
                        // No junk consonants — G2p went straight to the vowel (e.g. 'e, 'i, 'o, 'u).
                        // Synthesize a consonant-like offset before the vowel. 120 ticks matches
                        // the typical consonant pre-roll used elsewhere in OpenUtau.
                        glottalPosition = phonemes[startIndex].Item2 - 120;
                    }
                    processedPhonemes.Add(new Phoneme() {
                        phoneme = glottal,
                        position = glottalPosition
                    });
                }
                // If no vowel found at all, fall through with startIndex == phonemes.Count (emit nothing)
            }
 
            string prevPhoneme = "";
            if (prevNeighbour.HasValue && !notes[0].lyric.StartsWith("+")) {
                prevPhoneme = GetPreviousPhoneme(notes[0].position);
            }
 
            for (int i = startIndex; i < phonemes.Count; i++) {
                var tu = phonemes[i];
                string phoneme = tu.Item1;
 
                if (ShouldReplacePhoneme(phoneme, prevPhoneme)) {
                    phoneme = GetReplacementPhoneme(phoneme);
                }
 
                processedPhonemes.Add(new Phoneme() {
                    phoneme = phoneme,
                    position = tu.Item2
                });
 
                prevPhoneme = phoneme;
            }
            return new Result {
                phonemes = processedPhonemes.ToArray()
            };
        }
 
        /// <summary>
        /// Resolves which glottal phoneme to use, mirroring the ARPA+ priority logic:
        /// prefer "vf" if available and there's no previous neighbour, else "cl", else "q".
        /// Returns null if none are available (glottal stop is silently skipped).
        /// </summary>
        private string ResolveGlottalPhoneme(Note? prevNeighbour) {
            var langCode = GetLangCode() + "/";
 
            // "vf" (vocal fry / glottal) takes priority when there's no preceding note
            if (HasPhoneme("vf") && (!prevNeighbour.HasValue || string.IsNullOrWhiteSpace(prevNeighbour.Value.lyric))) {
                return "vf";
            }
            // "cl" is the standard glottal stop closure
            if (HasPhoneme("cl")) {
                return "cl";
            }
            // Prefer language-scoped "q" over bare "q"
            if (HasPhoneme(langCode + "q")) {
                return langCode + "q";
            }
            if (HasPhoneme("q")) {
                return "q";
            }
            // No glottal phoneme available in this voice bank — skip silently
            return null;
        }
 
        private string GetPreviousPhoneme(int currentPos) {
            var prevs = partResult.Where(kv => kv.Key < currentPos).OrderByDescending(kv => kv.Key);
            foreach (var kv in prevs) {
                if (kv.Value.Count > 0) {
                    return kv.Value.Last().Item1;
                }
            }
            return "";
        }
 
        private bool ShouldReplacePhoneme(string phoneme, string prevPhoneme) {
            var langCode = GetLangCode() + "/";
            string cleanPrev = prevPhoneme.Replace(langCode, "");
            if (string.IsNullOrEmpty(cleanPrev) || cleanPrev == "SP" || cleanPrev == "AP") {
                return false;
            }
            if (cleanPrev == "m" || cleanPrev == "n" || cleanPrev == "l") {
                return false;
            }

            if (phoneme == langCode + "g" || phoneme == "g") {
                return true;
            }
            if (phoneme == langCode + "d" || phoneme == "d") {
                return true;
            }
            if (phoneme == langCode + "b" || phoneme == "b") {
                return true;
            }
            return false;
        }
 
        private string GetReplacementPhoneme(string phoneme) {
            var langCode = GetLangCode() + "/";
            if (phoneme == langCode + "g" || phoneme == "g") {
                return HasPhoneme(langCode + "G") ? langCode + "G" : "G";
            }
            if (phoneme == langCode + "d" || phoneme == "d") {
                return HasPhoneme(langCode + "D") ? langCode + "D" : "D";
            }
            if (phoneme == langCode + "b" || phoneme == "b") {
                return HasPhoneme(langCode + "B") ? langCode + "B" : "B";
            }
            return phoneme;
        }
    }
}