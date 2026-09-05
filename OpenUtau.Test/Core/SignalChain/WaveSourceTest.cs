using Xunit;
using System.IO;
using System.Reflection;
using NAudio.Wave;

namespace OpenUtau.Core.SignalChain {
    public class WaveSourceTest {
        readonly ITestOutputHelper output;

        public WaveSourceTest(ITestOutputHelper output) {
            this.output = output;
        }

        float[] GetSamples() {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var file = Path.Join(dir, "Files", "sine.wav");
            using (var waveStream = Format.Wave.OpenFile(file)) {
                return Format.Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
            }
        }

        [Fact]
        public void ReadTest() {
            var source = new WaveSource(0, 25, 0, 2);
            source.SetSamples(GetSamples());

            var buffer = new float[500];
            int pos = 0;
            int newPos;
            while ((newPos = source.Mix(pos, buffer, 0, 500)) > pos) {
                output.WriteLine($"new pos {newPos}");
                pos = newPos;
            }
            output.WriteLine($"pos {pos}");
            Assert.Equal(44100 * 50 / 1000, pos);
        }

        [Fact]
        public void ReadWithOffsetTest() {
            var source = new WaveSource(15, 25, 0, 2);
            source.SetSamples(GetSamples());

            var buffer = new float[500];
            int pos = 0;
            int newPos;
            while ((newPos = source.Mix(pos, buffer, 0, 500)) > pos) {
                output.WriteLine($"new pos {newPos}");
                pos = newPos;
            }
            output.WriteLine($"pos {pos}");
            Assert.Equal(44100 * 80 / 1000 - 1, pos);
        }

        [Fact]
        public void IsReadyTest() {
            var source = new WaveSource(0, 50, 0, 1);
            Assert.False(source.IsReady(0, 100));
            source.SetSamples(GetSamples());
            Assert.True(source.IsReady(0, 100));
        }

        [Fact]
        public void IsReadyTestOutOfRange() {
            var source = new WaveSource(50, 50, 0, 1);
            Assert.True(source.IsReady(0, 100));
            int len = 44100 * 50 / 1000 * 2;
            Assert.True(source.IsReady(len - 100, 100));
            Assert.False(source.IsReady(len - 100 + 1, 100));
            Assert.True(source.IsReady(len * 2, 100));
            Assert.False(source.IsReady(len * 2 - 1, 100));
            source.SetSamples(GetSamples());
            Assert.True(source.IsReady(len - 100 + 1, 100));
            Assert.True(source.IsReady(len * 2 - 1, 100));
        }

        [Fact]
        public void MasterAdapterFadesAndStopsAtEndTest() {
            var source = new WaveSource(0, 20, 0, 2);
            var samples = new float[44100 * 20 / 1000 * 2];
            System.Array.Fill(samples, 1f);
            source.SetSamples(samples);
            var adapter = new MasterAdapter(source, endMs: 10);
            adapter.SetPosition(0);
            var buffer = new float[2048];

            int read = adapter.Read(buffer, 0, buffer.Length);

            Assert.Equal(44100 * 10 / 1000 * 2, read);
            Assert.Equal(0, buffer[0]);
            Assert.Equal(1, buffer[440]);
            Assert.Equal(0, buffer[read - 2]);
            Assert.Equal(0, adapter.Read(buffer, 0, buffer.Length));
        }
    }
}
