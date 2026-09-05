using System.IO;
using System.Text;
using Xunit;

namespace OpenUtau.Classic {
    public class VoicebankLoaderTest {
        readonly ITestOutputHelper output;

        public VoicebankLoaderTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void OtoSetRoundTrip() {
            string text = @"a.wav=,,,,,
a.wav=- a,
a.wav=a R,500,,,
!@#$!@#$
aoieu.wav=- a,,,,,,,,,

aoieu.wav=a o,,
aoieu.wav=o i,,,
aoieu.wav=i e,,100,150,,,
aoieu.wav=e u,20,
aoieu.wav=u R,5,,33,44,,
".Replace("\r\n", "\n");
            string expected = @"a.wav=a,,,,,
a.wav=- a,,,,,
a.wav=a R,500,,,,
!@#$!@#$
aoieu.wav=- a,,,,,

aoieu.wav=a o,,,,,
aoieu.wav=o i,,,,,
aoieu.wav=i e,,100,150,,
aoieu.wav=e u,20,,,,
aoieu.wav=u R,5,,33,44,
".Replace("\r\n", "\n");

            using (MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(text))) {
                VoicebankLoader.IsTest = true;
                var otoSet = VoicebankLoader.ParseOtoSet(stream, "oto.ini", Encoding.ASCII);
                using (MemoryStream stream2 = new MemoryStream()) {
                    VoicebankLoader.WriteOtoSet(otoSet, stream2, Encoding.ASCII);
                    string actual = Encoding.ASCII.GetString(stream2.ToArray());
                    Assert.Equal(expected, actual);
                }
            }
        }
    }
}
