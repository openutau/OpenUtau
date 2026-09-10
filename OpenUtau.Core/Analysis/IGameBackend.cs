using System;
using System.Collections.Generic;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// Pluggable inference backend for the GAME MIDI extractor.
/// <list type="bullet">
/// <item><see cref="GameOnnxBackend"/> wraps the ONNX Runtime four-model pipeline.</item>
/// <item><see cref="GameGgmlBackend"/> drives the native GAME-ggml CLI (<c>serve</c> mode).</item>
/// </list>
/// Implementations are responsible for lazily loading their model lifecycle and
/// for honoring <see cref="Interrupt"/>. Audio chunking, resampling, batching and
/// the final note→tick mapping are handled by <see cref="MidiExtractor{TOptions}"/>,
/// so backends only need to transcribe a single preprocessed waveform (or a batch).
/// </summary>
public interface IGameBackend : IDisposable {
    /// <summary>Short display name for diagnostics ("ONNX", "GGML").</summary>
    string Name { get; }

    /// <summary>Configuration loaded from this backend's own package.</summary>
    GameConfig Config { get; }

    /// <summary>
    /// Load the model if not already loaded. Called lazily before the first
    /// inference. Returns true if the weights/executable are present and ready.
    /// </summary>
    bool EnsureLoaded();

    /// <summary>Transcribe a single preprocessed (mono, expected-sample-rate) waveform.</summary>
    List<TranscribedNote> RunInference(float[] samples, GameOptions options);

    /// <summary>
    /// Transcribe a batch of waveforms. Default implementation falls back to
    /// per-chunk calls; backends with native batching override it.
    /// </summary>
    List<List<TranscribedNote>> RunInferenceBatch(List<float[]> batch, GameOptions options) {
        var results = new List<List<TranscribedNote>>(batch.Count);
        foreach (var samples in batch) {
            results.Add(RunInference(samples, options));
        }
        return results;
    }

    /// <summary>Request cancellation of any in-flight inference.</summary>
    void Interrupt();
}
