using System;

namespace OpenUtau.Core.SignalChain.Effects {
    /// <summary>
    /// Three-band biquad equaliser implementing Robert Bristow-Johnson's
    /// "Audio EQ Cookbook" formulae:
    ///   • low-shelf at 200 Hz
    ///   • peaking band (movable centre frequency, Q, gain)
    ///   • high-shelf at 8 kHz
    ///
    /// Designed for 44.1 kHz stereo, but works at any sample rate because the
    /// coefficients are recomputed in <see cref="Configure"/>.
    /// </summary>
    public class BiquadEQ : IEffect {
        private readonly int channels;
        private readonly int sampleRate;
        private readonly Biquad[,] stages; // [stageIndex, channel]
        private bool bypassed = true;

        private const int StageCount = 3;
        private const int StageLowShelf = 0;
        private const int StagePeak = 1;
        private const int StageHighShelf = 2;

        public bool IsBypassed => bypassed;

        public BiquadEQ(int sampleRate = 44100, int channels = 2) {
            this.sampleRate = sampleRate;
            this.channels = channels;
            stages = new Biquad[StageCount, channels];
            for (int s = 0; s < StageCount; s++) {
                for (int c = 0; c < channels; c++) {
                    stages[s, c] = new Biquad();
                }
            }
        }

        /// <summary>Set EQ parameters.  All gains in dB.</summary>
        public void Configure(double lowDb, double midFreq, double midQ, double midDb, double highDb) {
            // If every gain is zero we can short-circuit the whole effect.
            const double Eps = 0.01;
            bypassed = Math.Abs(lowDb) < Eps && Math.Abs(midDb) < Eps && Math.Abs(highDb) < Eps;
            if (bypassed) {
                return;
            }
            for (int c = 0; c < channels; c++) {
                stages[StageLowShelf,  c].SetLowShelf(sampleRate, 200.0, lowDb);
                stages[StagePeak,      c].SetPeak     (sampleRate, midFreq, midQ, midDb);
                stages[StageHighShelf, c].SetHighShelf(sampleRate, 8000.0, highDb);
            }
        }

        public void Reset() {
            for (int s = 0; s < StageCount; s++) {
                for (int c = 0; c < channels; c++) {
                    stages[s, c].Reset();
                }
            }
        }

        public void Process(float[] buffer, int offset, int count) {
            if (bypassed) {
                return;
            }
            int frames = count / channels;
            for (int i = 0; i < frames; i++) {
                for (int c = 0; c < channels; c++) {
                    int idx = offset + i * channels + c;
                    float x = buffer[idx];
                    x = stages[StageLowShelf,  c].Process(x);
                    x = stages[StagePeak,      c].Process(x);
                    x = stages[StageHighShelf, c].Process(x);
                    buffer[idx] = x;
                }
            }
        }

        // --------- single biquad section in Direct Form I -----------
        private class Biquad {
            private float b0 = 1, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
            private float x1, x2, y1, y2;

            public void Reset() {
                x1 = x2 = y1 = y2 = 0;
            }

            public float Process(float x) {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }

            // RBJ Audio EQ Cookbook coefficients ----------------------

            public void SetPeak(double fs, double f0, double Q, double gainDb) {
                double A = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * f0 / fs;
                double cosw = Math.Cos(w0);
                double alpha = Math.Sin(w0) / (2.0 * Q);
                double a0 = 1.0 + alpha / A;
                Apply(
                    1.0 + alpha * A,
                    -2.0 * cosw,
                    1.0 - alpha * A,
                    a0,
                    -2.0 * cosw,
                    1.0 - alpha / A);
            }

            public void SetLowShelf(double fs, double f0, double gainDb) {
                double A = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * f0 / fs;
                double cosw = Math.Cos(w0);
                double sinw = Math.Sin(w0);
                double S = 1.0;
                double alpha = sinw / 2.0 * Math.Sqrt((A + 1.0 / A) * (1.0 / S - 1.0) + 2.0);
                double beta = 2.0 * Math.Sqrt(A) * alpha;
                double a0 = (A + 1) + (A - 1) * cosw + beta;
                Apply(
                    A * ((A + 1) - (A - 1) * cosw + beta),
                    2 * A * ((A - 1) - (A + 1) * cosw),
                    A * ((A + 1) - (A - 1) * cosw - beta),
                    a0,
                    -2 * ((A - 1) + (A + 1) * cosw),
                    (A + 1) + (A - 1) * cosw - beta);
            }

            public void SetHighShelf(double fs, double f0, double gainDb) {
                double A = Math.Pow(10.0, gainDb / 40.0);
                double w0 = 2.0 * Math.PI * f0 / fs;
                double cosw = Math.Cos(w0);
                double sinw = Math.Sin(w0);
                double S = 1.0;
                double alpha = sinw / 2.0 * Math.Sqrt((A + 1.0 / A) * (1.0 / S - 1.0) + 2.0);
                double beta = 2.0 * Math.Sqrt(A) * alpha;
                double a0 = (A + 1) - (A - 1) * cosw + beta;
                Apply(
                    A * ((A + 1) + (A - 1) * cosw + beta),
                    -2 * A * ((A - 1) + (A + 1) * cosw),
                    A * ((A + 1) + (A - 1) * cosw - beta),
                    a0,
                    2 * ((A - 1) - (A + 1) * cosw),
                    (A + 1) - (A - 1) * cosw - beta);
            }

            private void Apply(double bb0, double bb1, double bb2, double aa0, double aa1, double aa2) {
                b0 = (float)(bb0 / aa0);
                b1 = (float)(bb1 / aa0);
                b2 = (float)(bb2 / aa0);
                a1 = (float)(aa1 / aa0);
                a2 = (float)(aa2 / aa0);
            }
        }
    }
}
