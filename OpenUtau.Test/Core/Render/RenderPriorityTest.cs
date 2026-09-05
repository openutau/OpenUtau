using Xunit;

namespace OpenUtau.Core.Render {
    public class RenderPriorityTest {
        [Fact]
        public void PlaybackBucket_PrioritizesCurrentThenFutureThenPast() {
            Assert.Equal(0, RenderPriority.PlaybackBucket(100, 200, 150));
            Assert.Equal(1, RenderPriority.PlaybackBucket(200, 300, 150));
            Assert.Equal(2, RenderPriority.PlaybackBucket(0, 100, 150));
        }

        [Fact]
        public void PlaybackDistance_PrioritizesEarlierOffsetInCurrentBucket() {
            Assert.Equal(50, RenderPriority.PlaybackDistance(100, 200, 150));
            Assert.Equal(50, RenderPriority.PlaybackDistance(200, 300, 150));
            Assert.Equal(50, RenderPriority.PlaybackDistance(0, 100, 150));
        }

        [Fact]
        public void PreRenderBucket_PrioritizesFocusedPartAtAttentionTick() {
            Assert.Equal(0, RenderPriority.PreRenderBucket(
                isPriorityPart: true, overlapsPriority: true, isAfterPriorityStart: true));
            Assert.Equal(1, RenderPriority.PreRenderBucket(
                isPriorityPart: true, overlapsPriority: false, isAfterPriorityStart: true));
            Assert.Equal(2, RenderPriority.PreRenderBucket(
                isPriorityPart: false, overlapsPriority: false, isAfterPriorityStart: true));
            Assert.Equal(3, RenderPriority.PreRenderBucket(
                isPriorityPart: false, overlapsPriority: false, isAfterPriorityStart: false));
        }

        [Fact]
        public void PreRenderDistance_PrioritizesPhraseContainingAttentionTick() {
            Assert.Equal(0, RenderPriority.PreRenderDistance(100, 200, 150));
            Assert.Equal(50, RenderPriority.PreRenderDistance(200, 300, 150));
            Assert.Equal(50, RenderPriority.PreRenderDistance(0, 100, 150));
        }
    }
}