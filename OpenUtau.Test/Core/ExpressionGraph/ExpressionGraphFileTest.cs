using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>Exporting a graph with its expressions, importing it into another project, and templates.</summary>
    [Collection(RenderSingletonCollection.Name)]
    public class ExpressionGraphFileTest : IDisposable {
        const string Custom = "brx";
        readonly UProject previousProject;
        readonly List<UCommand> executed = new List<UCommand>();

        public ExpressionGraphFileTest() {
            previousProject = DocManager.Inst.TakeProjectForTest(NewProject());
            DocManager.Inst.CommandSink = cmd => {
                cmd.Execute();
                executed.Add(cmd);
            };
        }

        public void Dispose() {
            DocManager.Inst.CommandSink = null!;
            DocManager.Inst.TakeProjectForTest(previousProject);
        }

        // The undo group's own notifications aside.
        IEnumerable<UCommand> Edits() => executed.Where(c => c is not UNotification);

        static UProject NewProject() {
            var project = new UProject();
            Format.Ustx.AddDefaultExpressions(project);
            return project;
        }

        static UExpressionDescriptor Breath() => new UExpressionDescriptor("breathiness", Custom, 0, 100, 0);

        // Reads the custom curve and drives DYN.
        static UExpressionGraph Graph(string id = "mine") {
            var graph = new UExpressionGraph { id = id, name = "Mine", renderer = Renderers.CLASSIC };
            ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.CurveInput, 0, 0).Set("abbr", Custom);
            ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.CurveOutput, 200, 0).Set("abbr", Format.Ustx.DYN);
            ExpressionGraphEdits.TryLink(graph, 1, 2, GraphNodeTypes.Value);
            return graph;
        }

        static UProject SourceProject() {
            var project = NewProject();
            project.RegisterExpression(Breath());
            project.expressionGraphs = new List<UExpressionGraph> { Graph() };
            return project;
        }

        [Fact]
        public void ExportsTheExpressionsTheGraphUses() {
            var file = ExpressionGraphFile.Create(SourceProject(), Graph());

            Assert.Equal(new[] { Custom, Format.Ustx.DYN }, file.expressions.Select(e => e.abbr));
            Assert.Equal(100, file.expressions[0].max);
        }

        [Fact]
        public void RoundTripsThroughAFile() {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ougraph");
            try {
                ExpressionGraphFile.Create(SourceProject(), Graph()).Save(path);
                var loaded = ExpressionGraphFile.Load(path);

                Assert.Equal("mine", loaded.graph!.id);
                Assert.Equal(Custom, loaded.graph.nodes[0].GetString("abbr"));
                Assert.Single(loaded.graph.links);
                Assert.Equal(new[] { Custom, Format.Ustx.DYN }, loaded.expressions.Select(e => e.abbr));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void RefusesOtherFiles() {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.ougraph");
            try {
                File.WriteAllText(path, "name: New Project\nustx_version: 0.9\n");
                Assert.Throws<MessageCustomizableException>(() => ExpressionGraphFile.Load(path));
                File.WriteAllText(path, "{ not: [valid");
                Assert.Throws<MessageCustomizableException>(() => ExpressionGraphFile.Load(path));
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void ImportAddsMissingExpressionsAndKeepsTheProjectsOwn() {
            var file = ExpressionGraphFile.Create(SourceProject(), Graph());
            var target = DocManager.Inst.Project;
            // The target defines DYN differently and already has a graph with the same id as its Classic default.
            target.expressions[Format.Ustx.DYN].max = 50;
            target.expressionGraphs = new List<UExpressionGraph> { Graph() };
            target.defaultExpressionGraphs = new Dictionary<string, string> { [Renderers.CLASSIC] = "mine" };

            var result = file.ImportInto(target);

            Assert.Equal(new[] { Custom }, result.AddedExpressions);
            Assert.Equal(new[] { Format.Ustx.DYN }, result.ConflictingExpressions);
            Assert.Equal(100, target.expressions[Custom].max);
            Assert.Equal(50, target.expressions[Format.Ustx.DYN].max);
            Assert.NotEqual("mine", result.GraphId);
            Assert.Equal(new[] { "mine", result.GraphId }, target.expressionGraphs!.Select(g => g.id));
            // The existing default stays.
            Assert.Equal("mine", target.defaultExpressionGraphs![Renderers.CLASSIC]);
            Assert.Equal(new[] { typeof(ConfigureExpressionsCommand), typeof(SetExpressionGraphsCommand) },
                Edits().Select(c => c.GetType()));
        }

        [Fact]
        public void ImportBecomesTheDefaultWhenTheRendererHasNone() {
            var file = ExpressionGraphFile.Create(SourceProject(), Graph());
            var target = DocManager.Inst.Project;
            target.RegisterExpression(Breath());

            var result = file.ImportInto(target);

            Assert.Empty(result.AddedExpressions);
            Assert.Empty(result.ConflictingExpressions);
            Assert.Equal("mine", result.GraphId);
            Assert.Equal("mine", target.defaultExpressionGraphs![Renderers.CLASSIC]);
            // Nothing to add to the expressions, so only the graph edit ran.
            Assert.IsType<SetExpressionGraphsCommand>(Assert.Single(Edits()));
        }

        [Fact]
        public void TemplatesKeepGraphsAndDefaults() {
            var project = SourceProject();
            project.defaultExpressionGraphs = new Dictionary<string, string> { [Renderers.CLASSIC] = "mine" };
            project.tracks[0].ExpressionGraph = "mine";

            var template = project.CloneAsTemplate();

            Assert.Equal("mine", Assert.Single(template.expressionGraphs!).id);
            Assert.NotSame(project.expressionGraphs![0], template.expressionGraphs![0]);
            Assert.Equal("mine", template.defaultExpressionGraphs![Renderers.CLASSIC]);
            Assert.True(template.expressions.ContainsKey(Custom));
            // The template's track is new, so it has no override.
            Assert.Null(template.tracks[0].ExpressionGraph);
        }
    }
}
