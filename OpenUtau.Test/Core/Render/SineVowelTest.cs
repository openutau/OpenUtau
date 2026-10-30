using System;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class SineVowelTest {
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

        static UNote MakeNote(int position, int duration, int tone, string lyric = "a") {
            var note = UNote.Create();
            note.position = position;
            note.duration = duration;
            note.tone = tone;
            note.lyric = lyric;
            return note;
        }

        [Theory]
        [InlineData("ka", 'a', false)]
        [InlineData("ki", 'i', false)]
        [InlineData("ku", 'u', false)]
        [InlineData("ke", 'e', false)]
        [InlineData("ko", 'o', false)]
        [InlineData("shi", 'i', false)]
        [InlineData("tsu", 'u', false)]
        [InlineData("fu", 'u', false)]
        [InlineData("sha", 'a', false)]
        [InlineData("chi", 'i', false)]
        [InlineData("jo", 'o', false)]
        [InlineData("rya", 'a', false)]
        [InlineData("si", 'i', false)]
        [InlineData("tu", 'u', false)]
        [InlineData("wo", 'o', true)]
        public void AnalyzesRomajiMorae(string lyric, char vowel, bool bareVowel) {
            Assert.Equal(((char?)vowel, bareVowel), SineVowels.Analyze(lyric));
        }

        [Theory]
        [InlineData("か", 'a', false)]
        [InlineData("し", 'i', false)]
        [InlineData("つ", 'u', false)]
        [InlineData("きゃ", 'a', false)]
        [InlineData("カ", 'a', false)]
        [InlineData("シ", 'i', false)]
        [InlineData("ヌ", 'u', false)]
        public void AnalyzesKanaMorae(string lyric, char vowel, bool bareVowel) {
            Assert.Equal(((char?)vowel, bareVowel), SineVowels.Analyze(lyric));
        }

        [Theory]
        [InlineData("a", 'a', true)]
        [InlineData("i", 'i', true)]
        [InlineData("u", 'u', true)]
        [InlineData("e", 'e', true)]
        [InlineData("o", 'o', true)]
        [InlineData("あ", 'a', true)]
        [InlineData("い", 'i', true)]
        public void DetectsBareVowels(string lyric, char vowel, bool bareVowel) {
            Assert.Equal(((char?)vowel, bareVowel), SineVowels.Analyze(lyric));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("n")]
        [InlineData("nn")]
        [InlineData("ん")]
        [InlineData("ン")]
        [InlineData("hello")]
        [InlineData("la")]
        [InlineData("day")]
        [InlineData("zzz")]
        [InlineData("R")]
        [InlineData("-")]
        public void FallsBackOnUnknownLyrics(string lyric) {
            Assert.Equal(((char?)null, false), SineVowels.Analyze(lyric));
        }

        [Fact]
        public void MergesAdjacentBareVowelNotesIntoOnePhrase() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 480, 60, "a"));
            part.notes.Add(MakeNote(480, 240, 64, "i"));

            var phrases = RenderPhrase.FromPart(project, track, part);

            var phrase = Assert.Single(phrases);
            Assert.Equal(720, phrase.duration);
            Assert.Equal(2, phrase.phones.Length);
            Assert.Equal("a", phrase.phones[0].phoneme);
            Assert.Equal("i", phrase.phones[1].phoneme);
        }

        [Fact]
        public void DoesNotMergeVowelIntoConsonantOnset() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 480, 60, "a"));
            part.notes.Add(MakeNote(480, 240, 64, "ka"));

            var phrases = RenderPhrase.FromPart(project, track, part);

            Assert.Equal(2, phrases.Count);
            Assert.Single(phrases[0].phones);
            Assert.Single(phrases[1].phones);
            Assert.Equal("a", phrases[1].phones[0].phoneme);
        }

        [Fact]
        public void DoesNotMergeNotesWithGap() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 480, 60, "a"));
            part.notes.Add(MakeNote(960, 240, 64, "i"));

            Assert.Equal(2, RenderPhrase.FromPart(project, track, part).Count);
        }

        [Fact]
        public void NonJapaneseLyricsAreNotMerged() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 240, 60, "a"));
            part.notes.Add(MakeNote(240, 240, 62, "hello"));
            part.notes.Add(MakeNote(480, 240, 64, "hello"));
            part.notes.Add(MakeNote(720, 240, 65, "hello"));
            part.notes.Add(MakeNote(960, 240, 67, "hello"));

            var phrases = RenderPhrase.FromPart(project, track, part);

            Assert.Equal(5, phrases.Count);
            Assert.All(phrases, phrase => Assert.Single(phrase.phones));
            Assert.Equal("a", phrases[0].phones[0].phoneme);
        }

        static double Rms(float[] samples, int start, int count) {
            double sum = 0;
            int end = Math.Min(start + count, samples.Length);
            for (int i = Math.Max(0, start); i < end; ++i) {
                sum += samples[i] * samples[i];
            }
            return Math.Sqrt(sum / Math.Max(1, end - start));
        }

        static float[] SynthesizePhrase(RenderPhrase phrase) {
            var result = SineRenderer.Instance.Layout(phrase);
            int totalSamples = (int)Math.Ceiling(result.estimatedLengthMs / 1000.0 * 44100);
            return SineRenderer.Instance.Synthesize(phrase, result, totalSamples);
        }

        [Fact]
        public void MergedVowelRenderHasNoDipAtBoundary() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 480, 60, "a"));
            part.notes.Add(MakeNote(480, 480, 64, "i"));

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            var samples = SynthesizePhrase(phrase);

            Assert.True(samples.Length >= 44000);
            double boundaryMs = phrase.timeAxis.TickPosToMsPos(part.position + 480) - phrase.positionMs;
            int boundary = (int)(boundaryMs / 1000.0 * 44100);
            double midRms = Rms(samples, 44100 / 4, 44100 / 10);
            double minBoundaryRms = Enumerable.Range(0, 33)
                .Select(i => Rms(samples, boundary - i * 110 - 2205 / 10, 2205 / 10))
                .Min();
            Assert.True(midRms > 0.05, $"midRms={midRms}");
            Assert.True(minBoundaryRms > midRms * 0.4, $"minBoundaryRms={minBoundaryRms}, midRms={midRms}");
            Assert.True(samples.Take(44).All(v => Math.Abs(v) < 0.05), "attack must be faded in");
            Assert.True(samples.Skip(samples.Length - 44).All(v => Math.Abs(v) < 0.05), "release must be faded out");
            Assert.True(samples.Max(Math.Abs) <= 0.45, "no clipping");
        }

        [Fact]
        public void FallbackRenderIsPlainSine() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 960, 60, "zzz"));

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            Assert.Equal("sine", phrase.phones[0].phoneme);
            var samples = SynthesizePhrase(phrase);

            double midRms = Rms(samples, samples.Length / 4, samples.Length / 10);
            Assert.Equal(0.25 / Math.Sqrt(2), midRms, precision: 2);
        }

        [Fact]
        public void VowelRenderFollowsPitch() {
            var project = CreateProject(out var track, out var part);
            part.notes.Add(MakeNote(0, 960, 69, "ka"));

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            Assert.Equal("a", phrase.phones[0].phoneme);
            var samples = SynthesizePhrase(phrase);

            double midRms = Rms(samples, samples.Length / 3, samples.Length / 6);
            Assert.True(midRms > 0.03, $"midRms={midRms}");
        }
    }
}
