using System;
using NAudio.Wave;

namespace OpenUtau.Core.SignalChain {
    class MasterAdapter : ISampleProvider {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        // Short edge fades prevent clicks without mixing audio past the playback end.
        private const int FadeMilliseconds = 3;
        private const int FadeFrames = SampleRate * FadeMilliseconds / 1000;

        private readonly WaveFormat waveFormat;
        private readonly ISignalSource source;
        private readonly int endPosition;
        private int position;
        private int startPosition;

        public WaveFormat WaveFormat => waveFormat;
        public int Waited { get; private set; }
        public bool IsWaiting { get; private set; }
        public MasterAdapter(ISignalSource source, double endMs = double.PositiveInfinity) {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            this.source = source;
            endPosition = double.IsPositiveInfinity(endMs)
                ? -1
                : (int)(endMs * SampleRate / 1000) * Channels;
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
                Waited += count;
                IsWaiting = true;
                return count;
            } else {
                int readPosition = position;
                int pos = source.Mix(position, buffer, offset, count);
                int n = Math.Max(0, pos - position);
                position = pos;
                int startFrame = startPosition / Channels;
                int endFrame = endPosition / Channels;
                for (int i = 0; i < n; ++i) {
                    int frame = (readPosition + i) / Channels;
                    int elapsedFrames = frame - startFrame;
                    float gain = Math.Clamp(
                        elapsedFrames / (FadeFrames - 1f),
                        0,
                        1);
                    if (endPosition >= 0) {
                        int remainingFrames = endFrame - (readPosition + i) / Channels;
                        if (remainingFrames <= FadeFrames) {
                            gain = Math.Min(
                                gain,
                                Math.Clamp(
                                    (remainingFrames - 1f) / (FadeFrames - 1),
                                    0,
                                    1));
                        }
                    }
                    if (gain < 1) {
                        buffer[offset + i] *= gain;
                    }
                }
                IsWaiting = false;
                return n;
            }
        }

        public void SetPosition(int position) {
            this.position = position;
            startPosition = position;
            Waited = 0;
        }
    }
}
