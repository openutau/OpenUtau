using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// A base diphone phonemizer for latin languages.
    /// Subclasses override LoadG2p() and LoadVowelFallbacks().
    /// </summary>
    // สืบทอดจาก SyllableBasedPhonemizer เพื่อให้ระบบมองเห็นตัวแปร 'singer'
    public abstract class LatinDiphonePhonemizer : SyllableBasedPhonemizer {

        // ประกาศตัวแปร vowelFallback เพื่อให้ระบบเรียกใช้งานได้
        protected Dictionary<string, string[]> vowelFallback = new Dictionary<string, string[]>();

        // G2p engine สำหรับแปลงคำเป็น phoneme
        private IG2p g2p;

        // Vowel set สำหรับ SyllableBasedPhonemizer
        private static readonly string[] defaultVowels = new[] {
            "aa", "ae", "ah", "ao", "aw", "ay",
            "eh", "er", "ey",
            "ih", "iy",
            "ow", "oy",
            "uh", "uw",
            // European vowels
            "a", "e", "i", "o", "u",
            "ax", "ex", "ee", "oe", "ue", "yy",
            "ooh",
        };

        // Consonant set
        private static readonly string[] defaultConsonants = new[] {
            "b", "ch", "d", "dh", "f", "g", "hh", "jh",
            "k", "l", "m", "n", "ng", "p", "r", "rr",
            "s", "sh", "t", "th", "v", "w", "y", "z", "zh",
            "cc", "x",
        };

        protected override string[] GetVowels() => defaultVowels;
        protected override string[] GetConsonants() => defaultConsonants;

        /// <summary>
        /// โหลด G2p engine สำหรับภาษานั้น ๆ
        /// </summary>
        protected virtual IG2p LoadG2p() => new ArpabetG2p();

        /// <summary>
        /// โหลด vowel fallback mapping
        /// </summary>
        protected virtual Dictionary<string, string[]> LoadVowelFallbacks() {
            return new Dictionary<string, string[]>();
        }

        /// <summary>
        /// เรียกใน constructor เพื่อ init G2p และ fallback
        /// </summary>
        protected void Initialize() {
            try {
                g2p = LoadG2p();
                vowelFallback = LoadVowelFallbacks() ?? new Dictionary<string, string[]>();
            } catch (Exception e) {
                Serilog.Log.Error(e, "LatinDiphonePhonemizer: Failed to initialize G2p.");
                g2p = new ArpabetG2p();
            }
        }

        protected override string[] GetSymbols(Note note) {
            if (g2p == null) return base.GetSymbols(note);
            string lyric = note.lyric?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(lyric)) return Array.Empty<string>();
            var result = g2p.Query(lyric);
            return result ?? base.GetSymbols(note);
        }

        protected override List<string> ProcessSyllable(Syllable syllable) {
            var phonemes = new List<string>();
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;
            int tone = syllable.vowelTone;
            string color = syllable.vowelAttr?.FirstOrDefault().voiceColor ?? string.Empty;
            string alt = syllable.vowelAttr?.FirstOrDefault().alternate?.ToString() ?? string.Empty;

            if (syllable.IsStartingV) {
                phonemes.Add(GetPhonemeOrFallback("-", v, tone, color, alt));
            } else if (syllable.IsVV) {
                var vv = $"{prevV} {v}";
                if (singer.TryGetMappedOto(vv, tone, color, out _)) {
                    phonemes.Add(vv);
                } else {
                    phonemes.Add(GetPhonemeOrFallback(prevV, v, tone, color, alt));
                }
            } else {
                // CC + V
                // VC (สุดท้ายของ cc ก่อน vowel)
                string lastC = cc.Last();
                phonemes.Add(GetPhonemeOrFallback(prevV, lastC, tone, color, alt));
                // CC clusters
                for (int i = 0; i < cc.Length - 1; i++) {
                    phonemes.Add(GetPhonemeOrFallback(cc[i], cc[i + 1], tone, color, alt));
                }
                // CV
                phonemes.Add(GetPhonemeOrFallback(lastC, v, tone, color, alt));
            }
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            var phonemes = new List<string>();
            string prevV = ending.prevV;
            string[] cc = ending.cc;
            int tone = ending.tone;
            string color = ending.attr?.FirstOrDefault().voiceColor ?? string.Empty;
            string alt = ending.attr?.FirstOrDefault().alternate?.ToString() ?? string.Empty;

            if (ending.IsEndingV) {
                // ไม่มีตัวสะกด — ending vowel
                phonemes.Add(GetPhonemeOrFallback(prevV, "-", tone, color, alt));
            } else {
                // VC ending
                phonemes.Add(GetPhonemeOrFallback(prevV, cc[0], tone, color, alt));
                for (int i = 1; i < cc.Length; i++) {
                    phonemes.Add(GetPhonemeOrFallback(cc[i - 1], cc[i], tone, color, alt));
                }
            }
            return phonemes;
        }

        // Helper: ลอง oto ด้วย alt, fallback, และ vowelFallback
        protected virtual string GetPhonemeOrFallback(
            string prevSymbol, string symbol, int tone, string color, string alt) {
            if (!string.IsNullOrEmpty(alt)
                && singer.TryGetMappedOto($"{prevSymbol} {symbol}{alt}", tone, color, out var oto0)) {
                return oto0.Alias;
            }
            if (singer.TryGetMappedOto($"{prevSymbol} {symbol}", tone, color, out var oto1)) {
                return oto1.Alias;
            }
            if (vowelFallback.TryGetValue(symbol, out var fallbacks) && fallbacks != null) {
                foreach (var fb in fallbacks) {
                    if (singer.TryGetMappedOto($"{prevSymbol} {fb}", tone, color, out var oto2)) {
                        return oto2.Alias;
                    }
                }
            }
            if (singer.TryGetMappedOto($"- {symbol}", tone, color, out var oto3)) {
                return oto3.Alias;
            }
            return $"{prevSymbol} {symbol}{alt}";
        }
    }
}