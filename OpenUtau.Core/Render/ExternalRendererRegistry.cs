using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Render {
    public sealed record RendererAnalysisOption(
        string RendererId, string RendererName, string Format, string FormatName);

    public sealed record RendererDiscoveryDiagnostic(
        string Path, string Message, string Details);

    public sealed class ExternalRendererDescriptor {
        public string Id { get; }
        public string Name { get; }
        public USingerType SingerType { get; }
        public string ManifestPath { get; }
        public ResamplerManifest Manifest { get; }
        public RendererPluginMetadata Metadata { get; }
        internal string AssemblyPath { get; }
        internal string TypeName { get; }

        internal ExternalRendererDescriptor(
            string id, string name, USingerType singerType, string manifestPath,
            ResamplerManifest manifest, RendererPluginMetadata metadata = null,
            string assemblyPath = null, string typeName = null) {
            Id = id;
            Name = name;
            SingerType = singerType;
            ManifestPath = manifestPath;
            Manifest = manifest;
            Metadata = metadata ?? new RendererPluginMetadata();
            AssemblyPath = assemblyPath;
            TypeName = typeName;
        }
    }

    /// <summary>
    /// Discovers renderer metadata declared by manifests or attributed plugin classes,
    /// and creates a fresh plugin instance when its renderer is selected.
    /// </summary>
    public static class ExternalRendererRegistry {
        public const int ApiVersion = 1;
        static readonly object locker = new object();
        static IReadOnlyList<ExternalRendererDescriptor> renderers = Array.Empty<ExternalRendererDescriptor>();
        static IReadOnlyList<RendererDiscoveryDiagnostic> diagnostics =
            Array.Empty<RendererDiscoveryDiagnostic>();

        public static IReadOnlyList<ExternalRendererDescriptor> Renderers {
            get { lock (locker) { return renderers.ToArray(); } }
        }
        public static IReadOnlyList<RendererDiscoveryDiagnostic> Diagnostics {
            get { lock (locker) { return diagnostics.ToArray(); } }
        }

        public static void Discover(string basePath) {
            var discovered = new List<ExternalRendererDescriptor>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveryDiagnostics = new List<RendererDiscoveryDiagnostic>();
            try {
                Directory.CreateDirectory(basePath);
                foreach (var path in Directory.EnumerateFiles(basePath, "*.yaml", new EnumerationOptions {
                    RecurseSubdirectories = true,
                }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                    try {
                        var manifest = ResamplerManifest.Load(path);
                        var renderer = manifest.renderer;
                        if (renderer == null || !renderer.enabled) {
                            continue;
                        }
                        Validate(renderer, path);
                        var metadata = NormalizeMetadata(new RendererPluginMetadata {
                            Capabilities = renderer.capabilities ?? new RendererCapabilitiesManifest(),
                            AnalysisFormats = manifest.analysis?.formats
                                ?? new Dictionary<string, AnalysisFormatManifest>(),
                            Expressions = manifest.expressions,
                        });
                        ValidateMetadata(metadata, path);
                        if (ids.Contains(renderer.id)) {
                            Log.Warning("Ignoring external renderer with duplicate id {Id} in {ManifestPath}", renderer.id, path);
                            continue;
                        }
                        if (names.Contains(renderer.name)) {
                            Log.Warning("Ignoring external renderer with duplicate name {Name} in {ManifestPath}", renderer.name, path);
                            continue;
                        }
                        ids.Add(renderer.id);
                        names.Add(renderer.name);
                        discovered.Add(new ExternalRendererDescriptor(
                            renderer.id, renderer.name, USingerType.Classic, path, manifest, metadata));
                    } catch (Exception e) {
                        Log.Error(e, "Failed to discover external renderer manifest {ManifestPath}", path);
                        discoveryDiagnostics.Add(new(path,
                            "Failed to discover external renderer manifest.", e.Message));
                    }
                }
                foreach (var path in Directory.EnumerateFiles(basePath, "*.dll", new EnumerationOptions {
                    RecurseSubdirectories = true,
                }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                    DiscoverAssembly(path, discovered, ids, names, discoveryDiagnostics);
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to search external renderers in {BasePath}", basePath);
                discoveryDiagnostics.Add(new(basePath,
                    "Failed to search external renderers.", e.Message));
            }
            lock (locker) {
                renderers = discovered;
                diagnostics = discoveryDiagnostics;
            }
        }

        static void DiscoverAssembly(
            string path,
            List<ExternalRendererDescriptor> discovered,
            HashSet<string> ids,
            HashSet<string> names,
            List<RendererDiscoveryDiagnostic> discoveryDiagnostics) {
            if (!HasExternalRendererAttribute(path, out var inspectionError)) {
                if (inspectionError != null) {
                    Log.Warning(inspectionError, "Failed to inspect renderer metadata in {AssemblyPath}", path);
                    discoveryDiagnostics.Add(new(path,
                        "Failed to inspect renderer assembly metadata.", inspectionError.Message));
                }
                return;
            }
            RendererLoadContext loadContext = null;
            try {
                loadContext = new RendererLoadContext(path);
                var assembly = loadContext.LoadPluginAssembly(path);
                foreach (var type in assembly.GetExportedTypes()) {
                    var attribute = type.GetCustomAttribute<ExternalRendererAttribute>();
                    if (attribute == null || type.IsAbstract
                            || !typeof(IOpenUtauRendererPlugin).IsAssignableFrom(type)) {
                        continue;
                    }
                    var plugin = (IOpenUtauRendererPlugin)Activator.CreateInstance(type)!;
                    try {
                        if (plugin.ApiVersion != ApiVersion) {
                            throw new InvalidDataException(
                                $"Renderer {attribute.Id} implements API {plugin.ApiVersion}; host supports API {ApiVersion}.");
                        }
                        var metadata = NormalizeMetadata(plugin.Metadata);
                        ValidateMetadata(metadata, path);
                        if (ids.Contains(attribute.Id) || names.Contains(attribute.Name)) {
                            Log.Warning("Ignoring duplicate external renderer {Id} in {AssemblyPath}", attribute.Id, path);
                            continue;
                        }
                        var manifest = CreateSyntheticManifest(attribute, metadata, path, type);
                        discovered.Add(new ExternalRendererDescriptor(
                            attribute.Id, attribute.Name, attribute.SingerType, path, manifest,
                            metadata, path, type.FullName));
                        ids.Add(attribute.Id);
                        names.Add(attribute.Name);
                    } finally {
                        if (plugin is IDisposable disposable) disposable.Dispose();
                    }
                }
            } catch (Exception e) {
                Log.Warning(e, "Failed to inspect external renderer assembly {AssemblyPath}", path);
                discoveryDiagnostics.Add(new(path,
                    "Failed to load external renderer metadata.", e.Message));
            } finally {
                loadContext?.Unload();
            }
        }

        static bool HasExternalRendererAttribute(string path, out Exception error) {
            error = null;
            try {
                using var stream = File.OpenRead(path);
                using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
                if (!pe.HasMetadata) return false;
                var reader = pe.GetMetadataReader();
                foreach (var typeHandle in reader.TypeDefinitions) {
                    var type = reader.GetTypeDefinition(typeHandle);
                    foreach (var attributeHandle in type.GetCustomAttributes()) {
                        var attribute = reader.GetCustomAttribute(attributeHandle);
                        EntityHandle owner = default;
                        if (attribute.Constructor.Kind == HandleKind.MemberReference) {
                            owner = reader.GetMemberReference(
                                (MemberReferenceHandle)attribute.Constructor).Parent;
                        } else if (attribute.Constructor.Kind == HandleKind.MethodDefinition) {
                            owner = reader.GetMethodDefinition(
                                (MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                        }
                        (string name, string ns) = owner.Kind switch {
                            HandleKind.TypeReference => (
                                reader.GetString(reader.GetTypeReference((TypeReferenceHandle)owner).Name),
                                reader.GetString(reader.GetTypeReference((TypeReferenceHandle)owner).Namespace)),
                            HandleKind.TypeDefinition => (
                                reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)owner).Name),
                                reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)owner).Namespace)),
                            _ => (string.Empty, string.Empty),
                        };
                        if (name == nameof(ExternalRendererAttribute) &&
                                ns == typeof(ExternalRendererAttribute).Namespace) {
                            return true;
                        }
                    }
                }
                return false;
            } catch (BadImageFormatException) {
                return false;
            } catch (Exception exception) {
                error = exception;
                return false;
            }
        }

        static ResamplerManifest CreateSyntheticManifest(
            ExternalRendererAttribute attribute,
            RendererPluginMetadata metadata,
            string path,
            Type type) {
            return new ResamplerManifest {
                renderer = new RendererManifest {
                    enabled = true,
                    id = attribute.Id,
                    name = attribute.Name,
                    capabilities = metadata.Capabilities,
                    bridge = new RendererBridgeManifest {
                        assembly = Path.GetFileName(path),
                        type = type.FullName,
                        apiVersion = ApiVersion,
                    },
                },
                expressions = metadata.Expressions.ToDictionary(pair => pair.Key, pair => pair.Value),
                analysis = new AnalysisManifest {
                    formats = metadata.AnalysisFormats.ToDictionary(pair => pair.Key, pair => pair.Value),
                },
            };
        }

        public static IRenderer CreateRenderer(string name) {
            ExternalRendererDescriptor descriptor;
            lock (locker) {
                descriptor = renderers.FirstOrDefault(item =>
                    string.Equals(item.Id, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            return descriptor == null ? null : LoadRenderer(descriptor);
        }

        public static IReadOnlyList<RendererAnalysisOption> GetAnalysisOptions() {
            lock (locker) {
                return renderers.SelectMany(renderer => renderer.Metadata.AnalysisFormats
                    .Where(pair => pair.Value.canGenerate)
                    .Select(pair => new RendererAnalysisOption(
                        renderer.Id, renderer.Name, pair.Key, pair.Value.name ?? pair.Key)))
                    .ToArray();
            }
        }

        public static async Task<IReadOnlyList<RendererAnalysisResult>> GenerateAnalysisAsync(
            string rendererId,
            string format,
            IReadOnlyList<string> sourceFiles,
            bool overwrite,
            IProgress<int> progress,
            CancellationToken cancellation) {
            ExternalRendererDescriptor descriptor;
            lock (locker) {
                descriptor = renderers.FirstOrDefault(renderer =>
                    string.Equals(renderer.Id, rendererId, StringComparison.OrdinalIgnoreCase));
            }
            if (descriptor == null) throw new KeyNotFoundException($"Renderer '{rendererId}' was not found.");
            if (!descriptor.Metadata.AnalysisFormats.TryGetValue(format, out var analysis) ||
                    !analysis.canGenerate) {
                throw new InvalidOperationException(
                    $"Renderer '{rendererId}' cannot generate analysis format '{format}'.");
            }
            var (plugin, context, loadContext) = LoadPlugin(descriptor);
            try {
                var provider = plugin.CreateAnalysisProvider(context)
                    ?? throw new NotSupportedException(
                        $"Renderer '{rendererId}' declares '{format}' as generatable but returned no analysis provider.");
                try {
                    var allRequests = sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(source => new RendererAnalysisRequest(
                            format, Path.GetFullPath(source), context.Analysis.GetPath(format, source), overwrite))
                        .ToArray();
                    var skipped = allRequests.Where(request => !overwrite && File.Exists(request.OutputFile))
                        .Select(request => new RendererAnalysisResult(
                            request, RendererAnalysisOutcome.AlreadyValid)).ToArray();
                    var requests = allRequests.Where(request => overwrite || !File.Exists(request.OutputFile)).ToArray();
                    var generated = await RunProviderAsync(provider, requests, progress, cancellation);
                    return skipped.Concat(generated).ToArray();
                } finally {
                    if (provider is IDisposable disposable) disposable.Dispose();
                }
            } finally {
                if (plugin is IDisposable pluginDisposable) pluginDisposable.Dispose();
                loadContext.Unload();
            }
        }

        static async Task<IReadOnlyList<RendererAnalysisResult>> RunProviderAsync(
                IRendererAnalysisProvider provider,
                IReadOnlyList<RendererAnalysisRequest> requests,
                IProgress<int> progress,
                CancellationToken cancellation) {
            if (requests.Count == 0) return Array.Empty<RendererAnalysisResult>();
            var results = await provider.GenerateAsync(requests, progress, cancellation)
                ?? Array.Empty<RendererAnalysisResult>();
            var byOutput = results
                .Where(result => result?.Request != null &&
                    !string.IsNullOrWhiteSpace(result.Request.OutputFile))
                .Select(result => (Result: result, Output: TryGetFullPath(result.Request.OutputFile)))
                .Where(item => item.Output != null)
                .GroupBy(item => item.Output!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Result,
                    StringComparer.OrdinalIgnoreCase);
            return requests.Select(request => {
                if (!byOutput.TryGetValue(Path.GetFullPath(request.OutputFile), out var result)) {
                    return new RendererAnalysisResult(request, RendererAnalysisOutcome.Failed,
                        "The renderer did not report a result for this request.");
                }
                if (result.Outcome is RendererAnalysisOutcome.Generated or RendererAnalysisOutcome.AlreadyValid
                        && !File.Exists(request.OutputFile)) {
                    return new RendererAnalysisResult(request, RendererAnalysisOutcome.Failed,
                        "The renderer reported success but did not create the output file.");
                }
                return result with { Request = request };
            }).ToArray();
        }

        static string? TryGetFullPath(string path) {
            try {
                return Path.GetFullPath(path);
            } catch (Exception exception) when (exception is ArgumentException or
                    NotSupportedException or PathTooLongException) {
                return null;
            }
        }

        public static async Task PrepareRequiredAnalysisAsync(
            string rendererId,
            IReadOnlyList<string> sourceFiles,
            IProgress<int> progress,
            CancellationToken cancellation) {
            ExternalRendererDescriptor descriptor;
            lock (locker) {
                descriptor = renderers.FirstOrDefault(renderer =>
                    string.Equals(renderer.Id, rendererId, StringComparison.OrdinalIgnoreCase));
            }
            if (descriptor == null) throw new KeyNotFoundException($"Renderer '{rendererId}' was not found.");
            var (plugin, context, loadContext) = LoadPlugin(descriptor);
            try {
                var provider = plugin.CreateAnalysisProvider(context);
                try {
                    await PrepareRequiredAnalysisAsync(provider, context, sourceFiles, progress, cancellation);
                } finally {
                    if (provider is IDisposable providerDisposable) providerDisposable.Dispose();
                }
            } finally {
                if (plugin is IDisposable disposable) disposable.Dispose();
                loadContext.Unload();
            }
        }

        static async Task PrepareRequiredAnalysisAsync(
            IRendererAnalysisProvider? provider,
            RendererPluginContext context,
            IReadOnlyList<string> sourceFiles,
            IProgress<int> progress,
            CancellationToken cancellation) {
            var required = context.Analysis.Formats.Where(pair => pair.Value.required).ToArray();
            if (required.Length == 0) return;
            var requests = new List<RendererAnalysisRequest>();
            foreach (var source in sourceFiles.Distinct(StringComparer.OrdinalIgnoreCase)) {
                foreach (var pair in required) {
                    cancellation.ThrowIfCancellationRequested();
                    var request = new RendererAnalysisRequest(pair.Key, Path.GetFullPath(source),
                        context.Analysis.GetPath(pair.Key, source), true);
                    var state = provider == null
                        ? context.Analysis.GetBasicState(pair.Key, request.SourceFile)
                        : await provider.ValidateAsync(request, cancellation);
                    if (state == RendererAnalysisState.Valid) continue;
                    if (!pair.Value.canGenerate) {
                        throw new InvalidDataException(
                            $"Required analysis '{pair.Key}' for '{source}' is {state.ToString().ToLowerInvariant()} " +
                            $"and renderer '{context.RendererId}' cannot generate it.");
                    }
                    requests.Add(request);
                }
            }
            if (requests.Count == 0) return;
            if (provider == null) {
                throw new NotSupportedException(
                    $"Renderer '{context.RendererId}' declares required analysis as generatable but returned no provider.");
            }
            var results = await RunProviderAsync(provider, requests, progress, cancellation);
            var failures = results.Where(result => result.Outcome != RendererAnalysisOutcome.Generated
                && result.Outcome != RendererAnalysisOutcome.AlreadyValid).ToArray();
            if (failures.Length > 0) {
                throw new InvalidDataException(
                    $"Renderer '{context.RendererId}' failed to generate {failures.Length} required analysis file(s): " +
                    string.Join("; ", failures.Select(result =>
                        $"{result.Request.SourceFile}: {result.Message ?? result.Outcome.ToString()}")));
            }
            foreach (var request in requests) {
                cancellation.ThrowIfCancellationRequested();
                var state = await provider.ValidateAsync(request, cancellation);
                if (state != RendererAnalysisState.Valid) {
                    throw new InvalidDataException(
                        $"Renderer '{context.RendererId}' generated '{request.OutputFile}', but validation returned {state}.");
                }
            }
        }

        static void Validate(RendererManifest renderer, string path) {
            if (string.IsNullOrWhiteSpace(renderer.id)) {
                throw new InvalidDataException($"Renderer id is missing in {path}.");
            }
            if (string.IsNullOrWhiteSpace(renderer.name)) {
                throw new InvalidDataException($"Renderer name is missing in {path}.");
            }
            if (renderer.bridge == null || string.IsNullOrWhiteSpace(renderer.bridge.assembly)
                    || string.IsNullOrWhiteSpace(renderer.bridge.type)) {
                throw new InvalidDataException($"Renderer bridge assembly or type is missing in {path}.");
            }
            if (renderer.bridge.apiVersion != ApiVersion) {
                throw new InvalidDataException(
                    $"Renderer {renderer.id} requests API {renderer.bridge.apiVersion}; host supports API {ApiVersion}.");
            }
            if (renderer.capabilities?.parallelism < 0) {
                throw new InvalidDataException(
                    $"Renderer {renderer.id} declares negative parallelism in {path}.");
            }
        }

        static void ValidateMetadata(RendererPluginMetadata metadata, string origin) {
            var abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in metadata.Expressions) {
                if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Value.abbr)) {
                    throw new InvalidDataException(
                        $"Renderer expression '{pair.Key}' has no descriptor or abbreviation in {origin}.");
                }
                if (!string.Equals(pair.Key, pair.Value.abbr, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidDataException(
                        $"Renderer expression key '{pair.Key}' does not match descriptor abbreviation " +
                        $"'{pair.Value.abbr}' in {origin}.");
                }
                if (!abbreviations.Add(pair.Value.abbr)) {
                    throw new InvalidDataException(
                        $"Renderer expression abbreviation '{pair.Value.abbr}' is duplicated in {origin}.");
                }
            }
            foreach (var pair in metadata.AnalysisFormats) {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) {
                    throw new InvalidDataException(
                        $"Renderer analysis format has no key or descriptor in {origin}.");
                }
                if (string.IsNullOrWhiteSpace(pair.Value.path)) {
                    throw new InvalidDataException(
                        $"Renderer analysis format '{pair.Key}' has no path in {origin}.");
                }
            }
            foreach (var pair in metadata.Settings) {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null ||
                        string.IsNullOrWhiteSpace(pair.Value.Name)) {
                    throw new InvalidDataException(
                        $"Renderer setting has no key, descriptor, or name in {origin}.");
                }
                if (pair.Value.Type == RendererSettingType.Choice &&
                        (pair.Value.Choices.Count == 0 ||
                         !pair.Value.Choices.Contains(pair.Value.DefaultValue))) {
                    throw new InvalidDataException(
                        $"Renderer choice setting '{pair.Key}' has an invalid default in {origin}.");
                }
                if (pair.Value.Min > pair.Value.Max) {
                    throw new InvalidDataException(
                        $"Renderer setting '{pair.Key}' has min greater than max in {origin}.");
                }
            }
            if (metadata.Capabilities.parallelism < 0) {
                throw new InvalidDataException(
                    $"Renderer declares negative parallelism in {origin}.");
            }
        }

        static RendererPluginMetadata NormalizeMetadata(RendererPluginMetadata? metadata) => new() {
            Capabilities = metadata?.Capabilities ?? new RendererCapabilitiesManifest(),
            AnalysisFormats = metadata?.AnalysisFormats
                ?? new Dictionary<string, AnalysisFormatManifest>(),
            Expressions = metadata?.Expressions
                ?? new Dictionary<string, UExpressionDescriptor>(),
            Settings = metadata?.Settings
                ?? new Dictionary<string, RendererSettingDescriptor>(),
        };

        static IRenderer LoadRenderer(ExternalRendererDescriptor descriptor) {
            var (plugin, context, loadContext) = LoadPlugin(descriptor);
            IRenderer renderer;
            try {
                renderer = plugin.CreateRenderer(context)
                    ?? throw new InvalidOperationException($"Renderer plugin {descriptor.Id} returned null.");
            } catch {
                if (plugin is IDisposable disposable) disposable.Dispose();
                loadContext.Unload();
                throw;
            }
            try {
                if (renderer.SingerType != descriptor.SingerType) {
                    throw new InvalidDataException(
                        $"Renderer {descriptor.Id} declared singer type {descriptor.SingerType} but returned {renderer.SingerType}.");
                }
                var capabilities = descriptor.Metadata.Capabilities;
                ValidateMetadata(descriptor.Metadata, descriptor.ManifestPath);
                if (capabilities.renderedPitch != renderer.SupportsRenderPitch) {
                    throw new InvalidDataException(
                        $"Renderer {descriptor.Id} declares renderedPitch={capabilities.renderedPitch}, " +
                        $"but its IRenderer reports SupportsRenderPitch={renderer.SupportsRenderPitch}.");
                }
                if (capabilities.realCurves != renderer.SupportsRealCurve) {
                    throw new InvalidDataException(
                        $"Renderer {descriptor.Id} declares realCurves={capabilities.realCurves}, " +
                        $"but its IRenderer reports SupportsRealCurve={renderer.SupportsRealCurve}.");
                }
                if (capabilities.parallelism < 0) {
                    throw new InvalidDataException(
                        $"Renderer {descriptor.Id} declares negative parallelism.");
                }
                return new ExternalRendererProxy(descriptor, plugin, renderer, context, loadContext);
            } catch {
                if (renderer is IDisposable rendererDisposable) rendererDisposable.Dispose();
                if (!ReferenceEquals(plugin, renderer) && plugin is IDisposable pluginDisposable)
                    pluginDisposable.Dispose();
                loadContext.Unload();
                throw;
            }
        }

        static (IOpenUtauRendererPlugin Plugin, RendererPluginContext Context,
                RendererLoadContext LoadContext) LoadPlugin(
                ExternalRendererDescriptor descriptor) {
            var bridge = descriptor.Manifest.renderer.bridge;
            var pluginDirectory = Path.GetDirectoryName(descriptor.AssemblyPath ?? descriptor.ManifestPath)!;
            var assemblyPath = descriptor.AssemblyPath ?? Path.GetFullPath(bridge.assembly, pluginDirectory);
            if (!File.Exists(assemblyPath)) {
                throw new FileNotFoundException("External renderer bridge assembly was not found.", assemblyPath);
            }
            var loadContext = new RendererLoadContext(assemblyPath);
            try {
                var assembly = loadContext.LoadPluginAssembly(assemblyPath);
                var type = assembly.GetType(descriptor.TypeName ?? bridge.type, throwOnError: true)!;
                if (Activator.CreateInstance(type) is not IOpenUtauRendererPlugin plugin) {
                    throw new InvalidCastException(
                        $"{bridge.type} does not implement {nameof(IOpenUtauRendererPlugin)}.");
                }
                if (plugin.ApiVersion != ApiVersion) {
                    if (plugin is IDisposable disposable) disposable.Dispose();
                    throw new InvalidDataException(
                        $"Renderer {descriptor.Id} implements API {plugin.ApiVersion}; host supports API {ApiVersion}.");
                }
                var context = new RendererPluginContext(
                    descriptor.Id, descriptor.Name, pluginDirectory, descriptor.ManifestPath,
                    descriptor.Manifest, descriptor.Metadata);
                return (plugin, context, loadContext);
            } catch {
                loadContext.Unload();
                throw;
            }
        }

        sealed class ExternalRendererProxy : IRenderer, IExternalRendererIdentity, IDisposable {
            readonly ExternalRendererDescriptor descriptor;
            readonly IOpenUtauRendererPlugin plugin;
            readonly IRenderer renderer;
            readonly RendererPluginContext context;
            readonly RendererLoadContext loadContext;
            readonly IRendererAnalysisProvider? analysisProvider;
            readonly SemaphoreSlim analysisLock = new(1, 1);
            readonly SemaphoreSlim? renderSlots;
            public ExternalRendererProxy(ExternalRendererDescriptor descriptor,
                    IOpenUtauRendererPlugin plugin, IRenderer renderer,
                    RendererPluginContext context, RendererLoadContext loadContext) {
                this.descriptor = descriptor;
                this.plugin = plugin;
                this.renderer = renderer;
                this.context = context;
                this.loadContext = loadContext;
                if (context.Analysis.Formats.Any(pair => pair.Value.required)) {
                    analysisProvider = plugin.CreateAnalysisProvider(context);
                }
                var parallelism = descriptor.Metadata.Capabilities.parallelism;
                if (parallelism > 0) renderSlots = new SemaphoreSlim(parallelism, parallelism);
            }
            public USingerType SingerType => renderer.SingerType;
            public string Id => descriptor.Id;
            public string Name => descriptor.Name;
            public bool SupportsRenderPitch => renderer.SupportsRenderPitch;
            public bool SupportsRealCurve => renderer.SupportsRealCurve;
            public bool SupportsExpression(UExpressionDescriptor expression) =>
                descriptor.Metadata.Expressions.Keys.Any(abbr =>
                    string.Equals(abbr, expression.abbr, StringComparison.OrdinalIgnoreCase)) ||
                renderer.SupportsExpression(expression);
            public RenderResult Layout(RenderPhrase phrase) => renderer.Layout(phrase);
            public async Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    CancellationTokenSource cancellation, bool isPreRender = false,
                    RenderPhraseEvents? renderEvents = null) {
                cancellation.Token.ThrowIfCancellationRequested();
                if (renderSlots != null) await renderSlots.WaitAsync(cancellation.Token);
                try {
                    var sources = phrase.phones.Where(phone => !phone.direct)
                        .Select(phone => phone.oto?.File).Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(path => path!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    await analysisLock.WaitAsync(cancellation.Token);
                    try {
                        await PrepareRequiredAnalysisAsync(analysisProvider, context, sources,
                            new Progress<int>(), cancellation.Token);
                    } finally {
                        analysisLock.Release();
                    }
                    var result = await renderer.Render(
                        phrase, progress, trackNo, cancellation, isPreRender, renderEvents);
                    cancellation.Token.ThrowIfCancellationRequested();
                    return result;
                } finally {
                    renderSlots?.Release();
                }
            }
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => renderer.LoadRenderedPitch(phrase);
            public RenderPitchResult LoadRenderedPitch(
                RenderPhrase phrase, HashSet<int> selectedNotePositions) =>
                renderer.LoadRenderedPitch(phrase, selectedNotePositions);
            public List<RenderRealCurveResult> LoadRenderedRealCurves(RenderPhrase phrase) => renderer.LoadRenderedRealCurves(phrase);
            public void ScheduleRealCurveRefresh(UProject project, UVoicePart part, UCommand command) =>
                renderer.ScheduleRealCurveRefresh(project, part, command);
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings settings) {
                var declared = descriptor.Metadata.Expressions.Values.ToDictionary(
                    expression => expression.abbr, StringComparer.OrdinalIgnoreCase);
                var runtime = renderer.GetSuggestedExpressions(singer, settings)
                    ?? Array.Empty<UExpressionDescriptor>();
                foreach (var expression in runtime) {
                    if (expression == null || string.IsNullOrWhiteSpace(expression.abbr)) {
                        throw new InvalidDataException(
                            $"Renderer {descriptor.Id} returned an expression without an abbreviation.");
                    }
                    if (declared.TryGetValue(expression.abbr, out var staticExpression)) {
                        if (!ExpressionsEqual(staticExpression, expression)) {
                            throw new InvalidDataException(
                                $"Renderer {descriptor.Id} returned a runtime definition for expression " +
                                $"'{expression.abbr}' that conflicts with its declared metadata.");
                        }
                    } else {
                        declared.Add(expression.abbr, expression);
                    }
                }
                return declared.Values.ToArray();
            }
            static bool ExpressionsEqual(UExpressionDescriptor left, UExpressionDescriptor right) =>
                string.Equals(left.name, right.name, StringComparison.Ordinal) &&
                string.Equals(left.abbr, right.abbr, StringComparison.OrdinalIgnoreCase) &&
                left.type == right.type && left.min == right.min && left.max == right.max &&
                left.defaultValue == right.defaultValue &&
                left.CustomDefaultValue == right.CustomDefaultValue &&
                left.isFlag == right.isFlag &&
                string.Equals(left.flag, right.flag, StringComparison.Ordinal) &&
                (left.options ?? Array.Empty<string>()).SequenceEqual(right.options ?? Array.Empty<string>()) &&
                left.skipOutputIfDefault == right.skipOutputIfDefault;
            public void Dispose() {
                if (renderer is IDisposable rendererDisposable) rendererDisposable.Dispose();
                if (analysisProvider is IDisposable providerDisposable) providerDisposable.Dispose();
                if (!ReferenceEquals(plugin, renderer) && plugin is IDisposable pluginDisposable)
                    pluginDisposable.Dispose();
                analysisLock.Dispose();
                renderSlots?.Dispose();
                loadContext.Unload();
            }
            public override string ToString() => descriptor.Name;
        }

        sealed class RendererLoadContext : AssemblyLoadContext {
            readonly AssemblyDependencyResolver resolver;
            static readonly HashSet<string> sharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase) {
                typeof(IOpenUtauRendererPlugin).Assembly.GetName().Name!,
                "Serilog",
                "NAudio.Core",
            };

            public RendererLoadContext(string pluginPath) : base(isCollectible: true) {
                resolver = new AssemblyDependencyResolver(pluginPath);
            }

            public Assembly LoadPluginAssembly(string path) {
                // Loading from a path keeps the bridge DLL locked on Windows until
                // the collectible context is finalized. A stream preserves normal
                // dependency resolution while allowing immediate plugin updates.
                using var stream = new MemoryStream(File.ReadAllBytes(Path.GetFullPath(path)), writable: false);
                return LoadFromStream(stream);
            }

            protected override Assembly Load(AssemblyName assemblyName) {
                if (sharedAssemblyNames.Contains(assemblyName.Name ?? string.Empty)) {
                    return Default.Assemblies.FirstOrDefault(assembly =>
                        string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                        ?? Default.LoadFromAssemblyName(assemblyName);
                }
                var path = resolver.ResolveAssemblyToPath(assemblyName);
                return path == null ? null : LoadFromAssemblyPath(path);
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
                var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
            }
        }
    }
}
