using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public class UPhoneme {
        public int rawPosition;
        public string rawPhoneme = "a";
        public int index;

        public int position { get; set; }
        public string phoneme { get; set; }
        public string phonemeMapped { get; private set; }
        public UEnvelope envelope { get; private set; } = new UEnvelope();
        public UOto oto { get; private set; }
        public double preutter { get; private set; }
        public double overlap { get; private set; }
        public double autoPreutter { get; private set; }
        public double autoOverlap { get; private set; }
        public bool overlapped { get; private set; }
        public double tailIntrude { get; private set; }
        public double tailOverlap { get; private set; }
        public double? preutterDelta { get; set; }
        public double? overlapDelta { get; set; }

        public UNote Parent { get; set; }
        public int Duration { get; private set; }
        public int End { get { return position + Duration; } }
        public double PositionMs { get; private set; }
        public double DurationMs => EndMs - PositionMs;
        public double EndMs { get; private set; }
        public UPhoneme Prev { get; set; }
        public UPhoneme Next { get; set; }
        public bool Error { get; set; } = false;
        public Exception? ErrorException { get; set; }

        public override string ToString() => $"\"{phoneme}\" pos:{position}";

        public UPhoneme Clone() {
            return new UPhoneme() {
                position = position,
                phoneme = phoneme,
            };
        }

        public void Validate(ValidateOptions options, UProject project, UTrack track, UVoicePart part, UNote note) {
            Error = note.Error;
            ValidateDuration(project, part);
            if (ErrorException != null) {
                Error = true;
            }
            ValidateOto(track, note);
            ValidateOverlap(project, track, part, note);
            ValidateEnvelope(project, track, note);
        }

        void ValidateDuration(UProject project, UVoicePart part) {
            if (Error) {
                return;
            }
            var leadingNote = Parent.Extends ?? Parent;
            Duration = leadingNote.ExtendedEnd - position;
            if (Next != null && Parent != null) {
                if (Next.Parent == Parent.Next && Parent.End == Next.Parent.position) {
                    Duration = int.MaxValue;
                }
                Duration = Math.Min(Duration, Next.position - position);
            }
            PositionMs = project.timeAxis.TickPosToMsPos(part.position + position);
            EndMs = project.timeAxis.TickPosToMsPos(part.position + End);
            Error = Duration <= 0;
            if (Error) {
                ErrorException ??= new Exception("Phoneme duration is not positive.");
            }
        }

        void ValidateOto(UTrack track, UNote note) {
            phonemeMapped = string.Empty;
            if (Error) {
                return;
            }
            if (track.Singer == null || !track.Singer.Found || !track.Singer.Loaded) {
                Error = true;
                ErrorException ??= new Exception("Singer is not loaded.");
                return;
            }
            // Load oto.
            if (track.Singer.TryGetOto(phoneme, out var oto)) {
                this.oto = oto;
                Error = false;
                phonemeMapped = oto.Alias;
            } else {
                this.oto = default;
                Error = true;
                ErrorException ??= new Exception($"Oto not found for \"{phoneme}\".");
                phonemeMapped = string.Empty;
            }
        }

        void ValidateOverlap(UProject project, UTrack track, UPart part, UNote note) {
            if (Error) {
                return;
            }
            double consonantStretch = Math.Pow(2f, 1.0f - GetExpression(project, track, Format.Ustx.VEL).Item1 / 100f);
            autoOverlap = oto.Overlap * consonantStretch;
            autoPreutter = oto.Preutter * consonantStretch;
            overlapped = false;
            tailIntrude = 0;
            tailOverlap = 0;

            if (Prev != null) {
                double gapMs = PositionMs - Prev.EndMs;
                double prevDur = Prev.DurationMs;
                double maxPreutter = autoPreutter;
                if (gapMs <= 0) {
                    overlapped = true;
                    if (autoPreutter - autoOverlap > prevDur * 0.5f) {
                        maxPreutter = prevDur * 0.5f / (autoPreutter - autoOverlap) * autoPreutter;
                    }
                } else if (gapMs < autoPreutter) {
                    maxPreutter = gapMs;
                }
                if (autoPreutter > maxPreutter) {
                    double ratio = maxPreutter / autoPreutter;
                    autoPreutter = maxPreutter;
                    autoOverlap *= ratio;
                }
                if (autoPreutter > prevDur && overlapped) {
                    double delta = autoPreutter - prevDur;
                    autoPreutter = prevDur; // Ensure autoPreutter doesn't exceed 100% of prevDur
                    autoOverlap -= delta;
                }
            }
            preutter = Math.Max(0, autoPreutter + (preutterDelta ?? 0));
            overlap = autoOverlap + (overlapDelta ?? 0);
            if (Prev != null) {
                Prev.tailIntrude = overlapped ? Math.Max(preutter, preutter - overlap) : 0;
                Prev.tailOverlap = overlapped ? Math.Max(overlap, 0) : 0;
                Prev.ValidateEnvelope(project, track, Prev.Parent);
            }
        }

        void ValidateEnvelope(UProject project, UTrack track, UNote note) {
            if (Error) {
                return;
            }
            var vol = GetExpression(project, track, Format.Ustx.VOL).Item1;
            var atk = GetExpression(project, track, Format.Ustx.ATK).Item1;
            var dec = GetExpression(project, track, Format.Ustx.DEC).Item1;

            Vector2 p0, p1, p2, p3, p4;
            p0.X = (float)-preutter;
            p1.X = (float)(p0.X + (!overlapped && overlapDelta == null ? 5f : Math.Max(overlap, 5f)));
            p2.X = Math.Max(0f, p1.X);
            p3.X = (float)(DurationMs - tailIntrude);
            p4.X = (float)(p3.X + tailOverlap);
            if (p3.X == p4.X) {
                p3.X = Math.Max(p2.X, p3.X - 35f);
            }

            p0.Y = 0f;
            p1.Y = vol;
            p1.Y = atk * vol / 100f;
            p2.Y = vol;
            p3.Y = vol * (1f - dec / 100f);
            p4.Y = 0f;

            envelope.data[0] = p0;
            envelope.data[1] = p1;
            envelope.data[2] = p2;
            envelope.data[3] = p3;
            envelope.data[4] = p4;
        }

        /// <summary>
        /// If the phoneme does not have the corresponding expression, return the track's expression and false
        /// <summary>
        public Tuple<float, bool> GetExpression(UProject project, UTrack track, string abbr) {
            track.TryGetExpDescriptor(project, abbr, out var descriptor);
            var note = Parent.Extends ?? Parent;
            var phonemeExp = note.phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == abbr && exp.index == index);
            if (phonemeExp != null) {
                return Tuple.Create(phonemeExp.value, true);
            } else {
                var phonemizerExp = note.phonemizerExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == abbr && exp.index == index);
                if (phonemizerExp != null) {
                    return Tuple.Create(phonemizerExp.value, false);
                } else {
                    return Tuple.Create(descriptor.CustomDefaultValue, false);
                }
            }
        }

        public void SetExpression(UProject project, UTrack track, string abbr, float? value) {
            if (!track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                return;
            }
            var note = Parent.Extends ?? Parent;
            if (value == null) {
                note.phonemeExpressions.RemoveAll(exp => exp.descriptor?.abbr == abbr && exp.index == index || (exp.index != null && !note.phonemeIndexes.Contains((int)exp.index)));
            } else {
                var phonemeExp = note.phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == abbr && exp.index == index);
                if (phonemeExp != null) {
                    phonemeExp.descriptor = descriptor;
                    phonemeExp.value = (float)value;
                } else {
                    note.phonemeExpressions.Add(new UExpression(descriptor) {
                        index = index,
                        value = (float)value,
                    });
                }
            }
        }

        public Tuple<string, int?, string>[] GetResamplerFlags(UProject project, UTrack track) {
            var flags = new List<Tuple<string, int?, string>>();
            var expressions = new List<UExpressionDescriptor>();
            expressions.AddRange(project.expressions.Values);
            expressions.RemoveAll(exp => track.TrackExpressions.Any(te => te.abbr == exp.abbr));
            expressions.AddRange(track.TrackExpressions);
            foreach (var descriptor in expressions) {
                if (descriptor.type == UExpressionType.Numerical) {
                    if (!string.IsNullOrEmpty(descriptor.flag)) {
                        int value = (int)GetExpression(project, track, descriptor.abbr).Item1;
                        if (descriptor.skipOutputIfDefault && value == (int)descriptor.defaultValue) {
                            continue;
                        }
                        flags.Add(Tuple.Create<string, int?, string>(descriptor.flag, value, descriptor.abbr));
                    }
                } else if (descriptor.type == UExpressionType.Options) {
                    if (descriptor.isFlag) {
                        int value = (int)GetExpression(project, track, descriptor.abbr).Item1;
                        flags.Add(Tuple.Create<string, int?, string>(descriptor.options[value], null, descriptor.abbr));
                    }
                }
            }
            return flags.ToArray();
        }

        public string GetVoiceColor(UProject project, UTrack track) {
            if (track.VoiceColorExp == null) {
                return null;
            }
            int index = (int)GetExpression(project, track, Format.Ustx.CLR).Item1;
            if (index < 0 || index >= track.VoiceColorExp.options.Length) {
                return null;
            }
            return track.VoiceColorExp.options[index];
        }
    }

    public class UEnvelope {
        public List<Vector2> data = new List<Vector2>();

        public UEnvelope() {
            data.Add(new Vector2(0, 0));
            data.Add(new Vector2(0, 100));
            data.Add(new Vector2(0, 100));
            data.Add(new Vector2(0, 100));
            data.Add(new Vector2(0, 0));
        }
    }

    public class UPhonemeOverride {
        public int index;
        public string? phoneme;
        public int? offset;
        public float? preutterDelta;
        public float? overlapDelta;

        [YamlIgnore]
        public bool IsEmpty => string.IsNullOrWhiteSpace(phoneme) && !offset.HasValue
            && !preutterDelta.HasValue && !overlapDelta.HasValue;

        public UPhonemeOverride Clone() {
            return new UPhonemeOverride() {
                index = index,
                phoneme = phoneme,
                offset = offset,
                preutterDelta = preutterDelta,
                overlapDelta = overlapDelta,
            };
        }
    }
}
