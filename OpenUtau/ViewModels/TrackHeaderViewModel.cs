using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using OpenUtau.Api;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public partial class TrackHeaderViewModel : ViewModelBase, IActivatableViewModel {
        public int TrackNo => track.TrackNo + 1;
        public USinger Singer => track.Singer;
        public Phonemizer Phonemizer => track.Phonemizer;
        public string PhonemizerTag => track.Phonemizer.Tag;
        public Core.Render.IRenderer Renderer => track.RendererSettings.Renderer;
        public ReactiveCommand<USinger, RxVoid> SelectSingerCommand { get; }
        public IReadOnlyList<MenuItemViewModel>? PhonemizerMenuItems { get; set; }
        public ReactiveCommand<PhonemizerFactory, RxVoid> SelectPhonemizerCommand { get; }
        public IReadOnlyList<MenuItemViewModel>? RenderersMenuItems { get; set; }
        public ReactiveCommand<string, RxVoid> SelectRendererCommand { get; }
        [Reactive] public partial string TrackName { get; set; } = string.Empty;
        [Reactive] public partial SolidColorBrush TrackAccentColor { get; set; } = ThemeManager.GetTrackColor("Blue").AccentColor;
        [Reactive] public partial TrackColor TrackColor { get; set; } = ThemeManager.GetTrackColor("Blue");
        [Reactive] public partial double Volume { get; set; }
        [Reactive] public partial double Pan { get; set; }
        [Reactive] public partial bool Mute { get; set; }
        [Reactive] public partial bool Muted { get; set; }
        [Reactive] public partial bool Solo { get; set; }
        [Reactive] public partial bool IsSelected { get; set; }
        [Reactive] public partial Bitmap? Avatar { get; set; }
        [Reactive] public partial double AvatarHeight { get; set; }
        [Reactive] public partial bool IsSingerVisible { get; set; }
        [Reactive] public partial bool IsPhonemizerVisible { get; set; }
        [Reactive] public partial bool IsRendererVisible { get; set; }
        [Reactive] public partial bool MixFxEnabled { get; set; }
        [Reactive] public partial IBrush HeaderBorderBrush { get; set; } = ThemeManager.NeutralAccentBrushSemi;

        public ViewModelActivator Activator { get; }

        private readonly UTrack track;

        // Parameterless constructor for Avalonia preview only.
        public TrackHeaderViewModel() {
            SelectSingerCommand = ReactiveCommand.Create<USinger>(_ => { });
            SelectPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(_ => { });
            SelectRendererCommand = ReactiveCommand.Create<string>(_ => { });
            Activator = new ViewModelActivator();
            track = new UTrack(DocManager.Inst.Project);
            this.WhenAnyValue(x => x.IsSelected)
                .Subscribe(_ => RefreshSelectionStyle());
            RefreshSelectionStyle();
        }

        public TrackHeaderViewModel(UTrack track) {
            this.track = track;
            SelectSingerCommand = ReactiveCommand.Create<USinger>(singer => {
                var targetTracks = GetBatchTargetTracks()
                    .Where(t => t.Singer != singer)
                    .ToList();
                if (targetTracks.Count > 0) {
                    DocManager.Inst.StartUndoGroup("command.track.singer");
                    foreach (var targetTrack in targetTracks) {
                        ApplySingerToTrack(targetTrack, singer);
                        DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(targetTrack.TrackNo, true));
                    }
                    DocManager.Inst.EndUndoGroup();
                    UpdateRecentSingers(singer);
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                    MessageBus.Current.SendMessage(new TracksRefreshEvent());
                }
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
                this.RaisePropertyChanged(nameof(Singer));
                this.RaisePropertyChanged(nameof(Renderer));
                RefreshAvatar();
            });
            SelectPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(factory => {
                var targetTracks = GetBatchTargetTracks()
                    .Where(t => t.Phonemizer.GetType() != factory.type)
                    .ToList();
                if (targetTracks.Count > 0) {
                    DocManager.Inst.StartUndoGroup("command.track.setting");
                    Phonemizer? phonemizer = null;
                    foreach (var targetTrack in targetTracks) {
                        phonemizer = factory.Create();
                        Log.Information($"Loading Phonemizer: {phonemizer.ToString()}");
                        DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, targetTrack, phonemizer));
                    }
                    DocManager.Inst.EndUndoGroup();
                    var name = phonemizer!.GetType().FullName!;
                    if (!string.IsNullOrEmpty(Singer?.Id) && phonemizer != null) {
                        Preferences.Default.SingerPhonemizers[Singer.Id] = name;
                    }
                    Preferences.Default.RecentPhonemizers.Remove(name);
                    Preferences.Default.RecentPhonemizers.Insert(0, name);
                    while (Preferences.Default.RecentPhonemizers.Count > 8) {
                        Preferences.Default.RecentPhonemizers.RemoveRange(
                            8, Preferences.Default.RecentPhonemizers.Count - 8);
                    }
                    Preferences.Save();
                }
                this.RaisePropertyChanged(nameof(Phonemizer));
                this.RaisePropertyChanged(nameof(PhonemizerTag));
            });
            SelectRendererCommand = ReactiveCommand.Create<string>(name => {
                var settings = new URenderSettings {
                    renderer = name,
                };
                DocManager.Inst.StartUndoGroup("command.track.setting");
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, track, settings));
                DocManager.Inst.EndUndoGroup();
                this.RaisePropertyChanged(nameof(Renderer));
            });

            Activator = new ViewModelActivator();

            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
            Volume = track.Volume;
            Pan = track.Pan;
            Mute = track.Mute;
            Muted = track.Muted;
            Solo = track.Solo;
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.WhenAnyValue(x => x.Volume)
                .Subscribe(volume => {
                    track.Volume = volume;
                    DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(track.TrackNo, Muted ? -24 : volume));
                });
            this.WhenAnyValue(x => x.Pan)
                .Subscribe(pan => {
                    track.Pan = pan;
                    DocManager.Inst.ExecuteCmd(new PanChangeNotification(track.TrackNo, pan));
                });
            this.WhenAnyValue(x => x.Mute)
                .Subscribe(mute => {
                    track.Mute = mute;
                });
            this.WhenAnyValue(x => x.Muted)
                .Subscribe(muted => {
                    track.Muted = muted;
                    DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(track.TrackNo, muted ? -24 : Volume));
                });
            this.WhenAnyValue(x => x.Solo)
                .Subscribe(solo => {
                    track.Solo = solo;
                });
            this.WhenAnyValue(x => x.MixFxEnabled)
                .Subscribe(enabled => {
                    if (track.MixFx != null) {
                        track.MixFx.Enabled = enabled;
                    } else if (enabled) {
                        track.MixFx = new UMixFx { Enabled = true };
                    }
                });
            this.WhenAnyValue(x => x.IsSelected)
                .Subscribe(_ => RefreshSelectionStyle());

            RefreshAvatar();
            RefreshSelectionStyle();
        }

        public void RefreshSelectionStyle() {
            HeaderBorderBrush = IsSelected
                ? TrackAccentColor
                : ThemeManager.NeutralAccentBrushSemi;
        }

        public void ToggleSolo() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(track.TrackNo, !track.Solo, false));
        }

        public void SoloAdditionally() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(track.TrackNo, !track.Solo, true));
        }

        public void UnsoloAll() {
            MessageBus.Current.SendMessage(new TracksSoloEvent(-1, false, false));
        }

        public void ToggleMute() {
            if (!Mute) {
                Mute = true;
            } else {
                Mute = false;
            }
            this.RaisePropertyChanged(nameof(Mute));
            JudgeMuted();
        }

        public void ToggleMuteWithBool(bool mute) {
            Mute = mute;
            this.RaisePropertyChanged(nameof(Mute));
            JudgeMuted();
        }

        public void MuteOnly() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, false));
            ToggleMute();
        }

        public void MuteAllOthers() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, true));
            ToggleMute();
        }

        public void UnmuteAll() {
            MessageBus.Current.SendMessage(new TracksMuteEvent(-1, false));
        }

        public void JudgeMuted() {
            if (Solo) {
                Muted = false;
            } else if (Mute) {
                Muted = true;
            } else if (DocManager.Inst.Project.SoloTrackExist) {
                Muted = true;
            } else {
                Muted = false;
            }
            this.RaisePropertyChanged(nameof(Muted));
        }

        /// <summary>
        /// The tracks a header action applies to: every selected track when this track is part of a
        /// multi-track selection, otherwise just this track.
        /// </summary>
        private List<UTrack> GetBatchTargetTracks() {
            var selected = ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.DataContext as MainWindowViewModel)?.TracksViewModel.SelectedTracks;
            if (selected != null && selected.Count > 1 && selected.Contains(track)) {
                return selected.OrderBy(t => t.TrackNo).ToList();
            }
            return new List<UTrack> { track };
        }

        private void ApplySingerToTrack(UTrack targetTrack, USinger? singer) {
            if (singer is USinger selectedSinger) {
                Log.Information($"Loading Singer: {selectedSinger.Name}");
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, selectedSinger));
                if (!string.IsNullOrEmpty(selectedSinger.Id) &&
                    Preferences.Default.SingerPhonemizers.TryGetValue(selectedSinger.Id, out var phonemizerName) &&
                    TryChangePhonemizer(targetTrack, phonemizerName)) {
                } else if (!string.IsNullOrEmpty(selectedSinger.DefaultPhonemizer)) {
                    TryChangePhonemizer(targetTrack, selectedSinger.DefaultPhonemizer);
                }
                if (!selectedSinger.Found || selectedSinger.SingerType != targetTrack.RendererSettings.Renderer?.SingerType) {
                    var settings = new URenderSettings();
                    if (selectedSinger.Found) {
                        settings = new URenderSettings {
                            renderer = Core.Render.Renderers.GetDefaultRenderer(selectedSinger.SingerType),
                        };
                    }
                    DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
                }
            } else {
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, USinger.CreateMissing(string.Empty)));
                var settings = new URenderSettings();
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
            }
        }

        private void UpdateRecentSingers(USinger? singer) {
            if (!string.IsNullOrEmpty(singer?.Id) && singer.Found) {
                Preferences.Default.RecentSingers.Remove(singer.Id);
                Preferences.Default.RecentSingers.Insert(0, singer.Id);
                if (Preferences.Default.RecentSingers.Count > 16) {
                    Preferences.Default.RecentSingers.RemoveRange(
                        16, Preferences.Default.RecentSingers.Count - 16);
                }
            }
        }

        private bool TryChangePhonemizer(UTrack targetTrack, string phonemizerName) {
            try {
                var factory = PhonemizerFactory.Get(phonemizerName);
                var phonemizer = factory?.Create();
                if (phonemizer != null) {
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, targetTrack, phonemizer));
                    return true;
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemizer {phonemizerName}");
            }
            return false;
        }

        public string GetPhonemizerGroupHeader(string key) {
            if (key is null) {
                return "General";
            }
            if (ThemeManager.TryGetString($"languages.{key.ToLowerInvariant()}", out var value)) {
                return $"{key}: {value}";
            }
            return key;
        }

        PhonemizerFactory? FindPhonemizerByName(string name) {
            return PhonemizerFactory.Get(name);
        }

        public void RefreshPhonemizers() {
            var items = new List<MenuItemViewModel>();
            //Singer default
            if (track != null && track.Singer != null && track.Singer.Found) {
                var factory = FindPhonemizerByName(track.Singer.DefaultPhonemizer);
                if (factory != null) {
                    items.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("tracks.singerdefault") + factory.ToString(),
                        Command = SelectPhonemizerCommand,
                        CommandParameter = factory,
                    });
                }
            }
            //Recently used phonemizers
            items.AddRange(Preferences.Default.RecentPhonemizers
                .Select(name => FindPhonemizerByName(name))
                .OfType<PhonemizerFactory>()
                .OrderBy(factory => factory.tag)
                .Select(factory => new MenuItemViewModel() {
                    Header = factory.ToString(),
                    Command = SelectPhonemizerCommand,
                    CommandParameter = factory,
                }));
            //more phonemizers grouped by singing language
            items.Add(new MenuItemViewModel() {
                Header = $"{ThemeManager.GetString("tracks.more")} ...",
                Items = PhonemizerFactory.GetAll().GroupBy(factory => factory.language)
                .OrderBy(group => group.Key)
                .Select(group => new MenuItemViewModel() {
                    Header = GetPhonemizerGroupHeader(group.Key),
                    Items = group.Select(factory => new MenuItemViewModel() {
                        Header = factory.ToString(),
                        Command = SelectPhonemizerCommand,
                        CommandParameter = factory,
                    }).ToArray(),
                }).ToArray()
            });
            PhonemizerMenuItems = items.ToArray();
            this.RaisePropertyChanged(nameof(PhonemizerMenuItems));
        }

        public void RefreshRenderers() {
            var items = new List<MenuItemViewModel>();
            if (track != null && track.Singer != null && track.Singer.Found) {
                items.AddRange(Core.Render.Renderers.GetSupportedRenderers(track.Singer.SingerType)
                    .Select(name => new MenuItemViewModel() {
                        Header = name,
                        Command = SelectRendererCommand,
                        CommandParameter = name,
                    }));
            }
            RenderersMenuItems = items.ToArray();
            this.RaisePropertyChanged(nameof(RenderersMenuItems));
        }

        // Keeps the avatar column's width while there is no avatar to show.
        private static Bitmap? emptyAvatar;
        private static Bitmap EmptyAvatar => emptyAvatar ??= new RenderTargetBitmap(new PixelSize(1, 1));

        public void RefreshAvatar() {
            var singer = track?.Singer;
            if (singer == null) {
                Avatar = EmptyAvatar;
                return;
            }
            // Cached bitmaps are shared, so they are never disposed here.
            Avatar = SingerAvatarCache.Get(singer, bitmap => {
                if (ReferenceEquals(track?.Singer, singer)) {
                    Avatar = bitmap ?? EmptyAvatar;
                }
            }) ?? EmptyAvatar;
        }

        public void ManuallyRaise() {
            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
            RefreshSelectionStyle();
            this.RaisePropertyChanged(nameof(Singer));
            this.RaisePropertyChanged(nameof(TrackNo));
            this.RaisePropertyChanged(nameof(TrackName));
            this.RaisePropertyChanged(nameof(TrackAccentColor));
            this.RaisePropertyChanged(nameof(TrackColor));
            this.RaisePropertyChanged(nameof(Phonemizer));
            this.RaisePropertyChanged(nameof(PhonemizerTag));
            this.RaisePropertyChanged(nameof(Renderer));
            this.RaisePropertyChanged(nameof(Mute));
            this.RaisePropertyChanged(nameof(Muted));
            this.RaisePropertyChanged(nameof(Solo));
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.RaisePropertyChanged(nameof(MixFxEnabled));
            this.RaisePropertyChanged(nameof(Volume));
            this.RaisePropertyChanged(nameof(Pan));
            RefreshAvatar();
        }

        public void Remove() {
            DocManager.Inst.StartUndoGroup("command.track.delete");
            DocManager.Inst.ExecuteCmd(new RemoveTrackCommand(DocManager.Inst.Project, track));
            DocManager.Inst.EndUndoGroup();
        }

        public void MoveUp() {
            if (track == DocManager.Inst.Project.tracks.First()) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.order");
            DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, track, true));
            DocManager.Inst.EndUndoGroup();
        }

        public void MoveDown() {
            if (track == DocManager.Inst.Project.tracks.Last()) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.order");
            DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, track, false));
            DocManager.Inst.EndUndoGroup();
        }

        public void Rename() {
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("tracks.rename");
            dialog.SetText(track.TrackName);
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name) && name != track.TrackName) {
                    DocManager.Inst.StartUndoGroup("command.track.setting");
                    this.TrackName = name;
                    DocManager.Inst.ExecuteCmd(new RenameTrackCommand(DocManager.Inst.Project, track, name));
                    DocManager.Inst.EndUndoGroup();
                }
            };
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                dialog.ShowDialog(desktop.MainWindow);
            }
        }

        public async void SelectTrackColor() {
            var dialog = new TrackColorDialog();
            dialog.DataContext = new TrackColorViewModel(track);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                await dialog.ShowDialog(desktop.MainWindow);
                TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
                TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
                RefreshSelectionStyle();
            }
        }

        public void Duplicate() {
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            var newTrack = new UTrack(track.TrackName + "_copy") {
                TrackNo = track.TrackNo + 1,
                Singer = track.Singer,
                Phonemizer = track.Phonemizer,
                RendererSettings = track.RendererSettings,
                Mute = track.Mute,
                Muted = track.Muted,
                Solo = false,
                Volume = track.Volume,
                Pan = track.Pan,
                TrackColor = track.TrackColor,
                TrackExpressions = track.TrackExpressions.Select(exp => exp.Clone()).ToList()
            };
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, newTrack));
            var parts = DocManager.Inst.Project.parts
                .Where(part => part.trackNo == track.TrackNo)
                .Select(part => part.Clone()).ToList();
            foreach (var part in parts) {
                part.trackNo = newTrack.TrackNo;
                DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, part));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void DuplicateSettings() {
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, new UTrack(track.TrackName + "_copy") {
                TrackNo = track.TrackNo + 1,
                Singer = track.Singer,
                Phonemizer = track.Phonemizer,
                RendererSettings = track.RendererSettings,
                Mute = track.Mute,
                Muted = track.Muted,
                Solo = false,
                Volume = track.Volume,
                Pan = track.Pan,
                TrackColor = track.TrackColor,
                TrackExpressions = track.TrackExpressions.Select(exp => exp.Clone()).ToList()
            }));
            DocManager.Inst.EndUndoGroup();
        }

        /// <summary>
        /// Copies this track's singer, phonemizer, renderer, mixer settings, color and expressions
        /// to the other selected tracks (all other tracks when fewer than two are selected).
        /// </summary>
        public void StandardizeSettings() {
            var project = DocManager.Inst.Project;
            var targetTracks = GetBatchTargetTracks();
            if (targetTracks.Count <= 1) {
                targetTracks = project.tracks.ToList();
            }
            var phonemizerFactory = PhonemizerFactory.Get(track.Phonemizer.GetType());
            DocManager.Inst.StartUndoGroup("command.track.setting");
            foreach (var targetTrack in targetTracks) {
                if (targetTrack == track) {
                    continue;
                }
                if (track.Singer != targetTrack.Singer) {
                    ApplySingerToTrack(targetTrack, track.Singer);
                }
                if (targetTrack.Phonemizer.GetType() != track.Phonemizer.GetType()) {
                    var phonemizer = phonemizerFactory?.Create();
                    if (phonemizer != null) {
                        DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(project, targetTrack, phonemizer));
                    }
                }
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(
                    project, targetTrack, track.RendererSettings.Clone()));
                DocManager.Inst.ExecuteCmd(new TrackChangeSettingsCommand(
                    project, targetTrack, track.Mute, track.Volume, track.Pan));
                DocManager.Inst.ExecuteCmd(new ChangeTrackColorCommand(project, targetTrack, track.TrackColor));
                DocManager.Inst.ExecuteCmd(new ConfigureExpressionsCommand(
                    project,
                    project.expressions.Values.ToArray(),
                    targetTrack,
                    track.TrackExpressions.Select(exp => exp.Clone()).ToArray()));
            }
            DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new TracksRefreshEvent());
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("TrackColor"));
        }

        /// <summary>
        /// Shifts singers one track along among the selected tracks (all tracks when fewer than two
        /// are selected); the last track receives the first track's singer.
        /// </summary>
        public void RotateSelectedTrackSingers() {
            var targetTracks = GetBatchTargetTracks();
            if (targetTracks.Count <= 1) {
                targetTracks = DocManager.Inst.Project.tracks.ToList();
            }
            if (targetTracks.Count <= 1) {
                return;
            }
            var singers = targetTracks.Select(t => t.Singer).ToList();
            DocManager.Inst.StartUndoGroup("command.track.singer");
            for (int i = 0; i < targetTracks.Count; i++) {
                ApplySingerToTrack(targetTracks[i], singers[(i + 1) % singers.Count]);
            }
            DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
            DocManager.Inst.EndUndoGroup();
            MessageBus.Current.SendMessage(new TracksRefreshEvent());
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
        }

        public void VoiceColorRemapping() {
            if (track.Singer != null && track.Singer.Found && track.VoiceColorExp != null) {
                DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(track.TrackNo, false));
            }
        }

        public void OpenMixFxDialog() {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                MixFxDialog.Open(desktop.MainWindow, track);
            }
        }
    }
}
