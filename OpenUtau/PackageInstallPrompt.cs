using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App {
    /// <summary>
    /// Installs missing registry packages on the user's confirmation, for those in
    /// <see cref="PackageRequirements.Installable"/>; others are left to the Package Manager.
    /// </summary>
    static class PackageInstallPrompt {
        static bool busy;
        static readonly HashSet<string> declined = new HashSet<string>();

        /// <summary>
        /// Offers to install whichever of the packages are missing, then pre-renders again. One prompt
        /// at a time; after a failure, packages the user declined this session are not offered again.
        /// </summary>
        public static async Task EnsureInstalledAsync(Window parent, IEnumerable<string> packageIds, bool afterFailure) {
            var missing = packageIds.Distinct().Where(id => PackageManager.Inst.GetInstalledPath(id) == null).ToArray();
            if (missing.Length == 0 || busy || afterFailure && missing.All(declined.Contains)) {
                return;
            }
            busy = true;
            try {
                var registry = missing.All(PackageRequirements.Installable.Contains)
                    ? await PackageManager.Inst.FetchRegistryAsync()
                    : new List<RegistrySoftware>();
                var software = missing.Select(id => registry.FirstOrDefault(s => s.id == id)).ToArray();
                if (software.Contains(null)) {
                    await MessageBox.ShowError(parent, new MissingPackageException(missing));
                    return;
                }
                var result = await MessageBox.Show(parent,
                    string.Format(ThemeManager.GetString("packages.confirm.install.message"), string.Join(", ", software.Select(s => s!.LocalizedName()))),
                    ThemeManager.GetString("packages.confirm.install.caption"),
                    MessageBox.MessageBoxButtons.OkCancel);
                if (result != MessageBox.MessageBoxResult.Ok) {
                    declined.UnionWith(missing);
                    return;
                }
                foreach (var s in software) {
                    string status = string.Format(ThemeManager.GetString("packages.status.installing"), s!.LocalizedName());
                    await PackageManager.Inst.InstallAsync(s, new Progress<int>(p => DocManager.Inst.ExecuteCmd(new ProgressBarNotification(p, $"{status} {p}%"))));
                }
                DocManager.Inst.ExecuteCmd(new PreRenderNotification());
            } catch (Exception e) {
                Log.Error(e, "Failed to install packages {ids}", missing);
                await MessageBox.ShowError(parent, e, ThemeManager.GetString("packages.status.installfailed"));
            } finally {
                busy = false;
            }
        }
    }
}
