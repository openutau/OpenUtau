using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using K4os.Hash.xxHash;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Render {
    public class RenderNote {
        public readonly string lyric;
        public readonly int tone;
        public readonly int tuning;
        public readonly float adjustedTone;

        public readonly int position;
        public readonly int duration;
        public readonly int end;

        public readonly double positionMs;
        public readonly double durationMs;
        public readonly double endMs;

        public RenderNote(Pipeline.NoteSource note, TimeAxis axis, int partPosition, int phrasePosition) {
            lyric = note.Lyric;
            tone = note.Tone;
            tuning = note.Tuning;
            adjustedTone = note.AdjustedTone;

            position = partPosition + note.Position - phrasePosition;
            duration = note.Duration;
            end = position + duration;

            positionMs = axis.TickPosToMsPos(partPosition + note.Position);
            endMs = axis.TickPosToMsPos(partPosition + note.End);
            durationMs = endMs - positionMs;
        }
    }

    public class RenderPhone {
        public readonly int position;
        public readonly int duration;
        public readonly int end;
        public readonly int leading;

        public readonly double positionMs;
        public readonly double durationMs;
        public readonly double endMs;
        public readonly double leadingMs;

        public readonly string phoneme;
        public readonly int tone;
        public readonly int noteIndex;
        public readonly double tempo;
        public readonly UTempo[] tempos;

        public readonly double preutterMs;
        public readonly double overlapMs;
        public readonly double durCorrectionMs;
        public readonly string resampler;
        public readonly double adjustedTempo;
        public Tuple<string, int?, string>[] flags;
        public readonly string suffix;
        public readonly float volume;
        public readonly float velocity;
        public readonly float modulation;
        public readonly bool direct;
        public readonly Vector2[] envelope;
        /// <summary>The per-phoneme values the expression graph drove, for display. Not part of the hash.</summary>
        public readonly IReadOnlyDictionary<string, float>? drivenExpressions;

        public readonly int toneShift;

        public UOto oto;
        public ulong hash;

        internal RenderPhone(Pipeline.PhraseSource source, Pipeline.PhonemeSource phoneme, int phrasePosition) {
            position = source.PartPosition + phoneme.Position - phrasePosition;
            duration = phoneme.Duration;
            end = position + duration;
            positionMs = phoneme.PositionMs;
            durationMs = phoneme.DurationMs;
            endMs = phoneme.EndMs;
            leadingMs = phoneme.Preutter;
            leading = phoneme.Leading;

            this.phoneme = phoneme.Phoneme;
            tone = phoneme.Tone;
            tempos = phoneme.Tempos;
            tempo = phoneme.Tempo;
            adjustedTempo = phoneme.AdjustedTempo;

            preutterMs = phoneme.Preutter;
            overlapMs = phoneme.Overlap;
            durCorrectionMs = phoneme.Preutter - phoneme.TailIntrude + phoneme.TailOverlap;

            resampler = phoneme.Resampler;
            flags = phoneme.Flags;
            suffix = phoneme.Suffix;
            volume = phoneme.Volume;
            velocity = phoneme.Velocity;
            modulation = phoneme.Modulation;
            envelope = phoneme.Envelope;
            direct = phoneme.Direct;
            toneShift = phoneme.ToneShift;
            drivenExpressions = phoneme.Driven;

            oto = phoneme.Oto;
            hash = Hash();
        }

        private ulong Hash() {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(adjustedTempo);
                    writer.Write(duration);
                    writer.Write(phoneme ?? string.Empty);
                    writer.Write(tone);

                    writer.Write(resampler ?? string.Empty);
                    foreach (var flag in flags) {
                        writer.Write(flag.Item1);
                        if (flag.Item2.HasValue) {
                            writer.Write(flag.Item2.Value);
                        }
                    }
                    writer.Write(suffix);
                    writer.Write(volume);
                    writer.Write(velocity);
                    writer.Write(modulation);
                    writer.Write(direct);
                    writer.Write(leadingMs);
                    foreach (var point in envelope) {
                        writer.Write(point.X);
                        writer.Write(point.Y);
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }
    }

    public class RenderPhrase {
        public readonly USinger singer;
        public readonly TimeAxis timeAxis;

        public readonly int position;
        public readonly int duration;
        public readonly int end;
        public readonly int leading;

        public readonly double positionMs;
        public readonly double durationMs;
        public readonly double endMs;
        public readonly double leadingMs;

        public readonly RenderNote[] notes;
        public RenderPhone[] phones;

        public readonly float[] pitches;
        public readonly float[] pitchesBeforeDeviation;
        public readonly float[] dynamics;
        public readonly float[] gender;
        public readonly float[] breathiness;
        public readonly float[] toneShift;
        public readonly float[] tension;
        public readonly float[] voicing;
        public readonly Tuple<string, float[]>[] curves;
        public readonly IReadOnlyDictionary<string, float[]>? drivenCurves;
        public readonly ulong preEffectHash;
        public ulong hash;

        public ulong renderSalt = 0;

        internal readonly IRenderer renderer;
        public readonly string wavtool;

        public readonly PhraseLayout Layout;

        private List<string> cacheFiles = new List<string>();

        internal RenderPhrase(Pipeline.PhraseSource source, Pipeline.PhonemeSource[] phonemes, int phraseStart, int phraseEnd) {
            var phrasePhonemes = phonemes
                .Skip(phraseStart)
                .Take(phraseEnd - phraseStart)
                .ToList();
            var notesOf = source.Notes;
            var uNotes = source.PhraseNotes(phraseStart, phraseEnd);
            if (notesOf[uNotes.First()].Prev != -1
                && notesOf[notesOf[uNotes.First()].Prev].End == notesOf[uNotes.First()].Position) {
                uNotes.Insert(0, notesOf[uNotes.First()].Prev);
            }
            if (notesOf[uNotes.Last()].Next != -1
                && notesOf[uNotes.Last()].End == notesOf[notesOf[uNotes.Last()].Next].Position) {
                uNotes.Add(notesOf[uNotes.Last()].Next);
            }

            singer = source.Singer;
            renderer = source.Renderer;
            wavtool = source.Wavtool;
            timeAxis = source.Axis;

            position = source.PartPosition + phrasePhonemes.First().Position;
            end = source.PartPosition + phrasePhonemes.Last().End;
            duration = end - position;

            notes = uNotes
                .Select(n => new RenderNote(notesOf[n], timeAxis, source.PartPosition, position))
                .ToArray();
            phones = phrasePhonemes
                .Select(p => new RenderPhone(source, p, position))
                .ToArray();

            leading = phones.First().leading;

            positionMs = phones.First().positionMs;
            endMs = phones.Last().endMs;
            durationMs = endMs - positionMs;
            leadingMs = phones.First().leadingMs;

            const int pitchInterval = 5;
            int pitchStart = position - source.PartPosition - leading;
            pitches = new float[(end - source.PartPosition - pitchStart) / pitchInterval + 1];
            int index = 0;
            foreach (int noteIdx in uNotes) {
                var note = notesOf[noteIdx];
                while (pitchStart + index * pitchInterval < note.End && index < pitches.Length) {
                    pitches[index] = note.AdjustedTone * 100;
                    index++;
                }
            }
            index = Math.Max(1, index);
            while (index < pitches.Length) {
                pitches[index] = pitches[index - 1];
                index++;
            }

            float[]? pitchesBeforeVibrato = source.ExpressionGraph != null ? pitches.ToArray() : null;
            foreach (int noteIdx in uNotes) {
                var note = notesOf[noteIdx];
                if (note.Vibrato.Length <= 0) {
                    continue;
                }
                int startIndex = Math.Max(0, (int)Math.Ceiling((float)(note.Position - pitchStart) / pitchInterval));
                int endIndex = Math.Min(pitches.Length, (note.End - pitchStart) / pitchInterval);
                float nPeriod = (float)(note.Vibrato.Period / note.DurationMs);
                for (int i = startIndex; i < endIndex; ++i) {
                    float nPos = (float)(pitchStart + i * pitchInterval - note.Position) / note.Duration;
                    var point = note.Vibrato.Evaluate(nPos, nPeriod, note);
                    pitches[i] = point.Y * 100;
                }
            }
            float[]? vibratoPitches = source.ExpressionGraph != null ? pitches.ToArray() : null;

            foreach (int noteIdx in uNotes) {
                var note = notesOf[noteIdx];
                var pitchPoints = note.PitchPoints
                    .Select(point => {
                        double nodePosMs = timeAxis.TickPosToMsPos(source.PartPosition + note.Position);
                        return new PitchPoint(
                               timeAxis.MsPosToTickPos(nodePosMs + point.X) - source.PartPosition,
                               point.Y * 10 + note.AdjustedTone * 100,
                               point.shape);
                    })
                    .ToList();
                if (pitchPoints.Count == 0) {
                    pitchPoints.Add(new PitchPoint(note.Position, note.AdjustedTone * 100, PitchPointShape.io, true));
                    pitchPoints.Add(new PitchPoint(note.End, note.AdjustedTone * 100, PitchPointShape.io, true));
                }
                if (noteIdx == uNotes.First() && pitchPoints[0].X > pitchStart) {
                    pitchPoints.Insert(0, new PitchPoint(pitchStart, pitchPoints[0].Y, PitchPointShape.io, true));
                } else if (pitchPoints[0].X > note.Position) {
                    pitchPoints.Insert(0, new PitchPoint(note.Position, pitchPoints[0].Y, PitchPointShape.io, true));
                }
                if (pitchPoints.Last().X < note.End) {
                    pitchPoints.Add(new PitchPoint(note.End, pitchPoints.Last().Y, PitchPointShape.io, true));
                }
                index = Math.Max(0, (int)((pitchPoints[0].X - pitchStart) / pitchInterval));

                for (int i = 0; i < pitchPoints.Count - 1; i++) {
                    PitchPoint point_1 = i == 0 ? pitchPoints[i] : pitchPoints[i - 1];
                    PitchPoint point0 = pitchPoints[i];
                    PitchPoint point1 = pitchPoints[i + 1];
                    PitchPoint point2 = i >= pitchPoints.Count - 2 ? pitchPoints[i + 1] : pitchPoints[i + 2];
                    int x = pitchStart + index * pitchInterval;

                    if (note.PitchPoints.Count > 2 && point0.shape == PitchPointShape.sp && !point1.autoCompleted) {
                        var curve = new CubicSplineSegment(
                            point_1.X, point_1.Y,
                            point0.X, point0.Y,
                            point1.X, point1.Y,
                            point2.X, point2.Y);
                        while (x < point1.X && index < pitches.Length) {
                            float pitch = (float)curve.GetY(x);
                            float basePitch = note.Prev != -1 && x < notesOf[note.Prev].End
                                ? notesOf[note.Prev].AdjustedTone * 100
                                : note.AdjustedTone * 100;
                            pitches[index] += pitch - basePitch;
                            index++;
                            x += pitchInterval;
                        }
                    } else {
                        while (x < point1.X && index < pitches.Length) {
                            float pitch = (float)MusicMath.InterpolateShape(point0.X, point1.X, point0.Y, point1.Y, x, point0.shape);
                            float basePitch = note.Prev != -1 && x < notesOf[note.Prev].End
                                ? notesOf[note.Prev].AdjustedTone * 100
                                : note.AdjustedTone * 100;
                            pitches[index] += pitch - basePitch;
                            index++;
                            x += pitchInterval;
                        }
                    }
                }
            }

            float[]? parametricPitches = source.ExpressionGraph != null ? pitches.ToArray() : null;

            if (source.ModpSupported && source.ClassicSinger != null) {
                var cSinger = source.ClassicSinger;
                foreach (var phoneme in phrasePhonemes) {
                    var phonemeModp = phoneme.ModpRaw;
                    if (phonemeModp == 0) {
                        continue;
                    }

                    try {
                        if (phoneme.Oto.Frq == null) {
                            phoneme.Oto.Frq = new OtoFrq(phoneme.Oto, cSinger.Frqs);
                        }
                        if (phoneme.Oto.Frq.loaded == false) {
                            continue;
                        }
                        var frq = phoneme.Oto.Frq;
                        UTempo[] noteTempos = phoneme.NoteTempos;
                        var tempo = noteTempos.Length > 0 ? noteTempos[0].bpm : source.DefaultBpm;
                        var frqIntervalTick = MusicMath.TempoMsToTick(tempo, (double)1 * 1000 / 44100 * frq.hopSize);
                        double consonantStretch = Math.Pow(2f, 1.0f - phoneme.VelRaw / 100f);

                        var preutter = MusicMath.TempoMsToTick(tempo, Math.Min(phoneme.Preutter, phoneme.Oto.Preutter * consonantStretch));
                        int startIndex = Math.Max(0, (int)Math.Floor((phoneme.Position - pitchStart - preutter) / pitchInterval));
                        int position = (int)Math.Round((double)((phoneme.Position - pitchStart) / pitchInterval));
                        int startStretch = position + (int)Math.Round(MusicMath.TempoMsToTick(tempo, (phoneme.Oto.Consonant - phoneme.Oto.Preutter) * consonantStretch) / pitchInterval);
                        int endIndex = Math.Min(pitches.Length, (int)Math.Ceiling(phoneme.End - pitchStart - MusicMath.TempoMsToTick(tempo, phoneme.TailIntrude - phoneme.TailOverlap)) / pitchInterval);

                        double stretch = 1;
                        if (frq.toneDiffStretch.Length * frqIntervalTick < ((double)endIndex - startStretch) * pitchInterval) {
                            stretch = ((double)endIndex - startStretch) * pitchInterval / (frq.toneDiffStretch.Length * frqIntervalTick);
                        }
                        var env0 = new Vector2(0, 0);
                        var env1 = new Vector2((phoneme.Envelope[1].X - phoneme.Envelope[0].X) / (phoneme.Envelope[4].X - phoneme.Envelope[0].X), 100);
                        var env3 = new Vector2((phoneme.Envelope[3].X - phoneme.Envelope[0].X) / (phoneme.Envelope[4].X - phoneme.Envelope[0].X), 100);
                        var env4 = new Vector2(1, 0);

                        for (int i = 0; startStretch + i <= endIndex; i++) {
                            var pit = startStretch + i;
                            if (pit >= pitches.Length) break;
                            var frqPoint = i * (pitchInterval / frqIntervalTick) / stretch;
                            var frqPointMin = Math.Clamp((int)Math.Floor(frqPoint), 0, frq.toneDiffStretch.Length - 1);
                            var frqPointMax = Math.Clamp((int)Math.Ceiling(frqPoint), 0, frq.toneDiffStretch.Length - 1);
                            var diff = MusicMath.Linear(frqPointMin, frqPointMax, frq.toneDiffStretch[frqPointMin], frq.toneDiffStretch[frqPointMax], frqPoint);
                            diff = diff * phonemeModp / 100;
                            diff = Fade(diff, pit);
                            pitches[pit] = pitches[pit] + (float)(diff * 100);
                        }
                        for (int i = 0; startStretch + i - 1 >= startIndex; i--) {
                            var pit = startStretch + i - 1;
                            if (pit > endIndex || pit >= pitches.Length) continue;
                            var frqPoint = frq.toneDiffFix.Length + (i * (pitchInterval / frqIntervalTick) / consonantStretch);
                            var frqPointMin = Math.Clamp((int)Math.Floor(frqPoint), 0, frq.toneDiffFix.Length - 1);
                            var frqPointMax = Math.Clamp((int)Math.Ceiling(frqPoint), 0, frq.toneDiffFix.Length - 1);
                            var diff = MusicMath.Linear(frqPointMin, frqPointMax, frq.toneDiffFix[frqPointMin], frq.toneDiffFix[frqPointMax], frqPoint);
                            diff = diff * phonemeModp / 100;
                            diff = Fade(diff, pit);
                            pitches[pit] = pitches[pit] + (float)(diff * 100);
                        }
                        double Fade(double diff, int pit) {
                            var percentage = (double)(pit - startIndex) / (endIndex - startIndex);
                            if (phoneme.NextAdjacent && percentage > env3.X) {
                                diff = diff * Math.Clamp(MusicMath.Linear(env3.X, env4.X, env3.Y, env4.Y, percentage), 0, 100) / 100;
                            }
                            if (phoneme.PrevAdjacent && percentage < env1.X) {
                                diff = diff * Math.Clamp(MusicMath.Linear(env0.X, env1.X, env0.Y, env1.Y, percentage), 0, 100) / 100;
                            }
                            return diff;
                        }
                    } catch(Exception e) {
                        Log.Error(e, "Failed to compute mod plus.");
                    }
                }
            }

            pitchesBeforeDeviation = pitches.ToArray();
            var pitchCurve = source.Curves.FirstOrDefault(c => c.Abbr == Format.Ustx.PITD);
            if (pitchCurve != null && !pitchCurve.IsEmpty) {
                for (int i = 0; i < pitches.Length; ++i) {
                    pitches[i] += pitchCurve.Sample(pitchStart + i * pitchInterval);
                }
            }

            Dictionary<string, float[]>? graphCurves = null;
            Dictionary<string, float[]>? drivenCurveValues = null;
            if (source.ExpressionGraph != null) {
                var ticks = new int[pitches.Length];
                var modPlus = new float[pitches.Length];
                var vibrato = new float[pitches.Length];
                var pitchBend = new float[pitches.Length];
                for (int i = 0; i < ticks.Length; ++i) {
                    ticks[i] = pitchStart + i * pitchInterval;
                    modPlus[i] = pitchesBeforeDeviation[i] - parametricPitches![i];
                    vibrato[i] = vibratoPitches![i] - pitchesBeforeVibrato![i];
                    pitchBend[i] = parametricPitches[i] - vibrato[i];
                }
                var phrasePitch = new ExpressionGraph.PhrasePitch(pitchStart, pitchInterval,
                    pitchBend, vibrato, modPlus, pitchesBeforeVibrato);
                var context = new ExpressionGraph.GraphContext(source, phrasePitch, phraseStart);
                var drivenPitch = source.ExpressionGraph.EvaluatePitch(context, ticks);
                if (drivenPitch != null) {
                    Array.Copy(drivenPitch, pitches, pitches.Length);
                }
                graphCurves = source.ExpressionGraph.Evaluate(context, ticks);
            }

            var curves = new List<Tuple<string, float[]>>();

            foreach (var descriptor in source.CurveDescriptors) {
                var curve = source.Curves.FirstOrDefault(c => c.Abbr == descriptor.abbr)
                    ?? Pipeline.CurveSource.Empty(descriptor.abbr, (int)descriptor.defaultValue, (float)descriptor.min);
                Func<float, Pipeline.CurveSource, float> convert = ((x, _) => x);
                if (curve.Abbr == Format.Ustx.DYN) {
                    convert = ((x, c) => x == c.Min ? 0 : (float)MusicMath.DecibelToLinear(x * 0.1));
                }
                float[] curveSampled;
                if (graphCurves != null && curve.Abbr != Format.Ustx.PITD
                        && graphCurves.TryGetValue(curve.Abbr, out var driven)) {
                    curveSampled = new float[driven.Length];
                    var shown = new float[driven.Length];
                    for (int i = 0; i < driven.Length; ++i) {
                        shown[i] = Math.Clamp(driven[i], descriptor.min, descriptor.max);
                        curveSampled[i] = convert(shown[i], curve);
                    }
                    drivenCurveValues ??= new Dictionary<string, float[]>();
                    drivenCurveValues[curve.Abbr] = shown;
                } else {
                    curveSampled = SampleCurve(curve, pitchStart, pitches.Length, convert);
                }

                if (descriptor.type == UExpressionType.MorphingCurve || descriptor.abbr.StartsWith("cl", StringComparison.OrdinalIgnoreCase)) {
                    curves.Add(Tuple.Create(curve.Abbr, curveSampled));
                } else {
                    switch (curve.Abbr) {
                        case Format.Ustx.PITD: break;
                        case Format.Ustx.DYN : dynamics = curveSampled; break;
                        case Format.Ustx.SHFC: toneShift = curveSampled; break;
                        case Format.Ustx.GENC: gender = curveSampled; break;
                        case Format.Ustx.TENC: tension = curveSampled; break;
                        case Format.Ustx.BREC: breathiness = curveSampled; break;
                        case Format.Ustx.VOIC: voicing = curveSampled; break;
                        default:
                            curves.Add(Tuple.Create(curve.Abbr, curveSampled));
                            break;
                    }
                }
            }

            foreach (int noteIdx in uNotes) {
                var note = notesOf[noteIdx];
                if (note.Vibrato.Length <= 0 || note.Vibrato.VolLink == 0) {
                    continue;
                }
                if (dynamics == null) {
                    dynamics = new float[(end - source.PartPosition - pitchStart) / pitchInterval + 1];
                }
                int startIndex = Math.Max(0, (int)Math.Ceiling((float)(note.Position - pitchStart) / pitchInterval));
                int endIndex = Math.Min(pitches.Length, (note.End - pitchStart) / pitchInterval);
                float nPeriod = (float)(note.Vibrato.Period / note.DurationMs);
                for (int i = startIndex; i < endIndex; ++i) {
                    float nPos = (float)(pitchStart + i * pitchInterval - note.Position) / note.Duration;
                    float ratio = note.Vibrato.EvaluateVolume(nPos, nPeriod);
                    dynamics[i] = dynamics[i] * ratio;
                }
            }
            this.curves = curves.ToArray();
            drivenCurves = drivenCurveValues;
            preEffectHash = Hash(false);
            hash = Hash(true);

            try {
                var layout = renderer.Layout(this);
                double startMs = layout.positionMs - layout.leadingMs;
                Layout = new PhraseLayout(startMs, startMs + layout.estimatedLengthMs, layout.leadingMs, layout.estimatedLengthMs);
            } catch {
                Layout = new PhraseLayout(positionMs, endMs, 0, endMs - positionMs);
            }
        }

        private static float[] SampleCurve(Pipeline.CurveSource curve, int start, int length, Func<float, Pipeline.CurveSource, float> convert) {
            const int interval = 5;
            var result = new float[length];
            for (int i = 0; i < length; ++i) {
                result[i] = convert(curve.Sample(start + i * interval), curve);
            }
            return result;
        }

        private ulong Hash(bool postEffect) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(singer.Id);
                    writer.Write(renderer?.ToString() ?? "");
                    writer.Write(wavtool ?? "");
                    writer.Write(timeAxis.Timestamp);
                    foreach (var phone in phones) {
                        writer.Write(phone.hash);
                    }
                    if (postEffect) {
                        // The trailing null preserves binary parity with legacy cache hashes
                        foreach (var array in new float[][] { pitches, dynamics, gender, breathiness, toneShift, tension, voicing, null }) {
                            if (array == null) {
                                writer.Write("null");
                            } else {
                                foreach (var v in array) {
                                    writer.Write(v);
                                }
                            }
                        }
                        foreach (var curve in curves) {
                            writer.Write(curve.Item1);
                            foreach (var v in curve.Item2) {
                                writer.Write(v);
                            }
                        }
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

        public static List<RenderPhrase> FromPart(UProject project, UTrack track, UVoicePart part) {
            var source = Pipeline.PhraseSource.FromPart(project, track, part, 0);
            if (source == null) {
                return new List<RenderPhrase>();
            }
            return source.BuildPhrases().ToList();
        }

        public void AddCacheFile(string file) {
            if (string.IsNullOrWhiteSpace(file)) return;
            var filename = Path.GetFileNameWithoutExtension(file);
            if (!cacheFiles.Contains(filename)) {
                cacheFiles.Add(filename);
            }
        }

        public void DeleteCacheFiles() {
            foreach (var filename in cacheFiles) {
                var files = Directory.EnumerateFiles(PathManager.Inst.CachePath, $"{filename}*");
                foreach (var file in files) {
                    try {
                        File.Delete(file);
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to delete file {file}");
                    }
                }
            }
            cacheFiles.Clear();

            if (singer is ClassicSinger cSinger && cSinger.Frqs != null) {
                foreach (var oto in phones.Select(p => p.oto).Distinct()) {
                    oto.Frq = null;
                    cSinger.Frqs.Remove(oto.File);
                }
            }
        }
    }
}