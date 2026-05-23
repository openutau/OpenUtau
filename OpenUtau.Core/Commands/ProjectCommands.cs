using System;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public abstract class ProjectCommand : UCommand {
        public readonly UProject project;
        public ProjectCommand(UProject project) {
            this.project = project;
        }
    }

    public class BpmCommand : ProjectCommand {
        public readonly double newBpm;
        public readonly double oldBpm;
        public BpmCommand(UProject project, double bpm) : base(project) {
            newBpm = Math.Clamp(bpm, 10, 1000);
            oldBpm = project.tempos[0].bpm;
        }
        public override void Execute() => project.tempos[0].bpm = newBpm;
        public override string ToString() => $"Change BPM from {newBpm} to {oldBpm}";
        public override void Unexecute() => project.tempos[0].bpm = oldBpm;
    }

    public class AddTempoChangeCommand : ProjectCommand {
        protected int tick;
        protected double bpm;
        public AddTempoChangeCommand(UProject project, int tick, double bpm) : base(project) {
            this.tick = tick;
            this.bpm = bpm;
        }
        protected AddTempoChangeCommand(UProject project) : base(project) { }
        public override void Execute() {
            int index = project.tempos.FindIndex(timSig => timSig.position > tick);
            var tempo = new UTempo {
                position = tick,
                bpm = bpm,
            };
            if (index >= 0) {
                project.tempos.Insert(index - 1, tempo);
            } else {
                project.tempos.Add(tempo);
            }
        }
        public override void Unexecute() {
            int index = project.tempos.FindIndex(tempo => tempo.position == tick);
            if (index >= 0) {
                project.tempos.RemoveAt(index);
            } else {
                throw new Exception("Cannot remove non-exist tempo change.");
            }
        }
        public override string ToString() => $"Add tempo change {bpm} at {tick}";
    }

    public class DelTempoChangeCommand : AddTempoChangeCommand {
        public DelTempoChangeCommand(UProject project, int tick) : base(project) {
            this.tick = tick;
            var tempo = project.tempos.Find(tempo => tempo.position == tick);
            bpm = tempo.bpm;
        }
        public override void Execute() {
            base.Unexecute();
        }
        public override void Unexecute() {
            base.Execute();
        }
        public override string ToString() => $"Del tempo change {bpm} at {tick}";
    }

    public class AddTimeSigCommand : ProjectCommand {
        protected int bar;
        protected int beatPerBar;
        protected int beatUnit;
        public AddTimeSigCommand(UProject project, int bar, int beatPerBar, int beatUnit) : base(project) {
            this.bar = bar;
            this.beatPerBar = beatPerBar;
            this.beatUnit = beatUnit;
        }
        protected AddTimeSigCommand(UProject project) : base(project) { }
        public override void Execute() {
            int index = project.timeSignatures.FindIndex(timSig => timSig.barPosition > bar);
            var timeSig = new UTimeSignature {
                barPosition = bar,
                beatPerBar = beatPerBar,
                beatUnit = beatUnit,
            };
            if (index >= 0) {
                project.timeSignatures.Insert(index - 1, timeSig);
            } else {
                project.timeSignatures.Add(timeSig);
            }
        }
        public override void Unexecute() {
            int index = project.timeSignatures.FindIndex(timSig => timSig.barPosition == bar);
            if (index >= 0) {
                project.timeSignatures.RemoveAt(index);
            } else {
                throw new Exception("Cannot remove non-exist time signature change");
            }
        }
        public override string ToString() => $"Add time sig change {beatPerBar}/{beatUnit} at bar {bar}";
    }

    public class DelTimeSigCommand : AddTimeSigCommand {
        public DelTimeSigCommand(UProject project, int bar) : base(project) {
            this.bar = bar;
            var timeSig = project.timeSignatures.Find(timSig => timSig.barPosition == bar);
            beatPerBar = timeSig.beatPerBar;
            beatUnit = timeSig.beatUnit;
        }
        public override void Execute() {
            base.Unexecute();
        }
        public override void Unexecute() {
            base.Execute();
        }
        public override string ToString() => $"Del time sig change {beatPerBar}/{beatUnit} at bar {bar}";
    }

    public class TimeSignatureCommand : ProjectCommand {
        public readonly int oldBeatPerBar;
        public readonly int oldBeatUnit;
        public readonly int newBeatPerBar;
        public readonly int newBeatUnit;
        public TimeSignatureCommand(UProject project, int beatPerBar, int beatUnit) : base(project) {
            oldBeatPerBar = project.timeSignatures[0].beatPerBar;
            oldBeatUnit = project.timeSignatures[0].beatUnit;
            newBeatPerBar = beatPerBar;
            newBeatUnit = beatUnit;
        }
        public override string ToString() => $"Change time signature for {oldBeatPerBar}/{oldBeatUnit} to {newBeatPerBar}/{newBeatUnit}";
        public override void Execute() {
            project.timeSignatures[0].beatPerBar = newBeatPerBar;
            project.timeSignatures[0].beatUnit = newBeatUnit;
        }
        public override void Unexecute() {
            project.timeSignatures[0].beatPerBar = oldBeatPerBar;
            project.timeSignatures[0].beatUnit = oldBeatUnit;
        }
    }

    public class KeyCommand : ProjectCommand{
        public readonly int oldKey;
        public readonly int newKey;
        public KeyCommand(UProject project, int key) : base(project) {
            oldKey = project.key;
            newKey = key;
        }
        public override string ToString() => $"Change key from {oldKey} to {newKey}";
        public override void Execute() => project.key = newKey;
        public override void Unexecute() => project.key = oldKey;
    }

    public class ConfigureExpressionsCommand : ProjectCommand {
        readonly UExpressionDescriptor[] oldProjectDescriptors;
        readonly UExpressionDescriptor[] newProjectDescriptors;
        readonly UExpressionDescriptor[] oldTrackDescriptors;
        readonly UExpressionDescriptor[] newTrackDescriptors;
        readonly UTrack? track;
        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
        };
        public ConfigureExpressionsCommand(
            UProject project,
            UExpressionDescriptor[] descriptors) : base(project) {
            oldProjectDescriptors = project.expressions.Values.ToArray();
            newProjectDescriptors = descriptors;
        }
        public ConfigureExpressionsCommand(
            UProject project,
            UExpressionDescriptor[] projectDescriptors,
            UTrack track,
            UExpressionDescriptor[] trackDescriptors) : base(project) {
            oldProjectDescriptors = project.expressions.Values.ToArray();
            newProjectDescriptors = projectDescriptors;
            oldTrackDescriptors = track.TrackExpressions.ToArray();
            newTrackDescriptors = trackDescriptors;
            this.track = track;
        }
        public override string ToString() => "Configure expressions";
        public override void Execute() {
            project.expressions = newProjectDescriptors.ToDictionary(descriptor => descriptor.abbr);
            Format.Ustx.AddDefaultExpressions(project);
            if (track != null) {
                track.TrackExpressions = newTrackDescriptors.ToList();
            }
            project.parts
                .Where(part => part is UVoicePart)
                .ToList()
                .ForEach(part => part.AfterLoad(project, project.tracks[part.trackNo]));
            project.ValidateFull();
        }
        public override void Unexecute() {
            project.expressions = oldProjectDescriptors.ToDictionary(descriptor => descriptor.abbr);
            if (track != null) {
                track.TrackExpressions = oldTrackDescriptors.ToList();
            }
            project.parts
                .Where(part => part is UVoicePart)
                .ToList()
                .ForEach(part => part.AfterLoad(project, project.tracks[part.trackNo]));
            project.ValidateFull();
        }
    }
}
