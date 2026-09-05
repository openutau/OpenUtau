using Xunit;

namespace OpenUtau.Core.G2p {
    public class G2pTest {
        readonly ITestOutputHelper output;

        public G2pTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void ArpabetG2pTest() {
            var g2p = new ArpabetG2p();
            Assert.Null(g2p.Query(""));
            Assert.Null(g2p.Query(","));
            Assert.Null(g2p.Query("-"));
            Assert.Equal("v ow k ah l", string.Join(' ', g2p.Query("vocal")));
            Assert.Equal("v ow k ah l", string.Join(' ', g2p.Query("%v,oca l")));
            Assert.Equal("v uw ow", string.Join(' ', g2p.Query("voooo")));
            Assert.Equal("m iy", string.Join(' ', g2p.Query("meeee")));
        }

        [Fact]
        public void RussianG2pTest() {
            var g2p = new RussianG2p();
            Assert.Null(g2p.Query(""));
            Assert.Null(g2p.Query(","));
            Assert.Null(g2p.Query("-"));
            Assert.Equal("nn i vv je zh d ay", string.Join(' ', g2p.Query("невежда")));
            Assert.Equal("nn i vv je zh d ay", string.Join(' ', g2p.Query("?нев,.еж да ")));
            Assert.Equal("nn i nn i nn i nn i", string.Join(' ', g2p.Query("нененене")));
            Assert.Equal("d ay d ay d a d aa", string.Join(' ', g2p.Query("дададада")));
        }

        [Fact]
        public void PortugueseG2pTest() {
            var g2p = new PortugueseG2p();
            Assert.Null(g2p.Query(""));
            Assert.Null(g2p.Query(","));
            Assert.Null(g2p.Query("-"));
            Assert.Equal("i~ v e R n e s", string.Join(' ', g2p.Query("inverness")));
            Assert.Equal("i~ v e R n e s", string.Join(' ', g2p.Query("inve  rn,.ess")));
            Assert.Equal("p i r i m i tS i", string.Join(' ', g2p.Query("pírímítí")));
        }

        [Fact]
        public void ArpabetPlusG2pTest() {
            var g2p = new ArpabetPlusG2p();
            Assert.Null(g2p.Query(""));
            Assert.Null(g2p.Query(","));
            Assert.Null(g2p.Query("-"));
            Assert.Equal("f l aa p tr aa p ih k ax", string.Join(' ', g2p.Query("floptropica")));
            Assert.Equal("f l aa p tr aa p ih k ax", string.Join(' ', g2p.Query("%fl,optro pica")));
            Assert.Equal("s l ey d", string.Join(' ', g2p.Query("slayed")));
            Assert.Equal("d er eh k sh ax n", string.Join(' ', g2p.Query("direction")));
        }
    }
}
