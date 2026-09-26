using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using NAudio.Wave;
using NWaves.Filters.Base;
using NWaves.Filters.Fda;
using NWaves.Signals;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;

namespace OpenUtau.Classic {
    class SharpWavtool : IWavtool {
        public const string nameConvergence = "convergence";
        public const string nameSimple = "simple";

        private readonly bool phaseComp;

        public SharpWavtool(bool phaseComp) {
            this.phaseComp = phaseComp;
        }

        class Segment {
            public float[] samples;
            public double posMs;
            public int posSamples;
            public int skipSamples;
            public int correction = 0;
            public IList<Vector2> envelope;
            public int headWindowStart;
            public double headWindowF0;
            public double? headPhase;
            public int tailWindowStart;
            public double tailWindowF0;
            public double? tailPhase;
        }

        public float[] Concatenate(List<ResamplerItem> resamplerItems, string tempPath, CancellationTokenSource cancellation) {
            if (cancellation.IsCancellationRequested) {
                return null;
            }
            var phrase = resamplerItems[0].phrase;
            var segments = new List<Segment>();
            foreach (var item in resamplerItems) {
                var segment = new Segment();
                if(item.phone.direct){
                    using (var waveStream = Wave.OpenFile(item.inputFile)) {
                        float[] samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        int offset = (int)(item.phone.oto.Offset / 1000 * 44100);
                        int cutoff = (int)(item.phone.oto.Cutoff / 1000 * 44100);
                        int length = cutoff >= 0 ? (samples.Length - offset - cutoff) : -cutoff;
                        segment.samples = samples.Skip(offset).Take(length).ToArray();
                    }
                } else {
                    //Serialize the read with the resampler writes (ClassicRenderer), which
                    //use the same per-path lock, to avoid "file is being used by another
                    //process" when two phrases sharing a resample cache render concurrently.
                    lock (Renderers.GetCacheLock(item.outputFile)) {
                        if (!File.Exists(item.outputFile)) {
                            continue;
                        }
                        using (var waveStream = Wave.OpenFile(item.outputFile)) {
                            segment.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                    }
                }
                segments.Add(segment);
                segment.posMs = item.phone.positionMs - item.phone.leadingMs - (phrase.positionMs - phrase.leadingMs);
                segment.posSamples = (int)Math.Round(segment.posMs * 44100 / 1000);
                segment.skipSamples = (int)Math.Round(item.skipOver * 44100 / 1000);
                segment.envelope = item.EnvelopeMsToSamples();

                if (phaseComp) {
                    var headWindow = GetHeadWindow(segment.samples, segment.envelope, out segment.headWindowStart);
                    segment.headWindowF0 = GetF0AtSample(phrase,
                        segment.posSamples - segment.skipSamples + segment.headWindowStart + headWindow.Length / 2);
                    segment.headPhase = CalcPhase(headWindow,
                        segment.posSamples - segment.skipSamples + segment.headWindowStart, 44100, segment.headWindowF0);

                    var tailWindow = GetTailWindow(segment.samples, segment.envelope, out segment.tailWindowStart);
                    segment.tailWindowF0 = GetF0AtSample(phrase,
                        segment.posSamples - segment.skipSamples + segment.tailWindowStart + tailWindow.Length / 2);
                    segment.tailPhase = CalcPhase(tailWindow,
                        segment.posSamples - segment.skipSamples + segment.tailWindowStart, 44100, segment.tailWindowF0);
                }

                item.ApplyEnvelope(segment.samples);
            }

            if (phaseComp) {
                for (int i = 1; i < segments.Count; ++i) {
                    double? tailPhase = segments[i - 1].tailPhase;
                    double? headPhase = segments[i].headPhase;
                    if (!tailPhase.HasValue || !headPhase.HasValue) {
                        continue;
                    }
                    double lastCorrAngle = segments[i - 1].correction * 2.0 * Math.PI / 44100.0 * segments[i].headWindowF0;
                    double diff = headPhase.Value - (tailPhase.Value - lastCorrAngle);
                    while (diff < 0) {
                        diff += 2 * Math.PI;
                    }
                    while (diff >= 2 * Math.PI) {
                        diff -= 2 * Math.PI;
                    }
                    if (Math.Abs(diff - 2 * Math.PI) < diff) {
                        diff -= 2 * Math.PI;
                    }
                    segments[i].correction = (int)(diff / 2 / Math.PI * 44100 / segments[i].headWindowF0);
                }
            }

            var phraseSamples = new float[0];
            foreach (var segment in segments) {
                Array.Resize(ref phraseSamples, segment.posSamples + segment.correction + segment.samples.Length - segment.skipSamples);
                for (int i = Math.Max(0, -segment.skipSamples); i < segment.samples.Length - segment.skipSamples; i++) {
                    phraseSamples[segment.posSamples + segment.correction + i] += segment.samples[segment.skipSamples + i];
                }
            }
            return phraseSamples;
        }

        private float[] GetHeadWindow(float[] samples, IList<Vector2> envelope, out int windowStart) {
            var windowCenter = (envelope[0] + envelope[1]) * 0.5f;
            windowStart = Math.Max((int)windowCenter.X - 440, 0);
            int windowLength = Math.Min(880, samples.Length - windowStart);
            return samples.Skip(windowStart).Take(windowLength).ToArray();
        }

        private float[] GetTailWindow(float[] samples, IList<Vector2> envelope, out int windowStart) {
            var windowCenter = (envelope[envelope.Count - 1] + envelope[envelope.Count - 2]) * 0.5f;
            windowStart = Math.Max((int)windowCenter.X - 440, 0);
            int windowLength = Math.Min(880, samples.Length - windowStart);
            return samples.Skip(windowStart).Take(windowLength).ToArray();
        }

        private double GetF0AtSample(RenderPhrase phrase, float sampleIndex) {
            float sampleMs = sampleIndex / 44100f * 1000f;
            int sampleTick = phrase.timeAxis.MsPosToTickPos(phrase.positionMs - phrase.leadingMs + sampleMs);
            int pitchIndex = (int)Math.Round((double)(sampleTick - (phrase.position - phrase.leading)) / 5);
            pitchIndex = Math.Clamp(pitchIndex, 0, phrase.pitches.Length - 1);
            return MusicMath.ToneToFreq(phrase.pitches[pitchIndex] / 100);
        }

        private double? CalcPhase(float[] samples, int offset, int fs, double f) {
            if (samples.Length < 4) {
                return null;
            }
            var x = new DiscreteSignal(fs, samples);
            var peakTf = DesignFilter.IirPeak(f / fs, 5);
            var filter = new ZiFilter(peakTf);
            samples = filter.ZeroPhase(x).Samples;
            if (samples.Max() > 10) {
                return null;
            }
            double left = 0;
            double right = 0;
            for (int i = samples.Length / 2 - 1; i >= 1; --i) {
                if (samples[i] >= samples[i - 1] && samples[i] >= samples[i + 1]) {
                    left = i;
                    break;
                }
            }
            for (int i = samples.Length / 2; i <= samples.Length - 2; ++i) {
                if (samples[i] >= samples[i - 1] && samples[i] >= samples[i + 1]) {
                    right = i;
                    break;
                }
            }
            if (left >= right) {
                return null;
            }
            double actualF = fs / (right - left);
            if (Math.Abs(f - actualF) > f * 0.25) {
                return null;
            }
            double t = (offset + (left + right) * 0.5) / fs * f;
            return 2 * Math.PI * (Math.Round(t) - t);
        }

        public void CheckPermissions() { }

        public override string ToString() => phaseComp ? nameConvergence : nameSimple;
    }
}
