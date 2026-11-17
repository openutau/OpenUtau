using System;
using System.Threading;
using Xunit;

namespace OpenUtau.Core.Util {
    public class MelSpectrogramTest {
        private const int Fft = MelSpectrogram.DefaultFftSize;
        private const int Hop = MelSpectrogram.DefaultHop;
        private const int Bands = MelSpectrogram.DefaultBands;

        private static float[] Sine(double hz, double seconds, int channels = 1) {
            int frames = (int)(seconds * MelSpectrogram.SampleRate);
            var samples = new float[frames * channels];
            for (int i = 0; i < frames; ++i) {
                float v = (float)Math.Sin(2 * Math.PI * hz * i / MelSpectrogram.SampleRate);
                for (int c = 0; c < channels; ++c) {
                    samples[i * channels + c] = v;
                }
            }
            return samples;
        }

        private static byte[] ReadAll(MelSpectrogram mel, int first, int count) {
            var dest = new byte[count * mel.Bands];
            mel.Read(first, count, dest);
            return dest;
        }

        [Theory]
        [InlineData(220.0, 2048, 128)]
        [InlineData(440.0, 2048, 128)]
        [InlineData(1760.0, 2048, 128)]
        [InlineData(440.0, 4096, 256)]
        [InlineData(1760.0, 1024, 512)]
        public void SinePeaksAtItsBand(double hz, int fft, int bands) {
            var data = new byte[bands];
            MelSpectrogram.Compute(Sine(hz, 1.0), 1, fft, Hop, bands, 100, 1, data, 0);
            int peak = 0;
            for (int b = 1; b < bands; ++b) {
                if (data[b] > data[peak]) {
                    peak = b;
                }
            }
            double expected = MelSpectrogram.HzToBand(hz, bands);
            Assert.InRange(peak, expected - 1.5 * bands / Bands, expected + 1.5 * bands / Bands);
            Assert.InRange(MelSpectrogram.ByteToDb(data[peak]), -12, 3);
        }

        [Fact]
        public void SilenceIsAboveNoData() {
            var data = new byte[Bands];
            MelSpectrogram.Compute(new float[44100], 1, Fft, Hop, Bands, 50, 1, data, 0);
            Assert.All(data, v => Assert.Equal(1, v));
        }

        [Fact]
        public void BatchMatchesSingleFrames() {
            var samples = Sine(300, 0.5);
            var together = new byte[3 * Bands];
            MelSpectrogram.Compute(samples, 1, Fft, Hop, Bands, 10, 3, together, 0);
            for (int f = 0; f < 3; ++f) {
                var single = new byte[Bands];
                MelSpectrogram.Compute(samples, 1, Fft, Hop, Bands, 10 + f, 1, single, 0);
                Assert.Equal(single, together[(f * Bands)..((f + 1) * Bands)]);
            }
        }

        [Fact]
        public void StereoMatchesMono() {
            var mono = new byte[Bands];
            var stereo = new byte[Bands];
            MelSpectrogram.Compute(Sine(500, 0.5), 1, Fft, Hop, Bands, 40, 1, mono, 0);
            MelSpectrogram.Compute(Sine(500, 0.5, 2), 2, Fft, Hop, Bands, 40, 1, stereo, 0);
            Assert.Equal(mono, stereo);
        }

        [Fact]
        public void BackgroundComputationFillsEveryFrame() {
            using var mel = new MelSpectrogram(Sine(440, 20.0), 1);
            for (int i = 0; i < 500 && ReadAll(mel, 0, mel.FrameCount).AsSpan().IndexOf((byte)0) >= 0; ++i) {
                Thread.Sleep(20);
            }
            Assert.True(ReadAll(mel, 0, mel.FrameCount).AsSpan().IndexOf((byte)0) < 0);
        }

        [Fact]
        public void CustomSettingsChangeTheLayout() {
            using var mel = new MelSpectrogram(Sine(440, 2.0), 1, 4096, 110, 256);
            Assert.Equal(4096, mel.FftSize);
            Assert.Equal(110, mel.Hop);
            Assert.Equal(256, mel.Bands);
            Assert.Equal(2 * MelSpectrogram.SampleRate / 110 + 1, mel.FrameCount);
            for (int i = 0; i < 500 && ReadAll(mel, 0, mel.FrameCount).AsSpan().IndexOf((byte)0) >= 0; ++i) {
                Thread.Sleep(20);
            }
            Assert.True(ReadAll(mel, 0, mel.FrameCount).AsSpan().IndexOf((byte)0) < 0);
        }

        [Fact]
        public void OutOfRangeReadsAreZero() {
            using var mel = new MelSpectrogram(Sine(440, 1.0), 1);
            Assert.All(ReadAll(mel, -3, 2), v => Assert.Equal(0, v));
            Assert.All(ReadAll(mel, mel.FrameCount + 5, 2), v => Assert.Equal(0, v));
        }
    }
}
