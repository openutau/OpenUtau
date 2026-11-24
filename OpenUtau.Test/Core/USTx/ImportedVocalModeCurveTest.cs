using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class ImportedVocalModeCurveTest {
        static UCurve Curve(string name, string abbr) =>
            new UCurve(new UExpressionDescriptor(name, abbr, 0, 200, 100) { type = UExpressionType.Curve });

        static UTrack DiffSingerTrack() {
            var track = new UTrack();
            track.RendererSettings.Renderer = new DiffSingerRenderer();
            return track;
        }

        /// <summary>
        /// Regression: a DiffSinger project with a velocity curve (velc) asked to remap "velocity (curve)" onto a
        /// voice color every time it was opened, although velc is a curve DiffSinger itself renders.
        /// </summary>
        [Fact]
        public void CurveTheRendererSupportsIsNotAnImportedMode() {
            Assert.False(DiffSingerTrack().IsImportedVocalModeCurve(Curve("velocity (curve)", "velc")));
        }

        [Theory]
        [InlineData("energy (curve)", "ene")]
        [InlineData("pitch expressiveness (curve)", "pexp")]
        public void OtherDiffSingerCurvesAreNotImportedModes(string name, string abbr) {
            Assert.False(DiffSingerTrack().IsImportedVocalModeCurve(Curve(name, abbr)));
        }

        [Theory]
        [InlineData("Power", "pwr")]
        [InlineData("Clear", "clear")]   // SVP import registers modes as lowercase names; "clear" is not a cl01-style curve
        public void CurveTheRendererDoesNotKnowIsAnImportedMode(string name, string abbr) {
            Assert.True(DiffSingerTrack().IsImportedVocalModeCurve(Curve(name, abbr)));
        }

        [Theory]
        [InlineData(Ustx.XSY)]
        [InlineData(Ustx.GRWC)]
        public void ClassicDefaultCurvesAreNotImportedModes(string abbr) {
            // Default project curves for other renderers, left on a track that switched to a DiffSinger singer.
            Assert.False(DiffSingerTrack().IsImportedVocalModeCurve(Curve(abbr, abbr)));
        }

        [Theory]
        [InlineData(Ustx.DYN)]
        [InlineData(Ustx.TENC)]
        [InlineData("cl01")]
        [InlineData("opec")]
        public void OwnExpressionsAreNeverImportedModes(string abbr) {
            Assert.False(DiffSingerTrack().IsImportedVocalModeCurve(Curve(abbr, abbr)));
        }

        [Fact]
        public void WithoutARendererTheFixedListStillApplies() {
            var track = new UTrack();
            Assert.False(track.IsImportedVocalModeCurve(Curve("dynamics (curve)", Ustx.DYN)));
            Assert.True(track.IsImportedVocalModeCurve(Curve("Power", "pwr")));
            Assert.False(track.IsImportedVocalModeCurve(new UCurve()));
            Assert.False(track.IsImportedVocalModeCurve(null!));
        }
    }
}
