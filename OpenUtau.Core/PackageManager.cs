using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using SharpCompress.Archives;
using OpenUtau.Core.Util;

namespace OpenUtau.Core {
    public class RegistryMirror {
        public string url = string.Empty;
        public string hash = string.Empty;
    }
    public class RegistryVersion {
        public string version = string.Empty;
        public RegistryMirror[] mirrors = [];
    }
    public class RegistrySoftware {
        public string id = string.Empty;
        public Dictionary<string, string> names = new Dictionary<string, string>();
        public string category = string.Empty;
        public string[] developers = [];
        public string homepage_url = string.Empty;
        public string download_page_url = string.Empty;
        public string[] tags = [];
        public RegistryVersion[] versions = [];

        public string LocalizedName() {
            if (names.TryGetValue("en", out var n)) return n;
            if (names.Values.FirstOrDefault() is string v) return v;
            return id;
        }
    }

    public class OudepMetadata {
        public string id = string.Empty;
        [Obsolete] public string? name = null;
        public string version = string.Empty;
        public string description = string.Empty;
        public string @class = string.Empty;
    }



    public class PackageManager : SingletonBase<PackageManager> {
        public const string OudepExt = ".oudep";
        const string registryUrl = "https://openutau.github.io/svs-index/registry/v1/softwares/all.json";

        public async Task<List<RegistrySoftware>> FetchRegistryAsync() {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "OpenUtau");
            client.Timeout = TimeSpan.FromSeconds(30);
            using var response = await client.GetAsync(registryUrl);
            response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync();

            List<RegistrySoftware> list = new List<RegistrySoftware>();
            try {
                var token = JToken.Parse(body);
                var items = token["items"];
                if (items != null && items.Type == JTokenType.Array) {
                    list = items.ToObject<List<RegistrySoftware>>() ?? new List<RegistrySoftware>();
                }
            } catch (Exception e) {
                Log.Warning(e, "Failed to parse registry JSON");
                list = new List<RegistrySoftware>();
            }

            return list.Where(s => s.tags != null && s.tags.Contains("oudep")).ToList();
        }

        public async Task<List<OudepMetadata>> GetInstalledAsync() {
            var list = new List<OudepMetadata>();
            var depPath = PathManager.Inst.DependencyPath;
            if (!Directory.Exists(depPath)) {
                return list;
            }
            return await Task.Run(() => {
                var dirs = Directory.GetDirectories(depPath);
                var results = dirs.Select(dir => {
                    try {
                        var yamlPath = Path.Combine(dir, "oudep.yaml");
                        using var reader = new StreamReader(yamlPath, Encoding.UTF8);
                        var metadata = Core.Yaml.DefaultDeserializer.Deserialize<OudepMetadata>(reader) ?? new OudepMetadata();
                        if (string.IsNullOrEmpty(metadata.id)) {
                            metadata.id = Path.GetFileName(dir);
                        }
                        if (string.IsNullOrEmpty(metadata.version)) {
                            metadata.version = string.Empty;
                        }
                        return metadata;
                    } catch (Exception e) {
                        Log.Error($"Failed to read oudep.yaml in {dir} {e}");
                        return null;
                    }
                }).Where(r => r != null).Select(r => r!).ToList();
                return results;
            });
        }

        static string GetSha256Hex(byte[] data) {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var sb = new StringBuilder();
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private class VersionStringComparer : IComparer<string> {
            public int Compare(string? x, string? y) {
                var a = x ?? string.Empty;
                var b = y ?? string.Empty;
                if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) {
                    return va.CompareTo(vb);
                }
                if (Version.TryParse(a, out _)) return 1;
                if (Version.TryParse(b, out _)) return -1;
                return string.Compare(a, b, StringComparison.Ordinal);
            }
        }

        private static readonly IComparer<string> VersionComparer = new VersionStringComparer();

        public static string GetLatestVersionString(RegistryVersion[] versions) {
            if (versions == null || versions.Length == 0) return string.Empty;
            return versions.OrderByDescending(v => v.version, VersionComparer).First().version;
        }

