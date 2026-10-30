using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class SineFallbackTest {
        static UProject CreateProject(out UTrack track, out UVoicePart part) {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("pitch deviation", Format.Ustx.PITD, -1200, 1200, 0) {
                type = UExpressionType.Curve,
            });
            foreach (var abbr in new[] { Format.Ustx.VEL, Format.Ustx.VOL, Format.Ustx.MOD, Format.Ustx.DIR, Format.Ustx.SHFT, Format.Ustx.CLR, Format.Ustx.CLRY }) {
                project.RegisterExpression(new UExpressionDescriptor(abbr, abbr, 0, 100, 0));
            }
            project.RegisterExpression(new UExpressionDescriptor("resampler", Format.Ustx.ENG, 0, 0, 0) {
                type = UExpressionType.Options,
                options = new string[] { "default" },
            });
            track = new UTrack(project);
            project.tracks.Add(track);
            part = new UVoicePart { trackNo = 0 };
            project.parts.Add(part);
            return project;
        }

        static UNote MakeNote(int position, int duration, int tone) {
            var note = UNote.Create();
            note.position = position;
            note.duration = duration;
            note.tone = tone;
            return note;
        }

        [Fact]
        public void FromPartBuildsOnePhrasePerConsonantNoteWithoutSinger() {
            var project = CreateProject(out var track, out var part);
            var note1 = MakeNote(0, 480, 60);
            note1.lyric = "ka";
            var note2 = MakeNote(480, 240, 64);
            note2.lyric = "ki";
            part.notes.Add(note1);
            part.notes.Add(note2);

            var phrases = RenderPhrase.FromPart(project, track, part);

            Assert.Equal(2, phrases.Count);
            Assert.All(phrases, phrase => Assert.Same(SineRenderer.Instance, phrase.renderer));
            Assert.All(phrases, phrase => Assert.NotEmpty(phrase.phones));
            Assert.Equal("a", phrases[0].phones[0].phoneme);
            Assert.Equal("i", phrases[1].phones[0].phoneme);
            Assert.Equal(60 * 100, phrases[0].pitches[0], 0);
            Assert.Equal(64 * 100, phrases[1].pitches[0], 0);
        }

        [Fact]
        public void SinePhraseCoversExtendedNoteChain() {
            var project = CreateProject(out var track, out var part);
            var note1 = MakeNote(0, 480, 60);
            var note2 = MakeNote(480, 240, 60);
            note2.Extends = note1;
            note1.Next = note2;
            note2.Prev = note1;
            part.notes.Add(note1);
            part.notes.Add(note2);

            var phrases = RenderPhrase.FromPart(project, track, part);

            var phrase = Assert.Single(phrases);
            Assert.Equal(720, phrase.duration);
        }

        [Fact]
        public void SinePhrasePitchesApplyPitdCurve() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 240, 60));
            var pitd = new UCurve(project.expressions[Format.Ustx.PITD]);
            pitd.xs.AddRange(new[] { 0, 240 });
            pitd.ys.AddRange(new[] { 100, 200 });
            part.curves.Add(pitd);

            var phrases = RenderPhrase.FromPart(project, track, part);

            var phrase = Assert.Single(phrases);
            Assert.Equal(60 * 100 + 100, phrase.pitches[0], 0);
            Assert.True(Math.Abs(phrase.pitches.Last() - (60 * 100 + 200)) < 1);
        }

        [Fact]
        public void FromPartUsesPhonemesWhenSingerLoaded() {
            var project = CreateProject(out var track, out var part);
            track.Singer = new MockLoadedSinger();
            track.RendererSettings.Validate(track);
            part.notes.Add(MakeNote(0, 480, 60));

            Assert.False(SineRenderer.IsFallbackActive(track));

            var phoneme = new UPhoneme {
                position = 0,
                phoneme = "a",
                Parent = part.notes.First(),
            };
            part.phonemes.Add(phoneme);

            var phrases = RenderPhrase.FromPart(project, track, part);
            Assert.NotEmpty(phrases);
            Assert.All(phrases, phrase => Assert.NotSame(SineRenderer.Instance, phrase.renderer));
        }

        [Fact]
        public void PreferenceOffSilencesSineWithoutStoppingTheRender() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 480, 60));
            var phrase = RenderPhrase.FromPart(project, track, part).Single();
            var layout = SineRenderer.Instance.Layout(phrase);

            var original = Preferences.Default.PlaySineWhenNoSinger;
            var previousProject = DocManager.Inst.TakeProjectForTest(project);
            DocManager.Inst.CommandSink = cmd => cmd.Execute();
            try {
                Preferences.Default.PlaySineWhenNoSinger = true;
                var tone = SineRenderer.Instance.Render(phrase, new Progress(1), 0,
                    new CancellationTokenSource()).Result;
                Assert.True(tone.samples.Any(sample => sample != 0f));

                Preferences.Default.PlaySineWhenNoSinger = false;
                var silent = SineRenderer.Instance.Render(phrase, new Progress(1), 0,
                    new CancellationTokenSource()).Result;
                Assert.Equal(tone.samples.Length, silent.samples.Length);
                Assert.All(silent.samples, sample => Assert.Equal(0f, sample));
                Assert.Equal(layout.leadingMs, silent.leadingMs);
                Assert.Equal(layout.estimatedLengthMs, silent.estimatedLengthMs);
            } finally {
                Preferences.Default.PlaySineWhenNoSinger = original;
                DocManager.Inst.CommandSink = null!;
                DocManager.Inst.TakeProjectForTest(previousProject);
            }
        }

        [Fact]
        public void PreferenceOffChangesPhraseHash() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 480, 60));

            var original = Preferences.Default.PlaySineWhenNoSinger;
            try {
                Preferences.Default.PlaySineWhenNoSinger = true;
                var withTone = RenderPhrase.FromPart(project, track, part).Single();
                Preferences.Default.PlaySineWhenNoSinger = false;
                var withoutTone = RenderPhrase.FromPart(project, track, part).Single();

                Assert.NotEqual(withTone.hash, withoutTone.hash);
            } finally {
                Preferences.Default.PlaySineWhenNoSinger = original;
            }
        }

        class MockLoadedSinger : USinger {
            public MockLoadedSinger() {
                found = true;
                loaded = true;
            }
            public override string Id => "mock";
            public override string Name => "mock";
            public override USingerType SingerType => USingerType.Classic;
            public override System.Collections.Generic.IList<USubbank> Subbanks => new USubbank[0];
        }
    }
}
