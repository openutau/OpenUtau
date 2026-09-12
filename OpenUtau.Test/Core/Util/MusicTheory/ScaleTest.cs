using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Internal;

namespace OpenUtau.Core.Util.MusicTheory;

public class ScaleTest
{
    public class Default
    {
        [Fact]
        public void TonicIsC()
        {
            Assert.Equal(Note.C, Scale.Default().Tonic);
        }

        [Fact]
        public void ModeIsIonian()
        {
            Assert.Equal(Mode.Ionian, Scale.Default().Mode);
        }
    }

    public class Build
    {
        private static void AssertScaleIsCorrect(Note tonic, Mode mode, List<Note> expectedNotes)
        {
            var scale = Scale.Build(tonic, mode);

            Assert.Equal(tonic, scale.Tonic);
            Assert.Equal(mode, scale.Mode);
            foreach (var note in Enum.GetValues<Note>())
            {
                Assert.Equal(!expectedNotes.Contains(note), scale.IsOutOfScale(note));
            }
            Assert.Equal(Note.C, Scale.Default().Tonic);
        }

        [Fact]
        public void CLydian() =>
            AssertScaleIsCorrect(
                Note.C,
                Mode.Lydian,
                [Note.C, Note.D, Note.E, Note.Fsharp, Note.G, Note.A, Note.B]
            );

        [Fact]
        public void CIonian() =>
            AssertScaleIsCorrect(
                Note.C,
                Mode.Ionian,
                [Note.C, Note.D, Note.E, Note.F, Note.G, Note.A, Note.B]
            );

        [Fact]
        public void CMixolydian() =>
            AssertScaleIsCorrect(
                Note.C,
                Mode.Mixolydian,
                [Note.C, Note.D, Note.E, Note.F, Note.G, Note.A, Note.Asharp]
            );

        [Fact]
        public void CDorian() =>
            AssertScaleIsCorrect(
                Note.C,
                Mode.Dorian,
                [Note.C, Note.D, Note.Dsharp, Note.F, Note.G, Note.A, Note.Asharp]
            );

        [Fact]
        public void CAeolian() =>
            AssertScaleIsCorrect(
                Note.C,
                Mode.Aeolian,
                [Note.C, Note.D, Note.Dsharp, Note.F, Note.G, Note.Gsharp, Note.Asharp]
            );

        [Fact]
        public void CPhrygian() =>
            AssertScaleIsCorrect(
                Note.C,
                Mode.Phrygian,
                [Note.C, Note.Csharp, Note.Dsharp, Note.F, Note.G, Note.Gsharp, Note.Asharp]
            );

        [Fact]
        public void CLocrian() =>
            AssertScaleIsCorrect(
                Note.C,
                Mode.Locrian,
                [Note.C, Note.Csharp, Note.Dsharp, Note.F, Note.Fsharp, Note.Gsharp, Note.Asharp]
            );

        [Fact]
        public void ALydian() =>
            AssertScaleIsCorrect(
                Note.A,
                Mode.Lydian,
                [Note.A, Note.B, Note.Csharp, Note.Dsharp, Note.E, Note.Fsharp, Note.Gsharp]
            );

        [Fact]
        public void AIonian() =>
            AssertScaleIsCorrect(
                Note.A,
                Mode.Ionian,
                [Note.A, Note.B, Note.Csharp, Note.D, Note.E, Note.Fsharp, Note.Gsharp]
            );

        [Fact]
        public void AMixolydian() =>
            AssertScaleIsCorrect(
                Note.A,
                Mode.Mixolydian,
                [Note.A, Note.B, Note.Csharp, Note.D, Note.E, Note.Fsharp, Note.G]
            );

        [Fact]
        public void ADorian() =>
            AssertScaleIsCorrect(
                Note.A,
                Mode.Dorian,
                [Note.A, Note.B, Note.C, Note.D, Note.E, Note.Fsharp, Note.G]
            );

        [Fact]
        public void AAeolian() =>
            AssertScaleIsCorrect(
                Note.A,
                Mode.Aeolian,
                [Note.A, Note.B, Note.C, Note.D, Note.E, Note.F, Note.G]
            );

        [Fact]
        public void APhrygian() =>
            AssertScaleIsCorrect(
                Note.A,
                Mode.Phrygian,
                [Note.A, Note.Asharp, Note.C, Note.D, Note.E, Note.F, Note.G]
            );

        [Fact]
        public void ALocrian() =>
            AssertScaleIsCorrect(
                Note.A,
                Mode.Locrian,
                [Note.A, Note.Asharp, Note.C, Note.D, Note.Dsharp, Note.F, Note.G]
            );
    }

    public class IsTonic
    {
        [Fact]
        public void DIsTheOnlyTonic()
        {
            var scale = Scale.Build(Note.D, Mode.Ionian);
            Assert.True(scale.IsTonic(Note.D));
            Enum.GetValues<Note>()
                .Where(note => !note.Equals(Note.D))
                .ForEach(note =>
                {
                    Assert.False(scale.IsTonic(note));
                });
        }
    }

    public class Interval
    {
        private readonly Scale _cIonian = Scale.Build(Note.C, Mode.Ionian);

        [Fact]
        public void CIsFirstInCIonian() => Assert.Equal(1, _cIonian.Interval(Note.C));

        [Fact]
        public void CsharpIsOutOfScaleInCIonian() => Assert.Null(_cIonian.Interval(Note.Csharp));

        [Fact]
        public void DIsSecondInCIonian() => Assert.Equal(2, _cIonian.Interval(Note.D));

        [Fact]
        public void DsharpIsOutOfScaleInCIonian() => Assert.Null(_cIonian.Interval(Note.Dsharp));

        [Fact]
        public void EIsThirdInCIonian() => Assert.Equal(3, _cIonian.Interval(Note.E));

        [Fact]
        public void FIsFourthInCIonian() => Assert.Equal(4, _cIonian.Interval(Note.F));

        [Fact]
        public void FsharpIsOutOfScaleInCIonian() => Assert.Null(_cIonian.Interval(Note.Fsharp));

        [Fact]
        public void GIsFifthInCIonian() => Assert.Equal(5, _cIonian.Interval(Note.G));

        [Fact]
        public void GsharpIsOutOfScaleInCIonian() => Assert.Null(_cIonian.Interval(Note.Gsharp));

        [Fact]
        public void AIsSixthInCIonian() => Assert.Equal(6, _cIonian.Interval(Note.A));

        [Fact]
        public void AsharpIsOutOfScaleInCIonian() => Assert.Null(_cIonian.Interval(Note.Asharp));

        [Fact]
        public void BIsSeventhInCIonian() => Assert.Equal(7, _cIonian.Interval(Note.B));

        private readonly Scale _aAeolian = Scale.Build(Note.A, Mode.Aeolian);

        [Fact]
        public void AIsFirstInAAeolian() => Assert.Equal(1, _aAeolian.Interval(Note.A));

        [Fact]
        public void AsharpIsOutOfScaleInAAeolian() => Assert.Null(_aAeolian.Interval(Note.Asharp));

        
        [Fact]
        public void EIsFifthInAAeolian() => Assert.Equal(5, _aAeolian.Interval(Note.E));
    }
}
