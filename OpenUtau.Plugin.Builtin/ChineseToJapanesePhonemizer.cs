// =====================================================================
// ChineseToJapanesePhonemizer v2.1.7
// 让日语音源唱中文歌词的 OpenUtau 音素器
//
// 版本历史：
//   v1.0  初始版本
//   v1.1  加入完整拼音优先 + 多音节输入
//   v1.2  加入标点/数字/儿化/YAML/别名缓存
//   v1.3  加入一不变调/轻声/叠字/声调时长/别名覆盖/拼音简写/C4 UI
//   v2.0  修复拼音轻声误判；精简多音字表；支持显式轻声 zhe5
//   v2.1  重构 FullPinyinMap 为音节级数组
//   v2.1.1 内置 RomajiToKana；修复拗音拆分
//   v2.1.2 修复 yu 键重复；dya/dyu/dyo 改 じゃ 系；新增 disable_vcv
//   v2.1.3 ou 韵母 o+u → o+o
//   v2.1.4 尝试 position 后移压缩鼻音（效果有限）
//   v2.1.5 nasal_mode 三档（none/short/full），默认 none
//   v2.1.6 修复 nasal_mode=none 不生效的 bug；short 模式改用 KOtoJA
//          风格的「末尾固定毫秒」逻辑（默认 120ms / 有下音符 180ms）；
//          默认改为 short
//   v2.1.7 加入通配符 * 过渡音素支持（Defoko 等 CV 音源），
//          可通过 use_wildcard 开关控制
//
// 依赖：OpenUtau v0.1.570+ / .NET 10 / YamlDotNet / Serilog
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;
using YamlDotNet.Serialization;

namespace OpenUtau.Plugin.Builtin {

    // =================================================================
    // YAML 配置结构
    // =================================================================
    public class Zh2JaConfig {
        [YamlMember(Alias = "full_pinyin")]
        public Dictionary<string, List<string>> FullPinyin { get; set; } = new();

        [YamlMember(Alias = "polyphones")]
        public Dictionary<string, string> Polyphones { get; set; } = new();

        [YamlMember(Alias = "alias_overrides")]
        public Dictionary<string, List<string>> AliasOverrides { get; set; } = new();

        [YamlMember(Alias = "ask_polyphone")]
        public bool AskPolyphone { get; set; } = false;

        [YamlMember(Alias = "polyphone_decisions")]
        public Dictionary<string, bool> PolyphoneDecisions { get; set; } = new();

        [YamlMember(Alias = "disable_tone3")]
        public bool DisableTone3 { get; set; } = false;

        [YamlMember(Alias = "disable_light_tone")]
        public bool DisableLightTone { get; set; } = false;

        [YamlMember(Alias = "disable_tone_timing")]
        public bool DisableToneTiming { get; set; } = false;

        [YamlMember(Alias = "disable_double_char")]
        public bool DisableDoubleChar { get; set; } = false;

        [YamlMember(Alias = "disable_vcv")]
        public bool DisableVcv { get; set; } = false;

        // 鼻音韵尾处理模式：
        //   none  → 完全省略「ん」
        //   short → 「ん」只占音符末尾 nasal_ms 毫秒（默认，推荐）
        //   full  → 保留完整采样
        [YamlMember(Alias = "nasal_mode")]
        public string NasalMode { get; set; } = "short";

        // short 模式下「ん」占音符末尾的毫秒数（有下一个音符时会自动 ×1.5）
        [YamlMember(Alias = "nasal_ms")]
        public int NasalMs { get; set; } = 120;

        // 是否启用通配符 * 过渡音素（对 Defoko 等 CV 音源有效）
        [YamlMember(Alias = "use_wildcard")]
        public bool UseWildcard { get; set; } = true;
    }

    // =================================================================
    // 音素器主类
    // =================================================================
    [Phonemizer("Chinese to Japanese Phonemizer", "ZH to JA", "Deepseek", language: "ZH")]
    public class ChineseToJapanesePhonemizer : SyllableBasedPhonemizer {

        // =============================================================
        // Romaji → Kana 映射表（内置）
        // =============================================================
        private static readonly Dictionary<string, string> RomajiToKana = new() {
            { "a", "あ" }, { "i", "い" }, { "u", "う" }, { "e", "え" }, { "o", "お" },
            { "n", "ん" }, { "N", "ん" },

            { "ba", "ば" }, { "bi", "び" }, { "bu", "ぶ" }, { "be", "べ" }, { "bo", "ぼ" },
            { "bya", "びゃ" }, { "byu", "びゅ" }, { "byo", "びょ" },

            { "pa", "ぱ" }, { "pi", "ぴ" }, { "pu", "ぷ" }, { "pe", "ぺ" }, { "po", "ぽ" },
            { "pya", "ぴゃ" }, { "pyu", "ぴゅ" }, { "pyo", "ぴょ" },

            { "ma", "ま" }, { "mi", "み" }, { "mu", "む" }, { "me", "め" }, { "mo", "も" },
            { "mya", "みゃ" }, { "myu", "みゅ" }, { "myo", "みょ" },

            { "fa", "ふぁ" }, { "fi", "ふぃ" }, { "fu", "ふ" }, { "fe", "ふぇ" }, { "fo", "ふぉ" },
            { "ha", "は" }, { "hi", "ひ" }, { "hu", "ふ" }, { "he", "へ" }, { "ho", "ほ" },
            { "hya", "ひゃ" }, { "hyu", "ひゅ" }, { "hyo", "ひょ" },

            { "da", "だ" }, { "di", "でぃ" }, { "du", "どぅ" }, { "de", "で" }, { "do", "ど" },
            { "dya", "じゃ" }, { "dyu", "じゅ" }, { "dyo", "じょ" },

            { "ta", "た" }, { "ti", "てぃ" }, { "tu", "とぅ" }, { "te", "て" }, { "to", "と" },
            { "tya", "ちゃ" }, { "tyu", "ちゅ" }, { "tyo", "ちょ" },

            { "na", "な" }, { "ni", "に" }, { "nu", "ぬ" }, { "ne", "ね" }, { "no", "の" },
            { "nya", "にゃ" }, { "nyu", "にゅ" }, { "nyo", "にょ" },

            { "ra", "ら" }, { "ri", "り" }, { "ru", "る" }, { "re", "れ" }, { "ro", "ろ" },
            { "rya", "りゃ" }, { "ryu", "りゅ" }, { "ryo", "りょ" },

            { "ga", "が" }, { "gi", "ぎ" }, { "gu", "ぐ" }, { "ge", "げ" }, { "go", "ご" },
            { "gya", "ぎゃ" }, { "gyu", "ぎゅ" }, { "gyo", "ぎょ" },

            { "ka", "か" }, { "ki", "き" }, { "ku", "く" }, { "ke", "け" }, { "ko", "こ" },
            { "kya", "きゃ" }, { "kyu", "きゅ" }, { "kyo", "きょ" },

            { "za", "ざ" }, { "zi", "じ" }, { "zu", "ず" }, { "ze", "ぜ" }, { "zo", "ぞ" },
            { "ja", "じゃ" }, { "ji", "じ" }, { "ju", "じゅ" }, { "je", "じぇ" }, { "jo", "じょ" },

            { "sa", "さ" }, { "si", "す" }, { "su", "す" }, { "se", "せ" }, { "so", "そ" },
            { "sha", "しゃ" }, { "shi", "し" }, { "shu", "しゅ" }, { "she", "しぇ" }, { "sho", "しょ" },

            { "tsa", "つぁ" }, { "tsi", "つぃ" }, { "tsu", "つ" }, { "tse", "つぇ" }, { "tso", "つぉ" },
            { "cha", "ちゃ" }, { "chi", "ち" }, { "chu", "ちゅ" }, { "che", "ちぇ" }, { "cho", "ちょ" },

            { "ya", "や" }, { "yu", "ゆ" }, { "ye", "いぇ" }, { "yo", "よ" },

            { "wa", "わ" }, { "wi", "うぃ" }, { "wu", "う" }, { "we", "うぇ" }, { "wo", "うぉ" },

            { "va", "ヴぁ" }, { "vi", "ヴぃ" }, { "vu", "ヴ" }, { "ve", "ヴぇ" }, { "vo", "ヴぉ" },
        };

