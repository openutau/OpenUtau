using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using OpenUtau.Audio;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using ReactiveUI.Primitives;
using ReactiveUI.Avalonia;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class LyricsHelperOption {
        public readonly Type klass;
        public LyricsHelperOption(Type klass) {
            this.klass = klass;
        }
        public override string ToString() {
            return klass.Name;
        }
    }

    public partial class PreferencesViewModel : ViewModelBase {
        // General
        private CultureInfo? language;
        private CultureInfo? sortingOrder;

        public List<CultureInfo>? Languages { get; }
        public CultureInfo? Language {
            get => language;
            set => this.RaiseAndSetIfChanged(ref language, value);
        }
        public List<CultureInfo>? SortingOrders { get; }
        public CultureInfo? SortingOrder {
            get => sortingOrder;
            set => this.RaiseAndSetIfChanged(ref sortingOrder, value);
        }
        // Release channel index: 0 = stable, 1 = beta, 2 = alpha.
        [Reactive] public partial int Channel { get; set; }

        // Playback
        private List<AudioOutputDevice>? audioOutputDevices;
        private AudioOutputDevice? audioOutputDevice;

        public List<AudioOutputDevice>? AudioOutputDevices {
            get => audioOutputDevices;
            set => this.RaiseAndSetIfChanged(ref audioOutputDevices, value);
        }
        public AudioOutputDevice? AudioOutputDevice {
            get => audioOutputDevice;
            set => this.RaiseAndSetIfChanged(ref audioOutputDevice, value);
        }
        [Reactive] public partial bool UseSystemDefaultDevice { get; set; }
        [Reactive] public partial int PreferPortAudio { get; set; }
        [Reactive] public partial uint AudioBackEnd { get; set; }
        [Reactive] public partial int LockStartTime { get; set; }
        [Reactive] public partial int PlaybackAutoScroll { get; set; }
        [Reactive] public partial double PlayPosMarkerMargin { get; set; }
        [Reactive] public partial float MetronomVolume { get; set; }

        // Paths
        public string SingerPath => PathManager.Inst.SingersPath;
        public string AdditionalSingersPath => !string.IsNullOrWhiteSpace(PathManager.Inst.AdditionalSingersPath) ? PathManager.Inst.AdditionalSingersPath : "(None)";
        [Reactive] public partial bool InstallToAdditionalSingersPath { get; set; }
        [Reactive] public partial bool LoadDeepFolders { get; set; }

        // Editing
        public List<LyricsHelperOption> LyricsHelpers { get; } =
            ActiveLyricsHelper.Inst.Available
                .Select(klass => new LyricsHelperOption(klass))
                .ToList();
        [Reactive] public partial LyricsHelperOption? LyricsHelper { get; set; }
        [Reactive] public partial bool LyricsHelperBrackets { get; set; }
        [Reactive] public partial bool PenPlusDefault { get; set; }

        // Render
        [Reactive] public partial bool PreRender { get; set; }
        [Reactive] public partial int NumRenderThreads { get; set; }
        public int LogicalCoreCount {
            get => Environment.ProcessorCount;
        }
        [Reactive] public partial bool HighThreads { get; set; }
        public int SafeMaxThreadCount {
            get => Math.Min(8, LogicalCoreCount / 2);
        }
        [Reactive] public partial bool SkipRenderingMutedTracks { get; set; }
        [Reactive] public partial bool ClearCacheOnQuit { get; set; }
        public List<string> OnnxRunnerOptions { get; set; }
        [Reactive] public partial string OnnxRunner { get; set; }
        public List<GpuInfo> OnnxGpuOptions { get; set; }
        [Reactive] public partial GpuInfo OnnxGpu { get; set; }
        [Reactive] public partial bool ShowOnnxGpu { get; set; }

        // GAME backend (onnx / ggml)
        public List<string> GameBackendOptions { get; } = new() { "ONNX", "GGML" };
        [Reactive] public partial string GameBackend { get; set; }

        // Appearance
        [Reactive] public partial string ThemeName { get; set; }
        [Reactive] public partial int DegreeStyle { get; set; }
        [Reactive] public partial bool UseTrackColor { get; set; }
        [Reactive] public partial bool ShowPortrait { get; set; }
        [Reactive] public partial bool ShowIcon { get; set; }
        [Reactive] public partial bool ShowGhostNotes { get; set; }
        [Reactive] public partial bool NoteHoverGlow { get; set; }
        [Reactive] public partial bool ShowPlaybackNoteHighlight { get; set; }
        [Reactive] public partial bool ShowPlaybackNoteBounce { get; set; }
        [Reactive] public partial bool DetachPianoRoll { get; set; }
        [Reactive] public partial bool ThemeEditable { get; set; }
        public List<string> ThemeItems => ThemeManager.GetAvailableThemes();
        public bool IsThemeEditorOpen => Views.ThemeEditorWindow.IsOpen;

        // UTAU
        public List<string> DefaultRendererOptions { get; set; }
        [Reactive] public partial string DefaultRenderer { get; set; }
        [Reactive] public partial int OtoEditor { get; set; }
        public string VLabelerPath => Preferences.Default.VLabelerPath;
        public string SetParamPath => Preferences.Default.SetParamPath;

        // Diffsinger
        public List<int> DiffSingerStepsOptions { get; } = new List<int> { 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        public List<int> DiffSingerStepsVarianceOptions { get; } = new List<int> { 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        public List<int> DiffSingerStepsPitchOptions { get; } = new List<int> { 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
        [Reactive] public partial int DiffSingerSteps { get; set; }
        [Reactive] public partial int DiffSingerStepsVariance { get; set; }
        [Reactive] public partial int DiffSingerStepsPitch { get; set; }
        [Reactive] public partial double DiffSingerDepth { get; set; }
        [Reactive] public partial bool DiffSingerTensorCache { get; set; }
        [Reactive] public partial bool DiffSingerVarianceLocalPitchPatch { get; set; }
        [Reactive] public partial bool DiffSingerLangCodeHide { get; set; }
        [Reactive] public partial bool DiffSingerLocalRetaking { get; set; }

        // Advanced
        [Reactive] public partial bool RememberMid { get; set; }
        [Reactive] public partial bool RememberUst { get; set; }
        [Reactive] public partial bool RememberVsqx { get; set; }
        [Reactive] public partial bool Wayland { get; set; }
        public string WinePath => Preferences.Default.WinePath;

        public PreferencesViewModel() {
            var audioOutput = PlaybackManager.Inst.AudioOutput;
            if (audioOutput != null) {
                AudioOutputDevices = audioOutput.GetOutputDevices();
                int deviceNumber = audioOutput.DeviceNumber;
                var device = AudioOutputDevices.FirstOrDefault(d => d.deviceNumber == deviceNumber);
                if (device != null) {
                    AudioOutputDevice = device;
                }
            }
            UseSystemDefaultDevice = Preferences.Default.UseSystemDefaultAudioDevice;
            AudioBackEnd = Preferences.Default.AudioBackEnd;
            PlaybackAutoScroll = Preferences.Default.PlaybackAutoScroll;
            PlayPosMarkerMargin = Preferences.Default.PlayPosMarkerMargin;
            MetronomVolume = Preferences.Default.MetronomVolume;
            LockStartTime = Preferences.Default.LockStartTime;
            InstallToAdditionalSingersPath = Preferences.Default.InstallToAdditionalSingersPath;
            LoadDeepFolders = Preferences.Default.LoadDeepFolderSinger;
            ToolsManager.Inst.Initialize();
            var pattern = new Regex(@"Strings\.([\w-]+)\.axaml");
            Languages = App.GetLanguages().Keys
                .Select(lang => CultureInfo.GetCultureInfo(lang))
                .ToList();
            Language = string.IsNullOrEmpty(Preferences.Default.Language)
                ? null
                : CultureInfo.GetCultureInfo(Preferences.Default.Language);
            SortingOrders = Languages.ToList();
            SortingOrders.Insert(0, CultureInfo.InvariantCulture);
            SortingOrder = Preferences.Default.SortingOrder == null ? Language
                : string.IsNullOrEmpty(Preferences.Default.SortingOrder) ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(Preferences.Default.SortingOrder);
            PreRender = Preferences.Default.PreRender;
            DefaultRendererOptions = Renderers.getRendererOptions();
            DefaultRenderer = String.IsNullOrEmpty(Preferences.Default.DefaultRenderer) ?
               DefaultRendererOptions[0] : Preferences.Default.DefaultRenderer;
            NumRenderThreads = Preferences.Default.NumRenderThreads;
            OnnxRunnerOptions = Onnx.getRunnerOptions();
            OnnxRunner = String.IsNullOrEmpty(Preferences.Default.OnnxRunner) ?
               OnnxRunnerOptions[0] : Preferences.Default.OnnxRunner;
            OnnxGpuOptions = Onnx.getGpuInfo();
            OnnxGpu = OnnxGpuOptions.FirstOrDefault(x => x.deviceId == Preferences.Default.OnnxGpu, OnnxGpuOptions[0]);
            ShowOnnxGpu = OnnxRunner == "DirectML";
            // GAME backend: ONNX is the default, GGML is available when installed.
            // The options list always includes both so the ComboBox UX is stable.
            GameBackend = Preferences.Default.GameBackend switch {
                "ggml" => "GGML",
                _ => "ONNX",  // default / empty / unrecognized all map to ONNX
            };
            DiffSingerDepth = Preferences.Default.DiffSingerDepth * 100;
            DiffSingerSteps = Preferences.Default.DiffSingerSteps;
            DiffSingerStepsVariance = Preferences.Default.DiffSingerStepsVariance;
            DiffSingerStepsPitch = Preferences.Default.DiffSingerStepsPitch;
            DiffSingerTensorCache = Preferences.Default.DiffSingerTensorCache;
            DiffSingerVarianceLocalPitchPatch = Preferences.Default.DiffSingerVarianceLocalPitchPatch;
            DiffSingerLangCodeHide = Preferences.Default.DiffSingerLangCodeHide;
            DiffSingerLocalRetaking = Preferences.Default.DiffSingerLocalRetaking;
            SkipRenderingMutedTracks = Preferences.Default.SkipRenderingMutedTracks;
            ThemeName = Preferences.Default.ThemeName;
            DegreeStyle = Preferences.Default.DegreeStyle;
            UseTrackColor = Preferences.Default.UseTrackColor;
            ShowPortrait = Preferences.Default.ShowPortrait;
            ShowIcon = Preferences.Default.ShowIcon;
            ShowGhostNotes = Preferences.Default.ShowGhostNotes;
            NoteHoverGlow = Preferences.Default.NoteHoverGlow;
            ShowPlaybackNoteHighlight = Preferences.Default.ShowPlaybackNoteHighlight;
            ShowPlaybackNoteBounce = Preferences.Default.ShowPlaybackNoteBounce;
            DetachPianoRoll = Preferences.Default.DetachPianoRoll;
            Channel = Preferences.Default.Channel switch {
                "beta" => 1,
                "alpha" => 2,
                _ => 0
            };
            LyricsHelper = LyricsHelpers.FirstOrDefault(option => option.klass.Equals(ActiveLyricsHelper.Inst.GetPreferred()));
            LyricsHelperBrackets = Preferences.Default.LyricsHelperBrackets;
            OtoEditor = Preferences.Default.OtoEditor;
            RememberMid = Preferences.Default.RememberMid;
            RememberUst = Preferences.Default.RememberUst;
            RememberVsqx = Preferences.Default.RememberVsqx;
            ClearCacheOnQuit = Preferences.Default.ClearCacheOnQuit;
            Wayland = Preferences.Default.UseWayland;

            MessageBus.Current.Listen<ThemeEditorStateChangedEvent>()
                .Subscribe(_ => this.RaisePropertyChanged(nameof(IsThemeEditorOpen)));

            this.WhenAnyValue(vm => vm.UseSystemDefaultDevice)
                .Subscribe(useDefault => {
                    Preferences.Default.UseSystemDefaultAudioDevice = useDefault;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.AudioOutputDevice)
                .OfType<AudioOutputDevice>()
                .SubscribeOn(AvaloniaScheduler.Instance)
                .Subscribe(device => {
                    if (UseSystemDefaultDevice) {
                        return;
                    }
                    if (PlaybackManager.Inst.AudioOutput != null) {
                        try {
                            PlaybackManager.Inst.AudioOutput.SelectDevice(device.guid, device.deviceNumber);
                        } catch (Exception e) {
                            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification($"Failed to select device {device.name}", e));
                        }
                    }
                });
            this.WhenAnyValue(vm => vm.AudioBackEnd)
                .Subscribe(index => {
                    Preferences.Default.AudioBackEnd = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.PlaybackAutoScroll)
                .Subscribe(autoScroll => {
                    Preferences.Default.PlaybackAutoScroll = autoScroll;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.PlayPosMarkerMargin)
                .Subscribe(playPosMarkerMargin => {
                    Preferences.Default.PlayPosMarkerMargin = playPosMarkerMargin;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.MetronomVolume)
                .Subscribe(metronomVolume => {
                    Preferences.Default.MetronomVolume = MetronomVolume;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.LockStartTime)
                .Subscribe(lockStartTime => {
                    Preferences.Default.LockStartTime = lockStartTime;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.InstallToAdditionalSingersPath)
                .Subscribe(additionalSingersPath => {
                    Preferences.Default.InstallToAdditionalSingersPath = additionalSingersPath;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.Wayland)
                .Subscribe(additionalSingersPath => {
                    Preferences.Default.UseWayland = Wayland;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.LoadDeepFolders)
                .Subscribe(loadDeepFolders => {
                    Preferences.Default.LoadDeepFolderSinger = loadDeepFolders;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.PreRender)
                .Subscribe(preRender => {
                    Preferences.Default.PreRender = preRender;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.Language)
                .OfType<CultureInfo>()
                .Subscribe(lang => {
                    Preferences.Default.Language = lang?.Name ?? string.Empty;
                    Preferences.Save();
                    App.SetLanguage(Preferences.Default.Language);
                });
            this.WhenAnyValue(vm => vm.SortingOrder)
                .OfType<CultureInfo>()
                .Subscribe(so => {
                    Preferences.Default.SortingOrder = so?.Name ?? null;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.ThemeName)
                .Subscribe(themeName => {
                    ThemeEditable = themeName != "Light" && themeName != "Dark" && !Colors.CustomTheme.IsPackageTheme(themeName);
                    if (!IsThemeEditorOpen) {
                        Preferences.Default.ThemeName = themeName;
                        Preferences.Save();
                        App.SetTheme();
                    }
                });
            this.WhenAnyValue(vm => vm.DegreeStyle)
                .Subscribe(degreeStyle => {
                    Preferences.Default.DegreeStyle = degreeStyle;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                });
            this.WhenAnyValue(vm => vm.UseTrackColor)
                .Subscribe(trackColor => {
                    Preferences.Default.UseTrackColor = trackColor;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("TrackColor"));
                });
            this.WhenAnyValue(vm => vm.ShowPortrait)
                .Subscribe(showPortrait => {
                    Preferences.Default.ShowPortrait = showPortrait;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
                });
            this.WhenAnyValue(vm => vm.ShowIcon)
                .Subscribe(showIcon => {
                    Preferences.Default.ShowIcon = showIcon;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
                });
            this.WhenAnyValue(vm => vm.ShowGhostNotes)
                .Subscribe(showGhostNotes => {
                    Preferences.Default.ShowGhostNotes = showGhostNotes;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                });
            this.WhenAnyValue(vm => vm.NoteHoverGlow)
                .Subscribe(noteHoverGlow => {
                    Preferences.Default.NoteHoverGlow = noteHoverGlow;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new NotesRefreshEvent());
                });
            this.WhenAnyValue(vm => vm.ShowPlaybackNoteHighlight)
                .Subscribe(showPlaybackNoteHighlight => {
                    Preferences.Default.ShowPlaybackNoteHighlight = showPlaybackNoteHighlight;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("PlaybackNoteHighlight"));
                });
            this.WhenAnyValue(vm => vm.ShowPlaybackNoteBounce)
                .Subscribe(showPlaybackNoteBounce => {
                    Preferences.Default.ShowPlaybackNoteBounce = showPlaybackNoteBounce;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("PlaybackNoteBounce"));
                });
            this.WhenAnyValue(vm => vm.DetachPianoRoll)
                .Subscribe(detachPianoRoll => {
                    Preferences.Default.DetachPianoRoll = detachPianoRoll;
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Attachment"));
                });
            this.WhenAnyValue(vm => vm.Channel)
                .Subscribe(channel => {
                    Preferences.Default.Channel = channel switch {
                        1 => "beta",
                        2 => "alpha",
                        _ => "stable"
                    };
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.LyricsHelper)
                .OfType<LyricsHelperOption>()
                .Subscribe(option => {
                    ActiveLyricsHelper.Inst.Set(option?.klass);
                    Preferences.Default.LyricHelper = option?.klass?.Name ?? string.Empty;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.LyricsHelperBrackets)
                .Subscribe(brackets => {
                    Preferences.Default.LyricsHelperBrackets = brackets;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.OtoEditor)
                .Subscribe(index => {
                    Preferences.Default.OtoEditor = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.NumRenderThreads)
                .Subscribe(index => {
                    Preferences.Default.NumRenderThreads = index;
                    HighThreads = index > SafeMaxThreadCount ? true : false;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DefaultRenderer)
                .Subscribe(index => {
                    Preferences.Default.DefaultRenderer = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.OnnxRunner)
                .Subscribe(index => {
                    Preferences.Default.OnnxRunner = index;
                    Preferences.Save();
                    ToggleOnnxGpuDisplay(index == "DirectML");
                });
            this.WhenAnyValue(vm => vm.OnnxGpu)
                .Subscribe(index => {
                    Preferences.Default.OnnxGpu = index.deviceId;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.GameBackend)
                .Subscribe(index => {
                    Preferences.Default.GameBackend = index == "GGML" ? "ggml" : "onnx";
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.RememberMid)
                .Subscribe(index => {
                    Preferences.Default.RememberMid = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.RememberUst)
                .Subscribe(index => {
                    Preferences.Default.RememberUst = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.RememberVsqx)
                .Subscribe(index => {
                    Preferences.Default.RememberVsqx = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.ClearCacheOnQuit)
                .Subscribe(index => {
                    Preferences.Default.ClearCacheOnQuit = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DiffSingerSteps)
                .Subscribe(index => {
                    Preferences.Default.DiffSingerSteps = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DiffSingerStepsVariance)
                 .Subscribe(index => {
                     Preferences.Default.DiffSingerStepsVariance = index;
                     Preferences.Save();
                 });
            this.WhenAnyValue(vm => vm.DiffSingerStepsPitch)
                .Subscribe(index => {
                    Preferences.Default.DiffSingerStepsPitch = index;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DiffSingerDepth)
                .Subscribe(index => {
                    Preferences.Default.DiffSingerDepth = index / 100;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DiffSingerTensorCache)
                .Subscribe(useCache => {
                    Preferences.Default.DiffSingerTensorCache = useCache;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DiffSingerVarianceLocalPitchPatch)
                .Subscribe(useLocalPatch => {
                    Preferences.Default.DiffSingerVarianceLocalPitchPatch = useLocalPatch;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DiffSingerLangCodeHide)
                .Subscribe(useCache => {
                    Preferences.Default.DiffSingerLangCodeHide = useCache;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.DiffSingerLocalRetaking)
                .Subscribe(value => {
                    Preferences.Default.DiffSingerLocalRetaking = value;
                    Preferences.Save();
                });
            this.WhenAnyValue(vm => vm.SkipRenderingMutedTracks)
                .Subscribe(skipRenderingMutedTracks => {
                    Preferences.Default.SkipRenderingMutedTracks = skipRenderingMutedTracks;
                    Preferences.Save();
                });
        }

        public void TestAudioOutputDevice() {
            try {
                PlaybackManager.Inst.PlayTestSound();
            } catch (Exception e) {
                Log.Error(e, "Failed to play test sound.");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to play test sound.", e));
            }
        }

        public void OpenResamplerLocation() {
            try {
                string path = PathManager.Inst.ResamplersPath;
                Directory.CreateDirectory(path);
                OS.OpenFolder(path);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void SetAddlSingersPath(string path) {
            Preferences.Default.AdditionalSingerPath = path;
            Preferences.Save();
            this.RaisePropertyChanged(nameof(AdditionalSingersPath));
        }

        public void SetVLabelerPath(string path) {
            Preferences.Default.VLabelerPath = path;
            Preferences.Save();
            this.RaisePropertyChanged(nameof(VLabelerPath));
        }

        public void SetSetParamPath(string path) {
            Preferences.Default.SetParamPath = path;
            Preferences.Save();
            this.RaisePropertyChanged(nameof(SetParamPath));
        }

        public void SetWinePath(string path) {
            Preferences.Default.WinePath = path;
            Preferences.Save();
            ToolsManager.Inst.Initialize();
            this.RaisePropertyChanged(nameof(WinePath));
        }

        public void RefreshThemes() {
            Colors.CustomTheme.ListThemes();
            _ = OudepLoaderRegistry.LoadAllAsync();
            this.RaisePropertyChanged(nameof(ThemeItems));
        }

        public void ToggleOnnxGpuDisplay(bool show) {
            ShowOnnxGpu = show;
        }
    }
}
