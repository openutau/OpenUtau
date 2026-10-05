using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public class MoveTempoChangeCommand : AddTempoChangeCommand {
        private readonly UTempo tempo;
        private readonly int oldTick;
        private readonly int newTick;

        public MoveTempoChangeCommand(UProject project, UTempo tempo, int newTick)
            : this(project, tempo, tempo.position, newTick) { }

        private MoveTempoChangeCommand(
            UProject project,
            UTempo tempo,
            int oldTick,
            int newTick) : base(project) {
            this.tempo = tempo;
            this.oldTick = oldTick;
            this.newTick = newTick;
        }

        public override void Execute() => MoveTempo(oldTick, newTick);

        public override void Unexecute() => MoveTempo(newTick, oldTick);

        private void MoveTempo(int fromTick, int toTick) {
            var currentTempo = project.tempos.Contains(tempo) && tempo.position == fromTick
                ? tempo
                : project.tempos.FirstOrDefault(candidate => candidate.position == fromTick);
            if (currentTempo == null) {
                throw new InvalidOperationException(
                    $"Cannot find tempo change at {fromTick} to move to {toTick}.");
            }
            currentTempo.position = toTick;
        }

        public override bool CanMerge(IList<UCommand> commands) {
            var moves = commands.OfType<MoveTempoChangeCommand>().ToList();
            return moves.Count == commands.Count &&
                moves.All(command => command.tempo == tempo);
        }

        public override UCommand Merge(IList<UCommand> commands) {
            var moves = commands.Cast<MoveTempoChangeCommand>().ToList();
            return new MoveTempoChangeCommand(
                project,
                tempo,
                moves.First().oldTick,
                moves.Last().newTick);
        }

        public override string ToString() =>
            $"Move tempo change {tempo.bpm} from {oldTick} to {newTick}";
    }
}
