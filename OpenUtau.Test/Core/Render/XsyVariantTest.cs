using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class XsyVariantTest {
        class TestSinger : USinger {
            readonly UOto otoA;
            readonly UOto otoB;
            public TestSinger(UOto a, UOto b) {
                otoA = a;
                otoB = b;
                found = true;
                loaded = true;
            }
            public override string Id => "test-singer";
            public override IList<USubbank> Subbanks => new USubbank[0];
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = phoneme == "A" ? otoA : (UOto)null;
                return oto != null;
            }
            // Only tone 60 has a secondary mapping; tone 61 leaves oto2 unset.
            public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
                oto = color == "B" && tone == 60 ? otoB : (UOto)null;
                return oto != null;
            }
        }

        class StubRenderer : IRenderer {
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => false;
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult();
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    CancellationTokenSource cancellation, bool isPreRender = false, RenderPhraseEvents? renderEvents = null) {
                throw new NotImplementedException();
            }
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        static RenderPhrase CreateXsyPhrase(out UOto otoA, out UOto otoB) {
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
            project.RegisterExpression(new UExpressionDescriptor("cross synthesis (curve)", "xsy", 0, 100, 0) {
                type = UExpressionType.Curve,
            });
            var track = project.tracks[0];
            otoA = UOto.OfDummy("A");
            otoB = UOto.OfDummy("B");
            track.Singer = new TestSinger(otoA, otoB);
            track.VoiceColor2Exp = new UExpressionDescriptor("color2", "clry", 0, 100, 0) {
                options = new[] { "B" },
            };
            track.RendererSettings.Renderer = new StubRenderer();

            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);
            part.curves.Add(new UCurve(project.expressions["xsy"]));

            var note1 = UNote.Create();
            note1.position = 0;
            note1.duration = 480;
            note1.tone = 60;
            note1.lyric = "a";
            note1.ExtendedDuration = 480;
            var note2 = UNote.Create();
            note2.position = 480;
            note2.duration = 480;
            note2.tone = 61;
            note2.lyric = "a";
            note2.ExtendedDuration = 480;
            note1.Next = note2;
            note2.Prev = note1;
            part.notes.Add(note1);
            part.notes.Add(note2);
            var phoneme1 = new UPhoneme { position = 0, phoneme = "A", Parent = note1 };
            var phoneme2 = new UPhoneme { position = 480, phoneme = "A", Parent = note2 };
            part.phonemes.AddRange(new[] { phoneme1, phoneme2 });
            phoneme1.Validate(new ValidateOptions(), project, track, part, note1);
            phoneme2.Validate(new ValidateOptions(), project, track, part, note2);
            Assert.False(phoneme1.Error, phoneme1.ErrorException?.ToString());
            Assert.False(phoneme2.Error);

            return Assert.Single(RenderPhrase.FromPart(project, track, part));
        }

        [Fact]
        public void BuildXsyVariantSwapsOto2AndMasksHashes() {
            var phrase = CreateXsyPhrase(out var otoA, out var otoB);
            var variant = RenderPhrase.BuildXsyVariant(phrase);

            Assert.NotSame(phrase, variant);
            // The phone with oto2 is replaced by a masked copy carrying oto2.
            Assert.NotSame(phrase.phones[0], variant.phones[0]);
            Assert.Equal(otoB, variant.phones[0].oto);
            Assert.Equal(otoB, variant.phones[0].oto2);
            Assert.Equal(phrase.phones[0].hash ^ RenderPhone.Oto2HashMask, variant.phones[0].hash);
            Assert.Equal(phrase.hash ^ RenderPhone.Oto2HashMask, variant.hash);
            // The live phrase is untouched.
            Assert.Equal(otoA, phrase.phones[0].oto);
        }

        [Fact]
        public void BuildXsyVariantKeepsPhonesWithoutOto2() {
            var phrase = CreateXsyPhrase(out _, out _);
            var variant = RenderPhrase.BuildXsyVariant(phrase);

            // The unmapped phone (tone 61) is shared as-is, unmasked.
            Assert.Same(phrase.phones[1], variant.phones[1]);
            Assert.Equal(phrase.phones[1].hash, variant.phones[1].hash);
        }

        [Fact]
        public void BuildXsyVariantSharesStructureAndCacheFiles() {
            var phrase = CreateXsyPhrase(out _, out _);
            var variant = RenderPhrase.BuildXsyVariant(phrase);

            Assert.Same(phrase.singer, variant.singer);
            Assert.Same(phrase.notes, variant.notes);
            Assert.Same(phrase.pitches, variant.pitches);
            Assert.Equal(phrase.position, variant.position);
            Assert.Equal(phrase.preEffectHash, variant.preEffectHash);

            // The cache-file list is shared so both variants' files are cleaned up together.
            var field = typeof(RenderPhrase).GetField("cacheFiles", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.Same(field.GetValue(phrase), field.GetValue(variant));
        }
    }
}
