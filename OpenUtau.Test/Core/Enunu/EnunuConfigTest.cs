using System;
using System.IO;
using OpenUtau.Core.Enunu;
using Xunit;

namespace OpenUtau.Core {
    public class EnunuConfigTest : IDisposable {
        readonly string location = Path.Combine(Path.GetTempPath(), $"enunu-config-{Guid.NewGuid():N}");

        public EnunuConfigTest() {
            Directory.CreateDirectory(Path.Join(location, "model"));
        }

        public void Dispose() {
            try { Directory.Delete(location, true); } catch { }
        }

        [Fact]
        public void ModelFolderPathsAreRelativeToTheVoicebank() {
            // As SimpleEnunu voicebanks ship: config.yaml and qst.hed in model/, a copy of the table at the top.
            File.WriteAllText(Path.Join(location, "model", "config.yaml"), "feature_type: world\ntable_path: ./kana.table\n");
            File.WriteAllText(Path.Join(location, "model", "kana.table"), "");
            File.WriteAllText(Path.Join(location, "model", "qst.hed"), "");
            File.WriteAllText(Path.Join(location, "other.table"), "");
            var config = EnunuConfig.SetSimpleENUNUConfig(location);
            Assert.Equal(Path.Join("model", "kana.table"), config.tablePath);
            Assert.Equal(Path.Join("model", "qst.hed"), config.questionPath);
            Assert.True(File.Exists(Path.Join(location, config.tablePath)));
        }

        [Fact]
        public void FindsTheTableWhenConfigHasNone() {
            File.WriteAllText(Path.Join(location, "config.yaml"), "feature_type: world\n");
            File.WriteAllText(Path.Join(location, "kana.table"), "");
            File.WriteAllText(Path.Join(location, "qst.hed"), "");
            var config = EnunuConfig.SetSimpleENUNUConfig(location);
            Assert.Equal("kana.table", config.tablePath);
            Assert.Equal("qst.hed", config.questionPath);
        }

        [Fact]
        public void LyricsOutsideTheTableAreUnknown() {
            var table = new System.Collections.Generic.Dictionary<string, string[]> {
                ["あ"] = new[] { "a" }, ["さ"] = new[] { "s", "a" }, ["っ"] = new[] { "cl" }, ["R"] = new[] { "pau" }, ["息"] = new[] { "br" },
            };
            var phonemes = new System.Collections.Generic.HashSet<string> { "a", "s", "cl", "pau", "br", "i", "B" };
            // Table keys, phonemes written directly, and a mix of both.
            Assert.True(EnunuSinger.IsKnownLyric("あ", table, phonemes));
            Assert.True(EnunuSinger.IsKnownLyric("i B", table, phonemes));
            Assert.True(EnunuSinger.IsKnownLyric("a R", table, phonemes));
            // っ is split off as utaupy does.
            Assert.True(EnunuSinger.IsKnownLyric("さっ", table, phonemes));
            // Not in the table.
            Assert.False(EnunuSinger.IsKnownLyric("さ子音", table, phonemes));
            Assert.False(EnunuSinger.IsKnownLyric("息_あ", table, phonemes));
            Assert.False(EnunuSinger.IsKnownLyric("xi", table, phonemes));
            Assert.False(EnunuSinger.IsKnownLyric("", table, phonemes));
            // Nothing to check against when the table did not load.
            Assert.True(EnunuSinger.IsKnownLyric("xi", new System.Collections.Generic.Dictionary<string, string[]>(), phonemes));
        }
    }
}
