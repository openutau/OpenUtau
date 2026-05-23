using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using WanaKanaNet;

namespace OpenUtau.Core.Voicevox {
    [Phonemizer("Simple Voicevox ENtoJA Phonemizer", "S-VOICEVOX EN to JA", "TUBS & ROKU10SHI", language: "UTAU")]
    public class SimpleVoicevoxENtoJAPhonemizer : SyllableBasedPhonemizer {

        protected VoicevoxSinger singer;

        public override void SetSinger(USinger singer) {
            base.SetSinger(singer);
            this.singer = singer as VoicevoxSinger;
            if (this.singer != null) {
                VoicevoxUtils.Loaddic(this.singer);
            }
        }
        protected override string[] GetVowels() => vowels;
        private static readonly string[] vowels =
            "a i u e o ay ey oy ow aw".Split();
        protected override string[] GetConsonants() => consonants;
        private static readonly string[] consonants =
            "b by ch d dh f g gy h hy j k ky l ly m my n ny ng p py r ry s sh t ts th v w y z zh".Split();
        protected override string GetDictionaryName() => "cmudict-0_7b.txt";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryPhonemesReplacement;
        private static readonly Dictionary<string, string> dictionaryPhonemesReplacement = new Dictionary<string, string> {
            { "aa", "a" },
            { "ae", "e" },
            { "ah", "a" },
            { "ao", "o" },
            { "aw", "aw" },
            { "ay", "ay" },
            { "b", "b" },
            { "ch", "ch" },
            { "d", "d" },
            { "dh", "dh" },
            { "eh", "e" },
            { "er", "o" },
            { "ey", "ey" },
            { "f", "f" },
            { "g", "g" },
            { "hh", "h" },
            { "ih", "e" },
            { "iy", "i" },
            { "jh", "j" },
            { "k", "k" },
            { "l", "l" },
            { "m", "m" },
            { "n", "n" },
            { "ng", "ng" },
            { "ow", "ow" },
            { "oy", "oy" },
            { "p", "p" },
            { "r", "r" },
            { "s", "s" },
            { "sh", "sh" },
            { "t", "t" },
            { "th", "th" },
            { "uh", "o" },
            { "uw", "u" },
            { "v", "v" },
            { "w", "w" },
            { "y", "y" },
            { "z", "z" },
            { "zh", "zh" },
        };

        protected override IG2p LoadBaseDictionary() => new ArpabetG2p();

        private Dictionary<string, string> StartingConsonant => startingConsonant;
        private static readonly Dictionary<string, string> startingConsonant = new Dictionary<string, string> {
            { "", "" },
            { "b", "b" },
            { "by", "by" },
            { "ch", "ch" },
            { "d", "d" },
            { "dh", "d" },
            { "f", "f" },
            { "g", "g" },
            { "gy", "gy" },
            { "h", "h" },
            { "hy", "hy" },
            { "j", "j" },
            { "k", "k" },
            { "ky", "ky" },
            { "l", "r" },
            { "ly", "ry" },
            { "m", "m" },
            { "my", "my" },
            { "n", "n" },
            { "ny", "ny" },
            { "ng", "n" },
            { "p", "p" },
            { "py", "py" },
            { "r", "rr" },
            { "ry", "ry" },
            { "s", "s" },
            { "sh", "sh" },
            { "t", "t" },
            { "ts", "ts" },
            { "th", "s" },
            { "v", "v" },
            { "w", "w" },
            { "y", "y" },
            { "z", "z" },
            { "zh", "sh" },
        };

