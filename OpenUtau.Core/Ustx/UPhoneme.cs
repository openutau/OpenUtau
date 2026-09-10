using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
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
        public double maxOtoPreutter { get; private set; }
        public bool adjacent { get; private set; }
        public bool overlapped { get; private set; }
        public double tailIntrude { get; private set; }
        public double tailOverlap { get; private set; }
        public double? preutterDelta { get; set; }
        public double? overlapDelta { get; set; }
        public double? attackTimeDelta { get; set; }
        public double? releaseTimeDelta { get; set; }
        public bool crossfade { get; set; } = true; //Todo

        public UNote Parent { get; set; }
        public int Duration { get; private set; }
        public int End => position + Duration;
        public double PositionMs { get; private set; }
        public double DurationMs => EndMs - PositionMs;
        public double EndMs { get; private set; }
        public UPhoneme Prev { get; set; }
        public UPhoneme Next { get; set; }
        public bool Error { get; set; } = false;
        public Exception? ErrorException { get; set; }
        Exception? durationErrorException;

        public override string ToString() => $"\"{phoneme}\" pos:{position}";

        public UPhoneme Clone() {
            return new UPhoneme() {
                position = position,
                phoneme = phoneme,
            };
        }

        public void Validate(ValidateOptions options, UProject project, UTrack track, UVoicePart part, UNote note) {
            Error = note.Error;
            ValidateOto(track, note);
            ValidateDuration(project, track, part);
            if (ErrorException != null) {
                Error = true;
            }
            ValidateOverlap(project, track, part, note);
            ValidateEnvelope(project, track, note);
        }

        void ValidateDuration(UProject project, UTrack track, UVoicePart part) {
            if (Error) {
                return;
            }
            var leadingNote = Parent.Extends ?? Parent;
            bool hasNextInSameNote = Next != null && (Next.Parent == Parent || (Next.Parent?.Extends ?? Next.Parent) == leadingNote);
            bool clump = !Util.Preferences.Default.ExtendEndingPhonemes;

            if (hasNextInSameNote || clump) {
                Duration = leadingNote.ExtendedEnd - position;
                if (Next != null) {
                    Duration = Math.Min(Duration, Next.position - position);
                }
            } else {
                double consonantStretch = Math.Pow(2.0, 1.0 - GetExpression(project, track, Format.Ustx.VEL).Item1 / 100.0);
                double tailMs = 5.0;
                if (!string.IsNullOrEmpty(phonemeMapped) && oto.Consonant > 0) {
                    if (oto.Cutoff < 0) {
                        double effectiveLength = Math.Abs(oto.Cutoff);
                        tailMs = Math.Max(35.0, (effectiveLength - oto.Preutter) * consonantStretch);
                    } else {
                        tailMs = Math.Max(35.0, (oto.Consonant - oto.Preutter) * consonantStretch);
                    }
                } else {
                    tailMs *= consonantStretch;
                }

                int tailTicks = project.timeAxis.MsPosToTickPos(tailMs) - project.timeAxis.MsPosToTickPos(0);
                int naturalEnd = Math.Max(leadingNote.ExtendedEnd, position + tailTicks);

                if (Next != null) {
                    naturalEnd = Math.Min(naturalEnd, Next.position);
                }

                Duration = naturalEnd - position;
            }

            PositionMs = project.timeAxis.TickPosToMsPos(part.position + position);
            EndMs = project.timeAxis.TickPosToMsPos(part.position + End);
            Error = Duration <= 0;
            if (Error) {
                // The exception is remembered so it does not flicker between
                // validates, and Validate() keeps the error visible until it
                // clears again.
                durationErrorException ??= new Exception("Phoneme duration is not positive.");
                ErrorException ??= durationErrorException;
            } else if (ReferenceEquals(ErrorException, durationErrorException)) {
                // Duration is valid again (e.g. the phoneme offset override was
                // moved back), so clear the stale duration error. A phonemizer
                // response error stored in ErrorException is left untouched.
                durationErrorException = null;
                ErrorException = null;
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
            autoPreutter = maxOtoPreutter = oto.Preutter * consonantStretch;
            adjacent = false;
            tailIntrude = 0;
            tailOverlap = 0;

            if (Prev != null) {
                double gapMs = PositionMs - Prev.EndMs;
                double prevDur = Prev.DurationMs;
                double maxPreutter = autoPreutter;
                if (gapMs <= 0) { // Adjacent to the previous note
                    adjacent = true;
                    if (autoOverlap > 0) {
                        if (autoPreutter - autoOverlap > prevDur * 0.5f) {
                            maxPreutter = prevDur * 0.5f / (autoPreutter - autoOverlap) * autoPreutter;
                        }
                    }
                    maxPreutter = Math.Min(maxPreutter, prevDur);
                    if (Prev.preutter < 5) {
                        maxPreutter = Math.Min(maxPreutter, prevDur + Prev.preutter - 5);
                    }
                } else if (gapMs < autoPreutter) { // There is a small gap between the previous note and this one
                    maxPreutter = gapMs;
                }
                if (autoPreutter > maxPreutter) {
                    double ratio = autoPreutter > 0 ? maxPreutter / autoPreutter : 0d;
                    autoPreutter = maxPreutter;
                    autoOverlap *= ratio;
                }
                if (autoOverlap < 0) {
                    autoOverlap = Math.Max(autoOverlap, Math.Min(0, 35 - prevDur + autoPreutter));
                }
            }
            preutter = Math.Max(0, autoPreutter + (preutterDelta ?? 0));
            overlap = autoOverlap + (overlapDelta ?? 0);
            if (Prev != null) {
                if (Prev.DurationMs - preutter < 5) {
                    var minOverlap = 5 - (Prev.DurationMs - preutter);
                    overlap = Math.Max(overlap, minOverlap);
                }
                Prev.tailIntrude = adjacent ? Math.Max(preutter, preutter - overlap) : 0;
                Prev.tailOverlap = adjacent ? Math.Max(overlap, 0) : 0;
                overlapped = adjacent && overlap > 0;
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
            p1.X = (float)Math.Max(p0.X + 5, p0.X + GetFadeIn() + (attackTimeDelta ?? 0));
            p2.X = Math.Max(0f, p1.X);

            // If Next == null (ending into rest), allow p4 to extend dynamically past the zero mark
            p4.X = (float)(DurationMs - tailIntrude + tailOverlap);
            p3.X = (float)Math.Max(p2.X, p4.X - GetFadeOut() - (releaseTimeDelta ?? 0));

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

        public double GetFadeIn() {
            if (!crossfade || !overlapped) {
                return 5;
            } else {
                return overlap;
            }
        }

        public double GetFadeOut() {
            if (Next == null || !Next.crossfade || !Next.overlapped) {
                return 35;
            } else {
                return tailOverlap;
            }
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

        public string GetVoiceColor2(UProject project, UTrack track) {
            if (track.VoiceColor2Exp == null) {
                return null;
            }
            int index = (int)GetExpression(project, track, Format.Ustx.CLRY).Item1;
            if (index < 0 || index >= track.VoiceColor2Exp.options.Length) {
                return null;
            }
            return track.VoiceColor2Exp.options[index];
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
        public float? attackTimeDelta;
        public float? releaseTimeDelta;

        [YamlIgnore]
        public bool IsEmpty => string.IsNullOrWhiteSpace(phoneme) && !offset.HasValue
            && !preutterDelta.HasValue && !overlapDelta.HasValue && !attackTimeDelta.HasValue && !releaseTimeDelta.HasValue;

        public UPhonemeOverride Clone() {
            return new UPhonemeOverride() {
                index = index,
                phoneme = phoneme,
                offset = offset,
                preutterDelta = preutterDelta,
                overlapDelta = overlapDelta,
                attackTimeDelta = attackTimeDelta,
                releaseTimeDelta = releaseTimeDelta,
            };
        }
    }
}