        private static class UiCompat {
            private static int _state = -1;
            private static readonly object _lock = new();

            public static bool Available {
                get {
                    if (_state >= 0) return _state == 1;
                    lock (_lock) {
                        if (_state >= 0) return _state == 1;
                        _state = Check() ? 1 : 0;
                        return _state == 1;
                    }
                }
            }

            private static bool Check() {
                try {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT) return false;
                    IntPtr h = LoadLibraryW("user32.dll");
                    if (h == IntPtr.Zero) return false;
                    FreeLibrary(h);
                    if (GetProcessWindowStation() == IntPtr.Zero) return false;
                    return true;
                } catch { return false; }
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr LoadLibraryW(string name);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool FreeLibrary(IntPtr h);
            [DllImport("user32.dll")]
            private static extern IntPtr GetProcessWindowStation();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private static readonly string[] Initials = {
            "zh", "ch", "sh",
            "b", "p", "m", "f", "d", "t", "n", "l",
            "g", "k", "h", "j", "q", "x", "r", "z", "c", "s",
            "y", "w"
        };

        private static readonly string[] Finals = {
            "iang", "iong", "uang", "ueng",
            "ang", "eng", "ing", "ong", "ian", "iao", "uan", "uai",
            "ai", "ei", "ao", "ou", "an", "en", "in", "un", "er",
            "ia", "ie", "iu", "ua", "uo", "ui", "ue", "ve", "vn",
            "a", "o", "e", "i", "u", "v"
        };

        private static readonly Dictionary<string, string> InitialMap = new() {
            { "b", "b" }, { "p", "p" }, { "m", "m" }, { "f", "h" },
            { "d", "d" }, { "t", "t" }, { "n", "n" }, { "l", "r" },
            { "g", "g" }, { "k", "k" }, { "h", "h" },
            { "j", "j" }, { "q", "ch" }, { "x", "sh" },
            { "zh", "j" }, { "ch", "ch" }, { "sh", "sh" }, { "r", "r" },
            { "z", "z" }, { "c", "ts" }, { "s", "s" },
            { "y", "y" }, { "w", "w" }
        };

        private static readonly HashSet<string> LightToneChars = new() {
            "的", "了", "着", "呢", "吧", "吗", "啊", "呀", "哦", "咯", "嘛", "啦", "哎", "哇",
        };

