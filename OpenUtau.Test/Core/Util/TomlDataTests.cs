using System;
using System.IO;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Util {
    public class TomlDataTests {
        [Fact]
        public void WhenLoadingTomlThenItReadsRootKey() {
            var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.toml");
            File.WriteAllText(filePath, "title = \"demo\"\n");

            var toml = TomlData.Load(filePath);

            Assert.True(toml.TryGetValue(string.Empty, "title", out var value));
            Assert.Equal("demo", value);
        }

        [Fact]
        public void WhenLoadingTomlThenItReadsSectionKey() {
            var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.toml");
            File.WriteAllText(filePath, "[singer]\nname = \"Tom\"\n");

            var toml = TomlData.Load(filePath);

            Assert.True(toml.TryGetValue("singer", "name", out var value));
            Assert.Equal("Tom", value);
        }

        [Fact]
        public void WhenLoadingTomlThenItEnumeratesSectionKeyValue() {
            var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.toml");
            File.WriteAllText(filePath, "[singer]\nname = \"Tom\"\n");

            var toml = TomlData.Load(filePath);

            Assert.Contains(toml.EnumerateEntries(), entry =>
                entry.Section == "singer" &&
                entry.Key == "name" &&
                Equals(entry.Value, "Tom"));
        }

        [Fact]
        public void WhenLoadingTomlThenItReadsFileLineByLineWithComments() {
            var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.toml");
            File.WriteAllText(filePath, "# comment\n[singer]\nname = \"Tom\" # inline\nage = 14\n");

            var toml = TomlData.Load(filePath);

            Assert.True(toml.TryGetValue("singer", "age", out var value));
            Assert.Equal(14L, value);
        }
    }
}
