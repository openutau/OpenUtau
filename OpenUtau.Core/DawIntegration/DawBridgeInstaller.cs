using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.DawIntegration {
    public enum DawBridgeFormat {
        Vst3,
        Clap,
    }

    /// <summary>Where the bundle ended up and how it got there.</summary>
    public sealed class DawBridgeInstallResult {
        /// <summary>Directory that now contains the bundle.</summary>
        public string TargetDirectory { get; init; } = string.Empty;
        /// <summary>True when the bundle went into the system-wide folder via elevation.</summary>
        public bool Elevated { get; init; }
        /// <summary>True when elevation was declined or failed and the per-user folder was used instead.</summary>
        public bool FellBack { get; init; }
        /// <summary>Null when elevation succeeded or was never attempted; "declined" when the
        /// user cancelled the UAC prompt, otherwise the message of the elevation failure.</summary>
        public string? ElevationError { get; init; }
    }

    /// <summary>
    /// Detects and installs the reference bridge plugin (VST3/CLAP) that ships separately at
    /// <see cref="RepoUrl"/>. Installation means: download the release zip for this OS, locate
    /// the bundle inside it, and copy it into a folder the user's DAW scans. On Windows the
    /// system-wide folder needs elevation (UAC); when that is declined the per-user folder is
    /// used instead, which every mainstream host also scans. On macOS and Linux the bundles
    /// live in the user's home and no elevation exists.
    /// </summary>
    /// <remarks>
    /// Zip layout, as produced by the plugin repo's CI (verified against release 1.0.0):
    /// Windows packs <c>CLAP/Release/OpenUtau Bridge.clap</c> (file) and
    /// <c>VST3/OpenUtau Bridge.vst3/...</c> (bundle); macOS packs the <c>.clap</c> and
    /// <c>.vst3</c> bundles as folders; Linux packs <c>OpenUtau Bridge.clap</c> (file) and the
    /// VST3 bundle folder. A .vst3 bundle is always a directory; a .clap is a file on Windows
    /// and Linux, a bundle directory on macOS. Zip extraction does not preserve Unix modes, so
    /// bundles installed on macOS/Linux get executable bits re-applied.
    /// </remarks>
    public static class DawBridgeInstaller {
        public const string RepoUrl = "https://github.com/KakaruHayate/openutau-vst-bridge";
        public const string ReleasesUrl = RepoUrl + "/releases/latest";
        public const string ManualUrl = RepoUrl + "/blob/main/MANUAL.md";
        public const string ManualZhUrl = RepoUrl + "/blob/main/MANUAL.zh-CN.md";

        /// <summary>Direct download of the current release for this OS, via GitHub's
        /// permanent latest-release asset redirect. Asset names are CI-produced.</summary>
        public static string DownloadUrl => OS.IsWindows()
            ? ReleasesUrl + "/download/plugins-windows-latest.zip"
            : OS.IsMacOS() ? ReleasesUrl + "/download/plugins-macos-latest.zip"
            : ReleasesUrl + "/download/plugins-ubuntu-latest.zip";

        public static string BundleName(DawBridgeFormat format) => format == DawBridgeFormat.Vst3
            ? "OpenUtau Bridge.vst3"
            : "OpenUtau Bridge.clap";

        static string UserPluginRoot => OS.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Common")
            : OS.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Audio", "Plug-Ins")
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>Candidate install roots for a format, system-wide first. The directories
        /// may not exist yet; installation creates them.</summary>
        public static string[] InstallRoots(DawBridgeFormat format) {
            string leaf = format == DawBridgeFormat.Vst3 ? "VST3" : "CLAP";
            if (OS.IsWindows()) {
                return new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files", leaf),
                    Path.Combine(UserPluginRoot, leaf),
                };
            }
            if (OS.IsMacOS()) {
                return new[] { Path.Combine(UserPluginRoot, leaf) };
            }
            // Linux: ~/.vst3 and ~/.clap
            return new[] { Path.Combine(UserPluginRoot, format == DawBridgeFormat.Vst3 ? ".vst3" : ".clap") };
        }

        /// <summary>Full path of the installed bundle, or null when no candidate has it.
        /// Probes both the file and directory forms so future packaging changes stay covered.</summary>
        public static string? FindInstalled(DawBridgeFormat format) {
            string name = BundleName(format);
            foreach (string root in InstallRoots(format)) {
                string asDirectory = Path.Combine(root, name);
                if (Directory.Exists(asDirectory) || File.Exists(asDirectory)) {
                    return asDirectory;
                }
            }
            return null;
        }

        /// <summary>Downloads the release zip (reporting 0-100 through <paramref name="progress"/>,
        /// with indeterminate downloads reported as -1) and extracts it to a fresh temp folder,
        /// whose root is returned.</summary>
        /// <remarks>The work directory is unique per call: two OpenUtau processes installing at
        /// the same time must not delete each other's download or extracted bundle.</remarks>
        public static async Task<string> FetchPackageAsync(IProgress<int>? progress, CancellationToken ct) {
            string workDir = Path.Combine(
                Path.GetTempPath(), "OpenUtau", "BridgeInstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string zipPath = Path.Combine(workDir, "plugins.zip");

            using (var client = new HttpClient()) {
                client.DefaultRequestHeaders.Add("User-Agent", "OpenUtau");
                client.Timeout = TimeSpan.FromMinutes(5);
                using var response = await client.GetAsync(
                    DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using (var file = File.Create(zipPath)) {
                    var buffer = new byte[81920];
                    long read = 0;
                    int lastPercent = -1;
                    while (true) {
                        int n = await stream.ReadAsync(buffer, ct);
                        if (n == 0) {
                            break;
                        }
                        await file.WriteAsync(buffer.AsMemory(0, n), ct);
                        read += n;
                        if (progress != null && total is > 0) {
                            int percent = (int)(100 * read / total.Value);
                            if (percent != lastPercent) {
                                lastPercent = percent;
                                progress.Report(percent);
                            }
                        }
                    }
                }
            }

            progress?.Report(100);
            string extractDir = Path.Combine(workDir, "extracted");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            return extractDir;
        }

        /// <summary>Finds the bundle of <paramref name="format"/> anywhere under the extracted
        /// zip root (CI zips nest bundles differently per OS).</summary>
        public static string? LocateBundle(string extractedRoot, DawBridgeFormat format) {
            string name = BundleName(format);
            if (format == DawBridgeFormat.Vst3) {
                // A .vst3 bundle is a directory; on Windows the inner engine dll shares its
                // name, so search directories only.
                return Directory.EnumerateDirectories(extractedRoot, name, SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
            // .clap: a file on Windows/Linux, a bundle directory on macOS. Prefer whichever
            // form exists; a directory named like the file wins (macOS layout).
            return Directory.EnumerateFileSystemEntries(extractedRoot, name, SearchOption.AllDirectories)
                .FirstOrDefault();
        }

        /// <summary>Installs the located bundle. Windows tries the system-wide folder through
        /// UAC elevation and falls back to the per-user folder when elevation is declined or
        /// fails; macOS/Linux install into the user's plugin folder directly.</summary>
        public static DawBridgeInstallResult Install(DawBridgeFormat format, string bundlePath) {
            if (OS.IsWindows()) {
                string[] roots = InstallRoots(format);
                try {
                    string target = CopyWithElevation(format, bundlePath, roots[0]);
                    return new DawBridgeInstallResult { TargetDirectory = target, Elevated = true };
                } catch (Win32Exception w) when (w.NativeErrorCode == 1223) {
                    Log.Warning($"DAW bridge: elevation for {format} declined; falling back to per-user folder.");
                    return new DawBridgeInstallResult {
                        TargetDirectory = CopyPlain(format, bundlePath, roots[1]),
                        FellBack = true,
                        ElevationError = "declined",
                    };
                } catch (Exception e) {
                    Log.Warning(e, $"DAW bridge: elevation for {format} failed; falling back to per-user folder.");
                    return new DawBridgeInstallResult {
                        TargetDirectory = CopyPlain(format, bundlePath, roots[1]),
                        FellBack = true,
                        ElevationError = e.Message,
                    };
                }
            }
            string targetDirectory = CopyPlain(format, bundlePath, InstallRoots(format)[0]);
            FixUnixModes(format, targetDirectory);
            return new DawBridgeInstallResult { TargetDirectory = targetDirectory };
        }

        /// <summary>Elevated copy into the system folder. robocopy does the work so a UAC
        /// prompt covers one short-lived process; exit codes 0-7 are successes. The console
        /// window is suppressed by the hidden window style.</summary>
        static string CopyWithElevation(DawBridgeFormat format, string bundlePath, string systemRoot) {
            string name = BundleName(format);
            string targetPath = Path.Combine(systemRoot, name);
            string args = format == DawBridgeFormat.Vst3
                ? $"\"{bundlePath}\" \"{targetPath}\" /E /NFL /NDL /NJH /NJS /NP"
                : $"\"{Path.GetDirectoryName(bundlePath)}\" \"{systemRoot}\" \"{name}\" /NFL /NDL /NJH /NJS /NP";
            var psi = new ProcessStartInfo("robocopy.exe", args) {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(psi)!;
            process.WaitForExit();
            if (process.ExitCode is < 0 or > 7) {
                throw new InvalidOperationException($"robocopy exited with {process.ExitCode}.");
            }
            return systemRoot;
        }

        /// <summary>Plain copy into <paramref name="root"/> (created if needed), replacing any
        /// previous bundle. Returns the root.</summary>
        static string CopyPlain(DawBridgeFormat format, string bundlePath, string root) {
            string name = BundleName(format);
            Directory.CreateDirectory(root);
            string targetPath = Path.Combine(root, name);
            if (Directory.Exists(bundlePath)) {
                if (Directory.Exists(targetPath)) {
                    Directory.Delete(targetPath, true);
                }
                Directory.CreateDirectory(targetPath);
                foreach (string src in Directory.EnumerateFileSystemEntries(bundlePath)) {
                    CopyEntry(src, targetPath);
                }
            } else {
                File.Copy(bundlePath, targetPath, overwrite: true);
            }
            return root;
        }

        static void CopyEntry(string src, string destDir) {
            string dest = Path.Combine(destDir, Path.GetFileName(src));
            if (Directory.Exists(src)) {
                Directory.CreateDirectory(dest);
                foreach (string child in Directory.EnumerateFileSystemEntries(src)) {
                    CopyEntry(child, dest);
                }
            } else {
                File.Copy(src, dest, overwrite: true);
            }
        }

        /// <summary>Zip extraction drops Unix modes; re-apply 0755 so hosts can load the
        /// bundles (executable bits on the bundle root, its Contents and the binaries).</summary>
        static void FixUnixModes(DawBridgeFormat format, string root) {
            if (OperatingSystem.IsWindows()) {
                return;
            }
            const UnixFileMode mode755 =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            try {
                string name = BundleName(format);
                string bundle = Path.Combine(root, name);
                if (Directory.Exists(bundle)) {
                    File.SetUnixFileMode(bundle, mode755);
                    foreach (string path in Directory.EnumerateFileSystemEntries(bundle, "*", SearchOption.AllDirectories)) {
                        File.SetUnixFileMode(path, mode755);
                    }
                } else if (File.Exists(bundle)) {
                    File.SetUnixFileMode(bundle, mode755);
                }
            } catch (Exception e) {
                Log.Warning(e, "DAW bridge: failed to apply executable bits after install.");
            }
        }
    }
}
