using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerParamsTest {
        [Fact]
        public void BuiltinsFollowTheVoicebankFlags() {
            var registry = DsParamRegistry.From(new DsConfig {
                useEnergyEmbed = true,
                useTensionEmbed = true,
                useKeyShiftEmbed = true,
                useSpeedEmbed = true,
            });

            Assert.Equal(new[] { "energy", "tension" }, registry.Variance.Select(p => p.Name));
            Assert.Equal(new[] { DiffSingerUtils.ENE, Format.Ustx.TENC },
                registry.Variance.Select(p => p.Abbr));
            Assert.Equal(new[] { "gender", "velocity" }, registry.Acoustic.Select(p => p.Name));
            Assert.Equal(new[] { Format.Ustx.GENC, DiffSingerUtils.VELC },
                registry.Acoustic.Select(p => p.Abbr));
            Assert.All(registry.All, p => Assert.True(p.IsBuiltIn));
        }

        [Fact]
        public void BuiltinsKeepTheirHistoricalConversions() {
            var registry = DsParamRegistry.From(new DsConfig {
                useEnergyEmbed = true,
                useVoicingEmbed = true,
                useSpeedEmbed = true,
            });
            var energy = registry.ByName("energy")!;
            var voicing = registry.ByName("voicing")!;
            var velocity = registry.ByName("velocity")!;

            // energy: +-100 of curve maps to +-12 of the parameter, clamped to its -96..0 range.
            Assert.Equal(-12f, energy.CombinedValue(0f, -100f), 5);
            // Positive energy would leave the -96..0 range, so it is clamped to its top.
            Assert.Equal(0f, energy.CombinedValue(0f, 100f), 5);
            Assert.Equal(-96f, energy.CombinedValue(-90f, -100f), 5);
            // voicing's neutral curve value is 100, not 0.
            Assert.Equal(0f, voicing.CombinedValue(0f, 100f), 5);
            Assert.Equal(100f, voicing.NeutralCurve, 5);
            // velocity is logarithmic: 200 is twice the speed, 0 half of it.
            Assert.Equal(2f, velocity.AcousticValue(200f), 5);
            Assert.Equal(0.5f, velocity.AcousticValue(0f), 5);
            Assert.Equal(100f, velocity.Descriptor.defaultValue);
        }

        [Fact]
        public void GenderCurveIsConvertedWithTheTrainingRange() {
            var registry = DsParamRegistry.From(new DsConfig {
                useKeyShiftEmbed = true,
                augmentationArgs = new AugmentationArgs {
                    randomPitchShifting = new RandomPitchShifting { range = new[] { -12f, 12f } },
                },
            });
            var gender = registry.ByName("gender")!;

            // The curve is normalized to the trained range: 100 is the whole range, so
            // with +-12 semitones in training the model sees +-1. Positive shifts down.
            Assert.Equal(1f, gender.AcousticValue(-100f), 5);
            Assert.Equal(-1f, gender.AcousticValue(100f), 5);
        }

        [Fact]
        public void CustomParametersAreAppendedAndCombinedInTheirMode() {
            var registry = Registry(
                new DsParamConfig {
                    name = "emotion", abbr = "emo", category = "variance",
                    min = -1, max = 1, defaultValue = 0,
                },
                new DsParamConfig {
                    name = "gain", abbr = "gain", category = "variance", mode = "mul",
                    min = 0, max = 2, defaultValue = 1,
                });

            var emotion = registry.ByName("emotion")!;
            var gain = registry.ByName("gain")!;

            Assert.Equal(DsParamCategory.Variance, emotion.Category);
            Assert.Equal(DsParamMode.Add, emotion.Mode);
            Assert.Equal(0.5f, emotion.CombinedValue(0.3f, 0.2f), 5);
            // The curve is clamped to the parameter's own range.
            Assert.Equal(1f, emotion.CombinedValue(1f, 0.5f), 5);

            Assert.Equal(DsParamMode.Multiply, gain.Mode);
            Assert.Equal(1f, gain.CombinedValue(0.5f, 2f), 5);
            Assert.Equal(2f, gain.CombinedValue(1f, 5f), 5);
            Assert.Equal(1, gain.NeutralCurve);
        }

        [Fact]
        public void CustomAcousticParameterIsTheCurveItself() {
            var registry = Registry(new DsParamConfig {
                name = "emotion", abbr = "emo", category = "acoustic",
                min = -1, max = 1, defaultValue = 0,
            });
            var emotion = registry.ByName("emotion")!;

            Assert.Equal(DsParamCategory.Acoustic, emotion.Category);
            Assert.Equal(0.42f, emotion.AcousticValue(0.42f), 5);
            Assert.Empty(registry.Variance);
        }

        [Fact]
        public void ScaleMapsCurveUnitsIntoParameterUnits() {
            var registry = Registry(new DsParamConfig {
                name = "emotion", abbr = "emo", category = "variance",
                min = -100, max = 100, defaultValue = 0,
                scale = 0.01f, valueMin = -1, valueMax = 1,
            });
            var emotion = registry.ByName("emotion")!;

            // 100 curve units are one parameter unit, like the built-in parameters.
            Assert.Equal(0.7f, emotion.CombinedValue(0.2f, 50f), 5);
            Assert.Equal(1f, emotion.CombinedValue(0.2f, 200f), 5);
            Assert.Equal(0.2f, emotion.CombinedValue(0.2f, 0f), 5);
            Assert.Equal(0.25f, Registry(new DsParamConfig {
                name = "gain", abbr = "gain", category = "acoustic",
                min = 0, max = 100, defaultValue = 0, scale = 0.01f,
            }).ByName("gain")!.AcousticValue(25f), 5);
        }

        [Fact]
        public void GainModeTurnsTheCurveIntoDecibels() {
            var registry = Registry(new DsParamConfig {
                name = "voicing", abbr = "voic", category = "variance", mode = "gain",
                min = 0, max = 100, defaultValue = 100, scale = 0.01f,
                valueMin = -96, valueMax = 0,
            });
            var voicing = registry.ByName("voicing")!;

            // A dB parameter: 100 is unchanged, 50 is half the amplitude, 0 is silent.
            Assert.Equal(-30f, voicing.CombinedValue(-30f, 100f), 5);
            Assert.Equal(-36.0206f, voicing.CombinedValue(-30f, 50f), 4);
            Assert.Equal(-96f, voicing.CombinedValue(-30f, 0f), 5);
        }

        [Fact]
        public void MulRequiresTheDefaultCurveToBeTheNeutralMultiplier() {
            var e = Assert.Throws<InvalidDataException>(() => Registry(new DsParamConfig {
                name = "gain", abbr = "gain", category = "variance", mode = "mul",
                min = 0, max = 100, defaultValue = 0,
            }));
            Assert.Contains("default * scale must be 1", e.Message);
        }

        [Fact]
        public void DisplayScaleTonesDownTheRealCurveLane() {
            var registry = Registry(new DsParamConfig {
                name = "falsetto_dev", abbr = "falc", category = "variance",
                min = 0, max = 100, defaultValue = 0,
                scale = 0.01f, valueMin = 0, valueMax = 1, displayScale = 0.5f,
            });
            var falsetto = registry.ByName("falsetto_dev")!;

            Assert.Equal(0f, falsetto.Normalize(0f), 5);
            Assert.Equal(0.25f, falsetto.Normalize(0.5f), 5);
            Assert.Equal(0.5f, falsetto.Normalize(1f), 5);
            // The model value itself is unaffected.
            Assert.Equal(0.5f, falsetto.CombinedValue(0f, 50f), 5);
        }

        [Fact]
        public void ParameterValuesAreNormalizedForTheRealCurveDisplay() {
            var registry = Registry(new DsParamConfig {
                name = "emotion", abbr = "emo", category = "variance",
                min = -1, max = 1, defaultValue = 0,
            });
            var emotion = registry.ByName("emotion")!;

            Assert.Equal(0f, emotion.Normalize(-1f), 5);
            Assert.Equal(0.5f, emotion.Normalize(0f), 5);
            Assert.Equal(1f, emotion.Normalize(1f), 5);
            Assert.Equal(1f, emotion.Normalize(4f), 5);
        }

        [Fact]
        public void OverridingAnAbbrInheritsTheFieldsLeftOut() {
            var config = new DsConfig {
                useEnergyEmbed = true,
                paramDefinitions = new DsParamsConfig {
                    definitions = new List<DsParamConfig> {
                        new DsParamConfig { abbr = DiffSingerUtils.ENE, displayName = "energy offset" },
                    },
                },
            };

            var energy = Assert.Single(DsParamRegistry.From(config).All);

            Assert.Equal("energy", energy.Name);
            Assert.Equal(DiffSingerUtils.ENE, energy.Abbr);
            Assert.Equal(DsParamCategory.Variance, energy.Category);
            Assert.Equal(DsParamMode.Add, energy.Mode);
            // The built-in curve definition, value range and scale are inherited.
            Assert.Equal(-100f, energy.Descriptor.min);
            Assert.Equal(100f, energy.Descriptor.max);
            Assert.Equal(-96f, energy.ValueMin);
            Assert.Equal(0f, energy.ValueMax);
            Assert.Equal(0.12f, energy.Scale, 5);
            Assert.Equal("energy offset", energy.Descriptor.name);
        }

        [Fact]
        public void ReusingAnAbbrOverridesTheParameterInPlace() {
            var config = new DsConfig {
                useEnergyEmbed = true,
                useBreathinessEmbed = true,
                paramDefinitions = new DsParamsConfig {
                    definitions = new List<DsParamConfig> {
                        new DsParamConfig {
                            name = "energy", abbr = DiffSingerUtils.ENE, category = "variance",
                            mode = "mul", min = 0, max = 2, defaultValue = 1, scale = 1,
                        },
                    },
                },
            };

            var registry = DsParamRegistry.From(config);

            Assert.Equal(new[] { "energy", "breathiness" }, registry.Variance.Select(p => p.Name));
            var energy = registry.ByName("energy")!;
            Assert.False(energy.IsBuiltIn);
            Assert.Equal(DsParamMode.Multiply, energy.Mode);
            Assert.Equal(2f, energy.Descriptor.max);
        }

        [Fact]
        public void ReusingANameRebindsItToTheNewAbbr() {
            var config = new DsConfig {
                useEnergyEmbed = true,
                paramDefinitions = new DsParamsConfig {
                    definitions = new List<DsParamConfig> {
                        new DsParamConfig { name = "energy", abbr = "eng2", min = -1, max = 1 },
                    },
                },
            };

            var registry = DsParamRegistry.From(config);

            var energy = Assert.Single(registry.All);
            Assert.Equal("eng2", energy.Abbr);
            Assert.Equal("energy", energy.Name);
        }

        [Fact]
        public void CustomCurvesAreKnownAbbrsOnceAVoicebankDeclaredThem() {
            var abbr = "zz" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Assert.False(DsParamRegistry.IsKnownAbbr(abbr));

            Registry(new DsParamConfig { name = "custom", abbr = abbr, min = 0, max = 1 });

            Assert.True(DsParamRegistry.IsKnownAbbr(abbr));
        }

        [Fact]
        public void MissingNameIsRejected() {
            var e = Assert.Throws<InvalidDataException>(() => Registry(
                new DsParamConfig { abbr = "custom", min = 0, max = 1 }));
            Assert.Contains("needs a name", e.Message);
        }

        [Fact]
        public void MissingAbbrIsRejected() {
            var e = Assert.Throws<InvalidDataException>(() => Registry(
                new DsParamConfig { name = "custom", min = 0, max = 1 }));
            Assert.Contains("needs an abbr", e.Message);
        }

        [Fact]
        public void AbbrMustBeAnIdentifier() {
            var e = Assert.Throws<InvalidDataException>(() => Registry(
                new DsParamConfig { name = "custom", abbr = "1bad", min = 0, max = 1 }));
            Assert.Contains("letters, digits or _", e.Message);
        }

        [Fact]
        public void EmptyRangeIsRejected() {
            var e = Assert.Throws<InvalidDataException>(() => Registry(
                new DsParamConfig { name = "custom", abbr = "ok", min = 0, max = 0 }));
            Assert.Contains("min must be below max", e.Message);
        }

        [Fact]
        public void UnknownModeIsRejected() {
            var e = Assert.Throws<InvalidDataException>(() => Registry(
                new DsParamConfig { name = "custom", abbr = "ok", mode = "scale", min = 0, max = 1 }));
            Assert.Contains("neither add, mul nor gain", e.Message);
        }

        [Fact]
        public void UnknownCategoryIsRejected() {
            var e = Assert.Throws<InvalidDataException>(() => Registry(
                new DsParamConfig { name = "custom", abbr = "ok", category = "pitch", min = 0, max = 1 }));
            Assert.Contains("neither acoustic nor variance", e.Message);
        }

        [Fact]
        public void ParamsFileBesideDsconfigIsLoaded() {
            string dir = Path.Combine(Path.GetTempPath(), "ou-params-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                File.WriteAllText(Path.Combine(dir, "dsconfig.yaml"),
                    "acoustic: model.onnx\nuse_emotion_embed: true\npredict_emotion: true\n");
                File.WriteAllText(Path.Combine(dir, "params.yaml"), @"
params:
  - name: emotion
    abbr: emo
    category: variance
    min: -1
    max: 1
    default: 0
");
                var config = DsConfig.Load(dir);

                Assert.True(config.UsesEmbed("emotion"));
                Assert.True(config.Predicts("emotion"));
                var emotion = Assert.Single(DsParamRegistry.From(config).All);
                Assert.Equal("emotion", emotion.Name);
                Assert.Equal("emo", emotion.Abbr);
                Assert.Equal(-1f, emotion.Descriptor.min);
                Assert.Equal(1f, emotion.Descriptor.max);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MissingParamsFileMeansNoCustomParameters() {
            string dir = Path.Combine(Path.GetTempPath(), "ou-params-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                File.WriteAllText(Path.Combine(dir, "dsconfig.yaml"), "acoustic: model.onnx\n");

                var config = DsConfig.Load(dir);

                Assert.Null(config.paramDefinitions);
                Assert.Empty(DsParamRegistry.From(config).All);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void DefinitionWithoutTheFlagIsIgnored() {
            var config = new DsConfig {
                paramDefinitions = new DsParamsConfig {
                    definitions = new List<DsParamConfig> {
                        new DsParamConfig { name = "emotion", abbr = "emo", min = -1, max = 1 },
                    },
                },
            };

            Assert.Empty(DsParamRegistry.From(config).All);
        }

        [Fact]
        public void FlagWithoutDefinitionIsNotAParameter() {
            var config = new DsConfig();
            config.dynamicFlags["use_emotion_embed"] = true;

            Assert.Empty(DsParamRegistry.From(config).All);
        }

        [Fact]
        public void DsconfigAcceptsCustomEmbedFlags() {
            var diagnostics = YamlValidator.Validate<DsConfig>(@"
acoustic: model.onnx
use_emotion_embed: true
predict_emotion: true
");
            Assert.Empty(diagnostics);

            var typo = YamlValidator.Validate<DsConfig>(@"
acoustic: model.onnx
use_emotion_embed: yes please
");
            Assert.Contains(typo, d => d.Kind == YamlDiagnosticKind.WrongType);
        }

        [Fact]
        public void ParamsFileValidatesAgainstItsType() {
            var diagnostics = YamlValidator.Validate<DsParamsConfig>(@"
params:
  - name: emotion
    abbr: emo
    category: variance
    mode: mul
    min: -1
    max: 1
    default: 0
");
            Assert.Empty(diagnostics);

            var unknown = YamlValidator.Validate<DsParamsConfig>(@"
params:
  - name: emotion
    abbr: emo
    typo_key: 1
");
            Assert.Contains(unknown, d => d.Kind == YamlDiagnosticKind.UnknownKey && d.Detail == "typo_key");
        }

        static DsParamRegistry Registry(params DsParamConfig[] definitions) {
            var config = new DsConfig {
                paramDefinitions = new DsParamsConfig { definitions = definitions.ToList() },
            };
            // params.yaml alone does not enable a parameter: dsconfig.yaml has the flag.
            foreach (var definition in definitions) {
                if (!string.IsNullOrWhiteSpace(definition.name)) {
                    config.dynamicFlags["use_" + definition.name.Trim() + "_embed"] = true;
                }
            }
            return DsParamRegistry.From(config);
        }
    }
}
