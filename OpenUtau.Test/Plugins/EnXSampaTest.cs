using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class EnXSampaTest : PhonemizerTestBase {
        public EnXSampaTest(ITestOutputHelper output) : base(output) { }
        protected override Phonemizer CreatePhonemizer() {
            return new EnXSampaPhonemizer();
        }

        [Theory]
        [InlineData("en_delta0",
            new string[] { "my", "test" },
            new string[] { "- maI", "aI t", "tE", "E st-" })]
        [InlineData("en_delta7",
            new string[] { "my", "test" },
            new string[] { "maI", "aI -", "tE", "E s", "s t-" })]
        public void BasicPhonemizingTest(string singerName, string[] lyrics, string[] aliases) {
            SameAltsTonesColorsTest(singerName, lyrics, aliases, "", "C4", "");
        }

        [Fact]
        public void ToneShiftTest() {
            RunPhonemizeTest("en_delta0", new NoteParams[] {
                new NoteParams {
                    lyric = "hi",
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
                            shift = 12,
                            color = "",
                        },
                    }
                }
            }, new string[] { "- haI", "aI -_H" });
        }

        [Theory]
        [InlineData("read", "", new string[] { "- rE", "E d-" })]
        [InlineData("read", "r i d", new string[] { "- ri", "i d-" })]

        [InlineData("asdfjkl", "r i d", new string[] { "- ri", "i d-" })]
        [InlineData("", "r i d", new string[] {"- ri", "i d-" })]
        public void HintTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_delta0", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C4", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }
    }
}
