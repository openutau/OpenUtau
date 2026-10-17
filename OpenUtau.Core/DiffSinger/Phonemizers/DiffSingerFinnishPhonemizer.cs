using System.Collections.Generic;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using System.Linq;

namespace OpenUtau.Core.G2p {
    public class DiffSingerFinnishPhonemizerG2p : IG2p {
        private readonly string[] phonemeTable =
            ["a", "e", "i", "o", "u", "y", "ae", "oe", "ng", "sh", "m", "n", "p", "t", "d", "k", "s", "h", "v", "l", "j", "r", "f", "b", "g", "z"];

        // These work differently for long consonants
        private readonly string[] specialDoubles = 
            ["p", "t", "d", "k", "b", "g"];
        
        private readonly string[] vowels = 
            ["a", "e", "i", "o", "u", "y", "ae", "oe"];
        
        public bool IsGlide(string symbol) => false; // Probably not important I hope
        
        public bool IsValidSymbol(string symbol) => phonemeTable.Contains(symbol);

        public bool IsVowel(string symbol) => vowels.Contains(symbol);

        public bool IsConsnant(string symbol) => !IsVowel(symbol);

        public string[] UnpackHint(string hint, char separator = ' ') {
            return hint.Split(separator)
                .Where(x => phonemeTable.Contains(x))
                .ToArray();
        }

        public string[] Query(string grapheme) {
            if (string.IsNullOrEmpty(grapheme)) return null;
            return Predict(grapheme);
        }

        private int CountMatches(string phonemePart) {
            int count = 0;
            foreach (string phoneme in phonemeTable) {
                if (phoneme.StartsWith(phonemePart)) count++;
            }
            return count;
        }

        private string[]? Predict(string grapheme) {
            List<string> phonemes = new List<string>();
            string phonemePart = "";
            
            int stringIndex = 0;
            foreach (var c in grapheme) {

                // Deal with the case where we have a phoneme that is the prefix of a longer sequence but that doesn't match anything with the suffix from this
                if (phonemePart != "" && CountMatches(phonemePart) > 1 && CountMatches(phonemePart + c) == 0) {
                    phonemes.Add(phonemePart);
                    phonemePart = "";
                }

                // A and O allow compatibility with conventional UTAU style Finnish
                // ä and ö are normal Finnish characters with specific sounds to them
                switch (c) {
                    case 'A':
                    case 'ä':
                        phonemePart += "ae";
                        break;
                    case 'O':
                    case 'ö':
                        phonemePart += "oe";
                        break;
                    default:
                        phonemePart += c;
                        break;
                }
                
                // If we're at the end of the string we should add whatever we have if we can
                if (phonemePart != "" && stringIndex >= grapheme.Length - 1 && CountMatches(phonemePart) > 0) {
                    phonemes.Add(phonemePart);
                    phonemePart = "";
                }

                int matchingPhonemes = CountMatches(phonemePart);

                stringIndex++;
                if (matchingPhonemes == 0) { // Invalid match, there is no possible phoneme, we skip for now, may want to report an error later on
                    phonemePart = "";
                    continue;
                } else if (matchingPhonemes == 1) { // Match found, we add our found phoneme to the list and reset
                    phonemes.Add(phonemePart);
                    phonemePart = "";
                    continue;
                }
            }
            
            // Next we parse out the special doubles
            List<string> finalPhonemes = new List<string>();
            int index = 0;
            while (index < phonemes.Count) {
                if (specialDoubles.Contains(phonemes[index]) && index < phonemes.Count - 1 && phonemes[index] == phonemes[index + 1]) {
                    finalPhonemes.Add("q");
                    index++; // We skip the second phoneme because we already dealt with this double
                }
                finalPhonemes.Add(phonemes[index]);
                index++;
            }
                        
            return (finalPhonemes.Count == 0) ? null : finalPhonemes.ToArray();
        }
    }
}

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Finnish Phonemizer", "DIFFS FI", "Superbox", "FI")]
    public class DiffSingerFinnishPhonemizer : DiffSingerG2pPhonemizer {
        protected override string GetDictionaryName() => "dsdict-fi.yaml";
        
        public override string GetLangCode() => "fi";

        protected override IG2p LoadBaseG2p() => new DiffSingerFinnishPhonemizerG2p();
        
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "e", "i", "o", "u", "y", "ae", "oe"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "ng", "sh", "m", "n", "p", "t", "d", "k", "s", "h", "v", "l", "j", "r", "f", "b", "g", "z", "q"
        };
    }
}
