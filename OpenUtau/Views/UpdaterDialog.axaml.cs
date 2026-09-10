using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NetSparkleUpdater.Enums;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class UpdaterDialog : Window {
        public readonly UpdaterViewModel ViewModel;
        public UpdaterDialog() {
            InitializeComponent();
            DataContext = ViewModel = new UpdaterViewModel();
        }

        void OnClosing(object sender, WindowClosingEventArgs e) {
            ViewModel.OnClosing();
        }

        public static void CheckForUpdate(Action<Window> showDialog, Action closeApplication, TaskScheduler scheduler) {
            Task.Run(async () => {
                using var updater = await UpdaterViewModel.NewUpdaterAsync();
                if (updater == null) {
                    return false;
                }
                var info = await updater.CheckForUpdatesQuietly(true);
                if (info.Status == UpdateStatus.UpdateAvailable) {
                    // SkipUpdate is stored as "<channel>:<version>". The bare
                    // form without a channel prefix is the legacy format.
                    string version = info.Updates[0].Version.ToString();
                    if (Preferences.Default.SkipUpdate == version ||
                        Preferences.Default.SkipUpdate == $"{Preferences.Default.Channel}:{version}") {
                        return false;
                    }
                    return true;
                }
                return false;
            }).ContinueWith(t => {
                if (t.IsCompletedSuccessfully && t.Result) {
                    var dialog = new UpdaterDialog();
                    dialog.ViewModel.CloseApplication = closeApplication;
                    showDialog.Invoke(dialog);
                }
                if (t.IsFaulted) {
                    Log.Error(t.Exception, "Failed to check for update");
                }
            }, scheduler).ContinueWith((t2, _) => {
                if (t2.IsFaulted) {
                    Log.Error(t2.Exception, "Failed to check for update");
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
