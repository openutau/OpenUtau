using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NWaves.Transforms;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.SignalChain {
    public class CrossSynthDSP {
        const int SAMPLE_RATE = 44100;
        const float EPS = 1e-8f;
        const int FFT_SIZE = 2048;
        const int HOP_SIZE = 512;
        const int HALF = FFT_SIZE / 2 + 1;
        const float BYPASS_RATIO = 0.005f;

        public static float[] MorphN(
            float[] baseAudio,
            List<float[]> colorAudios,
            List<float[]> colorCurves,
            List<double[]> trackAnchors = null,
            double[] baseAnchors = null) {

            if (baseAudio == null || baseAudio.Length == 0) return Array.Empty<float>();
            if (colorAudios == null || colorAudios.Count == 0 || colorCurves == null || colorCurves.Count == 0) {
                return (float[])baseAudio.Clone();
            }

            int numColors = Math.Min(colorAudios.Count, colorCurves.Count);

            bool anyActive = false;
            for (int c = 0; c < numColors; c++) {
                if (colorCurves[c] != null && colorCurves[c].Any(w => w > 0.001f)) {
                    anyActive = true;
                    break;
                }
            }
            if (!anyActive) {
                return (float[])baseAudio.Clone();
            }

            int maxLen = baseAudio.Length;
            for (int c = 0; c < numColors; c++) {
                if (colorAudios[c] != null && colorAudios[c].Length > maxLen) {
                    maxLen = colorAudios[c].Length;
                }
            }

            float[] baseBuf = PadBuffer(baseAudio, maxLen);
            List<float[]> colorBufs = new List<float[]>();
            for (int c = 0; c < numColors; c++) {
                colorBufs.Add(PadBuffer(colorAudios[c] ?? baseAudio, maxLen));
            }

            var fft = new Fft(FFT_SIZE);
            float[] win = MakeHann(FFT_SIZE);
            float invN = 1.0f / FFT_SIZE;

            float[] output = new float[maxLen];
            float[] wsum = new float[maxLen];

            int totalFrames = (maxLen - FFT_SIZE) / HOP_SIZE + 1;
            if (totalFrames <= 0) return (float[])baseAudio.Clone();

            for (int f = 0; f < totalFrames; f++) {
                int posBase = f * HOP_SIZE;

                double sumWeights = 0;
                double[] weights = new double[numColors];
                for (int c = 0; c < numColors; c++) {
                    float r = (colorCurves[c] != null && f < colorCurves[c].Length)
                        ? colorCurves[c][f]
                        : ((colorCurves[c] != null && colorCurves[c].Length > 0) ? colorCurves[c][^1] : 0f);
                    weights[c] = Math.Clamp(r / 100.0, 0.0, 1.0);
                    sumWeights += weights[c];
                }

                // Bypass unmodulated segments: Exact OLA reconstruction
                if (sumWeights < BYPASS_RATIO) {
                    for (int i = 0; i < FFT_SIZE; i++) {
                        float w = win[i];
                        output[posBase + i] += baseBuf[posBase + i] * w * w;
                        wsum[posBase + i] += w * w;
                    }
                    continue;
                }

                if (sumWeights > 1.0) {
                    for (int c = 0; c < numColors; c++) weights[c] /= sumWeights;
                    sumWeights = 1.0;
                }
                double baseWeight = 1.0 - sumWeights;

                float[] reBase = new float[FFT_SIZE];
                float[] imBase = new float[FFT_SIZE];
                for (int i = 0; i < FFT_SIZE; i++) reBase[i] = baseBuf[posBase + i] * win[i];
                fft.Direct(reBase, imBase);

                List<float[]> reColors = new List<float[]>();
                List<float[]> imColors = new List<float[]>();

                for (int c = 0; c < numColors; c++) {
                    int posColor = posBase;
                    if (trackAnchors != null && baseAnchors != null && c < trackAnchors.Count && trackAnchors[c] != null) {
                        posColor = ResolveAnchoredPosition(posBase, maxLen, baseAnchors, trackAnchors[c]);
                    }

                    float[] reC = new float[FFT_SIZE];
                    float[] imC = new float[FFT_SIZE];
                    for (int i = 0; i < FFT_SIZE; i++) reC[i] = colorBufs[c][posColor + i] * win[i];
                    fft.Direct(reC, imC);

                    reColors.Add(reC);
                    imColors.Add(imC);
                }

                float[] reOut = new float[FFT_SIZE];
                float[] imOut = new float[FFT_SIZE];

                for (int k = 0; k < HALF; k++) {
                    double magBase = Math.Sqrt(reBase[k] * reBase[k] + imBase[k] * imBase[k]);

                    // Equal-power / Geometric magnitude blend prevents volume inflation
                    double linMag = baseWeight * magBase;
                    double logMag = baseWeight * Math.Log(magBase + EPS);
                    double minMag = magBase;

                    for (int c = 0; c < numColors; c++) {
                        double magC = Math.Sqrt(reColors[c][k] * reColors[c][k] + imColors[c][k] * imColors[c][k]);
                        linMag += weights[c] * magC;
                        logMag += weights[c] * Math.Log(magC + EPS);
                        if (weights[c] > 0.01) {
                            minMag = Math.Min(minMag, magC);
                        }
                    }

                    double geoMag = Math.Exp(logMag);
                    double silenceWeight = Math.Clamp(minMag / 0.01, 0.0, 1.0);
                    double magOut = linMag + silenceWeight * (geoMag - linMag);
                    double silenceGate = Math.Clamp(magBase / 0.001, 0.0, 1.0);
                    magOut *= silenceGate;

                    // Phase locked to base voice
                    bool usePhaseLocked = Preferences.Default.PhaseLocked;

                    Complex targetComplex = new Complex(reBase[k], imBase[k]) * baseWeight;
                    for (int c = 0; c < numColors; c++) {
                        targetComplex += new Complex(reColors[c][k], imColors[c][k]) * weights[c];
                    }

                    double phsBase = Math.Atan2(imBase[k], reBase[k]);
                    Complex blended;

                    if (usePhaseLocked) {
                        // Coherence measurement: checks if phase cancellation is happening
                        double compMag = targetComplex.Magnitude;
                        double coherence = (magOut > 1e-8) ? Math.Clamp(compMag / magOut, 0.0, 1.0) : 1.0;
                        
                        // Smooth S-curve transition between locked base phase and complex phase
                        double blendFactor = coherence * coherence * (3.0 - 2.0 * coherence);
                        Complex pureLocked = Complex.FromPolarCoordinates(magOut, phsBase);
                        Complex freeComplex = Complex.FromPolarCoordinates(magOut, targetComplex.Phase);

                        blended = (pureLocked * (1.0 - blendFactor)) + (freeComplex * blendFactor);
                    } else {
                        // Unlocked: follows the natural vector phase sum
                        blended = Complex.FromPolarCoordinates(magOut, targetComplex.Phase);
                    }

                    reOut[k] = (float)blended.Real;
                    imOut[k] = (k == 0 || k == HALF - 1) ? 0f : (float)blended.Imaginary;

                    if (k > 0 && k < FFT_SIZE - k) {
                        reOut[FFT_SIZE - k] = reOut[k];
                        imOut[FFT_SIZE - k] = -imOut[k];
                    }
                }

                fft.Inverse(reOut, imOut);
                for (int i = 0; i < FFT_SIZE; i++) {
                    float w = win[i];
                    output[posBase + i] += reOut[i] * invN * w;
                    wsum[posBase + i] += w * w;
                }
            }

            for (int i = 0; i < maxLen; i++) {
                output[i] = wsum[i] > EPS ? output[i] / wsum[i] : 0f;
            }

            return output;
        }

        private static int ResolveAnchoredPosition(int posBase, int maxLen, double[] anchorBase, double[] anchorTarget) {
            double tMs = posBase * 1000.0 / SAMPLE_RATE;
            double targetMs;
            if (tMs <= anchorBase[0]) targetMs = anchorTarget[0];
            else if (tMs >= anchorBase[^1]) targetMs = anchorTarget[^1];
            else {
                int idx = 0;
                while (idx < anchorBase.Length - 1 && anchorBase[idx + 1] < tMs) idx++;
                double span = anchorBase[idx + 1] - anchorBase[idx];
                double factor = span < 1e-9 ? 0 : (tMs - anchorBase[idx]) / span;
                targetMs = anchorTarget[idx] + factor * (anchorTarget[idx + 1] - anchorTarget[idx]);
            }
            return (int)Math.Clamp(targetMs * SAMPLE_RATE / 1000.0, 0, maxLen - FFT_SIZE);
        }

        private static float[] PadBuffer(float[] src, int length) {
            if (src.Length == length) return src;
            float[] b = new float[length];
            Array.Copy(src, b, Math.Min(src.Length, length));
            return b;
        }

        private static float[] MakeHann(int size) {
            var w = new float[size];
            for (int i = 0; i < size; i++) {
                w[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / size)));
            }
            return w;
        }
    }
}