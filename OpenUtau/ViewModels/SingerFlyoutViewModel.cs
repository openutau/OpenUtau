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
        public IReadOnlyList<string> SearchTerms { get; }
        public string? ToolTipText { get; }
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
            SearchTerms = BuildSearchTerms(singer);
            // Leave out what the tile and the path already show.
            var extra = SearchTerms
                .Where(term => term != Name &&
                    (Location == null || !Location.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            ToolTipText = extra.Count == 0
                ? Location
                : $"{Location}\n{ThemeManager.GetString("tracks.searchterms")}: {string.Join(", ", extra)}".Trim();
            Avatar = SingerAvatarCache.Get(singer, bitmap => Avatar = bitmap);
        }

        /// <summary>
        /// The displayed and original names, all localized names, the id and folder name, which are often
        /// romanized, then the extra terms in the voicebank config.
        /// </summary>
        public static IReadOnlyList<string> BuildSearchTerms(USinger singer) {
            var terms = new List<string> { singer.LocalizedName, singer.Name };
            if (singer.LocalizedNames != null) {
                terms.AddRange(singer.LocalizedNames.Values);
            }
            terms.Add(singer.Id);
            if (!string.IsNullOrEmpty(singer.Location)) {
                terms.Add(Path.GetFileName(singer.Location));
            }
            terms.AddRange(singer.SearchTerms ?? Array.Empty<string>());
            return terms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .Distinct()
                .ToArray();
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

    /// <summary>
    /// A singer type chip of the flyout's filter row. Every chip is lit while showing all types,
    /// then only the chosen one is.
    /// </summary>
    public class SingerTypeChipViewModel : ViewModelBase {
        /// <summary>Null for the "All" chip.</summary>
        public USingerType? Type { get; }
        public string Label { get; }
        public bool IsOn { get; }
        public string ToolTipText { get; }

        public SingerTypeChipViewModel(USingerType? type, string label, bool isOn, string toolTipText) {
            Type = type;
            Label = label;
            IsOn = isOn;
            ToolTipText = toolTipText;
        }
    }

    public partial class SingerFlyoutViewModel : ViewModelBase, ICmdSubscriber {
        /// <summary>Singer types in the order the flyout's sections use them.</summary>
        static readonly USingerType[] TypeOrder = {
            USingerType.Classic, USingerType.DiffSinger, USingerType.Enunu, USingerType.Voicevox, USingerType.Vogen,
        };

        [Reactive] public partial IReadOnlyList<SingerTileViewModel> Tiles { get; set; } = Array.Empty<SingerTileViewModel>();
        public IReadOnlyList<int> SectionStarts { get; private set; } = Array.Empty<int>();
        [Reactive] public partial IReadOnlyList<SingerTypeChipViewModel> TypeChips { get; private set; } = Array.Empty<SingerTypeChipViewModel>();

        /// <summary>The singer type filter, or null for all of them. Kept only while the flyout is open.</summary>
        [Reactive] public partial USingerType? TypeFilter { get; set; }

        /// <summary>The filter row is only worth its space when there is more than one type to choose from.</summary>
        public bool ShowTypeFilter => TypeChips.Count > 1;

        /// <summary>
        /// Tiles the grid shows for the current type filter, ignoring the search text.
        /// Sizes the flyout, so searching doesn't resize it while filtering does.
        /// </summary>
        [Reactive] public partial int GridTileCount { get; set; }

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
        private IReadOnlyList<USingerType> availableTypes = Array.Empty<USingerType>();

        public SingerFlyoutViewModel(Func<USinger?> getCurrentSinger, ICommand selectSingerCommand) {
            this.getCurrentSinger = getCurrentSinger;
            this.selectSingerCommand = selectSingerCommand;
            Rebuild();
            this.WhenAnyValue(x => x.SearchText).Subscribe(_ => ApplyFilters());
            this.WhenAnyValue(x => x.TypeFilter).Subscribe(_ => ApplyTypeFilter());
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
            var types = sections.SelectMany(section => section)
                .Select(tile => tile.Singer.SingerType)
                .ToHashSet();
            availableTypes = TypeOrder.Where(types.Contains).ToArray();
            if (TypeFilter is { } filter && !availableTypes.Contains(filter)) {
                // The filtered type has no singers anymore.
                TypeFilter = null;
            }
            IsEmpty = sections.Sum(section => section.Length) == 0;
            ApplyTypeFilter();
            this.RaisePropertyChanged(nameof(HasAdditionalSingersFolder));
        }

        /// <summary>
        /// All-hot while no type is picked, one-hot for a picked type.
        /// Clicking the lit type, or the All chip, goes back to all-hot.
        /// </summary>
        public void ToggleTypeFilter(USingerType? type) {
            TypeFilter = type == null || TypeFilter == type ? null : type;
        }

        void ApplyTypeFilter() {
            GridTileCount = CountForType();
            BuildTypeChips();
            this.RaisePropertyChanged(nameof(ShowTypeFilter));
            ApplyFilters();
        }

        int CountForType() => sections.Sum(section => section.Count(tile => MatchesTypeFilter(tile.Singer.SingerType)));

        void BuildTypeChips() {
            var chips = new List<SingerTypeChipViewModel> {
                new SingerTypeChipViewModel(null,
                    ThemeManager.GetString("tracks.filtertype.all"), TypeFilter == null,
                    ThemeManager.GetString("tracks.filtertype.all.tip")),
            };
            foreach (var type in availableTypes) {
                var label = SingerTypeUtils.SingerTypeNames.TryGetValue(type, out var name) ? name : type.ToString();
                chips.Add(new SingerTypeChipViewModel(type, label, TypeFilter == null || TypeFilter == type,
                    string.Format(ThemeManager.GetString("tracks.filtertype.only"), label)));
            }
            TypeChips = chips;
        }

        void ApplyFilters() {
            var query = SearchText.Trim();
            var tiles = new List<SingerTileViewModel>();
            var starts = new List<int>();
            foreach (var section in sections) {
                int start = tiles.Count;
                tiles.AddRange(section.Where(tile => MatchesTypeFilter(tile.Singer.SingerType) && tile.Matches(query)));
                if (start > 0 && tiles.Count > start) {
                    starts.Add(start);
                }
            }
            SectionStarts = starts;
            Tiles = tiles;
            NoMatch = !IsEmpty && tiles.Count == 0;
        }

        bool MatchesTypeFilter(USingerType type) =>
            TypeFilter is not { } filter || type == filter;

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

        public bool IsRecent(SingerTileViewModel tile) {
            return Preferences.Default.RecentSingers.Contains(tile.Singer.Id);
        }

        public void RemoveFromRecent(SingerTileViewModel tile) {
            Preferences.Default.RecentSingers.Remove(tile.Singer.Id);
            Preferences.Save();
            Rebuild();
        }

        public void OpenLocation(SingerTileViewModel tile) {
            CloseRequested?.Invoke();
            SingersViewModel.OpenSingerLocation(tile.Singer);
        }

        public void EditSearchTerms(SingerTileViewModel tile) {
            CloseRequested?.Invoke();
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow == null) {
                return;
            }
            SingersDialog.ShowSearchTermsDialog(mainWindow, tile.Singer,
                text => SingersViewModel.SetSearchTerms(tile.Singer, text));
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
