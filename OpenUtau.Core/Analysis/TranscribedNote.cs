namespace OpenUtau.Core.Analysis;

public class TranscribedNote {
    /// <summary>Duration of the note in seconds.</summary>
    public float noteDuration;

    /// <summary>MIDI note number (pitch score).</summary>
    public float noteScore;

    /// <summary>Whether the note is voiced (not a rest).</summary>
    public bool noteVoiced;

    public TranscribedNote(float noteDuration, float noteScore, bool noteVoiced) {
        this.noteDuration = noteDuration;
        this.noteScore = noteScore;
        this.noteVoiced = noteVoiced;
    }
}

