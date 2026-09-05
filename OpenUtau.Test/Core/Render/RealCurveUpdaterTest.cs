using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class RealCurveUpdaterTest {
        const string Ene = "ene";

        [Fact]
        public void RenderPhraseEventsReportsNonEmptyRealCurvesOnly() {
            int reports = 0;
            var events = new RenderPhraseEvents(_ => reports++);

            events.ReportRealCurves(new List<RenderRealCurveResult>());
            events.ReportRealCurves(new[] {
                new RenderRealCurveResult {
                    abbr = Ene,
                    ticks = new[] { 0f },
                    values = new[] { 0.1f },
                },
            });

            Assert.Equal(1, reports);
        }

        [Fact]
        public void BuildUpdatesConvertsTicksToPartLocalCoordinates() {
            var result = new RenderRealCurveResult {
                abbr = Ene,
                ticks = new[] { 0f, 5f, 10f },
                values = new[] { 0.1f, 0.2f, 0.3f },
            };

            var update = Assert.Single(RealCurveUpdater.BuildUpdates(
                partPosition: 100,
                phrasePosition: 250,
                phraseHash: 42,
                results: new[] { result }));

            Assert.Equal(Ene, update.abbr);
            Assert.Equal((ulong)42, update.phraseHash);
            Assert.Equal(150, update.startTick);
            Assert.Equal(160, update.endTick);
            Assert.Equal(new[] { 150, 150, 155, 160 }, update.xs);
            Assert.Equal(new[] { -1, 100, 200, 300 }, update.ys);
        }

        [Fact]
        public void ApplyReplacesMatchingPhraseRangeOnly() {
            var project = CreateProjectWithCurve(out var part, out var curve);
            curve.realXs.AddRange(new[] { 0, 0, 10, 100, 100, 110 });
            curve.realYs.AddRange(new[] { -1, 10, 20, -1, 30, 40 });
            var update = new RealCurveUpdate(
                Ene,
                phraseHash: 42,
                startTick: 100,
                endTick: 110,
                xs: new[] { 100, 100, 105, 110 },
                ys: new[] { -1, 300, 400, 500 });

            bool changed = RealCurveUpdater.Apply(
                project,
                part,
                new[] { update },
                new HashSet<ulong> { 42 });

            Assert.True(changed);
            Assert.Equal(new[] { 0, 0, 10, 100, 100, 105, 110 }, curve.realXs);
            Assert.Equal(new[] { -1, 10, 20, -1, 300, 400, 500 }, curve.realYs);
        }

        [Fact]
        public void ApplySkipsStalePhraseHash() {
            var project = CreateProjectWithCurve(out var part, out var curve);
            curve.realXs.AddRange(new[] { 0, 0, 10 });
            curve.realYs.AddRange(new[] { -1, 10, 20 });
            var update = new RealCurveUpdate(
                Ene,
                phraseHash: 42,
                startTick: 0,
                endTick: 10,
                xs: new[] { 0, 0, 5 },
                ys: new[] { -1, 300, 400 });

            bool changed = RealCurveUpdater.Apply(
                project,
                part,
                new[] { update },
                new HashSet<ulong> { 43 });

            Assert.False(changed);
            Assert.Equal(new[] { 0, 0, 10 }, curve.realXs);
            Assert.Equal(new[] { -1, 10, 20 }, curve.realYs);
        }

        [Fact]
        public void TrimToCoverageRemovesGhostPointsOutsideCoverage() {
            var project = CreateProjectWithCurve(out var part, out var curve);
            curve.realXs.AddRange(new[] { 0, 0, 25, 50, 60, 80, 100 });
            curve.realYs.AddRange(new[] { -1, 10, 20, 30, 40, 50, 60 });

            bool changed = RealCurveUpdater.TrimToCoverage(
                project, part, new List<(int start, int end)> { (0, 50) });

            Assert.True(changed);
            Assert.Equal(new[] { 0, 0, 25, 50 }, curve.realXs);
            Assert.Equal(new[] { -1, 10, 20, 30 }, curve.realYs);
        }

        [Fact]
        public void TrimToCoverageKeepsPointsWhenAllInsideCoverage() {
            var project = CreateProjectWithCurve(out var part, out var curve);
            curve.realXs.AddRange(new[] { 0, 0, 25, 50 });
            curve.realYs.AddRange(new[] { -1, 10, 20, 30 });

            bool changed = RealCurveUpdater.TrimToCoverage(
                project, part, new List<(int start, int end)> { (0, 50) });

            Assert.False(changed);
            Assert.Equal(new[] { 0, 0, 25, 50 }, curve.realXs);
        }

        [Fact]
        public void TrimToCoverageSkipsWhenNoRanges() {
            var project = CreateProjectWithCurve(out var part, out var curve);
            curve.realXs.AddRange(new[] { 0, 50, 100 });
            curve.realYs.AddRange(new[] { -1, 30, 60 });

            bool changed = RealCurveUpdater.TrimToCoverage(
                project, part, new List<(int start, int end)>());

            Assert.False(changed);
            Assert.Equal(3, curve.realXs.Count);
        }

        static UProject CreateProjectWithCurve(out UVoicePart part, out UCurve curve) {
            var project = new UProject();
            var descriptor = new UExpressionDescriptor("energy", Ene, 0, 100, 0) {
                type = UExpressionType.Curve,
            };
            project.RegisterExpression(descriptor);
            part = new UVoicePart {
                trackNo = 0,
                position = 0,
                Duration = 480,
            };
            project.parts.Add(part);
            curve = new UCurve(descriptor);
            part.curves.Add(curve);
            return project;
        }
    }
}
