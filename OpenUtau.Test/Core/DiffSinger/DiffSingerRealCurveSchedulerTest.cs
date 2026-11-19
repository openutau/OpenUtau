using OpenUtau.Core.DiffSinger;
using UstxFormat = OpenUtau.Core.Format.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerRealCurveSchedulerTest {
        [Fact]
        public void RealCurveRefreshIsLimitedToVarianceOffsetCurves() {
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(null, DiffSingerUtils.ENE));
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(null, UstxFormat.BREC));
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(null, UstxFormat.VOIC));
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(null, UstxFormat.TENC));

            Assert.False(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(null, UstxFormat.PITD));
            Assert.False(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(null, UstxFormat.DYN));
        }
    }
}
