using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Core;
using OpenUtau.Core.Render;

namespace OpenUtau.Classic {
    public class ResamplerItem {
        public RenderPhrase phrase;
        public RenderPhone phone;

        public IResampler resampler;
        public string inputFile;
        public string inputTemp;
        public string outputFile;
        public int tone;

        public Tuple<string, int?, string>[] flags;//flag, value, abbr
        public int velocity;
        public int volume;
        public int modulation;

        public float preutter;
        public float overlap;
        public double offset;
        public double durRequired;
        public double durCorrection;
        public double consonant;
        public double cutoff;
        public double skipOver;

        public double tempo;
        public int[] pitches;

        public ulong hash;

        public ResamplerItem(RenderPhrase phrase, RenderPhone phone, IResampler? resamplerOverride = null) {
            this.phrase = phrase;
            this.phone = phone;

            resampler = resamplerOverride ?? ToolsManager.Inst.GetResampler(phone.resampler);
            inputFile = phone.oto.File;
            inputTemp = VoicebankFiles.Inst.GetSourceTempPath(phrase.singer.Id, phone.oto, ".wav");
            tone = phone.tone;

            flags = phone.flags.Where(flag => resampler.SupportsFlag(flag.Item3)).ToArray();
            velocity = (int)(phone.velocity * 100);
            volume = (int)(phone.volume * 100);
            modulation = (int)(phone.modulation * 100);

            preutter = (float)phone.preutterMs;
            overlap = (float)phone.overlapMs;
            offset = phone.oto.Offset;
            var stretchRatio = Math.Pow(2, 1.0 - velocity * 0.01);
            double pitchLeadingMs = phone.oto.Preutter * stretchRatio;
            skipOver = phone.oto.Preutter * stretchRatio - phone.leadingMs;
            durRequired = phone.endMs - phone.positionMs + phone.durCorrectionMs + skipOver;
            durRequired = Math.Max(durRequired, phone.oto.Consonant);
            durRequired = Math.Ceiling(durRequired / 50.0 + 0.5) * 50.0;
            durCorrection = phone.durCorrectionMs;
            consonant = phone.oto.Consonant;
            cutoff = phone.oto.Cutoff;

            tempo = phone.adjustedTempo;

            double pitchCountMs = (phone.positionMs + phone.envelope[4].X) - (phone.positionMs - pitchLeadingMs);
            int pitchCount = (int)Math.Ceiling(MusicMath.TempoMsToTick(tempo, pitchCountMs) / 5.0);
            pitchCount = Math.Max(pitchCount, 0);
            pitches = new int[pitchCount];

            var phrasePitchStartMs = phrase.positionMs - phrase.leadingMs;
            var phrasePitchStartTick = (int)Math.Floor(phrase.timeAxis.MsPosToNonExactTickPos(phrasePitchStartMs));

            var pitchIntervalMs = MusicMath.TempoTickToMs(tempo, 5);
            var pitchSampleStartMs = phone.positionMs - pitchLeadingMs;

            for (int i = 0; i < pitches.Length; i++) {
                var samplePosMs = pitchSampleStartMs + pitchIntervalMs * i;
                var samplePosTick = (int)Math.Floor(phrase.timeAxis.MsPosToNonExactTickPos(samplePosMs));

                var sampleInterval = phrase.timeAxis.TickPosToMsPos(samplePosTick + 5) - phrase.timeAxis.TickPosToMsPos(samplePosTick);
                var sampleIndex = (samplePosTick - phrasePitchStartTick) / 5.0;
                sampleIndex = Math.Clamp(sampleIndex, 0, phrase.pitches.Length - 1);

                var sampleStart = (int)Math.Floor(sampleIndex);
                var sampleEnd = (int)Math.Ceiling(sampleIndex);

                var diffPitchMs = samplePosMs - phrase.timeAxis.TickPosToMsPos(phrasePitchStartTick + sampleStart * 5);
                var sampleAlpha = diffPitchMs / sampleInterval;

                var sampleLerped = phrase.pitches[sampleStart] + (phrase.pitches[sampleEnd] - phrase.pitches[sampleStart]) * sampleAlpha;

                pitches[i] = (int)Math.Round(sampleLerped - phone.tone * 100);
            }

            hash = Hash();
            outputFile = Path.Join(PathManager.Inst.CachePath,
                $"res-{XXH32.DigestOf(Encoding.UTF8.GetBytes(phrase.singer.Id)):x8}-{hash:x16}.wav");

            phrase.AddCacheFile(inputTemp);
            phrase.AddCacheFile(outputFile);
        }
        public string GetFlagsString() {
            var builder = new StringBuilder();
            foreach (var flag in flags) {
                builder.Append(flag.Item1);
                if (flag.Item2.HasValue) {
                    builder.Append(flag.Item2.Value);
                }
            }
            return builder.ToString();
        }

        ulong Hash() {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(resampler.ToString());
                    writer.Write(inputFile);
                    writer.Write(tone);

                    foreach (var flag in flags) {
                        writer.Write(flag.Item1);
                        if (flag.Item2.HasValue) {
                            writer.Write(flag.Item2.Value);
                        }
                    }
                    writer.Write(velocity);
                    writer.Write(volume);
                    writer.Write(modulation);

                    writer.Write(offset);
                    writer.Write(durRequired);
                    writer.Write(consonant);
                    writer.Write(cutoff);
                    writer.Write(skipOver);

                    writer.Write(tempo);
                    foreach (int pitch in pitches) {
                        writer.Write(pitch);
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

        public List<Vector2> EnvelopeMsToSamples() {
            int skipOverSamples = (int)(skipOver * 44100 / 1000);
            var envelope = phone.envelope.ToList();
            double shift = -envelope[0].X;
            for (int i = 0; i < envelope.Count; ++i) {
                var point = envelope[i];
                point.X = (float)((point.X + shift) * 44100 / 1000) + skipOverSamples;
                point.Y /= 100;
                envelope[i] = point;
            }
            return envelope;
        }

        public void ApplyEnvelope(float[] samples) {
            var envelope = EnvelopeMsToSamples();
            int nextPoint = 0;
            for (int i = 0; i < samples.Length; ++i) {
                while (nextPoint < envelope.Count && i > envelope[nextPoint].X) {
                    nextPoint++;
                }
                float gain;
                if (nextPoint == 0) {
                    gain = envelope.First().Y;
                } else if (nextPoint >= envelope.Count) {
                    gain = envelope.Last().Y;
                } else {
                    var p0 = envelope[nextPoint - 1];
                    var p1 = envelope[nextPoint];
                    if (p0.X >= p1.X) {
                        gain = p0.Y;
                    } else {
                        gain = p0.Y + (p1.Y - p0.Y) * (i - p0.X) / (p1.X - p0.X);
                    }
                }
                samples[i] *= gain;
            }
        }
    }
}
