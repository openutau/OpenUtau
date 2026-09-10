using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger English+ Phonemizer", "DIFFS EN+", language: "EN", author: "Cadlaxa", engine: "Diffsinger")]
    public class DiffSingerARPAPlusEnglishPhonemizer : DiffSingerG2pPhonemizer
    // cadlaxa here, this diffsinger english phonemizer just uses the ARPA+ G2p so arpasing+ and this phonemizer
    // have same g2p mechanics such as triggering of glottal stop with ('), manual relaxed consonants
    // plus other ds features
    {
        protected override string GetDictionaryName() => "dsdict-en.yaml";
        public override string GetLangCode() => "en";
        protected override IG2p LoadBaseG2p() => new ArpabetPlusG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "aa", "ae", "ah", "ao", "aw", "ax", "ay", "eh", "er",
            "ey","ih", "iy", "ow", "oy","uh", "uw"
        };
        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "ch", "d", "dh", "dr", "dx", "f", "g", "hh", "jh",
            "k", "l", "m", "n", "ng", "p", "q", "r", "s", "sh", "t",
            "th", "tr", "v", "w", "y", "z", "zh"
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

            for (int i = 0; i < phonemes.Count; i++) {
                var tu = phonemes[i];

                // Check for "n dx" sequence and replace it with "n"
                // the actual phoneme for this is "nx" like (winner [w ih nx er])
                if (i < phonemes.Count - 1 && tu.Item1 == langCode + "n" && HasPhoneme(langCode + "n") && phonemes[i + 1].Item1 == langCode + "dx" && HasPhoneme(langCode + "dx")) {
                    // If phoneme "n" and "dx" exist, process "n" and skip "dx"
                    processedPhonemes.Add(new Phoneme() {
                        phoneme = langCode + "n",
                        position = tu.Item2
                    });
                    i++; // Skip next phoneme
                } else if (i < phonemes.Count - 1 && tu.Item1 == "n" && HasPhoneme("n") && phonemes[i + 1].Item1 == "dx" && HasPhoneme("dx") && !HasPhoneme(langCode + "n") && !HasPhoneme(langCode + "dx")) {
                    // If phoneme "n" and "dx" exist, but language-specific "n" and "dx" don't exist, process "n"
                    processedPhonemes.Add(new Phoneme() {
                        phoneme = "n",
                        position = tu.Item2
                    });
                    i++;
                } else if (ShouldReplacePhoneme(tu.Item1, prev, next, prevNeighbour, nextNeighbour, out string replacement)) {
                    // If phoneme should be replaced, process the replacement
                    processedPhonemes.Add(new Phoneme() {
                        phoneme = replacement,
                        position = tu.Item2
                    });
                } else {
                    // If no conditions are met, just add the current phoneme
                    processedPhonemes.Add(new Phoneme() {
                        phoneme = tu.Item1,
                        position = tu.Item2
                    });
                }
            }
            return new Result {
                phonemes = processedPhonemes.ToArray()
            };
        }

        private bool ShouldReplacePhoneme(string phoneme, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, out string replacement) {
            replacement = phoneme;
            var langCode = GetLangCode() + "/";

            if ((phoneme == langCode + "cl" || !HasPhoneme("q")) && HasPhoneme("vf")) {
                if (!prevNeighbour.HasValue || string.IsNullOrWhiteSpace(prevNeighbour.Value.lyric)) {
                    replacement = "vf";
                    return true;
                }
            }
            if ((phoneme == langCode + "q" || phoneme == "q") && HasPhoneme("vf")) {
                if (!prevNeighbour.HasValue || string.IsNullOrWhiteSpace(prevNeighbour.Value.lyric)) {
                    replacement = "vf";
                    return true;
                }
            }
            if ((phoneme == langCode + "q" || phoneme == "q") && HasPhoneme("cl")) {
                replacement = "cl";
                return true;
            }
            if (phoneme == langCode + "q" && !HasPhoneme("cl")) {
                replacement = "q";
                return true;
            }
            if (phoneme == langCode + "q" && !HasPhoneme("cl") && HasPhoneme(langCode + "q")) {
                replacement = langCode + "q";  // Keep the language-specific "q"
                return true;
            }
            if (phoneme == "ax" && !HasPhoneme("ax")) {
                return true;
            }
            if (phoneme == langCode + "ax" && !HasPhoneme(langCode + "ax")) {
                replacement = langCode + "ah";  // Replace language-specific "ax" with "ah"
                return true;
            }

            return false; 
        }

    }
}
