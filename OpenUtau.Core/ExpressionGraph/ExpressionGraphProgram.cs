using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>A node with its parameters copied out of the document.</summary>
    public sealed class CompiledNode {
        public readonly int Id;
        public readonly GraphNodeType Type;
        readonly Dictionary<string, string> parameters;
        // For each port: the index of the node feeding it in evaluation order, or -1 for a constant;
        // and which of that node's outputs.
        internal readonly int[] sources;
        internal readonly int[] sourceOutputs;
        internal readonly float[] constants;

        internal CompiledNode(UGraphNode node, GraphNodeType type) {
            Id = node.id;
            Type = type;
            parameters = new Dictionary<string, string>(node.parameters);
            sources = Enumerable.Repeat(-1, type.Ports.Length).ToArray();
            sourceOutputs = new int[type.Ports.Length];
            constants = type.Ports.Select((port, i) => GetFloat(port, type.PortDefaults[i])).ToArray();
        }

        public string? GetString(string key) => parameters.TryGetValue(key, out var value) ? value : null;

        public float GetFloat(string key, float fallback) =>
            parameters.TryGetValue(key, out var text)
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;

        public bool GetBool(string key) => GetString(key) is "true" or "True" or "1";
    }

    /// <summary>What nodes read from the part being rendered.</summary>
    public sealed class GraphContext {
        public readonly TimeAxis Axis;
        public readonly int PartPosition;
        public readonly int Resolution;
        public readonly PhonemeAnchors Phonemes;
        /// <summary>The phrase's pitch sources; null outside a phrase build.</summary>
        public readonly PhrasePitch? Pitch;
        readonly Dictionary<string, CurveSource> curves;
        readonly IReadOnlyDictionary<string, UMaskedRun[]> maskedCurves;
        readonly Dictionary<string, int> defaults;
        readonly NoteSource[] notes;

        public GraphContext(TimeAxis axis, int partPosition, int resolution,
                IEnumerable<CurveSource> curves, IReadOnlyDictionary<string, int> defaults, IEnumerable<NoteSource> notes,
                PhonemeAnchors? phonemes = null, PhrasePitch? pitch = null,
                IReadOnlyDictionary<string, UMaskedRun[]>? maskedCurves = null) {
            Axis = axis;
            PartPosition = partPosition;
            Resolution = resolution;
            Phonemes = phonemes ?? PhonemeAnchors.Empty;
            Pitch = pitch;
            this.maskedCurves = maskedCurves ?? new Dictionary<string, UMaskedRun[]>();
            this.curves = new Dictionary<string, CurveSource>();
            foreach (var curve in curves) {
                this.curves.TryAdd(curve.Abbr, curve);
            }
            this.defaults = new Dictionary<string, int>(defaults);
            this.notes = notes.OrderBy(n => n.Position).ToArray();
        }

        internal GraphContext(PhraseSource source, PhrasePitch? pitch = null)
            : this(source.Axis, source.PartPosition, source.Resolution, source.Curves, source.CurveDefaults, source.Notes,
                source.PhonemeAnchors, pitch, source.MaskedCurves) { }

        /// <summary>A curve's value at a part-relative tick, exactly as the renderer reads it without a graph.</summary>
        public float SampleCurve(string? abbr, int tick) {
            if (abbr != null && curves.TryGetValue(abbr, out var curve)) {
                return curve.Sample(tick);
            }
            return abbr != null && defaults.TryGetValue(abbr, out int y) ? y : 0;
        }

        /// <summary>A masked curve's value at a part-relative tick, if it has one there.</summary>
        public bool TrySampleMaskedCurve(string? abbr, int tick, out float value) {
            value = 0;
            return abbr != null && maskedCurves.TryGetValue(abbr, out var runs) && UMaskedCurve.TrySample(runs, tick, out value);
        }

        /// <summary>The note covering a part-relative tick, if any.</summary>
        public NoteSource? NoteAt(int tick) {
            int lo = 0, hi = notes.Length - 1, found = -1;
            while (lo <= hi) {
                int mid = (lo + hi) / 2;
                if (notes[mid].Position <= tick) {
                    found = mid;
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }
            return found >= 0 && tick < notes[found].End ? notes[found] : null;
        }
    }

    /// <summary>
    /// A validated graph, ready to evaluate off the UI thread. Immutable once compiled.
    /// </summary>
    public sealed class ExpressionGraphProgram {
        /// <summary>The nodes feeding one kind of output, in evaluation order, and each output's node.</summary>
        sealed class Plan {
            public readonly CompiledNode[] Order;
            public readonly Dictionary<string, int> Outputs;

            public Plan(CompiledNode[] order, Dictionary<string, int> outputs) {
                Order = order;
                Outputs = outputs;
            }

            public Dictionary<string, float[]> Evaluate(GraphContext context, int[] ticks, int[]? phonemeIndices) {
                // Each node's outputs.
                var values = new float[Order.Length][][];
                for (int i = 0; i < Order.Length; ++i) {
                    var node = Order[i];
                    var inputs = new float[node.sources.Length][];
                    for (int p = 0; p < inputs.Length; ++p) {
                        if (node.sources[p] >= 0) {
                            inputs[p] = values[node.sources[p]][node.sourceOutputs[p]];
                        } else {
                            inputs[p] = new float[ticks.Length];
                            Array.Fill(inputs[p], node.constants[p]);
                        }
                    }
                    values[i] = node.Type.Evaluate(new NodeArgs(inputs, node, context, ticks, phonemeIndices));
                }
                return Outputs.ToDictionary(kv => kv.Key, kv => values[kv.Value][0]);
            }
        }

        const string PitchKey = "pitch";

        readonly Plan curves;
        readonly Plan phonemes;
        readonly Plan pitch;

        ExpressionGraphProgram(Plan curves, Plan phonemes, Plan pitch) {
            this.curves = curves;
            this.phonemes = phonemes;
            this.pitch = pitch;
        }

        /// <summary>Whether the graph drives the pitch.</summary>
        public bool DrivesPitch => pitch.Outputs.Count > 0;


        /// <summary>The driven pitch in cents at the given part-relative ticks, or null when the graph doesn't drive it.</summary>
        public float[]? EvaluatePitch(GraphContext context, int[] ticks) =>
            DrivesPitch ? pitch.Evaluate(context, ticks, null)[PitchKey] : null;

        /// <summary>The curves this graph drives. Every other curve reaches the renderer as drawn.</summary>
        public IReadOnlyCollection<string> CurveOutputs => curves.Outputs.Keys;

        /// <summary>The per-phoneme expressions this graph drives.</summary>
        public IReadOnlyCollection<string> PhonemeOutputs => phonemes.Outputs.Keys;

        public bool DrivesCurve(string abbr) => curves.Outputs.ContainsKey(abbr);

        /// <summary>Evaluates every driven curve at the given part-relative ticks.</summary>
        public Dictionary<string, float[]> Evaluate(GraphContext context, int[] ticks) =>
            curves.Evaluate(context, ticks, null);

        /// <summary>
        /// Evaluates every driven per-phoneme expression at each phoneme's own position, one value per phoneme.
        /// Per-phoneme inputs read each phoneme's exact value there, without interpolating.
        /// </summary>
        public Dictionary<string, float[]> EvaluatePhonemes(GraphContext context) {
            var anchors = context.Phonemes;
            var indices = Enumerable.Range(0, anchors.Ticks.Length).ToArray();
            return phonemes.Evaluate(context, anchors.Ticks, indices);
        }

        /// <summary>
        /// Checks a graph and prepares it for evaluation. Returns null with the reason when the graph can't run:
        /// an unknown node type, a broken link, two outputs for the same target, or a cycle.
        /// </summary>
        public static ExpressionGraphProgram? Compile(UExpressionGraph graph, out string? error) {
            error = null;
            var nodes = new Dictionary<int, (UGraphNode node, GraphNodeType type)>();
            foreach (var node in graph.nodes) {
                if (!GraphNodeTypes.TryGet(node.type, out var type)) {
                    error = $"Node {node.id} has unknown type \"{node.type}\".";
                    return null;
                }
                if (!nodes.TryAdd(node.id, (node, type))) {
                    error = $"Two nodes have id {node.id}.";
                    return null;
                }
                if (type.NeedsAbbr && string.IsNullOrEmpty(node.GetString("abbr"))) {
                    error = $"Node {node.id} has no expression.";
                    return null;
                }
                if (type.Name == GraphNodeTypes.PitchInput && !PhrasePitch.TryParse(node.GetString("source"), out _)) {
                    error = $"Node {node.id} has unknown pitch source \"{node.GetString("source")}\".";
                    return null;
                }
            }
            // Port links, by target node: the node and output feeding each input port.
            var incoming = nodes.Keys.ToDictionary(id => id, _ => new Dictionary<int, (int from, int output)>());
            foreach (var link in graph.links) {
                if (!nodes.ContainsKey(link.from) || !nodes.TryGetValue(link.to, out var target)) {
                    error = $"A link connects missing node {(nodes.ContainsKey(link.from) ? link.to : link.from)}.";
                    return null;
                }
                int output = nodes[link.from].type.OutputIndex(link.fromPort);
                if (output < 0) {
                    error = $"Node {link.from} has no output \"{link.fromPort}\".";
                    return null;
                }
                int port = Array.IndexOf(target.type.Ports, link.toPort);
                if (port < 0) {
                    error = $"Node {link.to} has no input \"{link.toPort}\".";
                    return null;
                }
                if (!incoming[link.to].TryAdd(port, (link.from, output))) {
                    error = $"Input \"{link.toPort}\" of node {link.to} has two links.";
                    return null;
                }
            }
            if (HasCycle(nodes.Keys, incoming)) {
                error = "The graph has a cycle.";
                return null;
            }
            var curvePlan = PlanFor(GraphNodeRole.CurveOutput, nodes, incoming, ref error);
            var phonemePlan = PlanFor(GraphNodeRole.PhonemeOutput, nodes, incoming, ref error);
            var pitchPlan = PlanFor(GraphNodeRole.PitchOutput, nodes, incoming, ref error);
            if (curvePlan == null || phonemePlan == null || pitchPlan == null) {
                return null;
            }
            // Pitch is computed per phrase, after the per-phoneme values are resolved for the whole part.
            if (phonemePlan.Order.Any(n => n.Type.Name == GraphNodeTypes.PitchInput)) {
                error = "Per-phoneme outputs can't read pitch.";
                return null;
            }
            return new ExpressionGraphProgram(curvePlan, phonemePlan, pitchPlan);
        }

        /// <summary>The outputs of one kind that have a connected value, and everything they depend on.</summary>
        static Plan? PlanFor(GraphNodeRole role, Dictionary<int, (UGraphNode node, GraphNodeType type)> nodes,
                Dictionary<int, Dictionary<int, (int from, int output)>> incoming, ref string? error) {
            var outputs = new Dictionary<string, int>();
            foreach (var (node, _) in nodes.Values.Where(n => n.type.Role == role)) {
                string abbr = role == GraphNodeRole.PitchOutput ? PitchKey : node.GetString("abbr")!;
                if (outputs.ContainsKey(abbr)) {
                    error = $"Two outputs drive \"{abbr}\".";
                    return null;
                }
                outputs[abbr] = node.id;
            }
            var connected = outputs.Where(kv => incoming[kv.Value].Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);

            var ordered = new List<int>();
            var visited = new HashSet<int>();
            void Visit(int id) {
                if (!visited.Add(id)) {
                    return;
                }
                foreach (var from in incoming[id].OrderBy(kv => kv.Key).Select(kv => kv.Value.from)) {
                    Visit(from);
                }
                ordered.Add(id);
            }
            foreach (var id in connected.Values.OrderBy(id => id)) {
                Visit(id);
            }
            var indexOf = ordered.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
            var compiled = ordered.Select(id => new CompiledNode(nodes[id].node, nodes[id].type)).ToArray();
            for (int i = 0; i < compiled.Length; ++i) {
                foreach (var (port, (from, output)) in incoming[ordered[i]]) {
                    compiled[i].sources[port] = indexOf[from];
                    compiled[i].sourceOutputs[port] = output;
                }
            }
            return new Plan(compiled, connected.ToDictionary(kv => kv.Key, kv => indexOf[kv.Value]));
        }

        static bool HasCycle(IEnumerable<int> ids, Dictionary<int, Dictionary<int, (int from, int output)>> incoming) {
            var state = new Dictionary<int, int>(); // 1: visiting, 2: done
            bool Visit(int id) {
                state.TryGetValue(id, out int s);
                if (s == 1) {
                    return true;
                }
                if (s == 2) {
                    return false;
                }
                state[id] = 1;
                foreach (var (from, _) in incoming[id].Values) {
                    if (Visit(from)) {
                        return true;
                    }
                }
                state[id] = 2;
                return false;
            }
            return ids.Any(Visit);
        }

        /// <summary>Whether linking <paramref name="from"/> into <paramref name="to"/> would close a cycle.</summary>
        public static bool WouldCreateCycle(UExpressionGraph graph, int from, int to) {
            if (from == to) {
                return true;
            }
            // A cycle appears if "to" already feeds "from".
            var stack = new Stack<int>();
            var seen = new HashSet<int>();
            stack.Push(from);
            while (stack.Count > 0) {
                int id = stack.Pop();
                if (id == to) {
                    return true;
                }
                if (!seen.Add(id)) {
                    continue;
                }
                foreach (var link in graph.links.Where(l => l.to == id)) {
                    stack.Push(link.from);
                }
            }
            return false;
        }

        /// <summary>
        /// The graph a track renders with: its override, else the project's default for its renderer.
        /// A graph made for another renderer is not used.
        /// </summary>
        public static UExpressionGraph? GetEffectiveGraph(UProject project, UTrack track) {
            string? renderer = track.RendererSettings?.renderer;
            if (project.expressionGraphs == null || project.expressionGraphs.Count == 0 || string.IsNullOrEmpty(renderer)) {
                return null;
            }
            string? id = track.ExpressionGraph;
            if (id == null && project.defaultExpressionGraphs != null) {
                project.defaultExpressionGraphs.TryGetValue(renderer, out id);
            }
            var graph = id == null ? null : project.expressionGraphs.FirstOrDefault(g => g.id == id);
            return graph?.renderer == renderer ? graph : null;
        }

        /// <summary>
        /// Whether the track draws and loads pitch into the pitch override (PITO) instead of PITD: its graph,
        /// one that runs, prefers PITO. PITD otherwise, as without a graph.
        /// </summary>
        public static bool PrefersPitchOverride(UProject project, UTrack track) {
            var graph = GetEffectiveGraph(project, track);
            return graph?.preferredPitchCurve == Format.Ustx.PITO && Compile(graph, out _) != null;
        }

        /// <summary>Compiles the track's effective graph, or null when it has none or it can't run.</summary>
        public static ExpressionGraphProgram? ForTrack(UProject project, UTrack track) {
            var graph = GetEffectiveGraph(project, track);
            return graph == null ? null : Compile(graph, out _);
        }

        /// <summary>Logs every graph in the project that can't run.</summary>
        public static void LogProblems(UProject project) {
            foreach (var graph in project.expressionGraphs ?? Enumerable.Empty<UExpressionGraph>()) {
                if (Compile(graph, out var error) == null) {
                    Log.Warning($"Expression graph \"{graph.name ?? graph.id}\" is ignored: {error}");
                }
            }
        }
    }
}
