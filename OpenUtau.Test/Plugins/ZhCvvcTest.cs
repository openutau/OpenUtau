using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins
{
    public class ZhCvvcTest : PhonemizerTestBase{
        public ZhCvvcTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new ChineseCVVCPhonemizer();
        }

        //The voicebank used for testing here contains 3 pitches: C#4, F4, A#4
        //F4 and A#4 contains starting phonemes, such as "- a" "- zhuang", while C#4 doesn't.

        [Theory]
        //Test for slur notes and starting phonemes
        //If "- zha" doesn't exist, should fall back to "zha"
        [InlineData("zh_cvvc",
            new string[] { "zha", "+", "a", "R" },
            new string[] { "", "", "", "" },
            new string[] { "C4", "C4", "C4", "C4" },
            new string[] { "", "", "", "" },
            new string[] { "zhaC#4", "a aC#4", "a RC#4" })]
        [InlineData("zh_cvvc",
            new string[] { "zha", "+", "a", "R" },
            new string[] { "", "", "", "" },
            new string[] { "B4", "B4", "B4", "B4" },
            new string[] { "", "", "", "" },
            new string[] { "- zhaA#4", "a aA#4", "a RA#4" })]

        //Test for cross-subbank VC.
        //If the previous note and the current note belongs to different subbanks, 
        //the VC between them should use the same subbank with the previous note.
        [InlineData("zh_cvvc",
            new string[] { "ni", "hao", "R" },
            new string[] { "", "", "" },
            new string[] { "F4", "C4", "C4" },
            new string[] { "", "", "" },
            new string[] { "- niF4", "i hF4", "haoC#4", "ao RC#4" })]

        //Mixed hanzi and pinyin input
        [InlineData("zh_cvvc",
            new string[] { "鸡", "ni", "tai", "美" },
            new string[] { "", "", "", "" },
            new string[] { "F4", "F4", "F4", "F4" },
            new string[] { "", "", "", "" },
            new string[] { "- jiF4", "i nyF4", "niF4", "i tF4", "taiF4", "ai mF4", "meiF4", "ei RF4" })]
        
        public void PhonemizeTest(string singerName, string[] lyrics, string[] alts, string[] tones, string[] colors, string[] aliases) {
            RunPhonemizeTest(singerName, lyrics, alts, tones, colors, aliases);
        }
    }
}
