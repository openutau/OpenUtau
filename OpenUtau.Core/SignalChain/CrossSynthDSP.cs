using System;
using System.Linq;
using NWaves.Transforms;

namespace OpenUtau.Core.SignalChain {
    public class CrossSynthDSP {

        const int   SAMPLE_RATE = 44100;
        const float EPS         = 1e-8f;
        const bool  NWAVES_INVERSE_NORMALIZES = false;

        public static float[] StftBlend(
            float[]  a, float[]  b,
            float[]  frameRatios,
            double[] anchorA = null,
            double[] anchorB = null) {

            if (a == null && b != null) return (float[])b.Clone();
            if (b == null && a != null) return (float[])a.Clone();
            if (a == null && b == null) return new float[0];
            int length = Math.Max(a.Length, b.Length);
            if (a.Length < length) Array.Resize(ref a, length);
            if (b.Length < length) Array.Resize(ref b, length);
            if (frameRatios == null || frameRatios.All(r => r < 0.01f)) return (float[])a.Clone();
            if (frameRatios.All(r => r > 0.99f)) return (float[])b.Clone();

            const int fftSize = 2048;
            const int hopSize = 512;
            const int half    = fftSize / 2 + 1;
            float invN = NWAVES_INVERSE_NORMALIZES ? 1f : 1f / fftSize;
            var fft  = new Fft(fftSize);
            var win  = MakeHann(fftSize);
            float[] omega = new float[half];
            for (int k = 0; k < half; k++)
                omega[k] = 2f * MathF.PI * k / fftSize;

            float[] phsAccA  = new float[half];
            float[] phsAccB  = new float[half];
            float[] prevPhsA = new float[half];
            float[] prevPhsB = new float[half];
            bool firstFrame  = true;
            float prevEnergyA = 0f;
            float prevEnergyB = 0f;
            const float TRANSIENT_THRESHOLD = 4.0f;

            float[] output = new float[length];
            float[] wsum   = new float[length];

            float[] reA = new float[fftSize];
            float[] imA = new float[fftSize];
            float[] reB = new float[fftSize];
            float[] imB = new float[fftSize];

            bool hasAnchors  = anchorA != null && anchorB != null && anchorA.Length >= 2;
            int totalFrames  = (length - fftSize) / hopSize + 1;

            for (int f = 0; f < totalFrames; f++) {
                int posA = f * hopSize;

                int posB = posA;
                if (hasAnchors) {
                    double tA_ms = posA * 1000.0 / SAMPLE_RATE;
                    double tB_ms;
                    if      (tA_ms <= anchorA[0])                  tB_ms = anchorB[0];
                    else if (tA_ms >= anchorA[anchorA.Length - 1]) tB_ms = anchorB[anchorB.Length - 1];
                    else                                           tB_ms = PiecewiseLerp(anchorA, anchorB, tA_ms);
                    posB = (int)Math.Max(0, Math.Min(length - fftSize, tB_ms * SAMPLE_RATE / 1000.0));
                }

                float ratio = frameRatios != null && f < frameRatios.Length
                    ? frameRatios[f]
                    : (frameRatios?.Length > 0 ? frameRatios[frameRatios.Length - 1] : 0f);

                for (int i = 0; i < fftSize; i++) { reA[i] = a[posA + i] * win[i]; imA[i] = 0f; }
                fft.Direct(reA, imA);

                for (int i = 0; i < fftSize; i++) { reB[i] = b[posB + i] * win[i]; imB[i] = 0f; }
                fft.Direct(reB, imB);

                float energyA = 0f, energyB = 0f;
                for (int k = 0; k < half; k++) {
                    energyA += reA[k]*reA[k] + imA[k]*imA[k];
                    energyB += reB[k]*reB[k] + imB[k]*imB[k];
                }
                bool silenceToSignalA = !firstFrame && prevEnergyA <= EPS && energyA > EPS;
                bool transientA = !firstFrame && prevEnergyA > EPS && energyA / prevEnergyA > TRANSIENT_THRESHOLD;
                bool silenceToSignalB = !firstFrame && prevEnergyB <= EPS && energyB > EPS;
                bool transientB = !firstFrame && prevEnergyB > EPS && energyB / prevEnergyB > TRANSIENT_THRESHOLD;
                bool resetA = firstFrame || silenceToSignalA || transientA;
                bool resetB = firstFrame || silenceToSignalB || transientB;
                prevEnergyA = energyA;
                prevEnergyB = energyB;
                const float BYPASS_RATIO = 0.005f;
                bool bypass = ratio < BYPASS_RATIO;

                for (int k = 0; k < half; k++) {
                    float magA = MathF.Sqrt(reA[k]*reA[k] + imA[k]*imA[k]);
                    float magB = MathF.Sqrt(reB[k]*reB[k] + imB[k]*imB[k]);

                    float rawPhsA = MathF.Atan2(imA[k], reA[k]);
                    float rawPhsB = MathF.Atan2(imB[k], reB[k]);

                    if (resetA) {
                        phsAccA[k] = rawPhsA;
                    } else {
                        float dpA = WrapPi(rawPhsA - prevPhsA[k] - omega[k] * hopSize);
                        phsAccA[k] += omega[k] * hopSize + dpA;
                    }
                    if (resetB) {
                        phsAccB[k] = rawPhsB;
                    } else {
                        float dpB = WrapPi(rawPhsB - prevPhsB[k] - omega[k] * hopSize);
                        phsAccB[k] += omega[k] * hopSize + dpB;
                    }

                    prevPhsA[k] = rawPhsA;
                    prevPhsB[k] = rawPhsB;

                    if (bypass) continue;
                    const float SILENCE_GATE_FLOOR = 0.001f;
                    float silenceGateA = MathF.Min(1f, magA / SILENCE_GATE_FLOOR);
                    float linBlend = magA * (1f - ratio) + magB * ratio;
                    float geoBlend = MathF.Exp(
                        MathF.Log(magA + EPS) * (1f - ratio) +
                        MathF.Log(magB + EPS) * ratio);
                    float minMag = MathF.Min(magA, magB);
                    float silenceWeight = MathF.Min(1f, minMag / 0.01f);
                    float magOut = (linBlend + silenceWeight * (geoBlend - linBlend)) * silenceGateA;
                    float phsOut = phsAccA[k];
                    reA[k] = magOut * MathF.Cos(phsOut);
                    imA[k] = (k == 0 || k == half - 1) ? 0f : magOut * MathF.Sin(phsOut);

                    if (k > 0 && k < fftSize - k) {
                        reA[fftSize - k] =  reA[k];
                        imA[fftSize - k] = -imA[k];
                    }
                }

                firstFrame = false;
                if (bypass) {
                    for (int i = 0; i < fftSize; i++) {
                        float w = win[i];
                        output[posA + i] += a[posA + i] * w * w;
                        wsum  [posA + i] += w * w;
                    }
                } else {
                    fft.Inverse(reA, imA);
                    for (int i = 0; i < fftSize; i++) {
                        float w = win[i];
                        output[posA + i] += reA[i] * invN * w * w;
                        wsum  [posA + i] += w * w;
                    }
                }

            }
            for (int i = 0; i < length; i++) {
                output[i] = wsum[i] > EPS ? output[i] / wsum[i] : 0f;
            }

            return output;
        }

        static float WrapPi(float x) {
            x = (x + MathF.PI) % (2f * MathF.PI);
            if (x < 0f) x += 2f * MathF.PI;
            return x - MathF.PI;
        }

        static float[] MakeHann(int size) {
            var w = new float[size];
            for (int i = 0; i < size; i++) {
                w[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / size)));
            }
            return w;
        }

        static double PiecewiseLerp(double[] xs, double[] ys, double x) {
            for (int i = 0; i < xs.Length - 1; i++) {
                double lo = xs[i], hi = xs[i + 1];
                if (x >= lo && x <= hi) {
                    double span = hi - lo;
                    if (span < 1e-9) return ys[i];
                    return ys[i] + (x - lo) / span * (ys[i + 1] - ys[i]);
                }
            }
            return ys[ys.Length - 1];
        }
    }
}
