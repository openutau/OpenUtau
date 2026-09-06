using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using WanaKanaNet;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Filipino to Japanese Phonemizer", "FIL to JA", "Cadlaxa", language: "FIL")]
    public class FILtoJAPhonemizer : ENtoJAPhonemizer {
        protected override string[] GetVowels() => vowels;
        private string[] vowels =
            "a i u e o ay ey oy uy ow aw ew".Split();
        protected override string[] GetConsonants() => consonants;
        private string[] consonants =
            "b by ch d dh f g gy h hy j k ky l ly m my n ny ng p py r ry s sh t ts th v w y z zh".Split();
        protected override string GetDictionaryName() => "";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryPhonemesReplacement;
        private static readonly Dictionary<string, string> dictionaryPhonemesReplacement = new Dictionary<string, string> {
            { "a", "a" },
            { "e", "e" },
            { "o", "o" },
            { "aw", "aw" },
            { "ay", "ay" },
            { "b", "b" },
            { "ch", "ch" },
            { "d", "d" },
            { "ey", "ey" },
            { "f", "f" },
            { "g", "g" },
            { "hh", "h" },
            { "i", "i" },
            { "jh", "j" },
            { "k", "k" },
            { "l", "l" },
            { "m", "m" },
            { "n", "n" },
            { "ng", "ng" },
            { "ow", "ow" },
            { "oy", "oy" },
            { "p", "p" },
            { "q", "-" },
            { "r", "r" },
            { "s", "s" },
            { "sh", "sh" },
            { "t", "t" },
            { "u", "u" },
            { "v", "v" },
            { "w", "w" },
            { "y", "y" },
            { "z", "z" },
            { "zh", "zh" },
        };

        protected override IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();
            //g2ps.Add(new ArpabetPlusG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (note.lyric == "ng") {
                return new string[] { "n", "a", "ng" };
            }
            if (note.lyric == "mga") {
                return new string[] { "m", "a", "ng", "a" };
            }
            if (original == null) {
                string lyric = note.lyric.ToLowerInvariant();
                List<string> fallbackSplit = new List<string>();
                string[] vowels = GetVowels();
                string[] consonants = GetConsonants();

                // Handle apostrophes at the start or end
                bool hasLeadingApostrophe = lyric.StartsWith("'");
                bool hasTrailingApostrophe = lyric.EndsWith("'");

                if (hasLeadingApostrophe) {
                    lyric = lyric.Substring(1);
                }
                if (hasTrailingApostrophe && lyric.Length > 0) {
                    lyric = lyric.Substring(0, lyric.Length - 1);
                }

                int ii = 0;
                while (ii < lyric.Length) {
                    string match = null;
                    foreach (var cons in consonants.OrderByDescending(c => c.Length)) {
                        if (lyric.Substring(ii).StartsWith(cons)) {
                            match = cons;
                            break;
                        }
                    }
                    if (match == null) {
                        foreach (var vow in vowels.OrderByDescending(v => v.Length)) {
                            if (lyric.Substring(ii).StartsWith(vow)) {
                                match = vow;
                                break;
                            }
                        }
                    }
                    if (match != null) {
                        fallbackSplit.Add(match);
                        ii += match.Length;
                    } else {
                        fallbackSplit.Add(lyric[ii].ToString());
                        ii++;
                    }
                }
                // Add "q" at the beginning or end if needed
                if (hasLeadingApostrophe) {
                    fallbackSplit.Insert(0, "q");
                }
                if (hasTrailingApostrophe) {
                    fallbackSplit.Add("q");
                }
                original = fallbackSplit.ToArray();
            }
            List<string> modified = new List<string>();
            string[] diphthongs = new[] { "ay", "ey", "oy", "uy", "aw", "ew", "ow", "iw" };
            foreach (string s in original) {
                if (diphthongs.Contains(s)) {
                    modified.AddRange(new string[] { s[0].ToString(), s[1].ToString() });
                } else {
                    modified.Add(s);
                }
            }
            return modified.ToArray();
        }
    }
}
