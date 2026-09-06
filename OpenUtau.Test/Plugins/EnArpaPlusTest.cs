using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class EnArpaPlusTest : PhonemizerTestBase {
        public EnArpaPlusTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new ArpasingPlusPhonemizer();
        }

        [Theory]
        [InlineData("en_arpa-plus",
            new string[] { "good", "morning", },
            new string[] { "A#3", "A#3" },
            new string[] { "", "" },
            new string[] { "- g_C3", "g uh_C3", "uh d_C3", "d m_C3", "m ao1_C3", "ao r6_C3", "r n_C3", "n ih_C3", "ih ng10_C3", "ng -5_C3" })]
        [InlineData("en_arpa-plus",
            new string[] { "good", "morning" },
            new string[] { "C3", "C3" },
            new string[] { "", "" },
            new string[] { "- g_C3", "g uh_C3", "uh d_C3", "d m_C3", "m ao1_C3", "ao r6_C3", "r n_C3", "n ih_C3", "ih ng10_C3", "ng -5_C3" })]
        public void PhonemizeTest(string singerName, string[] lyrics, string[] tones, string[] colors, string[] aliases) {
            RunPhonemizeTest(singerName, lyrics, RepeatString(lyrics.Length, ""), tones, colors, aliases);
        }

        [Fact]
        public void ColorTest() {
            RunPhonemizeTest("en_arpa-plus", new NoteParams[] {
                new NoteParams {
                    lyric = "hi",
                    hint = "",
                    tone = "A#3",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            alt = 0,
                            shift = 0,
                            color = "",
                        },
                        new PhonemeParams {
                            alt = 0,
                            shift = 0,
                            color = "Whisper",
                        },
                        new PhonemeParams {
                            alt = 0,
                            shift = 0,
                            color = "",
                        }
                    }
                }
            }, new string[] { "- hh_C3", "hh ay_W", "ay -_C3" });
        }
        public void SyllableTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_arpa-plus", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C3", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }
        [Theory]
        [InlineData("read", "", new string[] { "- r_C3", "r eh2_C3", "eh d_C3", "d -7_C3" })]
        [InlineData("read", "r iy d", new string[] { "- r3_C3", "r iy7_C3", "iy d_C3", "d -_C3" })]

        [InlineData("asdfjkl", "r iy d", new string[] { "- r3_C3", "r iy7_C3", "iy d_C3", "d -_C3" })]
        [InlineData("", "r iy d", new string[] { "- r3_C3", "r iy7_C3", "iy d_C3", "d -_C3" })]

        public void SyllableExternalEndingTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_arpa-plus", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C3", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }
        [Theory]
        [InlineData("more", "m ao r", new string[] { "- m_C3", "m ao1_C3", "ao r6_C3", "r -5_C3" })]
        [InlineData("'a", "q ax hh", new string[] { "- q_C3", "q ax_C3", "ax hh_C3", "hh -_C3" })]

        public void SyllableCCVTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_arpa-plus", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C3", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }
        [Theory]
        [InlineData("trusting", "", new string[] { "- tr_C3", "tr ah2_C3", "ah st_C3", "st ih_C3", "ih ng8_C3", "ng -4_C3" })]
        [InlineData("drive", "", new string[] { "- dr3_C3", "dr ay2_C3", "ay v1_C3", "v -1_C3" })]

        public void SyllableFallbackTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_arpa-plus", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C3", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }
        [Theory]
        [InlineData("kroidroi", "", new string[] { "- kr3_C3", "kr oy_C3", "iy dr_C3", "dr oy_C3", "oy -_C3" })]
        [InlineData("whhat", "",  new string[] { "- hh_C3", "f w_C3", "w ah1_C3", "ah t1_C3", "t -4_C3" })]

        public void HintTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_arpa-plus", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C3", phonemes = SamePhonemeParams(4, 0, 0, "")} }, aliases);
        }
    }
}
