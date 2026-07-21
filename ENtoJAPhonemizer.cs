/*
 * Made And Checked By DELTA SYNTH & Gemini AI
 * Original by Patiphat Wongyai
 * Version: v.2.2
 * History/Summary: ยกระดับ Phonemizer ตามหลักไวยากรณ์เจ้าของภาษา ปรับความลื่นไหลของเสียงและป้องกัน Overlap
 */
#nullable enable
// Made And Checked By DELTA SYNTH & Gemini AI
// Original by TUBS

// วันที่แก้ไข: 2026-06-17
// เวอร์ชั่น: 2.1
// หน้าที่: ระบบแปลงเสียงร้องภาษาอังกฤษ ให้เข้ากับชุดสัทอักษรภาษาญี่ปุ่น (Romaji/Hiragana) โดยอัตโนมัติ 
// พร้อมรองรับ UI ภาษาไทยและอังกฤษ (EN/TH)

using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using WanaKanaNet;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("English to Japanese Phonemizer (EN/TH) v.2.2", "EN to JA", "TUBS (Upgraded)", language: "UTAU")]
    // Version: v
    public class ENtoJAPhonemizer : SyllableBasedPhonemizer {
        protected override string[] GetVowels() => vowels;
        private static readonly string[] vowels = "a i u e o ay ey oy ow aw".Split();

        protected override string[] GetConsonants() => consonants;
        private static readonly string[] consonants = "b by ch d dh f g gy h hy j k ky l ly m my n ny ng p py r ry s sh t ts th v w y z zh".Split();

        protected override string GetDictionaryName() => "cmudict-0_7b.txt";

        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryPhonemesReplacement;
        private static readonly Dictionary<string, string> dictionaryPhonemesReplacement = new Dictionary<string, string> {
            { "aa", "a" }, { "ae", "e" }, { "ah", "a" }, { "ao", "o" }, { "aw", "aw" }, { "ay", "ay" },
            { "b", "b" }, { "ch", "ch" }, { "d", "d" }, { "dh", "dh" }, { "eh", "e" }, { "er", "o" },
            { "ey", "ey" }, { "f", "f" }, { "g", "g" }, { "hh", "h" }, { "ih", "e" }, { "iy", "i" },
            { "jh", "j" }, { "k", "k" }, { "l", "l" }, { "m", "m" }, { "n", "n" }, { "ng", "ng" },
            { "ow", "ow" }, { "oy", "oy" }, { "p", "p" }, { "r", "r" }, { "s", "s" }, { "sh", "sh" },
            { "t", "t" }, { "th", "th" }, { "uh", "o" }, { "uw", "u" }, { "v", "v" }, { "w", "w" },
            { "y", "y" }, { "z", "z" }, { "zh", "zh" }
        };

        protected override IG2p LoadBaseDictionary() => new ArpabetG2p();

        private Dictionary<string, string> StartingConsonant => startingConsonant;
        private static readonly Dictionary<string, string> startingConsonant = new Dictionary<string, string> {
            { "", "" }, { "b", "b" }, { "by", "by" }, { "ch", "ch" }, { "d", "d" }, { "dh", "d" },
            { "f", "f" }, { "g", "g" }, { "gy", "gy" }, { "h", "h" }, { "hy", "hy" }, { "j", "j" },
            { "k", "k" }, { "ky", "ky" }, { "l", "r" }, { "ly", "ry" }, { "m", "m" }, { "my", "my" },
            { "n", "n" }, { "ny", "ny" }, { "ng", "n" }, { "p", "p" }, { "py", "py" }, { "r", "rr" },
            { "ry", "ry" }, { "s", "s" }, { "sh", "sh" }, { "t", "t" }, { "ts", "ts" }, { "th", "s" },
            { "v", "v" }, { "w", "w" }, { "y", "y" }, { "z", "z" }, { "zh", "sh" }
        };

        private Dictionary<string, string> SoloConsonant => soloConsonant;
        private static readonly Dictionary<string, string> soloConsonant = new Dictionary<string, string> {
            { "b", "ぶ" }, { "by", "び" }, { "ch", "ちゅ" }, { "d", "ど" }, { "dh", "ず" }, { "f", "ふ" },
            { "g", "ぐ" }, { "gy", "ぎ" }, { "h", "ほ" }, { "hy", "ひ" }, { "j", "じゅ" }, { "k", "く" },
            { "ky", "き" }, { "l", "う" }, { "ly", "り" }, { "m", "む" }, { "my", "み" }, { "n", "ん" },
            { "ny", "に" }, { "ng", "ん" }, { "p", "ぷ" }, { "py", "ぴ" }, { "r", "う" }, { "ry", "り" },
            { "s", "す" }, { "sh", "しゅ" }, { "t", "と" }, { "ts", "つ" }, { "th", "す" }, { "v", "ヴ" },
            { "w", "う" }, { "y", "い" }, { "z", "ず" }, { "zh", "しゅ" }
        };

        private string[] SpecialClusters = "ky gy ts ny hy by py my ry ly".Split();

        private Dictionary<string, string> AltCv => altCv;
        private static readonly Dictionary<string, string> altCv = new Dictionary<string, string> {
            {"si", "suli" }, {"zi", "zuli" }, {"ti", "teli" }, {"tu", "tolu" }, {"di", "deli" },
            {"du", "dolu" }, {"hu", "holu" }, {"yi", "i" }, {"wu", "u" }, {"wo", "ulo" },
            {"rra", "wa" }, {"rri", "wi" }, {"rru", "ru" }, {"rre", "we" }, {"rro", "ulo" }
        };

        private Dictionary<string, string> ConditionalAlt => conditionalAlt;
        private static readonly Dictionary<string, string> conditionalAlt = new Dictionary<string, string> {
            {"ulo", "wo"}, {"va", "fa"}, {"vi", "fi"}, {"vu", "fu"}, {"ヴ", "ふ"}, {"ve", "fe"}, {"vo", "fo"}
        };

        private Dictionary<string, string[]> ExtraCv => extraCv;
        private static readonly Dictionary<string, string[]> extraCv = new Dictionary<string, string[]> {
            {"kye", new [] { "ki", "e" } }, {"gye", new [] { "gi", "e" } }, {"suli", new [] { "se", "i" } },
            {"she", new [] { "si", "e" } }, {"zuli", new [] { "ze", "i" } }, {"je", new [] { "ji", "e" } },
            {"teli", new [] { "te", "i" } }, {"tolu", new [] { "to", "u" } }, {"che", new [] { "chi", "e" } },
            {"tsa", new [] { "tsu", "a" } }, {"tsi", new [] { "tsu", "i" } }, {"tse", new [] { "tsu", "e" } },
            {"tso", new [] { "tsu", "o" } }, {"deli", new [] { "de", "i" } }, {"dolu", new [] { "do", "u" } },
            {"nye", new [] { "ni", "e" } }, {"hye", new [] { "hi", "e" } }, {"holu", new [] { "ho", "u" } },
            {"fa", new [] { "fu", "a" } }, {"fi", new [] { "fu", "i" } }, {"fe", new [] { "fu", "e" } },
            {"fo", new [] { "fu", "o" } }, {"bye", new [] { "bi", "e" } }, {"pye", new [] { "pi", "e" } },
            {"mye", new [] { "mi", "e" } }, {"ye", new [] { "i", "e" } }, {"rye", new [] { "ri", "e" } },
            {"wi", new [] { "u", "i" } }, {"we", new [] { "u", "e" } }, {"ulo", new [] { "u", "o" } }
        };

        private string[] affricates = "ts ch j".Split();

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (original == null) return null!;
            List<string> modified = new List<string>();
            string[] diphthongs = new[] { "ay", "ey", "oy", "ow", "aw" };
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
            if (CanMakeAliasExtension(syllable)) return new List<string> { null! };

            var prevV = string.IsNullOrEmpty(syllable.prevV) ? "-" : syllable.prevV;
            var cc = syllable.cc;
            var v = syllable.v;
            var phonemes = new List<string>();
            var usingVC = false;

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

            var finalCons = "";
            if (cc.Length > 0) {
                finalCons = cc[cc.Length - 1];
                var start = 0;
                (var hasVc, var vcPhonemes) = HasVc(prevV, cc[0], syllable.tone, cc.Length);
                usingVC = hasVc;
                phonemes.AddRange(vcPhonemes);

                if (usingVC) start = 1;

                for (var i = start; i < cc.Length - 1; i++) {
                    var cons = SoloConsonant[cc[i]];
                    if (!usingVC) cons = TryVcv(prevV, cons, syllable.tone);
                    else usingVC = false;

                    if (HasOto(cons, syllable.tone)) phonemes.Add(cons);
                    else if (ConditionalAlt.TryGetValue(cons, out var altCons)) {
                        phonemes.Add(TryVcv(prevV, altCons, syllable.tone));
                        cons = altCons;
                    }
                    prevV = WanaKana.ToRomaji(cons).Last().ToString();
                }
            }

            var cv = $"{StartingConsonant[finalCons]}{v}";
            cv = AltCv.TryGetValue(cv, out var altCvValue) ? altCvValue : cv;
            var hiragana = ToHiragana(cv);

            hiragana = !usingVC ? TryVcv(prevV, hiragana, syllable.vowelTone) : FixCv(hiragana, syllable.vowelTone);

            var split = false;
            if (HasOto(hiragana, syllable.vowelTone)) phonemes.Add(hiragana);
            else if (ConditionalAlt.TryGetValue(cv, out var condCv)) {
                hiragana = TryVcv(prevV, ToHiragana(condCv), syllable.vowelTone);
                if (HasOto(hiragana, syllable.vowelTone)) phonemes.Add(hiragana);
                else split = true;
                cv = condCv;
            } else split = true;

            if (split && ExtraCv.TryGetValue(cv, out var splitCv)) {
                for (var i = 0; i < splitCv.Length; i++) {
                    if (splitCv[i] != prevV) {
                        var converted = ToHiragana(splitCv[i]);
                        phonemes.Add(TryVcv(prevV, converted, syllable.vowelTone));
                        prevV = splitCv[i].Last().ToString();
                    }
                }
            }
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            var prevV = ending.prevV;
            var cc = ending.cc;
            var phonemes = new List<string>();

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
            for (var i = 0; i < cc.Length; i++) {
                var symbol = cc[i];
                if (i == 0) {
                    (var hasVc, var vcPhonemes) = HasVc(prevV, symbol, ending.tone, cc.Length + 1);
                    usingVC = hasVc;
                    phonemes.AddRange(vcPhonemes);
                    if (usingVC) continue;
                }

                var solo = SoloConsonant[symbol];
                solo = !usingVC ? TryVcv(prevV, solo, ending.tone) : FixCv(solo, ending.tone);
                usingVC = false;

                if (HasOto(solo, ending.tone)) phonemes.Add(solo);
                else if (ConditionalAlt.TryGetValue(solo, out var altSolo)) {
                    solo = !usingVC ? TryVcv(prevV, altSolo, ending.tone) : FixCv(altSolo, ending.tone);
                    phonemes.Add(solo);
                }

                if (solo.Contains("ん")) {
                    if (ending.IsEndingVCWithOneConsonant || (ending.IsEndingVCWithMoreThanOneConsonant && (cc.Last() == "n" || cc.Last() == "ng"))) {
                        TryAddPhoneme(phonemes, ending.tone, "n R", "n -", "n-");
                    }
                }
                prevV = WanaKana.ToRomaji(solo).Last().ToString();
            }

            if (ending.IsEndingV) {
                TryAddPhoneme(phonemes, ending.tone, $"{prevV} R", $"{prevV} -", $"{prevV}-");
            }
            return phonemes;
        }

        private (bool, string[]) HasVc(string vowel, string cons, int tone, int cc) {
            if (string.IsNullOrEmpty(vowel) || vowel == "-") return (false, Array.Empty<string>());
            var phonemes = new List<string>();
            cons = cons == "r" ? "w" : cons == "l" ? "r" : cons == "ly" ? "ry" : StartingConsonant[cons];

            var vc = $"{vowel} {cons}";
            var altVc = $"{vowel} {cons[0]}";

            if (HasOto(vc, tone)) phonemes.Add(vc);
            else if (HasOto(altVc, tone)) phonemes.Add(altVc);
            else return (false, Array.Empty<string>());

            if (affricates.Contains(cons) && cc > 1) phonemes.Add(FixCv(SoloConsonant[cons], tone));
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

        private string ToHiragana(string romaji) => WanaKana.ToHiragana(romaji).Replace("ゔ", "ヴ");
    }
}
