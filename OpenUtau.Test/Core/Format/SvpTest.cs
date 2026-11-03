using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Format {
    public class SvpTest {
        readonly string basePath;

        public SvpTest() {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            basePath = Path.Join(dir, "Files", "Svp");
        }

        UWavePart LoadWavePart(string file) {
            var project = SVP.Load(Path.Join(basePath, file));
            return Assert.Single(project.parts.OfType<UWavePart>());
        }

        [Fact]
        public void AudioTrackLoads() {
            // A wave part with no peaks crashes the part control that binds to them,
            // and one with no duration renders as zero length.
            var part = LoadWavePart("instrumental.svp");

            Assert.NotNull(part.Peaks);
            Assert.DoesNotContain("[Missing]", part.DisplayName);
            Assert.True(part.fileDurationMs > 0);
            Assert.True(part.Duration > 0);
        }

        [Fact]
        public void MissingAudioKeepsDeclaredDuration() {
            // The SVP declares the audio length, so a track whose file is absent keeps
            // its place and its length instead of being dropped on import.
            var part = LoadWavePart("missing-audio.svp");

            Assert.NotNull(part.Peaks);
            Assert.Contains("[Missing]", part.DisplayName);
            Assert.Equal(50, part.fileDurationMs, 3);
            Assert.True(part.Duration > 0);
        }
    }
}
