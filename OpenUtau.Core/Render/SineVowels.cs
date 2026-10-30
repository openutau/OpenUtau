using System;
using System.Collections.Concurrent;
using System.Linq;
using OpenUtau.Core.G2p;
using WanaKanaNet;

namespace OpenUtau.Core.Render {
    internal static class SineVowels {
        const string VowelLetters = "aiueo";

        static readonly object g2pLock = new object();
        static JapaneseMonophoneG2p g2p;
        static readonly ConcurrentDictionary<string, (char? vowel, bool bareVowel)> cache =
            new ConcurrentDictionary<string, (char?, bool)>();

        internal static (char? vowel, bool bareVowel) Analyze(string lyric) {
            if (string.IsNullOrWhiteSpace(lyric)) {
                return (null, false);
            }
            return cache.GetOrAdd(lyric.Trim(), key => {
                var phonemes = QueryPhonemes(key);
                if (phonemes == null || phonemes.Length == 0) {
                    return (null, false);
                }
                string last = phonemes.Last();
                if (last.Length != 1 || !VowelLetters.Contains(last[0])) {
                    return (null, false);
                }
                return ((char?)(last[0]), phonemes.Length == 1);
            });
        }

        internal static char? GetVowel(string lyric) => Analyze(lyric).vowel;

        internal static bool IsBareVowel(string lyric) => Analyze(lyric).bareVowel;

        static string[] QueryPhonemes(string lyric) {
            var hiragana = NormalizeToHiragana(lyric);
            lock (g2pLock) {
                if (g2p == null) {
                    g2p = new JapaneseMonophoneG2p();
                }
                if (hiragana != null) {
                    var phonemes = g2p.Query(hiragana);
                    if (phonemes != null) {
                        return phonemes;
                    }
                }
                return g2p.Query(lyric);
            }
        }

        internal static string NormalizeToHiragana(string lyric) {
            lyric = lyric.Trim().ToLowerInvariant();
            if (lyric.Length == 0 || lyric.Length > 4) {
                return null;
            }
            if (WanaKana.IsHiragana(lyric) || WanaKana.IsKatakana(lyric)) {
                if (!WanaKana.IsKana(lyric)) {
                    return null;
                }
                return lyric.Length <= 3 ? WanaKana.ToHiragana(lyric) : null;
            }
            if (!WanaKana.IsRomaji(lyric)) {
                return null;
            }
            string hiragana = WanaKana.ToHiragana(lyric);
            return WanaKana.ToRomaji(hiragana) == lyric ? hiragana : null;
        }
    }
}
