using System.Collections.Generic;
using OpenUtau.Classic;
using OpenUtau.Classic.Hifisampler;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    /// <summary>The registry packages OpenUtau's engines need, and which the app may install itself.</summary>
    public static class PackageRequirements {
        public static readonly IReadOnlySet<string> Installable = new HashSet<string> {
            Hnsep.PackageId,
            HifiVocoder.PackageId,
            "game",
            "rmvpe",
        };

        /// <summary>The packages a track's renderer (and resampler) needs. hifisampler needs hnsep only for its curves.</summary>
        public static string[] For(URenderSettings settings) => settings.renderer switch {
            Renderers.WORLDLINE_R11 => [Hnsep.PackageId],
            Renderers.WORLDLINE_R2 => [HifiVocoder.PackageId],
            Renderers.CLASSIC when (settings.Resampler?.ToString() ?? settings.resampler) == HifisamplerResampler.name
                => [HifiVocoder.PackageId, Hnsep.PackageId],
            _ => [],
        };
    }
}
