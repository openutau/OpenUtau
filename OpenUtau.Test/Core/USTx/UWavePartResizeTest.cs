using System.Collections.Generic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class UWavePartResizeTest {
        static (UProject project, UWavePart part) CreateCroppedPart(bool changingTempo, int clipLength = 2400) {
            var project = new UProject();
            if (changingTempo) {
                project.tempos[0].bpm = 137;
                project.tempos.Add(new UTempo(700, 93));
                project.tempos.Add(new UTempo(1500, 171));
                project.timeAxis.BuildSegments(project);
            }
            var source = new UWavePart {
                position = 321,
                skip = 123,
                trim = 87,
                fileDurationMs = 12000,
            };
            source.Validate(new ValidateOptions(), project, project.tracks[0]);
            source.trim += source.Duration - clipLength;
            source.Validate(new ValidateOptions(), project, project.tracks[0]);
            project.parts.Add(source);
            return (project, source);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ResizeAndUndoExposeCurrentGeometryBeforeProjectValidation(bool fromStart, bool changingTempo) {
            var (project, part) = CreateCroppedPart(changingTempo);
            int oldPosition = part.position;
            int oldEnd = part.End;
            int oldSkip = part.skip;
            int oldTrim = part.trim;
            int oldDuration = part.Duration;
            var command = new ResizeWavePartCommand(project, part, -600, fromStart);

            command.Execute();

            Assert.Equal(oldDuration - 600, part.Duration);
            Assert.Equal(fromStart ? oldPosition + 600 : oldPosition, part.position);
            Assert.Equal(fromStart ? oldEnd : oldEnd - 600, part.End);
            Assert.Equal(fromStart ? oldSkip + 600 : oldSkip, part.skip);
            Assert.Equal(fromStart ? oldTrim : oldTrim + 600, part.trim);
            AssertValidationDoesNotChangeGeometry(project, part);

            command.Unexecute();

            Assert.Equal(oldPosition, part.position);
            Assert.Equal(oldEnd, part.End);
            Assert.Equal(oldDuration, part.Duration);
            Assert.Equal(oldSkip, part.skip);
            Assert.Equal(oldTrim, part.trim);
            AssertValidationDoesNotChangeGeometry(project, part);

            command.Execute();
            Assert.Equal(oldDuration - 600, part.Duration);
            AssertValidationDoesNotChangeGeometry(project, part);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RepeatedRightEdgeTargetsDoNotAccumulateTrimmingBetweenFrames(bool changingTempo) {
            var (project, part) = CreateCroppedPart(changingTempo);
            int originalEnd = part.End;
            int originalTrim = part.trim;
            var commands = new List<ResizeWavePartCommand>();

            // Several pointer moves can arrive before DocManager's next frame validation.
            // A duplicate target must not apply the preceding reduction a second time.
            foreach (int length in new[] { 1200, 1200, 900, 900, 600, 600, 1800, 1800 }) {
                int target = part.position + length;
                int delta = target - part.End;
                if (delta != 0) {
                    var command = new ResizeWavePartCommand(project, part, delta, false);
                    commands.Add(command);
                    command.Execute();
                }
                Assert.Equal(target, part.End);
                Assert.Equal(length, part.Duration);
                Assert.Equal(originalTrim + originalEnd - target, part.trim);
            }
            AssertValidationDoesNotChangeGeometry(project, part);

            for (int i = commands.Count - 1; i >= 0; i--) {
                commands[i].Unexecute();
            }
            Assert.Equal(originalEnd, part.End);
            Assert.Equal(originalTrim, part.trim);
            Assert.Equal(2400, part.Duration);

            foreach (var command in commands) {
                command.Execute();
            }
            Assert.Equal(1800, part.Duration);
            AssertValidationDoesNotChangeGeometry(project, part);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExtendingASubBeatClipUpdatesItsDurationImmediately(bool fromStart) {
            var (project, part) = CreateCroppedPart(false, 120);
            int originalEnd = part.End;
            var command = new ResizeWavePartCommand(project, part, 60, fromStart);

            command.Execute();

            Assert.Equal(180, part.Duration);
            Assert.Equal(fromStart ? originalEnd : originalEnd + 60, part.End);
            AssertValidationDoesNotChangeGeometry(project, part);

            command.Unexecute();
            Assert.Equal(120, part.Duration);
            Assert.Equal(originalEnd, part.End);
        }

        static void AssertValidationDoesNotChangeGeometry(UProject project, UWavePart part) {
            int duration = part.Duration;
            int end = part.End;
            project.Validate(new ValidateOptions { SkipTiming = true });
            Assert.Equal(duration, part.Duration);
            Assert.Equal(end, part.End);
        }
    }
}
