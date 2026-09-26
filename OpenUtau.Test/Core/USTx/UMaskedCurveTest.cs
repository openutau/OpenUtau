using System.Linq;
using Xunit;

namespace OpenUtau.Core.Ustx {
    public class UMaskedCurveTest {
        [Fact]
        public void MaskedCurvesHaveValuesOnlyWhereSet() {
            var curve = new UMaskedCurve("pito");
            Assert.False(curve.TrySample(0, out _));
            // A line from 6000 at tick 0 to 6100 at tick 20: five grid values, one run.
            curve.Set(0, 6000, 20, 6100);
            Assert.Equal(new[] { 6000f, 6025f, 6050f, 6075f, 6100f }, Assert.Single(curve.runs).ys);
            Assert.True(curve.TrySample(7, out float y));
            Assert.Equal(6035f, y);
            Assert.False(curve.TrySample(21, out _));
            // A second line apart from the first is a second run, with no values between them.
            curve.Set(100, 6200, 100, 6200);
            Assert.Equal(2, curve.runs.Count);
            Assert.False(curve.TrySample(60, out _));
            // Clearing splits a run; setting values joins runs that become consecutive.
            curve.Clear(8, 12);
            Assert.Equal(new[] { (0, 2), (15, 2), (100, 1) }, curve.runs.Select(r => (r.x, r.ys.Length)));
            curve.SetValues(new[] { (10, 1f) });
            Assert.Equal(new[] { 6000f, 6025f, 1f, 6075f, 6100f }, curve.runs[0].ys);
        }

        [Fact]
        public void MaskedCurveCommandsAreUndoable() {
            var part = new UVoicePart();
            var set = new SetMaskedCurveCommand(part, "pito", 0, 6000, 10, 6000);
            set.Execute();
            Assert.Equal(3, part.maskedCurves.Single().runs.Single().ys.Length);
            var clear = new ClearMaskedCurveCommand(part, "pito", 0, 10);
            clear.Execute();
            // A curve without values isn't kept.
            Assert.Empty(part.maskedCurves);
            clear.Unexecute();
            Assert.Equal(3, part.maskedCurves.Single().runs.Single().ys.Length);
            set.Unexecute();
            Assert.Empty(part.maskedCurves);
        }

        [Fact]
        public void PartsKeepTheirMaskedCurves() {
            var part = new UVoicePart();
            part.maskedCurves.Add(new UMaskedCurve("rpit"));
            part.maskedCurves[0].SetValues(new[] { (960, 6000.25f), (965, 6010.5f) });
            var yaml = Yaml.DefaultSerializer.Serialize(part);
            Assert.Contains("{x: 960, ys: [6000.25, 6010.5]}", yaml);
            var read = Yaml.DefaultDeserializer.Deserialize<UVoicePart>(yaml).maskedCurves.Single();
            Assert.Equal("rpit", read.abbr);
            Assert.Equal(new[] { 6000.25f, 6010.5f }, read.runs.Single().ys);
            Assert.Equal(1, part.Clone() is UVoicePart copy ? copy.maskedCurves.Count : 0);
        }
    }
}
