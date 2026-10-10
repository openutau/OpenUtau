using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls {
    /// <summary>The project's expression graph library, with a node editor for the selected graph.</summary>
    public partial class ExpressionGraphEditor : UserControl, ICmdSubscriber {
        static readonly string[] renderers = {
            // One slot per graph family: the Worldline-R variants use Worldline-R's.
            Renderers.CLASSIC, Renderers.WORLDLINE_R, Renderers.ENUNU,
            Renderers.VOGEN, Renderers.DIFFSINGER, Renderers.VOICEVOX,
        };

        string? selectedId;
        bool refreshing;

        public ExpressionGraphEditor() {
            InitializeComponent();
            Canvas.Refused += message => Status.Text = message;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            DocManager.Inst.AddSubscriber(this);
            Refresh();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            DocManager.Inst.RemoveSubscriber(this);
        }

        static UProject Project => DocManager.Inst.Project;

        sealed class GraphItem {
            public readonly string Id;
            readonly string label;
            public GraphItem(string id, string label) {
                Id = id;
                this.label = label;
            }
            public override string ToString() => label;
        }

        UExpressionGraph? SelectedGraph => Project.expressionGraphs?.FirstOrDefault(g => g.id == selectedId);

        void Refresh() {
            refreshing = true;
            try {
                var graphs = Project.expressionGraphs ?? new List<UExpressionGraph>();
                var items = graphs.Select(g => new GraphItem(g.id, $"{g.name ?? g.id}  ·  {g.renderer}")).ToList();
                if (selectedId == null || items.All(i => i.Id != selectedId)) {
                    selectedId = items.FirstOrDefault()?.Id;
                }
                GraphList.ItemsSource = items;
                GraphList.SelectedItem = items.FirstOrDefault(i => i.Id == selectedId);

                var graph = SelectedGraph;
                GraphSettings.IsEnabled = DuplicateButton.IsEnabled = DeleteButton.IsEnabled = ExportButton.IsEnabled
                    = graph != null;
                NameBox.Text = graph?.name ?? string.Empty;
                RendererText.Text = graph == null ? string.Empty
                    : $"{ThemeManager.GetString("expressiongraph.renderer")}: {graph.renderer}";
                PitchCurveBox.SelectedIndex = graph?.preferredPitchCurve == Core.Format.Ustx.PITO ? 1 : 0;
                DefaultBox.IsChecked = graph?.renderer != null
                    && Project.defaultExpressionGraphs?.TryGetValue(Renderers.GetExpressionGraphSlot(graph.renderer), out var id) == true
                    && id == graph.id;
                Canvas.Show(Project, graph);
                Hint.Text = ThemeManager.GetString(graph == null ? "expressiongraph.hint.nograph" : "expressiongraph.hint");
                Hint.IsVisible = graph == null || graph.nodes.Count == 0;
                Status.Text = graph == null ? string.Empty : Describe(graph);
            } finally {
                refreshing = false;
            }
        }

        /// <summary>Why the graph won't run, or which tracks use it.</summary>
        static string Describe(UExpressionGraph graph) {
            if (ExpressionGraphProgram.Compile(graph, out var error) == null) {
                return $"{ThemeManager.GetString("expressiongraph.error")}: {error}";
            }
            var tracks = Project.tracks
                .Where(t => ExpressionGraphProgram.GetEffectiveGraph(Project, t)?.id == graph.id)
                .Select(t => t.TrackName)
                .ToList();
            return tracks.Count == 0
                ? ThemeManager.GetString("expressiongraph.unused")
                : $"{ThemeManager.GetString("expressiongraph.usedby")}: {string.Join(", ", tracks)}";
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SetExpressionGraphsCommand || cmd is LoadProjectNotification
                    || cmd is ConfigureExpressionsCommand || cmd is TrackCommand) {
                Dispatcher.UIThread.Post(Refresh);
            }
        }

        void OnGraphSelected(object? sender, SelectionChangedEventArgs e) {
            if (refreshing || GraphList.SelectedItem is not GraphItem item || item.Id == selectedId) {
                return;
            }
            selectedId = item.Id;
            Refresh();
        }

        void OnNew(object? sender, RoutedEventArgs e) {
            var menu = new ContextMenu();
            // The renderers the project's tracks use first.
            var used = Project.tracks.Select(t => t.RendererSettings?.renderer).Where(r => r != null)
                .Select(r => Renderers.GetExpressionGraphSlot(r!)).Distinct().ToList();
            foreach (var renderer in used.Concat(renderers.Where(r => !used.Contains(r)))) {
                var item = new MenuItem { Header = renderer };
                item.Click += (s, args) => Create(renderer!);
                menu.Items.Add(item);
            }
            menu.Open(NewButton);
        }

        void Create(string renderer) {
            string? id = null;
            ExpressionGraphEdits.Apply(Project, draft => {
                string name = $"{renderer} {ThemeManager.GetString("expressiongraph.graph")}";
                id = draft.NewId(name);
                draft.Graphs.Add(ExpressionGraphEdits.CreateDefault(id, name, renderer));
                // The first graph for a renderer becomes its default.
                draft.Defaults.TryAdd(renderer, id);
            });
            selectedId = id;
            Refresh();
        }

        void OnDuplicate(object? sender, RoutedEventArgs e) {
            var graph = SelectedGraph;
            if (graph == null) {
                return;
            }
            string? id = null;
            ExpressionGraphEdits.Apply(Project, draft => {
                var copy = graph.Clone();
                copy.name = $"{graph.name ?? graph.id} ({ThemeManager.GetString("expressiongraph.copy")})";
                copy.id = id = draft.NewId(copy.name);
                draft.Graphs.Add(copy);
            });
            selectedId = id;
            Refresh();
        }

        void OnDelete(object? sender, RoutedEventArgs e) {
            if (selectedId is string id) {
                ExpressionGraphEdits.Apply(Project, draft => draft.Remove(id));
            }
        }

        /// <summary>Saves the selected graph with the definitions of the expressions it uses.</summary>
        async void OnExport(object? sender, RoutedEventArgs e) {
            var graph = SelectedGraph;
            if (graph == null || TopLevel.GetTopLevel(this) is not Window window) {
                return;
            }
            var path = await FilePicker.SaveFile(window, "expressiongraph.export", null,
                $"{graph.name ?? graph.id}.ougraph", FilePicker.ExpressionGraph);
            if (path == null) {
                return;
            }
            try {
                ExpressionGraphFile.Create(Project, graph).Save(path);
            } catch (Exception ex) {
                _ = MessageBox.ShowError(window, ex);
            }
        }

        /// <summary>
        /// Adds a graph from a file, with the expressions it brings that the project lacks, and says which
        /// expressions it added and which the project already defines differently.
        /// </summary>
        async void OnImport(object? sender, RoutedEventArgs e) {
            if (TopLevel.GetTopLevel(this) is not Window window) {
                return;
            }
            var path = await FilePicker.OpenFile(window, "expressiongraph.import", FilePicker.ExpressionGraph);
            if (path == null) {
                return;
            }
            ExpressionGraphFile.ImportResult result;
            try {
                result = ExpressionGraphFile.Load(path).ImportInto(Project);
            } catch (Exception ex) {
                _ = MessageBox.ShowError(window, ex);
                return;
            }
            selectedId = result.GraphId;
            Refresh();
            var lines = new List<string>();
            if (result.AddedExpressions.Count > 0) {
                lines.Add($"{ThemeManager.GetString("expressiongraph.import.added")}: "
                    + string.Join(", ", result.AddedExpressions.Select(a => a.ToUpperInvariant())));
            }
            if (result.ConflictingExpressions.Count > 0) {
                lines.Add($"{ThemeManager.GetString("expressiongraph.import.conflicts")}: "
                    + string.Join(", ", result.ConflictingExpressions.Select(a => a.ToUpperInvariant())));
            }
            if (lines.Count > 0) {
                _ = MessageBox.Show(window, string.Join("\n\n", lines),
                    ThemeManager.GetString("expressiongraph.import"), MessageBox.MessageBoxButtons.Ok);
            }
        }

        void OnNameCommitted(object? sender, RoutedEventArgs e) {
            var graph = SelectedGraph;
            var name = NameBox.Text?.Trim();
            if (refreshing || graph == null || string.IsNullOrEmpty(name) || name == graph.name) {
                return;
            }
            ExpressionGraphEdits.Apply(Project, draft => {
                var target = draft.Find(graph.id);
                if (target != null) {
                    target.name = name;
                }
            });
        }

        void OnNameKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) {
                OnNameCommitted(sender, e);
                e.Handled = true;
            }
        }

        /// <summary>Where the piano roll draws pitch and Load rendered pitch writes, for tracks using this graph.</summary>
        void OnPitchCurveChanged(object? sender, SelectionChangedEventArgs e) {
            var graph = SelectedGraph;
            if (refreshing || graph == null) {
                return;
            }
            string? preferred = PitchCurveBox.SelectedIndex == 1 ? Core.Format.Ustx.PITO : null;
            if (preferred == graph.preferredPitchCurve) {
                return;
            }
            ExpressionGraphEdits.Apply(Project, draft => {
                var target = draft.Find(graph.id);
                if (target != null) {
                    target.preferredPitchCurve = preferred;
                }
            });
        }

        void OnDefaultChanged(object? sender, RoutedEventArgs e) {
            var graph = SelectedGraph;
            if (refreshing || graph?.renderer == null) {
                return;
            }
            bool isDefault = DefaultBox.IsChecked == true;
            // Defaults are per slot: a Worldline-R2 graph is the default of Worldline-R's slot.
            string slot = Renderers.GetExpressionGraphSlot(graph.renderer);
            ExpressionGraphEdits.Apply(Project, draft => {
                if (isDefault) {
                    draft.Defaults[slot] = graph.id;
                } else if (draft.Defaults.TryGetValue(slot, out var id) && id == graph.id) {
                    draft.Defaults.Remove(slot);
                }
            });
        }

        void OnKeyDown(object? sender, KeyEventArgs e) {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) {
                return;
            }
            var modifiers = e.KeyModifiers & ~KeyModifiers.Shift;
            if (modifiers == KeyModifiers.Control || modifiers == KeyModifiers.Meta) {
                if (e.Key == Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                    DocManager.Inst.Undo();
                    e.Handled = true;
                } else if (e.Key == Key.Y || e.Key == Key.Z) {
                    DocManager.Inst.Redo();
                    e.Handled = true;
                }
            } else if (e.Key == Key.Delete || e.Key == Key.Back) {
                Canvas.DeleteSelected();
                e.Handled = true;
            }
        }
    }
}
