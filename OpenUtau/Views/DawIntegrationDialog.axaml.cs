using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.DawIntegration;

namespace OpenUtau.App.Views {
    /// <summary>
    /// The connection entry point for DAW integration: pick a plugin the discovery directory
    /// advertises and connect to it. Closing this window leaves the connection running.
    /// </summary>
    /// <remarks>
    /// The how-to-use guide is a floating card over a dimmed backdrop, so it never crowds the
    /// connection list and its layout cannot be clipped by a popup presenter. It opens itself
    /// once per dialog session while nothing is installed and nothing is discovered — the
    /// moment the feature would otherwise look dead.
    /// </remarks>
    public partial class DawIntegrationDialog : Window {
        private bool guideAutoShown;

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

        async void Refresh() {
            try {
                if (DataContext is DawIntegrationViewModel vm) {
                    await vm.RefreshAsync();
                    MaybeShowGuide(vm);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        /// <summary>First refresh with nothing installed and nothing discovered is where
        /// first-time users land: show the guide once, never against their will.</summary>
        void MaybeShowGuide(DawIntegrationViewModel vm) {
            if (guideAutoShown) {
                return;
            }
            if (vm.Servers.Count == 0 && vm.ConnectedCount == 0 && !vm.InstallDetected) {
                guideAutoShown = true;
                vm.IsGuideOpen = true;
            }
        }

        void OnToggleGuide(object sender, RoutedEventArgs e) {
            if (DataContext is DawIntegrationViewModel vm) {
                vm.IsGuideOpen = !vm.IsGuideOpen;
            }
        }

        void OnCloseGuide(object sender, RoutedEventArgs e) {
            if (DataContext is DawIntegrationViewModel vm) {
                vm.IsGuideOpen = false;
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

        void OnGuideDownload(object sender, RoutedEventArgs e) => OpenGuideLink(vm => vm.OpenDownloadPage());
        void OnGuideReleases(object sender, RoutedEventArgs e) => OpenGuideLink(vm => vm.OpenReleasesPage());
        void OnGuideManual(object sender, RoutedEventArgs e) => OpenGuideLink(vm => vm.OpenManual());
        void OnGuideManualZh(object sender, RoutedEventArgs e) => OpenGuideLink(vm => vm.OpenManualZh());

        async void OnInstallVst3(object sender, RoutedEventArgs e) {
            try {
                if (DataContext is DawIntegrationViewModel vm) {
                    await vm.InstallAsync(DawBridgeFormat.Vst3);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        async void OnInstallClap(object sender, RoutedEventArgs e) {
            try {
                if (DataContext is DawIntegrationViewModel vm) {
                    await vm.InstallAsync(DawBridgeFormat.Clap);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        void OpenGuideLink(Action<DawIntegrationViewModel> open) {
            try {
                if (DataContext is DawIntegrationViewModel vm) {
                    open(vm);
                }
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                if (DataContext is DawIntegrationViewModel vm && vm.IsGuideOpen) {
                    vm.IsGuideOpen = false;
                } else {
                    Close();
                }
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}
