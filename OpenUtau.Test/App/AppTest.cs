using Xunit;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Threading;
using System.Linq;
using System.Threading;
using OpenUtau.App.Views;
using OpenUtau.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

public class TestAppBuilder {
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

namespace OpenUtau.App {
    public class AppTest {
        [Fact]
        public void BuildTest() {
            Assert.False(typeof(App).IsAbstract);
            Assert.False(typeof(Program).IsAbstract);
        }

        [Fact]
        public void StringsTest() {
            var appBuilder = TestAppBuilder.BuildAvaloniaApp()
                .SetupWithoutStarting();
            var app = appBuilder.Instance as App;
            Assert.NotNull(app);

            var languages = App.GetLanguages();
            Assert.True(languages.Count > 1);
            Assert.Contains("en-US", languages.Keys);
            Assert.Contains("zh-CN", languages.Keys);
            Assert.Contains("ja-JP", languages.Keys);
            foreach (var pair in languages) {
                Assert.NotNull(pair.Value);
            }
            CheckLoadingWindowLifecycle();
        }

        static void CheckLoadingWindowLifecycle() {
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
            var owner = new Window();
            try {
                owner.Show();
                LoadingWindow.BeginLoadingImmediate(owner);
                var first = Assert.Single(owner.OwnedWindows.OfType<LoadingWindow>());
                first.Close(); // Close outside EndLoading, as with owner/WM closure.
                Assert.False(LoadingWindow.IsLoading());
                LoadingWindow.BeginLoadingImmediate(owner);
                var second = Assert.Single(owner.OwnedWindows.OfType<LoadingWindow>());
                Assert.NotSame(first, second);
                LoadingWindow.EndLoading();
                Assert.Empty(owner.OwnedWindows);
                LoadingWindow.BeginLoadingImmediate(owner);
                Assert.NotSame(second, Assert.Single(owner.OwnedWindows.OfType<LoadingWindow>()));
                LoadingWindow.EndLoading();
            } finally {
                LoadingWindow.EndLoading();
                owner.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
    }
}
