using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class USubbankToneRangeTest {
        static USubbank Make(params string[] ranges) {
            return new USubbank(new Subbank { ToneRanges = ranges });
        }

        [Fact]
        public void ToneSetCoversTheWholePianoRoll() {
            var subbank = Make("C-1-B9");

            Assert.Equal(132, subbank.toneSet.Count);
            Assert.Equal(0, subbank.toneSet.Min);
            Assert.Equal(131, subbank.toneSet.Max);
        }

        [Fact]
        public void ToneSetKeepsTheDefaultRange() {
            var subbank = Make("C1-B7");

            Assert.Equal(24, subbank.toneSet.Min);
            Assert.Equal(107, subbank.toneSet.Max);
        }

        [Fact]
        public void ToneSetAcceptsSingleTonesAndMixedRanges() {
            var subbank = Make("C-1", "C#-1-D-1", "B9");

            Assert.Equal(new[] { 0, 1, 2, 131 }, subbank.toneSet.ToArray());
        }

        [Fact]
        public void PrefixMapOutsideTheUtauRangeIsKept() {
            var dir = Path.Combine(Path.GetTempPath(), "SubbankRange_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                var path = Path.Combine(dir, "prefix.map");
                File.WriteAllText(path, "C-1\t\t_low\nC4\t\t\nB9\t\t_high\n");
                var voicebank = new Voicebank { TextFileEncoding = Encoding.UTF8 };

                VoicebankLoader.LoadMap(voicebank, path, string.Empty);

                var ranges = voicebank.Subbanks.SelectMany(subbank => subbank.ToneRanges).ToArray();
                Assert.Contains("C-1", ranges);
                Assert.Contains("C4", ranges);
                Assert.Contains("B9", ranges);
            } finally {
                Directory.Delete(dir, true);
            }
        }
    }
}
