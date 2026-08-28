using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using WanaKanaNet;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Filipino to Japanese Phonemizer", "FIL to JA", "Cadlaxa", language: "FIL")]
    public class FILtoJAPhonemizer : SyllableBasedPhonemizer {
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
            { "r", "r" },
            { "ry", "ry" },
            { "s", "s" },
            { "sh", "sh" },
            { "t", "t" },
            { "q", "-" },
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
            { "l", "る" },
            { "ly", "り" },
            { "m", "む" },
            { "my", "み" },
            { "n", "ん" },
            { "ny", "に" },
            { "ng", "ん" },
            { "p", "ぷ" },
            { "py", "ぴ" },
            { "r", "る" },
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

        protected override List<string> ProcessSyllable(Syllable syllable) {
            // Skip processing if this note extends the prevous syllable
            if (CanMakeAliasExtension(syllable)) {
                return new List<string> { null };
            }

            var prevV = syllable.prevV;
            var cc = syllable.cc;
            var v = syllable.v;
            var phonemes = new List<string>();
            var usingVC = false;

            if (prevV.Length == 0) {
                prevV = "-";
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
                (var hasVc, var vcPhonemes) = HasVc(prevV, cc[0], syllable.tone, cc.Length);
                usingVC = hasVc;
                phonemes.AddRange(vcPhonemes);

                if (usingVC) {
                    start = 1;
                }

                for (var i = start; i < cc.Length - 1; i++) {
                    var cons = SoloConsonant[cc[i]];
                    if (!usingVC) {
                        cons = TryVcv(prevV, cons, syllable.tone);
                    } else {
                        usingVC = false;
                    }
                    if (HasOto(cons, syllable.tone)) {
                        phonemes.Add(cons);
                    } else if (ConditionalAlt.ContainsKey(cons)) {
                        cons = ConditionalAlt[cons];
                        phonemes.Add(TryVcv(prevV, cons, syllable.tone));
                    }
                    prevV = WanaKana.ToRomaji(cons).Last<char>().ToString();
                }
            }

            // Convert to hiragana
            var cv = $"{StartingConsonant[finalCons]}{v}";
            cv = AltCv.ContainsKey(cv) ? AltCv[cv] : cv;
            var hiragana = ToHiragana(cv);
            if (!usingVC) {
                hiragana = TryVcv(prevV, hiragana, syllable.vowelTone);
            } else {
                hiragana = FixCv(hiragana, syllable.vowelTone);
            }

            // Check for nonstandard CV
            var split = false;
            if (HasOto(hiragana, syllable.vowelTone)) {
                phonemes.Add(hiragana);
            } else if (ConditionalAlt.ContainsKey(cv)) {
                cv = ConditionalAlt[cv];
                hiragana = TryVcv(prevV, ToHiragana(cv), syllable.vowelTone);
                if (HasOto(hiragana, syllable.vowelTone)) {
                    phonemes.Add(hiragana);
                } else {
                    split = true;
                }
            } else {
                split = true;
            }

            // Handle nonstandard CV
            if (split && ExtraCv.ContainsKey(cv)) {
                var splitCv = ExtraCv[cv];
                for (var i = 0; i < splitCv.Length; i++) {
                    if (splitCv[i] != prevV) {
                        var converted = ToHiragana(splitCv[i]);
                        phonemes.Add(TryVcv(prevV, converted, syllable.vowelTone));
                        prevV = splitCv[i].Last<char>().ToString();
                    }
                }
            }

            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            var prevV = ending.prevV;
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

            var usingVC = false;
            // Convert to hiragana
            for (var i = 0; i < cc.Length; i++) {
                var symbol = cc[i];

                if (i == 0) {
                    (var hasVc, var vcPhonemes) = HasVc(prevV, symbol, ending.tone, cc.Length + 1);
                    usingVC = hasVc;
                    phonemes.AddRange(vcPhonemes);
                    if (usingVC) {
                        continue;
                    }
                }

                var solo = SoloConsonant[symbol];
                if (!usingVC) {
                    solo = TryVcv(prevV, solo, ending.tone);
                } else {
                    usingVC = false;
                    solo = FixCv(solo, ending.tone);
                }

                if (HasOto(solo, ending.tone)) {
                    phonemes.Add(solo);
                } else if (ConditionalAlt.ContainsKey(solo)) {
                    solo = ConditionalAlt[solo];
                    if (!usingVC) {
                        solo = TryVcv(prevV, solo, ending.tone);
                    } else {
                        solo = FixCv(solo, ending.tone);
                    }
                    phonemes.Add(solo);
                }

                if (solo.Contains("ん")) {
                    if (ending.IsEndingVCWithOneConsonant) {
                        TryAddPhoneme(phonemes, ending.tone, $"n R", $"n -", $"n-");
                    } else if (ending.IsEndingVCWithMoreThanOneConsonant && cc.Last() == "n" || cc.Last() == "ng") {
                        TryAddPhoneme(phonemes, ending.tone, $"n R", $"n -", $"n-");
                    }
                }

                prevV = WanaKana.ToRomaji(solo).Last<char>().ToString();
            }
            
            if (ending.IsEndingV) {
                TryAddPhoneme(phonemes, ending.tone, $"{prevV} R", $"{prevV} -", $"{prevV}-");
            }

            return phonemes;
        }

        private (bool, string[]) HasVc(string vowel, string cons, int tone, int cc) {
            if (vowel == "" || vowel == "-") {
                return (false, new string[0]);
            }

            var phonemes = new List<string>();
            if (cons == "r") {
                cons = "w";
            } else if (cons == "l") {
                cons = "r";
            } else if (cons == "ly") {
                cons = "ry";
            } else {
                cons = StartingConsonant[cons];
            }

            var vc = $"{vowel} {cons}";
            var altVc = $"{vowel} {cons[0]}";
            
            if (HasOto(vc, tone)) {
                phonemes.Add(vc);
            } else if (HasOto(altVc, tone)) {
                phonemes.Add(altVc);
            } else {
                return (false, new string[0]);
            }

            if (affricates.Contains(cons) && cc > 1) {
                phonemes.Add(FixCv(SoloConsonant[cons], tone));
            }

            return (phonemes.Count > 0, phonemes.ToArray());
        }

        private string TryVcv(string vowel, string cv, int tone) {
            var vcv = $"{vowel} {cv}";
            return HasOto(vcv, tone) ? vcv : FixCv(cv, tone);
        }

        private string FixCv(string cv, int tone) {
            var alt = $"- {cv}";
            return HasOto(cv, tone) ? cv : HasOto(alt, tone) ? alt : cv;
        }

        private string ToHiragana(string romaji) {
            var hiragana = WanaKana.ToHiragana(romaji);
            hiragana = hiragana.Replace("ゔ", "ヴ");
            return hiragana;
        }

        // Endings has 50 ticks gap
        protected override bool NoGap => true;

        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            double otoLength = GetTransitionBasicLengthMsByOto(alias, tone, attr);

            var parts = alias.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool isVcv = false;

            if (parts.Length == 2) {
                var startingVowels = new[] { "a", "i", "u", "e", "o", "n", "N", "-" }; 
                var endingVowels = vowels;
                
                // First part must be a vowel (or a rest)
                if (startingVowels.Contains(parts[0])) {
                    string cv = parts[1];
                    
                    // Second part must end in a vowel (Romaji CV) OR be Japanese (Hiragana/Katakana)
                    bool isRomajiVcv = endingVowels.Contains(cv.Last().ToString());
                    bool isJapaneseVcv = cv.Any(c => c > 0xFF);

                    if (isRomajiVcv || isJapaneseVcv) {
                        isVcv = true;
                    }
                }
            }

            if (isVcv) {
                return GetTransitionBasicLengthMsByConstant() * 1.0;
            }
            
            return otoLength;
        }
    }
}
