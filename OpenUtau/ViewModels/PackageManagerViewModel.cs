using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core;
using ReactiveUI.Fody.Helpers;
using System.Reactive;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    public class PackageRowViewModel : ViewModelBase {
        public RegistrySoftware? Software { get; }
        public string Id { get; }
        public string Name { get; }
        public string Developer { get; }
        public string Version { get; }
        [Reactive] public bool IsInstalled { get; set; }
        [Reactive] public string InstalledVersion { get; set; } = string.Empty;

        public bool HasRegistry => Software != null;
        public bool IsUpToDate => IsInstalled && HasRegistry && !string.IsNullOrEmpty(InstalledVersion) && InstalledVersion == Version;
        public bool CanInstallOrUpdate => HasRegistry && (!IsInstalled || !IsUpToDate);
        public string PrimaryActionLabel => !HasRegistry ? ThemeManager.GetString("packages.install") : (!IsInstalled ? ThemeManager.GetString("packages.install") : (IsUpToDate ? ThemeManager.GetString("packages.install") : ThemeManager.GetString("packages.update")));
        public bool CanUninstall => IsInstalled;
        public string InstalledDisplay => IsInstalled ? (string.IsNullOrEmpty(InstalledVersion) ? ThemeManager.GetString("packages.unknownversion") : InstalledVersion) : string.Empty;

        public PackageRowViewModel(RegistrySoftware s) {
            Software = s;
            Id = s.id;
            Name = s.LocalizedName();
            Developer = (s.developers != null && s.developers.Length > 0) ? string.Join(", ", s.developers) : string.Empty;
            Version = (s.versions != null && s.versions.Length > 0) ? PackageManager.GetLatestVersionString(s.versions) : string.Empty;

            this.WhenAnyValue(x => x.IsInstalled, x => x.InstalledVersion)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(IsUpToDate));
                    this.RaisePropertyChanged(nameof(CanInstallOrUpdate));
                    this.RaisePropertyChanged(nameof(PrimaryActionLabel));
                    this.RaisePropertyChanged(nameof(CanUninstall));
                    this.RaisePropertyChanged(nameof(InstalledDisplay));
                });
        }

        public PackageRowViewModel(string id, string name, string developer, string version) {
            Software = null;
            Id = id;
            Name = name;
            Developer = developer;
            Version = version;
            IsInstalled = false;
        }

        public void SetInstalled(OudepMetadata info) {
            IsInstalled = true;
            InstalledVersion = info.version ?? string.Empty;
            this.RaisePropertyChanged(nameof(IsUpToDate));
            this.RaisePropertyChanged(nameof(CanInstallOrUpdate));
            this.RaisePropertyChanged(nameof(PrimaryActionLabel));
            this.RaisePropertyChanged(nameof(CanUninstall));
            this.RaisePropertyChanged(nameof(InstalledDisplay));
        }
    }

    public class PackageManagerViewModel : ViewModelBase {
        public ObservableCollection<PackageRowViewModel> Available { get; } = new ObservableCollection<PackageRowViewModel>();
        [Reactive] public string Status { get; set; } = string.Empty;
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<PackageRowViewModel, Unit> InstallCommand { get; }
        public ReactiveCommand<PackageRowViewModel, Unit> UninstallCommand { get; }

        public PackageManagerViewModel() {
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
            InstallCommand = ReactiveCommand.CreateFromTask<PackageRowViewModel>(InstallAsync);
            UninstallCommand = ReactiveCommand.CreateFromTask<PackageRowViewModel>(UninstallAsync);
            _ = RefreshAsync();
        }

        public async Task RefreshAsync() {
            try {
                Status = ThemeManager.GetString("packages.status.fetching");
                var registry = await PackageManager.Inst.FetchRegistryAsync();
                Status = ThemeManager.GetString("packages.status.listinginstalled");
                var installed = await PackageManager.Inst.GetInstalledAsync();

                var installedById = installed.ToDictionary(i => i.id, i => i);

                var rows = new List<PackageRowViewModel>();

                foreach (var s in registry) {
                    var row = new PackageRowViewModel(s);
                    if (installedById.TryGetValue(row.Id, out var info)) {
                        row.SetInstalled(info);
                        installedById.Remove(row.Id);
                    }
                    rows.Add(row);
                }

                foreach (var info in installedById.Values) {
                    var id = info.id;
                    var row = new PackageRowViewModel(id, id, string.Empty, string.Empty);
                    row.SetInstalled(info);
                    rows.Add(row);
                }

                var ordered = rows.OrderByDescending(r => r.IsInstalled).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

                Available.Clear();
                foreach (var r in ordered) Available.Add(r);

                Status = ThemeManager.GetString("packages.status.ready");
            } catch (Exception e) {
                Status = ThemeManager.GetString("packages.status.error");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async Task InstallAsync(PackageRowViewModel row) {
            try {
                if (row.Software == null) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new InvalidOperationException("No registry entry to install.")));
                    return;
                }
                if (row.IsUpToDate) return;
                var installingTemplate = ThemeManager.GetString("packages.status.installing");
                var baseStatus = string.Format(installingTemplate, row.Id);
                Status = baseStatus;
                var progress = new Progress<int>(p => {
                    Status = $"{baseStatus} ({p}%)";
                });
                await PackageManager.Inst.InstallAsync(row.Software, progress);
                await RefreshAsync();
                Status = ThemeManager.GetString("packages.status.installfinished");
            } catch (Exception e) {
                Status = ThemeManager.GetString("packages.status.installfailed");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async Task UninstallAsync(PackageRowViewModel row) {
            try {
                if (!row.IsInstalled) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new InvalidOperationException("Package is not installed.")));
                    return;
                }
                Status = string.Format(ThemeManager.GetString("packages.status.uninstalling"), row.Id);
                await PackageManager.Inst.UninstallAsync(row.Id);
                await RefreshAsync();
                Status = ThemeManager.GetString("packages.status.uninstallfinished");
            } catch (Exception e) {
                Status = ThemeManager.GetString("packages.status.uninstallfailed");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
    }
}
