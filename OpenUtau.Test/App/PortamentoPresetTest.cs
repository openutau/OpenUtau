using System.Collections.Generic;
using System.Linq;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.App {
    public class PortamentoPresetTest {
        [Fact]
        public void LengthStartPresetResetsToPlainPortamento() {
            var preset = new NotePresets.PortamentoPreset("Slow", 120, -60);

            var pitch = NotePropertiesViewModel.CreatePitchFromPreset(preset);

            Assert.Equal(2, pitch.data.Count);
            Assert.Equal(-60, pitch.data.First().X);
            Assert.Equal(60, pitch.data.Last().X);
            Assert.All(pitch.data, point => Assert.Equal(0, point.Y));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void PresetWithoutFullCurveResetsToPlainPortamento(int pointCount) {
            var preset = new NotePresets.PortamentoPreset("Snap", 2, -1) {
                PitchPoints = Enumerable.Range(0, pointCount).Select(i => new PitchPoint(i, 50)).ToList(),
            };

            var pitch = NotePropertiesViewModel.CreatePitchFromPreset(preset);

            Assert.Equal(2, pitch.data.Count);
            Assert.Equal(-1, pitch.data.First().X);
            Assert.Equal(1, pitch.data.Last().X);
            Assert.All(pitch.data, point => Assert.Equal(0, point.Y));
        }

        [Fact]
        public void PitchPointPresetKeepsItsOwnCurve() {
            var preset = new NotePresets.PortamentoPreset("Custom", new List<PitchPoint> {
                new PitchPoint(-40, 0),
                new PitchPoint(20, -35),
                new PitchPoint(120, 0),
            });

            var pitch = NotePropertiesViewModel.CreatePitchFromPreset(preset);

            Assert.Equal(new float[] { -40, 20, 120 }, pitch.data.Select(point => point.X));
            Assert.Equal(new float[] { 0, -35, 0 }, pitch.data.Select(point => point.Y));
        }

        [Fact]
        public void AppliedPresetIsDetachedFromTheNote() {
            var preset = new NotePresets.PortamentoPreset("Custom", new List<PitchPoint> {
                new PitchPoint(-40, 0),
                new PitchPoint(120, 0),
            });

            var pitch = NotePropertiesViewModel.CreatePitchFromPreset(preset);
            foreach (var point in pitch.data) {
                Assert.False(preset.PitchPoints.Any(original => ReferenceEquals(original, point)));
                point.X += 100;
                point.Y += 100;
            }

            Assert.Equal(-40, preset.PitchPoints.First().X);
            Assert.Equal(0, preset.PitchPoints.First().Y);
        }
    }
}
