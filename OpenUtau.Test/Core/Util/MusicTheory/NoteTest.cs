using Xunit;

namespace OpenUtau.Core.Util.MusicTheory;

public class NoteTest
{
    public class CastNote
    {
        [Fact]
        public void BelowZeroReturnsC() => Assert.Equal(Note.C, NoteHelper.CastNote(-1));

        [Fact]
        public void ZeroReturnsC() => Assert.Equal(Note.C, NoteHelper.CastNote(0));

        [Fact]
        public void AboveTwelveReturnsModuloNoteIndex() =>
            Assert.Equal(Note.Csharp, NoteHelper.CastNote(25));

        [Fact]
        public void ElseReturnsNoteIndex() => Assert.Equal(Note.G, NoteHelper.CastNote(7));
    }

    public class StringifyNote
    {
        [Fact]
        public void PlainNoteDoesNotChange() => Assert.Equal("G", NoteHelper.StringifyNote(Note.G));

        [Fact]
        public void SharpIsStringifiedDoesNotChange() =>
            Assert.Equal("G#", NoteHelper.StringifyNote(Note.Gsharp));
    }
}
