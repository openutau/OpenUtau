using System;
using NAudio.Wave;

namespace OpenUtau.Core.SignalChain {
    class ExportAdapter : ISampleProvider {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        private const int FadeMilliseconds = 3;
        private const int FadeFrames = SampleRate * FadeMilliseconds / 1000;

        private readonly WaveFormat waveFormat;
        private readonly ISignalSource source;
        private readonly int startPosition;
        private readonly int endPosition;
        private int position;

        public WaveFormat WaveFormat => waveFormat;

        public ExportAdapter(ISignalSource source, double startMs = 0, double endMs = double.PositiveInfinity) {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            this.source = source;
            startPosition = (int)(startMs * SampleRate / 1000) * Channels;
            endPosition = double.IsPositiveInfinity(endMs)
                ? -1
                : (int)(endMs * SampleRate / 1000) * Channels;
            position = startPosition;
        }

        public int Read(float[] buffer, int offset, int count) {
            if (endPosition >= 0) {
                count = Math.Min(count, endPosition - position);
                if (count <= 0) {
                    return 0;
                }
            }
            for (int i = offset; i < offset + count; ++i) {
                buffer[i] = 0;
            }
            if (!source.IsReady(position, count)) {
                throw new Exception("All sources must be ready when exporting.");
            } else {
                int readPosition = position;
                int pos = source.Mix(position, buffer, offset, count);
                if (endPosition >= 0) {
                    position += count;
                    ApplyEdgeFades(buffer, offset, count, readPosition);
                    return count;
                }
                int n = Math.Max(0, pos - position);
                position = pos;
                return n;
            }
        }

        private void ApplyEdgeFades(float[] buffer, int offset, int n, int readPosition) {
            int startFrame = startPosition / Channels;
            int endFrame = endPosition / Channels;
            for (int i = 0; i < n; ++i) {
                int frame = (readPosition + i) / Channels;
                float gain = Math.Min(
                    Math.Clamp((frame - startFrame) / (FadeFrames - 1f), 0, 1),
                    Math.Clamp((endFrame - frame - 1f) / (FadeFrames - 1f), 0, 1));
                if (gain < 1) {
                    buffer[offset + i] *= gain;
                }
            }
        }
    }
}
