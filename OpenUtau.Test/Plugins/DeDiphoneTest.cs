using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class DeDiphoneTest : PhonemizerTestBase {
        public DeDiphoneTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new GermanDiphonePhonemizer();
        }

        [Theory]
        [InlineData("de_diphone",
            new string[] { "guten", "Tag" },
            new string[] { "C4", "C4" },
            new string[] { "- g_C4", "g uw_C4", "uw t_C4", "t ax_C4", "ax n_C4", "n t", "t aa_C4", "aa k_C4", "k -_C4" })]
        [InlineData("de_diphone",
            new string[] { "guten", "+", "Tag" },
            new string[] { "F3", "F4", "C4" },
            new string[] { "- g_F3", "g uw_F3", "uw t_F4", "t ax_F4", "ax n_F4", "n t", "t aa_C4", "aa k_C4", "k -_C4" })]
        [InlineData("de_diphone",
            new string[] { "Mond", "+", "+", "+", "Licht", "+" },
            new string[] { "F4", "C4", "F4", "F4", "C4", "F4" },
            new string[] { "- m_F4", "m ooh_F4", "ooh n_F4", "n t", "t l", "l ih_C4", "ih cc_F4", "cc t", "t -_F4" })]
        public void PhonemizeTest(string singerName, string[] lyrics, string[] tones, string[] aliases) {
            RunPhonemizeTest(singerName, lyrics, RepeatString(lyrics.Length, ""), tones, RepeatString(lyrics.Length, ""), aliases);
        }

        [Fact]
        public void ToneShiftTest() {
            RunPhonemizeTest("de_diphone", new NoteParams[] {
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
                        new PhonemeParams {
                            alt = 0,
                            shift = 0,
                            color = "",
                        }
                    }
                }
            }, new string[] { "- hh_C4", "hh ay_F4", "ay -_C4" });
        }

        [Theory]
        [InlineData("Zug", "", new string[] { "- ts_C4", "ts uw_C4", "uw k_C4", "k -_C4" })]

        [InlineData("asdfjkl", "ts uw k", new string[] { "- ts_C4", "ts uw_C4", "uw k_C4", "k -_C4" })]
        [InlineData("", "ts uw k", new string[] { "- ts_C4", "ts uw_C4", "uw k_C4", "k -_C4" })]
        public void HintTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("de_diphone", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C4", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }
    }
}
