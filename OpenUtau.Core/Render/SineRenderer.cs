using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Render {
    public class SineRenderer : IRenderer {
        public static readonly SineRenderer Instance = new SineRenderer();

        const int SampleRate = 44100;
        const float Amplitude = 0.25f;
        const double FadeMs = 10;
        const int FrameSize = 256;
        const int PitchInterval = 5;

        static readonly HashSet<string> supportedExp = new HashSet<string> {
            Format.Ustx.DYN,
            Format.Ustx.PITD,
        };

        SineRenderer() { }

        public static bool IsFallbackActive(UTrack track) {
            return track.Singer == null || !track.Singer.Found || !track.Singer.Loaded;
        }

        public static IRenderer RendererFor(UTrack track) {
            return IsFallbackActive(track) && Preferences.Default.PlaySineWhenNoSinger ? Instance : null;
        }

        public USingerType SingerType => USingerType.Classic;

        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender = false, RenderPhraseEvents? renderEvents = null) {
            return Task.Run(() => {
                var result = Layout(phrase);
                string progressInfo = $"Track {trackNo + 1}: sine tone";
                progress.Complete(0, progressInfo);
                int totalSamples = (int)Math.Ceiling(result.estimatedLengthMs / 1000.0 * SampleRate);
                if (totalSamples <= 0 || cancellation.IsCancellationRequested) {
                    return result;
                }
                result.samples = Preferences.Default.PlaySineWhenNoSinger
                    ? Synthesize(phrase, result, totalSamples)
                    : new float[totalSamples];
                progress.Complete(phrase.phones.Length, progressInfo);
                Renderers.ApplyDynamics(phrase, result);
                DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
                return result;
            });
        }

        internal float[] Synthesize(RenderPhrase phrase, RenderResult result, int totalSamples) {
            var samples = new float[totalSamples];
            double startMs = result.positionMs - result.leadingMs;
            int pitchStartTick = phrase.position - phrase.leading;
            int frameCount = totalSamples / FrameSize + 2;
            var freq = new double[frameCount];
            for (int i = 0; i < frameCount; ++i) {
                double posMs = startMs + (double)i * FrameSize / SampleRate * 1000.0;
                int ticks = phrase.timeAxis.MsPosToTickPos(posMs) - pitchStartTick;
                int index = Math.Clamp(ticks / PitchInterval, 0, phrase.pitches.Length - 1);
                freq[i] = MusicMath.ToneToFreq(phrase.pitches[index] * 0.01);
            }
            double fadeSamples = Math.Max(1.0, FadeMs / 1000.0 * SampleRate);
            double phase = 0;
            for (int i = 0; i < totalSamples; ++i) {
                double t = (double)i / FrameSize;
                int k = Math.Min((int)t, frameCount - 2);
                double f = freq[k] + (freq[k + 1] - freq[k]) * (t - k);
                phase += 2 * Math.PI * f / SampleRate;
                double fade = Math.Clamp(Math.Min(i / fadeSamples, (totalSamples - i) / fadeSamples), 0.0, 1.0);
                samples[i] = (float)(Math.Sin(phase) * Amplitude * fade);
            }
            return samples;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            return null;
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            return new UExpressionDescriptor[] { };
        }

        public override string ToString() => "SINE";
    }
}
