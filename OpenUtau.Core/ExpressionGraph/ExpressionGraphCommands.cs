using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>
    /// A copy of everything about expression graphs a project stores: the library, the default graph of each
    /// renderer and each track's override. Never changed once captured.
    /// </summary>
    public sealed class ExpressionGraphState {
        public readonly IReadOnlyList<UExpressionGraph> Graphs;
        public readonly IReadOnlyDictionary<string, string> Defaults;
        public readonly IReadOnlyList<string?> TrackOverrides;

        public ExpressionGraphState(IEnumerable<UExpressionGraph> graphs, IReadOnlyDictionary<string, string> defaults,
                IEnumerable<string?> trackOverrides) {
            Graphs = graphs.Select(g => g.Clone()).ToList();
            Defaults = new Dictionary<string, string>(defaults);
            TrackOverrides = trackOverrides.ToList();
        }

        public static ExpressionGraphState Capture(UProject project) => new ExpressionGraphState(
            project.expressionGraphs ?? new List<UExpressionGraph>(),
            project.defaultExpressionGraphs ?? new Dictionary<string, string>(),
            project.tracks.Select(t => t.ExpressionGraph));

        /// <summary>Puts copies into the project; an empty library is stored as none.</summary>
        public void ApplyTo(UProject project) {
            project.expressionGraphs = Graphs.Count > 0 ? Graphs.Select(g => g.Clone()).ToList() : null;
            project.defaultExpressionGraphs = Defaults.Count > 0 ? new Dictionary<string, string>(Defaults) : null;
            for (int i = 0; i < project.tracks.Count && i < TrackOverrides.Count; ++i) {
                project.tracks[i].ExpressionGraph = TrackOverrides[i];
            }
        }

        /// <summary>Whether the two differ only in where nodes sit on the canvas, which doesn't change any render.</summary>
        public bool SameExceptLayout(ExpressionGraphState other) {
            string Content(ExpressionGraphState state) => Yaml.DefaultSerializer.Serialize(new {
                graphs = state.Graphs.Select(g => {
                    var copy = g.Clone();
                    copy.nodes.ForEach(n => { n.x = 0; n.y = 0; });
                    return copy;
                }).ToList(),
                defaults = state.Defaults,
                overrides = state.TrackOverrides,
            });
            return Content(this) == Content(other);
        }
    }

    /// <summary>
    /// Replaces the project's expression graphs with a new state, undoably. Every graph edit goes through it:
    /// the editor builds the new state from a copy and executes this.
    /// </summary>
    public class SetExpressionGraphsCommand : UCommand {
        readonly UProject project;
        readonly ExpressionGraphState before;
        readonly ExpressionGraphState after;
        readonly bool layoutOnly;

        public SetExpressionGraphsCommand(UProject project, ExpressionGraphState after) {
            this.project = project;
            before = ExpressionGraphState.Capture(project);
            this.after = after;
            layoutOnly = before.SameExceptLayout(after);
        }

        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            SkipPhonemizer = true,
            SkipPhoneme = true,
        };
        // Moving nodes doesn't change any render.
        public override Pipeline.ImpactSet Impact => layoutOnly ? Pipeline.ImpactSet.None : Pipeline.ImpactSet.All;
        public override string ToString() => "Edit expression graphs";
        public override void Execute() => after.ApplyTo(project);
        public override void Unexecute() => before.ApplyTo(project);
    }

    /// <summary>Edits that build a new state from the project's current one.</summary>
    public static class ExpressionGraphEdits {
        /// <summary>A mutable copy of the project's graphs, defaults and overrides.</summary>
        public sealed class Draft {
            public readonly List<UExpressionGraph> Graphs;
            public readonly Dictionary<string, string> Defaults;
            public readonly List<string?> TrackOverrides;

            public Draft(UProject project) {
                var state = ExpressionGraphState.Capture(project);
                Graphs = state.Graphs.Select(g => g.Clone()).ToList();
                Defaults = new Dictionary<string, string>(state.Defaults);
                TrackOverrides = state.TrackOverrides.ToList();
            }

            public UExpressionGraph? Find(string id) => Graphs.FirstOrDefault(g => g.id == id);

            public ExpressionGraphState ToState() => new ExpressionGraphState(Graphs, Defaults, TrackOverrides);

            /// <summary>An id not used by any graph, based on a name.</summary>
            public string NewId(string name) {
                var baseId = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
                if (string.IsNullOrEmpty(baseId)) {
                    baseId = "graph";
                }
                var id = baseId;
                for (int i = 2; Graphs.Any(g => g.id == id); ++i) {
                    id = $"{baseId}_{i}";
                }
                return id;
            }

            /// <summary>Removes a graph and every reference to it.</summary>
            public void Remove(string id) {
                Graphs.RemoveAll(g => g.id == id);
                foreach (var key in Defaults.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) {
                    Defaults.Remove(key);
                }
                for (int i = 0; i < TrackOverrides.Count; ++i) {
                    if (TrackOverrides[i] == id) {
                        TrackOverrides[i] = null;
                    }
                }
            }
        }

        /// <summary>Applies an edit to a copy of the project's graphs and executes it as one undoable step.</summary>
        public static void Apply(UProject project, Action<Draft> edit, DocManager? docManager = null) {
            docManager ??= DocManager.Inst;
            var draft = new Draft(project);
            edit(draft);
            docManager.StartUndoGroup("command.expressiongraph.edit");
            docManager.ExecuteCmd(new SetExpressionGraphsCommand(project, draft.ToState()));
            docManager.EndUndoGroup();
        }

        /// <summary>
        /// Links an output into a node's input port, replacing any link already into that port.
        /// Returns false, changing nothing, if the link would close a cycle or the port doesn't exist.
        /// </summary>
        public static bool TryLink(UExpressionGraph graph, int from, int to, string port, string? fromPort = null) {
            var target = graph.nodes.FirstOrDefault(n => n.id == to);
            var source = graph.nodes.FirstOrDefault(n => n.id == from);
            if (target == null || source == null
                    || !GraphNodeTypes.TryGet(target.type, out var targetType) || !targetType.Ports.Contains(port)
                    || !GraphNodeTypes.TryGet(source.type, out var sourceType) || sourceType.OutputIndex(fromPort) < 0
                    || ExpressionGraphProgram.WouldCreateCycle(graph, from, to)) {
                return false;
            }
            graph.links.RemoveAll(l => l.to == to && l.toPort == port);
            // The only output of a node needs no name.
            graph.links.Add(new UGraphLink {
                from = from,
                fromPort = sourceType.Outputs.Length > 1 ? fromPort ?? sourceType.Outputs[0] : null,
                to = to,
                toPort = port,
            });
            return true;
        }

        /// <summary>Removes a node and its links.</summary>
        public static void RemoveNode(UExpressionGraph graph, int id) {
            graph.nodes.RemoveAll(n => n.id == id);
            graph.links.RemoveAll(l => l.from == id || l.to == id);
        }

        /// <summary>
        /// A new graph for a renderer, drawing and loading pitch into the pitch override (PITO), which replaces the
        /// pitch wherever it has a value. Underneath it: for a renderer that renders pitch, the rendered pitch
        /// (RPIT) where stored, else the notes, plus PITD; for any other, today's pitch: pitch bends, vibrato,
        /// MOD+ and PITD added up.
        /// </summary>
        public static UExpressionGraph CreateDefault(string id, string name, string renderer) {
            Render.IRenderer? instance = null;
            try {
                instance = Render.Renderers.GetOrCreate(renderer);
            } catch {
                instance = null;
            }
            return CreateDefault(id, name, renderer, instance?.SupportsRenderPitch == true);
        }

        internal static UExpressionGraph CreateDefault(string id, string name, string renderer, bool rendersPitch) {
            var graph = new UExpressionGraph {
                id = id,
                name = name,
                renderer = renderer,
                preferredPitchCurve = Format.Ustx.PITO,
            };
            UGraphNode Pitch(string source, float x, float y) {
                var node = AddNode(graph, GraphNodeTypes.PitchInput, x, y);
                node.Set("source", source);
                return node;
            }
            UGraphNode Curve(string type, string abbr, float x, float y) {
                var node = AddNode(graph, type, x, y);
                node.Set("abbr", abbr);
                return node;
            }
            UGraphNode Add(UGraphNode a, UGraphNode b, float x, float y) {
                var node = AddNode(graph, GraphNodeTypes.Add, x, y);
                TryLink(graph, a.id, node.id, "a");
                TryLink(graph, b.id, node.id, "b");
                return node;
            }
            UGraphNode underneath;
            if (rendersPitch) {
                var notes = Pitch("notes", 20, 40);
                var rendered = Curve(GraphNodeTypes.MaskedCurveInput, Format.Ustx.RPIT, 160, 40);
                TryLink(graph, notes.id, rendered.id, "fallback");
                underneath = Add(rendered, Curve(GraphNodeTypes.CurveInput, Format.Ustx.PITD, 160, 160), 390, 100);
            } else {
                var sum = Add(Pitch("pitch_bend", 20, 20), Pitch("vibrato", 20, 110), 160, 60);
                sum = Add(sum, Pitch("mod_plus", 20, 200), 270, 130);
                underneath = Add(sum, Curve(GraphNodeTypes.CurveInput, Format.Ustx.PITD, 20, 290), 380, 200);
            }
            var overrides = Curve(GraphNodeTypes.MaskedCurveInput, Format.Ustx.PITO, 500, 100);
            TryLink(graph, underneath.id, overrides.id, "fallback");
            var output = AddNode(graph, GraphNodeTypes.PitchOutput, 740, 100);
            TryLink(graph, overrides.id, output.id, GraphNodeTypes.Value, GraphNodeTypes.Value);
            return graph;
        }

        /// <summary>Adds a node of a type with its parameters' defaults, returning it.</summary>
        public static UGraphNode AddNode(UExpressionGraph graph, string type, float x, float y) {
            var node = new UGraphNode {
                id = graph.nodes.Count == 0 ? 1 : graph.nodes.Max(n => n.id) + 1,
                type = type,
                x = x,
                y = y,
            };
            foreach (var parameter in GraphNodeTypes.ParametersOf(type)) {
                if (parameter.Default != null) {
                    node.Set(parameter.Name, parameter.Default);
                }
            }
            graph.nodes.Add(node);
            return node;
        }
    }
}
