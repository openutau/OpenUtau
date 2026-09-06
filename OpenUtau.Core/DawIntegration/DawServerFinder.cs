using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>Discovery file contents (PROTOCOL.md §4).</summary>
    public class DawServerInfo {
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; } = string.Empty;
    }

    /// <summary>A discovered plugin instance: its advertisement plus the version verdict.</summary>
    public class DawServer {
        public string FilePath { get; }
        public DawServerInfo Info { get; }

        /// <summary>Parsed <c>apiVersion</c>. Default when the field was missing or unparseable.</summary>
        public DawApiVersion Version { get; }

        /// <summary>False when <c>apiVersion</c> is unreadable or its major differs (§4).</summary>
        public bool IsCompatible { get; }

        public int Port => Info.Port;
        public string Name => string.IsNullOrWhiteSpace(Info.Name) ? Path.GetFileNameWithoutExtension(FilePath) : Info.Name;

        public DawServer(string filePath, DawServerInfo info) {
            FilePath = filePath;
            Info = info;
            bool parsed = DawApiVersion.TryParse(info.ApiVersion, out var version);
            Version = version;
            IsCompatible = parsed && version.IsCompatibleWith(DawApiVersion.Current);
        }

        public override string ToString() => $"{Name} (port {Port}, api {Info.ApiVersion})";
    }

    /// <summary>
    /// Finds plugin servers through the discovery directory, drops stale advertisements and
    /// reports version compatibility (PROTOCOL.md §4). The directory is injectable so tests
    /// never touch the real per-user temp path.
    /// </summary>
    public sealed class DawServerFinder {
        /// <summary>The protocol path: <c>%TEMP%/OpenUtau/PluginServers</c>, per-user on every OS.</summary>
        public static string DefaultDirectory => Path.Combine(Path.GetTempPath(), "OpenUtau", "PluginServers");

        public static DawServerFinder Default { get; } = new DawServerFinder(DefaultDirectory);

        public string Directory { get; }

        public DawServerFinder(string directory) {
            Directory = directory;
        }

        /// <summary>
        /// Reads every advertisement in the directory. Files whose port no longer answers are
        /// deleted when <paramref name="removeStale"/> is set, which is how the directory stays
        /// clean after a DAW crash.
        /// </summary>
        public List<DawServer> Scan(bool removeStale = true) {
            var servers = new List<DawServer>();
            if (!System.IO.Directory.Exists(Directory)) {
                return servers;
            }
            foreach (string path in System.IO.Directory.GetFiles(Directory, "*.json")) {
                DawServerInfo? info;
                try {
                    info = JsonSerializer.Deserialize<DawServerInfo>(
                        File.ReadAllText(path, Encoding.UTF8), DawJson.Options);
                } catch (Exception e) {
                    // A half-written file is normal if we scan while a plugin is starting.
                    Log.Warning(e, $"DAW: unreadable discovery file '{path}', skipped.");
                    continue;
                }
                if (info == null || info.Port <= 0 || info.Port > 65535) {
                    Log.Warning($"DAW: discovery file '{path}' has no usable port, skipped.");
                    continue;
                }
                if (!IsPortAlive(info.Port)) {
                    Log.Information($"DAW: discovery file '{path}' is stale (port {info.Port} is free).");
                    if (removeStale) {
                        TryDelete(path);
                    }
                    continue;
                }
                servers.Add(new DawServer(path, info));
            }
            return servers;
        }

        /// <summary>
        /// Liveness probe from §4: if we can bind the port ourselves, nothing is listening.
        /// <see cref="Socket.ExclusiveAddressUse"/> must be set — without it Windows allows a
        /// second bind through SO_REUSEADDR and every live server would look stale.
        /// </summary>
        public static bool IsPortAlive(int port) {
            try {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.ExclusiveAddressUse = true;
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                return false;
            } catch (SocketException) {
                return true;
            } catch (Exception e) {
                // Unexpected failures must not delete a possibly-live advertisement.
                Log.Warning(e, $"DAW: could not probe port {port}; assuming it is alive.");
                return true;
            }
        }

        /// <summary>
        /// Writes an advertisement. Production plugins publish their own file; this exists for the
        /// conformance harness and tests, which play the plugin side.
        /// </summary>
        public string Publish(string name, int port, string apiVersion = DawApiVersion.CurrentString) {
            System.IO.Directory.CreateDirectory(Directory);
            string path = Path.Combine(Directory, name + ".json");
            var info = new DawServerInfo { Port = port, Name = name, ApiVersion = apiVersion };
            File.WriteAllText(path, DawJson.Serialize(info), Encoding.UTF8);
            return path;
        }

        public void Remove(string path) => TryDelete(path);

        private static void TryDelete(string path) {
            try {
                File.Delete(path);
            } catch (Exception e) {
                Log.Warning(e, $"DAW: could not delete discovery file '{path}'.");
            }
        }
    }
}
