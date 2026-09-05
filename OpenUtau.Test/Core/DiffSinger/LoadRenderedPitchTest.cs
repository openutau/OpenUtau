using System.Linq;
using Xunit;

namespace OpenUtau.Core.DiffSinger {
    public class LoadRenderedPitchTest {
        [Fact]
        public void GetRetakeFrameRanges_NullMaskReturnsFullRange() {
            var ranges = DiffSingerRetake.GetRetakeFrameRanges(null, 5).ToArray();

            Assert.Equal(new[] { (0, 5) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_SeparatesNonAdjacentRetakeRegions() {
            var mask = new[] { false, true, true, false, false, true, true, false };

            var ranges = DiffSingerRetake.GetRetakeFrameRanges(mask, mask.Length).ToArray();

            Assert.Equal(new[] { (1, 3), (5, 7) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_TreatsFramesPastMaskAsUnselected() {
            var mask = new[] { false, true, true };

            var ranges = DiffSingerRetake.GetRetakeFrameRanges(mask, 6).ToArray();

            Assert.Equal(new[] { (1, 3) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_EmptySelectionReturnsNoRanges() {
            var ranges = DiffSingerRetake.GetRetakeFrameRanges(
                new[] { false, false, false }, 3).ToArray();

            Assert.Empty(ranges);
        }
    }
}
