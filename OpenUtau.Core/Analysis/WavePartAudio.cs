using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Analysis;

/// <summary>Raw, unfaded analysis input from the part's visible source range.</summary>
internal static class WavePartAudio {
    internal static float[] GetMonoSamples(UProject project, UWavePart part) {
        var (startFrame, endFrame) = part.GetSampleRange(project);
        return ToMono(part.Samples, startFrame, endFrame, part.channels);
    }

    internal static float[] ToMono(float[] samples, int startFrame, int endFrame, int channels) {
        if (channels == 1 && startFrame == 0 && endFrame == samples.Length) {
            return samples;
        }
        var mono = new float[endFrame - startFrame];
        for (int i = 0; i < mono.Length; ++i) {
            float sum = 0;
            int offset = (startFrame + i) * channels;
            for (int channel = 0; channel < channels; ++channel) {
                sum += samples[offset + channel];
            }
            mono[i] = sum / channels;
        }
        return mono;
    }
}
