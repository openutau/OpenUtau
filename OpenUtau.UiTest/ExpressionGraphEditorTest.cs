using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.UiTest {
    public class ExpressionGraphEditorTest {
        // Node views, left to right.
        static Border[] Nodes(ExpressionGraphCanvas canvas) => canvas.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Parent is Canvas)
            .ToArray();

        // A node view by its title, the first text in it.
        static Border Node(ExpressionGraphCanvas canvas, string title) =>
            Nodes(canvas).Single(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text == title);

        static Ellipse[] Dots(ExpressionGraphCanvas canvas, string title) =>
            Node(canvas, title).GetVisualDescendants().OfType<Ellipse>().ToArray();

        static Point Center(Control control, TopLevel window) =>
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        static void Drag(Window window, Point from, Point to) {
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
            HeadlessUi.Flush();
            window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            HeadlessUi.Flush();
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
            HeadlessUi.Flush();
        }

        [Fact]
        public void EditsGraphsWithThePointerAndUndoes() => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            HeadlessUi.Errors.Clear();
            var original = DocManager.Inst.Project;
            var project = Core.Format.Ustx.Create();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var window = new ExpressionsDialog { Width = 1000, Height = 640 };
            try {
                window.Show();
                HeadlessUi.Flush();
                HeadlessUi.SaveScreenshot(window, "ExpressionsTab");
                // The graphs share the expressions window, on their own tab.
                window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("GraphsTab");
                HeadlessUi.Flush();
                var editor = window.FindControl<ExpressionGraphEditor>("GraphEditor")!;
                var canvas = editor.FindControl<ExpressionGraphCanvas>("Canvas")!;
                var status = editor.FindControl<TextBlock>("Status")!;
                Assert.Empty(Nodes(canvas));

                // A "power" graph for the track's renderer: dyn mapped to gender, not linked yet.
                ExpressionGraphEdits.Apply(project, draft => {
                    draft.Graphs.Add(new UExpressionGraph { id = "power", name = "Power", renderer = Renderers.WORLDLINE_R2 });
                    draft.Defaults[Renderers.WORLDLINE_R2] = "power";
                    var graph = draft.Find("power")!;
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.CurveInput, 20, 40).Set("abbr", "dyn");
                    var map = ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.MapRange, 260, 40);
                    map.Set("in_min", "-24");
                    map.Set("in_max", "12");
                    map.Set("out_min", "-100");
                    map.Set("out_max", "100");
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.CurveOutput, 520, 40).Set("abbr", "genc");
                    // Unlinked, to show each kind of layout: inputs beside the output, labelled parameters,
                    // and nodes without inputs.
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.Subtract, 540, 140);
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.Clamp, 20, 380);
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.Lfo, 540, 240);
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.PitchInput, 200, 380);
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.PhonemeInput, 200, 460).Set("abbr", "vel");
                    ExpressionGraphEdits.AddNode(graph, GraphNodeTypes.MaskedCurveInput, 20, 230).Set("abbr", "pito");
                });
                HeadlessUi.Flush();
                Assert.Equal(9, Nodes(canvas).Length);
                // The track has no singer, so no renderer, and uses no graph.
                Assert.Equal(ThemeManagerString("expressiongraph.unused"), status.Text);

                // Drag from the curve's output to the map's input, then from the map to the output node.
                Drag(window, Center(Dots(canvas, "Curve").Single(), window), Center(Dots(canvas, "Map Range").First(), window));
                Drag(window, Center(Dots(canvas, "Map Range").Last(), window), Center(Dots(canvas, "Curve Output").Single(), window));
                var stored = project.expressionGraphs!.Single();
                Assert.Equal(new[] { (1, 2), (2, 3) }, stored.links.Select(l => (l.from, l.to)).OrderBy(l => l));
                Assert.NotNull(ExpressionGraphProgram.Compile(stored, out _));

                // A node linked into itself would close a loop: refused.
                Drag(window, Center(Dots(canvas, "Map Range").Last(), window), Center(Dots(canvas, "Map Range").First(), window));
                Assert.Equal(2, project.expressionGraphs!.Single().links.Count);

                // A masked curve has a fallback input and two outputs; a link leaves the one it's dragged from.
                var masked = Dots(canvas, "Masked Curve");
                Assert.Equal(3, masked.Length);
                Drag(window, Center(masked.Last(), window), Center(Dots(canvas, "Subtract")[2], window));
                Assert.Contains(project.expressionGraphs!.Single().links,
                    l => l.from == 9 && l.fromPort == "mask" && l.to == 4 && l.toPort == "b");

                // Move the map node by its header.
                var header = Node(canvas, "Map Range").GetVisualDescendants().OfType<TextBlock>().First();
                var start = Center(header, window);
                Drag(window, start, start + new Point(30, 120));
                Assert.Equal((290f, 160f), (project.expressionGraphs!.Single().nodes[1].x, project.expressionGraphs!.Single().nodes[1].y));
                HeadlessUi.SaveScreenshot(window, nameof(EditsGraphsWithThePointerAndUndoes));

                // Undo the move and the three links; redo one.
                DocManager.Inst.Undo();
                DocManager.Inst.Undo();
                DocManager.Inst.Undo();
                DocManager.Inst.Undo();
                HeadlessUi.Flush();
                Assert.Empty(project.expressionGraphs!.Single().links);
                Assert.Equal(260f, project.expressionGraphs!.Single().nodes[1].x);
                DocManager.Inst.Redo();
                HeadlessUi.Flush();
                Assert.Single(project.expressionGraphs!.Single().links);
                Assert.Empty(HeadlessUi.Errors.Snapshot());
            } finally {
                window.Close();
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(original));
            }
        });

        [Fact]
        public void NewGraphsComeWithTheirPitch() => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            HeadlessUi.Errors.Clear();
            var original = DocManager.Inst.Project;
            var project = Core.Format.Ustx.Create();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            var window = new ExpressionsDialog { Width = 1100, Height = 640 };
            try {
                window.Show();
                window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("GraphsTab");
                HeadlessUi.Flush();
                var editor = window.FindControl<ExpressionGraphEditor>("GraphEditor")!;
                var canvas = editor.FindControl<ExpressionGraphCanvas>("Canvas")!;
                var status = editor.FindControl<TextBlock>("Status")!;
                // Without pitch rendering, then with it.
                foreach (var (renderer, nodes) in new[] { (Renderers.WORLDLINE_R2, 9), (Renderers.DIFFSINGER, 6) }) {
                    ExpressionGraphEdits.Apply(project, draft => {
                        draft.Graphs.Clear();
                        draft.Graphs.Add(ExpressionGraphEdits.CreateDefault("g", renderer, renderer));
                    });
                    HeadlessUi.Flush();
                    Assert.Equal(nodes, Nodes(canvas).Length);
                    Assert.Equal(ThemeManagerString("expressiongraph.unused"), status.Text);
                    Assert.Equal(1, editor.FindControl<ComboBox>("PitchCurveBox")!.SelectedIndex);
                    HeadlessUi.SaveScreenshot(window, $"DefaultGraph{renderer}");
                }
                Assert.Empty(HeadlessUi.Errors.Snapshot());
            } finally {
                window.Close();
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(original));
            }
        });

        [Fact]
        public void TrackSettingsPickTheTracksGraph() => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            HeadlessUi.Errors.Clear();
            var original = DocManager.Inst.Project;
            var project = Core.Format.Ustx.Create();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            ExpressionGraphEdits.Apply(project, draft => {
                draft.Graphs.Add(new UExpressionGraph { id = "a", name = "Graph A", renderer = Renderers.WORLDLINE_R2 });
                draft.Graphs.Add(new UExpressionGraph { id = "b", name = "Graph B", renderer = Renderers.WORLDLINE_R2 });
                draft.Graphs.Add(new UExpressionGraph { id = "c", name = "Graph C", renderer = Renderers.DIFFSINGER });
                draft.Defaults[Renderers.WORLDLINE_R2] = "a";
            });
            var track = project.tracks[0];
            track.RendererSettings.renderer = Renderers.WORLDLINE_R2;
            var dialog = new TrackSettingsDialog(track);
            try {
                dialog.Show();
                HeadlessUi.Flush();
                // The renderer's default, then the graphs made for this renderer only.
                var box = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.IsVisible);
                Assert.Equal(new[] { $"{ThemeManagerString("tracks.expressiongraph.default")} (Graph A)", "Graph A", "Graph B" },
                    box.Items.Select(i => i!.ToString()));
                Assert.Equal(0, box.SelectedIndex);
                HeadlessUi.SaveScreenshot(dialog, nameof(TrackSettingsPickTheTracksGraph));
                box.SelectedIndex = 2;
                dialog.GetVisualDescendants().OfType<Button>().Last().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                HeadlessUi.Flush();
                Assert.Equal("b", track.ExpressionGraph);
                DocManager.Inst.Undo();
                Assert.Null(track.ExpressionGraph);
                Assert.Empty(HeadlessUi.Errors.Snapshot());
            } finally {
                dialog.Close();
                DocManager.Inst.ExecuteCmd(new LoadProjectNotification(original));
            }
        });

        static string? ThemeManagerString(string key) => Application.Current!.FindResource(key) as string;
    }
}