        private static readonly Dictionary<string, string[]> DefaultFullPinyinMap = new() {
            // 零声母
            { "a",   new[]{"a"} },        { "ai",  new[]{"a", "i"} },
            { "an",  new[]{"a", "n"} },   { "ang", new[]{"a", "n"} },
            { "ao",  new[]{"a", "o"} },   { "o",   new[]{"o"} },
            { "ou",  new[]{"o", "o"} },   { "e",   new[]{"e"} },
            { "en",  new[]{"e", "n"} },   { "eng", new[]{"e", "n"} },
            { "er",  new[]{"a"} },        { "i",   new[]{"i"} },
            { "u",   new[]{"u"} },        { "v",   new[]{"yu"} },

            // b
            { "ba",   new[]{"ba"} },          { "bo",   new[]{"bo"} },
            { "bai",  new[]{"ba", "i"} },     { "bei",  new[]{"be", "i"} },
            { "bao",  new[]{"ba", "o"} },     { "ban",  new[]{"ba", "n"} },
            { "ben",  new[]{"be", "n"} },     { "bang", new[]{"ba", "n"} },
            { "beng", new[]{"be", "n"} },     { "bi",   new[]{"bi"} },
            { "bie",  new[]{"bi", "e"} },     { "biao", new[]{"bya", "o"} },
            { "bian", new[]{"bya", "n"} },    { "bin",  new[]{"bi", "n"} },
            { "bing", new[]{"bi", "n"} },     { "bu",   new[]{"bu"} },

            // p
            { "pa",   new[]{"pa"} },          { "po",   new[]{"po"} },
            { "pai",  new[]{"pa", "i"} },     { "pei",  new[]{"pe", "i"} },
            { "pao",  new[]{"pa", "o"} },     { "pou",  new[]{"po", "o"} },
            { "pan",  new[]{"pa", "n"} },     { "pen",  new[]{"pe", "n"} },
            { "pang", new[]{"pa", "n"} },     { "peng", new[]{"pe", "n"} },
            { "pi",   new[]{"pi"} },          { "pie",  new[]{"pi", "e"} },
            { "piao", new[]{"pya", "o"} },    { "pian", new[]{"pya", "n"} },
            { "pin",  new[]{"pi", "n"} },     { "ping", new[]{"pi", "n"} },
            { "pu",   new[]{"pu"} },

            // m
            { "ma",   new[]{"ma"} },          { "mo",   new[]{"mo"} },
            { "me",   new[]{"me"} },          { "mai",  new[]{"ma", "i"} },
            { "mei",  new[]{"me", "i"} },     { "mao",  new[]{"ma", "o"} },
            { "mou",  new[]{"mo", "o"} },     { "man",  new[]{"ma", "n"} },
            { "men",  new[]{"me", "n"} },     { "mang", new[]{"ma", "n"} },
            { "meng", new[]{"me", "n"} },     { "mi",   new[]{"mi"} },
            { "mie",  new[]{"mi", "e"} },     { "miao", new[]{"mya", "o"} },
            { "miu",  new[]{"myu"} },         { "mian", new[]{"mya", "n"} },
            { "min",  new[]{"mi", "n"} },     { "ming", new[]{"mi", "n"} },
            { "mu",   new[]{"mu"} },

            // f
            { "fa",   new[]{"fa"} },          { "fo",   new[]{"fo"} },
            { "fei",  new[]{"fe", "i"} },     { "fou",  new[]{"fo", "o"} },
            { "fan",  new[]{"fa", "n"} },     { "fen",  new[]{"fe", "n"} },
            { "fang", new[]{"fa", "n"} },     { "feng", new[]{"fe", "n"} },
            { "fu",   new[]{"fu"} },

            // d
            { "da",   new[]{"da"} },          { "de",   new[]{"de"} },
            { "dai",  new[]{"da", "i"} },     { "dei",  new[]{"de", "i"} },
            { "dao",  new[]{"da", "o"} },     { "dou",  new[]{"do", "o"} },
            { "dan",  new[]{"da", "n"} },     { "den",  new[]{"de", "n"} },
            { "dang", new[]{"da", "n"} },     { "deng", new[]{"de", "n"} },
            { "di",   new[]{"di"} },          { "dia",  new[]{"ja"} },
            { "die",  new[]{"di", "e"} },     { "diao", new[]{"dya", "o"} },
            { "diu",  new[]{"ju"} },          { "dian", new[]{"dya", "n"} },
            { "ding", new[]{"di", "n"} },     { "dong", new[]{"do", "n"} },
            { "du",   new[]{"du"} },          { "duo",  new[]{"du", "o"} },
            { "dui",  new[]{"du", "i"} },     { "duan", new[]{"du", "a", "n"} },
            { "dun",  new[]{"du", "n"} },

            // t
            { "ta",   new[]{"ta"} },          { "te",   new[]{"te"} },
            { "tai",  new[]{"ta", "i"} },     { "tao",  new[]{"ta", "o"} },
            { "tou",  new[]{"to", "o"} },     { "tan",  new[]{"ta", "n"} },
            { "tang", new[]{"ta", "n"} },     { "teng", new[]{"te", "n"} },
            { "ti",   new[]{"ti"} },          { "tie",  new[]{"ti", "e"} },
            { "tiao", new[]{"tya", "o"} },    { "tian", new[]{"tya", "n"} },
            { "ting", new[]{"ti", "n"} },     { "tong", new[]{"to", "n"} },
            { "tu",   new[]{"tu"} },          { "tuo",  new[]{"tu", "o"} },
            { "tui",  new[]{"tu", "i"} },     { "tuan", new[]{"tu", "a", "n"} },
            { "tun",  new[]{"tu", "n"} },

            // n
            { "na",   new[]{"na"} },          { "ne",   new[]{"ne"} },
            { "nai",  new[]{"na", "i"} },     { "nei",  new[]{"ne", "i"} },
            { "nao",  new[]{"na", "o"} },     { "nou",  new[]{"no", "o"} },
            { "nan",  new[]{"na", "n"} },     { "nen",  new[]{"ne", "n"} },
            { "nang", new[]{"na", "n"} },     { "neng", new[]{"ne", "n"} },
            { "ni",   new[]{"ni"} },          { "nie",  new[]{"ni", "e"} },
            { "niao", new[]{"nya", "o"} },    { "niu",  new[]{"nyu"} },
            { "nian", new[]{"nya", "n"} },    { "nin",  new[]{"ni", "n"} },
            { "niang",new[]{"nya", "n"} },    { "ning", new[]{"ni", "n"} },
            { "nong", new[]{"no", "n"} },     { "nu",   new[]{"nu"} },
            { "nuo",  new[]{"nu", "o"} },     { "nuan", new[]{"nu", "a", "n"} },
            { "nv",   new[]{"nyu"} },         { "nve",  new[]{"nyu", "e"} },

            // l → r
            { "la",   new[]{"ra"} },          { "le",   new[]{"re"} },
            { "lai",  new[]{"ra", "i"} },     { "lei",  new[]{"re", "i"} },
            { "lao",  new[]{"ra", "o"} },     { "lou",  new[]{"ro", "o"} },
            { "lan",  new[]{"ra", "n"} },     { "lang", new[]{"ra", "n"} },
            { "leng", new[]{"re", "n"} },     { "li",   new[]{"ri"} },
            { "lia",  new[]{"rya"} },         { "lie",  new[]{"ri", "e"} },
            { "liao", new[]{"rya", "o"} },    { "liu",  new[]{"ryu"} },
            { "lian", new[]{"rya", "n"} },    { "lin",  new[]{"ri", "n"} },
            { "liang",new[]{"rya", "n"} },    { "ling", new[]{"ri", "n"} },
            { "long", new[]{"ro", "n"} },     { "lu",   new[]{"ru"} },
            { "luo",  new[]{"ru", "o"} },     { "luan", new[]{"ru", "a", "n"} },
            { "lun",  new[]{"ru", "n"} },     { "lv",   new[]{"ryu"} },
            { "lve",  new[]{"ryu", "e"} },

            // g
            { "ga",   new[]{"ga"} },          { "ge",   new[]{"ge"} },
            { "gai",  new[]{"ga", "i"} },     { "gei",  new[]{"ge", "i"} },
            { "gao",  new[]{"ga", "o"} },     { "gou",  new[]{"go", "o"} },
            { "gan",  new[]{"ga", "n"} },     { "gen",  new[]{"ge", "n"} },
            { "gang", new[]{"ga", "n"} },     { "geng", new[]{"ge", "n"} },
            { "gong", new[]{"go", "n"} },     { "gu",   new[]{"gu"} },
            { "gua",  new[]{"gu", "a"} },     { "guo",  new[]{"gu", "o"} },
            { "guai", new[]{"gu", "a", "i"} },{ "gui",  new[]{"gu", "i"} },
            { "guan", new[]{"gu", "a", "n"} },{ "guang",new[]{"gu", "a", "n"} },
            { "gun",  new[]{"gu", "n"} },

            // k
            { "ka",   new[]{"ka"} },          { "ke",   new[]{"ke"} },
            { "kai",  new[]{"ka", "i"} },     { "kei",  new[]{"ke", "i"} },
            { "kao",  new[]{"ka", "o"} },     { "kou",  new[]{"ko", "o"} },
            { "kan",  new[]{"ka", "n"} },     { "ken",  new[]{"ke", "n"} },
            { "kang", new[]{"ka", "n"} },     { "keng", new[]{"ke", "n"} },
            { "kong", new[]{"ko", "n"} },     { "ku",   new[]{"ku"} },
            { "kua",  new[]{"ku", "a"} },     { "kuo",  new[]{"ku", "o"} },
            { "kuai", new[]{"ku", "a", "i"} },{ "kui",  new[]{"ku", "i"} },
            { "kuan", new[]{"ku", "a", "n"} },{ "kuang",new[]{"ku", "a", "n"} },
            { "kun",  new[]{"ku", "n"} },

            // h
            { "ha",   new[]{"ha"} },          { "he",   new[]{"he"} },
            { "hai",  new[]{"ha", "i"} },     { "hei",  new[]{"he", "i"} },
            { "hao",  new[]{"ha", "o"} },     { "hou",  new[]{"ho", "o"} },
            { "han",  new[]{"ha", "n"} },     { "hen",  new[]{"he", "n"} },
            { "hang", new[]{"ha", "n"} },     { "heng", new[]{"he", "n"} },
            { "hong", new[]{"ho", "n"} },     { "hu",   new[]{"fu"} },
            { "hua",  new[]{"fa"} },          { "huo",  new[]{"fu", "o"} },
            { "huai", new[]{"fa", "i"} },     { "hui",  new[]{"fu", "i"} },
            { "huan", new[]{"fa", "n"} },     { "huang",new[]{"fa", "n"} },
            { "hun",  new[]{"fu", "n"} },

            // j
            { "ji",   new[]{"ji"} },          { "jia",  new[]{"ja"} },
            { "jie",  new[]{"je"} },          { "jiao", new[]{"ja", "o"} },
            { "jiu",  new[]{"ju"} },          { "jian", new[]{"je", "n"} },
            { "jin",  new[]{"ji", "n"} },     { "jiang",new[]{"ja", "n"} },
            { "jing", new[]{"ji", "n"} },     { "jiong",new[]{"jo", "n"} },
            { "ju",   new[]{"ju"} },          { "jue",  new[]{"ju", "e"} },
            { "juan", new[]{"ju", "e", "n"} },{ "jun",  new[]{"ju", "n"} },

            // q
            { "qi",   new[]{"chi"} },         { "qia",  new[]{"cha"} },
            { "qie",  new[]{"che"} },         { "qiao", new[]{"cha", "o"} },
            { "qiu",  new[]{"chu"} },         { "qian", new[]{"che", "n"} },
            { "qin",  new[]{"chi", "n"} },    { "qiang",new[]{"cha", "n"} },
            { "qing", new[]{"chi", "n"} },    { "qiong",new[]{"cho", "n"} },
            { "qu",   new[]{"chu"} },         { "que",  new[]{"chu", "e"} },
            { "quan", new[]{"chu", "e", "n"} },{ "qun", new[]{"chu", "n"} },

            // x
            { "xi",   new[]{"shi"} },         { "xia",  new[]{"sha"} },
            { "xie",  new[]{"she"} },         { "xiao", new[]{"sha", "o"} },
            { "xiu",  new[]{"shu"} },         { "xian", new[]{"she", "n"} },
            { "xin",  new[]{"shi", "n"} },    { "xiang",new[]{"sha", "n"} },
            { "xing", new[]{"shi", "n"} },    { "xiong",new[]{"sho", "n"} },
            { "xu",   new[]{"shu"} },         { "xue",  new[]{"shu", "e"} },
            { "xuan", new[]{"shu", "e", "n"} },{ "xun", new[]{"shu", "n"} },

            // zh
            { "zha",  new[]{"ja"} },          { "zhe",  new[]{"je"} },
            { "zhi",  new[]{"ji"} },          { "zhai", new[]{"ja", "i"} },
            { "zhao", new[]{"ja", "o"} },     { "zhou", new[]{"jo", "o"} },
            { "zhan", new[]{"ja", "n"} },     { "zhen", new[]{"je", "n"} },
            { "zhang",new[]{"ja", "n"} },     { "zheng",new[]{"je", "n"} },
            { "zhong",new[]{"jo", "n"} },     { "zhu",  new[]{"ju"} },
            { "zhua", new[]{"ju", "a"} },     { "zhuo", new[]{"ju", "o"} },
            { "zhuai",new[]{"ju", "a", "i"} },{ "zhui", new[]{"ju", "i"} },
            { "zhuan",new[]{"ju", "a", "n"} },{ "zhuang",new[]{"ju", "a", "n"} },
            { "zhun", new[]{"ju", "n"} },

            // ch
            { "cha",  new[]{"cha"} },         { "che",  new[]{"che"} },
            { "chi",  new[]{"chi"} },         { "chai", new[]{"cha", "i"} },
            { "chao", new[]{"cha", "o"} },    { "chou", new[]{"cho", "o"} },
            { "chan", new[]{"cha", "n"} },    { "chen", new[]{"che", "n"} },
            { "chang",new[]{"cha", "n"} },    { "cheng",new[]{"che", "n"} },
            { "chong",new[]{"cho", "n"} },    { "chu",  new[]{"chu"} },
            { "chua", new[]{"chu", "a"} },    { "chuo", new[]{"chu", "o"} },
            { "chuai",new[]{"chu", "a", "i"} },{ "chui", new[]{"chu", "i"} },
            { "chuan",new[]{"chu", "a", "n"} },{ "chuang",new[]{"chu", "a", "n"} },
            { "chun", new[]{"chu", "n"} },

            // sh
            { "sha",  new[]{"sha"} },         { "she",  new[]{"she"} },
            { "shi",  new[]{"shi"} },         { "shai", new[]{"sha", "i"} },
            { "shao", new[]{"sha", "o"} },    { "shou", new[]{"sho", "o"} },
            { "shan", new[]{"sha", "n"} },    { "shen", new[]{"she", "n"} },
            { "shang",new[]{"sha", "n"} },    { "sheng",new[]{"she", "n"} },
            { "shu",  new[]{"shu"} },         { "shua", new[]{"shu", "a"} },
            { "shuo", new[]{"shu", "o"} },    { "shuai",new[]{"shu", "a", "i"} },
            { "shui", new[]{"shu", "i"} },    { "shuan",new[]{"shu", "a", "n"} },
            { "shuang",new[]{"shu", "a", "n"} },{ "shun",new[]{"shu", "n"} },

            // r
            { "re",   new[]{"re"} },          { "ri",   new[]{"ri"} },
            { "rao",  new[]{"ra", "o"} },     { "rou",  new[]{"ro", "o"} },
            { "ran",  new[]{"ra", "n"} },     { "ren",  new[]{"re", "n"} },
            { "rang", new[]{"ra", "n"} },     { "reng", new[]{"re", "n"} },
            { "rong", new[]{"ro", "n"} },     { "ru",   new[]{"ru"} },
            { "ruo",  new[]{"ru", "o"} },     { "rui",  new[]{"ru", "i"} },
            { "ruan", new[]{"ru", "a", "n"} },{ "run",  new[]{"ru", "n"} },

            // z
            { "za",   new[]{"za"} },          { "ze",   new[]{"ze"} },
            { "zi",   new[]{"ji"} },          { "zai",  new[]{"za", "i"} },
            { "zao",  new[]{"za", "o"} },     { "zou",  new[]{"zo", "o"} },
            { "zan",  new[]{"za", "n"} },     { "zen",  new[]{"ze", "n"} },
            { "zang", new[]{"za", "n"} },     { "zeng", new[]{"ze", "n"} },
            { "zong", new[]{"zo", "n"} },     { "zu",   new[]{"zu"} },
            { "zuo",  new[]{"zu", "o"} },     { "zui",  new[]{"zu", "i"} },
            { "zuan", new[]{"zu", "a", "n"} },{ "zun",  new[]{"zu", "n"} },

            // c
            { "ca",   new[]{"tsa"} },         { "ce",   new[]{"tse"} },
            { "ci",   new[]{"tsu"} },         { "cai",  new[]{"tsa", "i"} },
            { "cao",  new[]{"tsa", "o"} },    { "cou",  new[]{"tso", "o"} },
            { "can",  new[]{"tsa", "n"} },    { "cen",  new[]{"tse", "n"} },
            { "cang", new[]{"tsa", "n"} },    { "ceng", new[]{"tse", "n"} },
            { "cong", new[]{"tso", "n"} },    { "cu",   new[]{"tsu"} },
            { "cuo",  new[]{"tsu", "o"} },    { "cui",  new[]{"tsu", "i"} },
            { "cuan", new[]{"tsu", "a", "n"} },{ "cun", new[]{"tsu", "n"} },

            // s
            { "sa",   new[]{"sa"} },          { "se",   new[]{"se"} },
            { "si",   new[]{"su"} },          { "sai",  new[]{"sa", "i"} },
            { "sao",  new[]{"sa", "o"} },     { "sou",  new[]{"so", "o"} },
            { "san",  new[]{"sa", "n"} },     { "sen",  new[]{"se", "n"} },
            { "sang", new[]{"sa", "n"} },     { "seng", new[]{"se", "n"} },
            { "song", new[]{"so", "n"} },     { "su",   new[]{"su"} },
            { "suo",  new[]{"su", "o"} },     { "sui",  new[]{"su", "i"} },
            { "suan", new[]{"su", "a", "n"} },{ "sun",  new[]{"su", "n"} },

            // y
            { "yi",   new[]{"i"} },           { "ya",   new[]{"ya"} },
            { "ye",   new[]{"ye"} },          { "yao",  new[]{"ya", "o"} },
            { "you",  new[]{"yo", "o"} },     { "yan",  new[]{"ya", "n"} },
            { "yin",  new[]{"i", "n"} },      { "yang", new[]{"ya", "n"} },
            { "ying", new[]{"i", "n"} },      { "yong", new[]{"yo", "n"} },
            { "yu",   new[]{"yu"} },          { "yue",  new[]{"yu", "e"} },
            { "yuan", new[]{"e", "n"} },      { "yun",  new[]{"yu", "n"} },

            // w
            { "wu",   new[]{"u"} },           { "wa",   new[]{"wa"} },
            { "wo",   new[]{"wo"} },          { "wai",  new[]{"wa", "i"} },
            { "wei",  new[]{"we", "i"} },     { "wan",  new[]{"wa", "n"} },
            { "wen",  new[]{"we", "n"} },     { "wang", new[]{"wa", "n"} },
            { "weng", new[]{"we", "n"} },
        };

