// Ported from hifisampler's backend/resampler.py (https://github.com/openhachimi/hifisampler),
// licensed under the Apache License 2.0; see LICENSE in this folder.
// Modified for OpenUtau: translated to C#, reading OpenUtau's resampler arguments.

using System;
using System.Linq;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>
    /// Breathiness, voicing, tension and gender per analysis frame of the source (at
    /// <see cref="HifiSamplerConfig.OriginHopSize"/>), in OpenUtau's curve units: breathiness
    /// -100..100 (noise gain 0..1..3, as Worldline-R), voicing 0..100 (harmonic gain v / 100), tension
    /// -100..100, gender -100..100 (formants down 12 cents per unit, as Worldline-R). They take
    /// the place of hifisampler's Hb / Hv / Ht / g flags.
    /// </summary>
    internal sealed class HifiSourceCurves {
        public readonly double[] Breathiness;
        public readonly double[] Voicing;
        public readonly double[] Tension;
        public readonly double[] Gender;
        /// <summary>The note's pitch (MIDI) at each source frame, where tension places its gains.</summary>
        public readonly double[] TargetPitch;
        readonly int hop;

        public HifiSourceCurves(double[] breathiness, double[] voicing, double[] tension, double[] gender,
                double[] targetPitch, int hop) {
            Breathiness = breathiness;
            Voicing = voicing;
            Tension = tension;
            Gender = gender;
            TargetPitch = targetPitch;
            this.hop = hop;
        }

        public static HifiSourceCurves Constant(int frames, double breathiness, double voicing, double tension, int hop,
                double gender = 0, double targetPitch = 60) {
            return new HifiSourceCurves(Enumerable.Repeat(breathiness, frames).ToArray(),
                Enumerable.Repeat(voicing, frames).ToArray(), Enumerable.Repeat(tension, frames).ToArray(),
                Enumerable.Repeat(gender, frames).ToArray(), Enumerable.Repeat(targetPitch, frames).ToArray(), hop);
        }

        public bool HasTension => Tension.Any(t => t != 0);

        /// <summary>Whether the harmonic / noise split is needed, as _needs_hnsep_separation, at any frame.</summary>
        public bool NeedsSeparation => HasTension || Breathiness.Zip(Voicing).Any(p => NoiseGain(p.First) != p.Second / 100);

        /// <summary>Worldline-R's breathiness gain: 1 + b / 100 below 0, 1 + 2 b / 100 above.</summary>
        public static double NoiseGain(double breathiness) {
            double b = Math.Clamp(breathiness, -100, 100);
            return b > 0 ? 1 + 0.02 * b : 1 + 0.01 * b;
        }

        public bool IsDefault => Breathiness.All(b => b == 0) && Voicing.All(v => v == 100) && !HasTension;

        /// <summary>A curve at a sample position, linear between frame centers (sample (m + 0.5) hop).</summary>
        public double At(double[] curve, double sample) {
            if (curve.Length == 0) {
                return 0;
            }
            double pos = Math.Clamp(sample / hop - 0.5, 0, curve.Length - 1);
            int i = (int)pos;
            if (i >= curve.Length - 1) {
                return curve[^1];
            }
            double a = pos - i;
            return curve[i] + (curve[i + 1] - curve[i]) * a;
        }
    }

    /// <summary>
    /// Resampler.generate_features: breath / voicing / tension on the source, peak-scaled to
    /// at most 0.5, then the log mel at the analysis hop. With curves the gains vary along the
    /// source and the tension tilt per STFT frame; constant curves are hifisampler's flags.
    /// </summary>
    internal sealed class HifiFeatures {
        /// <summary>Log mel, [frame][mel], at <see cref="HifiSamplerConfig.OriginHopSize"/>.</summary>
        public readonly float[][] Mel;
        /// <summary>The gain applied before analysis, undone on the rendered audio.</summary>
        public readonly double Scale;

        public HifiFeatures(float[][] mel, double scale) {
            Mel = mel;
            Scale = scale;
        }

        /// <param name="harmonic">The harmonic part of a signal (hnsep), only called when needed.</param>
        /// <param name="sourceF0">
        /// The source's f0 (Hz, 0 unvoiced) on frames of <see cref="HifiRdTension.Hop"/>, only
        /// called for tension.
        /// </param>
        public static HifiFeatures Generate(float[] wave, HifiSourceCurves curves,
                HifiSamplerConfig config, Func<float[], float[]> harmonic, Func<double[]> sourceF0) {
            var x = (float[])wave.Clone();
            if (curves.NeedsSeparation) {
                var h = harmonic(wave);
                var voiced = new float[h.Length];
                for (int i = 0; i < h.Length; i++) {
                    double voicing = Math.Clamp(curves.At(curves.Voicing, i), 0, 150) / 100.0;
                    voiced[i] = (float)(voicing * h[i]);
                }
                if (curves.HasTension) {
                    voiced = HifiRdTension.Apply(voiced, sourceF0(), config.SampleRate,
                        s => Math.Clamp(curves.At(curves.Tension, s), -100, 100),
                        s => 440 * Math.Pow(2, (curves.At(curves.TargetPitch, s) - 69) / 12));
                }
                for (int i = 0; i < x.Length; i++) {
                    double breath = HifiSourceCurves.NoiseGain(curves.At(curves.Breathiness, i));
                    x[i] = (float)(breath * (wave[i] - h[i]) + voiced[i]);
                }
            } else if (!curves.IsDefault) {
                // Equal gains: scale the whole signal instead of separating.
                for (int i = 0; i < x.Length; i++) {
                    double gain = HifiSourceCurves.NoiseGain(curves.At(curves.Breathiness, i));
                    x[i] = (float)(x[i] * gain);
                }
            }

            double scale = 1.0;
            float waveMax = (float)HifiArray.MaxAbs(x);
            if (waveMax >= 0.5f) {
                float s = 0.5f / waveMax;
                for (int i = 0; i < x.Length; i++) {
                    x[i] *= s;
                }
                scale = s;
            }

            var analyzer = new HifiMelSpectrogram(config.SampleRate, config.NFft, config.WinSize,
                config.OriginHopSize, config.MelFmin, config.MelFmax, config.NMels);
            var mel = analyzer.Compute(x, curves.Gender.Select(g => -0.12 * Math.Clamp(g, -100, 100)).ToArray());
            HifiMelSpectrogram.LogCompress(mel);
            return new HifiFeatures(mel, scale);
        }
    }
}
