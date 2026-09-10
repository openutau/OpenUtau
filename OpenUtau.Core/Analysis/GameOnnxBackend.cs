using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Serilog;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// ONNX Runtime backend for GAME. Runs the four-model pipeline
/// (encoder → segmenter (D3PM loop) → bd2dur → estimator) directly in-process.
/// This is a behavior-preserving extraction of the original single-backend
/// <see cref="Game"/> implementation; ONNX is always available when the
/// <c>game</c> dependency package with the <c>.onnx</c> files is installed.
/// </summary>
public class GameOnnxBackend : IGameBackend {
    private const string PackageId = "game";

    InferenceSession? encoderSession;
    InferenceSession? segmenterSession;
    InferenceSession? estimatorSession;
    InferenceSession? bd2durSession;
    RunOptions? runOptions;
    bool sessionsLoaded = false;
    bool disposed = false;
    volatile bool stopping = false;

    readonly GameConfig config;
    readonly string Location;

    public string Name => "ONNX";
    public GameConfig Config => config;

    public GameOnnxBackend(GameConfig config, string location) {
        this.config = config;
        this.Location = location;
    }

    /// <summary>Check if the ONNX GAME weights are installed without loading models.</summary>
    public static bool IsInstalled(string? location = null) {
        location ??= PackageManager.Inst.GetInstalledPath(PackageId);
        if (location == null) return false;
        if (!System.IO.File.Exists(System.IO.Path.Combine(location, "config.json"))) return false;
        // All four ONNX models must be present.
        return new[] { "encoder.onnx", "segmenter.onnx", "estimator.onnx", "bd2dur.onnx" }
            .All(f => System.IO.File.Exists(System.IO.Path.Combine(location, f)));
    }

    public bool EnsureLoaded() {
        if (sessionsLoaded) return true;
        runOptions = new RunOptions();
        encoderSession = CreateSession("encoder.onnx", OnnxRunnerChoice.CPUForCoreML);
        segmenterSession = CreateSession("segmenter.onnx", OnnxRunnerChoice.Default);
        estimatorSession = CreateSession("estimator.onnx", OnnxRunnerChoice.Default);
        bd2durSession = CreateSession("bd2dur.onnx", OnnxRunnerChoice.Default);
        sessionsLoaded = true;
        if (stopping) {
            runOptions.Terminate = true;
            throw new OperationCanceledException();
        }
        return true;
    }

    public List<TranscribedNote> RunInference(float[] samples, GameOptions options) {
        EnsureLoaded();
        return RunPipeline(new List<float[]> { samples }, options)[0];
    }

    public List<List<TranscribedNote>> RunInferenceBatch(List<float[]> batch, GameOptions options) {
        EnsureLoaded();
        return RunPipeline(batch, options);
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

    public void Interrupt() {
        stopping = true;
        if (!disposed && runOptions != null) {
            runOptions.Terminate = true;
        }
    }

    public void Dispose() {
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

    private InferenceSession CreateSession(string modelFile, OnnxRunnerChoice runnerChoice) {
        string modelPath = System.IO.Path.Combine(Location, modelFile);
        Log.Information("GAME(ONNX): Loading model {ModelPath} (exists={Exists})",
            modelPath, System.IO.File.Exists(modelPath));
        return Onnx.getInferenceSession(modelPath, runnerChoice);
    }

    private int ResolveLanguageId(string? languageCode) {
        if (languageCode != null && config.Languages != null &&
            config.Languages.TryGetValue(languageCode, out int id)) {
            return id;
        }

        return 0;
    }

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
