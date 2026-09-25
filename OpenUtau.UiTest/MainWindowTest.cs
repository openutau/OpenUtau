using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// Smoke tests that launch the real main window on the headless platform.
    /// Checks are meant to survive UI redesigns: no errors, the right app state (from view models),
    /// key parts present with real size (by type or x:Name, not text or position), and a non-blank frame.
    /// </summary>
    public class MainWindowTest {
        static bool coreInitialized;

        // What SplashWindow.Start does before showing the main window, minus audio. The splash itself
        // can't run headless: it waits for screens, which the headless platform doesn't report.
        static void InitCore() {
            if (coreInitialized) {
                return;
            }
            coreInitialized = true;
            UpdaterDialog.CheckForUpdateEnabled = false;
            ToolsManager.Inst.Initialize();
            SingerManager.Inst.Initialize();
            DocManager.Inst.Initialize(Thread.CurrentThread, TaskScheduler.FromCurrentSynchronizationContext());
            DocManager.Inst.PostOnUIThread = action => Avalonia.Threading.Dispatcher.UIThread.Post(action);
            DocManager.Inst.AddSubscriber(HeadlessUi.Errors);
        }

        // Opens the main window as the splash window would, runs the test, then saves a screenshot
        // of the final state (also when the test fails) and closes the window.
        static void WithMainWindow(string name, Action<MainWindow> test) {
            HeadlessUi.Run(() => {
                InitCore();
                HeadlessUi.Errors.Clear();
                var window = new MainWindow { Width = 1280, Height = 800 };
                try {
                    window.Show();
                    window.InitProject();
                    HeadlessUi.Flush();
                    test(window);
                    HeadlessUi.Flush();
                    var errors = HeadlessUi.Errors.Snapshot();
                    Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
                } finally {
                    HeadlessUi.Flush();
                    HeadlessUi.SaveScreenshot(window, name);
                    window.Close();
                }
            });
        }

        static void AssertHasSize(Control? control) {
            Assert.NotNull(control);
            Assert.True(control.IsEffectivelyVisible, $"{control} is not visible");
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0, $"{control} has no size");
        }

        static void AssertRendered(Window window) {
            // A blank window has one or two colors; the smallest drawn page has dozens.
            Assert.True(HeadlessUi.CountRenderedColors(window) > 10, "The window looks blank");
        }

        static void Click(Window window, Control target) {
            var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
            Assert.NotNull(center);
            window.MouseDown(center.Value, MouseButton.Left);
            window.MouseUp(center.Value, MouseButton.Left);
            HeadlessUi.Flush();
        }

        // Finds a button by its localized label's resource key, so it survives text and layout changes.
        static Button FindButtonWithText(Window window, string resourceKey) {
            var text = window.FindResource(resourceKey) as string;
            Assert.False(string.IsNullOrEmpty(text));
            var button = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsEffectivelyVisible &&
                    b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
            Assert.NotNull(button);
            return button;
        }

        static MainWindowViewModel ViewModelOf(Window window) => Assert.IsType<MainWindowViewModel>(window.DataContext);

        [Fact]
        public void LaunchShowsStartPage() {
            WithMainWindow(nameof(LaunchShowsStartPage), window => {
                Assert.Equal(0, ViewModelOf(window).Page);
                AssertHasSize(window.GetVisualDescendants().OfType<Menu>().FirstOrDefault());
                AssertHasSize(FindButtonWithText(window, "welcome.new"));
                AssertRendered(window);
            });
        }

        [Fact]
        public void NewProjectOpensEditor() {
            WithMainWindow(nameof(NewProjectOpensEditor), window => {
                Click(window, FindButtonWithText(window, "welcome.new"));

                Assert.Equal(1, ViewModelOf(window).Page);
                var trackCount = DocManager.Inst.Project.tracks.Count;
                Assert.True(trackCount > 0);
                // One header per track in the new project.
                var headers = window.GetVisualDescendants().OfType<TrackHeader>().ToList();
                Assert.Equal(trackCount, headers.Count);
                Assert.All(headers, header => AssertHasSize(header));
                AssertHasSize(window.FindControl<Control>("partsCanvas"));
                AssertRendered(window);
            });
        }

        class FakeSinger : USinger {
            readonly string name;
            public FakeSinger(string name) {
                this.name = name;
                found = true;
            }
            public override string Id => name;
            public override string Name => name;
            public override USingerType SingerType => USingerType.Classic;
        }

        // Adds singers that aren't loadable, so the test must not select them.
        static void WithFakeSingers(int count, IEnumerable<int> favorites, IEnumerable<int> recents, Action<List<FakeSinger>> test) {
            var singers = Enumerable.Range(0, count).Select(i => new FakeSinger($"Fake Singer {i:00}")).ToList();
            var manager = SingerManager.Inst;
            var prefs = Preferences.Default;
            var (oldFavorites, oldRecents) = (prefs.FavoriteSingers, prefs.RecentSingers);
            manager.SingerGroups.TryGetValue(USingerType.Classic, out var oldGroup);
            try {
                singers.ForEach(singer => manager.Singers[singer.Id] = singer);
                manager.SingerGroups[USingerType.Classic] = (oldGroup ?? new List<USinger>()).Concat(singers).ToList();
                prefs.FavoriteSingers = favorites.Select(i => singers[i].Id).ToList();
                prefs.RecentSingers = recents.Select(i => singers[i].Id).ToList();
                test(singers);
            } finally {
                singers.ForEach(singer => manager.Singers.Remove(singer.Id));
                if (oldGroup == null) {
                    manager.SingerGroups.Remove(USingerType.Classic);
                } else {
                    manager.SingerGroups[USingerType.Classic] = oldGroup;
                }
                (prefs.FavoriteSingers, prefs.RecentSingers) = (oldFavorites, oldRecents);
            }
        }

        [Fact]
        public void SingerFlyoutSearches() {
            WithMainWindow(nameof(SingerFlyoutSearches), window => WithFakeSingers(11, new[] { 0, 1, 2 }, new[] { 5, 6, 7 }, singers => {
                Click(window, FindButtonWithText(window, "welcome.new"));
                var header = window.GetVisualDescendants().OfType<TrackHeader>().First();
                Click(window, header.FindControl<Button>("SingerButton")!);

                var flyout = window.GetVisualDescendants().OfType<SingerFlyout>().SingleOrDefault();
                AssertHasSize(flyout);
                var viewModel = Assert.IsType<SingerFlyoutViewModel>(flyout.DataContext);
                var searchBox = flyout.FindControl<TextBox>("SearchBox");
                var grid = flyout.FindControl<ScrollViewer>("TileScroller");
                Assert.NotNull(searchBox);
                AssertHasSize(grid);
                // Favorites, recents, then the rest (with any real singers after the fake ones).
                Assert.Equal(new[] { 3, 6 }, viewModel.SectionStarts);
                // Typing searches right away.
                Assert.Same(searchBox, window.FocusManager?.GetFocusedElement());
                var gridSize = grid.Bounds.Size;

                window.KeyTextInput("fake singer 1");
                HeadlessUi.Flush();
                Assert.Equal(new[] { singers[10] }, viewModel.Tiles.Select(tile => tile.Singer));
                Assert.Empty(viewModel.SectionStarts);
                // Sections without matches get no divider.
                viewModel.SearchText = "FAKE SINGER 0";
                HeadlessUi.Flush();
                Assert.Equal(new[] { 0, 1, 2, 5, 6, 7, 3, 4, 8, 9 }.Select(i => singers[i]), viewModel.Tiles.Select(tile => tile.Singer));
                Assert.Equal(new[] { 3, 6 }, viewModel.SectionStarts);
                viewModel.SearchText = "fake singer 01";
                HeadlessUi.Flush();
                Assert.Equal(new[] { singers[1] }, viewModel.Tiles.Select(tile => tile.Singer));
                Assert.Empty(viewModel.SectionStarts);
                // The flyout keeps its size while searching.
                Assert.Equal(gridSize, grid.Bounds.Size);

                viewModel.SearchText = "no such singer";
                HeadlessUi.Flush();
                Assert.Empty(viewModel.Tiles);
                Assert.True(viewModel.NoMatch);

                // Escape clears the search before closing the flyout.
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                HeadlessUi.Flush();
                Assert.Equal(string.Empty, viewModel.SearchText);
                Assert.Equal(viewModel.AllTileCount, viewModel.Tiles.Count);
                Assert.True(flyout.IsEffectivelyVisible);
                AssertRendered(window);
            }));
        }
    }
}
