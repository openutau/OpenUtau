using Xunit;

namespace OpenUtau.Core.Util {
    public class MusicMathTest {
        readonly ITestOutputHelper output;

        public MusicMathTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void ToneNameTest() {
            for (int i = 24; i < 108; ++i) {
                string name = MusicMath.GetToneName(i);
                int tone = MusicMath.NameToTone(name);
                output.WriteLine($"{i} -> {name} -> {tone}");
                Assert.Equal(i, tone);
            }
            Assert.Equal("C1", MusicMath.GetToneName(24));
            Assert.Equal("B7", MusicMath.GetToneName(107));
        }

        [Theory]
        [InlineData("C4", 60, 60)]
        [InlineData("C-1", 0, 0)]
        [InlineData("C1-B7", 24, 107)]
        [InlineData("C#4-F4", 61, 65)]
        [InlineData("C-1-B9", 0, 131)]
        [InlineData("C-1-B-1", 0, 11)]
        [InlineData("A#8-B9", 118, 131)]
        public void ToneRangeParses(string range, int expectedStart, int expectedEnd) {
            Assert.True(MusicMath.TryParseToneRange(range, out int start, out int end));
            Assert.Equal(expectedStart, start);
            Assert.Equal(expectedEnd, end);
        }

        [Theory]
        [InlineData("")]
        [InlineData("-")]
        [InlineData("H4")]
        [InlineData("C4-")]
        [InlineData("-C4")]
        [InlineData("C4-X")]
        public void InvalidToneRangeIsRejected(string range) {
            Assert.False(MusicMath.TryParseToneRange(range, out _, out _));
        }

        [Fact]
        public void ToneCountCoversTheWholePianoRoll() {
            Assert.Equal("C-1", MusicMath.GetToneName(0));
            Assert.Equal("B9", MusicMath.GetToneName(MusicMath.ToneCount - 1));
        }
    }
}
