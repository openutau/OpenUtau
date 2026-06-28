using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    public partial class ExpressionsDialog : Window {
        public ExpressionsDialog() {
            InitializeComponent();
        }

        private void ApplyButtonClicked(object sender, RoutedEventArgs _) {
            try {
                (DataContext as ExpressionsViewModel)?.Apply();
                Close();
            } catch (Exception e) {
                MessageBox.ShowError(this, e);
            }
        }

        private void AddButtonClicked(object sender, RoutedEventArgs _) {
            var button = (Button)sender;
            var vm = DataContext as ExpressionsViewModel;
            if (vm != null) {
                vm.Add();
                if (vm.IsTrackOverride) {
                    button.ContextMenu?.Open();
                }
            }
        }
    }
}
