using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
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
}

public class Game : MidiExtractor<GameOptions> {
    private const string PackageId = "game";
    public const string DownloadUrl = "https://github.com/openvpi/GAME/releases/tag/oudep";

    InferenceSession? encoderSession;
    InferenceSession? segmenterSession;
    InferenceSession? estimatorSession;
    InferenceSession? bd2durSession;
    RunOptions? runOptions;
    bool sessionsLoaded = false;
    bool disposed = false;
    volatile bool stopping = false;
    GameConfig config;
    string Location;

    protected override int ExpectedSampleRate => config.SampleRate;
    public float Timestep => config.Timestep;
    public IReadOnlyDictionary<string, int>? Languages => config.Languages;

    /// <summary>
    /// Check if GAME is installed (config.json is present) without loading models.
    /// </summary>
    public static bool IsInstalled(string? location = null) {
        location ??= PackageManager.Inst.GetInstalledPath(PackageId);
        return location != null && File.Exists(Path.Combine(location, "config.json"));
    }

    /// <summary>
    /// Load only the config (no ONNX sessions). Safe to call before showing a UI dialog.
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
    /// </summary>
    public Game() : this(null) { }

    /// <summary>
    /// Create GAME instance with specified model path and parameters.
    /// Sessions are loaded lazily on first Transcribe call.
    /// </summary>
    /// <param name="location">Path to model directory, or null for default (Dependencies/game)</param>
    public Game(string? location) {
        Location = location ?? PackageManager.Inst.GetInstalledPath(PackageId)!;
        Log.Information("GAME: Model location = {Location}", Location);
        config = LoadConfig(location);
    }

    /// <summary>
    /// Ensure ONNX sessions are loaded. Called lazily before inference.
    /// </summary>
    private void EnsureSessionsLoaded() {
        if (sessionsLoaded) return;
        if (stopping) {
            throw new OperationCanceledException();
        }
        runOptions = new RunOptions();
        if (stopping) {
            runOptions.Terminate = true;
            throw new OperationCanceledException();
        }
        encoderSession = CreateSession("encoder.onnx", OnnxRunnerChoice.CPUForCoreML);
        segmenterSession = CreateSession("segmenter.onnx", OnnxRunnerChoice.Default);
        estimatorSession = CreateSession("estimator.onnx", OnnxRunnerChoice.Default);
        bd2durSession = CreateSession("bd2dur.onnx", OnnxRunnerChoice.Default);
        sessionsLoaded = true;
        if (stopping) {
            runOptions.Terminate = true;
            throw new OperationCanceledException();
        }
    }

    protected override bool SupportsBatch => true;

    protected override List<List<TranscribedNote>> TranscribeWaveformBatch(List<float[]> batch, GameOptions options) {
        EnsureSessionsLoaded();
        return RunPipeline(batch, options);
    }

    protected override List<TranscribedNote> TranscribeWaveform(float[] samples, GameOptions options) {
        EnsureSessionsLoaded();
        return RunPipeline(new List<float[]> { samples }, options)[0];
    }

