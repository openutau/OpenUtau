using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.Pipeline {
    /// <summary>
    /// Exercises the phrase source builder: off-UI-thread build, coalescing to
    /// the latest snapshot per part, and stale-result dropping through the
    /// generation gate. Serialized with the other DocManager-touching tests.
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class PhraseSourceBuilderTest : IDisposable {
        class TestSinger : USinger {
            readonly UOto otoA;
            public TestSinger(UOto a) {
                otoA = a;
                found = true;
                loaded = true;
            }
            public override string Id => "builder-test-singer";
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

        class TestRenderer : IRenderer {
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => false;
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

        static (UProject Project, UTrack Track, UVoicePart Part) BuildFixture() {
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
            track.RendererSettings.Renderer = new TestRenderer();

            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);

            var note0 = UNote.Create();
            note0.position = 0;
            note0.duration = 480;
            note0.tone = 60;
            note0.lyric = "a";
            note0.ExtendedDuration = 480;
            var note1 = UNote.Create();
            note1.position = 480;
            note1.duration = 480;
            note1.tone = 62;
            note1.lyric = "u";
            note1.ExtendedDuration = 480;
            note0.Next = note1;
            note1.Prev = note0;
            part.notes.Add(note0);
            part.notes.Add(note1);

            var phoneme0 = new UPhoneme { position = 0, phoneme = "A", Parent = note0 };
            var phoneme1 = new UPhoneme { position = 480, phoneme = "A", Parent = note1 };
            part.phonemes.AddRange(new[] { phoneme0, phoneme1 });
            phoneme0.Next = phoneme1;
            phoneme1.Prev = phoneme0;
            phoneme0.Validate(new ValidateOptions(), project, track, part, note0);
            phoneme1.Validate(new ValidateOptions(), project, track, part, note1);
            Assert.False(phoneme0.Error);
            Assert.False(phoneme1.Error);
            return (project, track, part);
        }

        readonly PhraseSourceBuilder builder;
        readonly UProject previousProject;

        public PhraseSourceBuilderTest() {
            builder = new PhraseSourceBuilder(TaskScheduler.Default);
            var (project, _, _) = BuildFixture();
            previousProject = DocManager.Inst.TakeProjectForTest(project);
        }

        public void Dispose() {
            DocManager.Inst.TakeProjectForTest(previousProject);
            builder.Dispose();
        }

        static bool WaitUntil(Func<bool> condition, int timeoutMs) {
            var deadline = Environment.TickCount + timeoutMs;
            while (Environment.TickCount < deadline) {
                if (condition()) {
                    return true;
                }
                Thread.Sleep(10);
            }
            return condition();
        }

        [Fact]
        public void BuildsOffThreadAndAppliesToBackReferenceSlot() {
            var (project, track, part) = BuildFixture();
            // Re-register the part with the installed project.
            DocManager.Inst.Project.parts.Clear();
            DocManager.Inst.Project.parts.Add(part);
            part.trackNo = 0;

            long generation = ++part.phraseGeneration;
            var source = PhraseSource.FromPart(project, track, part, generation);
            Assert.NotNull(source);
            var expected = source.BuildPhrases()[0].hash;
            part.phraseGate.MarkPending(generation);
            Assert.True(builder.Push(source, part));

            Assert.True(WaitUntil(() => part.phraseAppliedGeneration == generation
                    && part.renderPhrases.Count == 1
                    && part.renderPhrases[0].hash == expected, 5000),
                $"build did not land: gen={part.phraseAppliedGeneration} "
                + $"count={part.renderPhrases.Count}");
        }

        [Fact]
        public void CoalescesToLatestSnapshotPerPart() {
            var (project, track, part) = BuildFixture();
            DocManager.Inst.Project.parts.Clear();
            DocManager.Inst.Project.parts.Add(part);
            part.trackNo = 0;

            long generation1 = ++part.phraseGeneration;
            var source1 = PhraseSource.FromPart(project, track, part, generation1);
            // Edit the part and take a newer snapshot.
            part.notes.First().tone = 70;
            long generation2 = ++part.phraseGeneration;
            var source2 = PhraseSource.FromPart(project, track, part, generation2);
            Assert.NotNull(source1);
            Assert.NotNull(source2);
            Assert.NotEqual(source1.BuildPhrases()[0].hash, source2.BuildPhrases()[0].hash);

            part.phraseGate.MarkPending(generation2);
            builder.Push(source1, part);
            builder.Push(source2, part);

            var latestHash = source2.BuildPhrases()[0].hash;
            Assert.True(WaitUntil(() => part.phraseAppliedGeneration == generation2
                    && part.renderPhrases.Count > 0
                    && part.renderPhrases[0].hash == latestHash, 5000),
                $"latest build did not land: gen={part.phraseAppliedGeneration} "
                + $"count={part.renderPhrases.Count}");
        }

        [Fact]
        public void DropsResultForDeletedPart() {
            var (project, track, part) = BuildFixture();
            long generation = ++part.phraseGeneration;
            var source = PhraseSource.FromPart(project, track, part, generation);
            Assert.NotNull(source);
            // The part is not in the installed project, so the result is dropped.
            Assert.DoesNotContain(part, DocManager.Inst.Project.parts);
            part.phraseGate.MarkPending(generation);
            builder.Push(source, part);
            // Let the worker run and drain.
            Thread.Sleep(1000);
            Assert.NotEqual(generation, part.phraseAppliedGeneration);
            Assert.Empty(part.renderPhrases);
        }
    }

    public sealed class PhraseBuildGateTest {
        [Fact]
        public void WaitsForCompletion() {
            var gate = new PhraseBuildGate();
            Assert.True(gate.IsCurrent(0));
            gate.MarkPending(1);
            Assert.False(gate.WaitFor(1, TimeSpan.FromMilliseconds(50)));
            Assert.False(gate.IsCurrent(1));
            gate.MarkCompleted(1);
            Assert.True(gate.WaitFor(1, TimeSpan.FromMilliseconds(100)));
            Assert.True(gate.IsCurrent(1));
            gate.MarkPending(2);
            Assert.False(gate.IsCurrent(2));
        }

        [Fact]
        public void NewerGenerationSupersedesOlder() {
            var gate = new PhraseBuildGate();
            gate.MarkPending(1);
            gate.MarkCompleted(1);
            gate.MarkPending(3);
            // The superseded generation completing must not signal readiness
            // for the newer one.
            gate.MarkCompleted(2);
            Assert.False(gate.IsCurrent(3));
            gate.MarkCompleted(3);
            Assert.True(gate.IsCurrent(3));
        }
    }

    /// <summary>
    /// ImpactSet-driven invalidation of the incremental snapshot store. Unique
    /// ids keep the shared singleton from interfering with other tests.
    /// </summary>
    public class DocumentSnapshotStoreTest {
        const int TrackNo = 9999;
        readonly DocumentSnapshotStore store = DocumentSnapshotStore.Inst;
        readonly PartId partId = PartId.New();
        readonly PartId otherPartId = PartId.New();

        UVoicePart MakePart() {
            var part = new UVoicePart { trackNo = TrackNo };
            part.Id = PartId.New();
            return part;
        }

        [Fact]
        public void PartAndCurvesImpactDropOnlyThatPart() {
            var part = MakePart();
            part.Id = partId;
            var other = MakePart();
            other.Id = otherPartId;
            store.SetPart(part, null);
            store.SetPart(other, null);
            Assert.True(store.TryGetPart(partId, out _));

            store.Invalidate(ImpactSet.PartOf(part));
            Assert.False(store.TryGetPart(partId, out _));
            Assert.True(store.TryGetPart(otherPartId, out _));

            store.SetPart(part, null);
            store.Invalidate(ImpactSet.CurvesOf(part, "pitd"));
            Assert.False(store.TryGetPart(partId, out _));
            store.RemovePart(otherPartId);
        }

        [Fact]
        public void TrackImpactDropsTrackAndItsParts() {
            var track = new UTrack("test") { TrackNo = TrackNo };
            var part = MakePart();
            part.Id = partId;
            store.SetTrack(track);
            store.SetPart(part, null);
            Assert.True(store.TryGetTrack(TrackNo, out _));
            Assert.True(store.TryGetPart(partId, out _));

            store.Invalidate(ImpactSet.TrackOf(track));
            Assert.False(store.TryGetTrack(TrackNo, out _));
            Assert.False(store.TryGetPart(partId, out _));
        }

        [Fact]
        public void ProjectImpactClearsEverything() {
            var part = MakePart();
            part.Id = partId;
            store.SetPart(part, null);
            Assert.True(store.TryGetPart(partId, out _));

            store.Invalidate(ImpactSet.All);
            Assert.False(store.TryGetPart(partId, out _));
        }

        [Fact]
        public void MixAndNoneImpactKeepSnapshots() {
            var part = MakePart();
            part.Id = partId;
            store.SetPart(part, null);

            store.Invalidate(ImpactSet.MixOnly);
            store.Invalidate(ImpactSet.None);
            Assert.True(store.TryGetPart(partId, out var snapshot));
            Assert.Equal(partId, snapshot.PartId);
            Assert.Equal(TrackNo, snapshot.TrackNo);
            store.RemovePart(partId);
        }

        [Fact]
        public void AssemblesProjectSnapshot() {
            var part = MakePart();
            part.Id = partId;
            store.SetPart(part, null);
            store.SetTrack(new UTrack("test") { TrackNo = TrackNo });
            store.SetRevision(new DocRevision(42));

            var project = new UProject();
            var snapshot = store.Snapshot(project);
            Assert.True(snapshot.Revision.Value >= 42);
            Assert.True(snapshot.Parts.TryGetValue(partId, out var partSnapshot));
            Assert.Null(partSnapshot.Source);
            Assert.Contains(TrackNo, snapshot.Tracks.Select(t => t.TrackNo));
            store.RemovePart(partId);
        }
    }
}
