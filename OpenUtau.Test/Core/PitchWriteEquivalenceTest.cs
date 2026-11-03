using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// LoadRenderedPitch commits the pitch curve as one MergedSetCurveCommand; it must land on
    /// exactly the same curve as the per-point SetCurveCommand writes it replaced.
    /// </summary>
    public class PitchWriteEquivalenceTest {
        static List<(int x, int y, int lastX, int lastY)> PitchLikePoints() {
            var points = new List<(int, int, int, int)>();
            var random = new System.Random(1234);
            // Three phrases, written from the last to the first, as the batch edit does.
            for (int phrase = 2; phrase >= 0; phrase--) {
                int start = phrase * 2000;
                int? lastX = null;
                int? lastY = null;
                for (int x = start; x < start + 1000; x += 5) {
                    int y = random.Next(-400, 400);
                    lastX ??= x;
                    lastY ??= y;
                    points.Add((x, y, lastX.Value, lastY.Value));
                    lastX = x;
                    lastY = y;
                }
            }
            return points;
        }

        [Fact]
        public void MergedWriteMatchesOneCommandPerPoint() {
            var project = new UProject();
            var descriptor = new UExpressionDescriptor("pitch deviation", Format.Ustx.PITD, -1200, 1200, 0);
            project.RegisterExpression(descriptor);
            var perPoint = new UVoicePart();
            var merged = new UVoicePart();
            project.parts.Add(perPoint);
            project.parts.Add(merged);

            var points = PitchLikePoints();
            foreach (var (x, y, lastX, lastY) in points) {
                new SetCurveCommand(project, perPoint, Format.Ustx.PITD, x, y, lastX, lastY).Execute();
            }

            var edited = new UCurve(descriptor);
            foreach (var (x, y, lastX, lastY) in points) {
                edited.Set(x, y, lastX, lastY);
            }
            new MergedSetCurveCommand(project, merged, Format.Ustx.PITD,
                null, null, edited.xs.ToArray(), edited.ys.ToArray()).Execute();

            var expected = perPoint.curves.Single(c => c.abbr == Format.Ustx.PITD);
            var actual = merged.curves.Single(c => c.abbr == Format.Ustx.PITD);
            Assert.Equal(expected.xs, actual.xs);
            Assert.Equal(expected.ys, actual.ys);
        }

        [Fact]
        public void MergedWriteKeepsPointsOutsideTheEditedRanges() {
            var project = new UProject();
            var descriptor = new UExpressionDescriptor("pitch deviation", Format.Ustx.PITD, -1200, 1200, 0);
            project.RegisterExpression(descriptor);
            var part = new UVoicePart();
            project.parts.Add(part);
            var curve = new UCurve(descriptor);
            curve.Set(100, 50, 100, 50);
            curve.Set(5000, -50, 5000, -50);
            part.curves.Add(curve);

            var edited = curve.Clone();
            edited.Set(3000, 200, 3000, 200);
            new MergedSetCurveCommand(project, part, Format.Ustx.PITD,
                curve.xs.ToArray(), curve.ys.ToArray(), edited.xs.ToArray(), edited.ys.ToArray()).Execute();

            var result = part.curves.Single(c => c.abbr == Format.Ustx.PITD);
            Assert.Equal(50, result.Sample(100));
            Assert.Equal(200, result.Sample(3000));
            Assert.Equal(-50, result.Sample(5000));
        }
    }
}
