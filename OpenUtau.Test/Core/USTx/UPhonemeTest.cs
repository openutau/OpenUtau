using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class UPhonemeTest {
        [Fact]
        public void DurationErrorClearsWhenPhonemeMovedBackIntoNote() {
            var project = new UProject();
            project.timeAxis.BuildSegments(project);
            var track = new UTrack();
            var part = new UVoicePart();
            var note = UNote.Create();
            note.position = 0;
            note.duration = 120;
            note.ExtendedDuration = note.duration;

            var phoneme = new UPhoneme { Parent = note, position = 60 };

            // Dragging the phoneme offset beyond the end of the note makes the
            // duration non-positive.
            phoneme.position = 130;
            phoneme.Validate(default, project, track, part, note);
            Assert.True(phoneme.Error);
            Assert.Equal("Phoneme duration is not positive.", phoneme.ErrorException?.Message);

            // Moving the offset back must clear the stale duration error instead
            // of keeping the phoneme in error until re-phonemization.
            phoneme.position = 60;
            phoneme.Validate(default, project, track, part, note);
            Assert.NotEqual("Phoneme duration is not positive.", phoneme.ErrorException?.Message);
        }
    }
}
