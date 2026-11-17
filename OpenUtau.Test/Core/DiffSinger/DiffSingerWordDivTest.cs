using System.Collections.Generic;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerWordDivTest {
        static bool IsVowel(string phoneme)
            => phoneme is "a" or "i" or "ir";

        [Fact]
        public void TailSpIsASeparateWord() {
            // head SP, sh, ir, n, i, tail SP — the layout of [SP] [是] [你] [SP].
            var segments = new List<(string Phoneme, double DurationMs, int PhoneIndex)> {
                ("SP", 0, -1),
                ("sh", 0, 0),
                ("ir", 0, 1),
                ("n", 0, 2),
                ("i", 0, 3),
                ("SP", 0, -1),
            };
            var phDur = new[] { 8, 4, 5, 4, 4, 8 };

            var (wordDiv, wordDur) = DiffSingerUtils.WordDivAndDur(segments, phDur, IsVowel);

            // head+sh, ir+n, i, tail SP — the .ds convention keeps the tail SP
            // in its own word instead of merging it into the last vowel group.
            Assert.Equal(new long[] { 2, 2, 1, 1 }, wordDiv);
            Assert.Equal(new long[] { 12, 9, 4, 8 }, wordDur);
        }

        [Fact]
        public void NoVowelFallsBackToTheLastRealPhoneme() {
            var segments = new List<(string Phoneme, double DurationMs, int PhoneIndex)> {
                ("SP", 0, -1),
                ("k", 0, 0),
                ("SP", 0, -1),
            };
            var phDur = new[] { 8, 5, 8 };

            var (wordDiv, wordDur) = DiffSingerUtils.WordDivAndDur(segments, phDur, IsVowel);

            Assert.Equal(new long[] { 1, 1, 1 }, wordDiv);
            Assert.Equal(new long[] { 8, 5, 8 }, wordDur);
        }

        [Fact]
        public void NoteBudgetsUseCumulativeRounding() {
            // 120 BPM => 100 ms per 1/4 note; 512 / 44100 ms per frame.
            double frameMs = 1000.0 * 512 / 44100;

            var frames = DiffSingerUtils.DurationsMsToFrames(new double[] { 100, 100 }, frameMs);

            // round(cumsum / frame + 0.5) => boundaries 9 and 18, not
            // floor(100/frame)=8 and floor(200/frame)-8=9.
            Assert.Equal(new[] { 9, 9 }, frames);
        }
    }
}
