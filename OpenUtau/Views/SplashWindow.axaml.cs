using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using OpenUtau.Classic;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class SplashWindow : Window {
        public SplashWindow() {
            InitializeComponent();
            if (ThemeManager.IsDarkMode) {
                LogoTypeLight.IsVisible = false;
                LogoTypeDark.IsVisible = true;
            } else {
                LogoTypeLight.IsVisible = true;
                LogoTypeDark.IsVisible = false;
            }
            this.Cursor = new Cursor(StandardCursorType.AppStarting);
            this.Opened += SplashWindow_Opened;
        }

        private void SplashWindow_Opened(object? sender, EventArgs e) {
            if (Screens.Primary == null && Screens.ScreenCount == 0) {
                return;
            }

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
            if (!OS.IsWindows() || Core.Util.Preferences.Default.PreferPortAudio) {
                try {
                    PlaybackManager.Inst.AudioOutput = new Audio.MiniAudioOutput();
                } catch (Exception e1) {
                    Log.Error(e1, "Failed to init MiniAudio");
                }
            } else {
                try {
                    PlaybackManager.Inst.AudioOutput = new NAudioOutput();
                } catch (Exception e2) {
                    Log.Error(e2, "Failed to init NAudio");
                }
            }
            Log.Information("Initialized audio.");
        }
    }
}
