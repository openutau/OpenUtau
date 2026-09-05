using System;
using System.IO;
using OpenUtau.Core.Analysis;
using Xunit;

namespace OpenUtau.Core {
    public class GameGgmlBackendTest {
        [Fact]
        public void ResolveGgufPathHonorsExplicitLocation() {
            string directory = CreateTempDirectory();
            try {
                string small = Path.Combine(directory, "small.gguf");
                string medium = Path.Combine(directory, "medium.gguf");
                File.WriteAllBytes(small, new byte[1]);
                File.WriteAllBytes(medium, new byte[2]);

                Assert.Equal(medium, GameGgmlBackend.ResolveGgufPath(directory));
            } finally {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LoadConfigReadsConfigNextToGgufWithoutOnnxPackage() {
            string directory = CreateTempDirectory();
            try {
                File.WriteAllBytes(Path.Combine(directory, "model.gguf"), new byte[1]);
                File.WriteAllText(
                    Path.Combine(directory, "config.json"),
                    "{\"samplerate\":32000,\"timestep\":0.02,\"languages\":{\"zh\":1}}");

                GameConfig config = GameGgmlBackend.LoadConfig(directory);

                Assert.Equal(32000, config.SampleRate);
                Assert.Equal(0.02f, config.Timestep);
                Assert.Equal(1, config.Languages!["zh"]);
            } finally {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void LoadConfigRejectsMissingConfigNextToGguf() {
            string directory = CreateTempDirectory();
            try {
                File.WriteAllBytes(Path.Combine(directory, "model.gguf"), new byte[1]);

                var error = Assert.Throws<InvalidOperationException>(
                    () => GameGgmlBackend.LoadConfig(directory));

                Assert.Contains("config.json", error.Message);
            } finally {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTempDirectory() {
            string directory = Path.Combine(Path.GetTempPath(), $"OpenUtau.GameGgmlBackendTest.{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
