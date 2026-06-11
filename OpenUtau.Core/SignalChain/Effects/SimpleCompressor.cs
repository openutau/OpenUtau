using System;

namespace OpenUtau.Core.SignalChain.Effects {
    /// <summary>
    /// Soft-knee feed-forward compressor / limiter.
    /// Detector: stereo-linked peak.
    /// Envelope follower: classic one-pole, attack/release coefficients
    /// = exp(-1 / (timeMs * 0.001 * sr)).
    /// Static curve: piecewise with quadratic knee (DAFX, Zoelzer ch. 4).
    ///
    /// Reference code: airwindows Pressure, Chowdhury-DSP CHOWComp -- but those
    /// have many bells and whistles we don't need.  This is the smallest
    /// implementation that still sounds OK on vocals.
    /// </summary>
    public class SimpleCompressor : IEffect {
        private readonly int channels;
        private readonly int sampleRate;
        private double atkCoef = 0.9;
        private double relCoef = 0.99;
        private double thresholdDb = -18;
        private double ratio = 2.0;
        private double makeupLinear = 1.0;
        private double kneeDb = 6.0;
        private double envDb;        // current gain reduction in dB (≤ 0)
        private bool bypassed = true;

        public bool IsBypassed => bypassed;

        public SimpleCompressor(int sampleRate = 44100, int channels = 2) {
            this.sampleRate = sampleRate;
            this.channels = channels;
            envDb = 0;
        }

        public void Configure(double thresholdDb, double ratio, double attackMs, double releaseMs, double makeupDb, double kneeDb = 6.0) {
            // Off when ratio ≤ 1 (no compression) and no makeup gain.
            bypassed = ratio <= 1.0001 && Math.Abs(makeupDb) < 0.01;
            this.thresholdDb = thresholdDb;
            this.ratio = Math.Max(1.0, ratio);
            this.atkCoef = Math.Exp(-1.0 / (Math.Max(0.05, attackMs)  * 0.001 * sampleRate));
            this.relCoef = Math.Exp(-1.0 / (Math.Max(0.05, releaseMs) * 0.001 * sampleRate));
            this.makeupLinear = Math.Pow(10.0, makeupDb / 20.0);
            this.kneeDb = kneeDb;
        }

        public void Reset() {
            envDb = 0;
        }

        public void Process(float[] buffer, int offset, int count) {
            if (bypassed) {
                return;
            }
            int frames = count / channels;
            double T = thresholdDb;
            double W = kneeDb;
            double slope = (1.0 / ratio) - 1.0;
            double atk = atkCoef;
            double rel = relCoef;
            double env = envDb;
            float makeup = (float)makeupLinear;

            for (int i = 0; i < frames; i++) {
                // Peak detector across channels
                float peak = 0f;
                int baseIdx = offset + i * channels;
                for (int c = 0; c < channels; c++) {
                    float v = Math.Abs(buffer[baseIdx + c]);
                    if (v > peak) {
                        peak = v;
                    }
                }
                double det = peak + 1e-12;
                double detDb = 20.0 * Math.Log10(det);

                // Static curve: piecewise quadratic soft knee
                double above = detDb - T;
                double gainDb;
                double kneeLo = -W / 2.0;
                double kneeHi =  W / 2.0;
                if (above <= kneeLo) {
                    gainDb = 0;
                } else if (above >= kneeHi) {
                    gainDb = slope * above;
                } else {
                    double k = above + W / 2.0;
                    gainDb = slope * (k * k) / (2.0 * W);
                }

                // Smooth in log domain (attack when GR has to increase, i.e. gainDb more negative)
                double coef = gainDb < env ? atk : rel;
                env = coef * env + (1.0 - coef) * gainDb;

                float lin = (float)Math.Pow(10.0, env / 20.0) * makeup;
                for (int c = 0; c < channels; c++) {
                    buffer[baseIdx + c] *= lin;
                }
            }
            envDb = env;
        }
    }
}
