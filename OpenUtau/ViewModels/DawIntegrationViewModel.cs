using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.DawIntegration;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    /// <summary>One discovered plugin, as the connection list shows it.</summary>
    public partial class DawServerViewModel : ViewModelBase {
        public DawServer Server { get; }
        public string Name => Server.Name;
        public int Port => Server.Port;
        public string ApiVersion => Server.Info.ApiVersion;

        [Reactive] public partial DawConnectionState ConnectionState { get; set; } = DawConnectionState.Disconnected;

        public bool IsConnected => ConnectionState == DawConnectionState.Connected;

        /// <summary>
        /// Whether this entry can be connected to. Incompatible plugins are still listed, so the
        /// list can explain why one is refused instead of silently hiding it (PROTOCOL.md §4).
        /// </summary>
        public string Compatibility => Server.IsCompatible
            ? ThemeManager.GetString("dawintegration.compatible")
            : ThemeManager.GetString("dawintegration.incompatible");

        public string StateText => ConnectionState switch {
            DawConnectionState.Connected => ThemeManager.GetString("dawintegration.state.connected"),
            DawConnectionState.Connecting => ThemeManager.GetString("dawintegration.state.connecting"),
            DawConnectionState.Reconnecting => ThemeManager.GetString("dawintegration.state.reconnecting"),
            _ => ThemeManager.GetString("dawintegration.state.free"),
        };

        public DawServerViewModel(DawServer server) {
            Server = server;
        }

        public void RefreshState() {
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(StateText));
        }
    }

    /// <summary>
    /// The connection entry point: what the discovery directory currently advertises, plus the
    /// state of every connection <see cref="DawManager"/> owns. Several DAW plugin instances can
    /// be connected at once, one per OpenUtau track. The manager outlives this dialog, so
    /// closing the window does not drop the connections.
    /// </summary>
    /// <remarks>
    /// Also hosts the bridge plugin workflow: the reference plugin ships separately
    /// (<see cref="DawBridgeInstaller.RepoUrl"/>), so this dialog detects whether it is already
    /// installed, offers a one-click install (download, unpack, copy into a folder the DAW
    /// scans — with UAC elevation on Windows), and explains the first connection. The
    /// "How to use" panel auto-expands while nothing is discovered. Every string is a
    /// resource key, so translations fall back to English where a language has none yet.
    /// </remarks>
    public partial class DawIntegrationViewModel : ViewModelBase, IDisposable {
        private readonly DawServerFinder finder = new DawServerFinder(DawServerFinder.DefaultDirectory);
        private bool disposed;

        public ObservableCollection<DawServerViewModel> Servers { get; }
            = new ObservableCollection<DawServerViewModel>();

        [Reactive] public partial DawServerViewModel? SelectedServer { get; set; }
        [Reactive] public partial string Status { get; set; } = string.Empty;
        [Reactive] public partial bool IsBusy { get; set; }
        [Reactive] public partial int ConnectedCount { get; set; }
        [Reactive] public partial bool Vst3Installed { get; set; }
        [Reactive] public partial bool ClapInstalled { get; set; }
        [Reactive] public partial string InstallStatus { get; set; } = string.Empty;
        /// <summary>Whether the floating how-to-use card is shown over the dialog.</summary>
        [Reactive] public partial bool IsGuideOpen { get; set; }

        /// <summary>Whether any bridge format is installed. The floating guide only auto-opens
        /// while this is false — an installed setup needs "load it in your DAW", not a tour.</summary>
        public bool InstallDetected => Vst3Installed || ClapInstalled;

        /// <summary>Manual-download link label with this machine's OS name, e.g. "Windows".</summary>
        public string GuideDownloadText => string.Format(
            ThemeManager.GetString("dawintegration.guide.download"), OsDisplayName);

        /// <summary>Plugin folder paths for this machine's OS (VST3/CLAP), from resources.</summary>
        public string GuideInstallPaths => ThemeManager.GetString(OS.IsWindows()
            ? "dawintegration.guide.paths.win"
            : OS.IsMacOS() ? "dawintegration.guide.paths.mac"
            : "dawintegration.guide.paths.linux");

        public static string OsDisplayName =>
            OS.IsWindows() ? "Windows" : OS.IsMacOS() ? "macOS" : "Linux";

        // Installed formats stay clickable: clicking again re-downloads and overwrites, which
        // doubles as the upgrade path when the plugin ships a new version (and as a retry
        // after a declined UAC prompt). CopyPlain and robocopy both overwrite in place.
        public bool Vst3InstallEnabled => !IsBusy;
        public bool ClapInstallEnabled => !IsBusy;

        public string Vst3ButtonText => Vst3Installed
            ? ThemeManager.GetString("dawintegration.guide.install.vst3.reinstall")
            : ThemeManager.GetString("dawintegration.guide.install.vst3");
        public string ClapButtonText => ClapInstalled
            ? ThemeManager.GetString("dawintegration.guide.install.clap.reinstall")
            : ThemeManager.GetString("dawintegration.guide.install.clap");

        public string Vst3StateText => Vst3Installed
            ? ThemeManager.GetString("dawintegration.guide.installed.ok")
            : ThemeManager.GetString("dawintegration.guide.installed.no");
        public string ClapStateText => ClapInstalled
            ? ThemeManager.GetString("dawintegration.guide.installed.ok")
            : ThemeManager.GetString("dawintegration.guide.installed.no");

        public void OpenDownloadPage() => OS.OpenWeb(DawBridgeInstaller.DownloadUrl);
        public void OpenReleasesPage() => OS.OpenWeb(DawBridgeInstaller.ReleasesUrl);
        public void OpenManual() => OS.OpenWeb(DawBridgeInstaller.ManualUrl);
        public void OpenManualZh() => OS.OpenWeb(DawBridgeInstaller.ManualZhUrl);

        /// <summary>Re-probes the standard plugin folders. Cheap; safe to call often.</summary>
        void RefreshInstallState() {
            Vst3Installed = DawBridgeInstaller.FindInstalled(DawBridgeFormat.Vst3) != null;
            ClapInstalled = DawBridgeInstaller.FindInstalled(DawBridgeFormat.Clap) != null;
            this.RaisePropertyChanged(nameof(Vst3StateText));
            this.RaisePropertyChanged(nameof(ClapStateText));
            this.RaisePropertyChanged(nameof(Vst3ButtonText));
            this.RaisePropertyChanged(nameof(ClapButtonText));
            this.RaisePropertyChanged(nameof(Vst3InstallEnabled));
            this.RaisePropertyChanged(nameof(ClapInstallEnabled));
        }

        /// <summary>
        /// One-click install of the bridge plugin: fetch the release zip, locate the bundle,
        /// copy it where DAWs look for it. Runs on the thread pool; progress and results are
        /// marshalled into <see cref="InstallStatus"/>.
        /// </summary>
        public async Task InstallAsync(DawBridgeFormat format) {
            if (IsBusy) {
                return;
            }
            IsBusy = true;
            InstallStatus = ThemeManager.GetString("dawintegration.guide.install.preparing");
            this.RaisePropertyChanged(nameof(Vst3InstallEnabled));
            this.RaisePropertyChanged(nameof(ClapInstallEnabled));
            try {
                var progress = new Progress<int>(p => InstallStatus = string.Format(
                    ThemeManager.GetString("dawintegration.guide.install.downloading"), p));
                string root = await DawBridgeInstaller.FetchPackageAsync(progress, CancellationToken.None);
                InstallStatus = ThemeManager.GetString("dawintegration.guide.install.extracting");
                string? bundle = await Task.Run(() => DawBridgeInstaller.LocateBundle(root, format));
                if (bundle == null) {
                    throw new InvalidOperationException("Bundle not found in the downloaded package.");
                }
                var result = await Task.Run(() => DawBridgeInstaller.Install(format, bundle));
                RefreshInstallState();
                // Three outcomes: elevated success, UAC declined (per-user fallback), or the
                // elevated copy itself failing (per-user fallback with the error surfaced).
                InstallStatus = result.FellBack
                    ? string.Format(
                        ThemeManager.GetString(result.ElevationError == "declined"
                            ? "dawintegration.guide.install.fallback"
                            : "dawintegration.guide.install.elevfail"),
                        result.ElevationError, result.TargetDirectory)
                    : string.Format(ThemeManager.GetString("dawintegration.guide.install.done"), result.TargetDirectory);
                Log.Information(
                    $"DAW bridge: {format} installed to {result.TargetDirectory} (elevated={result.Elevated}, fellBack={result.FellBack})");
            } catch (Exception e) {
                Log.Error(e, $"DAW bridge: {format} install failed.");
                InstallStatus = string.Format(
                    ThemeManager.GetString("dawintegration.guide.install.failed"), e.Message);
            } finally {
                IsBusy = false;
                this.RaisePropertyChanged(nameof(Vst3InstallEnabled));
                this.RaisePropertyChanged(nameof(ClapInstallEnabled));
            }
        }

        public bool ConnectEnabled => !IsBusy
            && SelectedServer is { } connectable
            && connectable.ConnectionState == DawConnectionState.Disconnected
            && connectable.Server.IsCompatible;
        public bool DisconnectEnabled => !IsBusy
            // Judged from the manager, not the discovery list: a reconnecting connection is
            // still teardown-able, and a selected-but-unconnected entry must not block
            // tearing down other live connections.
            && DawManager.Inst.Connections.Any(
                info => info.State != DawConnectionState.Disconnected);

        public DawIntegrationViewModel() {
            DawManager.Inst.StateChanged += OnStateChanged;
            DawManager.Inst.ConnectionLost += OnConnectionLost;
            DawManager.Inst.ConnectionsChanged += OnConnectionsChanged;
            this.WhenAnyValue(x => x.SelectedServer, x => x.IsBusy)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(ConnectEnabled));
                    this.RaisePropertyChanged(nameof(DisconnectEnabled));
                    this.RaisePropertyChanged(nameof(Vst3InstallEnabled));
                    this.RaisePropertyChanged(nameof(ClapInstallEnabled));
                });
            RefreshInstallState();
            ShowState(DawManager.Inst.State);
        }

        public async Task RefreshAsync() {
            // Scan probes every advertised port, so it does blocking socket work even though the
            // directory itself is tiny.
            var found = await Task.Run(() => finder.Scan());
            int selectedPort = SelectedServer?.Port ?? 0;
            Servers.Clear();
            foreach (var server in found) {
                Servers.Add(new DawServerViewModel(server));
            }
            ApplyConnectionStates();
            SelectedServer = Servers.FirstOrDefault(item => item.Port == selectedPort)
                ?? Servers.FirstOrDefault(item => item.Server.IsCompatible);
            // The user may have installed the plugin since the dialog opened.
            RefreshInstallState();
            if (Servers.Count == 0 && ConnectedCount == 0) {
                // Different guidance depending on whether the plugin is installed at all:
                // an installed-but-not-running setup needs "load it in your DAW", not "install it".
                Status = ThemeManager.GetString(Vst3Installed || ClapInstalled
                    ? "dawintegration.none.installed"
                    : "dawintegration.none");
            }
        }

        public async Task ConnectAsync() {
            var target = SelectedServer;
            if (target == null || IsBusy) {
                return;
            }
            if (!target.Server.IsCompatible) {
                Status = ThemeManager.GetString("dawintegration.incompatible");
                return;
            }
            IsBusy = true;
            try {
                await DawManager.Inst.ConnectAsync(target.Server);
            } catch (MessageCustomizableException e) {
                // Friendly failures (e.g. the project has never been saved): show them the way
                // the renderer's own errors are shown, not as a raw stack.
                Log.Warning($"DAW: connect refused: {e.Message}");
                Status = e.Message;
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            } catch (Exception e) {
                Log.Error(e, "DAW: connect failed.");
                Status = e.Message;
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            } finally {
                IsBusy = false;
            }
        }

        public async Task DisconnectAsync() {
            var target = SelectedServer;
            if (IsBusy) {
                return;
            }
            IsBusy = true;
            try {
                // Only target a port that actually has a manager connection; the discovery
                // entry may have lost it (or never had one), in which case tear down all.
                if (target is { } connected && connected.ConnectionState != DawConnectionState.Disconnected) {
                    await DawManager.Inst.DisconnectAsync(connected.Port);
                } else {
                    await DawManager.Inst.DisconnectAsync();
                }
            } finally {
                IsBusy = false;
            }
        }

        /// <summary>
        /// <see cref="DawManager.StateChanged"/> fires from the transport read loop and from the
        /// reconnect task, so it has to be marshalled before it touches a binding.
        /// </summary>
        private void OnStateChanged(DawConnectionState state) {
            Dispatcher.UIThread.Post(() => ShowState(state));
        }

        private void OnConnectionsChanged() {
            Dispatcher.UIThread.Post(ApplyConnectionStates);
        }

        private void ApplyConnectionStates() {
            var infos = DawManager.Inst.Connections;
            ConnectedCount = infos.Count(info => info.State == DawConnectionState.Connected);
            foreach (var server in Servers) {
                server.ConnectionState = infos
                    .FirstOrDefault(info => info.Port == server.Port)
                    ?.State ?? DawConnectionState.Disconnected;
                server.RefreshState();
            }
            ShowState(DawManager.Inst.State);
            this.RaisePropertyChanged(nameof(ConnectEnabled));
            this.RaisePropertyChanged(nameof(DisconnectEnabled));
        }

        private void OnConnectionLost(string reason) {
            Log.Warning($"DAW: connection lost: {reason}");
            Dispatcher.UIThread.Post(
                () => Status = string.Format(ThemeManager.GetString("dawintegration.lost"), reason));
        }

        private void ShowState(DawConnectionState state) {
            string name = DawManager.Inst.ServerName;
            Status = state switch {
                DawConnectionState.Connected =>
                    string.Format(ThemeManager.GetString("dawintegration.connected.count"), ConnectedCount),
                DawConnectionState.Connecting => ThemeManager.GetString("dawintegration.connecting"),
                DawConnectionState.Reconnecting =>
                    string.Format(ThemeManager.GetString("dawintegration.reconnecting"), name),
                _ => ConnectedCount > 0
                    ? string.Format(ThemeManager.GetString("dawintegration.connected.count"), ConnectedCount)
                    : ThemeManager.GetString("dawintegration.disconnected"),
            };
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            // The manager outlives the dialog, so a leaked handler would keep this view model alive.
            DawManager.Inst.StateChanged -= OnStateChanged;
            DawManager.Inst.ConnectionLost -= OnConnectionLost;
            DawManager.Inst.ConnectionsChanged -= OnConnectionsChanged;
        }
    }
}
