using System;
using System.IO;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Neutrino {
    public enum NeutrinoVersion {
        Unsupported,
        V27,
        V3,
    }

    /// <summary>
    /// Shared lookup of NEUTRINO installations placed under the dependency folder.
    /// </summary>
    public static class NeutrinoUtils {
        public const string ConfFile = "japanese.utf_8.conf";
        public const string TableFile = "japanese.utf_8.table";

        const string BaseDirName = "NEUTRINO";
        const string V27DirName = "NEUTRINO_v27";
        const string V3DirName = "NEUTRINO_v3";

        // Supported ranges, lower bound inclusive and upper bound exclusive.
        static readonly Version V27Min = new Version(2, 7, 0, 0);
        static readonly Version V27Max = new Version(2, 7, 1, 0);
        static readonly Version V3Min = new Version(3, 2, 0, 0);
        static readonly Version V3Max = new Version(4, 0, 0, 0);

        /// <summary>
        /// Parses a singer version such as "v3.2.2" or "3.2" into a comparable value.
        /// Missing components are treated as 0 so that "v3.2" equals 3.2.0.
        /// </summary>
        public static bool TryParseVersion(string singerVersion, out Version version) {
            version = null;
            if (string.IsNullOrEmpty(singerVersion)) {
                return false;
            }
            string text = singerVersion.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) {
                text = text.Substring(1);
            }
            int suffix = text.IndexOfAny(new[] { '-', '+', ' ', '_' });
            if (suffix >= 0) {
                text = text.Substring(0, suffix);
            }
            if (!Version.TryParse(text, out var parsed)) {
                return false;
            }
            version = new Version(
                parsed.Major,
                parsed.Minor,
                Math.Max(parsed.Build, 0),
                Math.Max(parsed.Revision, 0));
            return true;
        }

        public static NeutrinoVersion DetectVersion(string singerVersion) {
            if (!TryParseVersion(singerVersion, out var version)) {
                return NeutrinoVersion.Unsupported;
            }
            if (version >= V27Min && version < V27Max) {
                return NeutrinoVersion.V27;
            }
            if (version >= V3Min && version < V3Max) {
                return NeutrinoVersion.V3;
            }
            return NeutrinoVersion.Unsupported;
        }

        /// <summary>
        /// Resolves the NEUTRINO folder to use for a singer version. A folder named
        /// "NEUTRINO" takes precedence over the version specific folders.
        /// </summary>
        public static bool TryResolveBasePath(string singerVersion, out string basePath, out string error) {
            error = string.Empty;
            basePath = Path.Join(PathManager.Inst.DependencyPath, BaseDirName);
            if (Directory.Exists(basePath)) {
                return true;
            }
            string dirName;
            switch (DetectVersion(singerVersion)) {
                case NeutrinoVersion.V27:
                    dirName = V27DirName;
                    break;
                case NeutrinoVersion.V3:
                    dirName = V3DirName;
                    break;
                default:
                    error = $"Unsupported NEUTRINO version: {singerVersion}";
                    return false;
            }
            basePath = Path.Join(PathManager.Inst.DependencyPath, dirName);
            if (!Directory.Exists(basePath)) {
                error = $"NEUTRINO not found at {basePath}";
                return false;
            }
            return true;
        }

        public static string DicPath(string basePath, string fileName) {
            return Path.Join(basePath, "settings", "dic", fileName);
        }

        public static string ModelDir(USinger singer) {
            return singer.Location + "/";
        }
    }

    /// <summary>
    /// Executable paths of a NEUTRINO installation. Paths not available on the
    /// running platform are left empty.
    /// </summary>
    public class NeutrinoPaths {
        public string Neutrino = string.Empty;
        public string NeutrinoClient = string.Empty;
        public string NeutrinoServer = string.Empty;
        public string Nsf = string.Empty;
        public string World = string.Empty;
        public string VocoderClient = string.Empty;
        public string VocoderServer = string.Empty;

        public bool HasClient => !string.IsNullOrEmpty(NeutrinoClient) && File.Exists(NeutrinoClient);

        public static NeutrinoPaths Create(string basePath) {
            string binPath = Path.Join(basePath, "bin");
            if (OS.IsWindows()) {
                return new NeutrinoPaths {
                    Neutrino = Path.Join(binPath, "NEUTRINO.exe"),
                    NeutrinoClient = Path.Join(binPath, "neutrino_client.exe"),
                    NeutrinoServer = Path.Join(binPath, "neutrino_server.exe"),
                    Nsf = Path.Join(binPath, "NSF.exe"),
                    World = Path.Join(binPath, "WORLD.exe"),
                    VocoderClient = Path.Join(binPath, "vocoder_client.exe"),
                    VocoderServer = Path.Join(binPath, "vocoder_server.exe"),
                };
            } else if (OS.IsMacOS() || OS.IsLinux()) {
                return new NeutrinoPaths {
                    Neutrino = Path.Join(binPath, "NEUTRINO"),
                    Nsf = Path.Join(binPath, "NSF"),
                    World = Path.Join(binPath, "WORLD"),
                };
            }
            throw new NotSupportedException("Platform not supported.");
        }
    }
}
