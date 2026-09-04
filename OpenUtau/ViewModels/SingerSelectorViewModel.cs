using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class SingerSelectorViewModel : ViewModelBase {
        public class SingerEngineGroup {
            public USingerType Type { get; }
            public string Header { get; }
            public IReadOnlyList<SingerOption> Singers { get; }

            public SingerEngineGroup(USingerType type, string header, IReadOnlyList<SingerOption> singers) {
                Type = type;
                Header = header;
                Singers = singers;
            }

            public override string ToString() => Header;
        }

        public class SingerOption : ReactiveObject {
            public USinger Singer { get; }
            public string Name => Singer.LocalizedName;
            public string Id => Singer.Id;
            public string Location => Singer.Location;
            readonly Action<string>? onFavouriteChanged;
            public bool IsFavourite {
                get => Singer.IsFavourite;
                set {
                    if (Singer.IsFavourite == value) {
                        return;
                    }
                    Singer.IsFavourite = value;
                    this.RaisePropertyChanged(nameof(IsFavourite));
                    onFavouriteChanged?.Invoke(Singer.Id);
                }
            }
            public Bitmap? Avatar { get; }

            public SingerOption(USinger singer, Action<string>? onFavouriteChanged = null) {
                Singer = singer;
                this.onFavouriteChanged = onFavouriteChanged;
                Avatar = LoadAvatar(singer);
            }

            static Bitmap? LoadAvatar(USinger singer) {
                return LoadAvatarBitmap(singer);
            }
        }

        [Reactive] public IReadOnlyList<SingerEngineGroup> EngineGroups { get; private set; } =
            Array.Empty<SingerEngineGroup>();
        [Reactive] public SingerEngineGroup? SelectedEngine { get; set; }
        [Reactive] public SingerOption? SelectedSingerOption { get; set; }
        [Reactive] public string SearchText { get; set; } = string.Empty;
        [Reactive] public bool ShowFavoritesOnly { get; set; }
        [Reactive] public IReadOnlyList<SingerOption> FilteredCurrentSingers { get; private set; } =
            Array.Empty<SingerOption>();
        [Reactive] public string SelectedSingerName { get; private set; } = string.Empty;
        [Reactive] public string SelectedSingerSubtitle { get; private set; } = string.Empty;
        [Reactive] public string SelectedSingerInfo { get; private set; } = string.Empty;
        [Reactive] public Bitmap? SelectedSingerPortrait { get; private set; }
        [Reactive] public bool HasSelectedSinger { get; private set; }

        public IReadOnlyList<SingerOption> CurrentSingers =>
            SelectedEngine?.Singers ?? Array.Empty<SingerOption>();
        public USinger? SelectedSinger => SelectedSingerOption?.Singer;
        public bool HasFilteredSingers => FilteredCurrentSingers.Count > 0;

        static readonly USingerType[] engineOrder = {
            USingerType.Classic,
            USingerType.Enunu,
            USingerType.DiffSinger,
            USingerType.Voicevox,
            USingerType.Vogen,
        };

        public SingerSelectorViewModel(USinger? currentSinger = null) {
            this.WhenAnyValue(x => x.SelectedEngine)
                .Subscribe(engine => {
                    ApplyFilterAndKeepSelection(SelectedSinger?.Id);
                });
            this.WhenAnyValue(x => x.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(120), RxApp.MainThreadScheduler)
                .Subscribe(_ => {
                    ApplyFilterAndKeepSelection(SelectedSinger?.Id);
                });
            this.WhenAnyValue(x => x.ShowFavoritesOnly)
                .Subscribe(_ => {
                    ApplyFilterAndKeepSelection(SelectedSinger?.Id);
                });
            this.WhenAnyValue(x => x.SelectedSingerOption)
                .Subscribe(option => {
                    this.RaisePropertyChanged(nameof(SelectedSinger));
                    UpdateSingerDetails(option?.Singer);
                });
            RebuildGroups(currentSinger?.Id, currentSinger?.SingerType);
        }

        public void RefreshSingers() {
            var singerId = SelectedSinger?.Id;
            var singerType = SelectedEngine?.Type;
            SingerManager.Inst.SearchAllSingers();
            RebuildGroups(singerId, singerType);
        }

        void RebuildGroups(string? selectedSingerId, USingerType? selectedSingerType) {
            var groups = SingerManager.Inst.SingerGroups
                .OrderBy(group => GetEngineSortIndex(group.Key))
                .ThenBy(group => group.Key.ToString())
                .Select(group => new SingerEngineGroup(
                    group.Key,
                    GetEngineDisplayName(group.Key),
                    group.Value.Select(singer =>
                        new SingerOption(singer, OnSingerFavouriteChanged)).ToList()))
                .Where(group => group.Singers.Count > 0)
                .ToList();
            EngineGroups = groups;
            if (groups.Count == 0) {
                SelectedEngine = null;
                SelectedSingerOption = null;
                FilteredCurrentSingers = Array.Empty<SingerOption>();
                this.RaisePropertyChanged(nameof(HasFilteredSingers));
                return;
            }
            SingerEngineGroup? preferred = null;
            if (selectedSingerType.HasValue) {
                preferred = groups.FirstOrDefault(group => group.Type == selectedSingerType.Value);
            }
            if (preferred == null && !string.IsNullOrEmpty(selectedSingerId)) {
                preferred = groups.FirstOrDefault(group =>
                    group.Singers.Any(item => item.Singer.Id == selectedSingerId));
            }
            SelectedEngine = preferred ?? groups.First();
            if (!string.IsNullOrEmpty(selectedSingerId) && SelectedEngine != null) {
                SelectedSingerOption = SelectedEngine.Singers
                    .FirstOrDefault(item => item.Singer.Id == selectedSingerId)
                    ?? SelectedEngine.Singers.FirstOrDefault();
            } else if (SelectedEngine != null) {
                SelectedSingerOption = SelectedEngine.Singers.FirstOrDefault();
            }
            ApplyFilterAndKeepSelection(selectedSingerId);
        }

        void ApplyFilterAndKeepSelection(string? preferredSingerId) {
            var singers = CurrentSingers;
            IEnumerable<SingerOption> query = singers;
            if (ShowFavoritesOnly) {
                query = query.Where(item => item.IsFavourite);
            }
            var keyword = SearchText?.Trim();
            if (!string.IsNullOrEmpty(keyword)) {
                var lower = keyword.ToLowerInvariant();
                query = query.Where(item =>
                        item.Name.ToLowerInvariant().Contains(lower) ||
                        item.Id.ToLowerInvariant().Contains(lower));
            }
            var filtered = query.ToList();
            FilteredCurrentSingers = filtered;
            this.RaisePropertyChanged(nameof(HasFilteredSingers));
            if (filtered.Count == 0) {
                SelectedSingerOption = null;
                return;
            }
            var keep = string.IsNullOrEmpty(preferredSingerId)
                ? null
                : filtered.FirstOrDefault(item => item.Singer.Id == preferredSingerId);
            SelectedSingerOption = keep ?? filtered.First();
        }

        void OnSingerFavouriteChanged(string preferredSingerId) {
            ApplyFilterAndKeepSelection(preferredSingerId);
        }

        static int GetEngineSortIndex(USingerType type) {
            var index = Array.IndexOf(engineOrder, type);
            return index >= 0 ? index : 999;
        }

        static string GetEngineDisplayName(USingerType type) {
            return type switch {
                USingerType.Classic => "UTAU",
                USingerType.Enunu => "ENUNU",
                USingerType.DiffSinger => "DiffSinger",
                USingerType.Voicevox => "VOICEVOX",
                USingerType.Vogen => "Vogen",
                _ => type.ToString(),
            };
        }

        void UpdateSingerDetails(USinger? singer) {
            if (singer == null) {
                SelectedSingerName = ThemeManager.GetString("tracks.nosinger");
                SelectedSingerSubtitle = string.Empty;
                SelectedSingerInfo = string.Empty;
                SelectedSingerPortrait = null;
                HasSelectedSinger = false;
                return;
            }
            try {
                singer.EnsureLoaded();
            } catch (Exception e) {
                Log.Error(e, $"Failed to load singer {singer.Id} in selector.");
            }
            var engineName = GetEngineDisplayName(singer.SingerType);
            var errors = singer.Errors ?? Array.Empty<string>();
            SelectedSingerName = singer.LocalizedName;
            SelectedSingerSubtitle = string.IsNullOrWhiteSpace(singer.Author)
                ? engineName
                : $"{engineName} - {singer.Author}";
            SelectedSingerInfo =
                $"ID: {singer.Id}\n" +
                $"Voice: {singer.Voice}\n" +
                $"Web: {singer.Web}\n" +
                $"Version: {singer.Version}\n\n" +
                $"{singer.OtherInfo}\n\n" +
                $"{string.Join("\n", errors)}";
            SelectedSingerPortrait = LoadPortraitOrAvatar(singer);
            HasSelectedSinger = true;
        }

        static Bitmap? LoadPortraitOrAvatar(USinger singer) {
            try {
                var portraitData = singer.LoadPortrait();
                if (portraitData != null && portraitData.Length > 0) {
                    using var stream = new MemoryStream(portraitData);
                    return new Bitmap(stream);
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to decode singer portrait for {singer.Id}.");
            }
            return LoadAvatarBitmap(singer);
        }

        static Bitmap? LoadAvatarBitmap(USinger singer) {
            if (singer.AvatarData == null && string.IsNullOrWhiteSpace(singer.Avatar)) {
                try {
                    singer.EnsureLoaded();
                } catch (Exception e) {
                    Log.Error(e, $"Failed to ensure singer loaded for avatar {singer.Id}.");
                }
            }
            if (singer.AvatarData == null) {
                if (!string.IsNullOrWhiteSpace(singer.Avatar) && File.Exists(singer.Avatar)) {
                    try {
                        return new Bitmap(singer.Avatar);
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to decode singer avatar file for {singer.Id}.");
                        return null;
                    }
                }
                return null;
            }
            try {
                using var stream = new MemoryStream(singer.AvatarData);
                return new Bitmap(stream);
            } catch (Exception e) {
                Log.Error(e, $"Failed to decode singer avatar for {singer.Id}.");
                if (!string.IsNullOrWhiteSpace(singer.Avatar) && File.Exists(singer.Avatar)) {
                    try {
                        return new Bitmap(singer.Avatar);
                    } catch (Exception e2) {
                        Log.Error(e2, $"Failed to decode singer avatar file for {singer.Id}.");
                    }
                }
                return null;
            }
        }
    }
}
