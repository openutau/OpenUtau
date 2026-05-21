using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Analysis;

public abstract class MidiExtractor<TOptions> : IDisposable where TOptions : new() {
    public struct BatchingStrategy {
        public int max_batch_size;
        public float max_batch_duration;
    }

    protected abstract int ExpectedSampleRate { get; }
    protected virtual bool SupportsBatch => false;

    protected abstract List<TranscribedNote> TranscribeWaveform(float[] samples, TOptions options);

    protected virtual List<List<TranscribedNote>> TranscribeWaveformBatch(List<float[]> batch, TOptions options) {
        return batch.Select(s => TranscribeWaveform(s, options)).ToList();
    }

    private float[] ToMono(float[] samples, int channels) {
        if (channels == 1) {
            return samples;
        }

        float[] monoSamples = new float[samples.Length / channels];
        for (int i = 0; i < monoSamples.Length; i++) {
            monoSamples[i] = samples[(i * channels)..((i + 1) * channels)].Average();
        }

        return monoSamples;
    }

    private float[] Resample(float[] samples, int fromRate, int toRate) {
        if (fromRate == toRate) {
            return samples;
        }

        var format = WaveFormat.CreateIeeeFloatWaveFormat(fromRate, 1);
        ISampleProvider provider = new RawSourceWaveStream(
            new System.IO.MemoryStream(System.Runtime.InteropServices.MemoryMarshal
                .AsBytes(samples.AsSpan()).ToArray()),
            format).ToSampleProvider();
        provider = new WdlResamplingSampleProvider(provider, toRate);
        var result = new List<float>();
        float[] buffer = new float[toRate];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0) {
            result.AddRange(buffer.Take(n));
        }

        return result.ToArray();
    }

    public const double LongChunkThresholdSeconds = 30.0;

    public UVoicePart? Transcribe(UProject project, UWavePart wavePart,
        TOptions? options, BatchingStrategy? batchingStrategy,
        Func<bool>? confirm, Action<int, int> progress) {
        try {
            options ??= new TOptions();
            var monoSamples = ToMono(wavePart.Samples, wavePart.channels);
            var resampledSamples = Resample(monoSamples, wavePart.sampleRate, AudioSlicer.SampleRate);
            var chunks = AudioSlicer.Slice(resampledSamples);
            if (AudioSlicer.SampleRate != ExpectedSampleRate) {
                chunks = chunks.Select(chunk => chunk with {
                    samples = Resample(chunk.samples, AudioSlicer.SampleRate, ExpectedSampleRate)
                }).ToList();
            }

            // Warn the user if any chunk is longer than the threshold (likely accompaniment audio)
            if (confirm != null) {
                var hasLongChunk = chunks.Any(chunk =>
                    (double)chunk.samples.Length / ExpectedSampleRate > LongChunkThresholdSeconds);
                if (hasLongChunk && !confirm()) {
                    return null;
                }
            }

            var part = new UVoicePart();
            part.position = wavePart.position;
            part.Duration = wavePart.Duration;
            var timeAxis = project.timeAxis;
            double partOffsetMs = timeAxis.TickPosToMsPos(wavePart.position);
            double currMs = partOffsetMs;

            // Total duration of all chunks in seconds
            int totalDurationS = (int)chunks.Sum(c => (double)c.samples.Length / ExpectedSampleRate);
            double processedDurationAccum = 0.0;

            // Map original chunk index -> transcribed notes
            var notesByChunkIndex = new List<TranscribedNote>[chunks.Count];

            if (SupportsBatch && batchingStrategy.HasValue) {
                var strategy = batchingStrategy.Value;

                // Sort chunk indices by sample length ascending
                var sortedIndices = Enumerable.Range(0, chunks.Count)
                    .OrderBy(i => chunks[i].samples.Length)
                    .ToList();

                // Form batches respecting max_batch_size and max_batch_duration
                var batches = new List<List<int>>();
                var currentBatch = new List<int>();
                float maxChunkDurationInBatch = 0f;

                foreach (var idx in sortedIndices) {
                    float chunkDuration = (float)chunks[idx].samples.Length / ExpectedSampleRate;
                    bool batchFull = strategy.max_batch_size > 0 && currentBatch.Count >= strategy.max_batch_size;
                    // Effective padded duration = (currentBatch.Count + 1) * newMax,
                    // because all items are padded to the longest (first) chunk in the batch.
                    // The guard `currentBatch.Count > 0` ensures a single chunk that already
                    // exceeds max_batch_duration is still allowed through on its own.
                    float newMax = Math.Max(maxChunkDurationInBatch, chunkDuration);
                    bool wouldExceedDuration = strategy.max_batch_duration > 0
                                               && (currentBatch.Count + 1) * newMax > strategy.max_batch_duration;

                    if (currentBatch.Count > 0 && (batchFull || wouldExceedDuration)) {
                        batches.Add(currentBatch);
                        currentBatch = new List<int>();
                        maxChunkDurationInBatch = 0f;
                    }

                    currentBatch.Add(idx);
                    maxChunkDurationInBatch = Math.Max(maxChunkDurationInBatch, chunkDuration);
                }

                if (currentBatch.Count > 0) {
                    batches.Add(currentBatch);
                }

                // Run each batch
                foreach (var batch in batches) {
                    var batchSamples = batch.Select(i => chunks[i].samples).ToList();
                    var batchNotes = TranscribeWaveformBatch(batchSamples, options);
                    for (int j = 0; j < batch.Count; j++) {
                        notesByChunkIndex[batch[j]] = batchNotes[j];
                    }

                    double batchDurationS = batch.Sum(i => (double)chunks[i].samples.Length / ExpectedSampleRate);
                    processedDurationAccum += batchDurationS;
                    progress.Invoke((int)processedDurationAccum, totalDurationS);
                }
            } else {
                for (int i = 0; i < chunks.Count; i++) {
                    notesByChunkIndex[i] = TranscribeWaveform(chunks[i].samples, options);
                    processedDurationAccum += (double)chunks[i].samples.Length / ExpectedSampleRate;
                    progress.Invoke((int)processedDurationAccum, totalDurationS);
                }
            }

            for (int ci = 0; ci < chunks.Count; ci++) {
                currMs = chunks[ci].offsetMs + partOffsetMs;
                foreach (var note in notesByChunkIndex[ci]) {
                    var noteDurMs = note.noteDuration * 1000;
                    if (note.noteVoiced) {
                        var posTick = timeAxis.MsPosToTickPos(currMs);
                        var durTick = timeAxis.MsPosToTickPos(currMs + noteDurMs) - posTick;
                        if (durTick > 0) {
                            var uNote = project.CreateNote(
                                (int)Math.Round(note.noteScore),
                                posTick - wavePart.position,
                                durTick
                            );
                            part.notes.Add(uNote);
                        }
                    }

                    currMs += noteDurMs;
                }
            }

            var endTick = timeAxis.MsPosToTickPos(currMs);
            if (endTick > part.End) {
                part.Duration = endTick - part.position;
            }

            return part;
        } catch (OperationCanceledException) {
            return null;
        }
    }

    public virtual void Interrupt() { }

    protected abstract void DisposeManaged();

    public void Dispose() {
        DisposeManaged();
    }
}
