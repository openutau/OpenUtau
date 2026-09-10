using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using OpenUtau.Classic;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class SplashWindow : Window, IDisposable {
        public SplashWindow() {
            InitializeComponent();
            UpdateLogo();
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => UpdateLogo())
                .DisposeWith(disposable);
            this.Cursor = new Cursor(StandardCursorType.AppStarting);
            // Screens are not populated yet when Opened fires on X11 and
            // Wayland, so retry on activation changes until they are. This
            // fires several times per launch (GetObservable also pushes the
            // current value on subscribe), so Start() is guarded to run once.
            this.GetObservable(Window.IsActiveProperty)
                .Subscribe(_ => SplashWindow_Opened())
                .DisposeWith(disposable);
        }

        private readonly MultipleDisposable disposable = new();
        private bool started;

        private void UpdateLogo() {
            LogoTypeDark.IsVisible = ThemeManager.IsDarkMode;
            LogoTypeLight.IsVisible = !ThemeManager.IsDarkMode;
        }

        public void Dispose() {
            disposable.Dispose();
        }

        private void SplashWindow_Opened() {
            if (started || (Screens.Primary == null && Screens.ScreenCount == 0)) {
                return;
            }
            started = true;

            Start();
        }

        private void Start() {
            var mainThread = Thread.CurrentThread;
            var mainScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            Task.Run(() => {
                Log.Information("Initializing OpenUtau.");
                ToolsManager.Inst.Initialize();
                SingerManager.Inst.Initialize();
                DocManager.Inst.Initialize(mainThread, mainScheduler);
                DocManager.Inst.PostOnUIThread = action => Avalonia.Threading.Dispatcher.UIThread.Post(action);
                Log.Information("Initialized OpenUtau.");
                InitAudio();
            }).ContinueWith(t => {
                if (t.IsFaulted) {
                    Log.Error(t.Exception?.Flatten(), "Failed to Start.");
                    MessageBox.ShowError(this, t.Exception, "Failed to Start OpenUtau").ContinueWith(t1 => { Close(); });
                    return;
                }
                if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                    desktop.MainWindow = mainWindow;
                    mainWindow.InitProject();
                    LoadingWindow.InitializeLoadingWindow();
                    Close();
                }
            }, CancellationToken.None, TaskContinuationOptions.None, mainScheduler);
        }

        private static void InitAudio() {
            Log.Information("Initializing audio.");
            if (OS.IsWindows() && Core.Util.Preferences.Default.AudioBackEnd == 0) {
                try {
                    PlaybackManager.Inst.AudioOutput = new NAudioOutput();
                } catch (Exception e0) {
                    Log.Error(e0, "Failed to init NAudio");
                }
            } else {
                switch (Core.Util.Preferences.Default.AudioBackEnd) {
                    case 0:
                    case 1:
                        try {
                            PlaybackManager.Inst.AudioOutput = new Audio.MiniAudioOutput();
                        } catch (Exception e1) {
                            Log.Error(e1, "Failed to init MiniAudio");
                        }
                        break;
                    case 2:
                        try {
                            PlaybackManager.Inst.AudioOutput = new Audio.SDL3AudioOutput();
                        } catch (Exception e2) {
                            Log.Error(e2, "Failed to init SDL3 Audio");
                        }
                        break;
                }
            }
            Log.Information("Initialized audio.");
        }
    }
}
