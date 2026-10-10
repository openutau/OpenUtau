using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OpenUtau.Core.Render {
    public class PitchRetakeTest {
        [Fact]
        public void MapSelectedPositionsToNoteIndexes_PicksMatchingNotes() {
            var noteRel = new[] { 0, 480, 960, 1440 };
            var selected = new HashSet<int> { 100 + 480, 100 + 1440 };

            var result = PitchRetake.MapSelectedPositionsToNoteIndexes(100, noteRel, selected);

            Assert.Equal(new HashSet<int> { 1, 3 }, result);
        }

        [Fact]
        public void MapSelectedPositionsToNoteIndexes_ReturnsEmptyWhenNoneSelected() {
            var noteRel = new[] { 0, 480 };
            var result = PitchRetake.MapSelectedPositionsToNoteIndexes(0, noteRel, new HashSet<int>());
            Assert.Empty(result);
        }

        [Fact]
        public void MapSelectedPositionsToNoteIndexes_HandlesNullSelected() {
            var noteRel = new[] { 0, 480 };
            var result = PitchRetake.MapSelectedPositionsToNoteIndexes(0, noteRel, null);
            Assert.Empty(result);
        }

        [Fact]
        public void BuildWriteBackMask_MarksOnlySelectedNote() {
            // Notes at 0, 480, 960 (phrase at 100); select the middle one.
            var noteRel = new[] { 0, 480, 960 };
            var ticks = new float[] { 0, 240, 480, 720, 960, 1200 };

            var mask = PitchRetake.BuildWriteBackMask(100, noteRel, new HashSet<int> { 100 + 480 }, ticks);

            Assert.Equal(new[] { false, false, true, true, false, false }, mask);
        }

        [Fact]
        public void BuildWriteBackMask_LeadingFramesBelongToFirstNote() {
            var noteRel = new[] { 0, 480 };
            var ticks = new float[] { -120, -60, 0, 240, 480 };

            var mask = PitchRetake.BuildWriteBackMask(0, noteRel, new HashSet<int> { 0 }, ticks);

            Assert.Equal(new[] { true, true, true, true, false }, mask);
        }

        [Fact]
        public void BuildWriteBackMask_GapAndTailBelongToPrecedingNote() {
            // Note 0 spans 0-240, gap 240-480, note 1 from 480; the last frames are tail padding.
            var noteRel = new[] { 0, 480 };
            var ticks = new float[] { 0, 300, 400, 480, 600, 900 };

            var first = PitchRetake.BuildWriteBackMask(0, noteRel, new HashSet<int> { 0 }, ticks);
            var second = PitchRetake.BuildWriteBackMask(0, noteRel, new HashSet<int> { 480 }, ticks);

            Assert.Equal(new[] { true, true, true, false, false, false }, first);
            Assert.Equal(new[] { false, false, false, true, true, true }, second);
        }

        static double[] FramesMs(int count) => Enumerable.Range(0, count).Select(i => i * 10.0).ToArray();

        static void AssertWeights(double[] expected, float[] actual) {
            Assert.Equal(expected, actual.Select(w => System.Math.Round(w, 3)).ToArray());
        }

        [Fact]
        public void BuildCrossfadeWeights_FadesInFromKeptFrames() {
            var mask = new[] { false, false, true, true, true, true, true, true, true, true };

            var weights = PitchRetake.BuildCrossfadeWeights(mask, FramesMs(10), null, 40);

            AssertWeights(new[] { 0, 0, 0, 0.25, 0.5, 0.75, 1, 1, 1, 1 }, weights);
        }

        [Fact]
        public void BuildCrossfadeWeights_ShortRangeSplitsFadeBetweenBothSides() {
            var mask = new[] { false, true, true, true, true, true, false };

            var weights = PitchRetake.BuildCrossfadeWeights(mask, FramesMs(7), null, 50);

            AssertWeights(new[] { 0, 0, 0.5, 1, 0.5, 0, 0 }, weights);
        }

        [Fact]
        public void BuildCrossfadeWeights_NoFadeAtPhraseStart() {
            var mask = new[] { true, true, true, false };

            var weights = PitchRetake.BuildCrossfadeWeights(mask, FramesMs(4), null, 20);

            AssertWeights(new[] { 1, 0.5, 0, 0 }, weights);
        }

        [Fact]
        public void BuildCrossfadeWeights_FadeStartsAtFirstVoicedFrame() {
            var mask = new[] { false, true, true, true, true, true };
            var voiced = new[] { true, false, false, true, true, true };

            var weights = PitchRetake.BuildCrossfadeWeights(mask, FramesMs(6), voiced, 20);

            AssertWeights(new[] { 0, 0, 0, 0, 0.5, 1 }, weights);
        }

        [Fact]
        public void BuildWriteBackMask_NoneOrAllSelectedReturnsNull() {
            var noteRel = new[] { 0, 480 };
            var ticks = new float[] { 0, 480 };

            Assert.Null(PitchRetake.BuildWriteBackMask(0, noteRel, new HashSet<int>(), ticks));
            Assert.Null(PitchRetake.BuildWriteBackMask(0, noteRel, new HashSet<int> { 0, 480 }, ticks));
        }

        [Fact]
        public void GetRetakeFrameRanges_NullMaskReturnsFullRange() {
            var ranges = PitchRetake.GetRetakeFrameRanges(null, 5).ToArray();

            Assert.Equal(new[] { (0, 5) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_SeparatesNonAdjacentRetakeRegions() {
            var mask = new[] { false, true, true, false, false, true, true, false };

            var ranges = PitchRetake.GetRetakeFrameRanges(mask, mask.Length).ToArray();

            Assert.Equal(new[] { (1, 3), (5, 7) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_TreatsFramesPastMaskAsUnselected() {
            var mask = new[] { false, true, true };

            var ranges = PitchRetake.GetRetakeFrameRanges(mask, 6).ToArray();

            Assert.Equal(new[] { (1, 3) }, ranges);
        }

        [Fact]
        public void GetRetakeFrameRanges_EmptySelectionReturnsNoRanges() {
            var ranges = PitchRetake.GetRetakeFrameRanges(
                new[] { false, false, false }, 3).ToArray();

            Assert.Empty(ranges);
        }
    }
}
