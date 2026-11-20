using System;
using System.IO;
using System.Linq;
using System.Threading;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using Xunit;
using UstxFormat = OpenUtau.Core.Format.Ustx;

namespace OpenUtau.Core {
    /// <summary>
    /// Live check against a voicebank whose acoustic model was exported with
    /// inpainting (`use_inpaint: true`): an edit must leave the mel, and as much of
    /// the audio as the vocoder allows, outside the changed frames untouched.
    ///
    /// Opt-in, because it needs a prepared voicebank on disk:
    /// <code>
    /// OPENUTAU_TEST_INPAINT_SINGERS=E:\temp\Singers dotnet run --project OpenUtau.Test
    /// </code>
    /// Skipped when that variable is unset, so CI is unaffected.
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class DiffSingerAcousticInpaintLiveTest {
        /// <summary>Room for the vocoder's receptive field around the regenerated frames.</summary>
        const int VocoderGuardSamples = 8192;

        /// <summary>
        /// With the level off the graph still needs its inpainting inputs; they must be
        /// fed as an all-true mask over a zero reference, not omitted.
        /// </summary>
        [Fact]
        public void FullReRenderFeedsTheRequiredInputsWithoutKeepingAnything() {
            using var fixture = new Fixture();
            Preferences.Default.DiffSingerLocalRetakeLevel = (int)DiffSingerLocalRetake.Off;
            var (project, track, part) = BuildPart(fixture.Singer, fixture.Renderer);
            var velocity = AddCurve(project, part, DiffSingerUtils.VELC, "velocity (curve)", 0, 200, 100);

            var samples1 = fixture.Render(BuildPhrase(project, track, part, 1));
            Assert.Empty(fixture.DsSinger.AcousticInpaintStates.Snapshot());

            velocity.ys[velocity.ys.Count / 2] = 160;
            var samples2 = fixture.Render(BuildPhrase(project, track, part, 2));

            Assert.Empty(fixture.DsSinger.AcousticInpaintStates.Snapshot());
            Assert.Equal(samples1.Length, samples2.Length);
            Assert.True(samples1.Where((sample, index) => sample != samples2[index]).Any(),
                "A full re-render should sample the phrase again.");
        }

        /// <summary>
        /// A velocity edit changes no pitch, so the vocoder source is identical and the
        /// whole waveform outside the regenerated frames must be bit-identical.
        /// </summary>
        [Fact]
        public void VelocityEditKeepsTheWholeRestOfTheAudio() {
            using var fixture = new Fixture();
            var (project, track, part) = BuildPart(fixture.Singer, fixture.Renderer);
            var velocity = AddCurve(project, part, DiffSingerUtils.VELC, "velocity (curve)", 0, 200, 100);

            var phrase1 = BuildPhrase(project, track, part, 1);
            var samples1 = fixture.Render(phrase1);
            var state1 = fixture.TakeState();

            velocity.ys[velocity.ys.Count / 2] = 160;
            var phrase2 = BuildPhrase(project, track, part, 2);
            Assert.NotEqual(phrase1.hash, phrase2.hash);
            var samples2 = fixture.Render(phrase2);
            var state2 = fixture.TakeState();

            bool[] mask = CompareMask(state1, state2);
            Assert.Equal(samples1.Length, samples2.Length);
            AssertNoDifferenceOutside(fixture.HopSize, mask, samples1, samples2, out bool insideChanged);
            Assert.True(insideChanged, "The edited region should sound different.");
        }

        /// <summary>
        /// A pitch edit changes the vocoder's phase source from the edit onward, so the
        /// waveform after it cannot be sample-identical; everything before it must be,
        /// and the mel outside the mask must be bit-identical.
        /// </summary>
        [Fact]
        public void PitchEditKeepsTheMelAndTheAudioBeforeIt() {
            using var fixture = new Fixture();
            var (project, track, part) = BuildPart(fixture.Singer, fixture.Renderer);
            var pitch = AddCurve(project, part, UstxFormat.PITD, "pitch bend", -1200, 1200, 0);

            var phrase1 = BuildPhrase(project, track, part, 1);
            var samples1 = fixture.Render(phrase1);
            var state1 = fixture.TakeState();

            pitch.ys[pitch.ys.Count / 2] += 200;
            var phrase2 = BuildPhrase(project, track, part, 2);
            Assert.NotEqual(phrase1.hash, phrase2.hash);
            var samples2 = fixture.Render(phrase2);
            var state2 = fixture.TakeState();

            bool[] mask = CompareMask(state1, state2);
            Assert.True(
                DiffSingerAcousticInpaint.VerifyRetainedFrames(
                    state2.Mel, state1.Mel, mask, state1.TotalFrames, state1.Bins, out float maxDifference),
                $"Kept mel frames changed (max difference {maxDifference}).");

            Assert.Equal(samples1.Length, samples2.Length);
            int firstChanged = Array.IndexOf(mask, true);
            int guardStart = Math.Max(0, firstChanged * fixture.HopSize - VocoderGuardSamples);
            for (int i = 0; i < guardStart; i++) {
                if (samples1[i] != samples2[i]) {
                    Assert.Fail($"Sample {i} before the edit changed, but nothing before it was regenerated.");
                }
            }
        }

        static bool[] CompareMask(AcousticInpaintState previous, AcousticInpaintState current) {
            // The mask the renderer used, rebuilt from the two condition snapshots.
            bool[] mask = DiffSingerAcousticInpaint.Expand(
                DiffSingerAcousticInpaint.BuildChangedMask(
                    previous.Conditions, current.Conditions, previous.TotalFrames),
                DiffSingerAcousticInpaint.MarginFrames);
            Assert.Contains(true, mask);
            Assert.Contains(false, mask);
            Assert.True(DiffSingerAcousticInpaint.CountChanged(mask) < previous.TotalFrames,
                "The edit should not have regenerated the whole phrase.");
            Assert.True(
                DiffSingerAcousticInpaint.VerifyRetainedFrames(
                    current.Mel, previous.Mel, mask, previous.TotalFrames, previous.Bins, out float maxDifference),
                $"Kept mel frames changed (max difference {maxDifference}).");
            return mask;
        }

        static void AssertNoDifferenceOutside(
                int hopSize, bool[] mask, float[] before, float[] after, out bool insideChanged) {
            int firstChanged = Array.IndexOf(mask, true);
            int lastChanged = Array.LastIndexOf(mask, true);
            int changedStart = Math.Max(0, firstChanged * hopSize - VocoderGuardSamples);
            int changedEnd = Math.Min(before.Length, (lastChanged + 1) * hopSize + VocoderGuardSamples);
            insideChanged = false;
            int outsideDifferences = 0;
            for (int i = 0; i < before.Length; i++) {
                if (before[i] == after[i]) {
                    continue;
                }
                if (i >= changedStart && i < changedEnd) {
                    insideChanged = true;
                } else {
                    outsideDifferences++;
                }
            }
            Assert.Equal(0, outsideDifferences);
        }

        static RenderPhrase BuildPhrase(UProject project, UTrack track, UVoicePart part, long generation) {
            var source = PhraseSource.FromPart(project, track, part, generation);
            Assert.True(source != null, "Phrase source should not be empty.");
            return source!.BuildPhrases().Single();
        }

        static UCurve AddCurve(
                UProject project, UVoicePart part, string abbr, string name,
                float min, float max, int defaultValue) {
            var descriptor = new UExpressionDescriptor(name, abbr, min, max, defaultValue) {
                type = UExpressionType.Curve,
            };
            project.RegisterExpression(descriptor);
            var curve = new UCurve(descriptor);
            for (int tick = 0; tick <= 960; tick += 20) {
                curve.xs.Add(tick);
                curve.ys.Add(defaultValue);
            }
            part.curves.Add(curve);
            return curve;
        }

        static (UProject, UTrack, UVoicePart) BuildPart(USinger singer, IRenderer renderer) {
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
            note1.lyric = "a";
            note1.ExtendedDuration = 480;
            note0.Next = note1;
            note1.Prev = note0;
            part.notes.Add(note0);
            part.notes.Add(note1);

            string phonemeName = "zh/A";
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

        /// <summary>Finds, loads and restores the inpainting voicebank from the environment.</summary>
        sealed class Fixture : IDisposable {
            readonly string? previousSingerPath;
            readonly int? previousLevel;
            readonly bool previousCache;
            readonly int previousSteps;
            readonly DiffSingerSinger dsSinger;

            public DiffSingerSinger DsSinger => dsSinger;
            public USinger Singer => dsSinger;
            public DiffSingerRenderer Renderer { get; } = new DiffSingerRenderer();
            public int HopSize { get; }

            public Fixture() {
                string? root = Environment.GetEnvironmentVariable("OPENUTAU_TEST_INPAINT_SINGERS");
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) {
                    Assert.Skip("Set OPENUTAU_TEST_INPAINT_SINGERS to a folder with an inpainting DiffSinger voicebank.");
                }
                previousSingerPath = Preferences.Default.AdditionalSingerPath;
                previousLevel = Preferences.Default.DiffSingerLocalRetakeLevel;
                previousCache = Preferences.Default.DiffSingerTensorCache;
                previousSteps = Preferences.Default.DiffSingerSteps;

                Preferences.Default.AdditionalSingerPath = root!;
                Preferences.Default.DiffSingerLocalRetakeLevel = (int)DiffSingerLocalRetake.VarianceAndAcoustic;
                Preferences.Default.DiffSingerTensorCache = false;
                Preferences.Default.DiffSingerSteps = 5;
                // VoicebankLoader opens character.txt with shift_jis; the app registers
                // this provider in Program.cs, a test host has to do it itself.
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                // A wav cached by an earlier run would skip the acoustic stage (and its
                // state) entirely, so start from a clean DiffSinger render cache.
                foreach (var file in Directory.EnumerateFiles(
                    PathManager.Inst.CachePath, "ds-*.wav", SearchOption.TopDirectoryOnly)) {
                    File.Delete(file);
                }

                SingerManager.Inst.SearchAllSingers();
                var singer = SingerManager.Inst.Singers.Values
                    .FirstOrDefault(s => s.SingerType == USingerType.DiffSinger);
                if (singer == null) {
                    Assert.Fail($"No DiffSinger voicebank found under {root}.");
                    throw new InvalidOperationException();
                }
                singer.EnsureLoaded();
                Assert.True(singer.Loaded, $"{singer.Id} failed to load: {string.Join("; ", singer.Errors)}");
                dsSinger = (DiffSingerSinger)singer;
                Assert.True(dsSinger.dsConfig.use_inpaint,
                    $"{dsSinger.Name} is expected to declare use_inpaint: true.");
                HopSize = dsSinger.getVocoder().hop_size;
            }

            public float[] Render(RenderPhrase phrase) {
                using var cancellation = new CancellationTokenSource();
                var result = Renderer.Render(phrase, new Progress(phrase.phones.Length), 0, cancellation, false)
                    .GetAwaiter().GetResult();
                Assert.NotNull(result.samples);
                return result.samples;
            }

            /// <summary>The state the acoustic render just kept, with the phrase identity checked.</summary>
            public AcousticInpaintState TakeState() {
                var states = dsSinger.AcousticInpaintStates.Snapshot();
                Assert.Single(states);
                return states[0];
            }

            public void Dispose() {
                Preferences.Default.AdditionalSingerPath = previousSingerPath;
                Preferences.Default.DiffSingerLocalRetakeLevel = previousLevel;
                Preferences.Default.DiffSingerTensorCache = previousCache;
                Preferences.Default.DiffSingerSteps = previousSteps;
            }
        }
    }
}
