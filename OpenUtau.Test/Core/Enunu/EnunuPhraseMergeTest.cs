using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Enunu;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// EnunuRenderer.ShouldMergePhrases: phrases are merged across rests shorter than
    /// half a beat (240 ticks) and stay split at longer ones.
    /// </summary>
    public class EnunuPhraseMergeTest {
        class TestSinger : USinger {
            public TestSinger() {
                found = true;
                loaded = true;
            }
            public override string Id => "enunu-merge-test-singer";
            public override IList<USubbank> Subbanks => new USubbank[0];
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = UOto.OfDummy(phoneme);
                return true;
            }
            public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
                oto = null;
                return false;
            }
        }

        /// <summary>
        /// One phoneme per note. At the default 120 BPM, 480 ticks = 500 ms.
        /// </summary>
        static List<UPhoneme> Build(params (int position, int duration)[] notes) {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("velocity", "vel", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("modulation", "mod", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("attack", "atk", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("decay", "dec", 0, 100, 100));
            var track = project.tracks[0];
            track.Singer = new TestSinger();
            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);
            var uNotes = new List<UNote>();
            var phonemes = new List<UPhoneme>();
            foreach (var (position, duration) in notes) {
                var note = UNote.Create();
                note.position = position;
                note.duration = duration;
                note.tone = 60;
                note.lyric = "a";
                note.ExtendedDuration = duration;
                if (uNotes.Count > 0) {
                    uNotes[^1].Next = note;
                    note.Prev = uNotes[^1];
                }
                uNotes.Add(note);
                var phoneme = new UPhoneme { position = position, phoneme = "a", Parent = note };
                if (phonemes.Count > 0) {
                    phonemes[^1].Next = phoneme;
                    phoneme.Prev = phonemes[^1];
                }
                phonemes.Add(phoneme);
            }
            part.notes.UnionWith(uNotes);
            part.phonemes.AddRange(phonemes);
            for (int i = 0; i < phonemes.Count; i++) {
                phonemes[i].Validate(new ValidateOptions(), project, track, part, uNotes[i]);
            }
            return phonemes;
        }

        static (int, int)[] Run(int start, int count) =>
            Enumerable.Range(0, count).Select(i => (start + i * 480, 480)).ToArray();

        static bool Merge(List<UPhoneme> phonemes, int gapAfter) =>
            new EnunuRenderer().ShouldMergePhrases(null, null, phonemes[gapAfter], phonemes[gapAfter + 1]);

        [Fact]
        public void LongPhrasesSplitAtLongRest() {
            // 3.5 s, a 1 s rest, 3.5 s
            var phonemes = Build(Run(0, 7).Concat(Run(480 * 9, 7)).ToArray());
            Assert.False(Merge(phonemes, 6));
        }

        [Fact]
        public void RestShorterThanHalfBeatMerges() {
            var phonemes = Build(Run(0, 7).Concat(Run(480 * 7 + 120, 7)).ToArray());
            Assert.True(Merge(phonemes, 6));
        }

        [Fact]
        public void HalfBeatRestSplits() {
            var phonemes = Build(Run(0, 7).Concat(Run(480 * 7 + 240, 7)).ToArray());
            Assert.False(Merge(phonemes, 6));
        }

        [Fact]
        public void ShortPhraseStaysSplitAtLongRest() {
            // 3.5 s, a 1 s rest, a lone 0.5 s note: merging across long rests made phrases too long
            var phonemes = Build(Run(0, 7).Concat(Run(480 * 9, 1)).ToArray());
            Assert.False(Merge(phonemes, 6));
        }
    }
}
