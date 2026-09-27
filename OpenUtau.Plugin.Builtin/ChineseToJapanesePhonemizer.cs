using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using WanaKanaNet;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// 中文 → 日语音素器
    /// 让日语音源（CV / VCV）唱中文歌词。
    /// 流程：汉字 → 拼音（RomanizeNotes）→ 日语 mora 序列 → 匹配音源别名。
    /// </summary>
    [Phonemizer("Chinese to Japanese Phonemizer", "ZH to JA", "Deepseek and bili_22186000871", language: "ZH")]
    public class ChineseToJapanesePhonemizer : SyllableBasedPhonemizer {

        // ---------------- 拼音声母（最长优先） ----------------
        private static readonly string[] Initials = {
            "zh", "ch", "sh",
            "b", "p", "m", "f", "d", "t", "n", "l",
            "g", "k", "h", "j", "q", "x", "r", "z", "c", "s",
            "y", "w"
        };

        // ---------------- 拼音韵母（最长优先） ----------------
        private static readonly string[] Finals = {
            "iang", "iong", "uang", "ueng",
            "ang", "eng", "ing", "ong", "ian", "iao", "uan", "uai",
            "ai", "ei", "ao", "ou", "an", "en", "in", "un", "er",
            "ia", "ie", "iu", "ua", "uo", "ui", "ue", "ve", "vn",
            "a", "o", "e", "i", "u", "v"
        };

        // ---------------- 声母 → 日语罗马字辅音 ----------------
        private static readonly Dictionary<string, string> InitialMap = new() {
            { "b", "b" }, { "p", "p" }, { "m", "m" }, { "f", "h" },
            { "d", "d" }, { "t", "t" }, { "n", "n" }, { "l", "r" },
            { "g", "g" }, { "k", "k" }, { "h", "h" },
            { "j", "j" }, { "q", "ch" }, { "x", "sh" },
            { "zh", "j" }, { "ch", "ch" }, { "sh", "sh" }, { "r", "r" },
            { "z", "z" }, { "c", "ts" }, { "s", "s" },
            { "y", "y" }, { "w", "w" }
        };

        public ChineseToJapanesePhonemizer() {
            this.vowels = Finals;
            this.consonants = Initials;
        }

        protected override string[] GetVowels() => Finals;
        protected override string[] GetConsonants() => Initials;
        protected override string GetDictionaryName() => null;

        // ================================================================
        // 关键 1：SetUp 中把汉字歌词批量转成拼音
        // ================================================================
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            BaseChinesePhonemizer.RomanizeNotes(groups);
            base.SetUp(groups, project, track);
        }

        // ================================================================
        // 关键 2：重写 GetSymbols，直接返回 [声母, 韵母] 结构
        // ================================================================
        protected override string[] GetSymbols(Note note) {
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                return note.phoneticHint.Split(
                    new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }
            string lyric = note.lyric;
            if (string.IsNullOrEmpty(lyric)) return null;
            if (lyric == "-" || lyric == "R" || lyric == "+") return new[] { lyric };

            return ParsePinyin(lyric.ToLowerInvariant());
        }

        private string[] ParsePinyin(string pinyin) {
            pinyin = new string(pinyin.Where(char.IsLetter).ToArray());
            if (string.IsNullOrEmpty(pinyin)) return new[] { "a" };

            // ---- y 开头的特殊音节 ----
            if (pinyin.StartsWith("y")) {
                string rest = pinyin.Substring(1);
                // yi / yin / ying → i / in / ing
                if (rest == "i" || rest == "in" || rest == "ing") {
                    return new[] { rest };
                }
                // yu / yun / yue / yuan → ü 系列
                if (rest.StartsWith("u")) {
                    return new[] { "v" + rest.Substring(1) };
                }
                // ya / ye / yao / you / yan / yang / yong → y 作声母
            }
            // ---- w 开头的特殊音节 ----
            if (pinyin.StartsWith("w")) {
                string rest = pinyin.Substring(1);
                // wu → u
                if (rest == "u") return new[] { "u" };
                // wa / wo / wai / wei / wan / wen / wang / weng → w 作声母
            }

            // ---- 匹配声母 ----
            string matchedInitial = null;
            foreach (var init in Initials) {
                if (pinyin.StartsWith(init)) {
                    string remaining = pinyin.Substring(init.Length);
                    if (Finals.Contains(remaining)) {
                        matchedInitial = init;
                        break;
                    }
                }
            }

            string final = matchedInitial != null
                ? pinyin.Substring(matchedInitial.Length)
                : pinyin;

            // 未知韵母兜底
            if (!Finals.Contains(final)) final = "a";

            return matchedInitial != null
                ? new[] { matchedInitial, final }
                : new[] { final };
        }

        // ================================================================
        // 关键 3：为每个音节生成日语 mora 列表
        // ================================================================
        protected override List<string> ProcessSyllable(Syllable syllable) {
            var phonemes = new List<string>();
            if (string.IsNullOrEmpty(syllable.v)) return phonemes;

            var moras = GenerateMoras(syllable.cc, syllable.v);
            if (moras.Count == 0) return phonemes;

            string prevJaVowel = ConvertToJapaneseVowel(syllable.prevV);

            for (int i = 0; i < moras.Count; i++) {
                string mora = moras[i];
                string alias;

                if (i == 0) {
                    if (string.IsNullOrEmpty(prevJaVowel) || prevJaVowel == "-") {
                        // 起始音符：优先 "- CV" 形式
                        alias = PickAlias(syllable.vowelTone,
                            $"- {mora}", $"-{mora}", mora);
                    } else {
                        // VCV 过渡：优先 "V CV" 形式
                        alias = PickAlias(syllable.vowelTone,
                            $"{prevJaVowel} {mora}", $"{prevJaVowel}{mora}", mora);
                    }
                } else {
                    // 后续 mora 只做普通 CV
                    alias = PickAlias(syllable.vowelTone, mora);
                }

                phonemes.Add(alias);
            }

            return phonemes;
        }

        // ================================================================
        // 乐句结尾处理
        // ================================================================
        protected override List<string> ProcessEnding(Ending ending) {
            var phonemes = new List<string>();
            string prevJaVowel = ConvertToJapaneseVowel(ending.prevV);
            if (string.IsNullOrEmpty(prevJaVowel) || prevJaVowel == "-") return phonemes;

            if (HasOto($"{prevJaVowel} R", ending.tone)) {
                phonemes.Add($"{prevJaVowel} R");
            } else if (HasOto($"{prevJaVowel} -", ending.tone)) {
                phonemes.Add($"{prevJaVowel} -");
            }
            return phonemes;
        }

        // ================================================================
        // 核心：拼音 → 日语 mora 列表
        //   "de"   (["d"], "e")     → [で]
        //   "yang" (["y"], "ang")   → [や, ん]
        //   "guo"  (["g"], "uo")    → [ぐ, お]
        //   "chuan"(["ch"], "uan")  → [ちゅ, あ, ん]
        // ================================================================
        private List<string> GenerateMoras(string[] cc, string final) {
            var moras = new List<string>();

            string cons = "";
            if (cc != null && cc.Length > 0) {
                cons = InitialMap.TryGetValue(cc[0], out var jc) ? jc : cc[0];
            }

            var vowelSeq = GetJapaneseVowelSequence(final);
            if (vowelSeq.Count == 0) return moras;

            // 把声母拼到第一个元音上
            if (!string.IsNullOrEmpty(cons)) {
                string firstV = vowelSeq[0];
                vowelSeq.RemoveAt(0);

                if (firstV == "N") {
                    // 罕见：声母后面直接跟拨音，用 u 兜底
                    moras.Add(ToKana(cons + "u"));
                    moras.Add("ん");
                } else {
                    moras.Add(ToKana(cons + firstV));
                }
            }

            // 剩下元音各自成为一个 mora
            foreach (var v in vowelSeq) {
                if (v == "N") moras.Add("ん");
                else moras.Add(ToKana(v));
            }

            return moras;
        }

        // ================================================================
        // 拼音韵母 → 日语元音罗马字序列
        //   "N" 代表拨音 ん
        //   "yu" 代表 ゆ（ü 的日语近似）
        // ================================================================
        private List<string> GetJapaneseVowelSequence(string pinyinFinal) {
            switch (pinyinFinal) {
                // ---- 单元音 ----
                case "a":  return new List<string> { "a" };
                case "o":  return new List<string> { "o" };
                case "e":  return new List<string> { "e" };
                case "i":  return new List<string> { "i" };
                case "u":  return new List<string> { "u" };
                case "v":  return new List<string> { "yu" };   // ü → ゆ
                case "er": return new List<string> { "a" };

                // ---- 双元音 ----
                case "ai": return new List<string> { "a", "i" };
                case "ei": return new List<string> { "e", "i" };
                case "ao": return new List<string> { "a", "o" };
                case "ou": return new List<string> { "o", "u" };

                // ---- 鼻韵母 ----
                case "an":   return new List<string> { "a", "N" };
                case "en":   return new List<string> { "e", "N" };
                case "in":   return new List<string> { "i", "N" };
                case "un":   return new List<string> { "u", "N" };
                case "ang":  return new List<string> { "a", "N" };
                case "eng":  return new List<string> { "e", "N" };
                case "ing":  return new List<string> { "i", "N" };
                case "ong":  return new List<string> { "o", "N" };
                case "ian":  return new List<string> { "i", "e", "N" };
                case "iang": return new List<string> { "i", "a", "N" };
                case "iong": return new List<string> { "i", "o", "N" };
                case "uan":  return new List<string> { "u", "a", "N" };
                case "uang": return new List<string> { "u", "a", "N" };
                case "ueng": return new List<string> { "u", "e", "N" };
                case "van":  return new List<string> { "yu", "e", "N" };
                case "vn":   return new List<string> { "yu", "N" };

                // ---- i 介音 ----
                case "ia":  return new List<string> { "i", "a" };
                case "ie":  return new List<string> { "i", "e" };
                case "iu":  return new List<string> { "i", "u" };
                case "iao": return new List<string> { "i", "a", "o" };

                // ---- u 介音 ----
                case "ua":  return new List<string> { "u", "a" };
                case "uo":  return new List<string> { "u", "o" };
                case "ui":  return new List<string> { "u", "i" };
                case "uai": return new List<string> { "u", "a", "i" };

                // ---- ü 介音 ----
                case "ue":  return new List<string> { "yu", "e" };
                case "ve":  return new List<string> { "yu", "e" };

                default: return new List<string> { pinyinFinal };
            }
        }

        // ================================================================
        // 工具方法
        // ================================================================
        private string ToKana(string romaji) {
            if (string.IsNullOrEmpty(romaji)) return romaji;
            try {
                var kana = WanaKana.ToHiragana(romaji);
                return string.IsNullOrEmpty(kana) ? romaji : kana;
            } catch {
                return romaji;
            }
        }

        /// <summary>
        /// 把拼音韵母转换成日语实际使用的最后一个元音（用于 VCV 前缀）。
        ///   "ang" → "n"  （最后一个 mora 是 ん）
        ///   "ai"  → "i"
        ///   "uo"  → "o"
        /// </summary>
        private string ConvertToJapaneseVowel(string pinyinFinal) {
            if (string.IsNullOrEmpty(pinyinFinal)) return "-";
            if (pinyinFinal == "-" || pinyinFinal == "R") return pinyinFinal;

            var seq = GetJapaneseVowelSequence(pinyinFinal);
            if (seq.Count > 0) {
                var last = seq.Last();
                if (last == "N") return "n";
                return last.Last().ToString();
            }
            return pinyinFinal.Last().ToString();
        }

        private string PickAlias(int tone, params string[] candidates) {
            foreach (var c in candidates) {
                if (HasOto(c, tone)) return c;
            }
            return candidates.Last();
        }
    }
}