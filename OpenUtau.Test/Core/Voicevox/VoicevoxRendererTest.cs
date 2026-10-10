using Xunit;

namespace OpenUtau.Core.Voicevox {
    public class VoicevoxRendererTest {
        [Fact]
        public void PaddingVoicedMask_MarksOnlyPaddingAsUnvoiced() {
            // head pau (2 frames), "k" (1), "a" (3), tail pau (2)
            var mask = VoicevoxRenderer.PaddingVoicedMask(new[] { 2, 1, 3, 2 });

            Assert.Equal(
                new[] { false, false, true, true, true, true, false, false },
                mask);
        }

        [Fact]
        public void PaddingVoicedMask_PhraseWithoutPhonemesIsAllUnvoiced() {
            var mask = VoicevoxRenderer.PaddingVoicedMask(new[] { 2, 2 });

            Assert.Equal(new[] { false, false, false, false }, mask);
        }

        [Fact]
        public void PaddingVoicedMask_EmptyListReturnsEmptyMask() {
            var mask = VoicevoxRenderer.PaddingVoicedMask(new int[0]);

            Assert.Empty(mask);
        }

        [Fact]
        public void PaddingVoicedMask_LengthMatchesTotalFrames() {
            var lengths = new[] { 86, 4, 12, 7, 86 };

            var mask = VoicevoxRenderer.PaddingVoicedMask(lengths);

            Assert.Equal(4 + 12 + 7 + 86 + 86, mask.Length);
            Assert.Equal(23, System.Linq.Enumerable.Count(mask, voiced => voiced));
        }
    }
}
