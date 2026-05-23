using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.G2p {
    public class RuleBasedFilipinoG2p : IG2p {
        readonly static Regex kAllPunct = new Regex(@"^[\p{P}]$");

        private string[] validPhonemes =
            { "m", "n", "ng", "p", "t", "ty", "k", "q", "b", "d", "dy", "g", "s", "sy", "h", "l", "y", "w", "r", "vf", "a", "e", "i", "o", "u" };

        private readonly string[] glides = { "w", "y" };

        private string[] vowels = { "a", "e", "i", "o", "u" };

        public bool IsGlide(string symbol) => glides.Contains(symbol);

        public bool IsValidSymbol(string symbol) => validPhonemes.Contains(symbol);

        public bool IsVowel(string symbol) => vowels.Contains(symbol);

        bool IsConsonant(string symbol) => !symbol.Equals("|") && !IsVowel(symbol);

        public string[] UnpackHint(string hint, char separator = ' ') {
            return hint.Split(separator)
                .Where(x => validPhonemes.Contains(x))
                .ToArray();
        }

        public string[] Query(string grapheme) {
            if (string.IsNullOrEmpty(grapheme) || kAllPunct.IsMatch(grapheme)) {
                return null;
            }
            return Predict(grapheme);
        }

        string[]? Predict(string grapheme) {
            grapheme = grapheme.ToLower(new CultureInfo("fil-PH"));
            if (grapheme.Equals("mga")) grapheme = "manga";
            if (grapheme.Equals("ng")) grapheme = "nang";
            List<string> phonemes = new List<string>();
            foreach (var c in grapheme) {
                var prev = phonemes.LastOrDefault("");
                switch (c) {
                    case 'a':
                    case 'o':
                    case 'u':
                        if (prev.Equals("c")) phonemes[^1] = "k";
                        phonemes.Add(c.ToString());
                        break;
                    case 'e':
                        if (prev.Equals("c")) phonemes[^1] = "s";
                        phonemes.Add("e");
                        break;
                    case 'i':
                        if (prev.Equals("c")) phonemes[^1] = "sy";
                        else phonemes.Add("i");
                        break;
                    case 'f':
                        phonemes.Add("p");
                        break;
                    case 'g':
                        if (prev.Equals("n")) phonemes[^1] = "ng";
                        else phonemes.Add("g");
                        break;
                    case 'h':
                        if (prev.Equals("c")) phonemes[^1] = "ty";
                        else phonemes.Add("h");
                        break;
                    case 'j':
                        phonemes.Add("dy");
                        break;
                    case 'ñ':
                        phonemes.Add("n");
                        phonemes.Add("y");
                        break;
                    case 's':
                        if (prev.Equals("t")) phonemes[^1] = "ty";
                        else phonemes.Add("s");
                        break;
                    case 'y':
                        if (prev.Equals("t") || prev.Equals("d") || prev.Equals("s"))
                            phonemes[^1] = prev + "y";
                        else phonemes.Add("y");
                        break;
                    case 'z':
                        phonemes.Add("s");
                        break;
                    case '-':
                        phonemes.Add("q");
                        break;
                    case '\'':
                        phonemes.Add("vf");
                        break;
                    default:
                        phonemes.Add(c.ToString());
                        break;
                }
            }
            
            // glide pass
            for (int i = 1; i < phonemes.Count - 1; i++) {
                string prev = phonemes[i - 1];
                string curr = phonemes[i];
                string next = phonemes[i + 1];
                phonemes[i] = curr switch {
                    "u" when IsConsonant(prev) && IsVowel(next) => "w",
                    "i" when IsConsonant(prev) && IsVowel(next) => "y",
                    _ => phonemes[i]
                };
            }
            
            // extra palatalization pass
            

            string[] filteredPhonemes = phonemes.Where(x => validPhonemes.Contains(x)).ToArray();

            return (filteredPhonemes.Length == 0) ? null : filteredPhonemes;
        }
    }
}

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Rule-based Filipino Phonemizer", "DIFFS FIL", "UtaUtaUtau", "FIL")]
    public class DiffSingerRuleBasedFilipinoPhonemizer : DiffSingerG2pPhonemizer {
        protected override string GetDictionaryName() => "dsdict-fil.yaml";

        public override string GetLangCode() => "fil";

        protected override IG2p LoadBaseG2p() => new RuleBasedFilipinoG2p();

        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "e", "i", "o", "u"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "m", "n", "ng", "p", "t", "ty", "k", "q", "b", "d", "dy", "g", "s", "sy", "h", "l", "y", "w", "r", "vf"
        };
    }
}
