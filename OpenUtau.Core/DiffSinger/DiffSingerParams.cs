using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// The two kinds of tunable parameter a DiffSinger voicebank can declare.
    /// <see cref="Acoustic"/> feeds the acoustic model only: the drawn curve is the value.
    /// <see cref="Variance"/> is predicted by the variance model (its <c>&lt;name&gt;_pred</c>
    /// output), and the drawn curve is added to or multiplied with that prediction.
    /// </summary>
    public enum DsParamCategory {
        Acoustic = 0,
        Variance = 1,
    }

    /// <summary>How the drawn curve of a variance parameter combines with the prediction.</summary>
    public enum DsParamMode {
        Add = 0,
        Multiply = 1,
        /// <summary>
        /// The curve is a linear gain on a parameter whose value is in dB: the gain is
        /// turned into decibels, so 100 leaves the prediction alone, 50 halves it and
        /// 0 silences it.
        /// </summary>
        Gain = 2,
    }

    /// <summary>Suggested values of <see cref="DsParamConfig.category"/>, shown while editing params.yaml.</summary>
    public class DsParamCategoryValues : IYamlValueSource {
        public IEnumerable<YamlValue> GetValues() => new[] {
            new YamlValue("acoustic", "Only the acoustic model takes <name>; the curve is the value."),
            new YamlValue("variance", "The variance model predicts <name>_pred; the curve is combined with it."),
        };
    }

    /// <summary>Suggested values of <see cref="DsParamConfig.mode"/>, shown while editing params.yaml.</summary>
    public class DsParamModeValues : IYamlValueSource {
        public IEnumerable<YamlValue> GetValues() => new[] {
            new YamlValue("add", "predicted + curve, e.g. an energy offset."),
            new YamlValue("mul", "predicted * curve, for a parameter whose value is a linear quantity."),
            new YamlValue("gain", "predicted + 20*log10(curve), for a parameter whose value is in dB (energy, breathiness, voicing)."),
        };
    }

    /// <summary>
    /// Root of params.yaml, the optional file beside dsconfig.yaml. It declares the
    /// tunable parameters the voicebank's models take beyond the ones OpenUtau knows
    /// by itself; a parameter also needs its use_&lt;name&gt;_embed flag in dsconfig.yaml.
    /// </summary>
    [Serializable]
    public class DsParamsConfig {
        [Description("Tunable parameters. Each entry becomes one curve, and one input of the model.")]
        [YamlMember(Alias = "params")]
        public List<DsParamConfig> definitions = new List<DsParamConfig>();
    }

    /// <summary>One tunable parameter: its ONNX name, its curve, and how the two combine.</summary>
    [Serializable]
    public class DsParamConfig {
        [Description("Input name of the model, e.g. emotion. A variance parameter also needs the variance model to output <name>_pred.")]
        public string name;

        [Description("Abbreviation of the curve, e.g. emo. Reusing an existing abbreviation overrides that parameter, keeps the curve already drawn in projects, and inherits the fields left out here.")]
        public string abbr;

        [Description("acoustic: only the acoustic model takes the parameter. variance: the variance model predicts it and the curve is combined with the prediction.")]
        [YamlValues(typeof(DsParamCategoryValues))]
        public string? category;

        [Description("add: predicted + curve. mul: predicted * curve. gain: predicted + 20*log10(curve), for dB parameters. Variance parameters only.")]
        [YamlValues(typeof(DsParamModeValues))]
        public string? mode;

        [Description("Lowest value the curve can be drawn at. Required for a new parameter.")]
        public float? min;

        [Description("Highest value the curve can be drawn at. Required for a new parameter.")]
        public float? max;

        [Description("Value of the curve where it is not drawn, and the value that changes nothing: 0 for an offset, 100 for a delta measured from 100, 1/scale for mul.")]
        [YamlMember(Alias = "default")]
        public float? defaultValue;

        [Description("Curve units per parameter unit, in the style of the built-in parameters: a curve of 100 and a scale of 0.12 give 12. Defaults to 1.")]
        public float? scale;

        [Description("Lowest value of the parameter itself, used to clamp and to scale the real curve display. Defaults to min. Variance parameters only.")]
        [YamlMember(Alias = "value_min")]
        public float? valueMin;

        [Description("Highest value of the parameter itself. Defaults to max. Variance parameters only.")]
        [YamlMember(Alias = "value_max")]
        public float? valueMax;

        [Description("Name shown in the piano roll's expression list. Defaults to name.")]
        [YamlMember(Alias = "display_name")]
        public string displayName;

        [Description("Vertical scale of the real curve in its lane: 1 fills the lane from value_min to value_max, 0.5 uses the lower half. Defaults to 1.")]
        [YamlMember(Alias = "display_scale")]
        public float? displayScale;
    }

    /// <summary>
    /// A tunable parameter resolved from dsconfig.yaml and params.yaml: the model
    /// input it drives, the curve that drives it, and the conversion between them.
    /// </summary>
    public sealed class DsParam {
        public string Name { get; }
        public string Abbr { get; }
        public DsParamCategory Category { get; }
        public DsParamMode Mode { get; }
        public UExpressionDescriptor Descriptor { get; }
        /// <summary>False for the parameters OpenUtau knows by itself, e.g. energy.</summary>
        public bool IsBuiltIn { get; }
        /// <summary>Range of the parameter value itself: clamping and the real curve display.</summary>
        public float ValueMin { get; }
        public float ValueMax { get; }
        /// <summary>
        /// Curve units per parameter unit, in the style of the built-in parameters:
        /// a curve of 100 and a scale of 0.12 give 12. Built-in parameters with a more
        /// involved conversion (gender, velocity) ignore it.
        /// </summary>
        public float Scale { get; }
        /// <summary>
        /// How much of its lane the real curve fills: 1 spans value_min to value_max,
        /// 0.5 the lower half. Parameters whose values look big or small next to the
        /// others can be toned down without changing what the model gets.
        /// </summary>
        public float DisplayScale { get; }

        readonly Func<RenderPhrase, float[]?> curveOf;
        readonly Func<float, float> fromCurve;
        readonly Func<float, float, float> combine;

        internal DsParam(
            string name, string abbr, DsParamCategory category, UExpressionDescriptor descriptor,
            float valueMin, float valueMax, float scale, bool isBuiltIn, Func<RenderPhrase, float[]?> curveOf,
            Func<float, float> fromCurve, Func<float, float, float> combine, DsParamMode mode = DsParamMode.Add,
            float displayScale = 1f) {
            Name = name;
            Abbr = abbr;
            Category = category;
            Descriptor = descriptor;
            ValueMin = valueMin;
            ValueMax = valueMax;
            Scale = scale;
            DisplayScale = displayScale;
            IsBuiltIn = isBuiltIn;
            Mode = mode;
            this.curveOf = curveOf;
            this.fromCurve = fromCurve;
            this.combine = combine;
        }

        /// <summary>
        /// The drawn curve of this parameter in a phrase, or null when it was never
        /// drawn. The built-in parameters come from their own fields of the phrase;
        /// the others are looked up by abbreviation.
        /// </summary>
        public float[]? CurveOf(RenderPhrase phrase) => curveOf(phrase);

        /// <summary>The acoustic input of an acoustic-only parameter.</summary>
        public float AcousticValue(float curve) => fromCurve(curve);

        /// <summary>
        /// The curve value used where no curve is drawn: the curve's own default, which
        /// is the value that changes nothing (0 for an offset, 100 for voicing, 1/scale
        /// for a multiplier).
        /// </summary>
        public float NeutralCurve => Descriptor.defaultValue;

        /// <summary>The acoustic input of a variance parameter, from its prediction and the curve.</summary>
        public float CombinedValue(float predicted, float curve) =>
            Math.Clamp(combine(predicted, curve), ValueMin, ValueMax);

        /// <summary>A parameter value as the 0 to 1 the piano roll draws real curves in.</summary>
        public float Normalize(float value) {
            if (ValueMax <= ValueMin) {
                return 0f;
            }
            return Math.Clamp((Math.Clamp(value, ValueMin, ValueMax) - ValueMin) / (ValueMax - ValueMin) * DisplayScale, 0f, 1f);
        }
    }

    /// <summary>
    /// The tunable parameters of one voicebank, in the order the models take them.
    /// Variance parameters come first, in the order of the variance model's heads
    /// (the <c>predicted</c> tensors and the retake mask are positional), then the
    /// acoustic-only ones.
    /// </summary>
    public sealed class DsParamRegistry {
        // Curve abbreviations any loaded voicebank declared. SupportsExpression has no
        // voicebank to ask, so it recognizes custom curves once a voicebank that
        // declares them has been loaded, the same way voice color curves are accepted.
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> knownAbbrs =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether any loaded voicebank declared a curve with this abbreviation.</summary>
        public static bool IsKnownAbbr(string abbr) => knownAbbrs.ContainsKey(abbr);

        public IReadOnlyList<DsParam> All { get; }
        public IReadOnlyList<DsParam> Acoustic { get; }
        public IReadOnlyList<DsParam> Variance { get; }

        DsParamRegistry(List<DsParam> all) {
            All = all;
            Acoustic = all.Where(p => p.Category == DsParamCategory.Acoustic).ToArray();
            Variance = all.Where(p => p.Category == DsParamCategory.Variance).ToArray();
            foreach (var param in all) {
                knownAbbrs.TryAdd(param.Abbr, 0);
            }
        }

        public DsParam? ByAbbr(string abbr) =>
            All.FirstOrDefault(p => string.Equals(p.Abbr, abbr, StringComparison.OrdinalIgnoreCase));

        public DsParam? ByName(string name) =>
            All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public static DsParamRegistry Empty { get; } = new DsParamRegistry(new List<DsParam>());

        /// <summary>
        /// The parameters of <paramref name="config"/>: the built-in ones its flags
        /// enable, then the ones params.yaml declares (overriding by abbreviation
        /// first, then by name).
        /// </summary>
        public static DsParamRegistry From(DsConfig config, bool includeDefinitions = true) {
            var all = new List<DsParam>();
            AddBuiltIns(config, all);
            if (includeDefinitions && config.paramDefinitions?.definitions != null) {
                foreach (var definition in config.paramDefinitions.definitions) {
                    ApplyDefinition(definition, config, all);
                }
            }
            WarnAboutEnabledWithoutDefinition(config, all);
            return new DsParamRegistry(all);
        }

        /// <summary>
        /// A flag alone is not enough: without a params.yaml entry OpenUtau cannot know
        /// the curve or the input's range, so the model would be asked for an input
        /// nothing feeds. Say so at load time rather than mid-render.
        /// </summary>
        static void WarnAboutEnabledWithoutDefinition(DsConfig config, List<DsParam> all) {
            foreach (var (key, enabled) in config.dynamicFlags) {
                if (!enabled || !key.StartsWith("use_") || !key.EndsWith("_embed")) {
                    continue;
                }
                string name = key.Substring("use_".Length, key.Length - "use_".Length - "_embed".Length);
                if (all.All(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) {
                    Log.Warning($"dsconfig.yaml sets {key}, but params.yaml defines no \"{name}\" parameter.");
                }
            }
        }

        /// <summary>Resolves predictor heads independently of acoustic embedding flags.</summary>
        public static IReadOnlyList<DsParam> Predicted(DsConfig config, DsParamRegistry registry) {
            var builtIns = new List<DsParam>();
            AddBuiltIns(config, builtIns, prediction: true);
            var names = builtIns.Where(p => p.Category == DsParamCategory.Variance).Select(p => p.Name)
                .Concat(registry.Variance.Where(p => config.Predicts(p.Name)).Select(p => p.Name))
                .Concat(config.dynamicFlags.Where(p => p.Value && p.Key.StartsWith("predict_")
                    && p.Key != "predict_dur").Select(p => p.Key.Substring("predict_".Length)))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return names.Select(name => registry.ByName(name)
                ?? builtIns.FirstOrDefault(p => p.Name == name)
                ?? throw new InvalidDataException($"The variance dsconfig.yaml predicts \"{name}\", but params.yaml defines no such parameter."))
                .ToArray();
        }

        /// <summary>The built-in parameters, ordered as the variance model's heads are.</summary>
        static void AddBuiltIns(DsConfig config, List<DsParam> all, bool prediction = false) {
            // Variance parameters, in the order DiffSinger's heads come in. Their curves
            // are graded the way the built-in ones always were: 100 curve units are 12
            // parameter units (energy, breathiness, voicing) or 5 (tension).
            if (prediction ? config.Predicts("energy") : config.UsesEmbed("energy")) {
                all.Add(MakeVariance("energy", DiffSingerUtils.ENE, "energy (curve)", -100, 100, 0,
                    0.12f, -96, 0,
                    phrase => phrase.curves.FirstOrDefault(c => c.Item1 == DiffSingerUtils.ENE)?.Item2,
                    isBuiltIn: true));
            }
            if (prediction ? config.Predicts("breathiness") : config.UsesEmbed("breathiness")) {
                all.Add(MakeVariance("breathiness", Format.Ustx.BREC, "breathiness (curve)", -100, 100, 0,
                    0.12f, -96, 0,
                    phrase => phrase.breathiness,
                    isBuiltIn: true));
            }
            if (prediction ? config.Predicts("voicing") : config.UsesEmbed("voicing")) {
                all.Add(MakeVariance("voicing", Format.Ustx.VOIC, "voicing (curve)", 0, 100, 100,
                    0.12f, -96, 0,
                    phrase => phrase.voicing,
                    isBuiltIn: true));
            }
            if (prediction ? config.Predicts("tension") : config.UsesEmbed("tension")) {
                all.Add(MakeVariance("tension", Format.Ustx.TENC, "tension (curve)", -100, 100, 0,
                    0.05f, -10, 10,
                    phrase => phrase.tension,
                    isBuiltIn: true));
            }
            if (prediction) {
                return;
            }
            // Acoustic-only parameters. Their conversions are not a single scale:
            // gender is asymmetric and velocity is logarithmic.
            if (config.UsesEmbed("gender")) {
                var (positiveScale, negativeScale) = KeyShiftScales(config);
                all.Add(MakeAcousticConverted("gender", Format.Ustx.GENC, "gender (curve)", -100, 100, 0,
                    phrase => phrase.gender,
                    curve => curve < 0 ? -curve * negativeScale : -curve * positiveScale));
            }
            if (config.UsesEmbed("velocity")) {
                all.Add(MakeAcousticConverted("velocity", DiffSingerUtils.VELC, "velocity (curve)", 0, 200, 100,
                    phrase => phrase.curves.FirstOrDefault(c => c.Item1 == DiffSingerUtils.VELC)?.Item2,
                    curve => (float)Math.Pow(2, (curve - 100) / 100)));
            }
        }

        // gender: 100 = 12 semitones of shift down, but only up to the range the model
        // was trained with, so the two directions have their own scale.
        static (float positive, float negative) KeyShiftScales(DsConfig config) {
            var range = config.augmentationArgs?.randomPitchShifting?.range;
            if (range == null || range.Length < 2) {
                Log.Warning("augmentationArgs.randomPitchShifting.range is missing but useKeyShiftEmbed is set; gender will not shift.");
                return (0, 0);
            }
            float positive = range[1] == 0 ? 0 : (12f / range[1] / 100f);
            float negative = range[0] == 0 ? 0 : (-12f / range[0] / 100f);
            return (positive, negative);
        }

        /// <summary>
        /// A variance parameter: the prediction is combined with the curve, which is
        /// graded in curve units and scaled into the parameter's units.
        /// </summary>
        static DsParam MakeVariance(
            string name, string abbr, string displayName, float min, float max, float defaultValue,
            float scale, float valueMin, float valueMax,
            Func<RenderPhrase, float[]?> curveOf, bool isBuiltIn,
            DsParamMode mode = DsParamMode.Add, float displayScale = 1f) {
            if (mode == DsParamMode.Gain && (min * scale < 0 || max * scale < 0)) {
                throw new InvalidDataException($"params.yaml: gain range for \"{name}\" must not contain negative scaled values.");
            }
            var descriptor = Curve(displayName, abbr, min, max, defaultValue);
            Func<float, float, float> combine = mode switch {
                DsParamMode.Multiply => (predicted, curve) => predicted * curve * scale,
                // A dB parameter: the curve is a linear gain, so halving it takes 6 dB off.
                DsParamMode.Gain => (predicted, curve) => predicted + 20f * (float)Math.Log10(curve * scale),
                _ => (predicted, curve) => predicted + (curve - descriptor.defaultValue) * scale,
            };
            return new DsParam(name, abbr, DsParamCategory.Variance, descriptor, valueMin, valueMax,
                scale, isBuiltIn, curveOf, curve => curve, combine, mode, displayScale);
        }

        /// <summary>An acoustic-only parameter whose input is its curve, scaled.</summary>
        static DsParam MakeAcoustic(
            string name, string abbr, string displayName, float min, float max, float defaultValue,
            float scale, Func<RenderPhrase, float[]?> curveOf, bool isBuiltIn,
            Func<float, float>? fromCurve = null) {
            return new DsParam(name, abbr, DsParamCategory.Acoustic,
                Curve(displayName, abbr, min, max, defaultValue), min, max, scale, isBuiltIn,
                curveOf, fromCurve ?? (curve => curve * scale), (predicted, curve) => predicted);
        }

        /// <summary>An acoustic-only parameter with a conversion of its own, e.g. velocity.</summary>
        static DsParam MakeAcousticConverted(
            string name, string abbr, string displayName, float min, float max, float defaultValue,
            Func<RenderPhrase, float[]?> curveOf, Func<float, float> fromCurve) {
            return new DsParam(name, abbr, DsParamCategory.Acoustic,
                Curve(displayName, abbr, min, max, defaultValue), min, max, 1f, true,
                curveOf, fromCurve, (predicted, curve) => predicted);
        }

        static UExpressionDescriptor Curve(string displayName, string abbr, float min, float max, float defaultValue) =>
            new UExpressionDescriptor(displayName, abbr, min, max, defaultValue) { type = UExpressionType.Curve };

        /// <summary>
        /// Adds a params.yaml entry. Reusing an abbreviation overrides that parameter
        /// where it stands, so the curve drawn in projects still applies and the fields
        /// left out here are inherited. A new abbreviation that reuses an existing name
        /// rebinds that model input and replaces the parameter in place.
        /// </summary>
        static void ApplyDefinition(DsParamConfig definition, DsConfig config, List<DsParam> all) {
            if (string.IsNullOrWhiteSpace(definition.abbr)) {
                throw new InvalidDataException(
                    $"params.yaml: every parameter needs an abbr{(string.IsNullOrWhiteSpace(definition.name) ? "" : $" (\"{definition.name}\")")}.");
            }
            string abbr = definition.abbr.Trim().ToLowerInvariant();
            var byAbbr = all.FirstOrDefault(p => string.Equals(p.Abbr, abbr, StringComparison.OrdinalIgnoreCase));
            var byName = all.FirstOrDefault(p => !string.IsNullOrWhiteSpace(definition.name)
                && string.Equals(p.Name, definition.name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byAbbr != null && byName != null && byAbbr != byName) {
                throw new InvalidDataException($"params.yaml: name \"{definition.name}\" and abbr \"{abbr}\" identify different parameters.");
            }
            DsParam? inherited = byAbbr ?? byName;
            string? name = string.IsNullOrWhiteSpace(definition.name) ? inherited?.Name : definition.name.Trim();
            if (name != null && !config.UsesEmbed(name)) {
                // dsconfig.yaml has the last word: a definition alone does not make the
                // model take the parameter.
                Log.Information($"params.yaml declares \"{name}\", but dsconfig.yaml does not enable use_{name}_embed.");
                return;
            }
            var param = Build(definition, inherited, abbr);
            int index = inherited == null ? -1 : all.IndexOf(inherited);
            if (index >= 0) {
                if (!string.Equals(inherited!.Name, param.Name, StringComparison.OrdinalIgnoreCase)) {
                    Log.Warning(
                        $"params.yaml: \"{inherited.Name}\" was bound to curve \"{inherited.Abbr}\", now to \"{param.Abbr}\" ({param.Name}).");
                }
                all[index] = param;
                return;
            }
            all.Add(param);
        }

        static DsParam Build(DsParamConfig definition, DsParam? inherited, string abbr) {
            string? rawName = string.IsNullOrWhiteSpace(definition.name) ? inherited?.Name : definition.name.Trim();
            if (rawName == null) {
                throw new InvalidDataException(
                    $"params.yaml: the parameter with curve \"{abbr}\" needs a name, or an existing curve to inherit it from.");
            }
            string name = rawName;
            string label = $"parameter \"{name}\"";
            if (name.Contains(' ') || name.Contains('\t')) {
                throw new InvalidDataException($"params.yaml: the name of {label} must not contain spaces.");
            }
            if (!abbr.All(c => char.IsLetterOrDigit(c) || c == '_') || !char.IsLetter(abbr[0])) {
                throw new InvalidDataException(
                    $"params.yaml: the abbr \"{definition.abbr}\" of {label} must be letters, digits or _, starting with a letter.");
            }
            float? min = definition.min ?? inherited?.Descriptor.min;
            float? max = definition.max ?? inherited?.Descriptor.max;
            if (min == null || max == null) {
                throw new InvalidDataException($"params.yaml: min and max are required for {label}.");
            }
            if (min >= max) {
                throw new InvalidDataException($"params.yaml: min must be below max for {label}.");
            }
            var category = definition.category == null
                ? inherited?.Category ?? DsParamCategory.Acoustic
                : ParseCategory(definition.category, label);
            var mode = definition.mode == null
                ? inherited?.Mode ?? DsParamMode.Add
                : ParseMode(definition.mode, label);
            float defaultValue = definition.defaultValue
                ?? inherited?.Descriptor.defaultValue
                ?? (mode == DsParamMode.Multiply ? 1f : 0f);
            if (defaultValue < min || defaultValue > max) {
                throw new InvalidDataException($"params.yaml: default must be between min and max for {label}.");
            }
            float scale = definition.scale ?? inherited?.Scale ?? 1f;
            if (scale == 0 || float.IsNaN(scale) || float.IsInfinity(scale)) {
                throw new InvalidDataException($"params.yaml: scale of {label} must be a finite non-zero number.");
            }
            if (mode != DsParamMode.Add && Math.Abs(defaultValue * scale - 1f) > 1e-3f) {
                throw new InvalidDataException(
                    $"params.yaml: for {mode.ToString().ToLowerInvariant()}, default * scale must be 1 for {label}, so that an undrawn curve leaves the prediction unchanged " +
                    $"(default {defaultValue} * scale {scale} = {defaultValue * scale}).");
            }
            float valueMin = definition.valueMin ?? inherited?.ValueMin ?? min.Value;
            float valueMax = definition.valueMax ?? inherited?.ValueMax ?? max.Value;
            if (valueMin >= valueMax) {
                throw new InvalidDataException($"params.yaml: value_min must be below value_max for {label}.");
            }
            string displayName = !string.IsNullOrWhiteSpace(definition.displayName) ? definition.displayName.Trim()
                : inherited?.Descriptor.name ?? name;
            // Some built-in parameters do not live in the phrase's curve list: their
            // drawn curve is folded into a field of the phrase (voicing, breathiness,
            // tension, gender). An override keeps the same abbreviation, so it has to
            // keep reading the same curve; a rebinding to one of those abbreviations has
            // to read the field of the abbreviation it ends up with.
            Func<RenderPhrase, float[]?> curveOf =
                inherited?.Abbr is string inheritedAbbr && string.Equals(inheritedAbbr, abbr, StringComparison.OrdinalIgnoreCase)
                    ? inherited.CurveOf
                    : CurveAccessor(abbr);
            float displayScale = definition.displayScale ?? inherited?.DisplayScale ?? 1f;
            if (displayScale <= 0 || float.IsNaN(displayScale) || float.IsInfinity(displayScale)) {
                throw new InvalidDataException($"params.yaml: display_scale of {label} must be a positive number.");
            }
            if (category == DsParamCategory.Acoustic) {
                return MakeAcoustic(name, abbr, displayName, min.Value, max.Value, defaultValue, scale, curveOf, isBuiltIn: false,
                    fromCurve: inherited?.Category == DsParamCategory.Acoustic
                        && definition.scale == null && definition.mode == null
                        ? inherited.AcousticValue : null);
            }
            return MakeVariance(name, abbr, displayName, min.Value, max.Value, defaultValue, scale,
                valueMin, valueMax, curveOf, isBuiltIn: false, mode, displayScale);
        }

        /// <summary>
        /// Where a curve lives in a phrase. Most curves are in the phrase's curve list,
        /// but the built-in ones whose parameter is graded in dB are folded into a field
        /// of the phrase instead, so their abbreviation has to map to that field.
        /// </summary>
        static Func<RenderPhrase, float[]?> CurveAccessor(string abbr) => abbr switch {
            Format.Ustx.BREC => phrase => phrase.hasBreathinessCurve ? phrase.breathiness : null,
            Format.Ustx.VOIC => phrase => phrase.voicing,
            Format.Ustx.TENC => phrase => phrase.tension,
            Format.Ustx.GENC => phrase => phrase.gender,
            _ => phrase => phrase.curves.FirstOrDefault(c => c.Item1 == abbr)?.Item2,
        };

        static DsParamCategory ParseCategory(string? value, string label) {
            switch ((value ?? "").Trim().ToLowerInvariant()) {
                case "":
                case "acoustic":
                    return DsParamCategory.Acoustic;
                case "variance":
                    return DsParamCategory.Variance;
                default:
                    throw new InvalidDataException(
                        $"params.yaml: category \"{value}\" of {label} is neither acoustic nor variance.");
            }
        }

        static DsParamMode ParseMode(string? value, string label) {
            switch ((value ?? "").Trim().ToLowerInvariant()) {
                case "":
                case "add":
                    return DsParamMode.Add;
                case "mul":
                    return DsParamMode.Multiply;
                case "gain":
                    return DsParamMode.Gain;
                default:
                    throw new InvalidDataException(
                        $"params.yaml: mode \"{value}\" of {label} is neither add, mul nor gain.");
            }
        }
    }
}
