using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class EsVccvTest : PhonemizerTestBase {
        public EsVccvTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new SpanishVCCVPhonemizer();
        }

        [Theory]
        [InlineData("es_vccv",
            new string[] { "lle" },
            new string[] { "A2" },
            new string[] { "-jjeA2" })]
        [InlineData("es_vccv",
            new string[] { "be" },
            new string[] { "A2" },
            new string[] { "-beA2" })]
        [InlineData("es_vccv",
            new string[] { "causa" },
            new string[] { "A2" },
            new string[] { "-kaA2", "aUA2", "UsA2", "saA2", "a-A2" })]
        public void PhonemizeTest(string singerName, string[] lyrics, string[] tones, string[] aliases) {
            RunPhonemizeTest(singerName, lyrics, RepeatString(lyrics.Length, ""), tones, RepeatString(lyrics.Length, ""), aliases);
        }

        [Theory(Skip = "openutau#2380: diphthong in 'hoy' is phonemized as 'o y' instead of the merged 'oI' alias")]
        [InlineData("es_vccv",
            new string[] { "hoy" },
            new string[] { "A2" },
            new string[] { "- oA2", "oIA2" })]
        public void PhonemizeTestKnownBug(string singerName, string[] lyrics, string[] tones, string[] aliases) {
            RunPhonemizeTest(singerName, lyrics, RepeatString(lyrics.Length, ""), tones, RepeatString(lyrics.Length, ""), aliases);
        }
    }
}
