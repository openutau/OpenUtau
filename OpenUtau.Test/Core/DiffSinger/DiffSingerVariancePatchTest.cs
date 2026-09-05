using System.Linq;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerVariancePatchTest {
        [Fact]
        public void FindChangedRangesGroupsContiguousPitchChanges() {
            var previous = new[] { 1f, 1f, 1f, 1f, 1f, 1f };
            var current = new[] { 1f, 2f, 2f, 1f, 2f, 1f };

            var ranges = DiffSingerVariancePatch.FindChangedRanges(previous, current, 1e-4f);

            Assert.Equal(2, ranges.Count);
            Assert.Equal(1, ranges[0].start);
            Assert.Equal(3, ranges[0].end);
            Assert.Equal(4, ranges[1].start);
            Assert.Equal(5, ranges[1].end);
        }

        [Fact]
        public void MergeKeepsPreviousResultWhenPitchDoesNotChange() {
            var previousResult = Result(new[] { 1f, 2f, 3f });
            var currentResult = Result(new[] { 10f, 20f, 30f });
            var previous = new VariancePatchState(new[] { 60f, 61f, 62f }, previousResult);

            var merged = DiffSingerVariancePatch.Merge(previous, new[] { 60f, 61f, 62f }, currentResult);

            Assert.Equal(previousResult.energy!, merged.energy!);
        }

        [Fact]
        public void MergeBlendsOnlyChangedPitchRange() {
            var previousResult = Result(Enumerable.Repeat(0f, 6).ToArray(), frameMs: 50);
            var currentResult = Result(Enumerable.Repeat(10f, 6).ToArray(), frameMs: 50);
            var previous = new VariancePatchState(
                new[] { 60f, 60f, 60f, 60f, 60f, 60f },
                previousResult);

            var merged = DiffSingerVariancePatch.Merge(
                previous,
                new[] { 60f, 60f, 61f, 61f, 60f, 60f },
                currentResult);

            Assert.Equal(new[] { 0f, 5f, 10f, 10f, 5f, 0f }, merged.energy!);
        }

        [Fact]
        public void MergeFallsBackToCurrentResultWhenMetadataChanges() {
            var previousResult = Result(new[] { 1f, 2f, 3f }, frameMs: 50);
            var currentResult = Result(new[] { 10f, 20f, 30f }, frameMs: 60);
            var previous = new VariancePatchState(new[] { 60f, 61f, 62f }, previousResult);

            var merged = DiffSingerVariancePatch.Merge(previous, new[] { 60f, 62f, 62f }, currentResult);

            Assert.Equal(currentResult.energy!, merged.energy!);
        }

        static VarianceResult Result(float[] energy, float frameMs = 50) {
            return new VarianceResult {
                energy = energy,
                frameMs = frameMs,
                headFrames = 1,
                tailFrames = 1,
                totalFrames = energy.Length,
            };
        }
    }
}
