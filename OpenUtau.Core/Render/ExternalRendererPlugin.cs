using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Render {
    public interface IExternalRendererIdentity {
        string Id { get; }
        string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ExternalRendererAttribute : Attribute {
        public string Id { get; }
        public string Name { get; }
        public USingerType SingerType { get; }

        public ExternalRendererAttribute(string id, string name, USingerType singerType = USingerType.Classic) {
            Id = id;
            Name = name;
            SingerType = singerType;
        }
    }

    public sealed class RendererPluginMetadata {
        public RendererCapabilitiesManifest Capabilities { get; init; } = new RendererCapabilitiesManifest();
        public IReadOnlyDictionary<string, AnalysisFormatManifest> AnalysisFormats { get; init; }
            = new Dictionary<string, AnalysisFormatManifest>();
        public IReadOnlyDictionary<string, UExpressionDescriptor> Expressions { get; init; }
            = new Dictionary<string, UExpressionDescriptor>();
        public IReadOnlyDictionary<string, RendererSettingDescriptor> Settings { get; init; }
            = new Dictionary<string, RendererSettingDescriptor>();
    }

    public enum RendererSettingType { Integer, Number, Boolean, Text, Choice }

    public sealed class RendererSettingDescriptor {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public RendererSettingType Type { get; init; } = RendererSettingType.Text;
        public string DefaultValue { get; init; } = string.Empty;
        public double? Min { get; init; }
        public double? Max { get; init; }
        public double? Step { get; init; }
        public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Versioned entry point implemented by external renderer bridge assemblies.
    /// </summary>
    public interface IOpenUtauRendererPlugin {
        int ApiVersion { get; }
        RendererPluginMetadata Metadata => new RendererPluginMetadata();
        IRenderer CreateRenderer(RendererPluginContext context);
        IRendererAnalysisProvider? CreateAnalysisProvider(RendererPluginContext context) => null;
    }

    public enum RendererAnalysisState {
        Valid,
        Missing,
        Stale,
        Invalid,
    }

    public sealed record RendererAnalysisRequest(
        string Format, string SourceFile, string OutputFile, bool Overwrite);

    public enum RendererAnalysisOutcome {
        Generated,
        AlreadyValid,
        Failed,
    }

    public sealed record RendererAnalysisResult(
        RendererAnalysisRequest Request,
        RendererAnalysisOutcome Outcome,
        string Message = null);

    /// <summary>Owns engine-specific validation and generation of reusable source
    /// analysis. The host handles paths, fallback timestamp checks and orchestration.</summary>
    public interface IRendererAnalysisProvider {
        Task<IReadOnlyList<RendererAnalysisResult>> GenerateAsync(
            IReadOnlyList<RendererAnalysisRequest> requests,
            IProgress<int> progress,
            CancellationToken cancellation);
        ValueTask<RendererAnalysisState> ValidateAsync(
            RendererAnalysisRequest request,
            CancellationToken cancellation);
    }

    public sealed class RendererPluginContext {
        public int ApiVersion => ExternalRendererRegistry.ApiVersion;
        public Version HostVersion => typeof(IRenderer).Assembly.GetName().Version ?? new Version();
        public string RendererId { get; }
        public string RendererName { get; }
        public string PluginDirectory { get; }
        public string ManifestPath { get; }
        public string CacheDirectory => PathManager.Inst.CachePath;
        public ILogger Logger { get; }
        public ResamplerManifest Manifest { get; }
        public RendererPluginMetadata Metadata { get; }
        public RendererAnalysisService Analysis { get; }
        public RendererCacheService Cache { get; }

        public RendererPluginContext(
            string rendererId,
            string rendererName,
            string pluginDirectory,
            string manifestPath,
            ResamplerManifest manifest,
            RendererPluginMetadata metadata = null,
            ILogger logger = null) {
            RendererId = rendererId;
            RendererName = rendererName;
            PluginDirectory = pluginDirectory;
            ManifestPath = manifestPath;
            Manifest = manifest;
            Metadata = metadata ?? new RendererPluginMetadata();
            Logger = logger ?? Log.Logger;
            Analysis = new RendererAnalysisService(Metadata.AnalysisFormats);
            Cache = new RendererCacheService(rendererId);
        }
    }

    /// <summary>Resolves renderer-declared source analysis files without coupling
    /// plugins to OpenUtau's render-output cache.</summary>
    public sealed class RendererAnalysisService {
        readonly IReadOnlyDictionary<string, AnalysisFormatManifest> formats;

        internal RendererAnalysisService(IReadOnlyDictionary<string, AnalysisFormatManifest> formats) {
            this.formats = formats;
        }

        public IReadOnlyDictionary<string, AnalysisFormatManifest> Formats => formats;

        public string GetPath(string format, string sourceFile) {
            if (!formats.TryGetValue(format, out var descriptor)) {
                throw new KeyNotFoundException($"Unknown renderer analysis format '{format}'.");
            }
            var fullSource = Path.GetFullPath(sourceFile);
            var directory = Path.GetDirectoryName(fullSource) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(fullSource);
            return descriptor.path
                .Replace("{wav_dir}", directory, StringComparison.Ordinal)
                .Replace("{wav_stem}", stem, StringComparison.Ordinal)
                .Replace("{wav_name}", Path.GetFileName(fullSource), StringComparison.Ordinal);
        }

        public RendererAnalysisState GetBasicState(string format, string sourceFile) {
            var outputFile = GetPath(format, sourceFile);
            if (!File.Exists(outputFile)) return RendererAnalysisState.Missing;
            if (!File.Exists(sourceFile)) return RendererAnalysisState.Invalid;
            return File.GetLastWriteTimeUtc(outputFile) < File.GetLastWriteTimeUtc(sourceFile)
                ? RendererAnalysisState.Stale
                : RendererAnalysisState.Valid;
        }

    }

    /// <summary>Provides namespaced final-output cache paths. Intermediate engine
    /// state belongs in memory; reusable source analysis belongs beside the source.</summary>
    public sealed class RendererCacheService {
        readonly string rendererKey;
        internal RendererCacheService(string rendererId) {
            rendererKey = string.Concat(rendererId.Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        }

        public string GetPhrasePath(RenderPhrase phrase, string extension = ".wav") {
            if (string.IsNullOrEmpty(extension)) extension = ".wav";
            if (!extension.StartsWith('.')) extension = "." + extension;
            var path = Path.Combine(PathManager.Inst.CachePath,
                $"renderer-{rendererKey}-{phrase.hash:x16}{extension}");
            phrase.AddCacheFile(path);
            return path;
        }
    }
}
