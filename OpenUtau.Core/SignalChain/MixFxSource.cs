using System;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.SignalChain {
    /// <summary>
    /// ISignalSource wrapper that applies the user-configured post-FX chain
    /// (3-band EQ -> compressor -> reverb).
    ///
    /// Parameters are pulled from a <see cref="UMixFx"/> getter at the start
    /// of every audio block, so edits made while playing (knobs, module power
    /// switches, the track's master switch) are heard within one block.
    /// Switching the master or a module on/off crossfades over
    /// <see cref="FadeFrames"/> instead of cutting, to avoid clicks.
    ///
    /// The wrapper is stateful (filter state, envelope follower, reverb
    /// buffers) and must be constructed fresh per playback session.
    /// </summary>
    public class MixFxSource : ISignalSource {
        public const int SampleRate = 44100;
        public const int Channels = 2;
        // Q of the movable EQ mid band.  Presets don't override it.
        public const double EqMidQ = 0.707;
        // ~15 ms at 44.1 kHz.
        private const int FadeFrames = 661;
        private const float FadeStep = 1f / FadeFrames;

        private readonly ISignalSource source;
        private readonly Func<UMixFx?> getFx;
        private readonly BiquadEQ eq = new BiquadEQ(SampleRate, Channels);
        private readonly SimpleCompressor comp = new SimpleCompressor(SampleRate, Channels);
        private readonly Freeverb reverb = new Freeverb(SampleRate, Channels);

        // Last parameters pushed into the effects.  Compared field by field
        // on the audio thread so a change costs no allocation.
        private readonly UMixFx applied = new UMixFx();
        private bool configured;

        // Current crossfade positions, 0 = dry, 1 = processed.
        private float masterGain;
        private float eqGain;
        private float compGain;
        private float reverbGain;

        // Scratch buffers.  The signal chain in MasterAdapter passes in a
        // zeroed buffer and we mix into it; we need a private writeable copy
        // because the inner source uses additive mixing.
        private float[] scratch = Array.Empty<float>();
        private float[] masterDry = Array.Empty<float>();
        private float[] stageDry = Array.Empty<float>();

        private MixFxSource(ISignalSource source, Func<UMixFx?> getFx) {
            this.source = source;
            this.getFx = getFx;
            // Start at the current state so playback doesn't fade in.
            var fx = getFx();
            if (fx != null) {
                Sync(fx);
                masterGain = fx.Enabled ? 1f : 0f;
                eqGain = fx.EqEnabled ? 1f : 0f;
                compGain = fx.CompEnabled ? 1f : 0f;
                reverbGain = fx.ReverbEnabled ? 1f : 0f;
            }
        }

        public bool IsReady(int position, int count) => source.IsReady(position, count);

        public int Mix(int position, float[] buffer, int index, int count) {
            // Allocate / grow scratch as needed.  In the common case the
            // playback buffer size is constant so this is allocated once.
            if (scratch.Length < count) {
                scratch = new float[count];
                masterDry = new float[count];
                stageDry = new float[count];
            }
            Array.Clear(scratch, 0, count);
            int ret = source.Mix(position, scratch, 0, count);

            var fx = getFx();
            if (fx != null) {
                Sync(fx);
            }
            float masterTarget = fx != null && fx.Enabled ? 1f : 0f;
            if (configured && (masterGain > 0f || masterTarget > 0f)) {
                bool masterSteady = masterGain == 1f && masterTarget == 1f;
                if (!masterSteady) {
                    Array.Copy(scratch, masterDry, count);
                }
                RunStage(eq, ref eqGain, applied.EqEnabled, count);
                RunStage(comp, ref compGain, applied.CompEnabled, count);
                RunStage(reverb, ref reverbGain, applied.ReverbEnabled, count);
                if (!masterSteady) {
                    Crossfade(masterDry, scratch, ref masterGain, masterTarget, count);
                    if (masterGain == 0f) {
                        eq.Reset();
                        comp.Reset();
                        reverb.Reset();
                    }
                }
            }

            // Additive mix into output (matches Fader / WaveMix convention).
            for (int i = 0; i < count; i++) {
                buffer[index + i] += scratch[i];
            }
            return ret;
        }

        private void RunStage(IEffect effect, ref float gain, bool enabled, int count) {
            float target = enabled ? 1f : 0f;
            if (gain == 0f && target == 0f) {
                return;
            }
            if (gain == 1f && target == 1f) {
                effect.Process(scratch, 0, count);
                return;
            }
            Array.Copy(scratch, stageDry, count);
            effect.Process(scratch, 0, count);
            Crossfade(stageDry, scratch, ref gain, target, count);
            if (gain == 0f) {
                // Drop stale state so switching back on doesn't replay it.
                effect.Reset();
            }
        }

        /// <summary>wet[i] = dry[i] + (wet[i] - dry[i]) * g, with g ramping toward target.</summary>
        private static void Crossfade(float[] dry, float[] wet, ref float gain, float target, int count) {
            float g = gain;
            for (int i = 0; i + 1 < count; i += Channels) {
                if (g < target) {
                    g = Math.Min(target, g + FadeStep);
                } else if (g > target) {
                    g = Math.Max(target, g - FadeStep);
                }
                for (int c = 0; c < Channels; c++) {
                    float d = dry[i + c];
                    wet[i + c] = d + (wet[i + c] - d) * g;
                }
            }
            gain = g;
        }

        /// <summary>Reconfigure the effects if <paramref name="fx"/> differs from what was last applied.</summary>
        private void Sync(UMixFx fx) {
            if (configured && SameParams(fx, applied)) {
                return;
            }
            CopyParams(fx, applied);
            configured = true;

            eq.Configure(applied.EqLowDb, applied.EqMidFreq, EqMidQ, applied.EqMidDb, applied.EqHighDb);

            FxPresets.CompParams cParams = FxPresets.Comp.TryGetValue(applied.CompPreset ?? FxPresets.Off, out var cp)
                ? cp
                : FxPresets.Comp[FxPresets.Off];
            comp.Configure(applied.CompThresholdDb, applied.CompRatio,
                           cParams.AttackMs, cParams.ReleaseMs, applied.CompMakeupDb);

            FxPresets.ReverbParams rParams = FxPresets.Reverb.TryGetValue(applied.ReverbPreset ?? FxPresets.Off, out var rp)
                ? rp
                : FxPresets.Reverb[FxPresets.Off];
            double userWet = Math.Clamp(applied.ReverbWet, 0.0, 2.0);
            reverb.Configure(applied.ReverbSize, applied.ReverbDamp, rParams.Width,
                             rParams.Wet * userWet, rParams.Dry, applied.ReverbPreDelayMs);
        }

        // Everything the DSP depends on except the master Enabled switch,
        // which is handled by the crossfade.
        private static bool SameParams(UMixFx a, UMixFx b) {
            return a.EqEnabled == b.EqEnabled && a.CompEnabled == b.CompEnabled && a.ReverbEnabled == b.ReverbEnabled
                && a.EqLowDb == b.EqLowDb && a.EqMidFreq == b.EqMidFreq && a.EqMidDb == b.EqMidDb && a.EqHighDb == b.EqHighDb
                && a.CompPreset == b.CompPreset && a.CompThresholdDb == b.CompThresholdDb
                && a.CompRatio == b.CompRatio && a.CompMakeupDb == b.CompMakeupDb
                && a.ReverbPreset == b.ReverbPreset && a.ReverbSize == b.ReverbSize && a.ReverbDamp == b.ReverbDamp
                && a.ReverbWet == b.ReverbWet && a.ReverbPreDelayMs == b.ReverbPreDelayMs;
        }

        private static void CopyParams(UMixFx src, UMixFx dst) {
            dst.EqEnabled = src.EqEnabled;
            dst.CompEnabled = src.CompEnabled;
            dst.ReverbEnabled = src.ReverbEnabled;
            dst.EqLowDb = src.EqLowDb;
            dst.EqMidFreq = src.EqMidFreq;
            dst.EqMidDb = src.EqMidDb;
            dst.EqHighDb = src.EqHighDb;
            dst.CompPreset = src.CompPreset;
            dst.CompThresholdDb = src.CompThresholdDb;
            dst.CompRatio = src.CompRatio;
            dst.CompMakeupDb = src.CompMakeupDb;
            dst.ReverbPreset = src.ReverbPreset;
            dst.ReverbSize = src.ReverbSize;
            dst.ReverbDamp = src.ReverbDamp;
            dst.ReverbWet = src.ReverbWet;
            dst.ReverbPreDelayMs = src.ReverbPreDelayMs;
        }

        /// <summary>True iff at least one enabled effect would change the signal.</summary>
        private bool IsAnythingEnabled =>
            (applied.EqEnabled && !eq.IsBypassed)
            || (applied.CompEnabled && !comp.IsBypassed)
            || (applied.ReverbEnabled && !reverb.IsBypassed);

        /// <summary>
        /// Fixed-parameter wrapper for offline export.  Returns the inner
        /// source unchanged when the track has no FX configured, has
        /// Enabled = false, or every module would pass the signal through.
        /// </summary>
        public static ISignalSource WrapWith(ISignalSource inner, UMixFx? fx) {
            if (fx == null || !fx.Enabled) {
                return inner;
            }
            var snapshot = fx.Clone();
            var wrapper = new MixFxSource(inner, () => snapshot);
            return wrapper.IsAnythingEnabled ? wrapper : inner;
        }

        /// <summary>
        /// Live wrapper for playback.  Always wraps, and follows
        /// <paramref name="track"/>'s current <see cref="UTrack.MixFx"/>
        /// every block so edits apply while playing.
        /// </summary>
        public static ISignalSource WrapLive(ISignalSource inner, UTrack track) {
            return new MixFxSource(inner, () => track.MixFx);
        }
    }
}
