using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerVarianceDeltaTest {
        [Fact]
        public void EnergyAndBreathinessClampToAcousticRange() {
            Assert.Equal(0f, DiffSingerUtils.ApplyVarianceDelta(DiffSingerUtils.ENE, 0f, 100f));
            Assert.Equal(-96f, DiffSingerUtils.ApplyVarianceDelta(DiffSingerUtils.ENE, -90f, -100f));
            Assert.Equal(0f, DiffSingerUtils.ApplyVarianceDelta(Format.Ustx.BREC, 0f, 100f));
            Assert.Equal(-96f, DiffSingerUtils.ApplyVarianceDelta(Format.Ustx.BREC, -90f, -100f));
        }

        [Fact]
        public void VoicingUsesTheHundredBaseline() {
            Assert.Equal(-50f, DiffSingerUtils.ApplyVarianceDelta(Format.Ustx.VOIC, -50f, 100f));
            Assert.Equal(-96f, DiffSingerUtils.ApplyVarianceDelta(Format.Ustx.VOIC, -90f, 0f));
        }

        [Fact]
        public void TensionClampsToPlusMinusTen() {
            Assert.Equal(10f, DiffSingerUtils.ApplyVarianceDelta(Format.Ustx.TENC, 8f, 100f));
            Assert.Equal(-10f, DiffSingerUtils.ApplyVarianceDelta(Format.Ustx.TENC, -8f, -100f));
        }
    }
}