        private Dictionary<string, string> SoloConsonant => soloConsonant;
        private static readonly Dictionary<string, string> soloConsonant = new Dictionary<string, string> {
            { "b", "ぶ" },
            { "by", "び" },
            { "ch", "ちゅ" },
            { "d", "ど" },
            { "dh", "ず" },
            { "f", "ふ" },
            { "g", "ぐ" },
            { "gy", "ぎ" },
            { "h", "ほ" },
            { "hy", "ひ" },
            { "j", "じゅ" },
            { "k", "く" },
            { "ky", "き" },
            { "l", "う" },
            { "ly", "り" },
            { "m", "む" },
            { "my", "み" },
            { "n", "ん" },
            { "ny", "に" },
            { "ng", "ん" },
            { "p", "ぷ" },
            { "py", "ぴ" },
            { "r", "う" },
            { "ry", "り" },
            { "s", "す" },
            { "sh", "しゅ" },
            { "t", "と" },
            { "ts", "つ" },
            { "th", "す" },
            { "v", "ヴ" },
            { "w", "う" },
            { "y", "い" },
            { "z", "ず" },
            { "zh", "しゅ" },
        };

        private string[] SpecialClusters = "ky gy ts ny hy by py my ry ly".Split();

        private Dictionary<string, string> AltCv => altCv;
        private static readonly Dictionary<string, string> altCv = new Dictionary<string, string> {
            {"si", "suli" },
            {"zi", "zuli" },
            {"ti", "teli" },
            {"tu", "tolu" },
            {"di", "deli" },
            {"du", "dolu" },
            {"hu", "holu" },
            {"yi", "i" },
            {"wu", "u" },
            {"wo", "ulo" },
            {"rra", "wa" },
            {"rri", "wi" },
            {"rru", "ru" },
            {"rre", "we" },
            {"rro", "ulo" },
        };

        private Dictionary<string, string> ConditionalAlt => conditionalAlt;
        private static readonly Dictionary<string, string> conditionalAlt = new Dictionary<string, string> {
            {"ulo", "wo"},
            {"va", "fa"},
            {"vi", "fi"},
            {"vu", "fu"},
            {"ヴ", "ふ"},
            {"ve", "fe"},
            {"vo", "fo"},
        };

        private Dictionary<string, string[]> ExtraCv => extraCv;
        private static readonly Dictionary<string, string[]> extraCv = new Dictionary<string, string[]> {
            {"kye", new [] { "ki", "e" } },
            {"gye", new [] { "gi", "e" } },
            {"suli", new [] { "se", "i" } },
            {"she", new [] { "si", "e" } },
            {"zuli", new [] { "ze", "i" } },
            {"je", new [] { "ji", "e" } },
            {"teli", new [] { "te", "i" } },
            {"tolu", new [] { "to", "u" } },
            {"che", new [] { "chi", "e" } },
            {"tsa", new [] { "tsu", "a" } },
            {"tsi", new [] { "tsu", "i" } },
            {"tse", new [] { "tsu", "e" } },
            {"tso", new [] { "tsu", "o" } },
            {"deli", new [] { "de", "i" } },
            {"dolu", new [] { "do", "u" } },
            {"nye", new [] { "ni", "e" } },
            {"hye", new [] { "hi", "e" } },
            {"holu", new [] { "ho", "u" } },
            {"fa", new [] { "fu", "a" } },
            {"fi", new [] { "fu", "i" } },
            {"fe", new [] { "fu", "e" } },
            {"fo", new [] { "fu", "o" } },
            {"bye", new [] { "bi", "e" } },
            {"pye", new [] { "pi", "e" } },
            {"mye", new [] { "mi", "e" } },
            {"ye", new [] { "i", "e" } },
            {"rye", new [] { "ri", "e" } },
            {"wi", new [] { "u", "i" } },
            {"we", new [] { "u", "e" } },
            {"ulo", new [] { "u", "o" } },
        };

        private string[] affricates = "ts ch j".Split();

        protected override string[] GetSymbols(Note note) {
            List<string> modified = new List<string>();
            if (VoicevoxUtils.phoneme_List.paus.TryGetValue(note.lyric,out string str)) {
                modified.Add(str);
            } else {
                string[] original = base.GetSymbols(note);
                if (original == null) {
                    return null;
                }
                string[] diphthongs = new[] { "ay", "ey", "oy", "ow", "aw" };
                foreach (string s in original) {
                    if (diphthongs.Contains(s)) {
                        modified.AddRange(new string[] { s[0].ToString(), s[1].ToString() });
                    } else {
                        modified.Add(s);
                    }
                }
            }
            return modified.ToArray();
        }

