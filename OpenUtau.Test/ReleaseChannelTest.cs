using System;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test {
    public class ReleaseChannelTest {
        [Theory]
        [InlineData("1.5.0", null)]
        [InlineData("1.5.0.0", null)]
        [InlineData("1.5.1.1", "alpha")]
        [InlineData("1.5.1.3", "alpha")]
        [InlineData("1.5.1.99", "alpha")]
        public void FromVersion(string version, string? expected) {
            Assert.Equal(expected, ReleaseChannel.FromVersion(new Version(version)));
        }

        [Fact]
        public void FromVersionNull() {
            Assert.Null(ReleaseChannel.FromVersion(null));
        }
    }
}
