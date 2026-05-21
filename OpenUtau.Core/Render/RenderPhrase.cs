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

        public RenderNote(UProject project, UPart part, UNote note, int phrasePosition) {
            lyric = note.lyric;
            tone = note.tone;
            tuning = note.tuning;
            adjustedTone = note.AdjustedTone;

            position = part.position + note.position - phrasePosition;
            duration = note.duration;
            end = position + duration;

            positionMs = project.timeAxis.TickPosToMsPos(part.position + note.position);
            endMs = project.timeAxis.TickPosToMsPos(part.position + note.End);
            durationMs = endMs - positionMs;
        }
    }

    public class RenderPhone {
        // Relative ticks
        public readonly int position;
        public readonly int duration;
        public readonly int end;
        public readonly int leading;

        // Absolute milliseconds
        public readonly double positionMs;
        public readonly double durationMs;
        public readonly double endMs;
        public readonly double leadingMs;

        public readonly string phoneme;
        public readonly int tone;
        public readonly int noteIndex;
        public readonly double tempo;
        public readonly UTempo[] tempos;

        // classic args
        public readonly double preutterMs;
        public readonly double overlapMs;
        public readonly double durCorrectionMs;
        public readonly string resampler;
        public readonly double adjustedTempo;
        public readonly Tuple<string, int?, string>[] flags;// flag, value, abbr. Abbr is kept here for flag filtering.
        public readonly string suffix;
        public readonly float volume;
        public readonly float velocity;
        public readonly float modulation;
        public readonly bool direct;
        public readonly Vector2[] envelope;

        // voicevox & enunu args
        public readonly int toneShift;

        public readonly UOto oto;
        public readonly ulong hash;

        internal RenderPhone(UProject project, UTrack track, UVoicePart part, UNote note, UPhoneme phoneme, int phrasePosition) {
            position = part.position + phoneme.position - phrasePosition;
            duration = phoneme.Duration;
            end = position + duration;
            positionMs = phoneme.PositionMs;
            durationMs = phoneme.DurationMs;
            endMs = phoneme.EndMs;
            leadingMs = phoneme.preutter;
            leading = Math.Max(0, project.timeAxis.TicksBetweenMsPos(positionMs - leadingMs, positionMs));

            this.phoneme = phoneme.phoneme;
            tone = note.tone;
            tempos = project.timeAxis.TemposBetweenTicks(part.position + phoneme.position - leading, part.position + phoneme.End);
            UTempo[] noteTempos = project.timeAxis.TemposBetweenTicks(part.position + phoneme.position, part.position + phoneme.End);
            tempo = noteTempos.Length > 0 ? noteTempos[0].bpm : project.tempos[0].bpm;

            double actualTickDuration = 0;
            for (int i = 0; i < noteTempos.Length; i++) {
                int tempoStart = Math.Max(part.position + phoneme.position, noteTempos[i].position);
                int tempoEnd = i + 1 < noteTempos.Length ? noteTempos[i + 1].position : part.position + phoneme.End;
                int tempoLength = tempoEnd - tempoStart;
                actualTickDuration += (double)(tempoLength * (tempo / noteTempos[i].bpm));
            }

            adjustedTempo = duration / actualTickDuration * tempo;

            preutterMs = phoneme.preutter;
            overlapMs = phoneme.overlap;
            durCorrectionMs = phoneme.preutter - phoneme.tailIntrude + phoneme.tailOverlap;


            resampler = track.RendererSettings.resampler;
            int eng = (int)phoneme.GetExpression(project, track, Format.Ustx.ENG).Item1;
            if (track.TryGetExpDescriptor(project, Format.Ustx.ENG, out var descriptor)
                && eng >= 0 && eng < descriptor.options.Length
                && !string.IsNullOrEmpty(descriptor.options[eng])) {
                resampler = descriptor.options[eng];
            }
            flags = phoneme.GetResamplerFlags(project, track);
            string voiceColor = phoneme.GetVoiceColor(project, track);
            suffix = track.Singer.Subbanks.FirstOrDefault(
                subbank => subbank.Color == voiceColor)?.Suffix ?? string.Empty;
            volume = phoneme.GetExpression(project, track, Format.Ustx.VOL).Item1 * 0.01f;
            velocity = phoneme.GetExpression(project, track, Format.Ustx.VEL).Item1 * 0.01f;
            modulation = phoneme.GetExpression(project, track, Format.Ustx.MOD).Item1 * 0.01f;
            leadingMs = phoneme.preutter;
            envelope = phoneme.envelope.data.ToArray();
            direct = phoneme.GetExpression(project, track, Format.Ustx.DIR).Item1 == 1;
            toneShift = (int)phoneme.GetExpression(project, track, Format.Ustx.SHFT).Item1;

            oto = phoneme.oto;
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
        public readonly RenderPhone[] phones;

        public readonly float[] pitches;
        public readonly float[] pitchesBeforeDeviation;
        public readonly float[] dynamics;
        public readonly float[] gender;
        public readonly float[] breathiness;
        public readonly float[] toneShift;
        public readonly float[] tension;
        public readonly float[] voicing;
        public readonly Tuple<string, float[]>[] curves;//custom curves defined by renderer
        public readonly ulong preEffectHash;
        public readonly ulong hash;

        internal readonly IRenderer renderer;
        public readonly string wavtool;

        private List<string> cacheFiles = new List<string>();

        internal RenderPhrase(UProject project, UTrack track, UVoicePart part, IEnumerable<UPhoneme> phonemes) {
            var uNotes = new List<UNote> { phonemes.First().Parent };
            var endNote = phonemes.Last().Parent;
            while (endNote.Next != null && endNote.Next.Extends != null) {
                endNote = endNote.Next;
            }
            while (uNotes.Last() != endNote) {
                uNotes.Add(uNotes.Last().Next);
            }
            var tail = uNotes.Last();
            var next = tail.Next;
            while (next != null && next.Extends == tail) {
                uNotes.Add(next);
                next = next.Next;
            }
            if (uNotes.First().Prev != null && uNotes.First().Prev.End == uNotes.First().position) {
                uNotes.Insert(0, uNotes.First().Prev);
            }
            if (uNotes.Last().Next != null && uNotes.Last().End == uNotes.Last().Next.position) {
                uNotes.Add(uNotes.Last().Next);
            }

            singer = track.Singer;
            renderer = track.RendererSettings.Renderer;
            wavtool = track.RendererSettings.wavtool;
            timeAxis = project.timeAxis.Clone();

            position = part.position + phonemes.First().position;
            end = part.position + phonemes.Last().End;
            duration = end - position;

            notes = uNotes
                .Select(n => new RenderNote(project, part, n, position))
                .ToArray();
            phones = phonemes
                .Select(p => new RenderPhone(project, track, part, p.Parent, p, position))
                .ToArray();

            leading = phones.First().leading;

            positionMs = phones.First().positionMs;
            endMs = phones.Last().endMs;
            durationMs = endMs - positionMs;
            leadingMs = phones.First().leadingMs;

            const int pitchInterval = 5;
            int pitchStart = position - part.position - leading;
            pitches = new float[(end - part.position - pitchStart) / pitchInterval + 1];
            int index = 0;
            // Create flat pitches
            foreach (var note in uNotes) {
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
            // Vibrato
            foreach (var note in uNotes) {
                if (note.vibrato.length <= 0) {
                    continue;
                }
                int startIndex = Math.Max(0, (int)Math.Ceiling((float)(note.position - pitchStart) / pitchInterval));
                int endIndex = Math.Min(pitches.Length, (note.End - pitchStart) / pitchInterval);
                // Use tempo at note start to calculate vibrato period.
                float nPeriod = (float)(note.vibrato.period / note.DurationMs);
                for (int i = startIndex; i < endIndex; ++i) {
                    float nPos = (float)(pitchStart + i * pitchInterval - note.position) / note.duration;
                    var point = note.vibrato.Evaluate(nPos, nPeriod, note);
                    pitches[i] = point.Y * 100;
                }
            }
            // Pitch points
            foreach (var note in uNotes) {
                var pitchPoints = note.pitch.data
                    .Select(point => {
                        double nodePosMs = timeAxis.TickPosToMsPos(part.position + note.position);
                        return new PitchPoint(
                               timeAxis.MsPosToTickPos(nodePosMs + point.X) - part.position,
                               point.Y * 10 + note.AdjustedTone * 100,
                               point.shape);
                    })
                    .ToList();
                if (pitchPoints.Count == 0) {
                    pitchPoints.Add(new PitchPoint(note.position, note.AdjustedTone * 100, PitchPointShape.io, true));
                    pitchPoints.Add(new PitchPoint(note.End, note.AdjustedTone * 100, PitchPointShape.io, true));
                }
                if (note == uNotes.First() && pitchPoints[0].X > pitchStart) {
                    pitchPoints.Insert(0, new PitchPoint(pitchStart, pitchPoints[0].Y, PitchPointShape.io, true));
                } else if (pitchPoints[0].X > note.position) {
                    pitchPoints.Insert(0, new PitchPoint(note.position, pitchPoints[0].Y, PitchPointShape.io, true));
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

                    if (note.pitch.data.Count > 2 && point0.shape == PitchPointShape.sp && !point1.autoCompleted) {
                        var curve = new CubicSplineSegment(
                            point_1.X, point_1.Y,
                            point0.X, point0.Y,
                            point1.X, point1.Y,
                            point2.X, point2.Y);
                        while (x < point1.X && index < pitches.Length) {
                            float pitch = (float)curve.GetY(x);
                            float basePitch = note.Prev != null && x < note.Prev.End
                                ? note.Prev.AdjustedTone * 100
                                : note.AdjustedTone * 100;
                            pitches[index] += pitch - basePitch;
                            index++;
                            x += pitchInterval;
                        }
                    } else {
                        while (x < point1.X && index < pitches.Length) {
                            float pitch = (float)MusicMath.InterpolateShape(point0.X, point1.X, point0.Y, point1.Y, x, point0.shape);
                            float basePitch = note.Prev != null && x < note.Prev.End
                                ? note.Prev.AdjustedTone * 100
                                : note.AdjustedTone * 100;
                            pitches[index] += pitch - basePitch;
                            index++;
                            x += pitchInterval;
                        }
                    }
                }
            }
            // Mod plus
            if (track.TryGetExpDescriptor(project, Format.Ustx.MODP, out var modp) && renderer.SupportsExpression(modp) && singer is ClassicSinger cSinger) {
                foreach (var phoneme in phonemes) {
                    var phonemeModp = phoneme.GetExpression(project, track, Format.Ustx.MODP).Item1;
                    if (phonemeModp == 0) {
                        continue;
                    }

                    try {
                        if (phoneme.oto.Frq == null) {
                            phoneme.oto.Frq = new OtoFrq(phoneme.oto, cSinger.Frqs);
                        }
                        if (phoneme.oto.Frq.loaded == false) {
                            continue;
                        }
                        var frq = phoneme.oto.Frq;
                        UTempo[] noteTempos = project.timeAxis.TemposBetweenTicks(part.position + phoneme.position, part.position + phoneme.End);
                        var tempo = noteTempos.Length > 0 ? noteTempos[0].bpm : project.tempos[0].bpm; // compromise 妥協！
                        var frqIntervalTick = MusicMath.TempoMsToTick(tempo, (double)1 * 1000 / 44100 * frq.hopSize);
                        double consonantStretch = Math.Pow(2f, 1.0f - phoneme.GetExpression(project, track, Format.Ustx.VEL).Item1 / 100f);

                        var preutter = MusicMath.TempoMsToTick(tempo, Math.Min(phoneme.preutter, phoneme.oto.Preutter * consonantStretch));
                        int startIndex = Math.Max(0, (int)Math.Floor((phoneme.position - pitchStart - preutter) / pitchInterval));
                        int position = (int)Math.Round((double)((phoneme.position - pitchStart) / pitchInterval));
                        int startStretch = position + (int)Math.Round(MusicMath.TempoMsToTick(tempo, (phoneme.oto.Consonant - phoneme.oto.Preutter) * consonantStretch) / pitchInterval);
                        int endIndex = Math.Min(pitches.Length, (int)Math.Ceiling(phoneme.End - pitchStart - MusicMath.TempoMsToTick(tempo, phoneme.tailIntrude - phoneme.tailOverlap)) / pitchInterval);

                        double stretch = 1;
                        if (frq.toneDiffStretch.Length * frqIntervalTick < ((double)endIndex - startStretch) * pitchInterval) {
                            stretch = ((double)endIndex - startStretch) * pitchInterval / (frq.toneDiffStretch.Length * frqIntervalTick);
                        }
                        var env0 = new Vector2(0, 0);
                        var env1 = new Vector2((phoneme.envelope.data[1].X - phoneme.envelope.data[0].X) / (phoneme.envelope.data[4].X - phoneme.envelope.data[0].X), 100);
                        var env3 = new Vector2((phoneme.envelope.data[3].X - phoneme.envelope.data[0].X) / (phoneme.envelope.data[4].X - phoneme.envelope.data[0].X), 100);
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
                            if (phoneme.Next != null && phoneme.End == phoneme.Next.position && percentage > env3.X) {
                                diff = diff * Math.Clamp(MusicMath.Linear(env3.X, env4.X, env3.Y, env4.Y, percentage), 0, 100) / 100;
                            }
                            if (phoneme.Prev != null && phoneme.Prev.End == phoneme.position && percentage < env1.X) {
                                diff = diff * Math.Clamp(MusicMath.Linear(env0.X, env1.X, env0.Y, env1.Y, percentage), 0, 100) / 100;
                            }
                            return diff;
                        }
                    } catch(Exception e) {
                        Log.Error(e, "Failed to compute mod plus.");
                    }
                }
            }

            // PITD
            pitchesBeforeDeviation = pitches.ToArray();
            var pitchCurve = part.curves.FirstOrDefault(c => c.abbr == Format.Ustx.PITD);
            if (pitchCurve != null && !pitchCurve.IsEmpty) {
                for (int i = 0; i < pitches.Length; ++i) {
                    pitches[i] += pitchCurve.Sample(pitchStart + i * pitchInterval);
                }
            }

            var curves = new List<Tuple<string, float[]>>();

            foreach(var descriptor in project.expressions.Values) {
                if(descriptor.type != UExpressionType.Curve) {
                    continue;
                }
                var curve = part.curves.FirstOrDefault(c => c.abbr == descriptor.abbr);
                bool isSupported = renderer.SupportsExpression(descriptor);
                if (!isSupported) {
                    continue;
                }
                if (curve == null) {
                    curve = new UCurve(descriptor);
                }
                Func<float, UCurve, float> convert = ((x, _) => x);
                if (curve.abbr == Format.Ustx.DYN) {
                    convert = ((x, c) => x == c.descriptor.min ? 0 : (float)MusicMath.DecibelToLinear(x * 0.1));
                }
                var curveSampled = SampleCurve(curve, pitchStart, pitches.Length, convert);
                switch (curve.abbr) {
                    case Format.Ustx.PITD: break;
                    case Format.Ustx.DYN : dynamics = curveSampled; break;
                    case Format.Ustx.SHFC: toneShift = curveSampled; break;
                    case Format.Ustx.GENC: gender = curveSampled; break;
                    case Format.Ustx.TENC: tension = curveSampled; break;
                    case Format.Ustx.BREC: breathiness = curveSampled; break;
                    case Format.Ustx.VOIC: voicing = curveSampled; break;
                    default:
                        curves.Add(Tuple.Create(curve.abbr,curveSampled));
                        break;
                }
            }
            // Linking vibrato and volume
            // int dynamicsInterval = 5;
            foreach (var note in uNotes) {
                if (note.vibrato.length <= 0 || note.vibrato.volLink == 0) {
                    continue;
                }
                if (dynamics == null) {
                    dynamics = new float[(end - part.position - pitchStart) / pitchInterval + 1];
                }
                int startIndex = Math.Max(0, (int)Math.Ceiling((float)(note.position - pitchStart) / pitchInterval));
                int endIndex = Math.Min(pitches.Length, (note.End - pitchStart) / pitchInterval);
                float nPeriod = (float)(note.vibrato.period / note.DurationMs);
                for (int i = startIndex; i < endIndex; ++i) {
                    float nPos = (float)(pitchStart + i * pitchInterval - note.position) / note.duration;
                    float ratio = note.vibrato.EvaluateVolume(nPos, nPeriod);
                    dynamics[i] = dynamics[i] * ratio;
                }
            }
            this.curves = curves.ToArray();
            preEffectHash = Hash(false);
            hash = Hash(true);
        }

        private static float[] SampleCurve(UCurve curve, int start, int length, Func<float, UCurve, float> convert) {
            const int interval = 5;
            var result = new float[length];
            for (int i = 0; i < length; ++i) {
                result[i] = convert(curve.Sample(start + i * interval), curve);
            }
            return result;
        }

        private static float[] SampleCurve(UVoicePart part, string abbr, int start, int length, Func<float, UCurve, float> convert) {
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (curve == null) {
                return null;
            }
            return SampleCurve(curve, start, length, convert);
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
                        foreach (var array in new float[][] { pitches, dynamics, gender, breathiness, toneShift, tension, voicing }) {
                            if (array == null) {
                                writer.Write("null");
                            } else {
                                foreach (var v in array) {
                                    writer.Write(v);
                                }
                            }
                        }
                        foreach(var curve in curves) {
                            writer.Write(curve.Item1);
                            foreach(var v in curve.Item2) {
                                writer.Write(v);
                            }
                        }
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

        public static List<RenderPhrase> FromPart(UProject project, UTrack track, UVoicePart part) {
            var phrases = new List<RenderPhrase>();
            var phonemes = part.phonemes
                .Where(phoneme => !phoneme.Error)
                .ToList();
            if (phonemes.Count == 0) {
                return phrases;
            }
            var phrasePhonemes = new List<UPhoneme>() { phonemes[0] };
            for (int i = 1; i < phonemes.Count; ++i) {
                if (phonemes[i - 1].End != phonemes[i].position) {
                    phrases.Add(new RenderPhrase(project, track, part, phrasePhonemes));
                    phrasePhonemes.Clear();
                }
                phrasePhonemes.Add(phonemes[i]);
            }
            if (phrasePhonemes.Count > 0) {
                phrases.Add(new RenderPhrase(project, track, part, phrasePhonemes));
                phrasePhonemes.Clear();
            }
            return phrases;
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