        private static readonly Dictionary<string, string> DefaultPolyphoneMap = new() {
            { "chang da", "zhang da" }, { "chang jia", "zhang jia" },
            { "chang bei", "zhang bei" }, { "chang zi", "zhang zi" },
            { "chang guan", "zhang guan" }, { "sheng chang", "sheng zhang" },
            { "le qu", "yue qu" }, { "le dui", "yue dui" },
            { "le qi", "yue qi" }, { "le pu", "yue pu" },
            { "le zhang", "yue zhang" }, { "yin le", "yin yue" },
            { "le jie", "liao jie" }, { "da fu", "dai fu" },
            { "zhong xin", "chong xin" }, { "zhong fu", "chong fu" },
            { "zhong qing", "chong qing" }, { "huan shi", "hai shi" },
            { "zhe ji", "zhao ji" }, { "gan huo", "gan huo" },
        };

        private static readonly HashSet<char> Punctuation = new() {
            '，', '。', '！', '？', '、', '；', '：', '“', '”', '‘', '’',
            '（', '）', '《', '》', '【', '】', '…', '—', '～', '·',
            ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')',
            '[', ']', '{', '}', '<', '>', '/', '\\', '|', '`',
        };

        private static readonly HashSet<char> LongMarks = new() { '~', '～', '—', 'ー' };

