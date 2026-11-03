using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// The WebGPU plugin execution provider is built against ONNX Runtime API 30 while
    /// its package only advertises "1.24.4 or later"; 1.24.x kills the process natively
    /// on large DiffSinger models and 1.26.x rejects the library. The runner is therefore
    /// gated on the version the plugin was really built for, which makes this comparison
    /// the difference between a hidden runner and a crashed one.
    /// </summary>
    public class OnnxRuntimeVersionTest {
        [Theory]
        [InlineData("1.30.0", true)]
        [InlineData("1.31.0", true)]
        [InlineData("1.30", true)]
        [InlineData("1.30.0-rc.1", true)]
        [InlineData("1.31.0.5", true)]
        [InlineData("2.0.0", true)]
        [InlineData("1.29.1", false)]
        [InlineData("1.27.1", false)]
        [InlineData("1.25.1", false)]
        [InlineData("1.24.4", false)]
        [InlineData("1.23.0", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("unknown", false)]
        [InlineData("1", false)]
        public void GateFollowsThePluginAbi(string version, bool expected) {
            Assert.Equal(expected, OnnxRuntimeVersion.IsAtLeast(version, 1, 30));
        }

        [Fact]
        public void GateIsInclusiveOfTheTargetVersion() {
            Assert.True(OnnxRuntimeVersion.IsAtLeast("1.30.0", 1, 30));
            Assert.False(OnnxRuntimeVersion.IsAtLeast("1.30.0", 1, 31));
        }
    }
}