    private List<List<TranscribedNote>> RunPipeline(List<float[]> batch, GameOptions options) {
        int B = batch.Count;
        int maxLen = batch.Max(s => s.Length);

        var waveformData = new float[B * maxLen];
        var durationData = new float[B];
        for (int b = 0; b < B; b++) {
            var s = batch[b];
            s.CopyTo(waveformData, b * maxLen);
            durationData[b] = (float)s.Length / config.SampleRate;
        }

        var waveform = new DenseTensor<float>(waveformData, new[] { B, maxLen });
        var duration = new DenseTensor<float>(durationData, new[] { B });

        try {
            // 1. Encoder
            var (xSeg, xEst, maskT) = RunEncoder(waveform, duration);

            // 2. Segmentation (D3PM loop)
            int T = xSeg.Dimensions[1];
            Tensor<bool> knownBoundaries = new DenseTensor<bool>(new[] { B, T });
            Tensor<bool> boundaries = new DenseTensor<bool>(new[] { B, T });

            Tensor<long>? language = null;
            if (config.Languages != null) {
                int languageId = ResolveLanguageId(options.LanguageCode);
                language = new DenseTensor<long>(
                    Enumerable.Repeat((long)languageId, B).ToArray(), new[] { B });
            }

            var segThreshold = new DenseTensor<float>(new[] { options.BoundaryThreshold }, Array.Empty<int>());
            var radius = new DenseTensor<long>(new long[] { options.BoundaryRadius }, Array.Empty<int>());

            if (config.Loop) {
                float step = 1.0f / options.SamplingSteps;
                for (int i = 0; i < options.SamplingSteps; i++) {
                    var t = new DenseTensor<float>(
                        Enumerable.Repeat(i * step, B).ToArray(), new[] { B });
                    boundaries = RunSegmenter(xSeg, knownBoundaries, boundaries, t, maskT, language, segThreshold, radius);
                }
            } else {
                boundaries = RunSegmenter(xSeg, knownBoundaries, null, null, maskT, language, segThreshold, radius);
            }

            // 3. Boundaries to durations
            var (durations, maskN) = RunBd2Dur(boundaries, maskT);
            int N = maskN.Dimensions[1];

            // 4. Estimation
            var scoreThreshold = new DenseTensor<float>(new[] { options.ScoreThreshold }, Array.Empty<int>());
            var (presence, scores) = RunEstimator(xEst, boundaries, maskT, maskN, scoreThreshold);

            // 5. Split results per batch item
            var results = new List<List<TranscribedNote>>(B);
            for (int b = 0; b < B; b++) {
                var notes = new List<TranscribedNote>(N);
                for (int i = 0; i < N; i++) {
                    if (!maskN[b, i]) break;
                    notes.Add(new TranscribedNote(durations[b, i], scores[b, i], presence[b, i]));
                }

                results.Add(notes);
            }

            return results;
        } catch (OnnxRuntimeException) {
            if (runOptions != null && runOptions.Terminate) {
                throw new OperationCanceledException();
            }
            throw;
        }
    }

    public override void Interrupt() {
        stopping = true;
        if (!disposed && runOptions != null) {
            runOptions.Terminate = true;
        }
    }

    protected override void DisposeManaged() {
        if (disposed) return;
        disposed = true;
        runOptions?.Dispose();
        encoderSession?.Dispose();
        segmenterSession?.Dispose();
        estimatorSession?.Dispose();
        bd2durSession?.Dispose();
        sessionsLoaded = false;
    }

    // -------------------------------------------------------------------------
    // Implementation details: session creation and low-level ONNX runners
    // -------------------------------------------------------------------------

    /// <summary>
    /// Create an ONNX session for the given model file.
    /// </summary>
    private InferenceSession CreateSession(string modelFile, OnnxRunnerChoice runnerChoice) {
        string modelPath = Path.Combine(Location, modelFile);
        Log.Information("GAME: Loading model {ModelPath} (exists={Exists})",
            modelPath, File.Exists(modelPath));
        return Onnx.getInferenceSession(modelPath, runnerChoice);
    }

    /// <summary>
    /// Resolve a language code string to an integer ID using the config's language map.
    /// Returns 0 (universal) if the code is null or not found.
    /// </summary>
    private int ResolveLanguageId(string? languageCode) {
        if (languageCode != null && config.Languages != null &&
            config.Languages.TryGetValue(languageCode, out int id)) {
            return id;
        }

        return 0;
    }

    /// <summary>
    /// Run encoder: waveform -> x_seg, x_est, maskT
    /// </summary>
    private (Tensor<float> x_seg, Tensor<float> x_est, Tensor<bool> maskT)
        RunEncoder(Tensor<float> waveform, Tensor<float> duration) {
        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("waveform", waveform),
            NamedOnnxValue.CreateFromTensor("duration", duration),
        };