        protected override List<string> ProcessSyllable(Syllable syllable) {
            // Skip processing if this note extends the prevous syllable
            if (CanMakeAliasExtension(syllable)) {
                return new List<string> { null };
            }

            var cc = syllable.cc;
            var v = syllable.v;
            var phonemes = new List<string>();
            if (VoicevoxUtils.phoneme_List.paus.TryGetValue(v, out string str)) {
                phonemes.Add(str);
                return phonemes;
            }

                // Check CCs for special clusters
                var adjustedCC = new List<string>();
            for (var i = 0; i < cc.Length; i++) {
                if (i == cc.Length - 1) {
                    adjustedCC.Add(cc[i]);
                } else {
                    if (cc[i] == cc[i + 1]) {
                        adjustedCC.Add(cc[i]);
                        i++;
                        continue;
                    }
                    var diphone = $"{cc[i]}{cc[i + 1]}";
                    if (SpecialClusters.Contains(diphone)) {
                        adjustedCC.Add(diphone);
                        i++;
                    } else {
                        adjustedCC.Add(cc[i]);
                    }
                }
            }
            cc = adjustedCC.ToArray();

            // Separate CCs and main CV
            var finalCons = "";
            if (cc.Length > 0) {
                finalCons = cc[cc.Length - 1];

                var start = 0;

                for (var i = start; i < cc.Length - 1; i++) {
                    var cons = SoloConsonant[cc[i]];
                    if (HasOto(cons, syllable.tone)) {
                        phonemes.Add(cons);
                    }
                }
            }

            // Convert to hiragana
            var cv = $"{StartingConsonant[finalCons]}{v}";
            cv = AltCv.ContainsKey(cv) ? AltCv[cv] : cv;
            var hiragana = ToHiragana(cv);

            // Check for nonstandard CV
            var split = false;
            if (HasOto(hiragana, syllable.vowelTone)) {
                phonemes.Add(hiragana);
            } else {
                split = true;
            }
            // Handle nonstandard CV
            if (split && ExtraCv.ContainsKey(cv)) {
                var splitCv = ExtraCv[cv];
                for (var i = 0; i < splitCv.Length; i++) {
                    var converted = ToHiragana(splitCv[i]);
                    phonemes.Add(converted);
                }
            }

            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            var cc = ending.cc;
            var phonemes = new List<string>();

            // Check CCs for special clusters
            var adjustedCC = new List<string>();
            for (var i = 0; i < cc.Length; i++) {
                if (i == cc.Length - 1) {
                    adjustedCC.Add(cc[i]);
                } else {
                    if (cc[i] == cc[i + 1]) {
                        adjustedCC.Add(cc[i]);
                        i++;
                        continue;
                    }
                    var diphone = $"{cc[i]}{cc[i + 1]}";
                    if (SpecialClusters.Contains(diphone)) {
                        adjustedCC.Add(diphone);
                        i++;
                    } else {
                        adjustedCC.Add(cc[i]);
                    }
                }
            }
            cc = adjustedCC.ToArray();

            // Convert to hiragana
            for (var i = 0; i < cc.Length; i++) {
                var symbol = cc[i];

                var solo = SoloConsonant[symbol];

                if (HasOto(solo, ending.tone)) {
                    phonemes.Add(solo);
                } else if (ConditionalAlt.ContainsKey(solo)) {
                    solo = ConditionalAlt[solo];
                    phonemes.Add(solo);
                }
            }

            return phonemes;
        }

        private string ToHiragana(string romaji) {
            var hiragana = WanaKana.ToHiragana(romaji);
            return hiragana;
        }
    }
}
