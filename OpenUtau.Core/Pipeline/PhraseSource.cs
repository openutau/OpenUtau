using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenUtau.Classic;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Pipeline {
    /// <summary>
    /// Immutable copy of a note's phrase-build inputs.
    /// </summary>
    public sealed class NoteSource {
        public readonly int Index;
        public readonly int Position;
        public readonly int Duration;
        public readonly int End;
        public readonly int ExtendedDuration;
        public readonly string Lyric;
        public readonly int Tone;
        public readonly int Tuning;
        public readonly float AdjustedTone;
        public readonly double DurationMs;
        // Index into PhraseSource.Notes, or -1.
        public readonly int Prev;
        public readonly int Next;
        public readonly int Extends;
        public readonly VibratoSource Vibrato;
        public readonly List<PitchPoint> PitchPoints;

        internal NoteSource(
                int index, UNote note, TimeAxis axis, int partPosition,
                int prev, int next, int extends) {
            Index = index;
            Position = note.position;
            Duration = note.duration;
            End = note.End;
            ExtendedDuration = note.ExtendedDuration;
            Lyric = note.lyric;
            Tone = note.tone;
            Tuning = note.tuning;
            AdjustedTone = note.AdjustedTone;
            DurationMs = axis.MsBetweenTickPos(partPosition + note.position, partPosition + note.End);
            Prev = prev;
            Next = next;
            Extends = extends;
            Vibrato = new VibratoSource(note.vibrato);
            PitchPoints = note.pitch.data
                .Select(p => new PitchPoint(p.X, p.Y, p.shape, p.autoCompleted))
                .ToList();
        }
    }

    /// <summary>
    /// Immutable copy of the vibrato curve; the evaluation math must stay
    /// bit-identical to the live one.
    /// </summary>
    public sealed class VibratoSource {
        public readonly float Length;
        public readonly float Period;
        public readonly float Depth;
        public readonly float In;
        public readonly float Out;
        public readonly float Shift;
        public readonly float Drift;
        public readonly float VolLink;

        public VibratoSource(UVibrato source) {
            Length = source.length;
            Period = source.period;
            Depth = source.depth;
            In = source.@in;
            Out = source.@out;
            Shift = source.shift;
            Drift = source.drift;
            VolLink = source.volLink;
        }

        public float NormalizedStart => 1f - Length / 100f;

        public Vector2 Evaluate(float nPos, float nPeriod, NoteSource note) {
            float nStart = NormalizedStart;
            float nIn = Length / 100f * In / 100f;
            float nInPos = nStart + nIn;
            float nOut = Length / 100f * Out / 100f;
            float nOutPos = 1f - nOut;
            float t = (nPos - nStart) / nPeriod + Shift / 100f;
            float y = (float)Math.Sin(2 * Math.PI * t) * Depth + (Depth / 100 * Drift);
            if (nPos < nStart) {
                y = 0;
            } else if (nPos < nInPos) {
                y *= (nPos - nStart) / nIn;
            } else if (nPos > nOutPos) {
                y *= (1f - nPos) / nOut;
            }
            return new Vector2(note.Position + note.Duration * nPos, note.AdjustedTone + y / 100f);
        }

        public float EvaluateVolume(float nPos, float nPeriod) {
            float nStart = NormalizedStart;
            float nIn = Length / 100f * In / 100f;
            float nInPos = nStart + nIn;
            float nOut = Length / 100f * Out / 100f;
            float nOutPos = 1f - nOut;
            float shift = Shift;
            float volLink = VolLink;
            if (volLink < 0) {
                shift += 50;
                if (shift > 100) {
                    shift -= 100;
                }
                volLink *= -1;
            }
            float t = (nPos - nStart) / nPeriod + Shift / 100f;
            float reduction = (-(float)Math.Sin(2 * Math.PI * t) / 2 + 0.3f) * volLink / 100;
            if (nPos < nStart) {
                reduction = 0;
            } else if (nPos < nInPos) {
                reduction *= (nPos - nStart) / nIn;
            } else if (nPos > nOutPos) {
                reduction *= (1f - nPos) / nOut;
            }
            float y = 1 - reduction;
            return y;
        }
    }

    /// <summary>
    /// Immutable copy of a part curve; <see cref="Sample"/> must stay
    /// bit-identical to <see cref="UCurve.Sample"/>.
    /// </summary>
    public sealed class CurveSource {
        public readonly string Abbr;
        public readonly int[] Xs;
        public readonly int[] Ys;
        public readonly int DefaultY;
        public readonly float Min;
        public readonly bool IsEmpty;

        internal CurveSource(UCurve curve, int defaultY) {
            Abbr = curve.abbr;
            Xs = curve.xs.ToArray();
            Ys = curve.ys.ToArray();
            DefaultY = defaultY;
            Min = (float)(curve.descriptor?.min ?? 0f);
            IsEmpty = curve.IsEmpty;
        }

        /// <summary>An absent curve: sampling always returns the default value.</summary>
        internal CurveSource(string abbr, int[] xs, int[] ys, int defaultY, float min, bool isEmpty) {
            Abbr = abbr;
            Xs = xs;
            Ys = ys;
            DefaultY = defaultY;
            Min = min;
            IsEmpty = isEmpty;
        }

        internal static CurveSource Empty(string abbr, int defaultY, float min) =>
            new CurveSource(abbr, Array.Empty<int>(), Array.Empty<int>(), defaultY, min, true);

        public int Sample(int x) {
            int idx = Array.BinarySearch(Xs, x);
            if (idx >= 0) {
                return Ys[idx];
            }
            idx = ~idx;
            if (idx > 0 && idx < Xs.Length) {
                return (int)Math.Round(MusicMath.Linear(Xs[idx - 1], Xs[idx], Ys[idx - 1], Ys[idx], x));
            }
            return DefaultY;
        }
    }

    /// <summary>
    /// Immutable copy of one validated phoneme's phrase-build inputs, with
    /// expressions resolved at snapshot time.
    /// </summary>
    public sealed class PhonemeSource {
        // Geometry (part-relative ticks / absolute ms).
        public readonly int Position;
        public readonly int Duration;
        public readonly int End;
        public readonly double PositionMs;
        public readonly double DurationMs;
        public readonly double EndMs;
        public readonly double Preutter;
        public readonly double Overlap;
        public readonly double TailIntrude;
        public readonly double TailOverlap;
        public readonly int Leading;
        public readonly bool PrevAdjacent;
        public readonly bool NextAdjacent;

        // Identity.
        public readonly string Phoneme;
        public readonly int Tone;
        public readonly int NoteIndex;

        // Time.
        public readonly UTempo[] Tempos;
        public readonly UTempo[] NoteTempos;
        public readonly double Tempo;
        public readonly double AdjustedTempo;

        // Resolved render arguments (classic).
        public readonly string Resampler;
        public readonly Tuple<string, int?, string>[] Flags;
        public readonly string Suffix;
        public readonly string Suffix2;
        public readonly float Volume;
        public readonly float Velocity;
        public readonly float Modulation;
        public readonly bool Direct;
        public readonly int ToneShift;
        public readonly Vector2[] Envelope;

        // MOD+ inputs (raw values; the builder applies them).
        public readonly float VelRaw;
        public readonly float ModpRaw;

        // Voicebank resources, stable per singer.
        public readonly UOto Oto;
        public readonly UOto Oto2;

        internal PhonemeSource(UPhoneme phoneme, int noteIndex, TimeAxis axis,
                int partPosition, UTrack track, UProject project,
                string trackResampler, bool xsyAvailable) {
            Position = phoneme.position;
            Duration = phoneme.Duration;
            End = phoneme.End;
            PositionMs = phoneme.PositionMs;
            DurationMs = phoneme.DurationMs;
            EndMs = phoneme.EndMs;
            Preutter = phoneme.preutter;
            Overlap = phoneme.overlap;
            TailIntrude = phoneme.tailIntrude;
            TailOverlap = phoneme.tailOverlap;
            Leading = Math.Max(0, axis.TicksBetweenMsPos(PositionMs - phoneme.preutter, PositionMs));
            PrevAdjacent = phoneme.Prev != null && phoneme.Prev.End == phoneme.position;
            NextAdjacent = phoneme.Next != null && phoneme.End == phoneme.Next.position;

            UNote note = phoneme.Parent;
            Phoneme = phoneme.phoneme;
            Tone = note?.tone ?? 0;
            NoteIndex = noteIndex;
            int absStart = partPosition + phoneme.position;
            int absEnd = partPosition + phoneme.End;
            Tempos = axis.TemposBetweenTicks(absStart - Leading, absEnd);
            NoteTempos = axis.TemposBetweenTicks(absStart, absEnd);
            double fallbackBpm = project.tempos.Count > 0 ? project.tempos[0].bpm : 120;
            Tempo = NoteTempos.Length > 0 ? NoteTempos[0].bpm : fallbackBpm;

            double actualTickDuration = 0;
            for (int i = 0; i < NoteTempos.Length; i++) {
                int tempoStart = Math.Max(absStart, NoteTempos[i].position);
                int tempoEnd = i + 1 < NoteTempos.Length ? NoteTempos[i + 1].position : absEnd;
                int tempoLength = tempoEnd - tempoStart;
                actualTickDuration += (double)(tempoLength * (Tempo / NoteTempos[i].bpm));
            }
            AdjustedTempo = Duration / actualTickDuration * Tempo;

            int eng = (int)phoneme.GetExpression(project, track, Format.Ustx.ENG).Item1;
            Resampler = trackResampler;
            if (track.TryGetExpDescriptor(project, Format.Ustx.ENG, out var engDescriptor)
                && eng >= 0 && eng < engDescriptor.options.Length
                && !string.IsNullOrEmpty(engDescriptor.options[eng])) {
                Resampler = engDescriptor.options[eng];
            }
            Flags = phoneme.GetResamplerFlags(project, track);
            string voiceColor = phoneme.GetVoiceColor(project, track);
            Suffix = track.Singer.Subbanks
                .FirstOrDefault(subbank => subbank.Color == voiceColor)?.Suffix ?? string.Empty;
            string targetColor = xsyAvailable ? phoneme.GetVoiceColor2(project, track) : null;
            if (!string.IsNullOrEmpty(targetColor)) {
                Suffix2 = track.Singer.Subbanks
                    .FirstOrDefault(subbank => subbank.Color == targetColor)?.Suffix ?? string.Empty;
            }
            Volume = phoneme.GetExpression(project, track, Format.Ustx.VOL).Item1 * 0.01f;
            float vel = phoneme.GetExpression(project, track, Format.Ustx.VEL).Item1;
            VelRaw = vel;
            Velocity = vel * 0.01f;
            Modulation = phoneme.GetExpression(project, track, Format.Ustx.MOD).Item1 * 0.01f;
            Direct = phoneme.GetExpression(project, track, Format.Ustx.DIR).Item1 == 1;
            ToneShift = (int)phoneme.GetExpression(project, track, Format.Ustx.SHFT).Item1;
            Envelope = phoneme.envelope.data.ToArray();
            // mod+ is an optional descriptor; the original code only resolved
            // the value when the descriptor existed, so an absent one means
            // "off" (0) rather than an error.
            bool hasModp = track.TryGetExpDescriptor(project, Format.Ustx.MODP, out _);
            ModpRaw = hasModp ? phoneme.GetExpression(project, track, Format.Ustx.MODP).Item1 : 0f;

            Oto = phoneme.oto;
            if (Oto != null && !string.IsNullOrEmpty(targetColor)) {
                string basePhoneme = Oto.Phonetic ?? phoneme.phoneme;
                if (track.Singer.TryGetMappedOto(basePhoneme, note.tone, targetColor, out var secondaryOto)) {
                    Oto2 = secondaryOto;
                }
            }
        }
    }

    /// <summary>
    /// Everything the phrase build reads, copied off the live document on the
    /// UI thread. The builder consumes it on a background thread and never
    /// touches the document; the document keeps its back-reference slot
    /// (<see cref="UVoicePart.renderPhrases"/>) filled from the result.
    /// </summary>
    public sealed class PhraseSource {
        public readonly PartId PartId;
        public readonly DocRevision Revision;
        /// <summary>Monotonic per-part build generation; stale results are dropped.</summary>
        public readonly long Generation;

        public readonly int PartPosition;
        /// <summary>A clone of the project time axis; never mutated after handoff.</summary>
        public readonly TimeAxis Axis;
        public readonly double DefaultBpm;

        // Stable resources, not per-keystroke document state.
        public readonly USinger Singer;
        public readonly IRenderer Renderer;
        public readonly string Resampler;
        public readonly string Wavtool;
        public readonly IReadOnlyDictionary<string, string> RendererSettings;
        public readonly ClassicSinger ClassicSinger;
        public readonly bool ModpSupported;

        public readonly NoteSource[] Notes;
        public readonly CurveSource[] Curves;
        /// <summary>The supported curve descriptors in document order.</summary>
        public readonly UExpressionDescriptor[] CurveDescriptors;
        public readonly bool XsyAvailable;
        public readonly PhonemeSource[] Phonemes;
        /// <summary>Half-open [start, end) index ranges into <see cref="Phonemes"/>.</summary>
        public readonly (int Start, int End)[] PhraseGroups;

        internal PhraseSource(
                PartId partId, DocRevision revision, long generation,
                UProject project, UTrack track, UVoicePart part,
                List<UPhoneme> phonemes, (int Start, int End)[] groups) {
            PartId = partId;
            Revision = revision;
            Generation = generation;
            PartPosition = part.position;
            Axis = project.timeAxis.Clone();
            DefaultBpm = project.tempos.Count > 0 ? project.tempos[0].bpm : 120;
            Singer = track.Singer;
            Renderer = track.RendererSettings.Renderer;
            Resampler = track.RendererSettings.resampler;
            Wavtool = track.RendererSettings.wavtool;
            RendererSettings = new Dictionary<string, string>(
                track.RendererSettings.rendererSettings ?? new Dictionary<string, string>());
            ClassicSinger = Singer as ClassicSinger;
            ModpSupported = track.TryGetExpDescriptor(project, Format.Ustx.MODP, out var modp)
                && Renderer.SupportsExpression(modp);
            XsyAvailable = part.curves.Any(c => c.abbr == Format.Ustx.XSY);

            var noteIndexByNote = new Dictionary<UNote, int>();
            var notes = part.notes.ToList();
            for (int i = 0; i < notes.Count; i++) {
                noteIndexByNote[notes[i]] = i;
            }
            Notes = new NoteSource[notes.Count];
            for (int i = 0; i < notes.Count; i++) {
                var n = notes[i];
                Notes[i] = new NoteSource(i, n, Axis, part.position,
                    n.Prev != null ? noteIndexByNote[n.Prev] : -1,
                    n.Next != null ? noteIndexByNote[n.Next] : -1,
                    n.Extends != null ? noteIndexByNote[n.Extends] : -1);
            }
            Curves = part.curves
                .Select(c => new CurveSource(c, (int)(c.descriptor?.defaultValue ?? 0)))
                .ToArray();
            // The supported project curve descriptors, in document order.
            CurveDescriptors = project.expressions.Values
                .Where(d => d.type == UExpressionType.Curve && Renderer.SupportsExpression(d))
                .ToArray();

            Phonemes = new PhonemeSource[phonemes.Count];
            for (int i = 0; i < phonemes.Count; i++) {
                var p = phonemes[i];
                Phonemes[i] = new PhonemeSource(p,
                    p.Parent != null ? noteIndexByNote[p.Parent] : -1,
                    Axis, part.position, track, project, Resampler, XsyAvailable);
            }
            PhraseGroups = groups;
        }

        /// <summary>
        /// Copies the part's phrase-build inputs on the UI thread and groups the
        /// phonemes into phrases; the heavy build is deferred to
        /// <see cref="BuildPhrases"/>.
        /// </summary>
        public static PhraseSource FromPart(UProject project, UTrack track, UVoicePart part,
                long generation) {
            var phonemes = part.phonemes
                .Where(phoneme => !phoneme.Error)
                .ToList();
            if (phonemes.Count == 0) {
                return null;
            }
            var renderer = track.RendererSettings.Renderer;
            var groups = new List<(int, int)>();
            int start = 0;
            for (int i = 1; i < phonemes.Count; ++i) {
                // A gap normally starts a new phrase, but the renderer may ask
                // to keep adjacent phrases together when their padded audio
                // would overlap (e.g. DiffSinger input padding).
                if (phonemes[i - 1].End != phonemes[i].position
                    && !renderer.ShouldMergePhrases(project, track, phonemes[i - 1], phonemes[i])) {
                    groups.Add((start, i));
                    start = i;
                }
            }
            groups.Add((start, phonemes.Count));
            return new PhraseSource(
                part.Id,
                DocManager.Inst.Revision,
                generation, project, track, part, phonemes,
                groups.Select(g => (g.Item1, g.Item2)).ToArray());
        }

        public RenderPhrase[] BuildPhrases() {
            var phrases = new RenderPhrase[PhraseGroups.Length];
            for (int i = 0; i < PhraseGroups.Length; ++i) {
                phrases[i] = new RenderPhrase(this, PhraseGroups[i].Start, PhraseGroups[i].End);
            }
            return phrases;
        }
    }
}
