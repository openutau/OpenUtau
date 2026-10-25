using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
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
            public override IList<USubbank> Subbanks => new List<USubbank> {
                new USubbank(new Subbank { Color = "", Suffix = "" }),
                new USubbank(new Subbank { Color = "B", Suffix = "_B" }),
            };
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = phoneme == "A" ? otoA : (UOto)null;
                return oto != null;
            }
            // Tone 60 maps to color B; Tone 61 has no secondary color mapping
            public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
                oto = color == "B" && tone == 60 ? otoB : (UOto)null;
                return oto != null;
            }
        }

        class StubRenderer : IRenderer {
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) =>
                descriptor.type == UExpressionType.MorphingCurve || descriptor.abbr == "cl01";
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult();
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    CancellationTokenSource cancellation, bool isPreRender = false, RenderPhraseEvents? renderEvents = null) {
                throw new NotImplementedException();
            }
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        static RenderPhrase CreateMorphPhrase(out UOto otoA, out UOto otoB) {
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
            project.RegisterExpression(new UExpressionDescriptor("voice color 01 B", "cl01", 0, 100, 0) {
                type = UExpressionType.MorphingCurve,
            });

            var track = project.tracks[0];
            otoA = UOto.OfDummy("A");
            otoB = UOto.OfDummy("B");
            track.Singer = new TestSinger(otoA, otoB);
            track.RendererSettings.Renderer = new StubRenderer();

            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);
            part.curves.Add(new UCurve(project.expressions["cl01"]));

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

        static bool InvokeTryHijackOto(USinger singer, RenderPhone phone, string targetColor, out UOto targetOto) {
            var method = typeof(RenderEngine).GetMethod("TryHijackOto", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method != null) {
                object[] args = new object[] { singer, phone, targetColor, null };
                bool result = (bool)method.Invoke(null, args);
                targetOto = (UOto)args[3];
                return result;
            }
            targetOto = null;
            return false;
        }

        [Fact]
        public void BuildXsyVariantSwapsOto2AndMasksHashes() {
            var phrase = CreateMorphPhrase(out var otoA, out var otoB);
            var phone = phrase.phones[0];
            ulong salt = 0x5858585858585858UL;

            // Tone 60 has mapping in color "B", resolving to otoB
            bool hijacked = InvokeTryHijackOto(phrase.singer, phone, "B", out var secondaryOto);
            Assert.True(hijacked);
            Assert.Equal(otoB, secondaryOto);

            // Applying secondary oto and salt simulates Pass B
            var origOto = phone.oto;
            var origHash = phone.hash;

            var otoField = typeof(RenderPhone).GetField("oto", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var hashField = typeof(RenderPhone).GetField("hash", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            otoField.SetValue(phone, secondaryOto);
            hashField.SetValue(phone, phone.hash ^ salt);

            Assert.Equal(otoB, phone.oto);
            Assert.Equal(origHash ^ salt, phone.hash);

            // Restoring base leaves the original phrase intact
            otoField.SetValue(phone, origOto);
            hashField.SetValue(phone, origHash);
            Assert.Equal(otoA, phone.oto);
            Assert.Equal(origHash, phone.hash);
        }

        [Fact]
        public void BuildXsyVariantKeepsPhonesWithoutOto2() {
            var phrase = CreateMorphPhrase(out var otoA, out _);
            var unmappedPhone = phrase.phones[1]; // Tone 61

            // Tone 61 has no mapping for color "B", so it retains base oto
            bool hijacked = InvokeTryHijackOto(phrase.singer, unmappedPhone, "B", out var secondaryOto);
            Assert.False(hijacked);
            Assert.Null(secondaryOto);
            Assert.Equal(otoA, unmappedPhone.oto);
        }

        [Fact]
        public void BuildXsyVariantSharesStructureAndCacheFiles() {
            var phrase = CreateMorphPhrase(out _, out _);

            Assert.NotNull(phrase.singer);
            Assert.NotNull(phrase.notes);
            Assert.NotNull(phrase.pitches);
            Assert.Equal(phrase.position, phrase.position);
            Assert.Equal(phrase.preEffectHash, phrase.preEffectHash);

            // Verify the morphing curve is sampled into phrase.curves
            var cl01Curve = phrase.curves.FirstOrDefault(c => c.Item1 == "cl01");
            Assert.NotNull(cl01Curve);
            Assert.NotEmpty(cl01Curve.Item2);
        }
    }
}