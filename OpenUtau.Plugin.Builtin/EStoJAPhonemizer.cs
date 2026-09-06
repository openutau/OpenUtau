using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using WanaKanaNet;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Spanish to Japanese Phonemizer", "ES to JA", "Lotte V", language: "ES")]
    public class EStoJAPhonemizer : SyllableBasedPhonemizer {
        /// <summary>
        /// Phonemizer for using Japanese banks for Spanish songs.
        /// Closely based on TUBS' English To Japanese Phonemizer; it has many of the same functions, just tweaked for Spanish.
        /// This phonemizer always uses seseo, because the Japanese "z" is very different from the Spanish "z".
        ///</summary>
        protected override string[] GetVowels() => vowels;
        private static readonly string[] vowels =
            "a i u e o".Split();
        protected override string[] GetConsonants() => consonants;
        private static readonly string[] consonants =
            "b by B By ch d dy D Dy f g gy G Gy h hh hy I j k ky l ly m my n ny p py r ry rr rry s sh t ty ts U w x y Y z".Split();
        protected override string GetDictionaryName() => "cmudict_es.txt";

        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryPhonemesReplacement;
        private static readonly Dictionary<string, string> dictionaryPhonemesReplacement = new Dictionary<string, string> {
            { "a", "a" },
            { "b", "b" },
            { "B", "b" },
            { "ch", "ch" },
            { "d", "d" },
            { "D", "d" },
            { "e", "e" },
            { "f", "f" },
            { "g", "g" },
            { "G", "g" },
            { "hh", "hh" },
            { "i", "i" },
            { "I", "y" },
            { "ll", "j" },
            { "k", "k" },
            { "l", "l" },
            { "m", "m" },
            { "n", "n" },
            { "gn", "ny" },
            { "o", "o" },
            { "p", "p" },
            { "r", "r" },
            { "rr", "rr" },
            { "s", "s" },
            { "t", "t" },
            { "u", "u" },
            { "U", "w" },
            { "w", "w" },
            { "x", "h" },
            { "y", "y" },
            { "Y", "y" },
            { "z", "z" },
        };

        protected override IG2p LoadBaseDictionary() => new SpanishG2p();

        private Dictionary<string, string> StartingConsonant => startingConsonant;
        private static readonly Dictionary<string, string> startingConsonant = new Dictionary<string, string> {
            { "", "" },
            { "b", "b" },
            { "by", "by" },
            { "bw", "bw" },
            { "B", "b" },
            { "By", "by" },
            { "Bw", "bw" },
            { "ch", "ch" },
            { "chy", "chy" },
            { "chw", "chw" },
            { "d", "d" },
            { "dy", "dy" },
            { "dw", "dw" },
            { "D", "d" },
            { "Dy", "dy" },
            { "Dw", "dw" },
            { "f", "f" },
            { "fy", "fy" },
            { "fw", "fw" },
            { "g", "g" },
            { "gy", "gy" },
            { "gw", "gw" },
            { "G", "g" },
            { "Gy", "gy" },
            { "Gw", "gw" },
            { "h", "h" },
            { "hy", "hy" },
            { "hw", "hw" },
            { "j", "j" },
            { "jy", "j" },
            { "jw", "jw" },
            { "k", "k" },
            { "ky", "ky" },
            { "kw", "kw" },
            { "l", "r" },
            { "ly", "ry" },
            { "lw", "rw" },
            { "m", "m" },
            { "my", "my" },
            { "mw", "mw" },
            { "n", "n" },
            { "ny", "ny" },
            { "nw", "nw" },
            { "p", "p" },
            { "py", "py" },
            { "pw", "pw" },
            { "r", "r" },
            { "ry", "ry" },
            { "rw", "rw" },
            { "rr", "rr" },
            { "rry", "rry" },
            { "rrw", "rrw" },
            { "s", "s" },
            { "sy", "sy" },
            { "sw", "sw" },
            { "t", "t" },
            { "ty", "ty" },
            { "tw", "tw" },
            { "w", "w" },
            { "y", "y" },
            { "z", "s" },
            { "zy", "sy" },
            { "zw", "sw" },
        };

        private Dictionary<string, string> SoloConsonant => soloConsonant;
        private static readonly Dictionary<string, string> soloConsonant = new Dictionary<string, string> {
            { "b", "ぶ" },
            { "by", "び" },
            { "bw", "ぶ" },
            { "B", "ぶ" },
            { "By", "ヴぃ" },
            { "Bw", "ぶ" },
            { "ch", "ちゅ" },
            { "chy", "ち" },
            { "chw", "ちゅ" },
            { "d", "ど" },
            { "dy", "でぃ" },
            { "dw", "どぅ" },
            { "D", "ど" },
            { "Dy", "でぃ" },
            { "Dw", "どぅ" },
            { "f", "ふ" },
            { "fy", "ふぃ" },
            { "fw", "ふ" },
            { "g", "ぐ" },
            { "gy", "ぎ" },
            { "gw", "ぐ" },
            { "G", "ぐ" },
            { "Gy", "ぎ" },
            { "Gw", "ぐ" },
            { "h", "ほ" },
            { "hy", "ひ" },
            { "hw", "ほ" },
            { "hh", "息" },
            { "I", "い" },
            { "j", "じゅ" },
            { "jy", "じ" },
            { "jw", "じゅ" },
            { "k", "く" },
            { "ky", "き" },
            { "kw", "く" },
            { "l", "る" },
            { "ly", "り" },
            { "lw", "る" },
            { "m", "ん" },
            { "my", "み" },
            { "mw", "む" },
            { "n", "ん" },
            { "ny", "に" },
            { "nw", "ぬ" },
            { "p", "ぷ" },
            { "py", "ぴ" },
            { "pw", "ぷ" },
            { "r", "る" },
            { "ry", "り" },
            { "rw", "る" },
            { "rr", "る" },
            { "rry", "り" },
            { "rrw", "る" },
            { "s", "す" },
            { "sy", "すぃ" },
            { "sw", "す" },
            { "t", "と" },
            { "ty", "てぃ" },
            { "tw", "とぅ" },
            { "U", "う" },
            { "w", "う" },
            { "y", "い" },
            { "z", "す" },
            { "zy", "すぃ" },
            { "zw", "す" },
        };

        private readonly string[] SpecialClusters = "ky kw gy gw Gy Gw sy sw zy zw jy jw ty tw chy chw dy dw Dy Dw ny nw nyw hy hw by bw By Bw py pw my mw ry rw rry rrw ly lw".Split();

        private Dictionary<string, string> AltCv => altCv;
        private static readonly Dictionary<string, string> altCv = new Dictionary<string, string> {
            {"kwa", "kula" },
            {"kwi", "kuli" },
            {"kwe", "kule" },
            {"kwo", "kulo" },
            {"gwa", "gula" },
            {"gwi", "guli" },
            {"gwe", "gule" },
            {"gwo", "gulo" },
            {"Gwa", "gula" },
            {"Gwi", "guli" },
            {"Gwe", "gule" },
            {"Gwo", "gulo" },
            {"si", "suli" },
            {"sya", "sulya" },
            {"syu", "sulyu" },
            {"sye", "sulile" },
            {"syo", "sulyo" },
            {"swa", "sula" },
            {"swi", "sui" },
            {"swe", "sule" },
            {"swo", "sulo" },
            {"zi", "suli" },
            {"zya", "sulya" },
            {"zyu", "sulyu" },
            {"zye", "sulile" },
            {"zyo", "sulyo" },
            {"zwa", "sula" },
            {"zwi", "sui" },
            {"zwe", "sule" },
            {"zwo", "sulo" },
            {"ti", "teli" },
            {"tya", "telya" },
            {"tyu", "telyu" },
            {"tye", "tele" },
            {"tyo", "telyo" },
            {"tu", "tolu" },
            {"twa", "tola" },
            {"twi", "toli" },
            {"twe", "tole" },
            {"two", "tolo" },
            {"di", "deli" },
            {"dya", "delya" },
            {"dyu", "delyu" },
            {"dye", "dele" },
            {"dyo", "delyo" },
            {"du", "dolu" },
            {"dwa", "dola" },
            {"dwi", "doli" },
            {"dwe", "dole" },
            {"dwo", "dolo" },
            {"Di", "deli" },
            {"Dya", "delya" },
            {"Dyu", "delyu" },
            {"Dye", "dele" },
            {"Dyo", "delyo" },
            {"Du", "dolu" },
            {"Dwa", "dola" },
            {"Dwi", "doli" },
            {"Dwe", "dole" },
            {"Dwo", "dolo" },
            {"nwa", "nula" },
            {"nwi", "nuli" },
            {"nwe", "nule" },
            {"nwo", "nulo" },
            {"hwa", "hola" },
            {"hwi", "holi" },
            {"hwe", "hole" },
            {"hwo", "holo" },
            {"fwa", "fua" },
            {"fwi", "fui" },
            {"fwe", "fue" },
            {"fwo", "fuo" },
            {"bwa", "bula" },
            {"bwi", "buli" },
            {"bwe", "bule" },
            {"bwo", "bulo" },
            {"Bwa", "bula" },
            {"Bwi", "buli" },
            {"Bwe", "bule" },
            {"Bwo", "bulo" },
            {"pwa", "pula" },
            {"pwi", "puli" },
            {"pwe", "pule" },
            {"pwo", "pulo" },
            {"hu", "holu" },
            {"mwa", "mula" },
            {"mwi", "muli" },
            {"mwe", "mule" },
            {"mwo", "mulo" },
            {"yi", "i" },
            {"rwa", "rula" },
            {"rwi", "ruli" },
            {"rwe", "rule" },
            {"rwo", "rulo" },
            {"wu", "u" },
            {"wi", "uli" },
            {"we", "ule" },
            {"wo", "ulo" }, 
        };

        private Dictionary<string, string> ConditionalAlt => conditionalAlt;
        private static readonly Dictionary<string, string> conditionalAlt = new Dictionary<string, string> {
            {"uli", "wi" },
            {"ule", "we" },
            {"ulo", "wo"},
        };

        private Dictionary<string, string[]> ExtraCv => extraCv;
        private static readonly Dictionary<string, string[]> extraCv = new Dictionary<string, string[]> {
            {"rr", new [] { "ru", "ru", "ru" } },
            {"rra", new [] { "ra", "ra", "ra" } },
            {"rri", new [] { "ri", "ri", "ri" } },
            {"rru", new [] { "ru", "ru", "ru" } },
            {"rre", new [] { "re", "re", "re" } },
            {"rro", new [] { "ro", "ro", "ro" } },
            {"rrya", new [] { "ri", "ri", "rya" } },
            {"rryu", new [] { "ri", "ri", "ryu" } },
            {"rrye", new [] { "ri", "ri", "rye" } },
            {"rryo", new [] { "ri", "ri", "ryo" } },
            {"rrwa", new [] { "ru", "ru", "wa" } },
            {"rrwi", new [] { "ru", "ru", "uli" } },
            {"rrwe", new [] { "ru", "ru", "ule" } },
            {"rrwo", new [] { "ru", "ru", "ulo" } },
            {"kye", new [] { "ki", "e" } },
            {"kula", new [] { "ku", "wa" } },
            {"kuli", new [] { "ku", "uli" } },
            {"kule", new [] { "ku", "ule" } },
            {"kulo", new [] { "ku", "ulo" } },
            {"gye", new [] { "gi", "e" } },
            {"gula", new [] { "gu", "wa" } },
            {"guli", new [] { "gu", "uli" } },
            {"gule", new [] { "gu", "ule" } },
            {"gulo", new [] { "gu", "ulo" } },
            {"suli", new [] { "se", "i" } },
            {"sulya", new [] { "suli", "ya" } },
            {"sulyu", new [] { "suli", "yu" } },
            {"sulile", new [] { "suli", "ye" } },
            {"sulyo", new [] { "suli", "yo" } },
            {"sula", new [] { "su", "wa" } },
            {"sui", new [] { "su", "uli" } },
            {"sule", new [] { "su", "ule" } },
            {"sulo", new [] { "su", "ulo" } }, 
            {"je", new [] { "ji", "e" } },
            {"jya", new [] { "ji", "ya" } },
            {"jye", new [] { "ji", "e" } },
            {"jyo", new [] { "ji", "yo" } },
            {"jyu", new [] { "ji", "yu" } },
            {"jwa", new [] { "ju", "wa" } },
            {"jwi", new [] { "ju", "uli" } },
            {"jwe", new [] { "ju", "ule" } },
            {"jwo", new [] { "ju", "ulo" } },
            {"teli", new [] { "te", "i" } },
            {"telya", new [] { "teli", "ya" } },
            {"telyu", new [] { "teli", "yu" } },
            {"tele", new [] { "teli", "ye" } },
            {"telyo", new [] { "teli", "yo" } },
            {"tolu", new [] { "to", "u" } },
            {"tola", new [] { "tolu", "wa" } },
            {"toli", new [] { "tolu", "uli" } },
            {"tole", new [] { "tolu", "ule" } },
            {"tolo", new [] { "tolu", "ulo" } },
            {"che", new [] { "chi", "e" } },
            {"chya", new [] { "chi", "ya" } },
            {"chye", new [] { "chi", "e" } },
            {"chyo", new [] { "chi", "yo" } },
            {"chyu", new [] { "chi", "yu" } },
            {"chwa", new [] { "chu", "wa" } },
            {"chwi", new [] { "chu", "uli" } },
            {"chwe", new [] { "chu", "ule" } },
            {"chwo", new [] { "chu", "ulo" } },
            {"deli", new [] { "de", "i" } },
            {"delya", new [] { "deli", "ya" } },
            {"delyu", new [] { "deli", "yu" } },
            {"dele", new [] { "deli", "ye" } },
            {"delyo", new [] { "deli", "yo" } },
            {"dolu", new [] { "do", "u" } },
            {"dola", new [] { "dolu", "wa" } },
            {"doli", new [] { "dolu", "uli" } },
            {"dole", new [] { "dolu", "ule" } },
            {"dolo", new [] { "dolu", "ulo" } },
            {"nye", new [] { "ni", "e" } },
            {"nula", new [] { "nu", "wa" } },
            {"nuli", new [] { "nu", "uli" } },
            {"nule", new [] { "nu", "ule" } },
            {"nulo", new [] { "nu", "ulo" } },
            {"hye", new [] { "hi", "e" } },
            {"holu", new [] { "ho", "u" } },
            {"hola", new [] { "ho", "wa" } },
            {"holi", new [] { "ho", "uli" } },
            {"hole", new [] { "ho", "ule" } },
            {"holo", new [] { "ho", "ulo" } },
            {"fa", new [] { "fu", "a" } },
            {"fi", new [] { "fu", "i" } },
            {"fe", new [] { "fu", "e" } },
            {"fo", new [] { "fu", "o" } },
            {"fua", new [] { "fu", "wa" } },
            {"fui", new [] { "fu", "uli" } },
            {"fue", new [] { "fu", "ule" } },
            {"fuo", new [] { "fu", "ulo" } },
            {"fya", new [] { "fi", "ya" } },
            {"fyu", new [] { "fi", "yu" } },
            {"fye", new [] { "fi", "ye" } },
            {"fyo", new [] { "fi", "yo" } },
            {"bye", new [] { "bi", "e" } },
            {"bula", new [] { "bu", "wa" } },
            {"buli", new [] { "bu", "uli" } },
            {"bule", new [] { "bu", "ule" } },
            {"bulo", new [] { "bu", "ulo" } },
            {"pye", new [] { "pi", "e" } },
            {"pula", new [] { "pu", "wa" } },
            {"puli", new [] { "pu", "uli" } },
            {"pule", new [] { "pu", "ule" } },
            {"pulo", new [] { "pu", "ulo" } },
            {"mye", new [] { "mi", "e" } },
            {"mula", new [] { "mu", "wa" } },
            {"muli", new [] { "mu", "uli" } },
            {"mule", new [] { "mu", "ule" } },
            {"mulo", new [] { "mu", "ulo" } },
            {"ye", new [] { "i", "e" } },
            {"rye", new [] { "ri", "e" } },
            {"rula", new [] { "ru", "wa" } },
            {"ruli", new [] { "ru", "uli" } },
            {"rule", new [] { "ru", "ule" } },
            {"rulo", new [] { "ru", "ulo" } },
            {"uli", new [] { "u", "i" } },
            {"ule", new [] { "u", "e" } },
            {"ulo", new [] { "u", "o" } },
        };

        private readonly string[] affricates = "ch j".Split();

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
                    } else if (ending.IsEndingVCWithMoreThanOneConsonant && cc.Last() == "n" || cc.Last() == "m") {
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
            if (cons == "rr") {
                cons = "r";
            } else if (cons == "rry") {
                cons = "ry";
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
