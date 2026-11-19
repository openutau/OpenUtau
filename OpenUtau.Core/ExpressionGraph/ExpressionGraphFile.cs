using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>
    /// One expression graph as a standalone file, bundled with the definitions of the expressions its nodes read
    /// or write, so it can be shared between projects.
    /// </summary>
    public class ExpressionGraphFile {
        public const string FormatName = "openutau_expression_graph";
        public const int CurrentVersion = 1;

        public string? format;
        public int version;
        public UExpressionGraph? graph;
        public List<UExpressionDescriptor> expressions = new List<UExpressionDescriptor>();

        /// <summary>The abbreviations a graph's nodes name, in node order.</summary>
        public static IEnumerable<string> ReferencedExpressions(UExpressionGraph graph) {
            return graph.nodes
                .SelectMany(node => GraphNodeTypes.ParametersOf(node.type)
                    .Where(p => p.Kind == GraphParameterKind.Expression)
                    .Select(p => node.GetString(p.Name)))
                .OfType<string>()
                .Where(abbr => abbr.Length > 0)
                .Distinct();
        }

        /// <summary>A graph of the project with the project's definitions of the expressions it uses.</summary>
        public static ExpressionGraphFile Create(UProject project, UExpressionGraph graph) {
            return new ExpressionGraphFile {
                format = FormatName,
                version = CurrentVersion,
                graph = graph.Clone(),
                expressions = ReferencedExpressions(graph)
                    .Select(abbr => project.expressions.TryGetValue(abbr, out var descriptor) ? descriptor.Clone() : null)
                    .OfType<UExpressionDescriptor>()
                    .ToList(),
            };
        }

        public void Save(string path) {
            File.WriteAllText(path, Yaml.DefaultSerializer.Serialize(this));
        }

        /// <summary>Reads a file, throwing if it isn't an expression graph file this version can read.</summary>
        public static ExpressionGraphFile Load(string path) {
            ExpressionGraphFile? file;
            try {
                file = Yaml.DefaultDeserializer.Deserialize<ExpressionGraphFile>(File.ReadAllText(path));
            } catch (YamlDotNet.Core.YamlException e) {
                throw new MessageCustomizableException(
                    $"Not an expression graph file: {path}", $"<translate:expressiongraph.import.invalid>: {path}", e, showStackTrace: false);
            }
            if (file == null || file.format != FormatName || file.graph == null) {
                throw new MessageCustomizableException(
                    $"Not an expression graph file: {path}", $"<translate:expressiongraph.import.invalid>: {path}",
                    new InvalidDataException(path), showStackTrace: false);
            }
            if (file.version > CurrentVersion) {
                throw new MessageCustomizableException(
                    $"Expression graph file version {file.version} is newer than this OpenUtau supports: {path}",
                    $"<translate:expressiongraph.import.newer>: {path}", new InvalidDataException(path),
                    showStackTrace: false);
            }
            return file;
        }

        /// <summary>What an import added and what it left alone.</summary>
        public sealed class ImportResult {
            public string GraphId = string.Empty;
            public readonly List<string> AddedExpressions = new List<string>();
            /// <summary>Expressions the project already defines differently; the project's definitions were kept.</summary>
            public readonly List<string> ConflictingExpressions = new List<string>();
        }

        /// <summary>
        /// Adds the graph to the project, under a new id if its id is taken, plus the expressions the project
        /// doesn't define yet, as one undoable step. An expression the project defines differently keeps the
        /// project's definition. The graph becomes its renderer's default if that renderer has none.
        /// </summary>
        public ImportResult ImportInto(UProject project, DocManager? docManager = null) {
            docManager ??= DocManager.Inst;
            var result = new ImportResult();
            var added = new List<UExpressionDescriptor>();
            foreach (var descriptor in expressions) {
                if (string.IsNullOrEmpty(descriptor?.abbr)) {
                    continue;
                }
                if (!project.expressions.TryGetValue(descriptor.abbr, out var existing)) {
                    added.Add(descriptor.Clone());
                    result.AddedExpressions.Add(descriptor.abbr);
                } else if (!SameDefinition(existing, descriptor)) {
                    result.ConflictingExpressions.Add(descriptor.abbr);
                }
            }

            var draft = new ExpressionGraphEdits.Draft(project);
            var imported = graph!.Clone();
            if (string.IsNullOrEmpty(imported.id) || draft.Find(imported.id) != null) {
                imported.id = draft.NewId(string.IsNullOrEmpty(imported.id) ? imported.name ?? "graph" : imported.id);
            }
            draft.Graphs.Add(imported);
            if (imported.renderer != null) {
                draft.Defaults.TryAdd(Render.Renderers.GetExpressionGraphSlot(imported.renderer), imported.id);
            }
            result.GraphId = imported.id;

            docManager.StartUndoGroup("command.expressiongraph.import");
            try {
                if (added.Count > 0) {
                    docManager.ExecuteCmd(new ConfigureExpressionsCommand(
                        project, project.expressions.Values.Concat(added).ToArray()));
                }
                docManager.ExecuteCmd(new SetExpressionGraphsCommand(project, draft.ToState()));
            } finally {
                docManager.EndUndoGroup();
            }
            return result;
        }

        // Compared as saved, so every field counts, including ones added later.
        static bool SameDefinition(UExpressionDescriptor a, UExpressionDescriptor b) =>
            Yaml.DefaultSerializer.Serialize(a) == Yaml.DefaultSerializer.Serialize(b);
    }
}
