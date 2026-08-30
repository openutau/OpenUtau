using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    /// <summary>
    /// Phoneme-to-ID mapping used by NEUTRINO Tau v3.x Japanese models.
    /// The IDs are taken from the official binary's ONNX tensor input, not from
    /// dictionary order.
    /// </summary>
    public static class NeutrinoPhoneme {
        public const int PAU = 0;
        public const int BR = 3;
        public const int VR = 26;
        public const int PAD = 32;
        public const int AP = 41;

        public const int VocabSize = 42;

        static readonly Dictionary<string, int> phonemeToId = new Dictionary<string, int>() {
            {"pau", 0},
            {"sil", 0},
            {"a", 1},
            {"b", 2},
            {"br", 3},
            {"by", 4},
            {"ch", 5},
            {"cl", 6},
            {"d", 7},
            {"dy", 8},
            {"e", 9},
            {"f", 10},
            {"g", 11},
            {"gy", 12},
            {"h", 13},
            {"hy", 14},
            {"i", 15},
            {"j", 16},
            {"k", 17},
            {"ky", 18},
            {"m", 19},
            {"my", 20},
            {"n", 21},
            {"N", 22},
            {"ny", 23},
            {"o", 24},
            {"p", 25},
            {"py", 27},
            {"r", 28},
            {"ry", 29},
            {"s", 30},
            {"sh", 31},
            {"t", 33},
            {"ts", 34},
            {"ty", 35},
            {"u", 36},
            {"v", 37},
            {"w", 38},
            {"y", 39},
            {"z", 40},
            {"AP", 41},
            {"ap", 41},
        };

        static readonly Dictionary<string, string[]> romajiToPhonemes =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {
                {"a", new[] {"a"}},
                {"i", new[] {"i"}},
                {"u", new[] {"u"}},
                {"e", new[] {"e"}},
                {"o", new[] {"o"}},
                {"ka", new[] {"k", "a"}},
                {"ki", new[] {"k", "i"}},
                {"ku", new[] {"k", "u"}},
                {"ke", new[] {"k", "e"}},
                {"ko", new[] {"k", "o"}},
                {"kya", new[] {"ky", "a"}},
                {"kyi", new[] {"ky", "i"}},
                {"kyu", new[] {"ky", "u"}},
                {"kye", new[] {"ky", "e"}},
                {"kyo", new[] {"ky", "o"}},
                {"kwa", new[] {"k", "w", "a"}},
                {"kwi", new[] {"k", "w", "i"}},
                {"kwu", new[] {"k", "w", "u"}},
                {"kwe", new[] {"k", "w", "e"}},
                {"kwo", new[] {"k", "w", "o"}},
                {"ga", new[] {"g", "a"}},
                {"gi", new[] {"g", "i"}},
                {"gu", new[] {"g", "u"}},
                {"ge", new[] {"g", "e"}},
                {"go", new[] {"g", "o"}},
                {"gya", new[] {"gy", "a"}},
                {"gyi", new[] {"gy", "i"}},
                {"gyu", new[] {"gy", "u"}},
                {"gye", new[] {"gy", "e"}},
                {"gyo", new[] {"gy", "o"}},
                {"gwa", new[] {"g", "w", "a"}},
                {"gwi", new[] {"g", "w", "i"}},
                {"gwu", new[] {"g", "w", "u"}},
                {"gwe", new[] {"g", "w", "e"}},
                {"gwo", new[] {"g", "w", "o"}},
                {"sa", new[] {"s", "a"}},
                {"si", new[] {"s", "i"}},
                {"shi", new[] {"sh", "i"}},
                {"su", new[] {"s", "u"}},
                {"se", new[] {"s", "e"}},
                {"so", new[] {"s", "o"}},
                {"sya", new[] {"sh", "a"}},
                {"sha", new[] {"sh", "a"}},
                {"syu", new[] {"sh", "u"}},
                {"shu", new[] {"sh", "u"}},
                {"sye", new[] {"sh", "e"}},
                {"she", new[] {"sh", "e"}},
                {"syo", new[] {"sh", "o"}},
                {"sho", new[] {"sh", "o"}},
                {"za", new[] {"z", "a"}},
                {"zi", new[] {"z", "i"}},
                {"ji", new[] {"j", "i"}},
                {"zu", new[] {"z", "u"}},
                {"ze", new[] {"z", "e"}},
                {"zo", new[] {"z", "o"}},
                {"zya", new[] {"j", "a"}},
                {"ja", new[] {"j", "a"}},
                {"zyu", new[] {"j", "u"}},
                {"ju", new[] {"j", "u"}},
                {"zye", new[] {"j", "e"}},
                {"je", new[] {"j", "e"}},
                {"zyo", new[] {"j", "o"}},
                {"jo", new[] {"j", "o"}},
                {"ta", new[] {"t", "a"}},
                {"ti", new[] {"t", "i"}},
                {"chi", new[] {"ch", "i"}},
                {"tu", new[] {"t", "u"}},
                {"tsu", new[] {"ts", "u"}},
                {"te", new[] {"t", "e"}},
                {"to", new[] {"t", "o"}},
                {"tya", new[] {"ty", "a"}},
                {"cha", new[] {"ch", "a"}},
                {"tyu", new[] {"ty", "u"}},
                {"chu", new[] {"ch", "u"}},
                {"tye", new[] {"ty", "e"}},
                {"che", new[] {"ch", "e"}},
                {"tyo", new[] {"ty", "o"}},
                {"cho", new[] {"ch", "o"}},
                {"tsa", new[] {"ts", "a"}},
                {"tsi", new[] {"ts", "i"}},
                {"tse", new[] {"ts", "e"}},
                {"tso", new[] {"ts", "o"}},
                {"da", new[] {"d", "a"}},
                {"di", new[] {"d", "i"}},
                {"du", new[] {"d", "u"}},
                {"de", new[] {"d", "e"}},
                {"do", new[] {"d", "o"}},
                {"dya", new[] {"dy", "a"}},
                {"dyi", new[] {"dy", "i"}},
                {"dyu", new[] {"dy", "u"}},
                {"dye", new[] {"dy", "e"}},
                {"dyo", new[] {"dy", "o"}},
                {"na", new[] {"n", "a"}},
                {"ni", new[] {"n", "i"}},
                {"nu", new[] {"n", "u"}},
                {"ne", new[] {"n", "e"}},
                {"no", new[] {"n", "o"}},
                {"nya", new[] {"ny", "a"}},
                {"nyi", new[] {"ny", "i"}},
                {"nyu", new[] {"ny", "u"}},
                {"nye", new[] {"ny", "e"}},
                {"nyo", new[] {"ny", "o"}},
                {"ha", new[] {"h", "a"}},
                {"hi", new[] {"h", "i"}},
                {"hu", new[] {"f", "u"}},
                {"fu", new[] {"f", "u"}},
                {"he", new[] {"h", "e"}},
                {"ho", new[] {"h", "o"}},
                {"hya", new[] {"hy", "a"}},
                {"hyi", new[] {"hy", "i"}},
                {"hyu", new[] {"hy", "u"}},
                {"hye", new[] {"hy", "e"}},
                {"hyo", new[] {"hy", "o"}},
                {"fa", new[] {"f", "a"}},
                {"fi", new[] {"f", "i"}},
                {"fe", new[] {"f", "e"}},
                {"fo", new[] {"f", "o"}},
                {"ba", new[] {"b", "a"}},
                {"bi", new[] {"b", "i"}},
                {"bu", new[] {"b", "u"}},
                {"be", new[] {"b", "e"}},
                {"bo", new[] {"b", "o"}},
                {"bya", new[] {"by", "a"}},
                {"byi", new[] {"by", "i"}},
                {"byu", new[] {"by", "u"}},
                {"bye", new[] {"by", "e"}},
                {"byo", new[] {"by", "o"}},
                {"pa", new[] {"p", "a"}},
                {"pi", new[] {"p", "i"}},
                {"pu", new[] {"p", "u"}},
                {"pe", new[] {"p", "e"}},
                {"po", new[] {"p", "o"}},
                {"pya", new[] {"py", "a"}},
                {"pyi", new[] {"py", "i"}},
                {"pyu", new[] {"py", "u"}},
                {"pye", new[] {"py", "e"}},
                {"pyo", new[] {"py", "o"}},
                {"ma", new[] {"m", "a"}},
                {"mi", new[] {"m", "i"}},
                {"mu", new[] {"m", "u"}},
                {"me", new[] {"m", "e"}},
                {"mo", new[] {"m", "o"}},
                {"mya", new[] {"my", "a"}},
                {"myi", new[] {"my", "i"}},
                {"myu", new[] {"my", "u"}},
                {"mye", new[] {"my", "e"}},
                {"myo", new[] {"my", "o"}},
                {"ya", new[] {"y", "a"}},
                {"yu", new[] {"y", "u"}},
                {"ye", new[] {"y", "e"}},
                {"yo", new[] {"y", "o"}},
                {"ra", new[] {"r", "a"}},
                {"ri", new[] {"r", "i"}},
                {"ru", new[] {"r", "u"}},
                {"re", new[] {"r", "e"}},
                {"ro", new[] {"r", "o"}},
                {"rya", new[] {"ry", "a"}},
                {"ryi", new[] {"ry", "i"}},
                {"ryu", new[] {"ry", "u"}},
                {"rye", new[] {"ry", "e"}},
                {"ryo", new[] {"ry", "o"}},
                {"wa", new[] {"w", "a"}},
                {"wi", new[] {"w", "i"}},
                {"wu", new[] {"w", "u"}},
                {"we", new[] {"w", "e"}},
                {"wo", new[] {"w", "o"}},
                {"va", new[] {"v", "a"}},
                {"vi", new[] {"v", "i"}},
                {"vu", new[] {"v", "u"}},
                {"ve", new[] {"v", "e"}},
                {"vo", new[] {"v", "o"}},
                {"n", new[] {"N"}},
                {"nn", new[] {"N"}},
            };

        public static IEnumerable<string> AllPhonemes =>
            phonemeToId
                .Where(kv => kv.Key != "sil" && kv.Key != "ap")
                .OrderBy(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key)
                .Concat(NeutrinoJapaneseDictionary.Phonemes)
                .Distinct();

        public static int GetPhonemeId(string phoneme) {
            phoneme = phoneme?.Trim();
            if (string.IsNullOrEmpty(phoneme)) {
                return PAU;
            }
            if (phoneme == "R"
                || phoneme.Equals("SP", StringComparison.OrdinalIgnoreCase)
                || phoneme.Equals("rest", StringComparison.OrdinalIgnoreCase)) {
                return PAU;
            }
            if (phonemeToId.TryGetValue(phoneme, out int id)) {
                return id;
            }
            if (phonemeToId.TryGetValue(phoneme.ToLowerInvariant(), out id)) {
                return id;
            }
            Log.Warning($"Unknown NEUTRINO phoneme: {phoneme}");
            return PAU;
        }

        public static int[] KanaToPhonemeIds(string kana) {
            return KanaToPhonemes(kana).Select(p => GetPhonemeId(p)).ToArray();
        }

        public static string[] RenderPhoneToPhonemes(string phone) {
            phone = phone?.Trim();
            if (string.IsNullOrEmpty(phone)) {
                return new[] { "pau" };
            }
            if (IsKnownPhoneme(phone)) {
                return new[] { NormalizePhoneme(phone) };
            }
            return KanaToPhonemes(phone);
        }

        public static bool IsVowelPhoneme(string phoneme) {
            phoneme = NormalizePhoneme(phoneme?.Trim() ?? string.Empty);
            return phoneme == "a"
                || phoneme == "i"
                || phoneme == "u"
                || phoneme == "e"
                || phoneme == "o"
                || phoneme == "N"
                || phoneme == "pau"
                || phoneme == "AP";
        }

        public static string[] KanaToPhonemes(string kana) {
            kana = kana?.Trim();
            if (string.IsNullOrEmpty(kana)) {
                return new[] { "pau" };
            }
            if (kana == "R"
                || kana.Equals("SP", StringComparison.OrdinalIgnoreCase)
                || kana.Equals("rest", StringComparison.OrdinalIgnoreCase)) {
                return new[] { "pau" };
            }
            if (kana.Equals("n", StringComparison.OrdinalIgnoreCase)
                || kana.Equals("nn", StringComparison.OrdinalIgnoreCase)) {
                return new[] { "N" };
            }

            var parts = kana.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.All(IsKnownPhoneme)) {
                return parts.Select(NormalizePhoneme).ToArray();
            }
            if (IsKnownPhoneme(kana)) {
                return new[] { NormalizePhoneme(kana) };
            }

            var normalizedKana = kana.Normalize(NormalizationForm.FormC);
            if (NeutrinoJapaneseDictionary.TryGetValue(normalizedKana, out var phonemes)) {
                return phonemes;
            }
            if (romajiToPhonemes.TryGetValue(kana, out phonemes)) {
                return phonemes;
            }

            Log.Warning($"Kana/romaji not in NEUTRINO dictionary: {kana}");
            return new[] { "pau" };
        }

        static bool IsKnownPhoneme(string phoneme) {
            phoneme = phoneme?.Trim();
            if (string.IsNullOrEmpty(phoneme)) {
                return false;
            }
            return phonemeToId.ContainsKey(phoneme)
                || phonemeToId.ContainsKey(phoneme.ToLowerInvariant());
        }

        static string NormalizePhoneme(string phoneme) {
            if (phonemeToId.ContainsKey(phoneme)) {
                return phoneme;
            }
            var lower = phoneme.ToLowerInvariant();
            return phonemeToId.ContainsKey(lower) ? lower : phoneme;
        }
    }
}
