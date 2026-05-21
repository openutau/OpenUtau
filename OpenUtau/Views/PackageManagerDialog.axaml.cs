using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;

namespace OpenUtau.App.Views {
    public partial class PackageManagerDialog : Window {
        public PackageManagerDialog() {
            InitializeComponent();
        }

        async void OnPrimaryActionClick(object sender, RoutedEventArgs e) {
            try {
                if (DataContext is PackageManagerViewModel vm) {
                    if (sender is Button b && b.DataContext is PackageRowViewModel row) {
                        if (row.IsInstalled && !row.IsUpToDate) {
                            var msg = string.Format(ThemeManager.GetString("packages.confirm.update.message"), row.Id, row.Version);
                            var caption = ThemeManager.GetString("packages.confirm.update.caption");
                            var result = await MessageBox.Show(this, msg, caption, MessageBox.MessageBoxButtons.YesNo);
                            if (result != MessageBox.MessageBoxResult.Yes) return;
                        } else {
                            var msg = string.Format(ThemeManager.GetString("packages.confirm.install.message"), row.Id);
                            var caption = ThemeManager.GetString("packages.confirm.install.caption");
                            var result = await MessageBox.Show(this, msg, caption, MessageBox.MessageBoxButtons.YesNo);
                            if (result != MessageBox.MessageBoxResult.Yes) return;
                        }
                        await vm.InstallAsync(row);
                    }
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        async void OnUninstallClick(object sender, RoutedEventArgs e) {
            try {
                if (DataContext is PackageManagerViewModel vm) {
                    if (sender is Button b && b.DataContext is PackageRowViewModel row) {
                        var msg = string.Format(ThemeManager.GetString("packages.confirm.uninstall.message"), row.Id);
                        var caption = ThemeManager.GetString("packages.confirm.uninstall.caption");
                        var result = await MessageBox.Show(this, msg, caption, MessageBox.MessageBoxButtons.YesNo);
                        if (result != MessageBox.MessageBoxResult.Yes) return;
                        await vm.UninstallAsync(row);
                    }
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        async void OnInstallFromFile(object sender, RoutedEventArgs e) {
            try {
                var file = await FilePicker.OpenFile(
                    this, "menu.tools.dependency.install", FilePicker.OUDEP);
                if (file == null) return;
                if (file.EndsWith(PackageManager.OudepExt)) {
                    await PackageManager.Inst.InstallFromFileAsync(file);
                    if (DataContext is PackageManagerViewModel vm) {
                        await vm.RefreshAsync();
                    }
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OpenLocation(object sender, RoutedEventArgs e) {
            try {
                Directory.CreateDirectory(PathManager.Inst.DependencyPath);
                OS.OpenFolder(PathManager.Inst.DependencyPath);
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }
    }
}
