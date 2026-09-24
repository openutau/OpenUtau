using System;
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
    }
}
