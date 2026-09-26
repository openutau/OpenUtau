using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Edits one expression graph as nodes on a pannable canvas. It shows a copy of the graph; every change goes
    /// through <see cref="ExpressionGraphEdits.Apply"/> as one undoable command, and the owner calls
    /// <see cref="Show"/> again when the document changes.
    /// </summary>
    public class ExpressionGraphCanvas : UserControl {
        // Nodes size to their contents: linked inputs hide their fields, so a fully linked node is narrow.
        const double NodeMinWidth = 80;
        const double NumberWidth = 48;
        const double ExpressionPickerWidth = 150;

        /// <summary>
        /// A dropdown wide enough for its longest option, so the node keeps its size whichever is selected.
        /// </summary>
        static double ChoiceWidth(IEnumerable<string> options) {
            double text = options.Select(option => {
                var block = new TextBlock { Text = option };
                block.Measure(Size.Infinity);
                return block.DesiredSize.Width;
            }).DefaultIfEmpty(0).Max();
            // Room for the arrow and the padding.
            return Math.Ceiling(text) + 40;
        }
        const double DotSize = 10;

        readonly Panel host;
        readonly Canvas nodeLayer;
        readonly LinkLayer linkLayer;
        readonly TranslateTransform pan = new TranslateTransform();

        UProject? project;
        UExpressionGraph? graph;
        readonly Dictionary<(int node, string port), Ellipse> inputDots = new Dictionary<(int, string), Ellipse>();
        readonly Dictionary<(int node, int output), Ellipse> outputDots = new Dictionary<(int, int), Ellipse>();
        readonly Dictionary<int, Border> nodeViews = new Dictionary<int, Border>();

        public int? SelectedNode { get; private set; }

        /// <summary>A message about the last edit that couldn't be made, e.g. a link closing a cycle.</summary>
        public event Action<string>? Refused;

        public ExpressionGraphCanvas() {
            Focusable = true;
            linkLayer = new LinkLayer(this) { RenderTransform = pan, IsHitTestVisible = false };
            nodeLayer = new Canvas { RenderTransform = pan };
            host = new Panel {
                ClipToBounds = true,
                Background = Brushes.Transparent,
                Children = { linkLayer, nodeLayer },
            };
            host.PointerPressed += OnHostPointerPressed;
            host.PointerMoved += OnPointerMoved;
            host.PointerReleased += OnPointerReleased;
            Content = host;
        }

        /// <summary>Shows a graph of the project, or nothing. Keeps the view's panning.</summary>
        public void Show(UProject? project, UExpressionGraph? graph) {
            this.project = project;
            this.graph = graph?.Clone();
            if (SelectedNode != null && this.graph?.nodes.Any(n => n.id == SelectedNode) != true) {
                SelectedNode = null;
            }
            Rebuild();
        }

        void Rebuild() {
            nodeLayer.Children.Clear();
            inputDots.Clear();
            outputDots.Clear();
            nodeViews.Clear();
            if (graph != null && project != null) {
                foreach (var node in graph.nodes) {
                    var view = BuildNode(node);
                    Canvas.SetLeft(view, node.x);
                    Canvas.SetTop(view, node.y);
                    nodeLayer.Children.Add(view);
                    nodeViews[node.id] = view;
                }
            }
            linkLayer.InvalidateVisual();
            LayoutUpdated -= OnLayoutUpdated;
            LayoutUpdated += OnLayoutUpdated;
        }

        void OnLayoutUpdated(object? sender, EventArgs e) {
            LayoutUpdated -= OnLayoutUpdated;
            linkLayer.InvalidateVisual();
        }

        void Edit(Action<UExpressionGraph> change) {
            if (project == null || graph == null) {
                return;
            }
            string id = graph.id;
            ExpressionGraphEdits.Apply(project, draft => {
                var target = draft.Find(id);
                if (target != null) {
                    change(target);
                }
            });
        }

        public void DeleteSelected() {
            if (SelectedNode is int id) {
                Edit(g => ExpressionGraphEdits.RemoveNode(g, id));
            }
        }

        static string Text(string key, string fallback) =>
            ThemeManager.TryGetString(key, out var value) ? value : fallback;

        static string Humanize(string name) {
            var text = name.Replace('_', ' ');
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        public static string NodeTitle(string? type) =>
            Text($"expressiongraph.node.{type}", Humanize(type ?? "?"));

        #region Nodes

        Border BuildNode(UGraphNode node) {
            GraphNodeTypes.TryGet(node.type, out var type);
            var body = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4) };
            var title = new TextBlock {
                Text = NodeTitle(node.type),
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(6, 3),
            };
            var header = new Border {
                Background = (IBrush?)FindBrush(type?.IsOutput == true ? "SelectedTrackAccentBrush" : "NeutralAccentBrush"),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Child = title,
                Cursor = new Cursor(StandardCursorType.SizeAll),
            };
            header.PointerPressed += (s, e) => BeginNodeDrag(node, e);
            var delete = new MenuItem { Header = Text("expressiongraph.deletenode", "Delete") };
            delete.Click += (s, e) => Edit(g => ExpressionGraphEdits.RemoveNode(g, node.id));
            header.ContextMenu = new ContextMenu { Items = { delete } };
            body.Children.Add(header);

            if (type == null) {
                body.Children.Add(new TextBlock {
                    Text = Text("expressiongraph.unknownnode", "Unknown node: this version can't run it."),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 2),
                });
            } else {
                // Outputs sit on the first rows, beside the inputs if there are any.
                var outputs = type.Outputs
                    .Select((name, k) => BuildOutput(node, k, type.Outputs.Length > 1 ? Humanize(name) : Text("expressiongraph.output", "Out")))
                    .ToArray();
                bool fieldsBesideOutput = outputs.Length > 0 && type.Ports.Length > 0;
                // One grid for every row, so labels and fields line up: input dot, label, field, output.
                // The first column fits a half-overhanging input dot, and indents nodes without inputs the same.
                var rows = new Grid { ColumnDefinitions = new ColumnDefinitions($"{DotSize - 1},Auto,*,Auto") };
                void AddRow(Control? input, Control? label, Control? field, Control? output) {
                    int row = rows.RowDefinitions.Count;
                    rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    void Place(Control? control, int column, int span = 1) {
                        if (control != null) {
                            Grid.SetRow(control, row);
                            Grid.SetColumn(control, column);
                            Grid.SetColumnSpan(control, span);
                            rows.Children.Add(control);
                        }
                    }
                    Place(input, 0);
                    Place(label, 1);
                    // A field without a label takes the label's column too. Fields stop short of the output's
                    // column only when an input field shares a row with the output, so that all fields line up.
                    int first = label == null ? 1 : 2;
                    int last = output != null || fieldsBesideOutput ? 2 : 3;
                    Place(field, first, last - first + 1);
                    Place(output, 3);
                }
                for (int i = 0; i < Math.Max(type.Ports.Length, outputs.Length); ++i) {
                    var output = i < outputs.Length ? outputs[i] : null;
                    if (i < type.Ports.Length) {
                        var (dot, label, field) = BuildPort(node, type.Ports[i], type.PortDefaults[i]);
                        AddRow(dot, label, field, output);
                    } else {
                        AddRow(null, null, null, output);
                    }
                }
                foreach (var parameter in GraphNodeTypes.ParametersOf(node.type)) {
                    var (label, field) = BuildParameter(node, parameter);
                    AddRow(null, label, field, null);
                }
                body.Children.Add(rows);
            }

            var view = new Border {
                MinWidth = NodeMinWidth,
                CornerRadius = new CornerRadius(4),
                // Selection changes the colour only, so the node doesn't shift.
                BorderThickness = new Thickness(1),
                BorderBrush = (IBrush?)FindBrush(node.id == SelectedNode
                    ? "SelectedTrackAccentBrush" : "SystemControlForegroundBaseMediumLowBrush") ?? Brushes.Gray,
                Background = (IBrush?)FindBrush("SystemControlBackgroundAltHighBrush"),
                Child = body,
            };
            ToolTip.SetTip(view, Problem(node, type));
            if (Problem(node, type) != null) {
                header.Background = (IBrush?)FindBrush("WarningBrush");
            }
            return view;
        }

        /// <summary>Why a node won't do anything, if it won't.</summary>
        string? Problem(UGraphNode node, GraphNodeType? type) {
            if (type == null || project == null || graph == null || !type.NeedsAbbr) {
                return null;
            }
            var abbr = node.GetString("abbr");
            if (string.IsNullOrEmpty(abbr)) {
                return Text("expressiongraph.noexpression", "Choose an expression.");
            }
            if (!GraphNodeTypes.ExpressionChoices(project, node.type, graph.renderer).Any(d => d.abbr == abbr)) {
                return Text("expressiongraph.unavailable", "Not available for this graph's renderer.");
            }
            return null;
        }

        object? FindBrush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value : null;

        Ellipse Dot() => new Ellipse {
            Width = DotSize,
            Height = DotSize,
            Fill = (IBrush?)FindBrush("SystemControlForegroundBaseMediumBrush") ?? Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        /// <summary>An input port's dot, label and, while nothing is linked to it, its fallback value.</summary>
        (Control dot, Control label, Control? field) BuildPort(UGraphNode node, string port, float fallback) {
            var dot = Dot();
            dot.Margin = new Thickness(-DotSize / 2, 0, 4, 0);
            dot.PointerPressed += (s, e) => BeginRelink(node.id, port, e);
            inputDots[(node.id, port)] = dot;
            bool linked = graph!.links.Any(l => l.to == node.id && l.toPort == port);
            // Output nodes' only port is what they output.
            GraphNodeTypes.TryGet(node.type, out var type);
            var label = Label(type?.IsOutput == true ? Text("expressiongraph.input", "In") : Humanize(port));
            var field = linked ? null
                : NumberBox(node, port, node.GetString(port) ?? fallback.ToString(CultureInfo.InvariantCulture), allowEmpty: false);
            return (dot, label, field);
        }

        static TextBlock Label(string text) => new TextBlock {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        Control BuildOutput(UGraphNode node, int output, string label) {
            var dot = Dot();
            dot.Margin = new Thickness(4, 0, -DotSize / 2, 0);
            dot.PointerPressed += (s, e) => BeginLink(node.id, output, e);
            outputDots[(node.id, output)] = dot;
            return new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = {
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                    dot,
                },
            };
        }

        /// <summary>A parameter's label and field. The expression picker has no label: the node's title says what it is.</summary>
        (Control? label, Control field) BuildParameter(UGraphNode node, GraphNodeParameter parameter) {
            Control editor;
            string? value = node.GetString(parameter.Name);
            switch (parameter.Kind) {
                case GraphParameterKind.Bool: {
                        var box = new CheckBox { IsChecked = value is "true" or "True" or "1" };
                        box.IsCheckedChanged += (s, e) =>
                            Edit(g => Find(g, node.id)?.Set(parameter.Name, box.IsChecked == true ? "true" : "false"));
                        editor = box;
                        break;
                    }
                case GraphParameterKind.Choice: {
                        var box = new ComboBox {
                            MinWidth = ChoiceWidth(parameter.Options),
                            ItemsSource = parameter.Options,
                            SelectedItem = value ?? parameter.Default,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                        };
                        box.SelectionChanged += (s, e) => {
                            if (box.SelectedItem is string selected && selected != (value ?? parameter.Default)) {
                                Edit(g => Find(g, node.id)?.Set(parameter.Name, selected));
                            }
                        };
                        editor = box;
                        break;
                    }
                case GraphParameterKind.Expression: {
                        var choices = GraphNodeTypes.ExpressionChoices(project!, node.type, graph!.renderer)
                            .Select(d => new ExpressionChoice(d.abbr, $"{d.abbr.ToUpperInvariant()}  {d.name}"))
                            .ToList();
                        if (!string.IsNullOrEmpty(value) && choices.All(c => c.Abbr != value)) {
                            choices.Insert(0, new ExpressionChoice(value, $"{value.ToUpperInvariant()}  ({Text("expressiongraph.missing", "unavailable")})"));
                        }
                        var box = new ComboBox {
                            Width = ExpressionPickerWidth,
                            ItemsSource = choices,
                            SelectedItem = choices.FirstOrDefault(c => c.Abbr == value),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                        };
                        box.SelectionChanged += (s, e) => {
                            if (box.SelectedItem is ExpressionChoice selected && selected.Abbr != value) {
                                Edit(g => Find(g, node.id)?.Set(parameter.Name, selected.Abbr));
                            }
                        };
                        editor = box;
                        break;
                    }
                case GraphParameterKind.Number:
                    editor = NumberBox(node, parameter.Name, value ?? string.Empty, allowEmpty: parameter.Default == null);
                    break;
                default: {
                        var box = new TextBox { Text = value ?? string.Empty, MinWidth = 0 };
                        void Commit() {
                            if (box.Text != (value ?? string.Empty)) {
                                Edit(g => Find(g, node.id)?.Set(parameter.Name, box.Text ?? string.Empty));
                            }
                        }
                        box.LostFocus += (s, e) => Commit();
                        box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
                        editor = box;
                        break;
                    }
            }
            editor.Margin = new Thickness(0, 1, 6, 1);
            editor.HorizontalAlignment = parameter.Kind == GraphParameterKind.Bool ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            if (!parameter.Labeled) {
                return (null, editor);
            }
            return (Label(Text($"expressiongraph.param.{parameter.Name}", Humanize(parameter.Name))), editor);
        }

        /// <summary>A number field; an empty one removes the parameter when that's allowed (e.g. an open clamp bound).</summary>
        TextBox NumberBox(UGraphNode node, string name, string text, bool allowEmpty) {
            var box = new TextBox {
                Text = text,
                MinWidth = NumberWidth,
                MinHeight = 0,
                Padding = new Thickness(4, 1),
                Margin = new Thickness(0, 1, 6, 1),
            };
            void Commit() {
                var input = (box.Text ?? string.Empty).Trim();
                if (input == text) {
                    return;
                }
                if (input.Length == 0 && allowEmpty) {
                    Edit(g => Find(g, node.id)?.parameters.Remove(name));
                } else if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) {
                    Edit(g => Find(g, node.id)?.Set(name, number));
                } else {
                    box.Text = text;
                }
            }
            box.LostFocus += (s, e) => Commit();
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            return box;
        }

        static UGraphNode? Find(UExpressionGraph g, int id) => g.nodes.FirstOrDefault(n => n.id == id);

        sealed class ExpressionChoice {
            public readonly string Abbr;
            readonly string label;
            public ExpressionChoice(string abbr, string label) {
                Abbr = abbr;
                this.label = label;
            }
            public override string ToString() => label;
        }

        #endregion

        #region Pointer

        enum DragKind { None, Pan, Node, Link }
        DragKind drag;
        Point dragStart;
        Vector panStart;
        UGraphNode? dragNode;
        Point nodeStart;
        int linkFrom;
        int linkFromOutput;
        UGraphLink? detached;
        internal Point? PendingLinkEnd { get; private set; }
        internal (int node, int output) PendingLinkFrom => (linkFrom, linkFromOutput);

        /// <summary>The output a link leaves, by index.</summary>
        int OutputIndexOf(UGraphLink link) {
            var node = graph?.nodes.FirstOrDefault(n => n.id == link.from);
            return node != null && GraphNodeTypes.TryGet(node.type, out var type) ? Math.Max(0, type.OutputIndex(link.fromPort)) : 0;
        }

        Point CanvasPoint(PointerEventArgs e) => e.GetPosition(nodeLayer);

        void Select(int? id) {
            if (SelectedNode == id) {
                return;
            }
            SelectedNode = id;
            Rebuild();
        }

        void OnHostPointerPressed(object? sender, PointerPressedEventArgs e) {
            // Only the empty background pans or adds nodes.
            if (e.Handled || (e.Source != host && e.Source != nodeLayer)) {
                return;
            }
            Focus();
            var point = e.GetCurrentPoint(host);
            if (point.Properties.IsRightButtonPressed) {
                ShowAddMenu(CanvasPoint(e));
                e.Handled = true;
                return;
            }
            Select(null);
            drag = DragKind.Pan;
            dragStart = point.Position;
            panStart = new Vector(pan.X, pan.Y);
            e.Pointer.Capture(host);
            e.Handled = true;
        }

        void BeginNodeDrag(UGraphNode node, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            Focus();
            Select(node.id);
            drag = DragKind.Node;
            dragNode = graph!.nodes.First(n => n.id == node.id);
            dragStart = e.GetPosition(host);
            nodeStart = new Point(dragNode.x, dragNode.y);
            e.Pointer.Capture(host);
            e.Handled = true;
        }

        void BeginLink(int from, int output, PointerPressedEventArgs e) {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            drag = DragKind.Link;
            linkFrom = from;
            linkFromOutput = output;
            detached = null;
            PendingLinkEnd = CanvasPoint(e);
            e.Pointer.Capture(host);
            e.Handled = true;
            linkLayer.InvalidateVisual();
        }

        /// <summary>Pressing a linked input picks its link up; dropping it elsewhere removes it.</summary>
        void BeginRelink(int to, string port, PointerPressedEventArgs e) {
            var link = graph!.links.FirstOrDefault(l => l.to == to && l.toPort == port);
            if (link == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
                return;
            }
            graph.links.Remove(link);
            detached = link;
            drag = DragKind.Link;
            linkFrom = link.from;
            linkFromOutput = OutputIndexOf(link);
            PendingLinkEnd = CanvasPoint(e);
            e.Pointer.Capture(host);
            e.Handled = true;
            linkLayer.InvalidateVisual();
        }

        void OnPointerMoved(object? sender, PointerEventArgs e) {
            switch (drag) {
                case DragKind.Pan: {
                        var delta = e.GetPosition(host) - dragStart;
                        pan.X = panStart.X + delta.X;
                        pan.Y = panStart.Y + delta.Y;
                        break;
                    }
                case DragKind.Node when dragNode != null: {
                        var delta = e.GetPosition(host) - dragStart;
                        dragNode.x = (float)Math.Round(nodeStart.X + delta.X);
                        dragNode.y = (float)Math.Round(nodeStart.Y + delta.Y);
                        if (nodeViews.TryGetValue(dragNode.id, out var view)) {
                            Canvas.SetLeft(view, dragNode.x);
                            Canvas.SetTop(view, dragNode.y);
                        }
                        linkLayer.InvalidateVisual();
                        break;
                    }
                case DragKind.Link:
                    PendingLinkEnd = CanvasPoint(e);
                    linkLayer.InvalidateVisual();
                    break;
            }
        }

        void OnPointerReleased(object? sender, PointerReleasedEventArgs e) {
            var kind = drag;
            drag = DragKind.None;
            e.Pointer.Capture(null);
            switch (kind) {
                case DragKind.Node when dragNode != null: {
                        var node = dragNode;
                        dragNode = null;
                        if (node.x != nodeStart.X || node.y != nodeStart.Y) {
                            float x = node.x, y = node.y;
                            Edit(g => {
                                var target = Find(g, node.id);
                                if (target != null) {
                                    target.x = x;
                                    target.y = y;
                                }
                            });
                        }
                        break;
                    }
                case DragKind.Link: {
                        var end = CanvasPoint(e);
                        PendingLinkEnd = null;
                        var target = inputDots.FirstOrDefault(kv => Contains(kv.Value, end));
                        var removed = detached;
                        detached = null;
                        int from = linkFrom;
                        var fromNode = graph!.nodes.FirstOrDefault(n => n.id == from);
                        string? fromPort = fromNode != null && GraphNodeTypes.TryGet(fromNode.type, out var fromType)
                            && linkFromOutput < fromType.Outputs.Length ? fromType.Outputs[linkFromOutput] : null;
                        if (target.Value != null) {
                            var (to, port) = target.Key;
                            if (removed != null && removed.to == to && removed.toPort == port) {
                                graph!.links.Add(removed);
                                linkLayer.InvalidateVisual();
                                break;
                            }
                            if (ExpressionGraphProgram.WouldCreateCycle(graph!, from, to)) {
                                Refused?.Invoke(Text("expressiongraph.cycle", "That link would make a loop."));
                                if (removed != null) {
                                    graph!.links.Add(removed);
                                }
                                linkLayer.InvalidateVisual();
                                break;
                            }
                            Edit(g => {
                                if (removed != null) {
                                    g.links.RemoveAll(l => l.to == removed.to && l.toPort == removed.toPort);
                                }
                                ExpressionGraphEdits.TryLink(g, from, to, port, fromPort);
                            });
                        } else if (removed != null) {
                            Edit(g => g.links.RemoveAll(l => l.to == removed.to && l.toPort == removed.toPort));
                        } else {
                            linkLayer.InvalidateVisual();
                        }
                        break;
                    }
            }
        }

        bool Contains(Control dot, Point point) {
            var center = DotCenter(dot);
            return center != null && Math.Abs(point.X - center.Value.X) <= DotSize && Math.Abs(point.Y - center.Value.Y) <= DotSize;
        }

        void ShowAddMenu(Point at) {
            if (graph == null) {
                return;
            }
            var menu = new ContextMenu();
            foreach (var (category, types) in GraphNodeTypes.Categories) {
                var group = new MenuItem { Header = Text($"expressiongraph.category.{category}", Humanize(category)) };
                foreach (var type in types) {
                    var item = new MenuItem { Header = NodeTitle(type) };
                    item.Click += (s, e) => Edit(g => ExpressionGraphEdits.AddNode(g, type, (float)Math.Round(at.X), (float)Math.Round(at.Y)));
                    group.Items.Add(item);
                }
                menu.Items.Add(group);
            }
            menu.Open(host);
        }

        #endregion

        #region Links

        /// <summary>The center of a port's dot, in node-layer coordinates.</summary>
        Point? DotCenter(Control dot) =>
            dot.TranslatePoint(new Point(dot.Bounds.Width / 2, dot.Bounds.Height / 2), nodeLayer);

        sealed class LinkLayer : Control {
            readonly ExpressionGraphCanvas owner;

            public LinkLayer(ExpressionGraphCanvas owner) {
                this.owner = owner;
            }

            public override void Render(DrawingContext context) {
                var graph = owner.graph;
                if (graph == null) {
                    return;
                }
                var brush = (IBrush?)owner.FindBrush("SystemControlForegroundBaseMediumBrush") ?? Brushes.Gray;
                var pen = new Pen(brush, 2);
                foreach (var link in graph.links) {
                    if (owner.outputDots.TryGetValue((link.from, owner.OutputIndexOf(link)), out var fromDot)
                            && link.toPort != null && owner.inputDots.TryGetValue((link.to, link.toPort), out var toDot)
                            && owner.DotCenter(fromDot) is Point a && owner.DotCenter(toDot) is Point b) {
                        DrawLink(context, pen, a, b);
                    }
                }
                if (owner.PendingLinkEnd is Point end && owner.outputDots.TryGetValue(owner.PendingLinkFrom, out var dot)
                        && owner.DotCenter(dot) is Point start) {
                    DrawLink(context, new Pen(brush, 2, new DashStyle(new double[] { 3, 2 }, 0)), start, end);
                }
            }

            static void DrawLink(DrawingContext context, Pen pen, Point a, Point b) {
                double bend = Math.Max(40, Math.Abs(b.X - a.X) / 2);
                var geometry = new StreamGeometry();
                using (var g = geometry.Open()) {
                    g.BeginFigure(a, false);
                    g.CubicBezierTo(new Point(a.X + bend, a.Y), new Point(b.X - bend, b.Y), b);
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, pen, geometry);
            }
        }

        #endregion
    }
}
