using System.Linq;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class DiffSingerTakeRerollTest {
        [Fact]
        public void EachPhraseConsumesARequestOnlyOnce() {
            DiffSingerTakeReroll.Clear();
            DiffSingerTakeReroll.Request(0, new[] { (0, 100) });

            int nonce = DiffSingerTakeReroll.Consume(0, 0, 100, out var ranges);
            Assert.True(nonce > 0);
            Assert.Equal(new[] { (0, 100) }, ranges);

            int again = DiffSingerTakeReroll.Consume(0, 0, 100, out var none);
            Assert.Equal(0, again);
            Assert.Null(none);
        }

        [Fact]
        public void RemainingRangesStillApplyToOtherPhrases() {
            DiffSingerTakeReroll.Clear();
            DiffSingerTakeReroll.Request(0, new[] { (0, 200) });

            int left = DiffSingerTakeReroll.Consume(0, 0, 100, out var leftRanges);
            Assert.True(left > 0);
            Assert.Equal(new[] { (0, 100) }, leftRanges);

            int right = DiffSingerTakeReroll.Consume(0, 100, 200, out var rightRanges);
            Assert.Equal(left, right);
            Assert.Equal(new[] { (100, 200) }, rightRanges);

            Assert.Equal(0, DiffSingerTakeReroll.Consume(0, 0, 200, out _));
        }

        [Fact]
        public void RequestOnlyMatchesItsTrack() {
            DiffSingerTakeReroll.Clear();
            DiffSingerTakeReroll.Request(3, new[] { (0, 100) });

            Assert.Equal(0, DiffSingerTakeReroll.Consume(0, 0, 100, out _));
            Assert.True(DiffSingerTakeReroll.Consume(3, 0, 100, out _) > 0);
        }
    }
}
