using System;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Derives the release channel from the build's own version.
    /// Only alpha builds carry a 4th version component
    /// (e.g. 1.5.1.3); stable and beta use plain 3-component versions.
    /// </summary>
    public static class ReleaseChannel {
        public static string? FromVersion(Version? version) {
            if (version == null) {
                return null;
            }
            return version.Revision > 0 ? "alpha" : null;
        }
    }
}
