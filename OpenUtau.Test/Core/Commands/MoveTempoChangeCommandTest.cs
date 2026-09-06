using System.Collections.Generic;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class MoveTempoChangeCommandTest {
        [Fact]
        public void ExecuteAndUnexecute() {
            var project = new UProject();
            var tempo = new UTempo(480, 140);
            project.tempos.Add(tempo);
            var command = new MoveTempoChangeCommand(project, tempo, 720);

            command.Execute();
            Assert.Equal(720, tempo.position);

            command.Unexecute();
            Assert.Equal(480, tempo.position);

            command.Execute();
            Assert.Equal(720, tempo.position);
        }

        [Fact]
        public void UnexecuteAfterTempoIsRecreated() {
            var project = new UProject();
            var tempo = new UTempo(480, 140);
            project.tempos.Add(tempo);
            var command = new MoveTempoChangeCommand(project, tempo, 720);
            command.Execute();

            project.tempos.Remove(tempo);
            var recreatedTempo = new UTempo(720, 140);
            project.tempos.Add(recreatedTempo);

            command.Unexecute();
            Assert.Equal(480, recreatedTempo.position);

            command.Execute();
            Assert.Equal(720, recreatedTempo.position);
        }

        [Fact]
        public void MergeKeepsFirstAndLastPositions() {
            var project = new UProject();
            var tempo = new UTempo(480, 140);
            project.tempos.Add(tempo);
            var firstMove = new MoveTempoChangeCommand(project, tempo, 600);
            firstMove.Execute();
            var secondMove = new MoveTempoChangeCommand(project, tempo, 720);
            secondMove.Execute();
            var commands = new List<UCommand> { firstMove, secondMove };

            Assert.True(secondMove.CanMerge(commands));
            var merged = secondMove.Merge(commands);

            merged.Unexecute();
            Assert.Equal(480, tempo.position);

            merged.Execute();
            Assert.Equal(720, tempo.position);
        }
    }
}
