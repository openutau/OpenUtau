using System;

namespace OpenUtau.Core.Format {
    /// <summary>
    /// Interleaved samples with the min and max of each channel over blocks of
    /// <see cref="BlockSize"/> frames, so the min and max of a long range read
    /// blocks instead of every sample.
    /// </summary>
    public class WavePeaks {
        public const int BlockSize = 256;

        public int SampleRate { get; }
        public int Channels { get; }
        public int Frames { get; }

        private readonly float[] samples;
        // [block * Channels + channel]
        private readonly float[] blockMin;
        private readonly float[] blockMax;

        public WavePeaks(float[] samples, int channels, int sampleRate) {
            this.samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
            Frames = samples.Length / channels;
            int blocks = Frames / BlockSize;
            blockMin = new float[blocks * channels];
            blockMax = new float[blocks * channels];
            for (int b = 0; b < blocks; ++b) {
                for (int c = 0; c < channels; ++c) {
                    Scan(c, b * BlockSize, (b + 1) * BlockSize, out blockMin[b * channels + c], out blockMax[b * channels + c]);
                }
            }
        }

        /// <summary>The min and max of a channel over frames [from, to), which must not be empty.</summary>
        public void MinMax(int channel, int from, int to, out float min, out float max) {
            int firstBlock = (from + BlockSize - 1) / BlockSize;
            int endBlock = to / BlockSize;
            if (firstBlock >= endBlock) {
                Scan(channel, from, to, out min, out max);
                return;
            }
            min = float.MaxValue;
            max = float.MinValue;
            if (from < firstBlock * BlockSize) {
                Scan(channel, from, firstBlock * BlockSize, out min, out max);
            }
            for (int b = firstBlock; b < endBlock; ++b) {
                min = Math.Min(min, blockMin[b * Channels + channel]);
                max = Math.Max(max, blockMax[b * Channels + channel]);
            }
            if (endBlock * BlockSize < to) {
                Scan(channel, endBlock * BlockSize, to, out float tailMin, out float tailMax);
                min = Math.Min(min, tailMin);
                max = Math.Max(max, tailMax);
            }
        }

        private void Scan(int channel, int from, int to, out float min, out float max) {
            min = float.MaxValue;
            max = float.MinValue;
            for (int i = from * Channels + channel; i < to * Channels; i += Channels) {
                float v = samples[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }
    }
}
