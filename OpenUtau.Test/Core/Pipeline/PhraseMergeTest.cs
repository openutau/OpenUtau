using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Pipeline {
    /// <summary>
    /// Phrase grouping at gaps: renderer-requested merging and the user cap
    /// on merged phrase length (<c>Preferences.MergePhrasesSec</c>).
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class PhraseMergeTest : IDisposable {
        class TestSinger : USinger {
            readonly UOto otoA;
            public TestSinger(UOto a) {
                otoA = a;
                found = true;
                loaded = true;
            }
            public override string Id => "merge-test-singer";
            public override IList<USubbank> Subbanks => new USubbank[0];
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = phoneme == "A" ? otoA : (UOto)null;
                return oto != null;
            }
            public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
                oto = null;
                return false;
            }
        }

        // Pads both ends of a phrase by padMs, like DiffSinger's head/tail
        // frames, so gaps shorter than 2 * padMs ask to merge.
        class PaddedRenderer : IRenderer {
            readonly double padMs;
            public PaddedRenderer(double padMs) {
                this.padMs = padMs;
            }
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => false;
            public (double HeadMs, double TailMs) PhrasePadding(USinger singer, IEnumerable<UPhoneme> phonemes) =>
                (padMs, padMs);
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    CancellationTokenSource cancellation, bool isPreRender = false,
                    RenderPhraseEvents? renderEvents = null) => throw new NotImplementedException();
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        readonly float previousMergeSec = Preferences.Default.MergePhrasesSec;

        public void Dispose() {
            Preferences.Default.MergePhrasesSec = previousMergeSec;
        }

        // The default project is 120 BPM: 480 ticks = 500 ms.
        static List<RenderPhrase> Build(int noteCount, int noteTicks, int gapTicks, double padMs) {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("engine", "eng", 0, 100, 0) {
                options = new[] { "" },
            });
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("velocity", "vel", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("modulation", "mod", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("direct", "dir", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("shift", "shft", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("attack", "atk", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("decay", "dec", 0, 100, 100));
            var track = project.tracks[0];
            track.Singer = new TestSinger(UOto.OfDummy("A"));
            track.RendererSettings.Renderer = new PaddedRenderer(padMs);
            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);

            var notes = new List<UNote>();
            for (int i = 0; i < noteCount; ++i) {
                var note = UNote.Create();
                note.position = i * (noteTicks + gapTicks);
                note.duration = noteTicks;
                note.tone = 60;
                note.lyric = "a";
                note.ExtendedDuration = noteTicks;
                if (i > 0) {
                    note.Prev = notes[i - 1];
                    notes[i - 1].Next = note;
                }
                notes.Add(note);
                part.notes.Add(note);
            }
            var phonemes = notes
                .Select(n => new UPhoneme { position = n.position, phoneme = "A", Parent = n })
                .ToList();
            part.phonemes.AddRange(phonemes);
            for (int i = 0; i < phonemes.Count; ++i) {
                phonemes[i].Prev = i > 0 ? phonemes[i - 1] : null;
                phonemes[i].Next = i + 1 < phonemes.Count ? phonemes[i + 1] : null;
            }
            for (int i = 0; i < phonemes.Count; ++i) {
                phonemes[i].Validate(new ValidateOptions(), project, track, part, notes[i]);
                Assert.False(phonemes[i].Error);
            }
            return RenderPhrase.FromPart(project, track, part);
        }

        [Fact]
        public void ContiguousNotesStayOnePhraseEvenPastCap() {
            // The cap only acts at gaps; 20 s of contiguous notes is one phrase.
            Preferences.Default.MergePhrasesSec = 5;
            var phrases = Build(noteCount: 40, noteTicks: 480, gapTicks: 0, padMs: 0);
            Assert.Single(phrases);
        }

        [Fact]
        public void ShortGapsMergeWhenUncapped() {
            Preferences.Default.MergePhrasesSec = 0; // Auto
            // 125 ms gaps < 2 * 100 ms padding.
            var phrases = Build(noteCount: 40, noteTicks: 480, gapTicks: 120, padMs: 100);
            Assert.Single(phrases);
        }

        [Fact]
        public void CapSplitsLongMergedRun() {
            Preferences.Default.MergePhrasesSec = 5;
            // 625 ms per note + gap, 40 notes = 25 s. A group holds 8 notes:
            // 7 * 625 + 500 = 4875 ms fits, 8 * 625 + 500 = 5500 ms does not.
            var phrases = Build(noteCount: 40, noteTicks: 480, gapTicks: 120, padMs: 100);
            Assert.Equal(5, phrases.Count);
            Assert.All(phrases, p => Assert.True(p.durationMs <= 5000, $"{p.durationMs} ms"));
        }

        [Fact]
        public void WideGapsNeverMerge() {
            Preferences.Default.MergePhrasesSec = 0;
            // 500 ms gaps > 2 * 100 ms padding.
            var phrases = Build(noteCount: 4, noteTicks: 480, gapTicks: 480, padMs: 100);
            Assert.Equal(4, phrases.Count);
        }

        [Fact]
        public void RendererWithoutPaddingNeverMerges() {
            Preferences.Default.MergePhrasesSec = 0;
            // A classic-style renderer asks for no merge at any gap.
            var phrases = Build(noteCount: 4, noteTicks: 480, gapTicks: 120, padMs: 0);
            Assert.Equal(4, phrases.Count);
        }
    }
}
