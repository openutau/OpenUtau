using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
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
        [Reactive] public partial bool IsNotClassic { get; set; }
        public ObservableCollectionExtended<RendererSettingViewModel> RendererSettings { get; } = new();
        [Reactive] public partial bool HasRendererSettings { get; set; }

        ObservableCollectionExtended<IResampler> resamplers =
            new ObservableCollectionExtended<IResampler>();
        ObservableCollectionExtended<IWavtool> wavtools =
            new ObservableCollectionExtended<IWavtool>();

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
                var metadata = ExternalRendererRegistry.Renderers
                    .FirstOrDefault(item => string.Equals(item.Id, renderer,
                        StringComparison.OrdinalIgnoreCase))?.Metadata;
                if (metadata != null) {
                    foreach (var pair in metadata.Settings) {
                        Track.RendererSettings.rendererSettings.TryGetValue(pair.Key, out var value);
                        RendererSettings.Add(new RendererSettingViewModel(
                            pair.Key, pair.Value, value ?? pair.Value.DefaultValue));
                    }
                }
                HasRendererSettings = RendererSettings.Count > 0;
                IsNotClassic = Renderers.CLASSIC != renderer && !HasRendererSettings;
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
            DocManager.Inst.StartUndoGroup("command.track.setting");
            var settings = Track.RendererSettings.Clone();
            if (Renderers.CLASSIC == Track.RendererSettings.renderer) {
                settings.resampler = Resampler?.ToString() ?? string.Empty;
                settings.wavtool = Wavtool?.ToString() ?? string.Empty;
            }
            settings.rendererSettings = RendererSettings.ToDictionary(row => row.Key, row => row.Value);
            DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, Track, settings));
            DocManager.Inst.EndUndoGroup();
        }
    }

    public sealed class RendererSettingViewModel {
        public string Key { get; }
        public string Name { get; }
        public string Description { get; }
        public bool IsBoolean { get; }
        public bool IsNumber { get; }
        public bool IsInteger { get; }
        public bool IsChoice { get; }
        public bool IsText => !IsBoolean && !IsNumber && !IsChoice;
        public decimal Minimum { get; }
        public decimal Maximum { get; }
        public IReadOnlyList<string> Choices { get; }
        public bool BoolValue { get; set; }
        public string NumberText { get; set; }
        public string TextValue { get; set; }
        public string ChoiceValue { get; set; }
        public string Value => IsBoolean ? BoolValue.ToString().ToLowerInvariant() :
            IsNumber ? NormalizeNumber() :
            IsChoice ? ChoiceValue : TextValue;

        public RendererSettingViewModel(string key, RendererSettingDescriptor descriptor, string value) {
            Key = key; Name = descriptor.Name; Description = descriptor.Description;
            IsBoolean = descriptor.Type == RendererSettingType.Boolean;
            IsNumber = descriptor.Type is RendererSettingType.Integer or RendererSettingType.Number;
            IsInteger = descriptor.Type == RendererSettingType.Integer;
            IsChoice = descriptor.Type == RendererSettingType.Choice;
            Minimum = (decimal)(descriptor.Min ?? -1000000);
            Maximum = (decimal)(descriptor.Max ?? 1000000);
            Choices = descriptor.Choices;
            BoolValue = bool.TryParse(value, out var boolean) && boolean;
            NumberText = value;
            TextValue = value;
            ChoiceValue = Choices.Contains(value) ? value : descriptor.DefaultValue;
        }

        string NormalizeNumber() {
            if (!decimal.TryParse(NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) {
                number = 0;
            }
            if (IsInteger) number = decimal.Round(number, 0);
            return Math.Clamp(number, Minimum, Maximum).ToString(CultureInfo.InvariantCulture);
        }
    }
}
