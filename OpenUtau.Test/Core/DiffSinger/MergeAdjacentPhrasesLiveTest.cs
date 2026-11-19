using System;
using System.IO;
using System.Linq;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Pipeline {
    /// <summary>
    /// Live check against a real DiffSinger voicebank: the piano roll's merge
    /// toggle must flip phrase grouping for gaps shorter than the renderer's
    /// head + tail padding, and leave wider gaps split either way.
    ///
    /// Opt-in, because it needs a voicebank on disk:
    /// <code>
    /// OPENUTAU_TEST_SINGERS=F:\OpenUTAU_SHMC\Singers dotnet test --filter MergeAdjacentPhrasesLiveTest
    /// </code>
    /// Skipped when that variable is unset, so CI and everyday runs are unaffected.
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class MergeAdjacentPhrasesLiveTest {
        [Fact]
        public void MergeToggleFlipsGroupingInsidePadding() {
            string? singersRoot = Environment.GetEnvironmentVariable("OPENUTAU_TEST_SINGERS");
            if (string.IsNullOrEmpty(singersRoot) || !Directory.Exists(singersRoot)) {
                Assert.Skip("Set OPENUTAU_TEST_SINGERS to a directory containing a DiffSinger voicebank to run this live test.");
            }

            string? previousSingerPath = Preferences.Default.AdditionalSingerPath;
            bool previousMerge = Preferences.Default.DiffSingerMergeNearbyPhrases;
            try {
                Preferences.Default.AdditionalSingerPath = singersRoot!;
                SingerManager.Inst.SearchAllSingers();
                var singer = SingerManager.Inst.Singers.Values
                    .FirstOrDefault(s => s.SingerType == USingerType.DiffSinger);
                Assert.True(singer != null, $"No DiffSinger voicebank found under {singersRoot}.");
                singer!.EnsureLoaded();
                Assert.True(singer.Loaded, $"{singer.Id} failed to load: {string.Join("; ", singer.Errors)}");

                var renderer = new DiffSingerRenderer();
                var (headMs, tailMs) = renderer.PhrasePadding(singer, Array.Empty<UPhoneme>());
                Assert.True(headMs + tailMs > 0, "DiffSinger padding should be positive.");

                // A gap inside the padding: merged only while the toggle is on.
                var inside = BuildPart(singer, renderer, (headMs + tailMs) / 2);
                Preferences.Default.DiffSingerMergeNearbyPhrases = false;
                Assert.Equal(2, CountPhrases(inside));
                Preferences.Default.DiffSingerMergeNearbyPhrases = true;
                Assert.Equal(1, CountPhrases(inside));

                // A gap wider than the padding: split either way.
                var outside = BuildPart(singer, renderer, (headMs + tailMs) * 2);
                Preferences.Default.DiffSingerMergeNearbyPhrases = false;
                Assert.Equal(2, CountPhrases(outside));
                Preferences.Default.DiffSingerMergeNearbyPhrases = true;
                Assert.Equal(2, CountPhrases(outside));
            } finally {
                Preferences.Default.AdditionalSingerPath = previousSingerPath;
                Preferences.Default.DiffSingerMergeNearbyPhrases = previousMerge;
            }
        }

        static int CountPhrases((UProject Project, UTrack Track, UVoicePart Part) fixture) {
            var source = PhraseSource.FromPart(fixture.Project, fixture.Track, fixture.Part, generation: 1);
            Assert.True(source != null, "Phrase source should not be empty.");
            return source!.BuildPhrases().Length;
        }

        /// <summary>
        /// Two notes on one DiffSinger track, separated by <paramref name="gapMs"/>.
        /// </summary>
        static (UProject, UTrack, UVoicePart) BuildPart(USinger singer, IRenderer renderer, double gapMs) {
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
            track.Singer = singer;
            track.RendererSettings.Renderer = renderer;

            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);

            // Default tempo is 120 BPM: one tick is 500 / 480 ms.
            double msPerTick = project.timeAxis.TickPosToMsPos(480) / 480.0;
            int gapTicks = (int)Math.Round(gapMs / msPerTick);
            var note0 = UNote.Create();
            note0.position = 0;
            note0.duration = 480;
            note0.tone = 60;
            note0.lyric = "a";
            note0.ExtendedDuration = 480;
            var note1 = UNote.Create();
            note1.position = 480 + gapTicks;
            note1.duration = 480;
            note1.tone = 62;
            note1.lyric = "a";
            note1.ExtendedDuration = 480;
            note0.Next = note1;
            note1.Prev = note0;
            part.notes.Add(note0);
            part.notes.Add(note1);

            string phonemeName = singer.Otos.FirstOrDefault()?.Alias ?? "a";
            var phoneme0 = new UPhoneme { position = 0, phoneme = phonemeName, Parent = note0 };
            var phoneme1 = new UPhoneme { position = note1.position, phoneme = phonemeName, Parent = note1 };
            part.phonemes.AddRange(new[] { phoneme0, phoneme1 });
            phoneme0.Next = phoneme1;
            phoneme1.Prev = phoneme0;
            var options = new ValidateOptions();
            phoneme0.Validate(options, project, track, part, note0);
            phoneme1.Validate(options, project, track, part, note1);
            Assert.False(phoneme0.Error, $"First phoneme errored: {phoneme0.ErrorException?.Message}");
            Assert.False(phoneme1.Error, $"Second phoneme errored: {phoneme1.ErrorException?.Message}");
            return (project, track, part);
        }
    }
}
