using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls {
    public partial class BatchEditProjectDialog : Window {
        static BatchEditProjectDialog? open;
        private BatchEditProjectViewModel ViewModel;

        private BatchEditProjectDialog(BatchEditProjectViewModel model) {
            InitializeComponent();
            DataContext = ViewModel = model;
        }

        public static void Open(Window owner, UProject project) {
            if (open != null) {
                open.Activate();
                return;
            }
            open?.Close();
            var dialog = new BatchEditProjectDialog(new BatchEditProjectViewModel(project));
            open = dialog;
            dialog.Show(owner);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }


        public void OnCancel(object? sender, RoutedEventArgs? args) {
            open?.Close();
            open = null;
        }

        public void OnFinish(object? sender, RoutedEventArgs? args) {
            ViewModel.RunSelected();
            open?.Close();
            DocManager.Inst.ExecuteCmd(new ToastNotification("MainWindow", "Batch reset done", "command.batch.project.toast"));
            open = null;
        }

        private void OnClosed(object? sender, EventArgs e) {
            open?.Close();
            open = null;
        }
    }
}
