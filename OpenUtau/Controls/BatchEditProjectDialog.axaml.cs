using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;
using System.Windows.Forms;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core.Editing;
using ReactiveUI.SourceGenerators;
using SharpJaad.MP4.Boxes.Impl.SampleEntries;

namespace OpenUtau.App.Controls {
    public partial class BatchEditProjectDialog : Window {
        static BatchEditProjectDialog? open;
        private BatchEditProjectViewModel ViewModel;

        private BatchEditProjectDialog(BatchEditProjectViewModel model) {
            InitializeComponent();
            DataContext = ViewModel = model;
        }

        public static void Open(Window owner) {
            if (open != null) {
                open.Activate();
                return;
            }
            open?.Close();
            var dialog = new BatchEditProjectDialog(new BatchEditProjectViewModel());
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
            open = null;
        }
    }
}
