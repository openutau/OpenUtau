using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Js;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class DeveloperWindow : Window {
        DeveloperViewModel viewModel;

        public DeveloperWindow() {
            InitializeComponent();
            DataContext = viewModel = new DeveloperViewModel();
            viewModel.SetWindow(this);
            viewModel.Attach();
        }
        public void CopyLogText() {
            CopyTextBox.Text = DeveloperViewModel.Sink.Inst.ToString();
            CopyTextBox.SelectAll();
            CopyTextBox.Copy();
            CopyTextBox.Clear();
        }

        void OnMenuOpenPackagesLocation(object sender, RoutedEventArgs args) {
            try {
                System.IO.Directory.CreateDirectory(PathManager.Inst.DependencyPath);
                OS.OpenFolder(PathManager.Inst.DependencyPath);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnMenuCreatePhonemizer(object sender, RoutedEventArgs args) {
            var dialog = new TypeInDialog {
                Title = ThemeManager.GetString("debug.packages.createphonemizer").TrimEnd('.'),
            };
            dialog.SetPrompt(ThemeManager.GetString("debug.packages.createphonemizer.prompt"));
            dialog.SetText("my_phonemizer");
            dialog.onFinish = id => {
                try {
                    var dir = JsPhonemizerFactory.CreatePackage(id.Trim());
                    Log.Information("Created package {Path}", dir);
                    DocManager.Inst.ExecuteCmd(new PackagesChangedNotification());
                    OS.OpenFolder(dir);
                } catch (Exception e) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                }
            };
            dialog.ShowDialog(this);
        }

        void OnClosed(object? sender, EventArgs e) {
            viewModel.Detach();
        }
    }
}