        public async Task InstallAsync(RegistrySoftware software, IProgress<int>? progress = null) {
            ArgumentNullException.ThrowIfNull(software);
            if (software.versions == null || software.versions.Length == 0) throw new ArgumentException("No versions available");
            var version = software.versions.OrderByDescending(v => v.version, VersionComparer).First();
            if (version.mirrors == null || version.mirrors.Length == 0) throw new ArgumentException("No mirrors available");
            var mirror = version.mirrors[0];
            string cacheDir = PathManager.Inst.CachePath;
            Directory.CreateDirectory(cacheDir);
            byte[] data;
            using (var client = new HttpClient()) {
                client.Timeout = TimeSpan.FromMinutes(5);
                using var response = await client.GetAsync(mirror.url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                var contentLength = response.Content.Headers.ContentLength;
                using var responseStream = await response.Content.ReadAsStreamAsync();
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                if (contentLength.HasValue && progress != null) progress.Report(0);
                while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                    ms.Write(buffer, 0, read);
                    totalRead += read;
                    if (contentLength.HasValue && progress != null) {
                        var percent = (int)(totalRead * 100 / contentLength.Value);
                        progress.Report(Math.Min(100, percent));
                    }
                }
                if (progress != null) progress.Report(100);
                data = ms.ToArray();
            }
            var hash = mirror.hash; // "sha256:..."
            if (!string.IsNullOrEmpty(hash) && hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) {
                var expected = hash.Substring("sha256:".Length).ToLowerInvariant();
                var actual = GetSha256Hex(data);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException("Downloaded file hash does not match expected value");
                }
            }
            using (var ms = new MemoryStream(data)) {
                await InstallFromStreamAsync(ms, software.id, version.version);
            }
        }

        public async Task UninstallAsync(string id) {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            var basePath = Path.Combine(PathManager.Inst.DependencyPath, id);
            if (!Directory.Exists(basePath)) return;
            try {
                await Task.Run(() => Directory.Delete(basePath, true));
            } catch (Exception e) {
                Log.Warning(e, "Failed to uninstall dependency {id}", id);
                throw;
            }
        }

        public async Task InstallFromFileAsync(string archivePath) {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read);
            await InstallFromStreamAsync(stream, string.Empty, string.Empty);
        }

        public async Task InstallFromStreamAsync(Stream stream, string expectedId, string expectedVersion) {
            using var archive = ArchiveFactory.Open(stream);
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, "Installing dependency"));
            var metadataEntry = archive.Entries.FirstOrDefault(e => e.Key == "oudep.yaml");
            if (metadataEntry == null) {
                throw new ArgumentException("Missing oudep.yaml");
            }
            OudepMetadata metadata;
            using (var entryStream = metadataEntry.OpenEntryStream()) {
                using var reader = new StreamReader(entryStream, Encoding.UTF8);
                metadata = Core.Yaml.DefaultDeserializer.Deserialize<OudepMetadata>(reader);
            }
            if (!string.IsNullOrEmpty(expectedId) && metadata.id != expectedId ||
                !string.IsNullOrEmpty(expectedVersion) && metadata.version != expectedVersion) {
                throw new ArgumentException("Archive metadata does not match expected id/version");
            }
            var id = metadata.id;
            if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(metadata.name)) {
                id = metadata.name;
            }
            await Task.Run(() => {
                var basePath = Path.Combine(PathManager.Inst.DependencyPath, id);
                try {
                    if (Directory.Exists(basePath)) {
                        Directory.Delete(basePath, true);
                    }
                } catch (Exception e) {
                    Log.Error(e, $"Failed to remove old dependency folder {basePath}");
                }
                foreach (var entry in archive.Entries) {
                    if (string.IsNullOrEmpty(entry.Key) || entry.Key.Contains("..")) {
                        // Prevent zipSlip attack
                        continue;
                    }
                    var filePath = Path.Combine(basePath, entry.Key);
                    var dir = Path.GetDirectoryName(filePath);
                    if (!entry.IsDirectory && !string.IsNullOrEmpty(dir)) {
                        Directory.CreateDirectory(dir);
                        entry.WriteToFile(Path.Combine(basePath, entry.Key));
                    }
                }
            });
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Installed dependency \"{id}\""));
        }

        public string? GetInstalledPath(string id) {
            var path = Path.Combine(PathManager.Inst.DependencyPath, id);
            if (Directory.Exists(path)) {
                return path;
            }
            return null;
        }
    }
}
