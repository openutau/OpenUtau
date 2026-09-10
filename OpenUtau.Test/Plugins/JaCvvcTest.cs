using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class JaCvvcTest : PhonemizerTestBase {
        public JaCvvcTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new JapaneseCVVCPhonemizer();
        }

        [Theory]
        [InlineData("ja_cvvc",
            new string[] { "あ", "+", "あ", "-" },
            new string[] { "C4", "C4", "C4", "C4" },
            new string[] { "", "", "弱", "" },
            new string[] { "- あ_C4", "a あ_弱C4", "-" })]
        [InlineData("ja_cvvc",
            new string[] { "ラ", "リ", "ル", "ら" },
            new string[] { "C4", "C4", "C4", "C4" },
            new string[] { "", "", "", "" },
            new string[] { "ラ_C4", "a ly_C4", "リ_C4", "i l_C4", "ル_C4", "u r_C4", "ら_C4" })]
        [InlineData("ja_cvvc",
            new string[] { "\u304c", "\u304b\u3099", "\u30f4", "\u30a6\u3099" }, // が, が, ヴ, ヴ
            new string[] { "A3", "C4", "D4", "E4" },
            new string[] { "", "", "", "" },
            new string[] { "が_A3", "a g_A3", "が_C4", "a v_C4", "ヴ_C4", "u v_C4", "ヴ_F4" })]
        public void PhonemizeTest(string singerName, string[] lyrics, string[] tones, string[] colors, string[] aliases) {
            RunPhonemizeTest(singerName, lyrics, RepeatString(lyrics.Length, ""), tones, colors, aliases);
        }

        [Fact]
        public void ColorTest() {
            RunPhonemizeTest("ja_cvvc", new NoteParams[] { 
               new NoteParams {
                   lyric = "あ",
                   hint = "",
                   tone = "C4",
                   phonemes = new PhonemeParams[] {
                       new PhonemeParams {
                           alt = 0,
                           shift = 0,
                           color = "",
                       },
                       new PhonemeParams {
                           alt = 0,
                           shift = 0,
                           color = "強",
                       }
                   }
               },
               new NoteParams { lyric = "か", hint = "", tone = "C4", phonemes = SamePhonemeParams(1, 0, 0, "") }
            }, new string[] { "- あ_C4", "a k_強B3", "か_C4" });
        }
    }
}
