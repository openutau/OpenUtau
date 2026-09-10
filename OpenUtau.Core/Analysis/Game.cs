using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Analysis;

public class GameConfig {
    [JsonPropertyName("samplerate")] public int SampleRate { get; set; } = 44100;

    [JsonPropertyName("timestep")] public float Timestep { get; set; } = 0.01f;

    [JsonPropertyName("languages")] public Dictionary<string, int>? Languages { get; set; }

    [JsonPropertyName("loop")] public bool Loop { get; set; } = true;
}

/// <summary>
/// Parameters for GAME inference, aligned with infer.py CLI options.
/// </summary>
public class GameOptions {
    /// <summary>Language code, e.g. "en", "zh". Null = auto/universal.</summary>
    public string? LanguageCode { get; set; } = null;

    /// <summary>Number of D3PM sampling steps (--nsteps). Default: 8</summary>
    public int SamplingSteps { get; set; } = 8;

    /// <summary>Boundary decoding threshold (--seg-threshold). Default: 0.2</summary>
    public float BoundaryThreshold { get; set; } = 0.2f;

    /// <summary>Boundary decoding radius in frames (--seg-radius). Default: 2</summary>
    public int BoundaryRadius { get; set; } = 2;

    /// <summary>Note presence threshold (--est-threshold). Default: 0.2</summary>
    public float ScoreThreshold { get; set; } = 0.2f;

    /// <summary>
    /// RNG seed driving the D3PM stochastic boundary removal. 0 = random per
    /// inference. Honored by the GGML backend for reproducible runs; the ONNX
    /// path reads its own RNG stream and ignores this.
    /// </summary>
    public ulong Seed { get; set; } = 0;
}

/// <summary>
/// GAME MIDI extractor. This class is a thin <see cref="MidiExtractor{TOptions}"/>
/// orchestrator over a pluggable <see cref="IGameBackend"/> (ONNX or GGML).
/// Audio chunking, resampling, batching and note→tick mapping live in the base
/// class; the inference contract is delegated entirely to the active backend,
/// selected via <see cref="GameBackendFactory"/> from the user's preferences.
/// </summary>
public class Game : MidiExtractor<GameOptions> {
    private const string PackageId = "game";
    public const string DownloadUrl = "https://github.com/openvpi/GAME/releases/tag/oudep";

    readonly IGameBackend backend;
    readonly GameConfig config;
    bool disposed = false;
    volatile bool stopping = false;

    protected override int ExpectedSampleRate => config.SampleRate;
    public float Timestep => config.Timestep;
    public IReadOnlyDictionary<string, int>? Languages => config.Languages;

    /// <summary>The resolved backend's display name (e.g. "GGML").</summary>
    public string BackendName => backend.Name;

    /// <summary>
    /// Check if any GAME backend is installed (ONNX or GGML) without loading models.
    /// </summary>
    public static bool IsInstalled(string? location = null) {
        return location != null
            ? GameOnnxBackend.IsInstalled(location)
            : GameBackendFactory.IsAnyInstalled();
    }

    /// <summary>
    /// Load only the config (no model sessions). Safe to call before showing a UI dialog.
    /// Throws if config.json is missing.
    /// </summary>
    public static GameConfig LoadConfig(string? modelPath = null) {
        string location = modelPath ?? Path.Combine(PathManager.Inst.DependencyPath, PackageId);
        string configPath = Path.Combine(location, "config.json");
        var jsonText = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
        return JsonSerializer.Deserialize<GameConfig>(jsonText)
               ?? throw new InvalidOperationException("Failed to parse GAME config.json");
    }

    /// <summary>
    /// Create a GAME instance using the user's preferred backend.
    /// </summary>
    public Game() : this(null) { }

    /// <summary>Create a GAME instance with an explicit ONNX model directory override.</summary>
    public Game(string? location) {
        if (!string.IsNullOrEmpty(location) && GameOnnxBackend.IsInstalled(location)) {
            config = LoadConfig(location);
            backend = new GameOnnxBackend(config, location);
        } else {
            backend = GameBackendFactory.Create();
            config = backend.Config;
        }
        Log.Information("GAME: active backend = {Backend}", backend.Name);
    }

    protected override bool SupportsBatch =>
        backend is GameOnnxBackend;  // only ONNX has native batching today

    protected override List<List<TranscribedNote>> TranscribeWaveformBatch(List<float[]> batch, GameOptions options) {
        if (stopping) throw new OperationCanceledException();
        return backend.RunInferenceBatch(batch, options);
    }

    protected override List<TranscribedNote> TranscribeWaveform(float[] samples, GameOptions options) {
        if (stopping) throw new OperationCanceledException();
        return backend.RunInference(samples, options);
    }

    public override void Interrupt() {
        stopping = true;
        backend.Interrupt();
    }

    protected override void DisposeManaged() {
        if (disposed) return;
        disposed = true;
        backend.Dispose();
    }
}
