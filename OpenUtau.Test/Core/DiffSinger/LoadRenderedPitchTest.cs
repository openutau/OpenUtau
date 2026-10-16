using System.Collections.Generic;
using Xunit;

namespace OpenUtau.Core.DiffSinger {
    public class LoadRenderedPitchTest {
        [Fact]
        public void PaddedVoicedMask_MarksOnlyRealPhonemes() {
            // head SP, "a" (2 frames), "i" (1 frame), tail SP
            var segments = new List<(string, double, int)> {
                ("SP", 0, -1), ("a", 0, 0), ("i", 0, 1), ("SP", 0, -1),
            };
            var durations = new[] { 2, 3, 2, 2 };

            var mask = DiffSingerUtils.PaddedVoicedMask(segments, durations);

            Assert.Equal(
                new[] { false, false, true, true, true, true, true, false, false },
                mask);
        }

        [Fact]
        public void PaddedVoicedMask_MarksInterPhonemeGapAsUnvoiced() {
            // Merged phrase: head SP, "a", gap SP, "i", tail SP.
            var segments = new List<(string, double, int)> {
                ("SP", 0, -1), ("a", 0, 0), ("SP", 0, -1), ("i", 0, 1), ("SP", 0, -1),
            };
            var durations = new[] { 2, 4, 3, 4, 2 };

            var mask = DiffSingerUtils.PaddedVoicedMask(segments, durations);

            // Frames 6..8 are the inter-phoneme gap and must not be voiced.
            Assert.Equal(
                new[] {
                    false, false,
                    true, true, true, true,
                    false, false, false,
                    true, true, true, true,
                    false, false,
                },
                mask);
        }

        [Fact]
        public void PaddedVoicedMask_ZeroDurationSegmentsAreDropped() {
            var segments = new List<(string, double, int)> {
                ("SP", 0, -1), ("a", 0, 0), ("SP", 0, -1),
            };
            var durations = new[] { 2, 3, 0 };

            var mask = DiffSingerUtils.PaddedVoicedMask(segments, durations);

            Assert.Equal(new[] { false, false, true, true, true }, mask);
        }

        [Fact]
        public void PaddedVoicedMask_EmptyDurationsReturnsEmptyMask() {
            var mask = DiffSingerUtils.PaddedVoicedMask(
                new List<(string, double, int)>(), new int[0]);

            Assert.Empty(mask);
        }
    }
}
