using System;
using Xunit;

namespace OpenUtau.Core.Voicevox {
    public class VoicevoxLayoutTest {
        const double frameMs = 1000.0 / VoicevoxUtils.fps;

        [Fact]
        public void ComputeLayout_EstimatedLengthCoversHeadAndTail() {
            double startMs = 2000;
            double endMs = 3000;
            var layout = VoicevoxUtils.ComputeLayout(startMs, endMs, 0);

            double slotStart = layout.positionMs - layout.leadingMs;
            double slotEnd = slotStart + layout.estimatedLengthMs;
            double snapped = VoicevoxUtils.SnapMs(startMs);
            // The slot starts one frame ahead of the head pau and ends after the tail pau.
            Assert.Equal(snapped - 95 * frameMs, slotStart, 6);
            Assert.True(slotEnd >= endMs + 94 * frameMs - frameMs);
            Assert.True(layout.estimatedLengthMs > (endMs - startMs) + 1900);
        }

        [Fact]
        public void ComputeLayout_PositionIsSnappedToFrameGrid() {
            // 2003 ms is not on the frame grid; the synthesized start frame is round(2003 / 1000 * 93.75) = 188.
            var layout = VoicevoxUtils.ComputeLayout(2003, 3000, 0);

            double expected = 188 * frameMs - 95 * frameMs;
            Assert.Equal(expected, layout.positionMs, 6);
        }

        [Fact]
        public void ComputeLayout_NearbyStartsShareTheSameFrame() {
            var a = VoicevoxUtils.ComputeLayout(2000.0, 3000, 0);
            var b = VoicevoxUtils.ComputeLayout(2000.0 + frameMs * 0.4, 3000, 0);

            Assert.Equal(a.positionMs, b.positionMs, 6);
        }

        [Fact]
        public void ComputeLayout_LeadingIsIncludedInLength() {
            var without = VoicevoxUtils.ComputeLayout(2000, 3000, 0);
            var with = VoicevoxUtils.ComputeLayout(2000, 3000, 50);

            Assert.Equal(50, with.leadingMs);
            Assert.Equal(without.estimatedLengthMs + 50, with.estimatedLengthMs, 6);
        }
    }
}
