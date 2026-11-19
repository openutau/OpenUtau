using System.IO;
using System.Reflection;
using System.Text;
using Classic;
using Xunit;

namespace OpenUtau.Classic {
    public class PresampIniTest {

        private Presamp LoadPresampIni(string dirName, Encoding? encoding = null) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Files", "presampini", dirName);
            var presamp = new Presamp();
            presamp.ReadPresampIni(dir, encoding ?? Encoding.GetEncoding("shift_jis"));
            return presamp;
        }

        [Fact]
        public void DefaultPresampIniTest() {
            var presamp = LoadPresampIni("default");

            Assert.True(presamp.FileExists);
            Assert.Equal("あ", presamp.Vowels["a"].VowelUpper);
            Assert.True(presamp.Consonants["k"].NotClossfade);
            Assert.Equal("sh", presamp.PhonemeList["しゃ"].Consonant);
            Assert.Equal("あ", presamp.Replace["a"]);
            Assert.Contains("k", presamp.Priorities);
            Assert.Equal(" ", presamp.AliasRules.VCPAD);
        }

        [Fact]
        public void BlankPresampIniTest() {
            var presamp = LoadPresampIni("blank");

            Assert.True(presamp.FileExists);
            Assert.True(presamp.Vowels.Count == 0);
            Assert.True(presamp.Consonants.Count == 0);
            Assert.True(presamp.Priorities.Count == 0);
            Assert.True(presamp.Replace.Count == 0);
            Assert.Equal("%num%", presamp.SuffixOrder[0]);
            Assert.True(presamp.Nums.Count == 0);
            Assert.True(presamp.Appends.Count == 0);
            Assert.True(presamp.Pitches.Count == 0);
            Assert.True(presamp.PhonemeList.Count == 0);
            Assert.True(presamp.CFlags == string.Empty);
            Assert.Equal(" ", presamp.AliasRules.VCPAD);
        }

        [Fact]
        public void EmptyPresampIniTest() {
            var presamp = LoadPresampIni("empty");

            Assert.True(presamp.FileExists);
            Assert.True(presamp.Vowels.Count == 7);
            Assert.Equal("あ", presamp.Vowels["a"].VowelUpper);
            Assert.True(presamp.Consonants.Count == 30);
            Assert.True(presamp.Consonants["k"].NotClossfade);
            Assert.Equal("sh", presamp.PhonemeList["しゃ"].Consonant);
            Assert.Equal("あ", presamp.Replace["a"]);
            Assert.Contains("k", presamp.Priorities);
            Assert.Equal(" ", presamp.AliasRules.VCPAD);
        }

        [Fact]
        public void CustomizedPresampIniTest() {
            var presamp = LoadPresampIni("tricky_symbols");

            Assert.True(presamp.FileExists);
            Assert.Contains("'", presamp.Vowels["a"].Phonemes);
            Assert.Contains("\"", presamp.Vowels["a"].Phonemes);
            Assert.Contains("ちぇ", presamp.Consonants["ch"].Phonemes);
            Assert.DoesNotContain("g", presamp.Priorities);
            Assert.Equal("a ン", presamp.Replace["a N"]);
            Assert.Equal("あ・", presamp.Replace["あ'"]);
            Assert.Equal(" あ", presamp.Replace[" a"]);
            Assert.Equal("%append%", presamp.SuffixOrder[0]);
            Assert.Equal("%pitch%", presamp.SuffixOrder[1]);
            Assert.Equal("%num%", presamp.SuffixOrder[2]);
            Assert.Contains("L", presamp.Nums);
            Assert.DoesNotContain("@NOREPEAT@", presamp.Appends);
            Assert.DoesNotContain("@UNDERBAR@", presamp.Pitches);
            Assert.False(presamp.Split);
            Assert.False(presamp.MustVC); // unexpected value
            Assert.Equal(0, presamp.AddEnding);
        }

        [Fact]
        public void XSampaPresampIniTest() {
            var presamp = LoadPresampIni("xsampa", Encoding.UTF8);

            Assert.True(presamp.FileExists);
            Assert.Contains("ちぇ", presamp.Consonants["tS"].Phonemes);
            Assert.DoesNotContain("ch", presamp.Consonants.Keys);
            Assert.Equal("S", presamp.PhonemeList["しゃ"].Consonant);
            Assert.Equal("4'", presamp.PhonemeList["り"].Consonant);
            Assert.Equal("p\\", presamp.PhonemeList["ふぁ"].Consonant);
            Assert.Contains("p\\", presamp.Priorities);
            Assert.Equal("でょ", presamp.Replace["dho"]);
        }

        [Fact]
        public void NotExistPresampIniTest() {
            var presamp = LoadPresampIni("dummy");

            Assert.False(presamp.FileExists);
            Assert.Equal("あ", presamp.Vowels["a"].VowelUpper);
            Assert.True(presamp.Consonants["k"].NotClossfade);
            Assert.Equal("sh", presamp.PhonemeList["しゃ"].Consonant);
            Assert.Equal("あ", presamp.Replace["a"]);
            Assert.Contains("k", presamp.Priorities);
            Assert.Equal(" ", presamp.AliasRules.VCPAD);
        }
    }
}
