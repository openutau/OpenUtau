using System;
using System.IO;
using System.Linq;
using OpenUtau.Core.Format;
using Xunit;

namespace OpenUtau.Core.Analysis {
    /// <summary>The hnsep cache next to the source, with a stand-in for the network.</summary>
    public class HnsepCacheTest : IDisposable {
        readonly string dir = Path.Combine(Path.GetTempPath(), "HnsepCacheTest-" + Guid.NewGuid());

        public HnsepCacheTest() {
            Directory.CreateDirectory(dir);
        }

        public void Dispose() {
            Directory.Delete(dir, true);
        }

        static float[] Samples(int n) => Enumerable.Range(0, n).Select(i => (float)Math.Sin(i * 0.05)).ToArray();

        [Fact]
        public void PathIsHifisamplers() {
            Assert.Equal(Path.Combine("vb", "_あ_hnsep"), HnsepCache.PathOf(Path.Combine("vb", "_あ.wav")));
        }

        [Fact]
        public void SeparatesOnceThenLoads() {
            var source = Path.Combine(dir, "_あ.wav");
            var x = Samples(5000);
            int calls = 0;
            float[] Half(float[] s) { calls++; return s.Select(v => v * 0.5f).ToArray(); }

            var first = HnsepCache.Harmonic(source, x, Half);
            var second = HnsepCache.Harmonic(source, x, Half);
            Assert.Equal(1, calls);
            Assert.Equal(first, second);
            // Saved as hifisampler saves it: a [1, 1, samples] tensor.
            var (data, shape) = TorchFile.ReadFloatTensor(HnsepCache.PathOf(source));
            Assert.Equal(new[] { 1, 1, x.Length }, shape);
            Assert.Equal(first, data);
        }

        [Fact]
        public void RebuildsOnOtherLengthOrUnreadable() {
            var source = Path.Combine(dir, "a.wav");
            int calls = 0;
            float[] Copy(float[] s) { calls++; return (float[])s.Clone(); }

            HnsepCache.Harmonic(source, Samples(1000), Copy);
            Assert.Equal(1200, HnsepCache.Harmonic(source, Samples(1200), Copy).Length);
            Assert.Equal(2, calls);

            File.WriteAllText(HnsepCache.PathOf(source), "not a torch file");
            HnsepCache.Harmonic(source, Samples(1200), Copy);
            Assert.Equal(3, calls);
            HnsepCache.Harmonic(source, Samples(1200), Copy);
            Assert.Equal(3, calls);
        }
    }
}
