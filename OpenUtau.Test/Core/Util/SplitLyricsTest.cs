using Xunit;

namespace OpenUtau.Core.Util {
    public class SplitLyricsTest {
        readonly ITestOutputHelper output;
        public SplitLyricsTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void SplitTest() {
            var result = SplitLyrics.Split("a word中文русский가각갂ひらがな \"がな\" \"\" \"two words\"");
            Assert.Collection(result,
                s => Assert.Equal("a", s),
                s => Assert.Equal("word", s),
                s => Assert.Equal("中", s),
                s => Assert.Equal("文", s),
                s => Assert.Equal("русский", s),
                s => Assert.Equal("가", s),
                s => Assert.Equal("각", s),
                s => Assert.Equal("갂", s),
                s => Assert.Equal("ひ", s),
                s => Assert.Equal("ら", s),
                s => Assert.Equal("が", s),
                s => Assert.Equal("な", s),
                s => Assert.Equal("がな", s),
                s => Assert.Equal("", s),
                s => Assert.Equal("two words", s));
        }

        [Fact]
        public void JoinTest() {
            string[] lyrics = new[] { "a", "word", "中", "文", "русский", "가", "각", "갂", "ひ", "ら", "が", "な", "がな", "", "two words" };
            Assert.Equal(
                "a word 中 文 русский 가 각 갂 ひ ら が な \"がな\" \"\" \"two words\"",
                SplitLyrics.Join(lyrics));
        }

        [Fact]
        public void RoundTripTest() {
            string[] lyrics = new[] { "a", "word", "中", "文", "中文", " ", "    ", "-", "12 3# $%^", "中русский", "русский", "ひ", "ら", "が", "な", "がな", "two words", "가", "각", "갂", "갃간" };
            Assert.Equal(lyrics, SplitLyrics.Split(SplitLyrics.Join(lyrics)));
        }
    }
}
