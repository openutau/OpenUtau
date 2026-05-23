using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core {
    public abstract class ExpCommand : UCommand {
        public UVoicePart Part;
        public UNote Note;
        public string Key;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = true,
            };
        public ExpCommand(UVoicePart part) {
            Part = part;
        }
    }

    public class SetNoteExpressionCommand : ExpCommand {
        static readonly HashSet<string> needsPhonemizer = new HashSet<string> {
            Format.Ustx.ALT, Format.Ustx.CLR, Format.Ustx.SHFT, Format.Ustx.VEL
        };

        public readonly UProject project;
        public readonly UTrack track;
        public readonly float?[] newValue;
        public readonly float?[] oldValue;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = !needsPhonemizer.Contains(Key),
            };
        public SetNoteExpressionCommand(UProject project, UTrack track, UVoicePart part, UNote note, string abbr, float?[] values) : base(part) {
            this.project = project;
            this.track = track;
            this.Note = note;
            Key = abbr;
            newValue = values;
            oldValue = note.GetExpressionNoteHas(project, track, abbr);
        }
        public override string ToString() => $"Set note expression {Key}";
        public override void Execute() => Note.SetExpression(project, track, Key, newValue);
        public override void Unexecute() => Note.SetExpression(project, track, Key, oldValue);
    }

    public class SetNotesSameExpressionCommand : ExpCommand {
        static readonly HashSet<string> needsPhonemizer = new HashSet<string> {
            Format.Ustx.ALT, Format.Ustx.CLR, Format.Ustx.SHFT, Format.Ustx.VEL
        };

        public readonly UProject project;
        public readonly UTrack track;
        public readonly UNote[] notes;
        public readonly float? newValue;
        public readonly float?[][] oldValue;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = !needsPhonemizer.Contains(Key),
            };
        public SetNotesSameExpressionCommand(UProject project, UTrack track, UVoicePart part, IEnumerable<UNote> notes, string abbr, float? value) : base(part) {
            this.project = project;
            this.track = track;
            Key = abbr;
            this.notes = notes.ToArray();
            newValue = value;
            oldValue = notes.Select(note => note.GetExpressionNoteHas(project, track, abbr)).ToArray();
        }
        public override string ToString() => $"Set note expression {Key}";
        public override void Execute() {
            for (var i = 0; i < notes.Length; i++) {
                notes[i].SetExpression(project, track, Key, new float?[] { newValue });
            }
        }
        public override void Unexecute() {
            for (var i = 0; i < notes.Length; i++) {
                notes[i].SetExpression(project, track, Key, oldValue[i]);
            }
        }
    }

    public class SetPhonemeExpressionCommand : ExpCommand {
        static readonly HashSet<string> needsPhonemizer = new HashSet<string> {
            Format.Ustx.ALT, Format.Ustx.CLR, Format.Ustx.SHFT, Format.Ustx.VEL
        };

        public readonly UProject project;
        public readonly UTrack track;
        public readonly UPhoneme phoneme;
        public readonly float? newValue;
        public readonly float? oldValue;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = !needsPhonemizer.Contains(Key),
            };
        public SetPhonemeExpressionCommand(UProject project, UTrack track, UVoicePart part, UPhoneme phoneme, string abbr, float? value) : base(part) {
            this.project = project;
            this.track = track;
            this.phoneme = phoneme;
            Key = abbr;
            newValue = value;
            var oldExp = phoneme.GetExpression(project, track, abbr);
            if (oldExp.Item2) {
                oldValue = oldExp.Item1;
            } else {
                oldValue = null;
            }
        }
        public override string ToString() => $"Set phoneme expression {Key}";
        public override void Execute() {
            phoneme.SetExpression(project, track, Key, newValue);
        }
        public override void Unexecute() {
            phoneme.SetExpression(project, track, Key, oldValue);
        }
    }

    public class ResetExpressionsCommand : ExpCommand {
        List<UExpression> phonemeExpressions;
        public ResetExpressionsCommand(UVoicePart part, UNote note) : base(part) {
            Note = note;
            phonemeExpressions = note.phonemeExpressions;
        }
        public override string ToString() => "Reset expressions.";
        public override void Execute() {
            Note.phonemeExpressions = new List<UExpression>();
        }
        public override void Unexecute() {
            Note.phonemeExpressions = phonemeExpressions;
        }
    }

    public abstract class PitchExpCommand : ExpCommand {
        public PitchExpCommand(UVoicePart part) : base(part) { }
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
            SkipPhoneme = true,
        };
    }

    public class DeletePitchPointCommand : PitchExpCommand {
        public int Index;
        public PitchPoint Point;
        public DeletePitchPointCommand(UVoicePart part, UNote note, int index) : base(part) {
            this.Note = note;
            this.Index = index;
            this.Point = Note.pitch.data[Index];
        }
        public override string ToString() { return "Delete pitch point"; }
        public override void Execute() { Note.pitch.data.RemoveAt(Index); }
        public override void Unexecute() { Note.pitch.data.Insert(Index, Point); }
    }

    public class ChangePitchPointShapeCommand : PitchExpCommand {
        public PitchPoint Point;
        public PitchPointShape NewShape;
        public PitchPointShape OldShape;
        public ChangePitchPointShapeCommand(UVoicePart part, PitchPoint point, PitchPointShape shape) : base(part) {
            this.Point = point;
            this.NewShape = shape;
            this.OldShape = point.shape;
        }
        public override string ToString() { return "Change pitch point shape"; }
        public override void Execute() { Point.shape = NewShape; }
        public override void Unexecute() { Point.shape = OldShape; }
    }

    public class SetPitchPointShapeCommand : PitchExpCommand {
        public UNote[] Notes;
        public PitchPointShape NewShape;
        public PitchPointShape[][] OldShapes;
        public SetPitchPointShapeCommand(UVoicePart part, IEnumerable<UNote> notes, PitchPointShape shape) : base(part) {
            this.Notes = notes.ToArray();
            this.NewShape = shape;
            this.OldShapes = notes
                .Select(note => note.pitch.data
                    .Select(point => point.shape)
                    .ToArray())
                .ToArray();
        }
        public override string ToString() { return "Change pitch point shape"; }
        public override void Execute() {
            foreach (var note in Notes) {
                foreach (var point in note.pitch.data) {
                    point.shape = NewShape;
                }
            }
        }
        public override void Unexecute() {
            for (var i = 0; i < Notes.Length; i++) {
                var note = Notes[i];
                var shapes = OldShapes[i];
                for (var p = 0; p < shapes.Length; p++) {
                    note.pitch.data[p].shape = shapes[p];
                }
            }
        }
    }

    public class SnapPitchPointCommand : PitchExpCommand {
        readonly float X, Y;
        public SnapPitchPointCommand(UVoicePart part, UNote note) : base(part) {
            Note = note;
            X = Note.pitch.data.First().X;
            Y = Note.pitch.data.First().Y;
        }
        public override string ToString() { return "Toggle pitch snap"; }
        public override void Execute() {
            Note.pitch.snapFirst = !Note.pitch.snapFirst;
            if (!Note.pitch.snapFirst) {
                Note.pitch.data.First().X = X;
                Note.pitch.data.First().Y = Y;
            }
        }
        public override void Unexecute() {
            Note.pitch.snapFirst = !Note.pitch.snapFirst;
            if (!Note.pitch.snapFirst) {
                Note.pitch.data.First().X = X;
                Note.pitch.data.First().Y = Y;
            }
        }
    }

    public class AddPitchPointCommand : PitchExpCommand {
        public int Index;
        public PitchPoint Point;
        public AddPitchPointCommand(UVoicePart part, UNote note, PitchPoint point, int index) : base(part) {
            this.Note = note;
            this.Index = index;
            this.Point = point;
        }
        public override string ToString() { return "Add pitch point"; }
        public override void Execute() { Note.pitch.data.Insert(Index, Point); }
        public override void Unexecute() { Note.pitch.data.RemoveAt(Index); }
    }

    public class MovePitchPointCommand : PitchExpCommand {
        readonly PitchPoint point;
        readonly float deltaX;
        readonly float deltaY;
        public MovePitchPointCommand(UVoicePart part, PitchPoint point, float deltaX, float deltaY) : base(part) {
            this.point = point;
            this.deltaX = deltaX;
            this.deltaY = deltaY;
        }
        public override string ToString() { return "Move pitch point"; }
        public override void Execute() { point.X += deltaX; point.Y += deltaY; }
        public override void Unexecute() { point.X -= deltaX; point.Y -= deltaY; }
    }

    public class ResetPitchPointsCommand : PitchExpCommand {
        UPitch oldPitch;
        UPitch newPitch;
        public ResetPitchPointsCommand(UVoicePart part, UNote note) : base(part) {
            Note = note;
            oldPitch = note.pitch;
            newPitch = new UPitch();
            int start = NotePresets.Default.DefaultPortamento.PortamentoStart;
            int length = NotePresets.Default.DefaultPortamento.PortamentoLength;
            var shape = NotePresets.Default.DefaultPitchShape;
            newPitch.AddPoint(new PitchPoint(start, 0, shape));
            newPitch.AddPoint(new PitchPoint(start + length, 0, shape));
        }
        public override string ToString() => "Reset pitch points";
        public override void Execute() => Note.pitch = newPitch;
        public override void Unexecute() => Note.pitch = oldPitch;
    }

    public class SetPitchPointsCommand : PitchExpCommand {
        UPitch[] oldPitch;
        UNote[] Notes;
        UPitch newPitch;
        public SetPitchPointsCommand(UVoicePart part, UNote note, UPitch pitch) : base(part) {
            Notes = new UNote[] { note };
            oldPitch = Notes.Select(note => note.pitch).ToArray();
            newPitch = pitch;
        }

        public SetPitchPointsCommand(UVoicePart part, IEnumerable<UNote> notes, UPitch pitch) : base(part) {
            Notes = notes.ToArray();
            oldPitch = Notes.Select(note => note.pitch).ToArray();
            newPitch = pitch;
        }
        public override string ToString() => "Set pitch points";
        public override void Execute(){
            lock (Part) {
                for (var i=0; i<Notes.Length; i++) {
                    Notes[i].pitch = newPitch.Clone();
                }
            }
        }
        public override void Unexecute() {
            lock (Part) {
                for (var i = 0; i < Notes.Length; i++) {
                    Notes[i].pitch = oldPitch[i];
                }
            }
        }
    }

    public class SetCurveCommand : ExpCommand {
        readonly UProject project;
        readonly string abbr;
        readonly int x;
        readonly int y;
        readonly int lastX;
        readonly int lastY;
        int[] oldXs;
        int[] oldYs;
        public override ValidateOptions ValidateOptions
            => new ValidateOptions {
                SkipTiming = true,
                Part = Part,
                SkipPhonemizer = true,
                SkipPhoneme = true,
            };
        public SetCurveCommand(UProject project, UVoicePart part, string abbr, int x, int y, int lastX, int lastY) : base(part) {
            this.project = project;
            this.abbr = abbr;
            this.x = x;
            this.y = y;
            this.lastX = lastX;
            this.lastY = lastY;
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            oldXs = curve?.xs.ToArray();
            oldYs = curve?.ys.ToArray();
        }
        public override string ToString() => "Edit Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (project.expressions.TryGetValue(abbr, out var descriptor)) {
                if (curve == null) {
                    curve = new UCurve(descriptor);
                    Part.curves.Add(curve);
                }
                int y1 = (int)Math.Clamp(y, descriptor.min, descriptor.max);
                int lastY1 = (int)Math.Clamp(lastY, descriptor.min, descriptor.max);
                curve.Set(x, y1, lastX, lastY1);
            }
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (curve == null) {
                return;
            }
            curve.xs.Clear();
            curve.ys.Clear();
            if (oldXs != null && oldYs != null) {
                curve.xs.AddRange(oldXs);
                curve.ys.AddRange(oldYs);
            }
        }
        public override bool CanMerge(IList<UCommand> commands) {
            return commands.All(c => c is SetCurveCommand);
        }
        public override UCommand Merge(IList<UCommand> commands) {
            var first = commands.First() as SetCurveCommand;
            var last = commands.Last() as SetCurveCommand;
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            curve.Simplify();
            int[] newXs = curve?.xs.ToArray();
            int[] newYs = curve?.ys.ToArray();
            return new MergedSetCurveCommand(
                last.project, last.Part, last.abbr,
                first.oldXs, first.oldYs, newXs, newYs);
        }
    }

    public class MergedSetCurveCommand : ExpCommand {
        readonly UProject project;
        readonly string abbr;
        readonly int[] oldXs;
        readonly int[] oldYs;
        readonly int[] newXs;
        readonly int[] newYs;
        readonly bool setReal;
        public MergedSetCurveCommand(UProject project, UVoicePart part,
            string abbr, int[] oldXs, int[] oldYs, int[] newXs, int[] newYs, bool setReal = false) : base(part) {
            this.project = project;
            this.abbr = abbr;
            this.oldXs = oldXs;
            this.oldYs = oldYs;
            this.newXs = newXs;
            this.newYs = newYs;
            this.setReal = setReal;
        }
        public override string ToString() => "Edit Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (curve == null && project.expressions.TryGetValue(abbr, out var descriptor)) {
                curve = new UCurve(descriptor);
                Part.curves.Add(curve);
            }
            GetCurveXs(curve)?.Clear();
            GetCurveYs(curve)?.Clear();
            if (newXs != null && newYs != null) {
                GetCurveXs(curve)?.AddRange(newXs);
                GetCurveYs(curve)?.AddRange(newYs);
            }
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (curve == null && project.expressions.TryGetValue(abbr, out var descriptor)) {
                curve = new UCurve(descriptor);
                Part.curves.Add(curve);
            }
            GetCurveXs(curve)?.Clear();
            GetCurveYs(curve)?.Clear();
            if (oldXs != null && oldYs != null) {
                GetCurveXs(curve)?.AddRange(oldXs);
                GetCurveYs(curve)?.AddRange(oldYs);
            }
        }
        private List<int>? GetCurveXs(UCurve? curve) {
            return setReal ? curve?.realXs : curve?.xs;
        }
        private List<int>? GetCurveYs(UCurve? curve) {
            return setReal ? curve?.realYs : curve?.ys;
        }
    }

    public class PasteCurveCommand : ExpCommand {
        readonly UProject project;
        readonly string abbr;
        readonly int[] xs;
        readonly int[] ys;
        int[]? oldXs;
        int[]? oldYs;
        public PasteCurveCommand(UProject project, UVoicePart part, string abbr, IEnumerable<int> xs, IEnumerable<int> ys) : base(part) {
            this.project = project;
            this.abbr = abbr;
            this.xs = xs.ToArray();
            this.ys = ys.ToArray();
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            oldXs = curve?.xs.ToArray();
            oldYs = curve?.ys.ToArray();
        }
        public PasteCurveCommand(UProject project, UVoicePart part, string abbr, int startX, int startY, int endX, int endY) : base(part) {
            this.project = project;
            this.abbr = abbr;
            this.xs = new int[] { startX, endX };
            this.ys = new int[] { startY, endY };
            var curve = part.curves.FirstOrDefault(c => c.abbr == abbr);
            oldXs = curve?.xs.ToArray();
            oldYs = curve?.ys.ToArray();
        }
        public override string ToString() => "Edit Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            var track = project.tracks[Part.trackNo];
            if (track.TryGetExpDescriptor(project, abbr, out var descriptor)) {
                if (curve == null) {
                    curve = new UCurve(descriptor);
                    Part.curves.Add(curve);
                }

                var xs = this.xs.ToList();
                var ys = this.ys.ToList();
                xs.Insert(0, xs[0] - UCurve.interval);
                ys.Insert(0, curve.Sample(xs[0]));
                xs.Add(xs.Last() + UCurve.interval);
                ys.Add(curve.Sample(xs.Last()));
                ys = ys.Select(y => (int)Math.Clamp(y, descriptor.min, descriptor.max)).ToList();

                curve.Set(xs.First(), ys.First(), xs.First(), ys.First());
                curve.Set(xs.Last(), ys.Last(), xs.Last(), ys.Last());
                for (int i = 0; i < xs.Count - 1; i++) {
                    curve.Set(xs[i + 1], ys[i + 1], xs[i], ys[i]);
                }
            }
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(c => c.abbr == abbr);
            if (curve == null) {
                return;
            }
            curve.xs.Clear();
            curve.ys.Clear();
            if (oldXs != null && oldYs != null) {
                curve.xs.AddRange(oldXs);
                curve.ys.AddRange(oldYs);
            }
        }
    }

    public class ClearCurveCommand : ExpCommand {
        readonly string abbr;
        readonly int[] oldXs;
        readonly int[] oldYs;
        public ClearCurveCommand(UVoicePart part, string abbr) : base(part) {
            this.abbr = abbr;
            var curve = Part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve != null) {
                oldXs = curve.xs.ToArray();
                oldYs = curve.ys.ToArray();
            }
        }
        public override string ToString() => "Clear Curve";
        public override void Execute() {
            var curve = Part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve != null) {
                curve.xs.Clear();
                curve.ys.Clear();
            }
        }
        public override void Unexecute() {
            var curve = Part.curves.FirstOrDefault(curve => curve.abbr == abbr);
            if (curve != null && oldXs != null && oldYs != null) {
                curve.xs.Clear();
                curve.xs.AddRange(oldXs);
                curve.ys.Clear();
                curve.ys.AddRange(oldYs);
            }
        }
    }
}
