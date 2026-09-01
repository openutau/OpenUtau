using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;

namespace OpenUtau.App.Views {
    public partial class DawIntegrationTerminalDialog : Window {
        public readonly DawIntegrationTerminalViewModel ViewModel;
        public DawIntegrationTerminalDialog() {
            InitializeComponent();
            DataContext = ViewModel = new DawIntegrationTerminalViewModel();
        }

        void OnClosing(object sender, WindowClosingEventArgs e) {
        }

        async void OnConnect(object sender, RoutedEventArgs args) {
            try {
                await ViewModel.Connect();

                DocManager.Inst.ExecuteCmd(new DawConnectedNotification());
                Close();
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void OnDownloadClick(object sender, RoutedEventArgs args) {
            try {
                OS.OpenWeb("https://example.com");
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
    }
}
