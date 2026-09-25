using System;
using System.Collections.Generic;
using System.Globalization;
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
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public partial class SingerTileViewModel : ViewModelBase {
        public USinger Singer { get; }
        public string Name => Singer.LocalizedName;
        public string? Location => Singer.Location;
        /// <summary>Strings the search box matches against.</summary>
        public IReadOnlyList<string> SearchTerms { get; }
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
            SearchTerms = new[] { Name };
            Avatar = SingerAvatarCache.Get(singer, bitmap => Avatar = bitmap);
        }

        const CompareOptions SearchOptions =
            CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

        /// <summary>
        /// Whether any search term contains the query, ignoring case, width and hiragana/katakana.
        /// An empty query matches everything.
        /// </summary>
        public bool Matches(string query) => Matches(SearchTerms, query);

        public static bool Matches(IEnumerable<string> terms, string query) {
            return string.IsNullOrEmpty(query) || terms.Any(term =>
                !string.IsNullOrEmpty(term) &&
                CultureInfo.InvariantCulture.CompareInfo.IndexOf(term, query, SearchOptions) >= 0);
        }
    }

    public partial class SingerFlyoutViewModel : ViewModelBase, ICmdSubscriber {
        /// <summary>The tiles matching the search, section after section.</summary>
        [Reactive] public partial IReadOnlyList<SingerTileViewModel> Tiles { get; set; } = Array.Empty<SingerTileViewModel>();
        /// <summary>Indices into <see cref="Tiles"/> where each section after the first begins. Updated before Tiles.</summary>
        public IReadOnlyList<int> SectionStarts { get; private set; } = Array.Empty<int>();
        /// <summary>Number of tiles without a search, which the flyout is sized for.</summary>
        [Reactive] public partial int AllTileCount { get; set; }
        [Reactive] public partial bool IsEmpty { get; set; }
        [Reactive] public partial bool NoMatch { get; set; }
        [Reactive] public partial string SearchText { get; set; } = string.Empty;
        public bool HasAdditionalSingersFolder =>
            !string.IsNullOrWhiteSpace(PathManager.Inst.AdditionalSingersPath) &&
            Directory.Exists(PathManager.Inst.AdditionalSingersPath);

        /// <summary>Raised when the flyout should close, e.g. after a singer is selected.</summary>
        public event Action? CloseRequested;

        private readonly Func<USinger?> getCurrentSinger;
        private readonly ICommand selectSingerCommand;
        private IReadOnlyList<SingerTileViewModel[]> sections = Array.Empty<SingerTileViewModel[]>();

        public SingerFlyoutViewModel(Func<USinger?> getCurrentSinger, ICommand selectSingerCommand) {
            this.getCurrentSinger = getCurrentSinger;
            this.selectSingerCommand = selectSingerCommand;
            Rebuild();
            this.WhenAnyValue(x => x.SearchText).Subscribe(_ => ApplySearch());
        }

        public void Rebuild() {
            var current = getCurrentSinger();
            var singerSections = OrderSingers(
                SingerManager.Inst.Singers,
                SingerManager.Inst.SingerGroups,
                Preferences.Default.RecentSingers,
                Preferences.Default.FavoriteSingers);
            // A missing singer isn't in SingerManager but should still be shown, in a section of its own,
            // when it is the track's singer.
            if (current != null && !current.Found && !string.IsNullOrEmpty(current.Name)) {
                singerSections.Insert(0, new List<USinger> { current });
            }
            sections = singerSections
                .Where(section => section.Count > 0)
                .Select(section => section
                    .Select(singer => new SingerTileViewModel(singer, current != null && singer.Equals(current)))
                    .ToArray())
                .ToArray();
            AllTileCount = sections.Sum(section => section.Length);
            IsEmpty = AllTileCount == 0;
            ApplySearch();
            this.RaisePropertyChanged(nameof(HasAdditionalSingersFolder));
        }

        void ApplySearch() {
            var query = SearchText.Trim();
            var tiles = new List<SingerTileViewModel>();
            var starts = new List<int>();
            foreach (var section in sections) {
                int start = tiles.Count;
                tiles.AddRange(section.Where(tile => tile.Matches(query)));
                if (start > 0 && tiles.Count > start) {
                    starts.Add(start);
                }
            }
            SectionStarts = starts;
            Tiles = tiles;
            NoMatch = !IsEmpty && tiles.Count == 0;
        }

        /// <summary>Selects the first tile matching the search, if any.</summary>
        public void SelectFirst() {
            if (Tiles.Count > 0) {
                Select(Tiles[0]);
            }
        }

        /// <summary>
        /// Three sections, each possibly empty: favorites, recent non-favorites, then everything else.
        /// Favorites are the recent ones (most recent first), then the rest alphabetically.
        /// Recent non-favorites are most recent first; everything else is by group name and singer name.
        /// Each singer appears once.
        /// </summary>
        public static List<List<USinger>> OrderSingers(
                IReadOnlyDictionary<string, USinger> singers,
                IReadOnlyDictionary<USingerType, List<USinger>> groups,
                IEnumerable<string> recentIds,
                IEnumerable<string> favoriteIds) {
            var result = new List<List<USinger>> { new List<USinger>(), new List<USinger>(), new List<USinger>() };
            var added = new HashSet<string>();
            void Add(int section, USinger singer) {
                if (added.Add(singer.Id)) {
                    result[section].Add(singer);
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
                Add(0, singer);
            }
            foreach (var singer in favorites.LocalizedOrderBy(singer => singer.LocalizedName)) {
                Add(0, singer);
            }
            foreach (var singer in recents) {
                Add(1, singer);
            }
            foreach (var pair in groups.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)) {
                foreach (var singer in pair.Value) {
                    Add(2, singer);
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
