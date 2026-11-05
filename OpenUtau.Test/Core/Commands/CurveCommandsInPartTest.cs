using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;
using Ustx = OpenUtau.Core.Format.Ustx;

namespace OpenUtau.Test.Core.Commands {
    // The piano roll shows the track around the open part, so curve edits can be drawn outside it.
    public class CurveCommandsInPartTest {
        static (UProject project, UVoicePart part) Part() {
            var project = Ustx.Create();
            var part = new UVoicePart { trackNo = 0, position = 1920, Duration = 1920 };
            project.parts.Add(part);
            return (project, part);
        }

        [Fact]
        public void CurveLineOutsideThePartIsDropped() {
            var (project, part) = Part();
            new SetCurveCommand(project, part, Ustx.DYN, -480, 10, -960, 10, withinPart: true).Execute();
            new SetCurveCommand(project, part, Ustx.DYN, 2400, 10, 2880, 10, withinPart: true).Execute();
            var curve = part.curves.FirstOrDefault(c => c.abbr == Ustx.DYN);
            Assert.True(curve == null || curve.xs.Count == 0);
        }

        // Within the part, but for the point UCurve.Set keeps one step beside a value to hold its neighbours.
        static void AssertInPart(UCurve curve, UVoicePart part) =>
            Assert.All(curve.xs, x => Assert.InRange(x, -UCurve.interval, part.Duration + UCurve.interval));

        [Fact]
        public void CurveLineIsCutAtThePartEdges() {
            var (project, part) = Part();
            // From 20 at -480 to -20 at 480: it crosses the part start at 0 with value 0.
            new SetCurveCommand(project, part, Ustx.DYN, 480, -20, -480, 20, withinPart: true).Execute();
            var curve = part.curves.Single(c => c.abbr == Ustx.DYN);
            AssertInPart(curve, part);
            Assert.Equal(0, curve.Sample(0));
            Assert.Equal(-20, curve.Sample(480));

            new SetCurveCommand(project, part, Ustx.DYN, part.Duration + 480, 10, part.Duration - 480, 10, withinPart: true).Execute();
            AssertInPart(curve, part);
            Assert.Equal(10, curve.Sample(part.Duration));
        }

        [Fact]
        public void OtherWritersKeepWritingAnywhere() {
            var (project, part) = Part();
            new SetCurveCommand(project, part, Ustx.DYN, -480, 10, -960, 10).Execute();
            Assert.Contains(part.curves.Single(c => c.abbr == Ustx.DYN).xs, x => x < 0);
            new SetMaskedCurveCommand(part, Ustx.PITO, -960, 6000, -480, 6000).Execute();
            Assert.True(part.maskedCurves.Single(c => c.abbr == Ustx.PITO).TrySample(-720, out _));
        }

        [Fact]
        public void MaskedCurveEditsStayInThePart() {
            var (project, part) = Part();
            new SetMaskedCurveCommand(part, Ustx.PITO, -960, 6000, -480, 6000, withinPart: true).Execute();
            Assert.DoesNotContain(part.maskedCurves, c => c.abbr == Ustx.PITO);

            new SetMaskedCurveCommand(part, Ustx.PITO, -480, 6000, 480, 6000, withinPart: true).Execute();
            var curve = part.maskedCurves.Single(c => c.abbr == Ustx.PITO);
            Assert.False(curve.TrySample(-240, out _));
            Assert.True(curve.TrySample(240, out float value));
            Assert.Equal(6000, value);

            new SetMaskedCurveValuesCommand(part, Ustx.PITO, new[] { (-120, 5000f), (part.Duration + 120, 5000f) }, withinPart: true).Execute();
            Assert.False(curve.TrySample(-120, out _));
            Assert.False(curve.TrySample(part.Duration + 120, out _));
        }
    }
}
