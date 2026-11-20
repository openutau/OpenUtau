using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// End to end of the custom parameter path: the files a voicebank ships
    /// (dsconfig.yaml, dsvariance/dsconfig.yaml, params.yaml) resolve to a parameter
    /// list, and that list decides the per-frame values the acoustic model takes and
    /// the channel layout of the variance model.
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class DiffSingerParamsIntegrationTest {
        const string DsconfigWithParameters = @"
acoustic: acoustic.onnx
vocoder: nsf_hifigan
use_energy_embed: true
use_emotion_embed: true
use_gain_embed: true
";

        const string VarianceDsconfig = @"
variance: variance.onnx
linguistic: linguistic.onnx
predict_energy: true
predict_emotion: true
";

        class TestSinger : USinger {
            readonly UOto oto;
            public TestSinger(UOto oto) {
                this.oto = oto;
                found = true;
                loaded = true;
            }
            public override string Id => "params-integration-test-singer";
            public override IList<USubbank> Subbanks => new USubbank[0];
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = phoneme == "A" ? this.oto : (UOto)null;
                return oto != null;
            }
            public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
                oto = null;
                return false;
            }
        }

        class TestRenderer : IRenderer {
            public USingerType SingerType => USingerType.DiffSinger;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => true;
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

        [Fact]
        public void DeclaredParametersDriveTheModels() {
            string dir = CreateVoicebank(DsconfigWithParameters, VarianceDsconfig, @"
params:
  - name: emotion
    abbr: emo
    category: variance
    min: -100
    max: 100
    default: 0
    scale: 0.01
    value_min: -1
    value_max: 1
  - name: gain
    abbr: gain
    category: acoustic
    min: 0
    max: 100
    default: 0
    scale: 0.01
");
            try {
                var config = DsConfig.Load(dir);
                var varianceConfig = DsConfig.Load(Path.Combine(dir, "dsvariance"));
                var registry = DsParamRegistry.From(config);

                // dsconfig.yaml decides what the models take; params.yaml decides how
                // the curve looks and combines. Variance parameters keep the model's
                // head order: built-ins first, then the file's order.
                Assert.Equal(new[] { "energy", "emotion" }, registry.Variance.Select(p => p.Name).ToArray());
                Assert.Equal(new[] { "gain" }, registry.Acoustic.Select(p => p.Name).ToArray());
                Assert.True(config.UsesEmbed("energy"));
                Assert.True(config.UsesEmbed("emotion"));
                Assert.True(config.UsesEmbed("gain"));
                Assert.True(varianceConfig.Predicts("energy"));
                Assert.True(varianceConfig.Predicts("emotion"));
                Assert.False(varianceConfig.Predicts("gain"));

                var (phrase, frameMs, headFrames, tailFrames, totalFrames) = BuildPhrase(
                    registry,
                    new Dictionary<string, int> {
                        [DiffSingerUtils.ENE] = -100,
                        ["emo"] = 50,
                        ["gain"] = 25,
                    });

                // An acoustic-only parameter is its curve.
                var gain = DiffSingerParamInputs.Acoustic(
                    phrase, registry.ByName("gain")!, frameMs, totalFrames, headFrames, tailFrames);
                Assert.Equal(totalFrames, gain.Length);
                // An acoustic curve is scaled into the model's units: 25 * 0.01.
                Assert.All(gain, value => Assert.Equal(0.25f, value, 5));

                // A variance parameter combines its prediction with the curve.
                var varianceResult = Result(
                    registry.Variance,
                    new Dictionary<string, float> { ["energy"] = -30f, ["emotion"] = 0.2f },
                    frameMs, headFrames, tailFrames, totalFrames);
                var energy = DiffSingerParamInputs.Variance(
                    phrase, registry.ByName("energy")!, varianceResult.Channel("energy")!, varianceResult,
                    frameMs, totalFrames, headFrames, tailFrames);
                var emotion = DiffSingerParamInputs.Variance(
                    phrase, registry.ByName("emotion")!, varianceResult.Channel("emotion")!, varianceResult,
                    frameMs, totalFrames, headFrames, tailFrames);
                // energy's curve is scaled by 12/100; emotion's by the 0.01 it declares.
                Assert.All(energy, value => Assert.Equal(-42f, value, 5));
                Assert.All(emotion, value => Assert.Equal(0.7f, value, 5));

                // The predictions and the retake mask are positional: only this order fits.
                Assert.True(DiffSingerVariancePatch.IsChannelLayoutCompatible(
                    varianceResult, registry.Variance, totalFrames));
                var reordered = new VarianceResult(
                    new[] { "emotion", "energy" },
                    new[] { varianceResult.Channel("emotion")!, varianceResult.Channel("energy")! },
                    (float)frameMs, headFrames, tailFrames, totalFrames);
                Assert.False(DiffSingerVariancePatch.IsChannelLayoutCompatible(
                    reordered, registry.Variance, totalFrames));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void OverridingABuiltinChangesHowItsCurveCombines() {
            string dir = CreateVoicebank(@"
acoustic: acoustic.onnx
vocoder: nsf_hifigan
use_energy_embed: true
", @"
variance: variance.onnx
linguistic: linguistic.onnx
predict_energy: true
", @"
params:
  - abbr: ene
    mode: mul
    default: 100
    scale: 0.01
");
            try {
                var config = DsConfig.Load(dir);
                var registry = DsParamRegistry.From(config);
                var energy = registry.ByName("energy")!;

                // Reusing the abbreviation keeps the parameter, its position and its
                // curve range; only what the entry says changes.
                Assert.Equal(DsParamMode.Multiply, energy.Mode);
                Assert.False(energy.IsBuiltIn);
                Assert.Equal(-100f, energy.Descriptor.min);

                var (phrase, frameMs, headFrames, tailFrames, totalFrames) = BuildPhrase(
                    registry,
                    new Dictionary<string, int> { [DiffSingerUtils.ENE] = 20 });
                var varianceResult = Result(
                    registry.Variance,
                    new Dictionary<string, float> { ["energy"] = -0.5f },
                    frameMs, headFrames, tailFrames, totalFrames);
                var values = DiffSingerParamInputs.Variance(
                    phrase, energy, varianceResult.Channel("energy")!, varianceResult,
                    frameMs, totalFrames, headFrames, tailFrames);

                // mul: prediction * curve * scale = -0.5 * 20 * 0.01.
                Assert.All(values, value => Assert.Equal(-0.1f, value, 5));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void RebindingToADbParameterReadsItsPhraseField() {
            // Rebinding energy to brec: breathiness is disabled, so no parameter owns
            // that abbreviation and the definition is a rebind. RenderPhrase folds brec
            // into the phrase's breathiness field, so the lookup must follow the final
            // abbreviation rather than the curve list.
            string dir = CreateVoicebank(@"
acoustic: acoustic.onnx
vocoder: nsf_hifigan
use_energy_embed: true
", @"
variance: variance.onnx
linguistic: linguistic.onnx
predict_energy: true
", @"
params:
  - name: energy
    abbr: brec
");
            try {
                var config = DsConfig.Load(dir);
                var registry = DsParamRegistry.From(config);
                var energy = registry.ByName("energy")!;
                Assert.Equal(Format.Ustx.BREC, energy.Abbr);

                var (phrase, frameMs, headFrames, tailFrames, totalFrames) = BuildPhrase(
                    registry,
                    new Dictionary<string, int> { [Format.Ustx.BREC] = 50 });
                var varianceResult = Result(
                    registry.Variance,
                    new Dictionary<string, float> { ["energy"] = -30f },
                    frameMs, headFrames, tailFrames, totalFrames);

                var values = DiffSingerParamInputs.Variance(
                    phrase, energy, varianceResult.Channel("energy")!, varianceResult,
                    frameMs, totalFrames, headFrames, tailFrames);

                // 50 curve units of the brec curve, scaled by the inherited 12/100.
                Assert.All(values, value => Assert.Equal(-24f, value, 4));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void RealCurvesUseTheCapturedParameterList() {
            // The display must follow the same parameter definitions the acoustic
            // inputs used, even if the singer's registry changed meanwhile.
            string dir = CreateVoicebank(@"
acoustic: acoustic.onnx
vocoder: nsf_hifigan
use_emotion_embed: true
", @"
variance: variance.onnx
linguistic: linguistic.onnx
predict_emotion: true
", @"
params:
  - name: emotion
    abbr: emo
    category: variance
    min: 0
    max: 100
    default: 0
    scale: 0.01
    value_min: 0
    value_max: 1
");
            try {
                var config = DsConfig.Load(dir);
                var registry = DsParamRegistry.From(config);
                var emotion = registry.ByName("emotion")!;

                var (phrase, frameMs, headFrames, tailFrames, totalFrames) = BuildPhrase(
                    registry,
                    new Dictionary<string, int> { ["emo"] = 50 });
                var varianceResult = Result(
                    registry.Variance,
                    new Dictionary<string, float> { ["emotion"] = 0f },
                    frameMs, headFrames, tailFrames, totalFrames);

                var curves = new DiffSingerRenderer().BuildRenderedRealCurves(
                    phrase, varianceResult, new[] { emotion });

                var curve = Assert.Single(curves);
                Assert.Equal("emo", curve.abbr);
                // prediction 0 + curve 50 * 0.01 = 0.5, normalized over value 0..1.
                Assert.All(curve.values, value => Assert.Equal(0.5f, value, 4));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void OverridingAVarianceParameterKeepsReadingItsCurve() {
            // voicing's curve is not in the phrase's curve list: RenderPhrase folds it
            // into the phrase's voicing field. An override must keep reading that.
            string dir = CreateVoicebank(@"
acoustic: acoustic.onnx
vocoder: nsf_hifigan
use_voicing_embed: true
", @"
variance: variance.onnx
linguistic: linguistic.onnx
predict_voicing: true
", @"
params:
  - abbr: voic
    mode: gain
    default: 100
    scale: 0.01
");
            try {
                var config = DsConfig.Load(dir);
                var registry = DsParamRegistry.From(config);
                var voicing = registry.ByName("voicing")!;
                Assert.Equal(DsParamMode.Gain, voicing.Mode);
                Assert.False(voicing.IsBuiltIn);

                var (phrase, frameMs, headFrames, tailFrames, totalFrames) = BuildPhrase(
                    registry,
                    new Dictionary<string, int> { [Format.Ustx.VOIC] = 50 });
                var varianceResult = Result(
                    registry.Variance,
                    new Dictionary<string, float> { ["voicing"] = -20f },
                    frameMs, headFrames, tailFrames, totalFrames);

                var values = DiffSingerParamInputs.Variance(
                    phrase, voicing, varianceResult.Channel("voicing")!, varianceResult,
                    frameMs, totalFrames, headFrames, tailFrames);

                // The drawn curve (50) halves the amplitude: -6.02 dB below the prediction.
                Assert.All(values, value => Assert.Equal(-26.0206f, value, 4));
            } finally {
                Directory.Delete(dir, true);
            }
        }

        static string CreateVoicebank(string dsconfig, string varianceDsconfig, string parameters) {
            string dir = Path.Combine(Path.GetTempPath(), "ou-ds-params-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "dsvariance"));
            File.WriteAllText(Path.Combine(dir, "dsconfig.yaml"), dsconfig);
            File.WriteAllText(Path.Combine(dir, "dsvariance", "dsconfig.yaml"), varianceDsconfig);
            File.WriteAllText(Path.Combine(dir, "params.yaml"), parameters);
            return dir;
        }

        /// <summary>
        /// One note and one phoneme at 120 BPM, with the given curves drawn. The
        /// renderer only needs to accept the expressions: the values come from the
        /// phrase, not from a model.
        /// </summary>
        static (RenderPhrase phrase, double frameMs, int headFrames, int tailFrames, int totalFrames)
                BuildPhrase(DsParamRegistry registry, Dictionary<string, int> curves) {
            var project = new UProject();
            // A real project carries the default expressions; the voicebank's own
            // parameters are added on top of them.
            Format.Ustx.AddDefaultExpressions(project);
            foreach (var param in registry.All) {
                if (!project.expressions.ContainsKey(param.Abbr)) {
                    project.RegisterExpression(param.Descriptor.Clone());
                }
            }
            var track = project.tracks[0];
            track.Singer = new TestSinger(UOto.OfDummy("A"));
            track.RendererSettings.Renderer = new TestRenderer();
            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);

            var note = UNote.Create();
            note.position = 0;
            note.duration = 480;
            note.ExtendedDuration = 480;
            note.tone = 60;
            note.lyric = "a";
            part.notes.Add(note);
            var phoneme = new UPhoneme {
                position = 0,
                phoneme = "A",
                Parent = note,
            };
            part.phonemes.Add(phoneme);
            phoneme.Validate(new ValidateOptions(), project, track, part, note);
            Assert.False(phoneme.Error, phoneme.ErrorException?.ToString());

            foreach (var (abbr, value) in curves) {
                var curve = new UCurve(project.expressions[abbr]);
                curve.xs.AddRange(new[] { 0, 480 });
                curve.ys.AddRange(new[] { value, value });
                part.curves.Add(curve);
            }

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));

            // 120 BPM, 10 ms frames: the note is 500 ms long.
            const double frameMs = 10;
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            int totalFrames = DiffSingerUtils
                .PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames)
                .Sum();
            return (phrase, frameMs, headFrames, tailFrames, totalFrames);
        }

        /// <summary>A variance prediction laid out like the model's, one channel per parameter.</summary>
        static VarianceResult Result(
                IReadOnlyList<DsParam> parameters, Dictionary<string, float> predictions,
                double frameMs, int headFrames, int tailFrames, int totalFrames) {
            var names = parameters.Select(param => param.Name).ToArray();
            var channels = new float[]?[names.Length];
            for (int i = 0; i < names.Length; i++) {
                var values = new float[totalFrames];
                Array.Fill(values, predictions.GetValueOrDefault(names[i]));
                channels[i] = values;
            }
            return new VarianceResult(names, channels, (float)frameMs, headFrames, tailFrames, totalFrames);
        }
    }
}
