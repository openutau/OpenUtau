using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core.Util.MusicTheory;

public class Scale
{
    public readonly Mode Mode;
    private readonly List<Note> _scale;
    public Note Tonic => _scale[0];

    private Scale(List<Note> notes, Mode mode)
    {
        _scale = notes;
        Mode = mode;
    }

    public bool IsOutOfScale(Note note)
    {
        return _scale.Contains(note) == false;
    }

    public bool IsTonic(Note note)
    {
        return Tonic.Equals(note);
    }

    public int? Interval(Note note)
    {
        int index = _scale.IndexOf(note);
        return index >= 0 ? index + 1 : null;
    }

    public string? SolfegeIntervalName(Note note)
    {
        if (IsOutOfScale(note))
            return null;

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
            _ => null,
        };
    }

    public static Scale Build(Note tonic, Mode mode)
    {
        int tonicValue = (int)tonic;
        int[] intervals = mode switch
        {
            Mode.Lydian => [0, 2, 4, 6, 7, 9, 11],
            Mode.Ionian => [0, 2, 4, 5, 7, 9, 11],
            Mode.Mixolydian => [0, 2, 4, 5, 7, 9, 10],
            Mode.Dorian => [0, 2, 3, 5, 7, 9, 10],
            Mode.Aeolian => [0, 2, 3, 5, 7, 8, 10],
            Mode.Phrygian => [0, 1, 3, 5, 7, 8, 10],
            Mode.Locrian => [0, 1, 3, 5, 6, 8, 10],
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

        var notes = intervals
            .Select(interval => NoteHelper.CastNote(tonicValue + interval))
            .ToList();

        return new Scale(notes, mode);
    }

    public static Scale Default()
    {
        return Build(Note.C, Mode.Ionian);
    }
}
