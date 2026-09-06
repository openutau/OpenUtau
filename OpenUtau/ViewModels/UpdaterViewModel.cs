using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media;
using NetSparkleUpdater;
using NetSparkleUpdater.AppCastHandlers;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;
using Newtonsoft.Json;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public partial class UpdaterViewModel : ViewModelBase {
        public class GithubReleaseAsset {
            public string name = string.Empty;
            public string browser_download_url = string.Empty;
        }
        public class GithubRelease {
#pragma warning disable 0649
            public string html_url = string.Empty;
            public long id = long.MaxValue;
            public bool draft;
            public bool prerelease;
            public string tag_name = string.Empty;
            public string name = string.Empty;
            public GithubReleaseAsset[] assets = new GithubReleaseAsset[0];
#pragma warning restore 0649
        }
        public string AppVersion {
            get {
                // The channel is derived from the build's own version, not the
                // update channel pref, which the user may have switched to a
                // different one.
                Version? version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                string suffix = Core.Util.ReleaseChannel.FromVersion(version) is { } channel
                    ? $" ({channel})" : string.Empty;
                return $"v{version}{suffix}";
            }
        }
        public bool IsDarkMode => ThemeManager.IsDarkMode;
        [Reactive] public partial string UpdaterStatus { get; set; }
        [Reactive] public partial bool UpdateAvailable { get; set; }
        [Reactive] public partial FontWeight UpdateButtonFontWeight { get; set; }
        public Action? CloseApplication { get; set; }

        private SparkleUpdater? sparkle;
        private UpdateInfo? updateInfo;
        private bool updateAccepted;

        public UpdaterViewModel() {
            UpdaterStatus = string.Empty;
            UpdateAvailable = false;
            UpdateButtonFontWeight = FontWeight.Normal;
            Init();
        }

        public static async Task<SparkleUpdater?> NewUpdaterAsync() {
            try {
                var release = await SelectRelease();
                if (release == null) {
                    Log.Error("No updatable release found.");
                    return null;
                }
                Log.Information($"Checking update at: {release.html_url}");
                var appcast = SelectAppcast(release);
                if (appcast == null) {
                    Log.Error("No updatable appcast found.");
                    return null;
                }
                Log.Information($"Checking appcast: {appcast.browser_download_url}");
                return new ZipUpdater(appcast.browser_download_url, new Ed25519Checker(SecurityMode.Unsafe)) {
                    UIFactory = null,
                    CheckServerFileName = false,
                    RelaunchAfterUpdate = true,
                    RelaunchAfterUpdateCommandPrefix = OS.IsLinux() ? "./" : string.Empty,
                    AppCastHandler = new XMLAppCast() {
                        AppCastFilter = new DowngradableFilter()
                    },
                };
            } catch (Exception e) {
                Log.Error(e, "Failed to select appcast to update.");
                return null;
            }
        }

        // Channel filter for GitHub releases. Alpha builds are published as
        // prereleases tagged `<version>-alpha`, beta builds as prereleases
        // tagged `<version>-beta`. The alpha channel is the superset: it
        // sees both alpha and beta releases.
        public static bool IsReleaseForChannel(GithubRelease release, string channel) {
            if (release.draft) {
                return false;
            }
            return channel switch {
                "alpha" => release.prerelease,
                "beta" => release.prerelease && !release.tag_name.EndsWith("-alpha"),
                _ => !release.prerelease,
            };
        }

        static async Task<GithubRelease?> SelectRelease() {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "Other");
            client.Timeout = TimeSpan.FromSeconds(30);
            // per_page=100: with frequent alpha releases, the default 30-item
            // page could push the release of the current channel off-page.
            using var resposne = await client.GetAsync("https://api.github.com/repos/stakira/OpenUtau/releases?per_page=100");
            resposne.EnsureSuccessStatusCode();
            string respBody = await resposne.Content.ReadAsStringAsync();
            List<GithubRelease>? releases = JsonConvert.DeserializeObject<List<GithubRelease>>(respBody);
            if (releases == null) {
                return null;
            }
            return releases
                .Where(r => IsReleaseForChannel(r, Preferences.Default.Channel))
                .OrderByDescending(r => r.id)
                .FirstOrDefault();
        }

        static GithubReleaseAsset? SelectAppcast(GithubRelease release) {
            string suffix = PathManager.Inst.IsInstalled ? "-installer" : "";
            return release.assets
                .Where(a => a.name == $"appcast.{OS.GetUpdaterRid()}{suffix}.xml")
                .FirstOrDefault();
        }

        async void Init() {
            UpdaterStatus = ThemeManager.GetString("updater.status.checking");
            sparkle = await NewUpdaterAsync();
            if (sparkle == null) {
                UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
                return;
            }
            updateInfo = await sparkle.CheckForUpdatesQuietly();
            if (updateInfo == null) {
                UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
                return;
            }
            switch (updateInfo.Status) {
                case UpdateStatus.UpdateAvailable:
                case UpdateStatus.UserSkipped:
                    UpdaterStatus = string.Format(ThemeManager.GetString("updater.status.available"), updateInfo.Updates[0].Version);
                    UpdateAvailable = true;
                    UpdateButtonFontWeight = FontWeight.Bold;
                    break;
                case UpdateStatus.UpdateNotAvailable:
                    UpdaterStatus = ThemeManager.GetString("updater.status.notavailable");
                    break;
                case UpdateStatus.CouldNotDetermine:
                    UpdaterStatus = ThemeManager.GetString("updater.status.unknown");
                    break;
            }
        }

        public void OnGithub() {
            try {
                OS.OpenWeb("https://github.com/stakira/OpenUtau/wiki");
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async void OnUpdate() {
            if (sparkle == null || updateInfo == null || updateInfo.Updates.Count == 0) {
                return;
            }
            UpdateAvailable = false;
            updateAccepted = true;

            AppCastItem? downloadedItem = null;
            sparkle.CloseApplication += () => {
                Log.Information($"shutting down for update");
                CloseApplication?.Invoke();
                Log.Information($"shut down for update");
            };
            sparkle.DownloadStarted += (item, path) => {
                Log.Information($"download started {path}");
                downloadedItem = item;
            };
            sparkle.DownloadFinished += (item, path) => {
                Log.Information($"download finished {path}");
                // `item` is somehow null in this callback, likely a NetSparkle bug.
                item = item ?? downloadedItem;
                if (item == null) {
                    Log.Error("DownloadFinished unexpected null item.");
                } else {
                    sparkle.InstallUpdate(downloadedItem, path);
                }
            };
            sparkle.DownloadHadError += (item, path, e) => {
                Log.Error(e, $"download error {path}");
            };
            sparkle.DownloadMadeProgress += (sender, item, e) => {
                UpdaterStatus = $"{e.ProgressPercentage}%";
            };

            await sparkle.InitAndBeginDownload(updateInfo.Updates.First());
        }

        public void OnClosing() {
            if (!updateAccepted && updateInfo != null &&
                (updateInfo.Status == UpdateStatus.UpdateAvailable ||
                updateInfo.Status == UpdateStatus.UserSkipped) &&
                updateInfo.Updates.Count > 0) {
                Log.Information($"Skipping update {updateInfo.Updates[0].Version}");
                // Skip is per-channel so that skipping e.g. an alpha build does
                // not suppress the same-target beta or stable release.
                Preferences.Default.SkipUpdate = $"{Preferences.Default.Channel}:{updateInfo.Updates[0].Version}";
                Preferences.Save();
            }
        }
    }

    // Force allow downgrading so that switching between beta and stable works.
    public class DowngradableFilter : IAppCastFilter {
        static bool Eq(int a, int b) {
            a = a == -1 ? 0 : a;
            b = b == -1 ? 0 : b;
            return a == b;
        }
        // Ambiguous version equal where 1.2 == 1.2.0 == 1.2.0.0.
        static bool Eq(Version a, Version b) {
            return Eq(a.Major, b.Major)
                && Eq(a.Minor, b.Minor)
                && Eq(a.Build, b.Build)
                && Eq(a.Revision, b.Revision);
        }
        public FilterResult GetFilteredAppCastItems(Version installed, List<AppCastItem> items) {
            items = items.Where(item => !Eq(new Version(item.Version), installed)).ToList();
            return new FilterResult(/*forceInstallOfLatestInFilteredList=*/true, items);
        }
    }

    public class ZipUpdater : SparkleUpdater {
        public ZipUpdater(string appcastUrl, ISignatureVerifier signatureVerifier) :
            base(appcastUrl, signatureVerifier) { }
        public ZipUpdater(string appcastUrl, ISignatureVerifier signatureVerifier, string referenceAssembly) :
            base(appcastUrl, signatureVerifier, referenceAssembly) { }
        public ZipUpdater(string appcastUrl, ISignatureVerifier signatureVerifier, string referenceAssembly, IUIFactory factory) :
            base(appcastUrl, signatureVerifier, referenceAssembly, factory) { }

        protected override string GetWindowsInstallerCommand(string downloadFilePath) {
            string installerExt = Path.GetExtension(downloadFilePath);
            if (DoExtensionsMatch(installerExt, ".exe")) {
                return $"\"{downloadFilePath}\"";
            }
            if (DoExtensionsMatch(installerExt, ".msi")) {
                return $"msiexec /i \"{downloadFilePath}\"";
            }
            if (DoExtensionsMatch(installerExt, ".msp")) {
                return $"msiexec /p \"{downloadFilePath}\"";
            }
            if (DoExtensionsMatch(installerExt, ".zip")) {
                string restart = RestartExecutablePath.TrimEnd('\\', '/');
                if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 17063) {
                    Log.Information("Starting update with tar.");
                    return $"tar -x -f \"{downloadFilePath}\" -C \"{restart}\"";
                }
                var unzipperPath = Path.Combine(Path.GetDirectoryName(downloadFilePath) ?? Path.GetTempPath(), "Unzipper.exe");
                File.WriteAllBytes(unzipperPath, Resources.Resources.Unzipper);
                Log.Information("Starting update with unzipper.");
                return $"{unzipperPath} \"{downloadFilePath}\" \"{restart}\"";
            }
            return downloadFilePath;
        }
    }
}
