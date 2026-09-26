using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>What a node computes from, at each tick of the grid.</summary>
    public readonly struct NodeArgs {
        public readonly float[][] Inputs;
        public readonly CompiledNode Node;
        public readonly GraphContext Context;
        /// <summary>Part-relative ticks.</summary>
        public readonly int[] Ticks;
        /// <summary>When the grid is the phonemes' own positions: the phoneme at each tick.</summary>
        public readonly int[]? PhonemeIndices;

        public NodeArgs(float[][] inputs, CompiledNode node, GraphContext context, int[] ticks, int[]? phonemeIndices = null) {
            Inputs = inputs;
            Node = node;
            Context = context;
            Ticks = ticks;
            PhonemeIndices = phonemeIndices;
        }
    }

    public enum GraphNodeRole { Input, Process, CurveOutput, PhonemeOutput, PitchOutput }

    public enum GraphParameterKind { Number, Bool, Choice, Text, Expression }

    /// <summary>A node parameter the editor shows. Input ports' fallback values are not listed here.</summary>
    public sealed class GraphNodeParameter {
        public readonly string Name;
        public readonly GraphParameterKind Kind;
        /// <summary>The value a new node gets; null leaves it unset.</summary>
        public readonly string? Default;
        public readonly string[] Options;
        /// <summary>Whether the editor labels it. Unlabeled ones are named by their node's title.</summary>
        public bool Labeled { get; init; } = true;

        public GraphNodeParameter(string name, GraphParameterKind kind, string? @default = null, params string[] options) {
            Name = name;
            Kind = kind;
            Default = @default;
            Options = options;
        }
    }

    /// <summary>
    /// A node type. An unconnected input port takes the node's parameter of the same name, else the port's default.
    /// </summary>
    public sealed class GraphNodeType {
        /// <summary>The output of nodes with one; a link from it may leave its port unnamed.</summary>
        public const string Out = "out";

        public readonly string Name;
        public readonly GraphNodeRole Role;
        public readonly string[] Ports;
        public readonly float[] PortDefaults;
        /// <summary>Output ports; output nodes have none.</summary>
        public readonly string[] Outputs;
        readonly Func<NodeArgs, float[][]> evaluate;

        public GraphNodeType(string name, GraphNodeRole role, string[] ports, float[] portDefaults, Func<NodeArgs, float[]> evaluate)
            : this(name, role, ports, portDefaults,
                role is GraphNodeRole.CurveOutput or GraphNodeRole.PhonemeOutput or GraphNodeRole.PitchOutput
                    ? Array.Empty<string>() : new[] { Out },
                a => new[] { evaluate(a) }) { }

        public GraphNodeType(string name, GraphNodeRole role, string[] ports, float[] portDefaults, string[] outputs,
                Func<NodeArgs, float[][]> evaluate) {
            Name = name;
            Role = role;
            Ports = ports;
            PortDefaults = portDefaults;
            Outputs = outputs;
            this.evaluate = evaluate;
        }

        /// <summary>The index of an output port; an unnamed one is the first.</summary>
        public int OutputIndex(string? port) => port == null ? (Outputs.Length > 0 ? 0 : -1) : Array.IndexOf(Outputs, port);

        public bool IsOutput => Role is GraphNodeRole.CurveOutput or GraphNodeRole.PhonemeOutput or GraphNodeRole.PitchOutput;

        /// <summary>Reads or drives an expression, named by the "abbr" parameter.</summary>
        public bool NeedsAbbr => Role is GraphNodeRole.CurveOutput or GraphNodeRole.PhonemeOutput
            || Name is GraphNodeTypes.CurveInput or GraphNodeTypes.PhonemeInput or GraphNodeTypes.MaskedCurveInput;

        /// <summary>One array per output; output nodes return the one value they output.</summary>
        internal float[][] Evaluate(NodeArgs args) => evaluate(args);
    }

    public static class GraphNodeTypes {
        public const string Constant = "constant";
        public const string CurveInput = "curve_input";
        public const string CurveOutput = "curve_output";
        public const string MaskedCurveInput = "masked_curve_input";
        public const string PhonemeInput = "phoneme_input";
        public const string PhonemeOutput = "phoneme_output";
        public const string PitchInput = "pitch_input";
        public const string PitchOutput = "pitch_output";
        public const string Add = "add";
        public const string Subtract = "subtract";
        public const string Multiply = "multiply";
        public const string Divide = "divide";
        public const string Min = "min";
        public const string Max = "max";
        public const string Mix = "mix";
        public const string Abs = "abs";
        public const string MapRange = "map_range";
        public const string Clamp = "clamp";
        public const string Lfo = "lfo";
        public const string NotePosition = "note_position";
        public const string NoteEnvelope = "note_envelope";

        /// <summary>The port of single-input nodes.</summary>
        public const string Value = "value";

        static readonly string[] none = Array.Empty<string>();
        static readonly string[] value = { Value };
        static readonly string[] ab = { "a", "b" };

        static readonly Dictionary<string, GraphNodeType> types = new[] {
            new GraphNodeType(Constant, GraphNodeRole.Input, none, new float[0],
                a => Fill(a, a.Node.GetFloat("value", 0))),
            new GraphNodeType(CurveInput, GraphNodeRole.Input, none, new float[0],
                a => Map(a, tick => a.Context.SampleCurve(a.Node.GetString("abbr"), tick))),
            new GraphNodeType(CurveOutput, GraphNodeRole.CurveOutput, value, new float[] { 0 },
                a => (float[])a.Inputs[0].Clone()),
            // The curve where it has a value, else the fallback; and 1 where it has a value, else 0.
            new GraphNodeType(MaskedCurveInput, GraphNodeRole.Input, new[] { "fallback" }, new float[] { 0 },
                new[] { Value, "mask" }, a => {
                    var abbr = a.Node.GetString("abbr");
                    var fallback = a.Inputs[0];
                    var values = new float[a.Ticks.Length];
                    var mask = new float[a.Ticks.Length];
                    for (int i = 0; i < values.Length; ++i) {
                        if (a.Context.TrySampleMaskedCurve(abbr, a.Ticks[i], out float y)) {
                            values[i] = y;
                            mask[i] = 1;
                        } else {
                            values[i] = fallback[i];
                        }
                    }
                    return new[] { values, mask };
                }),
            new GraphNodeType(PhonemeInput, GraphNodeRole.Input, none, new float[0], a => {
                var anchors = a.Context.Phonemes;
                var abbr = a.Node.GetString("abbr");
                if (a.PhonemeIndices != null) {
                    var indices = a.PhonemeIndices;
                    var result = new float[indices.Length];
                    for (int i = 0; i < result.Length; ++i) {
                        result[i] = anchors.At(abbr, indices[i]);
                    }
                    return result;
                }
                var mode = a.Node.GetString("interpolation") switch {
                    "linear" => AnchorInterpolation.Linear,
                    "cubic" => AnchorInterpolation.Cubic,
                    _ => AnchorInterpolation.Step,
                };
                return Map(a, tick => anchors.Sample(abbr, tick, mode));
            }),
            new GraphNodeType(PhonemeOutput, GraphNodeRole.PhonemeOutput, value, new float[] { 0 },
                a => (float[])a.Inputs[0].Clone()),
            new GraphNodeType(PitchInput, GraphNodeRole.Input, none, new float[0], a => {
                PhrasePitch.TryParse(a.Node.GetString("source"), out var source);
                var context = a.Context;
                return Map(a, tick => context.Pitch?.Sample(source, tick) ?? 0);
            }),
            new GraphNodeType(PitchOutput, GraphNodeRole.PitchOutput, value, new float[] { 0 },
                a => (float[])a.Inputs[0].Clone()),

            Binary(Add, 0, 0, (x, y) => x + y),
            Binary(Subtract, 0, 0, (x, y) => x - y),
            Binary(Multiply, 1, 1, (x, y) => x * y),
            Binary(Divide, 0, 1, (x, y) => y == 0 ? 0 : x / y),
            Binary(Min, 0, 0, Math.Min),
            Binary(Max, 0, 0, Math.Max),
            // A to B by the factor, held within 0 to 1: e.g. a 0/1 mask picks one input or the other.
            new GraphNodeType(Mix, GraphNodeRole.Process, new[] { "a", "b", "factor" }, new float[] { 0, 0, 0.5f }, a => {
                var x = a.Inputs[0];
                var y = a.Inputs[1];
                var t = a.Inputs[2];
                var result = new float[a.Ticks.Length];
                for (int i = 0; i < result.Length; ++i) {
                    float f = Math.Clamp(t[i], 0, 1);
                    result[i] = x[i] + (y[i] - x[i]) * f;
                }
                return result;
            }),
            new GraphNodeType(Abs, GraphNodeRole.Process, value, new float[] { 0 },
                a => Unary(a, Math.Abs)),

            new GraphNodeType(MapRange, GraphNodeRole.Process, value, new float[] { 0 }, a => {
                float inMin = a.Node.GetFloat("in_min", 0);
                float inMax = a.Node.GetFloat("in_max", 1);
                float outMin = a.Node.GetFloat("out_min", 0);
                float outMax = a.Node.GetFloat("out_max", 1);
                bool clamp = a.Node.GetBool("clamp");
                float lo = Math.Min(outMin, outMax);
                float hi = Math.Max(outMin, outMax);
                return Unary(a, x => {
                    float y = inMax == inMin ? outMin : outMin + (x - inMin) * (outMax - outMin) / (inMax - inMin);
                    return clamp ? Math.Clamp(y, lo, hi) : y;
                });
            }),
            new GraphNodeType(Clamp, GraphNodeRole.Process, value, new float[] { 0 }, a => {
                float min = a.Node.GetFloat("min", float.NegativeInfinity);
                float max = a.Node.GetFloat("max", float.PositiveInfinity);
                return Unary(a, x => Math.Max(min, Math.Min(max, x)));
            }),

            new GraphNodeType(Lfo, GraphNodeRole.Input, none, new float[0], a => {
                float rate = a.Node.GetFloat("rate", 5);
                bool perBeat = a.Node.GetString("unit") == "beat";
                double phase = a.Node.GetFloat("phase", 0);
                float amplitude = a.Node.GetFloat("amplitude", 1);
                float offset = a.Node.GetFloat("offset", 0);
                var context = a.Context;
                // Phase counts from the start of the project, so it doesn't depend on how notes group into phrases.
                return Map(a, tick => {
                    int absTick = context.PartPosition + tick;
                    double cycles = perBeat
                        ? absTick / (double)context.Resolution * rate
                        : context.Axis.TickPosToMsPos(absTick) / 1000.0 * rate;
                    return offset + amplitude * (float)Math.Sin(2 * Math.PI * (cycles + phase));
                });
            }),
            new GraphNodeType(NotePosition, GraphNodeRole.Input, none, new float[0], a => {
                var context = a.Context;
                return Map(a, tick => {
                    var note = context.NoteAt(tick);
                    return note == null ? 0 : (float)(tick - note.Position) / note.Duration;
                });
            }),
            new GraphNodeType(NoteEnvelope, GraphNodeRole.Input, none, new float[0], a => {
                double attack = a.Node.GetFloat("attack_ms", 0);
                double release = a.Node.GetFloat("release_ms", 0);
                var context = a.Context;
                return Map(a, tick => {
                    var note = context.NoteAt(tick);
                    if (note == null) {
                        return 0;
                    }
                    double ms = context.Axis.TickPosToMsPos(context.PartPosition + tick);
                    double start = context.Axis.TickPosToMsPos(context.PartPosition + note.Position);
                    double end = context.Axis.TickPosToMsPos(context.PartPosition + note.End);
                    double y = 1;
                    if (attack > 0) {
                        y = Math.Min(y, (ms - start) / attack);
                    }
                    if (release > 0) {
                        y = Math.Min(y, (end - ms) / release);
                    }
                    return (float)Math.Clamp(y, 0, 1);
                });
            }),
        }.ToDictionary(t => t.Name);

        public static IReadOnlyDictionary<string, GraphNodeType> All => types;

        public const string InputCategory = "input";
        public const string MathCategory = "math";
        public const string TimeCategory = "time";
        public const string OutputCategory = "output";

        /// <summary>Node types by category, in the order the editor offers them.</summary>
        public static readonly (string category, string[] types)[] Categories = {
            (InputCategory, new[] { CurveInput, MaskedCurveInput, PhonemeInput, PitchInput, Constant }),
            (MathCategory, new[] { Add, Subtract, Multiply, Divide, Mix, Min, Max, Abs, MapRange, Clamp }),
            (TimeCategory, new[] { Lfo, NotePosition, NoteEnvelope }),
            (OutputCategory, new[] { CurveOutput, PhonemeOutput, PitchOutput }),
        };

        static GraphNodeParameter Number(string name, string? @default) => new GraphNodeParameter(name, GraphParameterKind.Number, @default);
        static readonly GraphNodeParameter abbr = new GraphNodeParameter("abbr", GraphParameterKind.Expression) { Labeled = false };

        static readonly Dictionary<string, GraphNodeParameter[]> parameters = new Dictionary<string, GraphNodeParameter[]> {
            [Constant] = new[] { Number("value", "0") },
            [CurveInput] = new[] { abbr },
            [MaskedCurveInput] = new[] { abbr },
            [CurveOutput] = new[] { abbr },
            [PhonemeInput] = new[] { abbr, new GraphNodeParameter("interpolation", GraphParameterKind.Choice, "step", "step", "linear", "cubic") },
            [PhonemeOutput] = new[] { abbr },
            [PitchInput] = new[] { new GraphNodeParameter("source", GraphParameterKind.Choice, "pitch_bend",
                "pitch_bend", "vibrato", "mod_plus", "notes") { Labeled = false } },
            [MapRange] = new[] {
                Number("in_min", "0"), Number("in_max", "1"), Number("out_min", "0"), Number("out_max", "1"),
                new GraphNodeParameter("clamp", GraphParameterKind.Bool, "false"),
            },
            [Clamp] = new[] { Number("min", null), Number("max", null) },
            [Lfo] = new[] {
                Number("rate", "5"), new GraphNodeParameter("unit", GraphParameterKind.Choice, "hz", "hz", "beat"),
                Number("phase", "0"), Number("amplitude", "1"), Number("offset", "0"),
            },
            [NoteEnvelope] = new[] { Number("attack_ms", "0"), Number("release_ms", "0") },
        };

        /// <summary>
        /// Expressions that change phonemizing or phoneme timing, which happen before the graph runs.
        /// A graph can read them but not drive them.
        /// </summary>
        public static readonly IReadOnlyCollection<string> TimingExpressions = new HashSet<string> {
            Format.Ustx.ALT, Format.Ustx.SHFT, Format.Ustx.VEL,
        };

        /// <summary>Whether a graph can drive a per-phoneme expression on a renderer.</summary>
        public static bool CanDrivePhonemeExpression(Ustx.UExpressionDescriptor descriptor, Render.IRenderer? renderer) =>
            descriptor.type == Ustx.UExpressionType.Numerical && !TimingExpressions.Contains(descriptor.abbr)
                && (!string.IsNullOrEmpty(descriptor.flag) || renderer == null || renderer.SupportsExpression(descriptor));

        /// <summary>Whether a graph can drive a curve on a renderer. PITD is driven through the pitch output.</summary>
        public static bool CanDriveCurve(Ustx.UExpressionDescriptor descriptor, Render.IRenderer? renderer) =>
            descriptor.type == Ustx.UExpressionType.Curve && descriptor.abbr != Format.Ustx.PITD
                && (renderer == null || renderer.SupportsExpression(descriptor));

        /// <summary>The expressions a node's "abbr" parameter can name, for a graph targeting a renderer.</summary>
        public static IEnumerable<Ustx.UExpressionDescriptor> ExpressionChoices(Ustx.UProject project, string? type, string? renderer) {
            Render.IRenderer? instance = null;
            if (!string.IsNullOrEmpty(renderer)) {
                try {
                    instance = Render.Renderers.GetOrCreate(renderer);
                } catch {
                    instance = null;
                }
            }
            var all = project.expressions.Values;
            return type switch {
                CurveInput => all.Where(d => d.type == Ustx.UExpressionType.Curve),
                MaskedCurveInput => all.Where(d => d.type == Ustx.UExpressionType.MaskedCurve),
                CurveOutput => all.Where(d => CanDriveCurve(d, instance)),
                PhonemeInput => all.Where(d => d.type == Ustx.UExpressionType.Numerical),
                PhonemeOutput => all.Where(d => CanDrivePhonemeExpression(d, instance)),
                _ => Enumerable.Empty<Ustx.UExpressionDescriptor>(),
            };
        }

        /// <summary>The parameters the editor shows for a node type, besides its input ports.</summary>
        public static IReadOnlyList<GraphNodeParameter> ParametersOf(string? type) =>
            type != null && parameters.TryGetValue(type, out var result) ? result : Array.Empty<GraphNodeParameter>();

        public static bool TryGet(string? name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GraphNodeType? type) {
            type = null;
            return name != null && types.TryGetValue(name, out type);
        }

        static GraphNodeType Binary(string name, float aDefault, float bDefault, Func<float, float, float> op) =>
            new GraphNodeType(name, GraphNodeRole.Process, ab, new[] { aDefault, bDefault }, a => {
                var x = a.Inputs[0];
                var y = a.Inputs[1];
                var result = new float[a.Ticks.Length];
                for (int i = 0; i < result.Length; ++i) {
                    result[i] = op(x[i], y[i]);
                }
                return result;
            });

        static float[] Unary(NodeArgs a, Func<float, float> op) {
            var x = a.Inputs[0];
            var result = new float[a.Ticks.Length];
            for (int i = 0; i < result.Length; ++i) {
                result[i] = op(x[i]);
            }
            return result;
        }

        static float[] Map(NodeArgs a, Func<int, float> f) {
            var result = new float[a.Ticks.Length];
            for (int i = 0; i < result.Length; ++i) {
                result[i] = f(a.Ticks[i]);
            }
            return result;
        }

        internal static float[] Fill(NodeArgs a, float value) {
            var result = new float[a.Ticks.Length];
            Array.Fill(result, value);
            return result;
        }
    }
}
