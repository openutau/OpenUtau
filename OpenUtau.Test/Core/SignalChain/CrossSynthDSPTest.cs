using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OpenUtau.Core.SignalChain {
    public class CrossSynthDSPTest {
        [Fact]
        public void BypassesWhenColorWeightsAreZero() {
            var baseAudio = Enumerable.Range(0, 4096).Select(i => MathF.Sin(i * 0.01f)).ToArray();
            var colorA = Enumerable.Range(0, 4096).Select(i => MathF.Cos(i * 0.01f)).ToArray();
            var colorB = Enumerable.Range(0, 4096).Select(i => MathF.Sin(i * 0.02f)).ToArray();

            var colorAudios = new List<float[]> { colorA, colorB };
            var zeroCurves = new List<float[]> {
                new float[] { 0f, 0f, 0f, 0f },
                new float[] { 0f, 0f, 0f, 0f }
            };

            var result = CrossSynthDSP.MorphN(baseAudio, colorAudios, zeroCurves);

            Assert.Equal(baseAudio.Length, result.Length);
            // Verify that zero weights preserve the base signal output within tolerance
            for (int i = 0; i < baseAudio.Length; i++) {
                Assert.Equal(baseAudio[i], result[i], 3);
            }
        }

        [Fact]
        public void ProducesFiniteIntermediateNWayBlend() {
            var baseAudio = Enumerable.Range(0, 4096).Select(i => MathF.Sin(i * 0.01f)).ToArray();
            var colorA = Enumerable.Range(0, 4096).Select(i => MathF.Cos(i * 0.01f)).ToArray();
            var colorB = Enumerable.Range(0, 4096).Select(i => MathF.Sin(i * 0.03f)).ToArray();

            var colorAudios = new List<float[]> { colorA, colorB };
            // Multi-color curve weights (30% color A, 40% color B -> leaving 30% base)
            var curves = new List<float[]> {
                new float[] { 30f, 30f, 30f, 30f, 30f },
                new float[] { 40f, 40f, 40f, 40f, 40f }
            };

            var result = CrossSynthDSP.MorphN(baseAudio, colorAudios, curves);

            Assert.Equal(baseAudio.Length, result.Length);
            Assert.All(result, sample => Assert.True(float.IsFinite(sample)));
        }

        [Fact]
        public void NormalizesExcessiveWeightsGracefully() {
            var baseAudio = Enumerable.Range(0, 4096).Select(i => MathF.Sin(i * 0.01f)).ToArray();
            var colorA = Enumerable.Range(0, 4096).Select(i => MathF.Cos(i * 0.01f)).ToArray();
            var colorB = Enumerable.Range(0, 4096).Select(i => MathF.Cos(i * 0.02f)).ToArray();

            var colorAudios = new List<float[]> { colorA, colorB };
            // Total sum = 180% (exceeds 100%), which triggers automatic normalization
            var overblownCurves = new List<float[]> {
                new float[] { 90f, 90f, 90f },
                new float[] { 90f, 90f, 90f }
            };

            var result = CrossSynthDSP.MorphN(baseAudio, colorAudios, overblownCurves);

            Assert.Equal(baseAudio.Length, result.Length);
            Assert.All(result, sample => {
                Assert.True(float.IsFinite(sample));
                Assert.InRange(sample, -2.0f, 2.0f); // Guarantees no explosive gain blowup
            });
        }
    }
}