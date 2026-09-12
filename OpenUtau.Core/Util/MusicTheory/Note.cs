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
        return note.ToString().Replace("sharp", "#");
    }
}
