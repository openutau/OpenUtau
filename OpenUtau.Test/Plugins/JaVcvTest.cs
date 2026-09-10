using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class JaVcvTest : PhonemizerTestBase {
        public JaVcvTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new JapaneseVCVPhonemizer();
        }

        [Theory]
        [InlineData("ja_vcv",
            new string[] { "あ", "+", "あ", "-" },
            new string[] { "C4", "C4", "C4", "C4" },
            new string[] { "", "", "Clear", "" },
            new string[] { "- あA3", "a あCA3", "-" })]
        [InlineData("ja_vcv",
            new string[] { "お", "にょ", "ひょ", "みょ", "びょ", "ぴょ", "りょ" },
            new string[] { "A3", "C4", "D4", "E4", "F4", "G3", "F3" },
            new string[] { "", "Clear", "", "", "Whisper", "", "" },
            new string[] { "- おA3", "o にょCA3", "o ひょD4", "o みょD4", "o びょWD4", "o ぴょA3", "o りょA3" })]
        [InlineData("ja_vcv",
            new string[] { "- ず", "u と", "o R" },
            new string[] { "A3", "C4", "D4" },
            new string[] { "", "", "" },
            new string[] { "- ずA3", "u とA3", "o RD4" })]
        [InlineData("ja_vcv",
            new string[] { "\u304c", "\u304b\u3099", "\u30f4", "\u30a6\u3099" }, // が, が, ヴ, ヴ
            new string[] { "A3", "C4", "D4", "E4" },
            new string[] { "", "", "", "" },
            new string[] { "- がA3", "a がA3", "a ヴD4", "u ヴD4" })]
        public void PhonemizeTest(string singerName, string[] lyrics, string[] tones, string[] colors, string[] aliases) {
            RunPhonemizeTest(singerName, lyrics, RepeatString(lyrics.Length, ""), tones, colors, aliases);
        }
    }
}
