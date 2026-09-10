using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.Render {
    [Collection(RenderSingletonCollection.Name)]
    public class RenderViewTest : IDisposable {
        /// <summary>
        /// The minimal singer phrase construction needs: phoneme → render phrase
        /// runs against it, but no voicebank file is ever opened.
        /// </summary>
        private sealed class TestSinger : USinger {
            public TestSinger() {
                found = true;
                loaded = true;
            }
            public override string Id => "test-singer";
            public override string Name => "Test Singer";
            public override IList<USubbank> Subbanks => new USubbank[0];
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = UOto.OfDummy(phoneme);
                return true;
            }
        }

        private static UProject NewProject() {
            var project = new UProject();
            // The expression descriptors phrase construction reads.
            project.RegisterExpression(new UExpressionDescriptor("engine", "eng", 0, 100, 0) { options = new[] { "" } });
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("velocity", "vel", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("modulation", "mod", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("direct", "dir", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("shift", "shft", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("attack", "atk", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("decay", "dec", 0, 100, 100));
            // A fresh project ships with a default track; replace it.
            project.tracks.Clear();
            var track = new UTrack("Lead") { TrackNo = 0 };
            track.Singer = new TestSinger();
            // A track built around an assigned singer has no default renderer;
            // Validate creates the IRenderer instance phrase grouping needs.
            // DIFFSINGER keeps the test away from the app-startup tool registries.
            track.RendererSettings.renderer = Renderers.DIFFSINGER;
            track.RendererSettings.Validate(track);
            project.tracks.Add(track);
            return project;
        }

        // Note + phoneme + validation. Grouping runs once, over the whole part,
        // because RenderPhrase.FromPart regroups every phoneme it sees. The tone
        // differs per note: phrase hashes are content-based and ignore absolute
        // position, so identical notes would collide in the content-addressed
        // store.
        private static void AddNote(UProject project, UTrack track, UVoicePart part, int pos, int dur, int tone) {
            var note = UNote.Create();
            note.position = pos;
            note.duration = dur;
            note.tone = tone;
            note.lyric = "a";
            note.ExtendedDuration = dur;
            part.notes.Add(note);
            var phoneme = new UPhoneme { position = pos, phoneme = "A", Parent = note };
            part.phonemes.Add(phoneme);
            phoneme.Validate(new ValidateOptions(), project, track, part, note);
            if (phoneme.Error) {
                throw new Exception($"phoneme failed to validate: {phoneme.ErrorException}");
            }
        }

        private static void Group(UProject project, UTrack track, UVoicePart part) {
            part.renderPhrases.AddRange(RenderPhrase.FromPart(project, track, part));
        }

        private readonly MixPlanner planner = new MixPlanner();
        private readonly UProject project;
        private readonly UVoicePart part;
        private readonly RenderView view = RenderView.Inst;

        public RenderViewTest() {
            ThreadGuard.SetUiThread(Thread.CurrentThread);
            view.SetPlannerForTest(planner);
            project = NewProject();
            part = new UVoicePart { name = "A", trackNo = 0, position = 0, duration = 960 };
            project.parts.Add(part);
            project.timeAxis.BuildSegments(project);
            AddNote(project, project.tracks[0], part, 0, 240, 60);
            AddNote(project, project.tracks[0], part, 480, 240, 72);
            Group(project, project.tracks[0], part);
            Assert.Equal(2, part.renderPhrases.Count);
        }

        public void Dispose() {
            view.ForgetAll();
            view.SetPlannerForTest(null);
        }

        // The coalesced delivery rebuilds the cached projections; wait for it.
        private void Deliver(RenderProjection stale) {
            view.InvalidateAll();
            WaitFor(() => !ReferenceEquals(view.Current(part), stale));
        }

        private static void WaitFor(Func<bool> condition, int timeoutMs = 2000) {
            var deadline = Environment.TickCount + timeoutMs;
            while (!condition()) {
                if (Environment.TickCount > deadline) {
                    throw new TimeoutException("render view delivery did not arrive");
                }
                Thread.Sleep(5);
            }
        }

        [Fact]
        public void ProjectionTracksRenderedState() {
            var proj = view.Current(part);
            Assert.False(proj.Ready);
            Assert.Equal(2, proj.Phrases.Count);
            Assert.All(proj.Phrases, v => Assert.False(v.Rendered));

            planner.RegisterPcm(part, part.renderPhrases[0].hash, 0, 100, 1, new float[] { 1 });
            Deliver(proj);
            proj = view.Current(part);
            Assert.False(proj.Ready);
            Assert.Equal(1, proj.Phrases.Count(v => v.Rendered));
            // The layout is the phrase's precomputed range, not the store's.
            Assert.Equal(part.renderPhrases[0].Layout, proj.Phrases[0].Layout);

            planner.RegisterPcm(part, part.renderPhrases[1].hash, 0, 100, 1, new float[] { 2 });
            Deliver(proj);
            proj = view.Current(part);
            Assert.True(proj.Ready);
            Assert.All(proj.Phrases, v => Assert.True(v.Rendered));
        }

        [Fact]
        public void ProjectionFollowsTheDocumentNotTheStore() {
            planner.RegisterPcm(part, part.renderPhrases[0].hash, 0, 100, 1, new float[] { 1 });
            Deliver(view.Current(part));
            Assert.Single(view.Current(part).Phrases, v => v.Rendered);

            // An edit replaces the phrase set: the stale hash no longer counts.
            part.notes.Clear();
            part.phonemes.Clear();
            part.renderPhrases.Clear();
            AddNote(project, project.tracks[0], part, 0, 480, 84);
            Group(project, project.tracks[0], part);

            Deliver(view.Current(part));
            var proj = view.Current(part);
            Assert.Single(proj.Phrases);
            Assert.False(proj.Ready);
        }

        [Fact]
        public void ObserveCoalescesDeliveries() {
            var seen = 0;
            using var subscription = view.Observe(_ => Interlocked.Increment(ref seen));
            view.Current(part);
            view.InvalidateAll();
            view.InvalidateAll();
            WaitFor(() => Volatile.Read(ref seen) >= 1);
            Thread.Sleep(80); // let any (buggy) extra delivery land
            Assert.Equal(1, Volatile.Read(ref seen));
        }

        [Fact]
        public void WavePartProjectsFromHashZero() {
            var wave = new UWavePart { trackNo = 0 };
            var proj = view.Current(wave);
            Assert.False(proj.Ready);
            Assert.Empty(proj.Phrases);

            planner.RegisterWavePcm(wave, 100, 500, 2, new float[8]);
            view.InvalidateAll();
            WaitFor(() => view.Current(wave).Ready);
            proj = view.Current(wave);
            Assert.True(proj.Ready);
            Assert.Single(proj.Phrases);
            Assert.Equal(0UL, proj.Phrases[0].Hash);
            Assert.Equal(100, proj.Phrases[0].Layout.StartMs);
            Assert.Equal(600, proj.Phrases[0].Layout.EndMs);
        }
    }
}
