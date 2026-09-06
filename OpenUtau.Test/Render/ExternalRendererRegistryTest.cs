using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenUtau.Core;
using OpenUtau.Classic;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Render;

public class ExternalRendererRegistryTest {
    [Fact]
    public void MissingSavedRendererFallsBackWithoutReplacingStableId() {
        var track = new UTrack { Singer = new TestSinger() };
        var settings = new URenderSettings {
            renderer = "org.openutau.test.not-installed",
        };

        settings.Validate(track);

        Assert.Equal("org.openutau.test.not-installed", settings.renderer);
        Assert.NotNull(settings.Renderer);
        Assert.Equal(Renderers.WORLDLINE_R, settings.Renderer.ToString());
        Assert.Contains("not installed", settings.RendererLoadError);
    }

    [Fact]
    public void ExplicitMissingRendererSelectionDoesNotSilentlyFallback() {
        var track = new UTrack { Singer = new TestSinger() };
        var settings = new URenderSettings {
            renderer = "org.openutau.test.not-installed",
        };

        Assert.Throws<KeyNotFoundException>(() =>
            settings.Validate(track, fallbackUnavailableRenderer: false));

        Assert.Null(settings.Renderer);
    }

    [Fact]
    public void IgnoresUnrelatedDllWithoutLoadingIt() {
        var directory = CreateDirectory();
        try {
            File.WriteAllBytes(Path.Combine(directory, "native-or-unrelated.dll"),
                new byte[] { 0, 1, 2, 3 });

            ExternalRendererRegistry.Discover(directory);

            Assert.Empty(ExternalRendererRegistry.Renderers);
            Assert.Empty(ExternalRendererRegistry.Diagnostics);
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RetainsManifestDiscoveryDiagnostics() {
        var directory = CreateDirectory();
        try {
            var path = Path.Combine(directory, "broken.yaml");
            File.WriteAllText(path, "renderer: [not valid");

            ExternalRendererRegistry.Discover(directory);

            var diagnostic = Assert.Single(ExternalRendererRegistry.Diagnostics);
            Assert.Equal(path, diagnostic.Path);
            Assert.Contains("manifest", diagnostic.Message);
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void InvalidManifestDoesNotReserveRendererIdentity() {
        var directory = CreateDirectory();
        try {
            File.WriteAllText(Path.Combine(directory, "a-invalid.yaml"), """
                renderer:
                  enabled: true
                  id: org.openutau.test.reused
                  name: Invalid Renderer
                  bridge:
                    assembly: missing.dll
                    type: Missing.Plugin
                    api_version: 1
                analysis:
                  formats:
                    broken:
                      can_generate: true
                """);
            File.WriteAllText(Path.Combine(directory, "b-valid.yaml"), """
                renderer:
                  enabled: true
                  id: org.openutau.test.reused
                  name: Valid Renderer
                  bridge:
                    assembly: missing.dll
                    type: Missing.Plugin
                    api_version: 1
                """);

            ExternalRendererRegistry.Discover(directory);

            Assert.Equal("Valid Renderer", Assert.Single(ExternalRendererRegistry.Renderers).Name);
            Assert.Single(ExternalRendererRegistry.Diagnostics);
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DuplicateNameDoesNotReserveUnusedId() {
        var directory = CreateDirectory();
        try {
            static string Manifest(string id, string name) => $$"""
                renderer:
                  enabled: true
                  id: {{id}}
                  name: {{name}}
                  bridge:
                    assembly: missing.dll
                    type: Missing.Plugin
                    api_version: 1
                """;
            File.WriteAllText(Path.Combine(directory, "a.yaml"),
                Manifest("org.openutau.test.first", "Shared Name"));
            File.WriteAllText(Path.Combine(directory, "b.yaml"),
                Manifest("org.openutau.test.second", "Shared Name"));
            File.WriteAllText(Path.Combine(directory, "c.yaml"),
                Manifest("org.openutau.test.second", "Unique Name"));

            ExternalRendererRegistry.Discover(directory);

            Assert.Equal(2, ExternalRendererRegistry.Renderers.Count);
            Assert.Contains(ExternalRendererRegistry.Renderers,
                renderer => renderer.Name == "Unique Name");
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task GeneratesRendererDeclaredAnalysis() {
        var directory = CreateDirectory();
        try {
            File.Copy(typeof(TestRendererPlugin).Assembly.Location,
                Path.Combine(directory, "renderer-plugin.dll"));
            var source = Path.Combine(directory, "sample.wav");
            File.WriteAllBytes(source, new byte[] { 1 });
            ExternalRendererRegistry.Discover(directory);

            var option = Assert.Single(ExternalRendererRegistry.GetAnalysisOptions());
            var results = await ExternalRendererRegistry.GenerateAnalysisAsync(
                option.RendererId, option.Format, new[] { source }, true,
                new Progress<int>(), CancellationToken.None);

            Assert.Equal(RendererAnalysisOutcome.Generated, Assert.Single(results).Outcome);
            Assert.True(File.Exists(Path.Combine(directory, "sample.test-analysis")));
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ReportsPerFileAnalysisFailuresAndContinuesBatch() {
        var directory = CreateDirectory();
        try {
            File.Copy(typeof(TestRendererPlugin).Assembly.Location,
                Path.Combine(directory, "renderer-plugin.dll"));
            var good = Path.Combine(directory, "good.wav");
            var bad = Path.Combine(directory, "fail.wav");
            File.WriteAllText(good, "source");
            File.WriteAllText(bad, "source");
            ExternalRendererRegistry.Discover(directory);

            var results = await ExternalRendererRegistry.GenerateAnalysisAsync(
                "org.openutau.test.renderer", "test", new[] { bad, good }, true,
                new Progress<int>(), CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, result => result.Request.SourceFile == bad
                && result.Outcome == RendererAnalysisOutcome.Failed);
            Assert.Contains(results, result => result.Request.SourceFile == good
                && result.Outcome == RendererAnalysisOutcome.Generated);
            Assert.True(File.Exists(Path.Combine(directory, "good.test-analysis")));
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task GeneratesMissingRequiredAnalysisBeforeRendering() {
        var directory = CreateDirectory();
        try {
            File.Copy(typeof(TestRendererPlugin).Assembly.Location,
                Path.Combine(directory, "renderer-plugin.dll"));
            var source = Path.Combine(directory, "sample.wav");
            File.WriteAllText(source, "source");
            ExternalRendererRegistry.Discover(directory);

            await ExternalRendererRegistry.PrepareRequiredAnalysisAsync(
                "org.openutau.test.renderer", new[] { source },
                new Progress<int>(), CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(directory, "sample.test-analysis")));
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RegeneratesStaleRequiredAnalysis() {
        var directory = CreateDirectory();
        try {
            File.Copy(typeof(TestRendererPlugin).Assembly.Location,
                Path.Combine(directory, "renderer-plugin.dll"));
            var source = Path.Combine(directory, "sample.wav");
            var analysis = Path.Combine(directory, "sample.test-analysis");
            File.WriteAllText(source, "source");
            File.WriteAllText(analysis, "stale");
            File.SetLastWriteTimeUtc(analysis, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-1));
            ExternalRendererRegistry.Discover(directory);

            await ExternalRendererRegistry.PrepareRequiredAnalysisAsync(
                "org.openutau.test.renderer", new[] { source },
                new Progress<int>(), CancellationToken.None);

            Assert.Equal("ok", File.ReadAllText(analysis));
            Assert.True(File.GetLastWriteTimeUtc(analysis) >= File.GetLastWriteTimeUtc(source));
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DiscoversMetadataFromAttributedAssemblyWithoutManifest() {
        var directory = CreateDirectory();
        try {
            File.Copy(typeof(TestRendererPlugin).Assembly.Location,
                Path.Combine(directory, "renderer-plugin.dll"));

            ExternalRendererRegistry.Discover(directory);

            var descriptor = Assert.Single(ExternalRendererRegistry.Renderers);
            Assert.Equal("org.openutau.test.renderer", descriptor.Id);
            Assert.Equal("Test External Renderer", descriptor.Name);
            Assert.Equal(typeof(TestRendererPlugin).FullName, descriptor.Manifest.renderer.bridge.type);
            var renderer = ExternalRendererRegistry.CreateRenderer(descriptor.Id);
            var expression = Assert.Single(renderer.GetSuggestedExpressions(null, null));
            Assert.Equal("test-expression", expression.abbr);
            Assert.True(renderer.SupportsExpression(expression));
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RuntimeRendererOwnsACollectibleLoadContext() {
        var directory = CreateDirectory();
        try {
            File.Copy(typeof(TestRendererPlugin).Assembly.Location,
                Path.Combine(directory, "renderer-plugin.dll"));
            ExternalRendererRegistry.Discover(directory);
            var renderer = ExternalRendererRegistry.CreateRenderer(
                "org.openutau.test.renderer");
            var field = renderer.GetType().GetField(
                "loadContext", BindingFlags.Instance | BindingFlags.NonPublic);
            var context = Assert.IsAssignableFrom<AssemblyLoadContext>(
                field?.GetValue(renderer));

            Assert.True(context.IsCollectible);

            Assert.IsAssignableFrom<IDisposable>(renderer).Dispose();
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void DiscoversManifestWithoutLoadingAssembly() {
        var directory = CreateDirectory();
        try {
            File.WriteAllText(Path.Combine(directory, "renderer.yaml"), """
                renderer:
                  enabled: true
                  id: org.openutau.test.missing
                  name: Missing Assembly Renderer
                  bridge:
                    assembly: does-not-exist.dll
                    type: Missing.Plugin
                    api_version: 1
                """);

            ExternalRendererRegistry.Discover(directory);

            var descriptor = Assert.Single(ExternalRendererRegistry.Renderers);
            Assert.Equal("org.openutau.test.missing", descriptor.Id);
            Assert.Equal("Missing Assembly Renderer", descriptor.Name);
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void LoadsPluginWhenRendererIsCreated() {
        var directory = CreateDirectory();
        try {
            var assembly = typeof(TestRendererPlugin).Assembly.Location.Replace("\\", "/");
            File.WriteAllText(Path.Combine(directory, "renderer.yaml"), $$"""
                renderer:
                  enabled: true
                  id: org.openutau.test.renderer
                  name: Test External Renderer
                  bridge:
                    assembly: "{{assembly}}"
                    type: OpenUtau.Test.Render.TestRendererPlugin
                    api_version: 1
                """);
            ExternalRendererRegistry.Discover(directory);

            var renderer = ExternalRendererRegistry.CreateRenderer("Test External Renderer");

            Assert.NotNull(renderer);
            Assert.Equal("Test External Renderer", renderer.ToString());
            Assert.Equal("org.openutau.test.renderer", Renderers.GetRendererId(renderer));
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RejectsRuntimeCapabilityMismatch() {
        var directory = CreateDirectory();
        try {
            var assembly = typeof(TestRendererPlugin).Assembly.Location.Replace("\\", "/");
            File.WriteAllText(Path.Combine(directory, "renderer.yaml"), $$"""
                renderer:
                  enabled: true
                  id: org.openutau.test.renderer
                  name: Test External Renderer
                  bridge:
                    assembly: "{{assembly}}"
                    type: OpenUtau.Test.Render.TestRendererPlugin
                    api_version: 1
                  capabilities:
                    rendered_pitch: true
                """);
            ExternalRendererRegistry.Discover(directory);

            var exception = Assert.Throws<InvalidDataException>(() =>
                ExternalRendererRegistry.CreateRenderer("org.openutau.test.renderer"));

            Assert.Contains("SupportsRenderPitch=False", exception.Message);
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void RejectsConflictingRuntimeExpressionDefinition() {
        var directory = CreateDirectory();
        try {
            var assembly = typeof(TestRendererPlugin).Assembly.Location.Replace("\\", "/");
            File.WriteAllText(Path.Combine(directory, "renderer.yaml"), $$"""
                renderer:
                  enabled: true
                  id: org.openutau.test.conflicting-expression
                  name: Conflict Renderer
                  bridge:
                    assembly: "{{assembly}}"
                    type: OpenUtau.Test.Render.TestRendererPlugin
                    api_version: 1
                expressions:
                  test-expression:
                    name: Static Definition
                    abbr: test-expression
                    type: Numerical
                    min: 0
                    max: 100
                    default_value: 0
                """);
            ExternalRendererRegistry.Discover(directory);
            var renderer = ExternalRendererRegistry.CreateRenderer(
                "org.openutau.test.conflicting-expression");

            var exception = Assert.Throws<InvalidDataException>(() =>
                renderer.GetSuggestedExpressions(null, null));

            Assert.Contains("conflicts with its declared metadata", exception.Message);
        } finally {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ParsesCapabilitiesAndAnalysisMetadata() {
        var directory = CreateDirectory();
        try {
            File.WriteAllText(Path.Combine(directory, "renderer.yaml"), """
                renderer:
                  enabled: true
                  id: org.openutau.test.metadata
                  name: Metadata Renderer
                  bridge:
                    assembly: renderer.dll
                    type: Renderer.Plugin
                    api_version: 1
                  capabilities:
                    cancellation: true
                    parallelism: 2
                analysis:
                  formats:
                    llsm2:
                      name: LLSM2
                      path: "{wav_dir}/{wav_stem}.llsm2"
                      required: true
                      can_generate: true
                      shared: false
                """);

            ExternalRendererRegistry.Discover(directory);

            var manifest = Assert.Single(ExternalRendererRegistry.Renderers).Manifest;
            Assert.True(manifest.renderer.capabilities.cancellation);
            Assert.Equal(2, manifest.renderer.capabilities.parallelism);
            Assert.True(manifest.analysis.formats["llsm2"].required);
            Assert.True(manifest.analysis.formats["llsm2"].canGenerate);
        } finally {
            Directory.Delete(directory, true);
        }
    }

    static string CreateDirectory() {
        var path = Path.Combine(Path.GetTempPath(), $"openutau-renderer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class TestSinger : USinger {
    public TestSinger() {
        found = true;
        loaded = true;
    }
    public override string Id => "test-singer";
    public override string Name => "Test Singer";
    public override USingerType SingerType => USingerType.Classic;
}

[ExternalRenderer("org.openutau.test.renderer", "Test External Renderer")]
public sealed class TestRendererPlugin : IOpenUtauRendererPlugin {
    public int ApiVersion => 1;
    public RendererPluginMetadata Metadata => new() {
        AnalysisFormats = new Dictionary<string, AnalysisFormatManifest> {
            ["test"] = new() {
                name = "Test analysis",
                path = "{wav_dir}/{wav_stem}.test-analysis",
                required = true,
                canGenerate = true,
            },
        },
        Expressions = new Dictionary<string, UExpressionDescriptor> {
            ["test-expression"] = new("Test Expression", "test-expression", 0, 100, 0),
        },
    };
    public IRenderer CreateRenderer(RendererPluginContext context) =>
        new TestRenderer(context.Manifest.renderer.name);
    public IRendererAnalysisProvider CreateAnalysisProvider(RendererPluginContext context) =>
        new TestAnalysisProvider(context.Analysis);
}

public sealed class TestAnalysisProvider : IRendererAnalysisProvider {
    readonly RendererAnalysisService analysis;
    public TestAnalysisProvider(RendererAnalysisService analysis) => this.analysis = analysis;
    public Task<IReadOnlyList<RendererAnalysisResult>> GenerateAsync(
            IReadOnlyList<RendererAnalysisRequest> requests,
            IProgress<int> progress, CancellationToken cancellation) {
        var results = new List<RendererAnalysisResult>();
        for (int i = 0; i < requests.Count; ++i) {
            if (Path.GetFileName(requests[i].SourceFile) == "fail.wav") {
                results.Add(new RendererAnalysisResult(
                    requests[i], RendererAnalysisOutcome.Failed, "Expected test failure."));
            } else {
                File.WriteAllText(requests[i].OutputFile, "ok");
                results.Add(new RendererAnalysisResult(
                    requests[i], RendererAnalysisOutcome.Generated));
            }
            progress.Report(i + 1);
        }
        return Task.FromResult<IReadOnlyList<RendererAnalysisResult>>(results);
    }
    public ValueTask<RendererAnalysisState> ValidateAsync(
            RendererAnalysisRequest request, CancellationToken cancellation) =>
        ValueTask.FromResult(analysis.GetBasicState(request.Format, request.SourceFile));
}

public sealed class TestRenderer : IRenderer {
    readonly string name;
    public TestRenderer(string name) => this.name = name;
    public USingerType SingerType => USingerType.Classic;
    public bool SupportsRenderPitch => false;
    public bool SupportsExpression(UExpressionDescriptor descriptor) => false;
    public RenderResult Layout(RenderPhrase phrase) => new();
    public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
        CancellationTokenSource cancellation, bool isPreRender = false,
        RenderPhraseEvents renderEvents = null) =>
        Task.FromResult(new RenderResult { samples = Array.Empty<float>() });
    public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
    public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
        name == "Conflict Renderer"
            ? new[] { new UExpressionDescriptor(
                "Runtime Definition", "test-expression", 0, 100, 0) }
            : Array.Empty<UExpressionDescriptor>();
    public override string ToString() => name;
}
