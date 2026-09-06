using OpenUtau.App.ViewModels;
using Xunit;

namespace OpenUtau.Test {
    public class UpdaterChannelTest {
        static UpdaterViewModel.GithubRelease Release(bool draft, bool prerelease, string tag) => new() {
            draft = draft,
            prerelease = prerelease,
            tag_name = tag,
        };

        [Theory]
        [InlineData("stable", false, "1.5.0", true)]
        [InlineData("stable", true, "1.5.0-beta", false)]
        [InlineData("stable", true, "1.5.1.3-alpha", false)]
        [InlineData("beta", false, "1.5.0", false)]
        [InlineData("beta", true, "1.5.0-beta", true)]
        [InlineData("beta", true, "1.5.1.3-alpha", false)]
        // Legacy beta releases have bare (unsuffixed) prerelease tags.
        [InlineData("beta", true, "1.4.10", true)]
        [InlineData("alpha", false, "1.5.0", false)]
        [InlineData("alpha", true, "1.5.1.3-alpha", true)]
        [InlineData("alpha", true, "1.5.0-beta", true)]
        [InlineData("alpha", true, "1.4.10", true)]
        [InlineData("unknown", false, "1.5.0", true)]
        [InlineData("unknown", true, "1.5.0-beta", false)]
        public void IsReleaseForChannel(string channel, bool prerelease, string tag, bool expected) {
            Assert.Equal(expected, UpdaterViewModel.IsReleaseForChannel(Release(false, prerelease, tag), channel));
        }

        [Theory]
        [InlineData(false, "1.5.0-beta")]
        [InlineData(true, "1.5.1.3-alpha")]
        public void DraftsAreNeverSelected(bool prerelease, string tag) {
            Assert.False(UpdaterViewModel.IsReleaseForChannel(Release(true, prerelease, tag), "alpha"));
            Assert.False(UpdaterViewModel.IsReleaseForChannel(Release(true, prerelease, tag), "beta"));
            Assert.False(UpdaterViewModel.IsReleaseForChannel(Release(true, prerelease, tag), "stable"));
        }
    }
}
