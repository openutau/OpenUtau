using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    class KeyboardPlayState {
        private readonly TrackBackground element;
        private readonly PianoRollViewModel vm;
        private int activeTone;

        public KeyboardPlayState(TrackBackground element, PianoRollViewModel vm) {
            this.element = element;
            this.vm = vm;
        }
        public void Begin(IPointer pointer, Point point) {
            pointer.Capture(element);
            var tone = vm.NotesViewModel.PointToTone(point);
            PlaybackManager.Inst.PlayTone(MusicMath.ToneToFreq(tone));
            activeTone = tone;
        }
        public void Update(IPointer pointer, Point point) {
            var tone = vm.NotesViewModel.PointToTone(point);
            if (activeTone != tone) {
                PlaybackManager.Inst.EndTone(MusicMath.ToneToFreq(activeTone));
                PlaybackManager.Inst.PlayTone(MusicMath.ToneToFreq(tone));
                activeTone = tone;
            }
        }
        public void End(IPointer pointer, Point point) {
            pointer.Capture(null);
            PlaybackManager.Inst.EndTone(MusicMath.ToneToFreq(activeTone));
        }
    }

    class NoteEditState {
        public virtual MouseButton MouseButton => MouseButton.Left;
        public readonly Control control;
        public readonly PianoRollViewModel vm;
        public Point startPoint;
        public IValueTip valueTip;
        protected virtual bool ShowValueTip => true;
        protected virtual string? commandNameKey => null;
        public bool ctrlShiftHeld = false;
        public bool altShiftHeld = false;
        public bool shiftHeld = false;
        public bool ctrlHeld = false;
        public bool altHeld = false;

        public NoteEditState(Control control, PianoRollViewModel vm, IValueTip valueTip) {
            this.control = control;
            this.vm = vm;
            this.valueTip = valueTip;
        }
        public virtual void Begin(IPointer pointer, Point point) {
            pointer.Capture(control);
            startPoint = point;
            DocManager.Inst.StartUndoGroup(commandNameKey);
            if (ShowValueTip) {
                valueTip.ShowValueTip();
            }
        }
        public virtual void End(IPointer pointer, Point point) {
            pointer.Capture(null);
            DocManager.Inst.EndUndoGroup();
            if (ShowValueTip) {
                valueTip.HideValueTip();
            }
        }
        public virtual void Update(IPointer pointer, Point point) { }
        public static void Swap<T>(ref T a, ref T b) {
            T temp = a;
            a = b;
            b = temp;
        }
        public static double Lerp(Point p1, Point p2, double x) {
            double t = (x - p1.X) / (p2.X - p1.X);
            t = Math.Clamp(t, 0, 1);
            return p1.Y + t * (p2.Y - p1.Y);
        }
    }

    class NoteSelectionEditState : NoteEditState {
        public readonly Rectangle selectionBox;
        protected override bool ShowValueTip => false;
        private int startTick;
        private int startTone;

        public NoteSelectionEditState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            Rectangle selectionBox) : base(control, vm, valueTip) {
            this.selectionBox = selectionBox;
        }
        public override void Begin(IPointer pointer, Point point) {
            pointer.Capture(control);
            startPoint = point;
            selectionBox.IsVisible = true;
            var notesVm = vm.NotesViewModel;
            startTick = notesVm.PointToTick(point);
            startTone = notesVm.PointToTone(point);
        }
        public override void End(IPointer pointer, Point point) {
            pointer.Capture(null);
            selectionBox.IsVisible = false;
            var notesVm = vm.NotesViewModel;
            notesVm.CommitTempSelectNotes();
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            int tick = notesVm.PointToTick(point);
            int tone = notesVm.PointToTone(point);

            int minTick = Math.Min(tick, startTick);
            int maxTick = Math.Max(tick, startTick);
            notesVm.TickToLineTick(minTick, out int x0, out int _);
            notesVm.TickToLineTick(maxTick, out int _, out int x1);

            int y0 = Math.Min(tone, startTone) - 1;
            int y1 = Math.Max(tone, startTone);

            var leftTop = notesVm.TickToneToPoint(x0, y1);
            var Size = notesVm.TickToneToSize(x1 - x0, y1 - y0);
            Canvas.SetLeft(selectionBox, leftTop.X);
            Canvas.SetTop(selectionBox, leftTop.Y);
            selectionBox.Width = Size.Width + 1;
            selectionBox.Height = Size.Height;
            notesVm.TempSelectNotes(x0, x1, y0, y1);
        }
    }

    class NoteMoveEditState : NoteEditState {
        public readonly UNote note;
        private double xOffset;
        protected override bool ShowValueTip => false;
        protected override string? commandNameKey => "command.note.move";

        public NoteMoveEditState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note) : base(control, vm, valueTip) {
            this.note = note;
            var notesVm = vm.NotesViewModel;
            if (!notesVm.Selection.Contains(note)) {
                notesVm.SelectNote(note);
            }
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            var notesVm = vm.NotesViewModel;
            xOffset = point.X - notesVm.TickToneToPoint(note.position, 0).X;
        }
        public override void Update(IPointer pointer, Point point) {
            var delta = point - startPoint;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) < 4) {
                return;
            }
            var project = DocManager.Inst.Project;
            var notesVm = vm.NotesViewModel;
            var part = notesVm.Part;
            if (part == null) {
                return;
            }

            int deltaTone = notesVm.PointToTone(point) - note.tone;
            int minDeltaTone;
            int maxDeltaTone;
            var selectedNotes = notesVm.Selection.ToList();
            if (selectedNotes.Count > 0) {
                minDeltaTone = -selectedNotes.Select(p => p.tone).Min();
                maxDeltaTone = ViewConstants.MaxTone - 1 - selectedNotes.Select(p => p.tone).Max();
            } else {
                minDeltaTone = -note.tone;
                maxDeltaTone = ViewConstants.MaxTone - 1 - note.tone;
            }
            deltaTone = Math.Clamp(deltaTone, minDeltaTone, maxDeltaTone);

            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            int newPos = notesVm.PointToTick(point - new Point(xOffset, 0));
            if (notesVm.IsSnapOn) {
                newPos = (int)Math.Floor((double)newPos / snapUnit) * snapUnit;
            }
            int deltaTick = newPos - note.position;
            int minDeltaTick;
            int maxDeltaTick;
            if (selectedNotes.Count > 0) {
                minDeltaTick = -selectedNotes.Select(n => n.position).Min();
                maxDeltaTick = part.Duration - selectedNotes.Select(n => n.End).Max();
            } else {
                minDeltaTick = -note.position;
                maxDeltaTick = part.Duration - note.End;
            }
            deltaTick = Math.Clamp(deltaTick, minDeltaTick, maxDeltaTick);

            if (deltaTone == 0 && deltaTick == 0) {
                return;
            }
            if (selectedNotes.Count == 0) {
                DocManager.Inst.ExecuteCmd(new MoveNoteCommand(
                    part, note, deltaTick, deltaTone));
            } else {
                DocManager.Inst.ExecuteCmd(new MoveNoteCommand(
                    part, selectedNotes, deltaTick, deltaTone));
            }
        }
    }

    class NoteDrawEditState : NoteEditState {
        private UNote? note;
        private bool playTone;
        private int activeTone;
        protected override string? commandNameKey => "command.note.add";

        public NoteDrawEditState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            bool playTone) : base(control, vm, valueTip) {
            this.playTone = playTone;
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            note = vm.NotesViewModel.MaybeAddNote(point, false);
            if (note != null && playTone) {
                if (PlaybackManager.Inst.PlayingMaster) {
                    // Stop playback if playing project
                    PlaybackManager.Inst.StopPlayback();
                }
                activeTone = note.tone;
                PlaybackManager.Inst.PlayTone(MusicMath.ToneToFreq(note.tone));
            }
            if (note != null) {
                var prev = vm.NotesViewModel.Part!.notes.FirstOrDefault(n => n.position < note.position && note.position < n.End);
                if (prev != null) {
                    DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(vm.NotesViewModel.Part, prev, note.position - prev.End));
                }
            }
        }
        public override void Update(IPointer pointer, Point point) {
            if (note == null) {
                return;
            }
            var project = DocManager.Inst.Project;
            var notesVm = vm.NotesViewModel;
            var part = notesVm.Part;
            if (part == null) {
                return;
            }
            int tone = notesVm.PointToTone(point);
            if (activeTone != tone) {
                // Tone has changed
                PlaybackManager.Inst.EndTone(MusicMath.ToneToFreq(activeTone));
                PlaybackManager.Inst.PlayTone(MusicMath.ToneToFreq(tone));
                activeTone = tone;
            }
            int deltaTone = tone - note.tone;
            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            int newEnd = notesVm.PointToTick(point);
            if (notesVm.IsSnapOn) {
                newEnd = (int)Math.Floor((double)newEnd / snapUnit + 1) * snapUnit;
            }
            int deltaDuration = newEnd - note.End;
            int minNoteTicks = notesVm.IsSnapOn ? snapUnit : 15;
            if (deltaDuration < 0) {
                int maxNegDelta = note.duration - minNoteTicks;
                if (notesVm.Selection.Count > 0) {
                    maxNegDelta = notesVm.Selection.Min(n => n.duration - minNoteTicks);
                }
                if (notesVm.IsSnapOn && snapUnit > 0) {
                    maxNegDelta = (int)Math.Floor((double)maxNegDelta / snapUnit) * snapUnit;
                }
                deltaDuration = Math.Max(deltaDuration, -maxNegDelta);
            }
            if (deltaTone != 0) {
                DocManager.Inst.ExecuteCmd(new MoveNoteCommand(part, note, 0, deltaTone));
            }
            if (deltaDuration != 0) {
                DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, note, deltaDuration));
                if (NotePresets.Default.AutoVibratoToggle && note.duration >= NotePresets.Default.AutoVibratoNoteDuration) {
                    DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(part, note, NotePresets.Default.DefaultVibrato.VibratoLength));
                } else {
                    DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(part, note, 0));
                }
            }
            valueTip.UpdateValueTip(note.duration.ToString());
        }
        public override void End(IPointer pointer, Point point) {
            base.End(pointer, point);
            PlaybackManager.Inst.EndTone(MusicMath.ToneToFreq(activeTone));
        }
    }

    class NoteResizeEditState : NoteEditState {
        public readonly UNote note;
        public readonly UNote? neighborNote;
        public readonly bool resizeNeighbor;
        public readonly int neighborNoteLength;
        public readonly bool fromStart;
        protected override string? commandNameKey => "command.note.edit";

        public NoteResizeEditState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note,
            bool resizeNeighbor,
            bool fromStart = false) : base(control, vm, valueTip) {
            this.note = note;
            var notesVm = vm.NotesViewModel;
            if (!notesVm.Selection.Contains(note)) {
                notesVm.DeselectNotes();
            }
            neighborNote = fromStart ? note.Prev : note.Next;
            neighborNoteLength = neighborNote?.duration ?? 0;
            this.resizeNeighbor = resizeNeighbor;
            this.fromStart = fromStart;
        }
        public override void Update(IPointer pointer, Point point) {
            var project = DocManager.Inst.Project;
            var notesVm = vm.NotesViewModel;
            var part = notesVm.Part;
            if (part == null) {
                return;
            }
            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            int newTick = notesVm.PointToTick(point);
            if (notesVm.IsSnapOn) {
                newTick = fromStart
                    ? (int)Math.Floor((double)newTick / snapUnit) * snapUnit
                    : (int)Math.Floor((double)newTick / snapUnit) * snapUnit + snapUnit;
            }

            int deltaDuration = fromStart
                ? note.position - newTick
                : newTick - note.End;
            int minNoteTicks = notesVm.IsSnapOn ? snapUnit : 15;
            if (deltaDuration < 0) {
                int maxNegDelta = note.duration - minNoteTicks;
                if (notesVm.Selection.Count > 0) {
                    maxNegDelta = notesVm.Selection.Min(n => n.duration - minNoteTicks);
                }
                if (notesVm.IsSnapOn && snapUnit > 0) {
                    maxNegDelta = (int)Math.Floor((double)maxNegDelta / snapUnit) * snapUnit;
                }
                deltaDuration = Math.Max(deltaDuration, -maxNegDelta);
            }

            var adjacent = neighborNote != null && ((!fromStart && neighborNote.position == note.End) || (fromStart && neighborNote.End == note.position));
            if (deltaDuration > 0 && neighborNote != null) {
                if (!fromStart && neighborNote.position < note.End + deltaDuration) adjacent = true;
                if (fromStart && note.position - deltaDuration < neighborNote.End) adjacent = true;
            }
            var resizeNeighbor = notesVm.Selection.Count <= 1
                && neighborNote != null
                && (this.resizeNeighbor || deltaDuration > 0 || neighborNote.duration < neighborNoteLength)
                && adjacent;
            if (resizeNeighbor && neighborNote != null) {
                var maxDelta = Math.Max(0, neighborNote.duration - minNoteTicks);
                deltaDuration = Math.Min(deltaDuration, maxDelta);
            }
            // Prevent note from moving past part start (position < 0)
            if (fromStart) {
                deltaDuration = Math.Min(deltaDuration, note.position);
            }
            if (deltaDuration == 0) {
                valueTip.UpdateValueTip(note.duration.ToString());
                return;
            }
            // Resize neighbor note
            if (resizeNeighbor && neighborNote != null) {
                int cutDuration = deltaDuration;
                if (!this.resizeNeighbor && deltaDuration < 0) {
                    cutDuration = Math.Max(deltaDuration, neighborNote.duration - neighborNoteLength);
                }
                if (!fromStart && neighborNote.position != note.End) {
                    cutDuration = note.End + deltaDuration - neighborNote.position;
                } else if (fromStart && neighborNote.End != note.position) {
                    cutDuration = neighborNote.End - (note.position - deltaDuration);
                }
                if (!fromStart) {
                    DocManager.Inst.ExecuteCmd(new MoveNoteCommand(part, neighborNote, cutDuration, 0));
                }
                DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, neighborNote, -cutDuration));
            }
            // Resize current note
            if (notesVm.Selection.Count <= 1) {
                if (fromStart) {
                    DocManager.Inst.ExecuteCmd(new MoveNoteCommand(part, note, -deltaDuration, 0));
                }
                DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, note, deltaDuration));
            } else {
                if (fromStart) {
                    DocManager.Inst.ExecuteCmd(new MoveNoteCommand(part, notesVm.Selection.ToList(), -deltaDuration, 0));
                }
                DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(part, notesVm.Selection.ToList(), deltaDuration));
            }
            valueTip.UpdateValueTip(note.duration.ToString());
        }
    }

    class NoteSplitEditState : NoteEditState {
        public readonly UNote note;
        private UNote? newNote;
        private int oldDur;
        private float oldVibLength;
        private float oldVibFadeIn;
        private float oldVibFadeOut;
        private float oldVibShift;
        private float oldVibLengthTicks => oldVibLength * oldDur / 100;
        private float oldVibFadeInTicks => oldVibFadeIn * oldVibLengthTicks / 100;
        private float oldVibFadeOutTicks => oldVibFadeOut * oldVibLengthTicks / 100;
        private float vibPeriod => note.vibrato.period;
        protected override string? commandNameKey => "command.note.split";

        public NoteSplitEditState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note) : base(control, vm, valueTip) {
            this.note = note;
            var notesVm = vm.NotesViewModel;
            if (!notesVm.Selection.Contains(note)) {
                notesVm.DeselectNotes();
            }
            oldDur = note.duration;
            oldVibLength = note.vibrato.length;
            oldVibFadeIn = note.vibrato.@in;
            oldVibFadeOut = note.vibrato.@out;
            oldVibShift = note.vibrato.shift;
        }

        public override void Begin(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            base.Begin(pointer, point);
            var project = DocManager.Inst.Project;
            var part = notesVm.Part;
            if (project == null || part == null || note == null) {
                return;
            }
            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            if (note.duration <= snapUnit) {
                return;
            }
            newNote = notesVm.MaybeAddNote(point, false);
            if (newNote == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(part, newNote, NotePresets.Default.SplittedLyric));
        }

        public override void Update(IPointer pointer, Point point) {
            var project = DocManager.Inst.Project;
            var notesVm = vm.NotesViewModel;
            if (notesVm.Part == null || newNote == null) {
                return;
            }
            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            int tick = notesVm.PointToTick(point);
            int roundedSnappedTick = (int)Math.Round((double)tick / snapUnit) * snapUnit;
            int deltaDuration = notesVm.IsSnapOn
                ? roundedSnappedTick - note.End
                : tick - note.End;
            int minNoteTicks = notesVm.IsSnapOn ? snapUnit : 15;

            int maxNegDelta = note.duration - minNoteTicks;
            if (notesVm.IsSnapOn && snapUnit > 0) {
                maxNegDelta = (int)Math.Floor((double)maxNegDelta / snapUnit) * snapUnit;
            }

            int maxNoteTicks = (notesVm.IsSnapOn && snapUnit > 0)
                ? (oldDur - 1) / snapUnit * snapUnit
                : oldDur - 15;
            int maxDelta = maxNoteTicks - note.duration;

            deltaDuration = Math.Clamp(deltaDuration, -maxNegDelta, maxDelta);

            if (deltaDuration == 0) {
                valueTip.UpdateValueTip(note.duration.ToString());
                return;
            }
            if (note.duration + deltaDuration < oldDur) {
                DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(notesVm.Part, newNote, -deltaDuration));
                DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(notesVm.Part, note, deltaDuration));
                if (note.duration > oldDur - 10) DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(notesVm.Part, note, oldDur - note.duration - 10));
                if (note.duration + newNote.duration > oldDur) DocManager.Inst.ExecuteCmd(new ResizeNoteCommand(notesVm.Part, newNote, -(note.duration + newNote.duration - oldDur))); ;
                DocManager.Inst.ExecuteCmd(new MoveNoteCommand(notesVm.Part, newNote, note.End - newNote.position, 0));
            }

            if (oldVibLength > 0) {
                DocManager.Inst.ExecuteCmd(new VibratoDepthCommand(notesVm.Part, newNote, note.vibrato.depth));
                DocManager.Inst.ExecuteCmd(new VibratoPeriodCommand(notesVm.Part, newNote, note.vibrato.period));

                if (oldVibLengthTicks > newNote.duration) {
                    float newVibLengthTicks = oldVibLengthTicks - newNote.duration;
                    //length correction
                    DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(notesVm.Part, newNote, 100));
                    DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(notesVm.Part, note, newVibLengthTicks * 100 / note.duration));
                    //fade in/out correction
                    DocManager.Inst.ExecuteCmd(new VibratoFadeInCommand(notesVm.Part, note, oldVibFadeInTicks * 100 / newVibLengthTicks));
                    DocManager.Inst.ExecuteCmd(new VibratoFadeOutCommand(notesVm.Part, note, 0));
                    DocManager.Inst.ExecuteCmd(new VibratoFadeInCommand(notesVm.Part, newNote, 0));
                    DocManager.Inst.ExecuteCmd(new VibratoFadeOutCommand(notesVm.Part, newNote, oldVibFadeOutTicks * 100 / newNote.duration));
                    //phase correction
                    double newVibLengthMs = project.timeAxis.MsBetweenTickPos(newNote.position, newNote.position + newVibLengthTicks);
                    float newVibShift = (float)(100 * (newVibLengthMs % vibPeriod / vibPeriod)) + oldVibShift;
                    if (newVibShift > 100) newVibShift -= 100;
                    DocManager.Inst.ExecuteCmd(new VibratoShiftCommand(notesVm.Part, newNote, newVibShift));
                } else {
                    DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(notesVm.Part, note, 0));
                    DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(notesVm.Part, newNote, oldVibLengthTicks * 100 / newNote.duration));
                    DocManager.Inst.ExecuteCmd(new VibratoFadeInCommand(notesVm.Part, newNote, oldVibFadeIn));
                    DocManager.Inst.ExecuteCmd(new VibratoFadeOutCommand(notesVm.Part, newNote, oldVibFadeOut));
                    DocManager.Inst.ExecuteCmd(new VibratoShiftCommand(notesVm.Part, newNote, oldVibShift));
                }
            }

            valueTip.UpdateValueTip(note.duration.ToString());
        }
    }

    class NoteEraseEditState : NoteEditState {
        public override MouseButton MouseButton => mouseButton;
        private MouseButton mouseButton;
        protected override bool ShowValueTip => false;
        protected override string? commandNameKey => "command.note.delete";

        public NoteEraseEditState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            MouseButton mouseButton) : base(control, vm, valueTip) {
            this.mouseButton = mouseButton;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var noteHitInfo = notesVm.HitTest.HitTestNote(point);
            if (noteHitInfo.hitBody && notesVm.Part != null) {
                DocManager.Inst.ExecuteCmd(new RemoveNoteCommand(notesVm.Part, noteHitInfo.note));
            }
        }
    }

    class NotePanningState : NoteEditState {
        public override MouseButton MouseButton => MouseButton.Middle;
        protected override bool ShowValueTip => false;
        public NotePanningState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip) : base(control, vm, valueTip) { }
        public override void Begin(IPointer pointer, Point point) {
            pointer.Capture(control);
            startPoint = point;
        }
        public override void End(IPointer pointer, Point point) {
            pointer.Capture(null);
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            double deltaX = (point.X - startPoint.X) / notesVm.TickWidth;
            double deltaY = (point.Y - startPoint.Y) / notesVm.TrackHeight;
            startPoint = point;
            notesVm.TickOffset = Math.Max(0, notesVm.TickOffset - deltaX);
            notesVm.TrackOffset = Math.Max(0, notesVm.TrackOffset - deltaY);
        }
    }

    class PitchPointEditState : NoteEditState {
        public readonly UNote note;
        private bool onPoint;
        private float x;
        private float y;
        private int index;
        private PitchPoint pitchPoint;
        protected override string? commandNameKey => "command.pitch.editpoint";

        public PitchPointEditState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note,
            int index, bool onPoint, float x, float y) : base(control, vm, valueTip) {
            this.note = note;
            this.index = index;
            this.onPoint = onPoint;
            this.x = x;
            this.y = y;
            pitchPoint = note.pitch.data[index];
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            if (!onPoint && vm.NotesViewModel.Part != null) {
                pitchPoint = new PitchPoint(x, y, NotePresets.Default.DefaultPitchShape);
                index++;
                DocManager.Inst.ExecuteCmd(new AddPitchPointCommand(
                    vm.NotesViewModel.Part, note, pitchPoint, index));
            }
        }
        public override void End(IPointer pointer, Point point) {
            if (note.pitch.data.Count > 2) {
                var notesVm = vm.NotesViewModel;
                bool removed = false;
                if (index > 0 && index < note.pitch.data.Count - 1 && notesVm.Part != null) {
                    var prev = note.pitch.data[index - 1];
                    var delta = notesVm.TickToneToSize(prev.X - pitchPoint.X, (prev.Y - pitchPoint.Y) * 0.1);
                    if (delta.Width * delta.Width + delta.Height * delta.Height < 64) {
                        DocManager.Inst.ExecuteCmd(new DeletePitchPointCommand(notesVm.Part, note, index));
                        removed = true;
                    }
                    if (!removed) {
                        var next = note.pitch.data[index + 1];
                        delta = notesVm.TickToneToSize(next.X - pitchPoint.X, (next.Y - pitchPoint.Y) * 0.1);
                        if (delta.Width * delta.Width + delta.Height * delta.Height < 64) {
                            DocManager.Inst.ExecuteCmd(new DeletePitchPointCommand(notesVm.Part, note, index));
                        }
                    }
                }
            }
            base.End(pointer, point);
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            int partPos = notesVm.Part?.position ?? 0;
            double x = notesVm.Project.timeAxis.TickPosToMsPos(notesVm.PointToTick(point) + partPos);
            double deltaX = x - (note.PositionMs + pitchPoint.X);
            bool isFirst = index == 0;
            bool isLast = index == note.pitch.data.Count - 1;
            if (!isFirst) {
                deltaX = Math.Max(deltaX, note.pitch.data[index - 1].X - pitchPoint.X);
            }
            if (!isLast) {
                deltaX = Math.Min(deltaX, note.pitch.data[index + 1].X - pitchPoint.X);
            }
            double deltaY;
            if (isLast) {
                deltaY = -pitchPoint.Y;
            } else if (isFirst && note.pitch.snapFirst) {
                var snapTo = note.Prev == null ? note : note.Prev.End == note.position ? note.Prev : note;
                deltaY = (snapTo.AdjustedTone - note.AdjustedTone) * 10 - pitchPoint.Y;
            } else if (ctrlHeld) {
                var snappedSemitone = Math.Round(notesVm.PointToToneDouble(point) - note.AdjustedTone, MidpointRounding.AwayFromZero);
                deltaY = snappedSemitone * 10 - pitchPoint.Y;
            } else if (altShiftHeld && note.pitch.data.Count > 2 && !isLast) {
                deltaY = note.pitch.data[index + 1].Y - pitchPoint.Y;
            } else if (shiftHeld && note.pitch.data.Count > 2 && !isFirst) {
                deltaY = note.pitch.data[index - 1].Y - pitchPoint.Y;
            } else {
                deltaY = (notesVm.PointToToneDouble(point) - note.AdjustedTone) * 10 - pitchPoint.Y;
            }
            if (deltaX == 0 && deltaY == 0) {
                return;
            }
            if (notesVm.Part != null) {
                DocManager.Inst.ExecuteCmd(new MovePitchPointCommand(notesVm.Part, pitchPoint, (float)deltaX, (float)deltaY));
            }
            valueTip.UpdateValueTip($"{pitchPoint.X:0.0}ms, {pitchPoint.Y * 10:0}cent");
        }
    }

    class ExpSetValueState : NoteEditState {
        private Point firstPoint;
        private Point lastPoint;
        private UExpressionDescriptor? descriptor;
        private UTrack track;
        private double startValue = 0;
        private bool shiftWasHeld = false;
        protected override string? commandNameKey => "command.exp.edit";

        public ExpSetValueState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UExpressionDescriptor descriptor) : base(control, vm, valueTip) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            var part = notesVm.Part;
            track = project.tracks[part!.trackNo];
            this.descriptor = descriptor;
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            firstPoint = point;
            lastPoint = point;
            startValue = 0;
        }
        public override void End(IPointer pointer, Point point) {
            base.End(pointer, point);
        }
        public override void Update(IPointer pointer, Point point) {
            if (descriptor == null) {
                return;
            }
            if (descriptor.type != UExpressionType.Curve) {
                UpdatePhonemeExp(pointer, point);
            } else {
                UpdateCurveExp(pointer, point);
            }
            bool typeOptions = descriptor.type == UExpressionType.Options;
            double viewMax = descriptor.max + (typeOptions ? 1 : 0);
            double displayValue;
            if (shiftHeld) {
                displayValue = startValue;
            } else {
                displayValue = descriptor.min + (viewMax - descriptor.min) * (1 - point.Y / control.Bounds.Height);
                displayValue = Math.Max(descriptor.min, Math.Min(descriptor.max, displayValue));
            }
            string valueTipText = string.Empty;
            if (typeOptions) {
                int index = (int)displayValue;
                if (index >= 0 && index < descriptor.options.Length) {
                    var value = string.IsNullOrWhiteSpace(descriptor.options[index]) ? "(Default)" : descriptor.options[index];
                    if (descriptor.abbr == Ustx.CLR && track.Singer is ClassicSinger singer) {
                        var subbanks = singer.Subbanks
                            .Where(bank => bank.Color == descriptor.options[index])
                            .OrderBy(bank => bank.toneSet.FirstOrDefault());
                        if (subbanks.Count() > 1) {
                            var low = string.IsNullOrWhiteSpace(subbanks.First().Prefix) ? subbanks.First().Suffix : subbanks.First().Prefix;
                            var high = string.IsNullOrWhiteSpace(subbanks.Last().Prefix) ? subbanks.Last().Suffix : subbanks.Last().Prefix;
                            valueTipText = $"{value}: \"{low}\" - \"{high}\"";
                        } else if (subbanks.Count() == 1) {
                            var suffix = string.IsNullOrWhiteSpace(subbanks.First().Prefix) ? subbanks.First().Suffix : subbanks.First().Prefix;
                            if (string.IsNullOrWhiteSpace(suffix)) {
                                valueTipText = value;
                            } else {
                                valueTipText = $"{value}: \"{suffix}\"";
                            }
                        }
                    } else {
                        valueTipText = value;
                    }
                } else {
                    valueTipText = "Error: out of range";
                }
                if (string.IsNullOrEmpty(valueTipText)) {
                    valueTipText = "\"\"";
                }
            } else {
                valueTipText = ((int)displayValue).ToString();
            }
            valueTip.UpdateValueTip(valueTipText);
            lastPoint = point;
            shiftWasHeld = shiftHeld;
        }
        private void UpdatePhonemeExp(IPointer pointer, Point point) {
            if (descriptor == null) {
                return;
            }
            var notesVm = vm.NotesViewModel;
            var p1 = lastPoint;
            var p2 = point;
            if (p1.X > p2.X) {
                Swap(ref p1, ref p2);
            }
            string key = notesVm.PrimaryKey;
            var hits = notesVm.HitTest.HitTestExpRange(p1, p2);
            double viewMax = descriptor.max + (descriptor.type == UExpressionType.Options ? 1 : 0);
            if (shiftHeld != shiftWasHeld) {
                startValue = descriptor.min + (viewMax - descriptor.min) * (1 - point.Y / control.Bounds.Height);
                startValue = Math.Max(descriptor.min, Math.Min(descriptor.max, startValue));
            }
            foreach (var hit in hits) {
                if (Preferences.Default.LockUnselectedNotesExpressions && notesVm.Selection.Count > 0 && !notesVm.Selection.Contains(hit.phoneme.Parent)) {
                    continue;
                }
                var valuePoint = notesVm.TickToneToPoint(hit.note.position + hit.phoneme.position, 0);
                double y = Lerp(p1, p2, valuePoint.X);
                double newValue = descriptor.min + (viewMax - descriptor.min) * (1 - y / control.Bounds.Height);
                newValue = Math.Max(descriptor.min, Math.Min(descriptor.max, newValue));

                float value = hit.phoneme.GetExpression(notesVm.Project, track, key).Item1;
                double finalValue = shiftHeld ? startValue : newValue;
                if ((int)finalValue == (int)value) {
                    continue;
                }
                if (notesVm.Part != null) {
                    DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(
                        notesVm.Project, track, notesVm.Part, hit.phoneme, key, (int)finalValue));
                }
            }
        }
        private void UpdateCurveExp(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            if (descriptor == null || notesVm.Part == null) {
                return;
            }
            int lastX = notesVm.PointToTick(lastPoint);
            int x = notesVm.PointToTick(point);
            int lastY = (int)Math.Round(descriptor.min + (descriptor.max - descriptor.min) * (1 - lastPoint.Y / control.Bounds.Height));
            int y = (int)Math.Round(descriptor.min + (descriptor.max - descriptor.min) * (1 - point.Y / control.Bounds.Height));
            if (shiftHeld != shiftWasHeld) {
                firstPoint = point;
            }
            if (ctrlShiftHeld) {
                lastX = notesVm.PointToTick(firstPoint);
                x = notesVm.PointToTick(lastPoint);
                lastY = (int)Math.Round(descriptor.min + (descriptor.max - descriptor.min) * (1 - lastPoint.Y / control.Bounds.Height));
                y = (int)Math.Round(descriptor.min + (descriptor.max - descriptor.min) * (1 - lastPoint.Y / control.Bounds.Height));
            } else if (shiftHeld) {
                lastX = notesVm.PointToTick(lastPoint);
                x = notesVm.PointToTick(point);
                lastY = (int)Math.Round(descriptor.min + (descriptor.max - descriptor.min) * (1 - firstPoint.Y / control.Bounds.Height));
                y = (int)Math.Round(descriptor.min + (descriptor.max - descriptor.min) * (1 - firstPoint.Y / control.Bounds.Height));
                startValue = y;
            }
            DocManager.Inst.ExecuteCmd(new SetCurveCommand(notesVm.Project, notesVm.Part, notesVm.PrimaryKey, x, y, lastX, lastY));
        }
    }

    class ExpResetValueState : NoteEditState {
        private Point lastPoint;
        private UExpressionDescriptor? descriptor;
        private UTrack track;
        public override MouseButton MouseButton => mouseButton;
        private MouseButton mouseButton;
        protected override string? commandNameKey => "command.exp.reset";

        public ExpResetValueState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UExpressionDescriptor descriptor,
            MouseButton mouseButton = MouseButton.Right) : base(control, vm, valueTip) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            var part = notesVm.Part;
            track = project.tracks[part!.trackNo];
            this.descriptor = descriptor;
            this.mouseButton = mouseButton;
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            lastPoint = point;
        }
        public override void Update(IPointer pointer, Point point) {
            if (descriptor == null) {
                return;
            }
            if (descriptor.type != UExpressionType.Curve) {
                ResetPhonemeExp(pointer, point);
            } else {
                ResetCurveExp(pointer, point);
            }
            valueTip.UpdateValueTip(descriptor.CustomDefaultValue.ToString());
        }
        private void ResetPhonemeExp(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var p1 = lastPoint;
            var p2 = point;
            if (p1.X > p2.X) {
                Swap(ref p1, ref p2);
            }
            string key = notesVm.PrimaryKey;
            var hits = notesVm.HitTest.HitTestExpRange(p1, p2);
            if (descriptor == null || notesVm.Part == null) {
                return;
            }
            foreach (var hit in hits) {
                if (Preferences.Default.LockUnselectedNotesExpressions && notesVm.Selection.Count > 0 && !notesVm.Selection.Contains(hit.phoneme.Parent)) {
                    continue;
                }
                if (!hit.phoneme.GetExpression(notesVm.Project, track, key).Item2) {
                    continue;
                }
                DocManager.Inst.ExecuteCmd(new SetPhonemeExpressionCommand(
                    notesVm.Project, track, notesVm.Part, hit.phoneme, key, null));
            }
        }
        private void ResetCurveExp(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            int lastX = notesVm.PointToTick(lastPoint);
            int x = notesVm.PointToTick(point);
            if (descriptor != null && notesVm.Part != null) {
                DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                    notesVm.Project, notesVm.Part, notesVm.PrimaryKey,
                    x, (int)descriptor.defaultValue, lastX, (int)descriptor.defaultValue));
            }
        }
    }

    class CurveSelectionState : NoteEditState {
        private int startTick;
        private int? endTick;
        private UExpressionDescriptor? descriptor;
        protected override bool ShowValueTip => false;

        public CurveSelectionState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UExpressionDescriptor descriptor) : base(control, vm, valueTip) {
            this.descriptor = descriptor;
        }
        public override void Begin(IPointer pointer, Point point) {
            pointer.Capture(control);
            startPoint = point;
            var notesVm = vm.NotesViewModel;
            int snapUnit = notesVm.Project.resolution * 4 / notesVm.SnapDiv;
            int tick = notesVm.PointToTick(point);
            if (notesVm.IsSnapOn) {
                tick = (int)Math.Floor((double)tick / snapUnit) * snapUnit;
            }
            startTick = tick;
        }
        public override void End(IPointer pointer, Point point) {
            pointer.Capture(null);
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            if (descriptor == null || notesVm.Part == null) {
                return;
            }
            int snapUnit = notesVm.Project.resolution * 4 / notesVm.SnapDiv;
            int tick = notesVm.PointToTick(point);
            if (notesVm.IsSnapOn) {
                tick = (int)Math.Floor((double)tick / snapUnit) * snapUnit;
            }
            if (endTick == tick) return;
            endTick = tick;
            if (startTick == tick) {
                vm.CurveViewModel.ClearSelect();
                return;
            }
            int minTick = Math.Min(tick, startTick);
            int maxTick = Math.Max(tick, startTick);
            var curve = notesVm.Part.curves.FirstOrDefault(c => c.abbr == descriptor.abbr);
            vm.CurveViewModel.Select(descriptor, minTick, maxTick, curve);
        }
    }

    class VibratoChangeStartState : NoteEditState {
        public readonly UNote note;
        protected override string? commandNameKey => "command.vibrato.edit";

        public VibratoChangeStartState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note) : base(control, vm, valueTip) {
            this.note = note;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            int tick = notesVm.PointToTick(point);
            float newLength = 100f - 100f * (tick - note.position) / note.duration;
            if (newLength != note.vibrato.length && notesVm.Part != null) {
                DocManager.Inst.ExecuteCmd(new VibratoLengthCommand(notesVm.Part, note, newLength));
            }
            valueTip.UpdateValueTip($"{note.vibrato.length:0}%");
        }
    }

    class VibratoChangeInState : NoteEditState {
        public readonly UNote note;
        protected override string? commandNameKey => "command.vibrato.edit";

        public VibratoChangeInState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note) : base(control, vm, valueTip) {
            this.note = note;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            int tick = notesVm.PointToTick(point);
            float vibratoTick = note.vibrato.length / 100f * note.duration;
            float startTick = note.position + note.duration - vibratoTick;
            float newIn = (tick - startTick) / vibratoTick * 100f;
            if (newIn != note.vibrato.@in && notesVm.Part != null) {
                if (newIn + note.vibrato.@out > 100) {
                    DocManager.Inst.ExecuteCmd(new VibratoFadeOutCommand(notesVm.Part, note, 100 - newIn));
                }
                DocManager.Inst.ExecuteCmd(new VibratoFadeInCommand(notesVm.Part, note, newIn));
            }
            valueTip.UpdateValueTip($"{note.vibrato.@in:0}%");
        }
    }

    class VibratoChangeOutState : NoteEditState {
        public readonly UNote note;
        protected override string? commandNameKey => "command.vibrato.edit";

        public VibratoChangeOutState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note) : base(control, vm, valueTip) {
            this.note = note;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            int tick = notesVm.PointToTick(point);
            float vibratoTick = note.vibrato.length / 100f * note.duration;
            float newOut = (note.position + note.duration - tick) / vibratoTick * 100f;
            if (newOut != note.vibrato.@out && notesVm.Part != null) {
                if (newOut + note.vibrato.@in > 100) {
                    DocManager.Inst.ExecuteCmd(new VibratoFadeInCommand(notesVm.Part, note, 100 - newOut));
                }
                DocManager.Inst.ExecuteCmd(new VibratoFadeOutCommand(notesVm.Part, note, newOut));
            }
            valueTip.UpdateValueTip($"{note.vibrato.@out:0}%");
        }
    }

    class VibratoChangeDepthState : NoteEditState {
        public readonly UNote note;
        protected override string? commandNameKey => "command.vibrato.edit";

        public VibratoChangeDepthState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note) : base(control, vm, valueTip) {
            this.note = note;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            float tone = (float)notesVm.PointToToneDouble(point) - 0.5f;
            float newDepth = note.vibrato.ToneToDepth(note, tone);
            if (newDepth != note.vibrato.depth && notesVm.Part != null) {
                DocManager.Inst.ExecuteCmd(new VibratoDepthCommand(notesVm.Part, note, newDepth));
            }
            valueTip.UpdateValueTip($"{note.vibrato.depth:0.0}");
        }
    }

    class VibratoChangePeriodState : NoteEditState {
        public readonly UNote note;
        protected override string? commandNameKey => "command.vibrato.edit";

        public VibratoChangePeriodState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note) : base(control, vm, valueTip) {
            this.note = note;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            int partPos = notesVm.Part?.position ?? 0;
            float vibratoTick = note.vibrato.length / 100f * note.duration;
            float startTick = note.position + note.duration - vibratoTick;
            if (notesVm.Part == null) {
                return;
            }
            double startMs = project.timeAxis.TickPosToMsPos(startTick + partPos);
            double pointerMs = project.timeAxis.TickPosToMsPos(notesVm.PointToTick(point) + notesVm.Part.position);
            float newPeriod = (float)((pointerMs - startMs) / (1 + note.vibrato.shift / 100f));
            if (newPeriod != note.vibrato.period) {
                DocManager.Inst.ExecuteCmd(new VibratoPeriodCommand(notesVm.Part, note, newPeriod));
            }
            valueTip.UpdateValueTip($"{note.vibrato.period:0.0}ms");
        }
    }

    class VibratoChangeShiftState : NoteEditState {
        public readonly UNote note;
        public readonly Point hitPoint;
        public readonly float initialShift;
        protected override string? commandNameKey => "command.vibrato.edit";

        public VibratoChangeShiftState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote note,
            Point hitPoint,
            float initialShift) : base(control, vm, valueTip) {
            this.note = note;
            this.hitPoint = hitPoint;
            this.initialShift = initialShift;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            float periodTick = project.timeAxis.TicksBetweenMsPos(note.PositionMs, note.PositionMs + note.vibrato.period);
            float deltaTick = notesVm.PointToTick(point) - notesVm.PointToTick(hitPoint);
            float deltaShift = deltaTick / periodTick * 100f;
            float newShift = initialShift + deltaShift;
            if (newShift != note.vibrato.shift && notesVm.Part != null) {
                DocManager.Inst.ExecuteCmd(new VibratoShiftCommand(notesVm.Part, note, newShift));
            }
            valueTip.UpdateValueTip($"{note.vibrato.shift:0}%");
        }
    }

    class PhonemeMoveState : NoteEditState {
        public readonly UNote leadingNote;
        public readonly UPhoneme phoneme;
        public readonly int index;
        public int startOffset;
        protected override string? commandNameKey => "command.phoneme.edit";

        public PhonemeMoveState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote leadingNote,
            UPhoneme phoneme,
            int index) : base(control, vm, valueTip) {
            this.leadingNote = leadingNote;
            this.phoneme = phoneme;
            this.index = index;
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            startOffset = leadingNote.GetPhonemeOverride(index).offset ?? 0;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            int partPos = notesVm.Part?.position ?? 0;
            int offset = startOffset + notesVm.PointToTick(point) - notesVm.PointToTick(startPoint);
            if (notesVm.Part == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(
                notesVm.Part, leadingNote, index, offset));
            var project = notesVm.Project;
            double offsetMs = project.timeAxis.TickPosToMsPos(phoneme.position + offset + partPos) - phoneme.PositionMs;
            valueTip.UpdateValueTip($"{offsetMs:0.0}ms");
        }
    }

    class PhonemeChangePreutterState : NoteEditState {
        public readonly UNote leadingNote;
        public readonly UPhoneme phoneme;
        public readonly int index;
        protected override string? commandNameKey => "command.phoneme.edit";

        public PhonemeChangePreutterState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote leadingNote,
            UPhoneme phoneme,
            int index) : base(control, vm, valueTip) {
            this.leadingNote = leadingNote;
            this.phoneme = phoneme;
            this.index = index;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            double preutter = project.timeAxis.MsBetweenTickPos(notesVm.PointToTick(point), phoneme.position);
            double preutterDelta = preutter - phoneme.autoPreutter;
            if (notesVm.Part == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new PhonemePreutterCommand(notesVm.Part, leadingNote, index, phoneme, (float)preutterDelta));
            valueTip.UpdateValueTip($"{ThemeManager.GetString("pianoroll.tooltip.preutter")}: {phoneme.preutter:0.0}ms ({phoneme.preutterDelta ?? 0:+0.0;-0.0;0}ms)");
        }
    }

    class PhonemeChangeAttackTimeState : NoteEditState {
        public readonly UNote leadingNote;
        public readonly UPhoneme phoneme;
        public readonly int index;
        public PhonemeChangeAttackTimeState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote leadingNote,
            UPhoneme phoneme,
            int index) : base(control, vm, valueTip) {
            this.leadingNote = leadingNote;
            this.phoneme = phoneme;
            this.index = index;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            int partPos = notesVm.Part?.position ?? 0;
            double p1x = phoneme.PositionMs + Math.Max(-phoneme.preutter + 5, -phoneme.preutter + phoneme.GetFadeIn());
            double attackTimeDelta = project.timeAxis.TickPosToMsPos(notesVm.PointToTick(point) + partPos) - p1x;
            if (notesVm.Part == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new PhonemeAttackTimeCommand(notesVm.Part, leadingNote, index, phoneme, (float)attackTimeDelta));
            valueTip.UpdateValueTip($"{ThemeManager.GetString("pianoroll.tooltip.attack")}: {phoneme.attackTimeDelta ?? 0:+0.0;-0.0;0}ms");
        }
    }

    class PhonemeChangeReleaseTimeState : NoteEditState {
        public readonly UNote leadingNote;
        public readonly UPhoneme phoneme;
        public readonly int index;
        public PhonemeChangeReleaseTimeState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote leadingNote,
            UPhoneme phoneme,
            int index) : base(control, vm, valueTip) {
            this.leadingNote = leadingNote;
            this.phoneme = phoneme;
            this.index = index;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            int partPos = notesVm.Part?.position ?? 0;
            double p3x = phoneme.PositionMs + Math.Max(phoneme.envelope.data[2].X, phoneme.envelope.data[4].X - phoneme.GetFadeOut());
            double releaseTimeDelta = p3x - project.timeAxis.TickPosToMsPos(notesVm.PointToTick(point) + partPos);
            if (notesVm.Part == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new PhonemeReleaseTimeCommand(notesVm.Part, leadingNote, index, phoneme, (float)releaseTimeDelta));
            valueTip.UpdateValueTip($"{ThemeManager.GetString("pianoroll.tooltip.release")}: {phoneme.releaseTimeDelta ?? 0:+0.0;-0.0;0}ms");
        }
    }

    class PhonemeChangeOverlapState : NoteEditState {
        public readonly UNote leadingNote;
        public readonly UPhoneme phoneme;
        public readonly int index;
        protected override string? commandNameKey => "command.phoneme.edit";

        public PhonemeChangeOverlapState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            UNote leadingNote,
            UPhoneme phoneme,
            int index) : base(control, vm, valueTip) {
            this.leadingNote = leadingNote;
            this.phoneme = phoneme;
            this.index = index;
        }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var project = notesVm.Project;
            int partPos = notesVm.Part?.position ?? 0;
            double overlap = - phoneme.preutter + phoneme.autoOverlap;
            double overlapDelta = project.timeAxis.TickPosToMsPos(notesVm.PointToTick(point) - phoneme.position) - overlap;
            if (notesVm.Part == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new PhonemeOverlapCommand(notesVm.Part, leadingNote, index, phoneme, (float)overlapDelta));
            valueTip.UpdateValueTip($"{ThemeManager.GetString("pianoroll.tooltip.overlap")}: {phoneme.overlap:0.0}ms ({phoneme.overlapDelta ?? 0:+0.0;-0.0;0}ms)");
        }
    }

    class PhonemeResetState : NoteEditState {
        public override MouseButton MouseButton => MouseButton.Right;
        protected override bool ShowValueTip => false;
        protected override string? commandNameKey => "command.phoneme.reset";

        public PhonemeResetState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip) : base(control, vm, valueTip) { }
        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            var hitInfo = notesVm.HitTest.HitTestPhoneme(point);
            if (hitInfo.hit && notesVm.Part != null) {
                var phoneme = hitInfo.phoneme;
                var parent = phoneme.Parent;
                var leadingNote = parent.Extends ?? parent;
                int index = phoneme.index;
                if (hitInfo.hitPosition) {
                    DocManager.Inst.ExecuteCmd(new PhonemeOffsetCommand(notesVm.Part, leadingNote, index, 0));
                } else if (hitInfo.hitPreutter) {
                    DocManager.Inst.ExecuteCmd(new PhonemePreutterCommand(notesVm.Part, leadingNote, index, phoneme, 0));
                } else if (hitInfo.hitOverlap) {
                    if (phoneme.Next == null) {
                        return;
                    }
                    phoneme = phoneme.Next;
                    parent = phoneme.Parent;
                    leadingNote = parent.Extends ?? parent;
                    index = phoneme.index;
                    DocManager.Inst.ExecuteCmd(new PhonemeOverlapCommand(notesVm.Part, leadingNote, index, phoneme, 0));
                } else if (hitInfo.hitAttackTime) {
                    DocManager.Inst.ExecuteCmd(new PhonemeAttackTimeCommand(notesVm.Part, leadingNote, index, phoneme, 0));
                } else if (hitInfo.hitReleaseTime) {
                    DocManager.Inst.ExecuteCmd(new PhonemeReleaseTimeCommand(notesVm.Part, leadingNote, index, phoneme, 0));
                }
                return;
            }
            var aliasHitInfo = notesVm.HitTest.HitTestAlias(point);
            if (aliasHitInfo.hit) {
                var phoneme = aliasHitInfo.phoneme;
                if (phoneme.rawPhoneme != phoneme.phoneme && notesVm.Part != null) {
                    var note = phoneme.Parent;
                    int index = phoneme.index;
                    DocManager.Inst.ExecuteCmd(
                        new ChangePhonemeAliasCommand(
                            notesVm.Part, note.Extends ?? note, index, null));
                }
            }
        }
    }

    class DrawPitchState : NoteEditState {
        protected override bool ShowValueTip => false;
        protected override string? commandNameKey => "command.pitch.draw";
        private readonly bool overwrite;
        double? lastPitch;
        Point lastPoint;

        public DrawPitchState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            bool overwrite = false) : base(control, vm, valueTip) {
            this.overwrite = overwrite;
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            lastPoint = point;
        }
        public override void Update(IPointer pointer, Point point) {
            int tick = vm.NotesViewModel.PointToTick(point);
            var samplePoint = vm.NotesViewModel.TickToneToPoint(
                (int)Math.Round(tick / 5.0) * 5,
                vm.NotesViewModel.PointToToneDouble(point));
            double? pitch = overwrite
                ? vm.NotesViewModel.HitTest.SampleOverwritePitch(samplePoint)
                : vm.NotesViewModel.HitTest.SamplePitch(samplePoint);
            if (pitch == null || vm.NotesViewModel.Part == null) {
                return;
            }
            double tone = vm.NotesViewModel.PointToToneDouble(point);
            DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                vm.NotesViewModel.Project,
                vm.NotesViewModel.Part,
                Core.Format.Ustx.PITD,
                vm.NotesViewModel.PointToTick(point),
                (int)Math.Round(tone * 100 - pitch.Value),
                vm.NotesViewModel.PointToTick(lastPitch == null ? point : lastPoint),
                (int)Math.Round(tone * 100 - (lastPitch ?? pitch.Value))));
            lastPitch = pitch;
            lastPoint = point;
        }
    }

    class PitchCurveState : NoteEditState {
        protected override bool ShowValueTip => true;
        protected override string? commandNameKey => "command.pitch.draw";
        protected const int step = 5;
        public enum CurveMode { Line, Sine, SCurve }

        enum Phase { Drawing, Adjusting }

        private readonly CurveMode mode;
        private readonly bool overwrite;
        private readonly Polyline previewLine;

        private Phase phase = Phase.Drawing;
        private bool curveApplied = false;
        private Point firstPoint;
        private Point endPoint;
        private Point prevPoint;

        // Curve parameters
        private double spacingTicks;
        private double amplitudeCents;
        private double scurveStrength;

        public CurveMode Mode => mode;
        public bool IsInAdjustingPhase => phase == Phase.Adjusting;

        public PitchCurveState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            Polyline previewLine,
            CurveMode mode,
            bool overwrite) : base(control, vm, valueTip) {
            this.mode = mode;
            this.overwrite = overwrite;
            this.previewLine = previewLine;
        }

        public override void Begin(IPointer pointer, Point point) {
            pointer.Capture(control);
            firstPoint = point;
            endPoint = point;
            prevPoint = point;
            phase = Phase.Drawing;
            InitDefaultParams();
            DocManager.Inst.StartUndoGroup(commandNameKey);
            valueTip.ShowValueTip();
            ShowPreview();
        }

        public override void Update(IPointer pointer, Point point) {
            var notesVm = vm.NotesViewModel;
            if (notesVm.Part == null) {
                prevPoint = point;
                return;
            }

            if (phase == Phase.Drawing) {
                UpdateDrawingParams(point);
                if (mode == CurveMode.Line) {
                    endPoint = point;
                }
                UpdatePreview(firstPoint, point);
            } else { // Phase.Adjusting
                UpdateAdjustingParams(point);
                UpdatePreview(firstPoint, endPoint);
            }
            prevPoint = point;
        }

        public override void End(IPointer pointer, Point point) {
            if (!curveApplied) {
                if (IsSignificantDrag(firstPoint, endPoint)) {
                    ApplyCurve();
                }
                curveApplied = true;
            }
            HidePreview();
            pointer.Capture(null);
            DocManager.Inst.EndUndoGroup();
            valueTip.HideValueTip();
        }

        /// <summary>
        /// Called by PianoRoll on first mouse-up for S-curve/Sine to enter adjusting phase.
        /// Returns false if the drag was not significant (click without drag), signaling cancellation.
        /// </summary>
        public bool TransitionToAdjusting(Point point) {
            if (!IsSignificantDrag(firstPoint, point)) {
                // Click without drag — cancel the edit
                Cancel(null);
                return false;
            }
            endPoint = point;
            phase = Phase.Adjusting;
            UpdatePreview(firstPoint, endPoint);
            return true;
        }

        private bool IsSignificantDrag(Point a, Point b) {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy > 25; // minimum 5px drag
        }

        /// <summary>Called by PianoRoll to apply the curve edit.</summary>
        public void Apply() {
            ApplyCurve();
            curveApplied = true;
        }

        /// <summary>Called by PianoRoll to hide the preview after finalization.</summary>
        public void HidePreview() {
            previewLine.IsVisible = false;
            previewLine.Points.Clear();
        }

        /// <summary>Called by PianoRoll to cancel the edit (e.g. right-click during adjusting).</summary>
        public void Cancel(IPointer? pointer) {
            HidePreview();
            pointer?.Capture(null);
            DocManager.Inst.EndUndoGroup();
            valueTip.HideValueTip();
        }

        private void ShowPreview() {
            previewLine.IsVisible = true;
        }

        private void InitDefaultParams() {
            spacingTicks = 120;
            amplitudeCents = 100; // 1 tone
            scurveStrength = 2.0;
        }

        private void UpdateDrawingParams(Point point) {
            var notesVm = vm.NotesViewModel;
            switch (mode) {
                case CurveMode.Sine:
                    valueTip.UpdateValueTip($"\u03BB:{spacingTicks:0} ticks, amp:{amplitudeCents / 100:0.00}tone");
                    break;
                case CurveMode.SCurve:
                    valueTip.UpdateValueTip($"S:{scurveStrength:0.00}");
                    break;
                case CurveMode.Line:
                default:
                    valueTip.UpdateValueTip("line");
                    break;
            }
        }

        private void UpdateAdjustingParams(Point point) {
            var notesVm = vm.NotesViewModel;
            double dx = point.X - prevPoint.X;
            double dy = point.Y - prevPoint.Y;
            switch (mode) {
                case CurveMode.Sine: {
                        int startTick = notesVm.PointToTick(firstPoint);
                        int endTick = notesVm.PointToTick(endPoint);
                        if (startTick > endTick) (startTick, endTick) = (endTick, startTick);
                        int maxSpacing = Math.Max(step, endTick - startTick);
                        // Horizontal: adjust wavelength, Vertical: adjust amplitude
                        spacingTicks = Math.Clamp(spacingTicks + dx, step, maxSpacing);
                        amplitudeCents = Math.Clamp(amplitudeCents - dy * 2, 0, 1200);
                        valueTip.UpdateValueTip($"\u03BB:{spacingTicks:0} ticks, amp:{amplitudeCents / 100:0.00}tone");
                        break;
                    }
                case CurveMode.SCurve: {
                        // Both directions adjust S-curve strength
                        scurveStrength = Math.Clamp(scurveStrength - dy * 0.05, 1.0, 8.0);
                        valueTip.UpdateValueTip($"S:{scurveStrength:0.00}");
                        break;
                    }
            }
        }

        private void UpdatePreview(Point start, Point end) {
            var notesVm = vm.NotesViewModel;
            if (notesVm.Part == null) return;

            var pts = new Points();
            foreach (var (tick, tone) in ComputeSamples(start, end, 1)) {
                pts.Add(notesVm.TickToneToPoint(tick, tone - 0.5));
            }
            previewLine.Points = pts;
        }

        private void ApplyCurve() {
            var notesVm = vm.NotesViewModel;
            if (notesVm.Part == null) return;

            int startTick = notesVm.PointToTick(firstPoint);
            int endTick = notesVm.PointToTick(endPoint);

            if (startTick == endTick) {
                ApplySinglePoint(notesVm, endPoint, endPoint);
                return;
            }
            if (startTick > endTick) {
                (startTick, endTick) = (endTick, startTick);
                (firstPoint, endPoint) = (endPoint, firstPoint);
            }
            if (mode == CurveMode.Sine) {
                spacingTicks = Math.Min(Math.Max(step, spacingTicks), Math.Max(step, endTick - startTick));
            }

            var curveSamples = new List<(int x, int y)>();
            foreach (var (tick, tone) in ComputeSamples(firstPoint, endPoint, step)) {
                var sp = notesVm.TickToneToPoint(tick, tone);
                double? basePitch = overwrite
                    ? notesVm.HitTest.SampleOverwritePitch(sp)
                    : notesVm.HitTest.SamplePitch(sp);
                if (basePitch == null) continue;
                curveSamples.Add((tick, (int)Math.Round(tone * 100 - basePitch.Value)));
            }
            if (curveSamples.Count == 0) return;

            // Compute boundary anchor points to avoid interpolation from far-away points
            var (startAnchor, endAnchor) = ComputeBoundaryAnchors(notesVm, startTick, endTick, curveSamples);

            var part = notesVm.Part!;
            var project = notesVm.Project;
            if (overwrite) {
                var curve = part.curves.FirstOrDefault(c => c.abbr == Core.Format.Ustx.PITD);
                var oldXs = curve != null ? curve.xs.ToArray() : Array.Empty<int>();
                var oldYs = curve != null ? curve.ys.ToArray() : Array.Empty<int>();
                var newXs = new List<int>();
                var newYs = new List<int>();
                for (int i = 0; i < oldXs.Length; i++) {
                    if (oldXs[i] < startTick) { newXs.Add(oldXs[i]); newYs.Add(oldYs[i]); }
                }
                // Insert start boundary anchor if there's a gap
                if (startAnchor.HasValue) {
                    var (ax, ay) = startAnchor.Value;
                    if (newXs.Count == 0 || newXs[newXs.Count - 1] < ax) {
                        newXs.Add(ax); newYs.Add(ay);
                    }
                }
                foreach (var (x, y) in curveSamples) { newXs.Add(x); newYs.Add(y); }
                // Insert end boundary anchor if there's a gap
                if (endAnchor.HasValue) {
                    var (ax, ay) = endAnchor.Value;
                    newXs.Add(ax); newYs.Add(ay);
                }
                for (int i = 0; i < oldXs.Length; i++) {
                    if (oldXs[i] > endTick) { newXs.Add(oldXs[i]); newYs.Add(oldYs[i]); }
                }
                DocManager.Inst.ExecuteCmd(new MergedSetCurveCommand(project, part, Core.Format.Ustx.PITD, oldXs, oldYs, newXs.ToArray(), newYs.ToArray()));
            } else {
                // Insert start boundary anchor if there's a gap
                if (startAnchor.HasValue) {
                    var (ax, ay) = startAnchor.Value;
                    DocManager.Inst.ExecuteCmd(new SetCurveCommand(project, part, Core.Format.Ustx.PITD, ax, ay, ax, ay));
                }
                foreach (var (x, y) in curveSamples)
                    DocManager.Inst.ExecuteCmd(new SetCurveCommand(project, part, Core.Format.Ustx.PITD, x, y, x, y));
                // Insert end boundary anchor if there's a gap
                if (endAnchor.HasValue) {
                    var (ax, ay) = endAnchor.Value;
                    DocManager.Inst.ExecuteCmd(new SetCurveCommand(project, part, Core.Format.Ustx.PITD, ax, ay, ax, ay));
                }
            }
        }

        /// <summary>
        /// Computes anchor points at the curve boundaries to prevent interpolation
        /// from far-away existing points. Returns (tick, deviation) or null if no anchor needed.
        /// </summary>
        private ((int x, int y)? startAnchor, (int x, int y)? endAnchor) ComputeBoundaryAnchors(
                NotesViewModel notesVm, int startTick, int endTick, List<(int x, int y)> samples) {
            var curve = notesVm.Part?.curves.FirstOrDefault(c => c.abbr == Core.Format.Ustx.PITD);
            int[] oldXs = curve != null ? curve.xs.ToArray() : Array.Empty<int>();
            int[] oldYs = curve != null ? curve.ys.ToArray() : Array.Empty<int>();

            (int x, int y)? startAnchor = null;
            (int x, int y)? endAnchor = null;

            int firstSampleTick = samples[0].x;
            int lastSampleTick = samples[samples.Count - 1].x;

            // Find nearest existing point before the curve start
            int nearestBeforeStart = -1;
            for (int i = 0; i < oldXs.Length; i++) {
                if (oldXs[i] < firstSampleTick) nearestBeforeStart = i;
                else break;
            }

            // Find nearest existing point after the curve end
            int nearestAfterEnd = -1;
            for (int i = oldXs.Length - 1; i >= 0; i--) {
                if (oldXs[i] > lastSampleTick) nearestAfterEnd = i;
                else break;
            }

            // Compute anchor at start boundary: one step before first sample
            int anchorStartTick = firstSampleTick - step;
            if (nearestBeforeStart >= 0) {
                // There's an existing point before - use its value at the anchor position
                if (oldXs[nearestBeforeStart] <= anchorStartTick) {
                    // Existing point is at or before anchor - use its deviation value
                    startAnchor = (anchorStartTick, oldYs[nearestBeforeStart]);
                }
                // If existing point is between anchor and first sample, no anchor needed
                // (the existing point will serve as the bridge)
            } else {
                // No existing point before - anchor at zero deviation
                startAnchor = (anchorStartTick, 0);
            }

            // Compute anchor at end boundary: one step after last sample
            int anchorEndTick = lastSampleTick + step;
            if (nearestAfterEnd >= 0) {
                // There's an existing point after - use its value at the anchor position
                if (oldXs[nearestAfterEnd] >= anchorEndTick) {
                    // Existing point is at or after anchor - use its deviation value
                    endAnchor = (anchorEndTick, oldYs[nearestAfterEnd]);
                }
                // If existing point is between last sample and anchor, no anchor needed
            } else {
                // No existing point after - anchor at zero deviation
                endAnchor = (anchorEndTick, 0);
            }

            return (startAnchor, endAnchor);
        }

        private void ApplySinglePoint(NotesViewModel notesVm, Point point, Point lastPoint) {
            var part = notesVm.Part;
            if (part == null) return;
            int tick = notesVm.PointToTick(point);
            var sp = notesVm.TickToneToPoint((int)Math.Round(tick / (double)step) * step, notesVm.PointToToneDouble(point));
            double? pitch = overwrite
                ? notesVm.HitTest.SampleOverwritePitch(sp)
                : notesVm.HitTest.SamplePitch(sp);
            if (pitch == null) return;
            double tone = notesVm.PointToToneDouble(point);
            int y = (int)Math.Round(tone * 100 - pitch.Value);
            if (overwrite) {
                var curve = part.curves.FirstOrDefault(c => c.abbr == Core.Format.Ustx.PITD);
                var oldXs = curve != null ? curve.xs.ToArray() : Array.Empty<int>();
                var oldYs = curve != null ? curve.ys.ToArray() : Array.Empty<int>();
                var newXs = new List<int>();
                var newYs = new List<int>();
                for (int i = 0; i < oldXs.Length; i++) {
                    if (oldXs[i] != tick) { newXs.Add(oldXs[i]); newYs.Add(oldYs[i]); }
                }
                newXs.Add(tick);
                newYs.Add(y);
                DocManager.Inst.ExecuteCmd(new MergedSetCurveCommand(
                    notesVm.Project, part, Core.Format.Ustx.PITD,
                    oldXs, oldYs, newXs.ToArray(), newYs.ToArray()));
            } else {
                DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                    notesVm.Project, part, Core.Format.Ustx.PITD,
                    notesVm.PointToTick(point), y,
                    notesVm.PointToTick(lastPoint), y));
            }
        }

        /// <summary>
        /// Computes samples along the curve from start to end point, snapping to step boundaries.
        /// Returns (tick, tone) pairs. Handles tick swapping internally.
        /// </summary>
        private IEnumerable<(int tick, double tone)> ComputeSamples(Point start, Point end, int sampleStep) {
            var notesVm = vm.NotesViewModel;
            int startTick = notesVm.PointToTick(start);
            int endTick = notesVm.PointToTick(end);
            double startTone = notesVm.PointToToneDouble(start);
            double endTone = notesVm.PointToToneDouble(end);

            if (startTick == endTick) {
                yield return (startTick, notesVm.PointToToneDouble(end));
                yield break;
            }
            if (startTick > endTick) {
                (startTick, endTick) = (endTick, startTick);
                (startTone, endTone) = (endTone, startTone);
            }

            int firstSample = (int)Math.Round(startTick / (double)step) * step;
            int lastSample = (int)Math.Round(endTick / (double)step) * step;
            if (firstSample < startTick) firstSample += step;
            if (lastSample > endTick) lastSample -= step;

            double total = endTick - startTick;
            int effectiveSpacing = mode == CurveMode.Sine
                ? Math.Min(Math.Max(step, (int)spacingTicks), Math.Max(step, (int)total))
                : 0;

            for (int x = firstSample; x <= lastSample; x += sampleStep) {
                double t = (x - startTick) / total;
                double tone = ComputeTone(startTone, endTone, t, x, (int)total, startTick, effectiveSpacing);
                yield return (x, tone);
            }
        }

        private double ComputeTone(double startTone, double endTone, double t, int tick, int totalTicks, int sineStartTick = 0, int? spacingOverride = null) {
            switch (mode) {
                case CurveMode.Sine:
                    double center = startTone + (endTone - startTone) * t;
                    double sp = spacingOverride ?? spacingTicks;
                    double fadeTicks = Math.Max(step, Math.Min(totalTicks * 0.1, sp));
                    double fadeIn = Math.Clamp((tick - sineStartTick) / fadeTicks, 0, 1);
                    double fadeOut = Math.Clamp((sineStartTick + totalTicks - tick) / fadeTicks, 0, 1);
                    double envelope = Math.Min(fadeIn, fadeOut);
                    double phase = 2 * Math.PI * (tick - sineStartTick) / sp;
                    return center + Math.Sin(phase) * amplitudeCents / 100.0 * envelope;
                case CurveMode.SCurve:
                    double a = Math.Pow(t, scurveStrength);
                    double b = Math.Pow(1 - t, scurveStrength);
                    return startTone + (endTone - startTone) * (a / (a + b));
                default:
                    return startTone + (endTone - startTone) * t;
            }
        }
    }
    class SmoothenPitchState : NoteEditState {
        protected override bool ShowValueTip => false;
        protected override string? commandNameKey => "command.pitch.edit";
        private readonly bool overwrite;
        int brushRadius = 10;
        int kernelRadius = 3;
        double kernelWeight = 1.0 / (2 * 3 + 1);

        public SmoothenPitchState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip,
            bool overwrite = false) : base(control, vm, valueTip) {
            this.overwrite = overwrite;
        }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
        }
        private double GetPitch(int tick, UCurve? curve = null) {
            var point = vm.NotesViewModel.TickToneToPoint(tick, 0);
            var pitch = overwrite
                ? vm.NotesViewModel.HitTest.SampleOverwritePitch(point)
                : vm.NotesViewModel.HitTest.SamplePitch(point);
            if (pitch == null) return 0;
            if (curve == null) return pitch.Value;
            return pitch.Value + curve.Sample(tick);
        }
        public override void Update(IPointer pointer, Point point) {
            if (vm.NotesViewModel.Part == null) return;
            var curve = vm.NotesViewModel.Part.curves.FirstOrDefault(c => c.abbr == Core.Format.Ustx.PITD);
            if (curve == null) return;
            double total = 0;
            List<(int tick, int pitch)> newPoints = new List<(int tick, int pitch)>();
            int baseTick = ((int)Math.Round(vm.NotesViewModel.PointToTick(point) / 5.0) - brushRadius) * 5;
            for (int i = -kernelRadius; i <= kernelRadius; i++) total += GetPitch(baseTick + i * 5, curve);
            newPoints.Add((baseTick, (int)Math.Round(total * kernelWeight - GetPitch(baseTick))));
            total -= GetPitch(baseTick - kernelRadius * 5, curve);
            for (int i = -brushRadius + 1; i <= brushRadius; i++) {
                baseTick += 5;
                total += GetPitch(baseTick + kernelRadius * 5, curve);
                newPoints.Add((baseTick, (int)Math.Round(total * kernelWeight - GetPitch(baseTick))));
                total -= GetPitch(baseTick - kernelRadius * 5, curve);
            }
            foreach (var (tick, pitch) in newPoints)
                DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                    vm.NotesViewModel.Project,
                    vm.NotesViewModel.Part,
                    Core.Format.Ustx.PITD,
                    tick, pitch,
                    tick, pitch));
        }
    }

    class ResetPitchState : NoteEditState {
        public override MouseButton MouseButton => MouseButton.Right;
        protected override bool ShowValueTip => false;
        protected override string? commandNameKey => "command.pitch.reset";
        Point lastPoint;

        public ResetPitchState(
            Control control,
            PianoRollViewModel vm,
            IValueTip valueTip) : base(control, vm, valueTip) { }
        public override void Begin(IPointer pointer, Point point) {
            base.Begin(pointer, point);
            lastPoint = point;
        }
        public override void Update(IPointer pointer, Point point) {
            if (vm.NotesViewModel.Part == null) {
                return;
            }
            DocManager.Inst.ExecuteCmd(new SetCurveCommand(
                vm.NotesViewModel.Project,
                vm.NotesViewModel.Part,
                Core.Format.Ustx.PITD,
                vm.NotesViewModel.PointToTick(point),
                0,
                vm.NotesViewModel.PointToTick(lastPoint),
                0));
            lastPoint = point;
        }
    }
}
