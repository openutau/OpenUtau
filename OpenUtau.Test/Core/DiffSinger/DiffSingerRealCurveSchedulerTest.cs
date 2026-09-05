using OpenUtau.Core.DiffSinger;
using UstxFormat = OpenUtau.Core.Format.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerRealCurveSchedulerTest {
        [Fact]
        public void RealCurveRefreshIsLimitedToVarianceOffsetCurves() {
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(DiffSingerUtils.ENE));
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(UstxFormat.BREC));
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(UstxFormat.VOIC));
            Assert.True(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(UstxFormat.TENC));

            Assert.False(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(UstxFormat.PITD));
            Assert.False(DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(UstxFormat.DYN));
        }
    }
}
