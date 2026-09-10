using System;
using System.Linq;
using Xunit;

namespace OpenUtau.Core.SignalChain {
    public class CrossSynthDSPTest {
        [Fact]
        public void BypassesAtRatioExtremes() {
            var a = Enumerable.Range(0, 4096).Select(i => MathF.Sin(i * 0.01f)).ToArray();
            var b = Enumerable.Range(0, 4096).Select(i => MathF.Cos(i * 0.01f)).ToArray();

            Assert.Equal(a, CrossSynthDSP.StftBlend(a, b, new[] { 0f }));
            Assert.Equal(b, CrossSynthDSP.StftBlend(a, b, new[] { 1f }));
        }

        [Fact]
        public void ProducesFiniteIntermediateBlend() {
            var a = Enumerable.Range(0, 4096).Select(i => MathF.Sin(i * 0.01f)).ToArray();
            var b = Enumerable.Range(0, 4096).Select(i => MathF.Cos(i * 0.02f)).ToArray();

            var result = CrossSynthDSP.StftBlend(a, b, new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f });

            Assert.Equal(a.Length, result.Length);
            Assert.All(result, sample => Assert.True(float.IsFinite(sample)));
        }
    }
}
