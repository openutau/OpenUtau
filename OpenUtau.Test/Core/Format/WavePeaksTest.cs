using System;
using Xunit;

namespace OpenUtau.Core.Format {
    public class WavePeaksTest {
        [Fact]
        public void MinMaxMatchesScan() {
            // Stereo, with a partial last block, so ranges cover partial head and
            // tail blocks, whole blocks, and ranges inside one block.
            const int channels = 2;
            int frames = WavePeaks.BlockSize * 5 + 37;
            var random = new Random(1);
            var samples = new float[frames * channels];
            for (int i = 0; i < samples.Length; ++i) {
                samples[i] = (float)(random.NextDouble() * 2 - 1);
            }
            var peaks = new WavePeaks(samples, channels, 44100);
            Assert.Equal(frames, peaks.Frames);

            int[] points = { 0, 1, 100, 255, 256, 257, 511, 512, 700, 1024, 1279, 1280, frames - 1, frames };
            foreach (int from in points) {
                foreach (int to in points) {
                    if (to <= from) {
                        continue;
                    }
                    for (int c = 0; c < channels; ++c) {
                        float min = float.MaxValue, max = float.MinValue;
                        for (int f = from; f < to; ++f) {
                            min = Math.Min(min, samples[f * channels + c]);
                            max = Math.Max(max, samples[f * channels + c]);
                        }
                        peaks.MinMax(c, from, to, out float peakMin, out float peakMax);
                        Assert.Equal(min, peakMin);
                        Assert.Equal(max, peakMax);
                    }
                }
            }
        }
    }
}
