using System;
using System.Linq;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    partial class TrackSettingsViewModel : ViewModelBase {
        public UTrack Track { get; private set; }
        public ObservableCollectionExtended<IResampler> Resamplers => resamplers;
        [Reactive] public partial IResampler? Resampler { get; set; }
        [Reactive] public partial bool NeedsResampler { get; set; }
        public ObservableCollectionExtended<IWavtool> Wavtools => wavtools;
        [Reactive] public partial IWavtool? Wavtool { get; set; }
        [Reactive] public partial bool NeedsWavtool { get; set; }
        [Reactive] public partial bool HasRenderer { get; set; }
        /// <summary>The graphs the track can use: its renderer's default, or any graph made for its renderer.</summary>
        public ObservableCollectionExtended<GraphChoice> Graphs => graphs;
        [Reactive] public partial GraphChoice? Graph { get; set; }

        public sealed class GraphChoice {
            /// <summary>The graph's id; null for the renderer's default.</summary>
            public readonly string? Id;
            readonly string label;
            public GraphChoice(string? id, string label) {
                Id = id;
                this.label = label;
            }
            public override string ToString() => label;
        }

        ObservableCollectionExtended<IResampler> resamplers =
            new ObservableCollectionExtended<IResampler>();
        ObservableCollectionExtended<IWavtool> wavtools =
            new ObservableCollectionExtended<IWavtool>();
        ObservableCollectionExtended<GraphChoice> graphs =
            new ObservableCollectionExtended<GraphChoice>();

        public TrackSettingsViewModel(UTrack track) {
            ToolsManager.Inst.Initialize();
            Track = track;
            if (!string.IsNullOrEmpty(Track.RendererSettings.renderer)) {
                var renderer = Track.RendererSettings.renderer;
                resamplers.AddRange(ToolsManager.Inst.Resamplers);
                string? resamplerName = Track.RendererSettings.resampler;
                if (string.IsNullOrEmpty(resamplerName)) {
                    if (!Preferences.Default.DefaultResamplers.TryGetValue(renderer, out resamplerName)) {
                        resamplerName = string.Empty;
                    }
                }
                Resampler = ToolsManager.Inst.GetResampler(resamplerName);
                wavtools.AddRange(Renderers.GetSupportedWavtools(Resampler));
                string? wavtoolName = Track.RendererSettings.wavtool;
                if (string.IsNullOrEmpty(wavtoolName)) {
                    if (!Preferences.Default.DefaultWavtools.TryGetValue(renderer, out wavtoolName)) {
                        wavtoolName = string.Empty;
                    }
                }
                Wavtool = ToolsManager.Inst.GetWavtool(wavtoolName);
                NeedsResampler = Renderers.CLASSIC == renderer;
                NeedsWavtool = Renderers.CLASSIC == renderer;
                HasRenderer = true;

                var project = DocManager.Inst.Project;
                var library = project.expressionGraphs ?? new System.Collections.Generic.List<Core.ExpressionGraph.UExpressionGraph>();
                // The Worldline-R variants share Worldline-R's graphs.
                string slot = Renderers.GetExpressionGraphSlot(renderer);
                bool InSlot(Core.ExpressionGraph.UExpressionGraph g) =>
                    g.renderer != null && Renderers.GetExpressionGraphSlot(g.renderer) == slot;
                string? defaultId = null;
                project.defaultExpressionGraphs?.TryGetValue(slot, out defaultId);
                var defaultGraph = library.FirstOrDefault(g => g.id == defaultId && InSlot(g));
                string defaultName = defaultGraph != null
                    ? defaultGraph.name ?? defaultGraph.id
                    : ThemeManager.GetString("tracks.expressiongraph.none");
                graphs.Add(new GraphChoice(null, $"{ThemeManager.GetString("tracks.expressiongraph.default")} ({defaultName})"));
                graphs.AddRange(library.Where(InSlot).Select(g => new GraphChoice(g.id, g.name ?? g.id)));
                Graph = graphs.FirstOrDefault(c => c.Id != null && c.Id == Track.ExpressionGraph) ?? graphs[0];
            }
            this.WhenAnyValue(x => x.Resampler)
                .OfType<IResampler>()
                .Subscribe(resampler => {
                    resampler?.CheckPermissions();
                    var wavtool = Wavtool;
                    wavtools.Clear();
                    wavtools.AddRange(Renderers.GetSupportedWavtools(resampler));
                    if (wavtool != null && wavtools.Contains(wavtool)) {
                        Wavtool = wavtool;
                    } else {
                        Wavtool = wavtools.FirstOrDefault();
                    }
                });
            this.WhenAnyValue(x => x.Wavtool)
                .OfType<IWavtool>()
                .Subscribe(wavtool => {
                    wavtool?.CheckPermissions();
                });
        }

        public void OpenResamplerLocation() {
            OS.OpenFolder(PathManager.Inst.ResamplersPath);
        }

        public void SetDefaultResampler() {
            if (Resampler != null) {
                Preferences.Default.DefaultResamplers[Track.RendererSettings.renderer] = Resampler.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void OpenWavtoolLocation() {
            OS.OpenFolder(PathManager.Inst.WavtoolsPath);
        }

        public void SetDefaultWavtool() {
            if (Wavtool != null) {
                Preferences.Default.DefaultWavtools[Track.RendererSettings.renderer] = Wavtool.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void Finish() {
            var project = DocManager.Inst.Project;
            int index = project.tracks.IndexOf(Track);
            if (Graph != null && index >= 0 && Graph.Id != Track.ExpressionGraph) {
                string? id = Graph.Id;
                Core.ExpressionGraph.ExpressionGraphEdits.Apply(project, draft => draft.TrackOverrides[index] = id);
            }
            if (Renderers.CLASSIC != Track.RendererSettings.renderer) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.setting");
            var settings = Track.RendererSettings.Clone();
            settings.resampler = Resampler?.ToString() ?? string.Empty;
            settings.wavtool = Wavtool?.ToString() ?? string.Empty;
            DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, Track, settings));
            DocManager.Inst.EndUndoGroup();
        }
    }
}
