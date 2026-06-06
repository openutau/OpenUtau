using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.Util;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public class UNote : IComparable {
        static readonly Regex phoneticHintPattern = new Regex(@"\[(.*)\]");

        /// <summary>
        /// Position of the note in ticks, relative to the beginning of the part.
        /// </summary>
        public int position;
        public int duration;
        public int tone;
        public string lyric = NotePresets.Default.DefaultLyric;
        public UPitch pitch;
        public UVibrato vibrato;
        public int tuning;
        [YamlMember(Alias = "phonemizer", ApplyNamingConventions = false)]
        public string? PhonemizerOverride { get; set; } = null;

        public List<UExpression> phonemeExpressions = new List<UExpression>();
        public List<UPhonemeOverride> phonemeOverrides = new List<UPhonemeOverride>();

        [YamlIgnore] public int End => position + duration;
        /// <summary>
        /// The final tone, taking tuning into consideration
        /// </summary>
        [YamlIgnore] public float AdjustedTone => tone + tuning / 100f;

        /// <summary>
        /// Position of the note in milliseconds, relative to the beginning of the project.
        /// </summary>
        [YamlIgnore] public double PositionMs { get; set; }
        [YamlIgnore] public double DurationMs => EndMs - PositionMs;
        [YamlIgnore] public double EndMs { get; set; }
        [YamlIgnore] public bool Selected { get; set; } = false;
        [YamlIgnore] public UNote? Prev { get; set; }
        [YamlIgnore] public UNote? Next { get; set; }
        [YamlIgnore] public UNote? Extends { get; set; }
        [YamlIgnore] public int ExtendedDuration { get; set; }
        [YamlIgnore] public int ExtendedEnd => position + ExtendedDuration;
        [YamlIgnore] public int LeftBound => position;
        [YamlIgnore] public int RightBound => position + duration;
        [YamlIgnore] public bool Error { get; set; } = false;
        [YamlIgnore] public bool OverlapError { get; set; } = false;
        [YamlIgnore] public List<UExpression> phonemizerExpressions = new List<UExpression>();
        [YamlIgnore] public int[] phonemeIndexes { get; set; } = new int[0];

        public static UNote Create() {
            var note = new UNote();
            note.pitch = new UPitch();
            note.vibrato = new UVibrato();
            return note;
        }

        public int CompareTo(object obj) {
            if (obj == null) return 1;

            if (!(obj is UNote other))
                throw new ArgumentException("CompareTo object is not a Note");

            if (position != other.position) {
                return position.CompareTo(other.position);
            }
            return GetHashCode().CompareTo(other.GetHashCode());
        }

        public override string ToString() {
            return $"\"{lyric}\" Pos:{position} Dur:{duration} Tone:{tone}{(Error ? " Error" : string.Empty)}{(Selected ? " Selected" : string.Empty)}";
        }

        public void AfterLoad(UProject project, UTrack track, UVoicePart part) {
            foreach (var exp in phonemeExpressions) {
                if (track.TryGetExpDescriptor(project, exp.abbr, out var descriptor)) {
                    exp.descriptor = descriptor;
                }
            }
            phonemeExpressions = phonemeExpressions.Where(exp => exp.descriptor != null).ToList();
        }

        public void BeforeSave(UProject project, UTrack track, UVoicePart part) {
            phonemeExpressions = phonemeExpressions
                .OrderBy(exp => exp.index)
                .ThenBy(exp => exp.abbr)
                .ToList();
        }

        public void Validate(ValidateOptions options, UProject project, UTrack track, UVoicePart part) {
            duration = Math.Max(10, duration);
            PositionMs = project.timeAxis.TickPosToMsPos(part.position + position);
            EndMs = project.timeAxis.TickPosToMsPos(part.position + End);
            if (Prev != null && Prev.End > position) {
                Error = true;
                OverlapError = true;
                return;
            }
            Error = false;
            OverlapError = false;
            if (track.Singer == null || !track.Singer.Found || !track.Singer.Loaded) {
                Error |= true;
            }
            if (pitch.snapFirst) {
                if (Prev != null && Prev.End == position) {
                    pitch.data[0].Y = (Prev.AdjustedTone - AdjustedTone) * 10;
                } else {
                    pitch.data[0].Y = 0;
                }
            }
        }

        static List<Phonemizer.PhonemeAttributes> attributesBuffer = new List<Phonemizer.PhonemeAttributes>();
        internal Phonemizer.Note ToPhonemizerNote(UTrack track, UPart part) {
            string lrc = lyric;
            string phoneticHint = null;
            lrc = phoneticHintPattern.Replace(lrc, match => {
                phoneticHint = match.Groups[1].Value;
                return "";
            });
            attributesBuffer.Clear();
            foreach (var exp in phonemeExpressions) {
                if (exp.abbr != Format.Ustx.VEL &&
                    exp.abbr != Format.Ustx.ALT &&
                    exp.abbr != Format.Ustx.CLR &&
                    exp.abbr != Format.Ustx.SHFT) {
                    continue;
                }
                var posInBuffer = attributesBuffer.FindIndex(attr => attr.index == exp.index);
                if (posInBuffer < 0) {
                    posInBuffer = attributesBuffer.Count;
                    attributesBuffer.Add(new Phonemizer.PhonemeAttributes());
                }
                Phonemizer.PhonemeAttributes attr = attributesBuffer[posInBuffer];
                attr.index = exp.index.Value;
                if (exp.abbr == Format.Ustx.VEL) {
                    attr.consonantStretchRatio = Math.Pow(2, 1.0 - exp.value / 100.0);
                } else if (exp.abbr == Format.Ustx.ALT) {
                    attr.alternate = (int)exp.value;
                } else if (exp.abbr == Format.Ustx.CLR && track.VoiceColorExp != null) {
                    int optionIdx = (int)exp.value;
                    if (optionIdx < track.VoiceColorExp.options.Length && optionIdx >= 0) {
                        attr.voiceColor = track.VoiceColorExp.options[optionIdx];
                    }
                } else if (exp.abbr == Format.Ustx.SHFT) {
                    attr.toneShift = (int)exp.value;
                }
                attributesBuffer[posInBuffer] = attr;
            }
            var attributes = attributesBuffer.ToArray();
            attributesBuffer.Clear();
            return new Phonemizer.Note() {
                lyric = lrc.Trim(),
                phoneticHint = phoneticHint?.Trim(),
                tone = tone,
                position = part.position + position,
                duration = duration,
                phonemeAttributes = attributes,
            };
        }

        public UPhonemeOverride GetPhonemeOverride(int index) {
            var result = phonemeOverrides.Find(o => o.index == index);
            if (result == null) {
                result = new UPhonemeOverride { index = index };
                phonemeOverrides.Add(result);
            }
            return result;
        }

        public List<Tuple<float, bool>> GetExpression(UProject project, UTrack track, string abbr) {
            track.TryGetExpDescriptor(project, abbr, out var descriptor);
            var list = new List<Tuple<float, bool>>();

            if (phonemeIndexes != null && phonemeIndexes.Length > 0) {
                int indexes = phonemeIndexes.LastOrDefault() + 1;
                for (int i = 0; i < indexes; i++) {
                    var phonemeExp = phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == abbr && exp.index == i);
                    if (phonemeExp != null) {
                        list.Add(Tuple.Create(phonemeExp.value, true));
                    } else {
                        var phonemizerExp = phonemizerExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == abbr && exp.index == i);
                        if (phonemizerExp != null) {
                            list.Add(Tuple.Create(phonemizerExp.value, false));
                        } else {
                            list.Add(Tuple.Create(descriptor.CustomDefaultValue, false));
                        }
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Returns value if phoneme has expression, null otherwise.
        /// </summary>
        public float?[] GetExpressionNoteHas(UProject project, UTrack track, string abbr) {
            var list = new List<float?>();

            if (phonemeIndexes != null && phonemeIndexes.Length > 0) {
                int indexes = phonemeIndexes.LastOrDefault() + 1;
                UExpression? phonemeExp = null;

                for (int i = 0; i < indexes; i++) {
                    if (phonemeIndexes.Contains(i)) {
                        phonemeExp = phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == abbr && exp.index == i);
                    }
                    if (phonemeExp != null) {
                        list.Add(phonemeExp.value);
                    } else {
                        list.Add(null);
                    }
                }
            }
            return list.ToArray();
        }

        public void SetExpression(UProject project, UTrack track, string abbr, float?[] values) {
            if (!track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                return;
            }
            if (values.Length == 0) {
                return;
            }

            phonemeExpressions.RemoveAll(exp => exp.descriptor?.abbr == abbr);

            int indexes = phonemeIndexes.LastOrDefault() + 1;
            for (int i = 0; i < indexes; i++) {
                if (i == 0 || phonemeIndexes.Contains(i)) {
                    float? value;
                    if (values.Length > i) {
                        value = values[i];
                    } else {
                        value = values.Last();
                    }
                    if (value == null) {
                        continue;
                    }

                    phonemeExpressions.Add(new UExpression(descriptor) {
                        index = i,
                        value = (float)value,
                    });
                }
            }
        }

        public UNote Clone() {
            return new UNote() {
                position = position,
                duration = duration,
                tone = tone,
                lyric = lyric,
                pitch = pitch.Clone(),
                vibrato = vibrato.Clone(),
                tuning = tuning,
                PhonemizerOverride = PhonemizerOverride,
                phonemeExpressions = phonemeExpressions.Select(exp => exp.Clone()).ToList(),
                phonemeOverrides = phonemeOverrides.Select(o => o.Clone()).ToList(),
                phonemeIndexes = (int[])phonemeIndexes.Clone()
            };
        }
    }
    public class ChangeNotePhonemizerCommand : NoteCommand {
        public readonly string? newOverride;
        public readonly string? oldOverride;
        public readonly UNote _note;

        public ChangeNotePhonemizerCommand(UVoicePart part, UNote note, string? newOverride) : base(part, note) {
            this._note = note;
            this.newOverride = newOverride;
            this.oldOverride = note.PhonemizerOverride;
        }
        public override string ToString() => "Change note phonemizer";
        public override void Execute() {
            _note.PhonemizerOverride = newOverride;
        }
        public override void Unexecute() {
            _note.PhonemizerOverride = oldOverride;
        }
    }
    public class UVibrato {
        // Vibrato percentage of note length.
        float _length;
        // Period in milliseconds.
        float _period = NotePresets.Default.DefaultVibrato.VibratoPeriod;
        // Depth in cents (1 semitone = 100 cents).
        float _depth = NotePresets.Default.DefaultVibrato.VibratoDepth;
        // Fade-in percentage of vibrato length.
        float _in = NotePresets.Default.DefaultVibrato.VibratoIn;
        // Fade-out percentage of vibrato length.
        float _out = NotePresets.Default.DefaultVibrato.VibratoOut;
        // Shift percentage of period length.
        float _shift = NotePresets.Default.DefaultVibrato.VibratoShift;
        // Shift the whole vibrato up and down.
        float _drift = NotePresets.Default.DefaultVibrato.VibratoDrift;
        // Percentage of volume reduction in linkage with vibrato. When this is 100%, volume will be 1.2 times to 0.2 times regardless of depth.
        float _volLink = NotePresets.Default.DefaultVibrato.VibratoVolLink;

        public float length { get => _length; set => _length = Math.Max(0, Math.Min(100, value)); }
        public float period { get => _period; set => _period = Math.Max(5, Math.Min(500, value)); }
        public float depth { get => _depth; set => _depth = Math.Max(5, Math.Min(200, value)); }

        public float @in {
            get => _in;
            set {
                _in = Math.Max(0, Math.Min(100, value));
                _out = Math.Min(_out, 100 - _in);
            }
        }

        public float @out {
            get => _out;
            set {
                _out = Math.Max(0, Math.Min(100, value));
                _in = Math.Min(_in, 100 - _out);
            }
        }
        public float shift { get => _shift; set => _shift = Math.Max(0, Math.Min(100, value)); }
        public float drift { get => _drift; set => _drift = Math.Max(-100, Math.Min(100, value)); }
        public float volLink { get => _volLink; set => _volLink = Math.Max(-100, Math.Min(100, value)); }

        [YamlIgnore] public float NormalizedStart => 1f - length / 100f;

        public UVibrato Clone() {
            var result = new UVibrato {
                length = length,
                period = period,
                depth = depth,
                @in = @in,
                @out = @out,
                shift = shift,
                drift = drift,
                volLink = volLink
            };
            return result;
        }

        public UVibrato Split(int offset) {
            // TODO
            return Clone();
        }

        /// <summary>
        /// Evaluate a position on the vibrato curve.
        /// </summary>
        /// <param name="nPos">Normalized position in note length.</param>
        /// <returns>Vector2(tick, noteNum)</returns>
        public Vector2 Evaluate(float nPos, float nPeriod, UNote note) {
            float nStart = NormalizedStart;
            float nIn = length / 100f * @in / 100f;
            float nInPos = nStart + nIn;
            float nOut = length / 100f * @out / 100f;
            float nOutPos = 1f - nOut;
            float t = (nPos - nStart) / nPeriod + shift / 100f;
            float y = (float)Math.Sin(2 * Math.PI * t) * depth + (depth / 100 * drift);
            if (nPos < nStart) {
                y = 0;
            } else if (nPos < nInPos) {
                y *= (nPos - nStart) / nIn;
            } else if (nPos > nOutPos) {
                y *= (1f - nPos) / nOut;
            }
            return new Vector2(note.position + note.duration * nPos, note.AdjustedTone + y / 100f);
        }
        /// <summary>
        /// Evaluate the volume of the position on the vibrato curve.
        /// </summary>
        public float EvaluateVolume(float nPos, float nPeriod) {
            float nStart = NormalizedStart;
            float nIn = length / 100f * @in / 100f;
            float nInPos = nStart + nIn;
            float nOut = length / 100f * @out / 100f;
            float nOutPos = 1f - nOut;
            float shift = this.shift;
            float volLink = this.volLink;
            if (volLink < 0) {
                shift += 50;
                if (shift > 100) {
                    shift -= 100;
                }
                volLink *= -1;
            }
            float t = (nPos - nStart) / nPeriod + shift / 100f;
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

        public Vector2 GetEnvelopeStart(UNote note) {
            return new Vector2(
                note.position + note.duration * NormalizedStart,
                note.tone - 3f);
        }

        public Vector2 GetEnvelopeFadeIn(UNote note) {
            return new Vector2(
                note.position + note.duration * (NormalizedStart + length / 100f * @in / 100f),
                note.tone - 3f + depth / 50f);
        }

        public Vector2 GetEnvelopeFadeOut(UNote note) {
            return new Vector2(
                note.position + note.duration * (1f - length / 100f * @out / 100f),
                note.tone - 3f + depth / 50f);
        }

        public Vector2 GetEnvelopeEnd(UNote note) {
            return new Vector2(
                note.position + note.duration,
                note.tone - 3f);
        }

        public Vector2 GetToggle(UNote note) {
            return new Vector2(note.position + note.duration, note.tone - 1.5f);
        }

        public void GetPeriodStartEnd(UProject project, UNote note, out Vector2 start, out Vector2 end) {
            float periodTick = project.timeAxis.TicksBetweenMsPos(note.PositionMs, note.PositionMs + period);
            float shiftTick = periodTick * shift / 100f;
            start = new Vector2(
                note.position + note.duration * NormalizedStart + shiftTick,
                note.tone - 3.5f);
            end = new Vector2(
                note.position + note.duration * NormalizedStart + shiftTick + periodTick,
                note.tone - 3.5f);
        }

        public float ToneToDepth(UNote note, float tone) {
            return (tone - (note.tone - 3f)) * 50f;
        }
    }

    public enum PitchPointShape {
        /// <summary>
        /// SineInOut
        /// </summary>
        io,
        /// <summary>
        /// Linear
        /// </summary>
        l,
        /// <summary>
        /// SineIn
        /// </summary>
        i,
        /// <summary>
        /// SineOut
        /// </summary>
        o,
        /// <summary>
        /// Spline
        /// </summary>
        sp
    };

    public class PitchPoint : IComparable<PitchPoint> {
        /// <summary>
        /// Position relative to the beginning of the note in milliseconds.
        /// </summary>
        public float X;

        /// <summary>
        /// Pitch relative to the tone of the note in 0.1 semi-tones.
        /// </summary>
        public float Y;
        public PitchPointShape shape;
        [YamlIgnore] public bool autoCompleted = false;

        public PitchPoint() { }

        public PitchPoint(float x, float y, PitchPointShape shape = PitchPointShape.io, bool autoCompleted = false) {
            X = x;
            Y = y;
            this.shape = shape;
            this.autoCompleted = autoCompleted;
        }

        public PitchPoint Clone() {
            return new PitchPoint(X, Y, shape);
        }

        public int CompareTo(PitchPoint other) { return X.CompareTo(other.X); }
        public override string ToString() {
            return $"x: {X}, y: {Y}, shape: {shape}";
        }
    }

    public class UPitch {
        public List<PitchPoint> data = new List<PitchPoint>();
        public bool snapFirst = true;

        public void AddPoint(PitchPoint p) {
            data.Add(p);
            data.Sort();
        }

        public void RemovePoint(PitchPoint p) {
            data.Remove(p);
        }

        public UPitch Clone() {
            var result = new UPitch() {
                data = data.Select(p => p.Clone()).ToList(),
                snapFirst = snapFirst,
            };
            return result;
        }

        public double? Sample(UProject project, UPart part, UNote note, double tick) {
            for (int i = 0; i < note.pitch.data.Count - 1; i++) {
                var p1 = note.pitch.data[i];
                int t1 = project.timeAxis.MsPosToTickPos(note.PositionMs + p1.X) - part.position;
                var p2 = note.pitch.data[i + 1];
                int t2 = project.timeAxis.MsPosToTickPos(note.PositionMs + p2.X) - part.position;
                if (t1 <= tick && tick <= t2) {
                    return MusicMath.InterpolateShape(
                        t1, t2, p1.Y, p2.Y, tick, p1.shape) * 10;
                }
            }
            var pFirst = note.pitch.data.First();
            var tFirst = project.timeAxis.MsPosToTickPos(note.PositionMs + pFirst.X) - part.position;
            if (tick < tFirst) {
                return pFirst.Y * 10;
            }
            var pLast = note.pitch.data.Last();
            var tLast = project.timeAxis.MsPosToTickPos(note.PositionMs + pLast.X) - part.position;
            if (tick > tLast) {
                return pLast.Y * 10;
            }
            return null;
        }
    }
}
