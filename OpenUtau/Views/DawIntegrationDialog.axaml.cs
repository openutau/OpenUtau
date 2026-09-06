using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;

namespace OpenUtau.App.Views {
    /// <summary>
    /// The connection entry point for DAW integration: pick a plugin the discovery directory
    /// advertises and connect to it. Closing this window leaves the connection running.
    /// </summary>
    public partial class DawIntegrationDialog : Window {
        public DawIntegrationDialog() {
            InitializeComponent();
        }

        protected override void OnOpened(EventArgs e) {
            base.OnOpened(e);
            Refresh();
        }

        protected override void OnClosed(EventArgs e) {
            // Drops the manager's event handlers; the connection itself is unaffected.
            (DataContext as DawIntegrationViewModel)?.Dispose();
            base.OnClosed(e);
        }

        void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

        void OnClose(object sender, RoutedEventArgs e) => Close();

        async void Refresh() {
            try {
                if (DataContext is DawIntegrationViewModel vm) {
                    await vm.RefreshAsync();
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        async void OnConnect(object sender, RoutedEventArgs e) {
            try {
                if (DataContext is DawIntegrationViewModel vm) {
                    await vm.ConnectAsync();
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        async void OnDisconnect(object sender, RoutedEventArgs e) {
            try {
                if (DataContext is DawIntegrationViewModel vm) {
                    await vm.DisconnectAsync();
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Close();
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}
