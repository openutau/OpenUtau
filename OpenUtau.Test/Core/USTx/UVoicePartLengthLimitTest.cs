using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    // At the default 480 ticks per beat in 4/4.
    public class UVoicePartLengthLimitTest {
        static (UProject project, UVoicePart part) Part(int position, params (int position, int duration)[] notes) {
            var project = OpenUtau.Core.Format.Ustx.Create();
            var part = new UVoicePart { position = position, Duration = 1920 * 4 };
            foreach (var (notePosition, duration) in notes) {
                part.notes.Add(project.CreateNote(60, notePosition, duration));
            }
            return (project, part);
        }

        [Fact]
        public void EndOnABeatNeedsNoExtraBeat() {
            var (project, part) = Part(1920, (0, 960));
            Assert.Equal(960, part.GetMinDurTick(project));
        }

        [Fact]
        public void EndInsideABeatRoundsUp() {
            var (project, part) = Part(1920, (0, 1000));
            Assert.Equal(1440, part.GetMinDurTick(project));
        }

        [Fact]
        public void LongEarlierNoteKeepsItsEnd() {
            // The note that starts last ends before the long one.
            var (project, part) = Part(0, (0, 1920), (480, 240));
            Assert.Equal(1920, part.GetMinDurTick(project));
        }

        [Fact]
        public void EmptyPartIsOneBeat() {
            var (project, part) = Part(0);
            Assert.Equal(480, part.GetMinDurTick(project));
        }

        [Fact]
        public void StartLeavesABeatBeforeTheFirstNote() {
            var (project, part) = Part(1920, (960, 480));
            Assert.Equal(1920 + 480, part.GetMaxPosiTick(project));
        }
    }
}
