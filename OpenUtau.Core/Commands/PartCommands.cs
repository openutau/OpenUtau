using OpenUtau.Core.Ustx;
using SharpCompress;

namespace OpenUtau.Core {
    public abstract class PartCommand : UCommand {
        public readonly UProject project;
        public readonly UPart part;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
        };
        public PartCommand(UProject project, UPart part) {
            this.project = project;
            this.part = part;
        }
    }

    public class AddPartCommand : PartCommand {
        public AddPartCommand(UProject project, UPart part) : base(project, part) { }
        public override string ToString() => "Add part";
        public override void Execute() => project.parts.Add(part);
        public override void Unexecute() => project.parts.Remove(part);
    }

    public class RemovePartCommand : PartCommand {
        public RemovePartCommand(UProject project, UPart part) : base(project, part) { }
        public override string ToString() => "Remove parts";
        public override void Execute() => project.parts.Remove(part);
        public override void Unexecute() => project.parts.Add(part);
    }

    public class MovePartCommand : PartCommand {
        public readonly int newPos;
        public readonly int oldPos;
        public readonly int newTrackNo;
        public readonly int oldTrackNo;
        public MovePartCommand(UProject project, UPart part, int position, int trackNo) : base(project, part) {
            newPos = position;
            newTrackNo = trackNo;
            oldPos = part.position;
            oldTrackNo = part.trackNo;
        }
        public override string ToString() => "Move parts";
        public override void Execute() {
            part.position = newPos;
            part.trackNo = newTrackNo;
        }
        public override void Unexecute() {
            part.position = oldPos;
            part.trackNo = oldTrackNo;
        }
    }

    public class ResizeVoicePartCommand : PartCommand {
        public readonly new UVoicePart part;
        readonly int deltaDur;
        readonly bool fromStart;
        public ResizeVoicePartCommand(UProject project, UVoicePart part, int deltaDur, bool fromStart) : base(project, part) {
            this.part = part;
            this.deltaDur = deltaDur;
            this.fromStart = fromStart;
        }
        public override string ToString() => "Change parts duration";
        public override void Execute() {
            if (fromStart) {
                part.position -= deltaDur;
                part.Duration += deltaDur;
                part.notes.ForEach(note => note.position += deltaDur);
                foreach (var curve in part.curves) {
                    for (var i = 0; i < curve.xs.Count; i++) {
                        curve.xs[i] += deltaDur;
                    }
                }
            } else {
                part.Duration += deltaDur;
            }
        }
        public override void Unexecute() {
            if (fromStart) {
                part.position += deltaDur;
                part.Duration -= deltaDur;
                part.notes.ForEach(note => note.position -= deltaDur);
                foreach (var curve in part.curves) {
                    for (var i = 0; i < curve.xs.Count; i++) {
                        curve.xs[i] -= deltaDur;
                    }
                }
            } else {
                part.Duration -= deltaDur;
            }
        }
    }

    public class ResizeWavePartCommand : PartCommand {
        public readonly new UWavePart part;
        readonly int deltaDur;
        readonly bool fromStart;
        public ResizeWavePartCommand(UProject project, UWavePart part, int deltaDur, bool fromStart) : base(project, part) {
            this.part = part;
            this.deltaDur = deltaDur;
            this.fromStart = fromStart;
        }
        public override string ToString() => "Change parts duration";
        public override void Execute() {
            if (fromStart) {
                part.position -= deltaDur;
                part.skip -= deltaDur;
            } else {
                part.trim -= deltaDur;
            }
        }
        public override void Unexecute() {
            if (fromStart) {
                part.position += deltaDur;
                part.skip += deltaDur;
            } else {
                part.trim += deltaDur;
            }
        }
    }

    public class PartFadeInCommand : PartCommand {
        public readonly new UWavePart part;
        readonly int fadeTick;
        readonly int oldFade;
        public PartFadeInCommand(UProject project, UWavePart part, int fadeTick) : base(project, part) {
            this.part = part;
            this.fadeTick = fadeTick;
            this.oldFade = part.fadein;
        }
        public override string ToString() => "Fade in part";
        public override void Execute() {
            part.fadein = fadeTick;
        }
        public override void Unexecute() {
            part.fadein = oldFade;
        }
    }

    public class PartFadeOutCommand : PartCommand {
        public readonly new UWavePart part;
        readonly int fadeTick;
        readonly int oldFade;
        public PartFadeOutCommand(UProject project, UWavePart part, int fadeTick) : base(project, part) {
            this.part = part;
            this.fadeTick = fadeTick;
            this.oldFade = part.fadeout;
        }
        public override string ToString() => "Fade out part";
        public override void Execute() {
            part.fadeout = fadeTick;
        }
        public override void Unexecute() {
            part.fadeout = oldFade;
        }
    }

    public class RenamePartCommand : PartCommand {
        readonly string newName, oldName;
        public RenamePartCommand(UProject project, UPart part, string name) : base(project, part) {
            newName = name;
            oldName = part.name;
        }
        public override string ToString() => "Rename part";
        public override void Execute() => part.name = newName;
        public override void Unexecute() => part.name = oldName;
    }

    public class ReplacePartCommand : PartCommand {
        public readonly int index;
        public readonly UPart newPart;
        public ReplacePartCommand(UProject project, UPart part, UPart newPart) : base(project, part) {
            index = project.parts.IndexOf(part);
            this.newPart = newPart;
        }
        public override string ToString() => "Replace part";
        public override void Execute() => project.parts[index] = newPart;
        public override void Unexecute() => project.parts[index] = part;
    }
}
