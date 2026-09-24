using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public partial class SingerTileViewModel : ViewModelBase {
        public USinger Singer { get; }
        public string Name => Singer.LocalizedName;
        public string? Location => Singer.Location;
        public bool IsCurrent { get; }
        public bool IsMissing => !Singer.Found;
        public bool IsFavourite {
            get => Singer.IsFavourite;
            set {
                if (Singer.IsFavourite != value) {
                    Singer.IsFavourite = value;
                    this.RaisePropertyChanged();
                }
            }
        }
        [Reactive] public partial Bitmap? Avatar { get; set; }

        public SingerTileViewModel(USinger singer, bool isCurrent) {
            Singer = singer;
            IsCurrent = isCurrent;
            Avatar = SingerAvatarCache.Get(singer, bitmap => Avatar = bitmap);
        }
    }

    public partial class SingerFlyoutViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public partial IReadOnlyList<SingerTileViewModel> Tiles { get; set; } = Array.Empty<SingerTileViewModel>();
        [Reactive] public partial bool IsEmpty { get; set; }
        public bool HasAdditionalSingersFolder =>
            !string.IsNullOrWhiteSpace(PathManager.Inst.AdditionalSingersPath) &&
            Directory.Exists(PathManager.Inst.AdditionalSingersPath);

        /// <summary>Raised when the flyout should close, e.g. after a singer is selected.</summary>
        public event Action? CloseRequested;

        private readonly Func<USinger?> getCurrentSinger;
        private readonly ICommand selectSingerCommand;

        public SingerFlyoutViewModel(Func<USinger?> getCurrentSinger, ICommand selectSingerCommand) {
            this.getCurrentSinger = getCurrentSinger;
            this.selectSingerCommand = selectSingerCommand;
            Rebuild();
        }

        public void Rebuild() {
            var current = getCurrentSinger();
            var singers = OrderSingers(
                SingerManager.Inst.Singers,
                SingerManager.Inst.SingerGroups,
                Preferences.Default.RecentSingers,
                Preferences.Default.FavoriteSingers);
            // A missing singer isn't in SingerManager but should still be shown when it is the track's singer.
            if (current != null && !current.Found && !string.IsNullOrEmpty(current.Name)) {
                singers.Insert(0, current);
            }
            Tiles = singers
                .Select(singer => new SingerTileViewModel(singer, current != null && singer.Equals(current)))
                .ToArray();
            IsEmpty = Tiles.Count == 0;
            this.RaisePropertyChanged(nameof(HasAdditionalSingersFolder));
        }

        /// <summary>
        /// Favorites always come before non-favorites. Within each, recent singers come first:
        /// recent favorites (most recent first), remaining favorites alphabetically,
        /// recent non-favorites, then everything else by group name and singer name.
        /// Each singer appears once.
        /// </summary>
        public static List<USinger> OrderSingers(
                IReadOnlyDictionary<string, USinger> singers,
                IReadOnlyDictionary<USingerType, List<USinger>> groups,
                IEnumerable<string> recentIds,
                IEnumerable<string> favoriteIds) {
            var result = new List<USinger>();
            var added = new HashSet<string>();
            void Add(USinger singer) {
                if (added.Add(singer.Id)) {
                    result.Add(singer);
                }
            }
            IEnumerable<USinger> Lookup(IEnumerable<string> ids) {
                foreach (var id in ids) {
                    if (!string.IsNullOrWhiteSpace(id) && singers.TryGetValue(id, out var singer) && singer != null) {
                        yield return singer;
                    }
                }
            }
            var favorites = Lookup(favoriteIds).ToList();
            var favoriteSet = favorites.Select(singer => singer.Id).ToHashSet();
            var recents = Lookup(recentIds).ToList();
            foreach (var singer in recents.Where(singer => favoriteSet.Contains(singer.Id))) {
                Add(singer);
            }
            foreach (var singer in favorites.LocalizedOrderBy(singer => singer.LocalizedName)) {
                Add(singer);
            }
            foreach (var singer in recents) {
                Add(singer);
            }
            foreach (var pair in groups.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)) {
                foreach (var singer in pair.Value) {
                    Add(singer);
                }
            }
            return result;
        }

        public void Select(SingerTileViewModel tile) {
            CloseRequested?.Invoke();
            selectSingerCommand.Execute(tile.Singer);
        }

        public async void InstallSinger() {
            CloseRequested?.Invoke();
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow as MainWindow;
            if (mainWindow == null) {
                return;
            }
            var file = await FilePicker.OpenFileAboutSinger(
                mainWindow, "menu.tools.singer.install", FilePicker.ArchiveFiles);
            if (file == null) {
                return;
            }
            try {
                if (file.EndsWith(Core.Vogen.VogenSingerInstaller.FileExt)) {
                    Core.Vogen.VogenSingerInstaller.Install(file);
                    return;
                }
                if (file.EndsWith(PackageManager.OudepExt)) {
                    await PackageManager.Inst.InstallFromFileAsync(file);
                    return;
                }

                var setup = new SingerSetupDialog() {
                    DataContext = new SingerSetupViewModel() {
                        ArchiveFilePath = file,
                    },
                };
                _ = setup.ShowDialog(mainWindow);
                if (setup.Position.Y < 0) {
                    setup.Position = setup.Position.WithY(0);
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to install singer {file}");
                _ = await MessageBox.ShowError(mainWindow, new MessageCustomizableException($"Failed to install singer {file}", $"<translate:errors.failed.installsinger>: {file}", e));
            }
        }

        public void OpenSingersFolder() {
            OpenFolder(PathManager.Inst.SingersPath);
        }

        public void OpenAdditionalSingersFolder() {
            OpenFolder(PathManager.Inst.AdditionalSingersPath);
        }

        void OpenFolder(string path) {
            CloseRequested?.Invoke();
            try {
                OS.OpenFolder(path);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void RefreshSingers() {
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), true, "singer"));
            SingerManager.Inst.SearchAllSingers();
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), false, "singer"));
        }

        // Subscribed only while the flyout is open, so the grid follows singer rescans and installs.
        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SingersChangedNotification ||
                cmd is SingersRefreshedNotification { singer: null }) {
                Dispatcher.UIThread.Post(Rebuild);
            }
        }
    }
}