        using var outputs = encoderSession!.Run(inputs, encoderSession.OutputNames, runOptions);

        var xSeg = outputs.First(o => o.Name == "x_seg").AsTensor<float>().ToDenseTensor();
        var xEst = outputs.First(o => o.Name == "x_est").AsTensor<float>().ToDenseTensor();
        var maskT = outputs.First(o => o.Name == "maskT").AsTensor<bool>().ToDenseTensor();

        return (xSeg, xEst, maskT);
    }

    /// <summary>
    /// Run a single segmenter step (D3PM sampling iteration)
    /// </summary>
    private Tensor<bool> RunSegmenter(
        Tensor<float> xSeg,
        Tensor<bool> knownBoundaries, Tensor<bool>? prevBoundaries,
        Tensor<float>? t, Tensor<bool> maskT,
        Tensor<long>? language,
        Tensor<float> threshold, Tensor<long> radius) {
        var inputs = new List<NamedOnnxValue>();
        inputs.Add(NamedOnnxValue.CreateFromTensor("x_seg", xSeg));

        if (language != null) {
            inputs.Add(NamedOnnxValue.CreateFromTensor("language", language));
        }

        inputs.Add(NamedOnnxValue.CreateFromTensor("known_boundaries", knownBoundaries));

        if (prevBoundaries != null) {
            inputs.Add(NamedOnnxValue.CreateFromTensor("prev_boundaries", prevBoundaries));
        }

        if (t != null) {
            inputs.Add(NamedOnnxValue.CreateFromTensor("t", t));
        }

        inputs.Add(NamedOnnxValue.CreateFromTensor("maskT", maskT));
        inputs.Add(NamedOnnxValue.CreateFromTensor("threshold", threshold));
        inputs.Add(NamedOnnxValue.CreateFromTensor("radius", radius));

        using var outputs = segmenterSession!.Run(inputs, segmenterSession.OutputNames, runOptions);
        var boundaries = outputs.First(o => o.Name == "boundaries").AsTensor<bool>().ToDenseTensor();
        return boundaries;
    }

    /// <summary>
    /// Run bd2dur: boundaries -> durations (seconds) + maskN
    /// </summary>
    private (Tensor<float> durations, Tensor<bool> maskN)
        RunBd2Dur(Tensor<bool> boundaries, Tensor<bool> maskT) {
        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("boundaries", boundaries),
            NamedOnnxValue.CreateFromTensor("maskT", maskT),
        };

        using var outputs = bd2durSession!.Run(inputs, bd2durSession.OutputNames, runOptions);
        var durations = outputs.First(o => o.Name == "durations").AsTensor<float>().ToDenseTensor();
        var maskN = outputs.First(o => o.Name == "maskN").AsTensor<bool>().ToDenseTensor();

        return (durations, maskN);
    }

    /// <summary>
    /// Run estimator: predict note presence and pitch scores
    /// </summary>
    private (Tensor<bool> presence, Tensor<float> scores)
        RunEstimator(Tensor<float> xEst, Tensor<bool> boundaries, Tensor<bool> maskT,
            Tensor<bool> maskN, Tensor<float> threshold) {
        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("x_est", xEst),
            NamedOnnxValue.CreateFromTensor("boundaries", boundaries),
            NamedOnnxValue.CreateFromTensor("maskT", maskT),
            NamedOnnxValue.CreateFromTensor("maskN", maskN),
            NamedOnnxValue.CreateFromTensor("threshold", threshold),
        };

        using var outputs = estimatorSession!.Run(inputs, estimatorSession.OutputNames, runOptions);
        var presence = outputs.First(o => o.Name == "presence").AsTensor<bool>().ToDenseTensor();
        var scores = outputs.First(o => o.Name == "scores").AsTensor<float>().ToDenseTensor();
        return (presence, scores);
    }
}
