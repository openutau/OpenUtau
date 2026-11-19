using System.Xml.Linq;
using System;
using OpenUtau.Api;
using OpenUtau.Plugin.Builtin;
using Xunit;
using System.Linq;

namespace OpenUtau.Plugins {
    public class EnglishCpVPhonemizerTest : PhonemizerTestBase {
        public EnglishCpVPhonemizerTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new EnglishCpVPhonemizer();
        }

        [Theory]
        [InlineData("en_CpV_full",
            new string[] { "test", "words" },
            new string[] { "C4", "C4" },
            new string[] { "- t", "eh", "s", "tt", "w", "er", "dd", "z", "z -" })]
        [InlineData("en_CpV_noVC",
            new string[] { "test", "words" },
            new string[] { "C4", "C4" },
            new string[] { "- t", "eh", "s", "t", "w", "er", "d", "z" })]
        [InlineData("en_CpV_5vowel",
            new string[] { "test", "words" },
            new string[] { "C4", "C4" },
            new string[] { "t", "e", "s", "t", "u", "o", "r", "d" })]
        public void BasicPhonemizingTest(string singerName, string[] lyrics, string[] tones, string[] aliases) {
            RunPhonemizeTest(
                singerName,
                lyrics,
                RepeatString(lyrics.Length, ""),
                tones,
                RepeatString(lyrics.Length, ""),
                aliases
            );
        }

        [Theory]
        [InlineData("en_CpV_full",
            new string[] { "my", "go" },
            new string[] { "C4", "C4" },
            new string[] { "- m", "ay", "ay-", "g", "ow", "ow -" })]
        [InlineData("en_CpV_noVC",
            new string[] { "my", "go" },
            new string[] { "C4", "C4" },
            new string[] { "m", "ay", "y", "g", "ow", "w" })]
        [InlineData("en_CpV_5vowel",
            new string[] { "my", "go" },
            new string[] { "C4", "C4" },
            new string[] { "m", "a", "g", "o", "u" })]
        public void DiphthongTest(string singerName, string[] lyrics, string[] tones, string[] aliases) {
            RunPhonemizeTest(
                singerName,
                lyrics,
                RepeatString(lyrics.Length, ""),
                tones,
                RepeatString(lyrics.Length, ""),
                aliases
            );
        }

        [Theory]
        [InlineData("en_CpV_full",
            new string[] { "players", "+" },
            new string[] { "C4", "C4" },
            new string[] { "- p", "l", "ey", "ey-", "er", "z", "z -" })]
        [InlineData("en_CpV_noVC",
            new string[] { "players", "+" },
            new string[] { "C4", "C4" },
            new string[] { "- p", "l", "ey", "y", "er", "z" })]
        [InlineData("en_CpV_5vowel",
            new string[] { "players", "+" },
            new string[] { "C4", "C4" },
            new string[] { "p", "l", "e", "o", "z" })]
        public void VVTest(string singerName, string[] lyrics, string[] tones, string[] aliases) {
            RunPhonemizeTest(
                singerName,
                lyrics,
                RepeatString(lyrics.Length, ""),
                tones,
                RepeatString(lyrics.Length, ""),
                aliases
            );
        }

        [Theory]
        [InlineData("en_CpV_full",
            new string[] { "streets", "marx" },
            new string[] { "C4", "C4" },
            new string[] { "- s", "tt", "r", "iy", "tt", "s", "m", "ar", "ar-", "kk", "s", "s -" })]
        [InlineData("en_CpV_noVC",
             new string[] { "streets", "marx" },
            new string[] { "C4", "C4" },
            new string[] { "- s", "t", "r", "iy", "t", "s", "m", "aa", "r", "k", "s" })]
        [InlineData("en_CpV_5vowel",
             new string[] { "streets", "marx" },
            new string[] { "C4", "C4" },
            new string[] { "s", "t", "r", "i", "t", "s", "m", "a", "r", "k", "s" })]
        public void ClusterTest(string singerName, string[] lyrics, string[] tones, string[] aliases) {
            RunPhonemizeTest(
                singerName,
                lyrics,
                RepeatString(lyrics.Length, ""),
                tones,
                RepeatString(lyrics.Length, ""),
                aliases
            );
        }

        [Theory]
        [InlineData("star", "", new string[] { "- s", "tt", "ar", "ar -" })]
        [InlineData("asdfjkl", "s t aa r", new string[] { "- s", "tt", "aa", "aa r", "r -" })]
        [InlineData("", "s t ar", new string[] { "- s", "tt", "ar", "ar -" })]
        public void HintTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_CpV_full", new NoteParams[] {
                new NoteParams {
                    lyric = lyric,
                    hint = hint,
                    tone = "C4",
                    phonemes = SamePhonemeParams(aliases.Length, 0, 0, "")
                }
            }, aliases);
        }
    }
}