using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public abstract class NoteCommand : UCommand {
        public readonly UNote[] Notes;
        public readonly UVoicePart Part;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
        };
        public NoteCommand(UVoicePart part, UNote note) {
            Part = part;
            Notes = new UNote[] { note };
        }
        public NoteCommand(UVoicePart part, IEnumerable<UNote> notes) {
            Part = part;
            Notes = notes.ToArray();
        }
    }

    public class AddNoteCommand : NoteCommand {
        readonly int NewPartDuration;
        readonly int OldPartDuration;
        public AddNoteCommand(UVoicePart part, UNote note) : base(part, note) {
            OldPartDuration = part.Duration;
            DocManager.Inst.Project.timeAxis.TickPosToBarBeat(note.End - 1, out int bar, out int beat, out int remainingTicks);
            int minDurTick = DocManager.Inst.Project.timeAxis.BarBeatToTickPos(bar + 2, 0) - part.position;
            if (part.Duration < minDurTick) {
                NewPartDuration = minDurTick;
            }
        }
        public AddNoteCommand(UVoicePart part, List<UNote> notes) : base(part, notes) {
            OldPartDuration = part.Duration;
            DocManager.Inst.Project.timeAxis.TickPosToBarBeat((Notes.LastOrDefault()?.End ?? 1) - 1, out int bar, out int beat, out int remainingTicks);
            int minDurTick = DocManager.Inst.Project.timeAxis.BarBeatToTickPos(bar + 2, 0) - part.position;
            if (part.Duration < minDurTick) {
                NewPartDuration = minDurTick;
            }
        }
        public override string ToString() { return "Add note"; }
        public override void Execute() {
            lock (Part) {
                foreach (var note in Notes) {
                    Part.notes.Add(note);
                }
                if (NewPartDuration > 0) {
                    Part.Duration = NewPartDuration;
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                foreach (var note in Notes) {
                    Part.notes.Remove(note);
                }
                Part.Duration = OldPartDuration;
            }
        }
    }

    public class RemoveNoteCommand : NoteCommand {
        public RemoveNoteCommand(UVoicePart part, UNote note) : base(part, note) { }
        public RemoveNoteCommand(UVoicePart part, List<UNote> notes) : base(part, notes) { }
        public override string ToString() { return "Remove note"; }
        public override void Execute() {
            lock (Part) {
                foreach (var note in Notes) {
                    Part.notes.Remove(note);
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                foreach (var note in Notes) {
                    Part.notes.Add(note);
                }
            }
        }
    }

    public class MoveNoteCommand : NoteCommand {
        readonly int DeltaPos, DeltaNoteNum;
        public MoveNoteCommand(UVoicePart part, UNote note, int deltaPos, int deltaNoteNum) : base(part, note) {
            DeltaPos = deltaPos;
            DeltaNoteNum = deltaNoteNum;
        }
        public MoveNoteCommand(UVoicePart part, List<UNote> notes, int deltaPos, int deltaNoteNum) : base(part, notes) {
            DeltaPos = deltaPos;
            DeltaNoteNum = deltaNoteNum;
        }
        public override string ToString() { return $"Move {Notes.Count()} notes"; }
        public override void Execute() {
            lock (Part) {
                foreach (UNote note in Notes) {
                    Part.notes.Remove(note);
                    note.position += DeltaPos;
                    note.tone += DeltaNoteNum;
                    Part.notes.Add(note);
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                foreach (UNote note in Notes) {
                    Part.notes.Remove(note);
                    note.position -= DeltaPos;
                    note.tone -= DeltaNoteNum;
                    Part.notes.Add(note);
                }
            }
        }
    }

    public class ResizeNoteCommand : NoteCommand {
        readonly int NewPartDuration;
        readonly int OldPartDuration;
        readonly int DeltaDur;
        public ResizeNoteCommand(UVoicePart part, UNote note, int deltaDur) : base(part, note) {
            DeltaDur = deltaDur;
            OldPartDuration = part.Duration;
            DocManager.Inst.Project.timeAxis.TickPosToBarBeat(note.End + deltaDur - 1, out int bar, out int beat, out int remainingTicks);
            int minDurTick = DocManager.Inst.Project.timeAxis.BarBeatToTickPos(bar + 2, 0) - part.position;
            if (part.Duration < minDurTick) {
                NewPartDuration = minDurTick;
            }
        }
        public ResizeNoteCommand(UVoicePart part, List<UNote> notes, int deltaDur) : base(part, notes) {
            DeltaDur = deltaDur;
            OldPartDuration = part.Duration;
            DocManager.Inst.Project.timeAxis.TickPosToBarBeat((Notes.LastOrDefault()?.End ?? 1) + deltaDur - 1, out int bar, out int beat, out int remainingTicks);
            int minDurTick = DocManager.Inst.Project.timeAxis.BarBeatToTickPos(bar + 2, 0) - part.position;
            if (part.Duration < minDurTick) {
                NewPartDuration = minDurTick;
            }
        }
        public override string ToString() { return $"Change {Notes.Count()} notes duration"; }
        public override void Execute() {
            lock (Part) {
                foreach (var note in Notes) {
                    note.duration += DeltaDur;
                }
                if (NewPartDuration > 0) {
                    Part.Duration = NewPartDuration;
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                foreach (var note in Notes) {
                    note.duration -= DeltaDur;
                }
                Part.Duration = OldPartDuration;
            }
        }
    }

    public class ChangeNoteLyricCommand : NoteCommand {
        readonly string[] NewLyrics;
        readonly string[] OldLyrics;
        public ChangeNoteLyricCommand(UVoicePart part, UNote note, string newLyric) : base(part, note) {
            NewLyrics = new string[] { newLyric };
            OldLyrics = new string[] { note.lyric };
        }
        public ChangeNoteLyricCommand(UVoicePart part, UNote[] notes, string[] newLyrics) : base(part, notes) {
            if (notes.Length != newLyrics.Length) {
                throw new ArgumentException($"notes count {notes.Length} and lyrics count {newLyrics.Length} does not match.");
            }
            NewLyrics = newLyrics;
            OldLyrics = notes.Select(note => note.lyric).ToArray();
        }
        public override string ToString() {
            return "Change notes lyric";
        }
        public override void Execute() {
            lock (Part) {
                for (var i = 0; i < Notes.Length; i++) {
                    var note = Notes[i];
                    note.lyric = NewLyrics[i];
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                for (var i = 0; i < Notes.Length; i++) {
                    var note = Notes[i];
                    note.lyric = OldLyrics[i];
                }
            }
        }
    }

    public class ChangeNoteTuningCommand : NoteCommand {
        readonly int[] NewTuning;
        readonly int[] OldTuning;
        public ChangeNoteTuningCommand(UVoicePart part, UNote note, int newTuning) : base(part, note) {
            NewTuning = new int[] { newTuning };
            OldTuning = new int[] { note.tuning };
        }
        public ChangeNoteTuningCommand(UVoicePart part, UNote[] notes, int[] newTuning) : base(part, notes) {
            if (notes.Length != newTuning.Length) {
                throw new ArgumentException($"notes count {notes.Length} and Tunings count {newTuning.Length} does not match.");
            }
            NewTuning = newTuning;
            OldTuning = notes.Select(note => note.tuning).ToArray();
        }
        public override string ToString() {
            return "Change notes Tuning";
        }
        public override void Execute() {
            lock (Part) {
                for (var i = 0; i < Notes.Length; i++) {
                    var note = Notes[i];
                    note.tuning = NewTuning[i];
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                for (var i = 0; i < Notes.Length; i++) {
                    var note = Notes[i];
                    note.tuning = OldTuning[i];
                }
            }
        }
    }

    public abstract class VibratoCommand : NoteCommand {
        public VibratoCommand(UVoicePart part, UNote note) : base(part, note) { }
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
            SkipPhoneme = true,
        };
    }

    public class SetVibratoCommand : VibratoCommand {
        readonly UNote note;
        readonly UVibrato newVibrato;
        readonly UVibrato oldVibrato;
        public SetVibratoCommand(UVoicePart part, UNote note, UVibrato vibrato) : base(part, note) {
            this.note = note;
            newVibrato = vibrato.Clone();
            oldVibrato = note.vibrato;
        }
        public override string ToString() {
            return "Change vibrato";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato = newVibrato;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato = oldVibrato;
            }
        }
    }

    public class VibratoLengthCommand : VibratoCommand {
        readonly UNote note;
        readonly float newLength;
        readonly float oldLength;
        public VibratoLengthCommand(UVoicePart part, UNote note, float length) : base(part, note) {
            this.note = note;
            newLength = length;
            oldLength = note.vibrato.length;
        }
        public override string ToString() {
            return "Change vibrato length";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.length = newLength;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.length = oldLength;
            }
        }
    }

    public class VibratoFadeInCommand : VibratoCommand {
        readonly UNote note;
        readonly float newFadeIn;
        readonly float oldFadeIn;
        public VibratoFadeInCommand(UVoicePart part, UNote note, float fadeIn) : base(part, note) {
            this.note = note;
            newFadeIn = fadeIn;
            oldFadeIn = note.vibrato.@in;

        }
        public override string ToString() {
            return "Change vibrato fade in";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.@in = newFadeIn;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.@in = oldFadeIn;
            }
        }
    }

    public class VibratoFadeOutCommand : VibratoCommand {
        readonly UNote note;
        readonly float newFadeOut;
        readonly float oldFadeOut;
        public VibratoFadeOutCommand(UVoicePart part, UNote note, float fadeOut) : base(part, note) {
            this.note = note;
            newFadeOut = fadeOut;
            oldFadeOut = note.vibrato.@out;
        }
        public override string ToString() {
            return "Change vibrato fade out";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.@out = newFadeOut;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.@out = oldFadeOut;
            }
        }
    }

    public class VibratoDepthCommand : VibratoCommand {
        readonly UNote note;
        readonly float newDepth;
        readonly float oldDepth;
        public VibratoDepthCommand(UVoicePart part, UNote note, float depth) : base(part, note) {
            this.note = note;
            newDepth = depth;
            oldDepth = note.vibrato.depth;
        }
        public override string ToString() {
            return "Change vibrato depth";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.depth = newDepth;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.depth = oldDepth;
            }
        }
    }

    public class VibratoPeriodCommand : VibratoCommand {
        readonly UNote note;
        readonly float newPeriod;
        readonly float oldPeriod;
        public VibratoPeriodCommand(UVoicePart part, UNote note, float period) : base(part, note) {
            this.note = note;
            newPeriod = period;
            oldPeriod = note.vibrato.period;
        }
        public override string ToString() {
            return "Change vibrato period";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.period = newPeriod;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.period = oldPeriod;
            }
        }
    }

    public class VibratoShiftCommand : VibratoCommand {
        readonly UNote note;
        readonly float newShift;
        readonly float oldShift;
        public VibratoShiftCommand(UVoicePart part, UNote note, float shift) : base(part, note) {
            this.note = note;
            newShift = shift;
            oldShift = note.vibrato.shift;
        }
        public override string ToString() {
            return "Change vibrato shift";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.shift = newShift;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.shift = oldShift;
            }
        }
    }

    public class VibratoDriftCommand : VibratoCommand {
        readonly UNote note;
        readonly float newDrift;
        readonly float oldDrift;
        public VibratoDriftCommand(UVoicePart part, UNote note, float drift) : base(part, note) {
            this.note = note;
            newDrift = drift;
            oldDrift = note.vibrato.drift;
        }
        public override string ToString() {
            return "Change vibrato drift";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.drift = newDrift;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.drift = oldDrift;
            }
        }
    }

    public class VibratoVolumeLinkCommand : VibratoCommand {
        readonly UNote note;
        readonly float newVolLink;
        readonly float oldVolLink;
        public VibratoVolumeLinkCommand(UVoicePart part, UNote note, float volLink) : base(part, note) {
            this.note = note;
            newVolLink = volLink;
            oldVolLink = note.vibrato.volLink;
        }
        public override string ToString() {
            return "Change vibrato volume link";
        }
        public override void Execute() {
            lock (Part) {
                note.vibrato.volLink = newVolLink;
            }
        }
        public override void Unexecute() {
            lock (Part) {
                note.vibrato.volLink = oldVolLink;
            }
        }
    }


    public class PhonemeOffsetCommand : NoteCommand {
        readonly UNote note;
        readonly int index;
        readonly int oldOffset;
        readonly int newOffset;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
        };
        public PhonemeOffsetCommand(UVoicePart part, UNote note, int index, int offset) : base(part, note) {
            this.note = note;
            this.index = index;
            var o = this.note.GetPhonemeOverride(index);
            oldOffset = o.offset ?? 0;
            newOffset = offset;
        }
        public override void Execute() {
            var o = note.GetPhonemeOverride(index);
            if (newOffset == 0) {
                o.offset = null;
            } else {
                o.offset = newOffset;
            }
        }
        public override void Unexecute() {
            var o = note.GetPhonemeOverride(index);
            if (oldOffset == 0) {
                o.offset = null;
            } else {
                o.offset = oldOffset;
            }
        }
        public override string ToString() => "Set phoneme offset";
    }

    public class PhonemePreutterCommand : NoteCommand {
        readonly UNote note;
        readonly int index;
        readonly float oldDelta;
        readonly float newDelta;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
        };
        public PhonemePreutterCommand(UVoicePart part, UNote note, int index, float delta) : base(part, note) {
            this.note = note;
            this.index = index;
            var o = this.note.GetPhonemeOverride(index);
            oldDelta = o.preutterDelta ?? 0;
            newDelta = delta;
        }
        public override void Execute() {
            var o = note.GetPhonemeOverride(index);
            o.preutterDelta = newDelta == 0 ? null : (float?)newDelta;
        }
        public override void Unexecute() {
            var o = note.GetPhonemeOverride(index);
            o.preutterDelta = oldDelta == 0 ? null : (float?)oldDelta;
        }
        public override string ToString() => "Set phoneme preutter";
    }

    public class PhonemeOverlapCommand : NoteCommand {
        readonly UNote note;
        readonly int index;
        readonly float oldDelta;
        readonly float newDelta;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
        };
        public PhonemeOverlapCommand(UVoicePart part, UNote note, int index, float delta) : base(part, note) {
            this.note = note;
            this.index = index;
            var o = this.note.GetPhonemeOverride(index);
            oldDelta = o.overlapDelta ?? 0;
            newDelta = delta;
        }
        public override void Execute() {
            var o = note.GetPhonemeOverride(index);
            o.overlapDelta = newDelta == 0 ? null : (float?)newDelta;
        }
        public override void Unexecute() {
            var o = note.GetPhonemeOverride(index);
            o.overlapDelta = oldDelta == 0 ? null : (float?)oldDelta;
        }
        public override string ToString() => "Set phoneme overlap";
    }

    public class ClearPhonemeTimingCommand : NoteCommand {
        readonly UNote note;
        readonly Tuple<int, int?, float?, float?>[] oldValues;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
        };
        public ClearPhonemeTimingCommand(UVoicePart part, UNote note) : base(part, note) {
            this.note = note;
            oldValues = note.phonemeOverrides
                .Select(o => Tuple.Create(o.index, o.offset, o.preutterDelta, o.overlapDelta))
                .ToArray();
        }

        public override void Execute() {
            foreach (var o in note.phonemeOverrides) {
                o.offset = null;
                o.preutterDelta = null;
                o.overlapDelta = null;
            }
        }
        public override void Unexecute() {
            foreach (var t in oldValues) {
                var o = note.GetPhonemeOverride(t.Item1);
                o.offset = t.Item2;
                o.preutterDelta = t.Item3;
                o.overlapDelta = t.Item4;
            }
        }
        public override string ToString() => "Clear phoneme timing";
    }

    public class ChangePhonemeAliasCommand : NoteCommand {
        readonly UNote note;
        readonly int index;
        readonly string oldAlias;
        readonly string? newAlias;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
        };
        public ChangePhonemeAliasCommand(UVoicePart part, UNote note, int index, string? alias) : base(part, note) {
            this.note = note;
            this.index = index;
            var o = this.note.GetPhonemeOverride(index);
            oldAlias = o.phoneme;
            newAlias = alias;
        }

        public override void Execute() {
            var o = note.GetPhonemeOverride(index);
            o.phoneme = string.IsNullOrWhiteSpace(newAlias) ? null : newAlias;
        }
        public override void Unexecute() {
            var o = note.GetPhonemeOverride(index);
            o.phoneme = string.IsNullOrWhiteSpace(oldAlias) ? null : oldAlias;
        }
        public override string ToString() => "Change phoneme alias";
    }
}
