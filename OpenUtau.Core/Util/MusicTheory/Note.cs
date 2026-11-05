using System;

namespace OpenUtau.Core.Util.MusicTheory;

public enum Note
{
    C,
    Csharp,
    D,
    Dsharp,
    E,
    F,
    Fsharp,
    G,
    Gsharp,
    A,
    Asharp,
    B,
}

public static class NoteHelper
{
    public static Note CastNote(int note)
    {
        note %= Enum.GetValues<Note>().Length;
        if (note < 0)
            return Note.C;
        else
            return (Note)note;
    }

    public static string StringifyNote(Note note)
    {
        var solfegeNotation = Preferences.Default.DegreeStyle == 1;
        if (solfegeNotation)
        {
            return note switch
            {
                Note.C => "do",
                Note.Csharp => "do#",
                Note.D => "re",
                Note.Dsharp => "re#",
                Note.E => "mi",
                Note.F => "fa",
                Note.Fsharp => "fa#",
                Note.G => "sol",
                Note.Gsharp => "sol#",
                Note.A => "la",
                Note.Asharp => "la#",
                Note.B => "ti",
                _ => "N/A",
            };
        }
        return note.ToString().Replace("sharp", "#");
    }

    public static string StringifyTone(int tone)
    {
        return tone < 0 ? string.Empty : StringifyNote(CastNote(tone)) + (tone / 12 - 1).ToString();
    }
}