        private static readonly Dictionary<char, (char baseChar, int tone)> ToneMarks = new() {
            { 'ā', ('a', 1) }, { 'á', ('a', 2) }, { 'ǎ', ('a', 3) }, { 'à', ('a', 4) },
            { 'ē', ('e', 1) }, { 'é', ('e', 2) }, { 'ě', ('e', 3) }, { 'è', ('e', 4) },
            { 'ī', ('i', 1) }, { 'í', ('i', 2) }, { 'ǐ', ('i', 3) }, { 'ì', ('i', 4) },
            { 'ō', ('o', 1) }, { 'ó', ('o', 2) }, { 'ǒ', ('o', 3) }, { 'ò', ('o', 4) },
            { 'ū', ('u', 1) }, { 'ú', ('u', 2) }, { 'ǔ', ('u', 3) }, { 'ù', ('u', 4) },
            { 'ü', ('v', 0) }, { 'Ü', ('v', 0) },
            { 'ǖ', ('v', 1) }, { 'ǘ', ('v', 2) }, { 'ǚ', ('v', 3) }, { 'ǜ', ('v', 4) },
            { 'ê', ('e', 0) }, { 'Ê', ('e', 0) },
            { 'ế', ('e', 2) }, { 'ề', ('e', 4) }, { 'ể', ('e', 3) },
            { 'ễ', ('e', 3) }, { 'ệ', ('e', 4) },
        };

        private static readonly string[] DigitPinyin = {
            "ling", "yi", "er", "san", "si", "wu", "liu", "qi", "ba", "jiu"
        };

        private readonly Dictionary<string, string[]> fullPinyinMap = new();
        private readonly Dictionary<string, string> polyphoneMap = new();
        private readonly Dictionary<string, List<string>> aliasOverrides = new();
        private readonly Dictionary<(string, int), bool> otoCache = new();
        private readonly Dictionary<int, int> noteTones = new();
        private readonly Dictionary<int, bool> lightToneFlags = new();
        private readonly Dictionary<string, bool> polyphoneDecisions = new();

        private bool askPolyphone = false;
        private bool disableTone3 = false;
        private bool disableLightTone = false;
        private bool disableToneTiming = false;
        private bool disableDoubleChar = false;
        private bool disableVcv = false;
        private string nasalMode = "short";
        private int nasalMs = 120;
        private bool useWildcard = true;

        private USinger currentSinger;

        public ChineseToJapanesePhonemizer() {
            this.vowels = Finals;
            this.consonants = Initials;
            foreach (var kv in DefaultFullPinyinMap) fullPinyinMap[kv.Key] = kv.Value;
            foreach (var kv in DefaultPolyphoneMap) polyphoneMap[kv.Key] = kv.Value;
        }

        protected override string[] GetVowels() => Finals;
        protected override string[] GetConsonants() => Initials;
        protected override string GetDictionaryName() => null;

        public override void SetSinger(USinger singer) {
            base.SetSinger(singer);
            this.currentSinger = singer;
            if (singer == null || !singer.Loaded) return;

            Log.Information($"[ZH to JA v2.1.7] UI compatibility: {(UiCompat.Available ? "available" : "NOT available")}");

            TryLoadConfig(Path.Combine(PluginDir, "zh2ja.yaml"));
            TryLoadConfig(Path.Combine(singer.Location, "zh2ja.yaml"));
            otoCache.Clear();
        }

        private void TryLoadConfig(string path) {
            if (!File.Exists(path)) return;
            try {
                var text = File.ReadAllText(path);
                var cfg = Core.Yaml.DefaultDeserializer.Deserialize<Zh2JaConfig>(text);
                if (cfg == null) return;

                if (cfg.FullPinyin != null) {
                    foreach (var kv in cfg.FullPinyin) {
                        if (kv.Value != null) fullPinyinMap[kv.Key] = kv.Value.ToArray();
                    }
                }
                if (cfg.Polyphones != null) {
                    foreach (var kv in cfg.Polyphones) polyphoneMap[kv.Key] = kv.Value;
                }
                if (cfg.AliasOverrides != null) {
                    foreach (var kv in cfg.AliasOverrides) {
                        if (kv.Value != null) aliasOverrides[kv.Key] = kv.Value;
                    }
                }
                if (cfg.PolyphoneDecisions != null) {
                    foreach (var kv in cfg.PolyphoneDecisions) polyphoneDecisions[kv.Key] = kv.Value;
                }

                askPolyphone = cfg.AskPolyphone;
                disableTone3 = cfg.DisableTone3;
                disableLightTone = cfg.DisableLightTone;
                disableToneTiming = cfg.DisableToneTiming;
                disableDoubleChar = cfg.DisableDoubleChar;
                disableVcv = cfg.DisableVcv;
                nasalMode = cfg.NasalMode ?? "short";
                nasalMs = cfg.NasalMs;
                if (nasalMs < 30) nasalMs = 30;
                if (nasalMs > 500) nasalMs = 500;
                useWildcard = cfg.UseWildcard;

                Log.Information($"[ZH to JA v2.1.7] Loaded config from {path} (nasal_mode={nasalMode}, nasal_ms={nasalMs}, use_wildcard={useWildcard})");
            } catch (Exception e) {
                Log.Error(e, $"Failed to load zh2ja config: {path}");
            }
        }

        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            noteTones.Clear();
            lightToneFlags.Clear();

            PreprocessNotes(groups);
            BaseChinesePhonemizer.RomanizeNotes(groups);
            FixPolyphones(groups);
            base.SetUp(groups, project, track);
        }

        private void PreprocessNotes(Note[][] groups) {
            foreach (var group in groups) {
                if (group == null || group.Length == 0) continue;
                var note = group[0];
                string lyric = note.lyric;
                if (string.IsNullOrEmpty(lyric)) continue;

                string trimmed = lyric.Trim();
                if (trimmed == "-" || trimmed == "R" || trimmed == "+") continue;

                lyric = ReplacePunctuation(lyric);

                var (baseLyric, tone) = ParseToneMarks(lyric);
                lyric = baseLyric;

                bool isExplicitLight = (tone == 5);
                bool hasExplicitTone = (tone >= 1 && tone <= 4);
                bool isLightChar = LightToneChars.Contains(lyric);

                if (!disableLightTone && !hasExplicitTone && (isExplicitLight || isLightChar)) {
                    lightToneFlags[note.position] = true;
                }

                if (tone >= 1 && tone <= 4) {
                    noteTones[note.position] = tone;
                }

                lyric = ReplaceDigits(lyric);
                lyric = StripLongMarks(lyric);

                group[0] = new Note {
                    lyric = lyric,
                    phoneticHint = note.phoneticHint,
                    tone = note.tone,
                    position = note.position,
                    duration = note.duration,
                    phonemeAttributes = note.phonemeAttributes,
                };
            }
        }

        private static (string, int) ParseToneMarks(string s) {
            var sb = new StringBuilder(s.Length);
            int tone = 0;
            foreach (char c in s) {
                if (ToneMarks.TryGetValue(c, out var m)) {
                    sb.Append(m.baseChar);
                    tone = m.tone;
                } else {
                    sb.Append(c);
                }
            }
            string result = sb.ToString();

            if (result.Length >= 2) {
                char last = result[^1];
                char prev = result[^2];
                if (char.IsDigit(last) && char.IsLetter(prev)) {
                    int t = last - '0';
                    if (t >= 1 && t <= 5) {
                        tone = t;
                        result = result[..^1];
                    }
                }
            }
            return (result, tone);
        }

