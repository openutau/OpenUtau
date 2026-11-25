namespace OpenUtau.Core.Util.MusicTheory;

public enum Mode {
    Ionian,
    Aeolian,
    Lydian,
    Mixolydian,
    Dorian,
    Phrygian,
    Locrian,
}

public static class ModeHelper {

    public static string StringifyMode(Mode mode) {
        return mode switch {
            Mode.Ionian => "Ionian (Major)",
            Mode.Aeolian => "Aeolian (Natural Minor)",
            _ => mode.ToString()
        };
    }
}