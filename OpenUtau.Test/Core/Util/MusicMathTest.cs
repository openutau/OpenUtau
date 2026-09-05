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
    }
}
