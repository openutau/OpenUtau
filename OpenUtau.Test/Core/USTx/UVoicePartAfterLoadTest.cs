using System.Linq;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class UVoicePartAfterLoadTest {
        static (UProject project, UTrack track) CreateProject() {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("dynamics (curve)", "dyn", -60, 24, 0) {
                type = UExpressionType.Curve,
            });
            return (project, project.tracks[0]);
        }

        // Curves loaded from ustx only have abbr, xs and ys; descriptor is not serialized.
        static UCurve LoadedCurve(string abbr) {
            return new UCurve(abbr) {
                xs = { 0, 5 },
                ys = { 10, 20 },
            };
        }

        [Fact]
        public void ResolvesProjectExpressionCurve() {
            var (project, track) = CreateProject();
            var part = new UVoicePart { curves = { LoadedCurve("dyn") } };

            part.AfterLoad(project, track);

            var curve = Assert.Single(part.curves);
            Assert.Same(project.expressions["dyn"], curve.descriptor);
        }

        [Fact]
        public void ResolvesTrackExpressionCurve() {
            var (project, track) = CreateProject();
            var trackExp = new UExpressionDescriptor("track curve", "trkc", 0, 100, 0) {
                type = UExpressionType.Curve,
            };
            track.TrackExpressions.Add(trackExp);
            var part = new UVoicePart { curves = { LoadedCurve("trkc") } };

            part.AfterLoad(project, track);

            var curve = Assert.Single(part.curves);
            Assert.Same(trackExp, curve.descriptor);
        }

        [Fact]
        public void RemovesCurveWithUnknownExpression() {
            var (project, track) = CreateProject();
            var part = new UVoicePart { curves = { LoadedCurve("dyn"), LoadedCurve("gone") } };

            part.AfterLoad(project, track);

            Assert.Equal(new[] { "dyn" }, part.curves.Select(c => c.abbr));
        }

        [Fact]
        public void KeepsExistingDescriptorWhenExpressionRemoved() {
            // ConfigureExpressionsCommand calls AfterLoad; curves must survive so undo can restore them.
            var (project, track) = CreateProject();
            var part = new UVoicePart { curves = { LoadedCurve("dyn") } };
            part.AfterLoad(project, track);

            project.expressions.Remove("dyn");
            part.AfterLoad(project, track);

            Assert.Single(part.curves);
        }

        [Fact]
        public void CloneAfterLoadDoesNotThrow() {
            var (project, track) = CreateProject();
            track.TrackExpressions.Add(new UExpressionDescriptor("track curve", "trkc", 0, 100, 0) {
                type = UExpressionType.Curve,
            });
            var part = new UVoicePart { curves = { LoadedCurve("trkc"), LoadedCurve("gone") } };
            part.AfterLoad(project, track);

            var clone = (UVoicePart)part.Clone();

            Assert.Equal(new[] { "trkc" }, clone.curves.Select(c => c.abbr));
        }
    }
}
