using System;
using System.Linq;

namespace OpenUtau.Core {
    /// <summary>
    /// Compares the ONNX Runtime version that is actually loaded against the version a
    /// plugin execution provider was built for. Kept separate from <see cref="Onnx"/> so
    /// the comparison stays a pure function and does not pull in the native runtime.
    /// </summary>
    internal static class OnnxRuntimeVersion {
        /// <summary>Whether <paramref name="versionString"/> is at least
        /// <paramref name="major"/>.<paramref name="minor"/>. Pre-release and build
        /// suffixes are ignored; an unparsable string never satisfies the gate.</summary>
        public static bool IsAtLeast(string versionString, int major, int minor) {
            if (string.IsNullOrWhiteSpace(versionString)) {
                return false;
            }
            string numeric = new string(versionString.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            if (!Version.TryParse(numeric, out var version)) {
                return false;
            }
            return version.Major > major || (version.Major == major && version.Minor >= minor);
        }
    }
}
