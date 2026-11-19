using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenUtau.Classic {
    public class ClassicSingerTest {
        static ClassicSinger LoadSinger(string name) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var file = Path.Join(dir, "Files", name, "character.txt");
            VoicebankLoader.IsTest = true;
            var voicebank = new Voicebank() { File = file, BasePath = dir };
            VoicebankLoader.LoadVoicebank(voicebank);
            var singer = new ClassicSinger(voicebank);
            singer.EnsureLoaded();
            return singer;
        }

        [Fact]
        public void LookupsNeverObserveAPartialReload() {
            using var singer = LoadSinger("ja_cv");
            var aliases = singer.Otos.Select(oto => oto.Alias).ToArray();
            Assert.NotEmpty(aliases);
            int otoCount = singer.Otos.Count;

            using var stop = new CancellationTokenSource();
            var reader = Task.Run(() => {
                while (!stop.IsCancellationRequested) {
                    // Every snapshot, old or new, is a complete load of the same
                    // files, so every alias must resolve and the count must never dip.
                    Assert.Equal(otoCount, singer.Otos.Count);
                    foreach (var alias in aliases) {
                        Assert.True(singer.TryGetMappedOto(alias, 60, out _), alias);
                    }
                }
            });

            for (int i = 0; i < 20 && !reader.IsCompleted; i++) {
                singer.Reload();
            }
            stop.Cancel();
            reader.GetAwaiter().GetResult();
        }
    }
}
