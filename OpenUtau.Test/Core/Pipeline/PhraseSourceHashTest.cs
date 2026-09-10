using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.Pipeline {
    /// <summary>
    /// Hash-identity gate: the same document must produce the same
    /// <c>phrase.hash</c> / <c>phone.hash</c> values regardless of which code
    /// path builds the phrases, so every on-disk cache stays valid.
    /// </summary>
    public class PhraseSourceHashTest {
        class FixtureSinger : USinger {
            readonly UOto otoA;
            public FixtureSinger(UOto a) {
                otoA = a;
                found = true;
                loaded = true;
            }
            public override string Id => "hash-fixture-singer";
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

        class FixtureRenderer : IRenderer {
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) =>
                descriptor.abbr == Format.Ustx.MODP;
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    System.Threading.CancellationTokenSource cancellation, bool isPreRender = false,
                    RenderPhraseEvents? renderEvents = null) => throw new NotImplementedException();
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        /// <summary>
        /// A fixture exercising the interesting corners of phrase build: a
        /// spline pitch segment, vibrato with volume link, a tuning offset,
        /// an extended ("+") note, a gap that splits a second phrase, and the
        /// pitd / dyn / xsy / custom curves.
        /// </summary>
        static (UProject project, UTrack track, UVoicePart part) BuildFixture() {
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
            project.RegisterExpression(new UExpressionDescriptor("modulation plus", "modp", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("pitch (curve)", "pitd", -5000, 5000, 0) {
                type = UExpressionType.Curve,
            });
            project.RegisterExpression(new UExpressionDescriptor("dynamics (curve)", "dyn", -60, 24, 0) {
                type = UExpressionType.Curve,
            });
            project.RegisterExpression(new UExpressionDescriptor("tension (curve)", "tenc", 0, 100, 50) {
                type = UExpressionType.Curve,
            });
            project.RegisterExpression(new UExpressionDescriptor("custom (curve)", "cstm", 0, 100, 0) {
                type = UExpressionType.Curve,
            });
            project.RegisterExpression(new UExpressionDescriptor("cross synthesis (curve)", "xsy", 0, 100, 0) {
                type = UExpressionType.Curve,
            });

            var track = project.tracks[0];
            track.Singer = new FixtureSinger(UOto.OfDummy("A"));
            track.RendererSettings.Renderer = new FixtureRenderer();
            track.RendererSettings.resampler = null;
            track.RendererSettings.wavtool = null;

            var part = new UVoicePart { trackNo = 0, position = 960 };
            project.parts.Add(part);

            var note0 = UNote.Create();
            note0.position = 0;
            note0.duration = 480;
            note0.tone = 60;
            note0.tuning = 10;
            note0.lyric = "a";
            note0.pitch.data.Clear();
            note0.pitch.AddPoint(new PitchPoint(0, 5, PitchPointShape.sp));
            note0.pitch.AddPoint(new PitchPoint(240, -5, PitchPointShape.sp));
            note0.pitch.AddPoint(new PitchPoint(400, 0, PitchPointShape.io));
            note0.vibrato.length = 60;
            note0.vibrato.period = 180;
            note0.vibrato.depth = 30;
            note0.vibrato.volLink = 40;
            // A per-phoneme velocity so the envelope and overlap math see a
            // non-default consonant stretch.
            note0.phonemeExpressions.Add(new UExpression(project.expressions["vel"]) {
                index = 0, value = 80,
            });

            var note1 = UNote.Create();
            note1.position = 480;
            note1.duration = 480;
            note1.tone = 62;
            note1.lyric = "u";

            // A 480-tick gap after note1 splits the second phrase.
            var note2 = UNote.Create();
            note2.position = 1200;
            note2.duration = 480;
            note2.tone = 64;
            note2.lyric = "e";

            var note3 = UNote.Create();
            note3.position = 1680;
            note3.duration = 240;
            note3.tone = 64;
            note3.lyric = "+o";

            var notes = new[] { note0, note1, note2, note3 };
            for (int i = 0; i < notes.Length; ++i) {
                notes[i].Prev = i > 0 ? notes[i - 1] : null;
                notes[i].Next = i + 1 < notes.Length ? notes[i + 1] : null;
                notes[i].ExtendedDuration = notes[i].duration;
                notes[i].PositionMs = project.timeAxis.TickPosToMsPos(part.position + notes[i].position);
                notes[i].EndMs = project.timeAxis.TickPosToMsPos(part.position + notes[i].End);
                part.notes.Add(notes[i]);
            }
            note3.Extends = note2;
            note2.ExtendedDuration = note3.End - note2.position;

            var pitd = new UCurve(project.expressions["pitd"]);
            pitd.xs.AddRange(new[] { 0, 480 });
            pitd.ys.AddRange(new[] { -10, 20 });
            part.curves.Add(pitd);
            var dyn = new UCurve(project.expressions["dyn"]);
            dyn.xs.AddRange(new[] { 0, 960, 1920 });
            dyn.ys.AddRange(new[] { -6, 0, -12 });
            part.curves.Add(dyn);
            var tenc = new UCurve(project.expressions["tenc"]);
            tenc.xs.AddRange(new[] { 240 });
            tenc.ys.AddRange(new[] { 30 });
            part.curves.Add(tenc);
            var cstm = new UCurve(project.expressions["cstm"]);
            cstm.xs.AddRange(new[] { 720 });
            cstm.ys.AddRange(new[] { 7 });
            part.curves.Add(cstm);
            var xsy = new UCurve(project.expressions["xsy"]);
            xsy.xs.AddRange(new[] { 0, 960 });
            xsy.ys.AddRange(new[] { 50, 100 });
            part.curves.Add(xsy);

            var phonemes = new List<UPhoneme>();
            for (int i = 0; i < notes.Length; ++i) {
                var phoneme = new UPhoneme {
                    position = notes[i].position,
                    phoneme = "A",
                    Parent = notes[i],
                };
                phonemes.Add(phoneme);
            }
            part.phonemes.AddRange(phonemes);
            for (int i = 0; i < phonemes.Count; ++i) {
                phonemes[i].Prev = i > 0 ? phonemes[i - 1] : null;
                phonemes[i].Next = i + 1 < phonemes.Count ? phonemes[i + 1] : null;
                phonemes[i].Validate(new ValidateOptions(), project, track, part, notes[i]);
                Assert.False(phonemes[i].Error, phonemes[i].ErrorException?.ToString());
            }
            return (project, track, part);
        }

        static string Hashes(params RenderPhrase[] phrases) {
            var lines = new List<string>();
            foreach (var phrase in phrases) {
                lines.Add($"p {phrase.hash:x16} {phrase.preEffectHash:x16} {phrase.position} {phrase.end}");
                foreach (var phone in phrase.phones) {
                    lines.Add($"h {phone.hash:x16} {phone.position} {phone.end}");
                }
            }
            return string.Join("\n", lines);
        }

        // Recorded against the original live-document RenderPhrase.FromPart
        // implementation, before the snapshot migration.
        const string GoldenHashes =
            "p 421f4de410612c9f dad0a5c0a538bf0d 960 1920\n" +
            "h 4b02f6dc1b7b8bc0 0 480\n" +
            "h 2c4217fa2a280880 480 960\n" +
            "p e396531782f1541c 28f9028f145623b8 2160 2880\n" +
            "h 0838e47bf75d4e71 0 480\n" +
            "h 5841af07ae5785eb 480 720";

        [Fact]
        public void SnapshotPathProducesIdenticalHashes() {
            var (project, track, part) = BuildFixture();
            var phrases = RenderPhrase.FromPart(project, track, part);
            string actual = Hashes(phrases.ToArray());
            Assert.Equal(2, phrases.Count);
            Assert.Equal(GoldenHashes, actual);
        }
    }
}