        private static string ReplacePunctuation(string s) {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) {
                if (Punctuation.Contains(c)) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private static string ReplaceDigits(string s) {
            if (!s.Any(char.IsDigit)) return s;
            var sb = new StringBuilder();
            var digits = new List<int>();
            foreach (char c in s) {
                if (char.IsDigit(c)) digits.Add(c - '0');
                else {
                    if (digits.Count > 0) {
                        sb.Append(ConvertDigitSequence(digits));
                        sb.Append(' ');
                        digits.Clear();
                    }
                    sb.Append(c);
                }
            }
            if (digits.Count > 0) sb.Append(ConvertDigitSequence(digits));
            return sb.ToString();
        }

        private static string ConvertDigitSequence(List<int> digits) {
            if (digits.Count == 1) return DigitPinyin[digits[0]];
            long num = 0;
            foreach (var d in digits) num = num * 10 + d;
            if (num == 10) return "shi";
            if (num < 20) return "shi " + DigitPinyin[num - 10];
            if (num < 100) {
                int t = (int)(num / 10);
                int o = (int)(num % 10);
                return o == 0
                    ? DigitPinyin[t] + " shi"
                    : DigitPinyin[t] + " shi " + DigitPinyin[o];
            }
            return string.Join(" ", digits.Select(d => DigitPinyin[d]));
        }

        private static string StripLongMarks(string s) {
            return new string(s.Where(c => !LongMarks.Contains(c)).ToArray()).Trim();
        }

        private void FixPolyphones(Note[][] groups) {
            var lyrics = new List<string>();
            var refs = new List<(int gi, int ni)>();

            for (int i = 0; i < groups.Length; i++) {
                if (groups[i].Length == 0) continue;
                var note = groups[i][0];
                if (!string.IsNullOrEmpty(note.phoneticHint)) lyrics.Add("__skip__");
                else lyrics.Add(note.lyric.ToLowerInvariant());
                refs.Add((i, 0));
            }

            for (int i = 0; i < lyrics.Count; i++) {
                if (lyrics[i] == "__skip__") continue;

                for (int len = Math.Min(4, lyrics.Count - i); len >= 2; len--) {
                    bool hasSkip = false;
                    for (int j = 0; j < len; j++) {
                        if (lyrics[i + j] == "__skip__") { hasSkip = true; break; }
                    }
                    if (hasSkip) continue;

                    string pattern = string.Join(" ", lyrics.Skip(i).Take(len));
                    if (!polyphoneMap.TryGetValue(pattern, out var replacement)) continue;

                    bool useCorrection;
                    if (polyphoneDecisions.TryGetValue(pattern, out bool cached)) {
                        useCorrection = cached;
                    } else if (askPolyphone) {
                        useCorrection = AskPolyphoneUser(pattern,
                            lyrics.Skip(i).Take(len).ToArray(),
                            replacement.Split(' '));
                        polyphoneDecisions[pattern] = useCorrection;
                    } else {
                        polyphoneDecisions[pattern] = true;
                        useCorrection = true;
                    }

                    if (!useCorrection) continue;

                    var parts = replacement.Split(' ');
                    if (parts.Length != len) break;

                    for (int j = 0; j < len; j++) {
                        var (gi, ni) = refs[i + j];
                        var oldNote = groups[gi][ni];
                        groups[gi][ni] = new Note {
                            lyric = parts[j],
                            phoneticHint = oldNote.phoneticHint,
                            tone = oldNote.tone,
                            position = oldNote.position,
                            duration = oldNote.duration,
                            phonemeAttributes = oldNote.phonemeAttributes,
                        };
                        lyrics[i + j] = parts[j];
                    }
                    i += len - 1;
                    break;
                }
            }

            FixYiBuToneChange(lyrics, groups, refs);

            if (!disableDoubleChar) {
                for (int i = 0; i < lyrics.Count - 1; i++) {
                    if (lyrics[i] == "__skip__" || string.IsNullOrEmpty(lyrics[i])) continue;
                    if (lyrics[i] == lyrics[i + 1]) {
                        var (gi, ni) = refs[i + 1];
                        lightToneFlags[groups[gi][ni].position] = true;
                    }
                }
            }

            for (int i = 0; i < lyrics.Count - 1; i++) {
                if (lyrics[i] == "__skip__") continue;
                if (lyrics[i] == "er") continue;
                if (lyrics[i + 1] != "er") continue;
                if (!IsSingleSyllable(lyrics[i])) continue;

                var (gi, ni) = refs[i + 1];
                var oldNote = groups[gi][ni];
                groups[gi][ni] = new Note {
                    lyric = "",
                    phoneticHint = null,
                    tone = oldNote.tone,
                    position = oldNote.position,
                    duration = oldNote.duration,
                    phonemeAttributes = oldNote.phonemeAttributes,
                };
                lyrics[i + 1] = "";
            }
        }

        private void FixYiBuToneChange(List<string> lyrics, Note[][] groups, List<(int gi, int ni)> refs) {
            for (int i = 0; i < lyrics.Count - 1; i++) {
                if (lyrics[i] == "__skip__") continue;

                string cur = StripDigits(lyrics[i]);
                if (cur != "yi" && cur != "bu") continue;

                int nextPos = groups[refs[i + 1].gi][refs[i + 1].ni].position;
                bool nextIs4th = noteTones.TryGetValue(nextPos, out int t) && t == 4;
                if (!nextIs4th) continue;

                string newLyric = cur + "2";
                var (gi, ni) = refs[i];
                var oldNote = groups[gi][ni];
                groups[gi][ni] = new Note {
                    lyric = newLyric,
                    phoneticHint = oldNote.phoneticHint,
                    tone = oldNote.tone,
                    position = oldNote.position,
                    duration = oldNote.duration,
                    phonemeAttributes = oldNote.phonemeAttributes,
                };
                lyrics[i] = newLyric;
                noteTones[oldNote.position] = 2;
            }
        }

        private static string StripDigits(string s) =>
            new string(s.Where(c => !char.IsDigit(c)).ToArray());

        private bool IsSingleSyllable(string lyric) => ParsePinyin(lyric) != null;

        private bool AskPolyphoneUser(string pattern, string[] original, string[] corrected) {
            if (!UiCompat.Available) return true;

            try {
                string origStr = string.Join(" ", original);
                string corrStr = string.Join(" ", corrected);

                string msg =
                    $"检测到多音字：\n\n" +
                    $"  原始识别：{origStr}\n" +
                    $"  建议修正：{corrStr}\n\n" +
                    $"是否采用建议读音？\n\n" +
                    $"「是」= 使用修正读音\n" +
                    $"「否」= 保持原始识别";

                int result = MessageBoxW(IntPtr.Zero, msg, "ZH to JA - 多音字确认",
                    0x4 | 0x20 | 0x40000);

                return result == 6;
            } catch {
                return true;
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next,
                                       Note? prevNeighbour, Note? nextNeighbour,
                                       Note[] prevNeighbours) {
            if (string.IsNullOrEmpty(notes[0].lyric))
                return new Result { phonemes = new Phoneme[0] };

            string rawLyric = notes[0].lyric;
            if (!string.IsNullOrEmpty(notes[0].phoneticHint))
                rawLyric = notes[0].phoneticHint;

            var parts = rawLyric.Split(new[] { ' ', ',', '，', '、', '/', '|' },
                StringSplitOptions.RemoveEmptyEntries);

            Result result;
            if (parts.Length > 1)
                result = ProcessMultipleSyllables(parts, notes, prevNeighbour);
            else
                result = base.Process(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours);

            ShortenNasalTail(result, notes, next.HasValue);

            int notePos = notes[0].position;
            if (!disableLightTone && lightToneFlags.TryGetValue(notePos, out bool isLight) && isLight)
                ApplyLightTone(result, notes);

            if (!disableToneTiming && noteTones.TryGetValue(notePos, out int tone)
                && tone >= 1 && tone <= 4)
                ApplyToneTiming(result, notes, tone);

            return result;
        }

        private void ApplyLightTone(Result result, Note[] notes) {
            if (result.phonemes == null || result.phonemes.Length < 2) return;
            int totalDuration = notes.Sum(n => n.duration);
            int last = result.phonemes.Length - 1;
            int lastPos = result.phonemes[last].position;
            int currentDur = totalDuration - lastPos;
            int newDur = Math.Max(25, (int)(currentDur * 0.6));
            int newPos = totalDuration - newDur;

            if (newPos > 0 && newPos < totalDuration) {
                var p = result.phonemes[last];
                p.position = newPos;
                result.phonemes[last] = p;
            }
        }

        private void ApplyToneTiming(Result result, Note[] notes, int tone) {
            if (result.phonemes == null || result.phonemes.Length < 2) return;
            int totalDuration = notes.Sum(n => n.duration);
            int last = result.phonemes.Length - 1;

            float ratio = tone switch {
                1 => 1.00f, 2 => 0.95f, 3 => 1.10f, 4 => 0.75f, _ => 1.00f,
            };

            int lastPos = result.phonemes[last].position;
            int currentDur = totalDuration - lastPos;
            int newDur = Math.Max(25, (int)(currentDur * ratio));
            int newPos = totalDuration - newDur;

            if (newPos > 0 && newPos < totalDuration) {
                var p = result.phonemes[last];
                p.position = newPos;
                result.phonemes[last] = p;
            }
        }

        // =============================================================
        // v2.1.6 鼻音韵尾处理（KOtoJA 风格：末尾固定毫秒）
        // =============================================================
        private void ShortenNasalTail(Result result, Note[] notes, bool hasNext) {
            if (result.phonemes == null || result.phonemes.Length == 0) return;
            if (nasalMode == "full") return;

            int totalDuration = notes.Sum(n => n.duration);
            if (totalDuration <= 0) return;

            for (int i = result.phonemes.Length - 1; i >= 0; i--) {
                var p = result.phonemes[i];
                if (string.IsNullOrEmpty(p.phoneme)) continue;

                string ph = p.phoneme.ToLowerInvariant();
                bool isNasalTail = ph == "ん" || ph == "ン" || ph == "n" ||
                                   ph.EndsWith("ん") || ph.EndsWith("ン");
                if (!isNasalTail) break;

                if (result.phonemes.Length == 1) break;

                if (nasalMode == "none") {
                    var np = p;
                    np.position = totalDuration;
                    result.phonemes[i] = np;
                    continue;
                }

                int targetMs = hasNext ? (int)(nasalMs * 1.5) : nasalMs;
                int targetStart = totalDuration - targetMs;
                int minStart = totalDuration / 2;
                if (targetStart < minStart) targetStart = minStart;

                if (targetStart <= p.position) continue;

                var np2 = p;
                np2.position = targetStart;
                result.phonemes[i] = np2;
            }
        }

        protected override string[] GetSymbols(Note note) {
            if (!string.IsNullOrEmpty(note.phoneticHint))
                return note.phoneticHint.Split(
                    new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string lyric = note.lyric;
            if (string.IsNullOrEmpty(lyric)) return null;
            if (lyric == "-" || lyric == "R" || lyric == "+") return new[] { lyric };

            if (ContainsJapaneseKana(lyric))
                return lyric.Split(
                    new[] { ' ', ',', '，', '、' },
                    StringSplitOptions.RemoveEmptyEntries);

            return ParsePinyin(lyric);
        }

        private static bool ContainsJapaneseKana(string s) {
            foreach (char c in s) {
                if ((c >= 0x3040 && c <= 0x309F) ||
                    (c >= 0x30A0 && c <= 0x30FF) ||
                    (c >= 0xFF66 && c <= 0xFF9D))
                    return true;
            }
            return false;
        }

        private string[] ParsePinyin(string pinyin) {
            pinyin = new string(pinyin.Where(c => c >= 'a' && c <= 'z').ToArray()).ToLowerInvariant();
            if (string.IsNullOrEmpty(pinyin)) return null;

            if (pinyin == "n" || pinyin == "ng") return new[] { "N" };
            if (pinyin == "zh") pinyin = "zhi";
            else if (pinyin == "ch") pinyin = "chi";
            else if (pinyin == "sh") pinyin = "shi";

            if (pinyin.StartsWith("y")) {
                string rest = pinyin.Substring(1);
                if (rest == "i" || rest == "in" || rest == "ing") return new[] { rest };
                if (rest.StartsWith("u")) {
                    string after = "v" + rest.Substring(1);
                    if (Finals.Contains(after)) return new[] { after };
                    return null;
                }
                if (Finals.Contains(rest)) return new[] { "y", rest };
                return null;
            }

            if (pinyin.StartsWith("w")) {
                string rest = pinyin.Substring(1);
                if (rest == "u") return new[] { "u" };
                if (Finals.Contains(rest)) return new[] { "w", rest };
                return null;
            }

            if (Finals.Contains(pinyin)) return new[] { pinyin };

            foreach (var init in Initials) {
                if (pinyin.StartsWith(init)) {
                    string remaining = pinyin.Substring(init.Length);
                    if (Finals.Contains(remaining))
                        return new[] { init, remaining };
                }
            }
            return null;
        }

        protected override List<string> ProcessSyllable(Syllable syllable) {
            var phonemes = new List<string>();
            if (string.IsNullOrEmpty(syllable.v)) return phonemes;

            var moras = GenerateMoras(syllable.cc, syllable.v);
            if (moras.Count == 0) return phonemes;

            string prevJaVowel = ConvertToJapaneseVowel(syllable.prevV);
            string prevMora = null;

            for (int i = 0; i < moras.Count; i++) {
                string mora = moras[i];
                string alias = null;

                // 1. 优先尝试带 * 的过渡音素
                if (useWildcard && !disableVcv) {
                    if (i == 0) {
                        if (!string.IsNullOrEmpty(prevJaVowel) && prevJaVowel != "-") {
                            alias = PickAlias(syllable.vowelTone,
                                $"{prevJaVowel} * {mora}", $"{prevJaVowel} *{mora}",
                                $"{prevJaVowel}* {mora}", $"{prevJaVowel}*{mora}",
                                $"{prevJaVowel} {mora}", $"{prevJaVowel}{mora}",
                                $"{prevJaVowel} *", $"* {mora}",
                                mora);
                        }
                    } else {
                        if (!string.IsNullOrEmpty(prevMora)) {
                            string prevVowel = GetLastJapaneseVowel(prevMora);
                            if (!string.IsNullOrEmpty(prevVowel) && prevVowel != "-") {
                                alias = PickAlias(syllable.vowelTone,
                                    $"{prevVowel} * {mora}", $"{prevVowel} *{mora}",
                                    $"{prevVowel}* {mora}", $"{prevVowel}*{mora}",
                                    $"{prevVowel} {mora}", $"{prevVowel}{mora}",
                                    $"{prevVowel} *", $"* {mora}",
                                    mora);
                            }
                        }
                    }
                }

                // 2. 回退到标准逻辑
                if (string.IsNullOrEmpty(alias) || alias == mora) {
                    if (i == 0) {
                        if (disableVcv || string.IsNullOrEmpty(prevJaVowel) || prevJaVowel == "-") {
                            alias = PickAlias(syllable.vowelTone, $"- {mora}", $"-{mora}", mora);
                        } else {
                            alias = PickAlias(syllable.vowelTone, $"{prevJaVowel} {mora}", $"{prevJaVowel}{mora}", mora);
                        }
                    } else {
                        alias = PickAlias(syllable.vowelTone, mora);
                    }
                }

                phonemes.Add(alias);
                prevMora = mora;
            }
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            var phonemes = new List<string>();
            string prevV = ConvertToJapaneseVowel(ending.prevV);
            if (string.IsNullOrEmpty(prevV)) return phonemes;

            if (ending.cc != null && ending.cc.Length > 0) {
                foreach (var c in ending.cc) {
                    if (string.IsNullOrEmpty(c)) continue;
                    string cons = InitialMap.TryGetValue(c, out var jc) ? jc : c;

                    if (HasOtoCached($"{prevV} {cons}", ending.tone)) {
                        phonemes.Add($"{prevV} {cons}");
                    } else if (HasOtoCached($"{prevV}{cons}", ending.tone)) {
                        phonemes.Add($"{prevV}{cons}");
                    }
                }
            }

            if (phonemes.Count == 0) {
                if (HasOtoCached($"{prevV} R", ending.tone)) {
                    phonemes.Add($"{prevV} R");
                } else if (HasOtoCached($"{prevV} -", ending.tone)) {
                    phonemes.Add($"{prevV} -");
                }
            }

            return phonemes;
        }

        private Result ProcessMultipleSyllables(string[] parts, Note[] notes, Note? prevNeighbour) {
            var phonemes = new List<Phoneme>();
            int totalDuration = notes.Sum(n => n.duration);
            int perSyllable = totalDuration / parts.Length;

            string prevV = "-";
            if (prevNeighbour.HasValue) {
                string prevLyric = prevNeighbour.Value.lyric;
                if (!string.IsNullOrEmpty(prevNeighbour.Value.phoneticHint))
                    prevLyric = prevNeighbour.Value.phoneticHint;
                var prevParts = prevLyric.Split(
                    new[] { ' ', ',', '，', '、' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (prevParts.Length > 0) {
                    var parsedPrev = ParsePinyin(prevParts.Last());
                    if (parsedPrev != null && parsedPrev.Length > 0)
                        prevV = ConvertToJapaneseVowel(parsedPrev[parsedPrev.Length - 1]);
                }
            }

            int cursor = 0;
            for (int i = 0; i < parts.Length; i++) {
                var parsed = ParsePinyin(parts[i]);
                if (parsed == null) continue;

                string[] cc = parsed.Length > 1
                    ? parsed.Take(parsed.Length - 1).ToArray()
                    : new string[0];
                string v = parsed[parsed.Length - 1];
                if (string.IsNullOrEmpty(v)) continue;

                var moras = GenerateMoras(cc, v);
                if (moras.Count == 0) continue;

                int moraStep = Math.Max(1, perSyllable / moras.Count);

                for (int j = 0; j < moras.Count; j++) {
                    string alias;
                    if (j == 0) {
                        if (disableVcv || string.IsNullOrEmpty(prevV) || prevV == "-")
                            alias = PickAlias(notes[0].tone,
                                $"- {moras[j]}", $"-{moras[j]}", moras[j]);
                        else
                            alias = PickAlias(notes[0].tone,
                                $"{prevV} {moras[j]}", $"{prevV}{moras[j]}", moras[j]);
                    } else {
                        alias = PickAlias(notes[0].tone, moras[j]);
                    }

                    phonemes.Add(new Phoneme { phoneme = alias, position = cursor });
                    cursor += moraStep;
                }

                prevV = ConvertToJapaneseVowel(v);
            }

            return new Result { phonemes = phonemes.ToArray() };
        }

        private List<string> GenerateMoras(string[] cc, string final) {
            var moras = new List<string>();

            string rawInitial = (cc != null && cc.Length > 0) ? cc[0] : "";
            string fullPinyin = rawInitial + final;

            if (fullPinyinMap.TryGetValue(fullPinyin, out var mapped)) {
                foreach (var m in mapped) {
                    moras.Add(ToKana(m));
                }
                return moras;
            }

            string cons = "";
            if (cc != null && cc.Length > 0)
                cons = InitialMap.TryGetValue(cc[0], out var jc) ? jc : cc[0];

            var vowelSeq = GetJapaneseVowelSequence(final);
            if (vowelSeq.Count == 0) return moras;

            if (!string.IsNullOrEmpty(cons)) {
                string firstV = vowelSeq[0];
                vowelSeq.RemoveAt(0);

                if (firstV == "N") {
                    moras.Add(ToKana(cons + "u"));
                    moras.Add("ん");
                } else {
                    moras.Add(ToKana(cons + firstV));
                }
            }

            foreach (var v in vowelSeq) {
                if (v == "N") moras.Add("ん");
                else moras.Add(ToKana(v));
            }

            return moras;
        }

        private List<string> GetJapaneseVowelSequence(string pinyinFinal) {
            switch (pinyinFinal) {
                case "a": return new List<string> { "a" };
                case "o": return new List<string> { "o" };
                case "e": return new List<string> { "e" };
                case "i": return new List<string> { "i" };
                case "u": return new List<string> { "u" };
                case "v": return new List<string> { "yu" };
                case "er": return new List<string> { "a" };

                case "ai": return new List<string> { "a", "i" };
                case "ei": return new List<string> { "e", "i" };
                case "ao": return new List<string> { "a", "o" };
                case "ou": return new List<string> { "o", "o" };

                case "an": return new List<string> { "a", "N" };
                case "en": return new List<string> { "e", "N" };
                case "in": return new List<string> { "i", "N" };
                case "un": return new List<string> { "u", "N" };
                case "ang": return new List<string> { "a", "N" };
                case "eng": return new List<string> { "e", "N" };
                case "ing": return new List<string> { "i", "N" };
                case "ong": return new List<string> { "o", "N" };
                case "ian": return new List<string> { "i", "e", "N" };
                case "iang": return new List<string> { "i", "a", "N" };
                case "iong": return new List<string> { "i", "o", "N" };
                case "uan": return new List<string> { "u", "a", "N" };
                case "uang": return new List<string> { "u", "a", "N" };
                case "ueng": return new List<string> { "u", "e", "N" };
                case "van": return new List<string> { "yu", "e", "N" };
                case "vn": return new List<string> { "yu", "N" };

                case "ia": return new List<string> { "i", "a" };
                case "ie": return new List<string> { "i", "e" };
                case "iu": return new List<string> { "i", "u" };
                case "iao": return new List<string> { "i", "a", "o" };

                case "ua": return new List<string> { "u", "a" };
                case "uo": return new List<string> { "u", "o" };
                case "ui": return new List<string> { "u", "i" };
                case "uai": return new List<string> { "u", "a", "i" };

                case "ue": return new List<string> { "yu", "e" };
                case "ve": return new List<string> { "yu", "e" };

                default: return new List<string> { pinyinFinal };
            }
        }

        private string ToKana(string romaji) {
            if (string.IsNullOrEmpty(romaji)) return romaji;
            if (RomajiToKana.TryGetValue(romaji, out var kana)) return kana;
            return romaji;
        }

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

        private string GetLastJapaneseVowel(string mora) {
            if (string.IsNullOrEmpty(mora)) return "-";
            if (mora.EndsWith("ん")) return "n";
            if (mora.EndsWith("ゃ")) return "a";
            if (mora.EndsWith("ゅ")) return "u";
            if (mora.EndsWith("ょ")) return "o";

            char last = mora.Last();
            if ("あかさたなはまやらわがざだばぱ".Contains(last)) return "a";
            if ("いきしちにひみりぎじぢびぴ".Contains(last)) return "i";
            if ("うくすつぬふむゆるぐずづぶぷ".Contains(last)) return "u";
            if ("えけせてねへめれげぜでべぺ".Contains(last)) return "e";
            if ("おこそとのほもよろごぞどぼぽ".Contains(last)) return "o";
            return "-";
        }

        private string PickAlias(int tone, params string[] candidates) {
            foreach (var c in candidates) {
                if (HasOtoCached(c, tone)) return c;

                foreach (var variant in ExpandAliasVariants(c)) {
                    if (variant != c && HasOtoCached(variant, tone)) return variant;
                }
            }
            return candidates.Last();
        }

        private IEnumerable<string> ExpandAliasVariants(string alias) {
            if (aliasOverrides.Count == 0) yield break;

            string bare = alias;
            string prefix = "";
            int lastSpace = alias.LastIndexOf(' ');
            if (lastSpace >= 0 && lastSpace < alias.Length - 1) {
                bare = alias.Substring(lastSpace + 1);
                prefix = alias.Substring(0, lastSpace + 1);
            }

            if (aliasOverrides.TryGetValue(bare, out var alts)) {
                foreach (var alt in alts) yield return prefix + alt;
            }
        }

        private bool HasOtoCached(string alias, int tone) {
            var key = (alias, tone);
            if (!otoCache.TryGetValue(key, out bool result)) {
                result = HasOto(alias, tone);
                otoCache[key] = result;
            }
            return result;
        }
    }
}