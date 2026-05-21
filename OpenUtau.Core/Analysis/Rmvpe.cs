using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Analysis;

public class RmvpeResult {
    public double TimeStepSeconds { get; init; } = 0.01;
    public float[] MidiPitch { get; init; } = Array.Empty<float>();

    const int MedianWindowRadius = 2;
    const double AdaptiveSpikeThresholdCents = 75.0;
    const double AdaptiveSpikeBlend = 0.7;
    const int MaxFilledGapSteps = 12;
    const double MaxEdgeTrimMs = 25.0;
    const double MaxEdgeTrimRatio = 0.15;

    struct PitchPoint {
        public int x;
        public int y;
    }

    struct NoteSegment {
        public double onsetMs;
        public double durationMs;
        public int midi;
        public bool rest;
    }

    static List<int> MedianFilter(IReadOnlyList<int> values) {
        var result = new List<int>(values.Count);
        for (var i = 0; i < values.Count; ++i) {
            var window = new List<int>();
            for (var j = Math.Max(0, i - MedianWindowRadius); j <= Math.Min(values.Count - 1, i + MedianWindowRadius); ++j) {
                window.Add(values[j]);
            }
            window.Sort();
            result.Add(window[window.Count / 2]);
        }
        return result;
    }

    static List<int> AdaptiveSmooth(IReadOnlyList<int> values) {
        if (values.Count <= 2) {
            return values.ToList();
        }
        var result = values.ToList();
        for (var i = 1; i < values.Count - 1; ++i) {
            var neighborAverage = (result[i - 1] + result[i + 1]) / 2.0;
            var delta = result[i] - neighborAverage;
            if (Math.Abs(delta) <= AdaptiveSpikeThresholdCents) {
                continue;
            }
            result[i] = (int)Math.Round(result[i] - delta * AdaptiveSpikeBlend);
        }
        return result;
    }

    static List<PitchPoint> FillShortGaps(List<PitchPoint> points) {
        if (points.Count == 0) {
            return points;
        }
        var expanded = new List<PitchPoint>();
        expanded.Add(points[0]);
        for (var i = 1; i < points.Count; ++i) {
            var prev = expanded[^1];
            var current = points[i];
            var gapSteps = Math.Max(0, (current.x - prev.x) / UCurve.interval - 1);
            if (gapSteps > 0 && gapSteps <= MaxFilledGapSteps) {
                for (var step = 1; step <= gapSteps; ++step) {
                    var ratio = step / (double)(gapSteps + 1);
                    expanded.Add(new PitchPoint {
                        x = prev.x + step * UCurve.interval,
                        y = (int)Math.Round(prev.y + (current.y - prev.y) * ratio),
                    });
                }
            }
            expanded.Add(current);
        }
        return expanded;
    }

    static void AppendSmoothedPoints(UCurve curve, List<PitchPoint> points) {
        if (points.Count == 0) {
            return;
        }
        var processedPoints = FillShortGaps(points);
        var ys = processedPoints.Select(point => point.y).ToList();
        var smoothedYs = AdaptiveSmooth(MedianFilter(ys));
        for (var i = 0; i < processedPoints.Count; ++i) {
            var point = processedPoints[i];
            if (curve.xs.Count > 0 && curve.xs[^1] == point.x) {
                curve.ys[^1] = smoothedYs[i];
            } else {
                curve.xs.Add(point.x);
                curve.ys.Add(smoothedYs[i]);
            }
        }
    }

    static List<NoteSegment> BuildSegments(UProject project, UVoicePart part) {
        return part.notes
            .OrderBy(note => note.position)
            .Select(note => {
                var onsetTick = part.position + note.position;
                var endTick = part.position + note.End;
                var onsetMs = project.timeAxis.TickPosToMsPos(onsetTick);
                var endMs = project.timeAxis.TickPosToMsPos(endTick);
                return new NoteSegment {
                    onsetMs = onsetMs,
                    durationMs = endMs - onsetMs,
                    midi = note.tone,
                    rest = false,
                };
            })
            .ToList();
    }

    public void ApplyToPart(UProject project, UVoicePart part, double offsetMs = 0) {
        ApplyToPart(project, part, BuildSegments(project, part), offsetMs);
    }

    void ApplyToPart(UProject project, UVoicePart part, IReadOnlyList<NoteSegment> notes, double offsetMs) {
        if (MidiPitch.Length == 0 || notes.Count == 0 || !project.expressions.TryGetValue(Format.Ustx.PITD, out var descriptor)) {
            Log.Information(
                "RMVPE apply skipped. pitch={PitchCount} notes={NoteCount} hasPITD={HasPitd}",
                MidiPitch.Length,
                notes.Count,
                project.expressions.ContainsKey(Format.Ustx.PITD));
            return;
        }
        var curve = new UCurve(descriptor);
        var frameMs = TimeStepSeconds * 1000.0;
        var partStartMs = project.timeAxis.TickPosToMsPos(part.position);
        var pendingPoints = new List<PitchPoint>();
        var pendingNoteIndex = -1;
        int noteIndex = 0;
        for (int i = 0; i < MidiPitch.Length; ++i) {
            var midiPitch = MidiPitch[i];
            var localTimeMs = i * frameMs;
            var absoluteTimeMs = partStartMs + localTimeMs + offsetMs;
            while (noteIndex + 1 < notes.Count && notes[noteIndex].onsetMs + notes[noteIndex].durationMs <= absoluteTimeMs) {
                noteIndex++;
            }
            if (noteIndex >= notes.Count) {
                break;
            }
            var note = notes[noteIndex];
            if (pendingPoints.Count > 0 && pendingNoteIndex != noteIndex) {
                AppendSmoothedPoints(curve, pendingPoints);
                pendingPoints.Clear();
                pendingNoteIndex = -1;
            }
            var isInNote = note.onsetMs <= absoluteTimeMs && absoluteTimeMs < note.onsetMs + note.durationMs;
            if (!isInNote || note.rest || float.IsNaN(midiPitch)) {
                continue;
            }
            var noteOffsetMs = absoluteTimeMs - note.onsetMs;
            var edgeTrimMs = Math.Min(MaxEdgeTrimMs, note.durationMs * MaxEdgeTrimRatio);
            if (note.durationMs > edgeTrimMs * 2 &&
                (noteOffsetMs < edgeTrimMs || note.durationMs - noteOffsetMs <= edgeTrimMs)) {
                continue;
            }
            var tick = project.timeAxis.MsPosToTickPos(absoluteTimeMs);
            var x = tick - part.position;
            var y = (int)Math.Round(Math.Clamp((midiPitch - note.midi) * 100.0, descriptor.min, descriptor.max));
            var snappedX = (int)Math.Round((double)x / UCurve.interval) * UCurve.interval;
            pendingNoteIndex = noteIndex;
            if (pendingPoints.Count > 0 && pendingPoints[^1].x == snappedX) {
                pendingPoints[^1] = new PitchPoint { x = snappedX, y = y };
            } else {
                pendingPoints.Add(new PitchPoint { x = snappedX, y = y });
            }
        }
        AppendSmoothedPoints(curve, pendingPoints);
        curve.Simplify();
        if (curve.xs.Count > 0) {
            var oldCurve = part.curves.FirstOrDefault(c => c.abbr == Format.Ustx.PITD);
            var oldXs = oldCurve?.xs.ToArray();
            var oldYs = oldCurve?.ys.ToArray();
            DocManager.Inst.ExecuteCmd(new MergedSetCurveCommand(
                project,
                part,
                Format.Ustx.PITD,
                oldXs,
                oldYs,
                curve.xs.ToArray(),
                curve.ys.ToArray()));
        }
        Log.Information("RMVPE applied pitch curve. points={PointCount}", curve.xs.Count);
    }
}

public class RmvpeTranscriber : IDisposable {
    const int SampleRate = 16000;
    const int HopLength = 160;
    const float Threshold = 0.03f;

    readonly InferenceSession session;
    readonly string waveformInputName;
    readonly string thresholdInputName;
    readonly string f0OutputName;
    readonly string uvOutputName;
    readonly string modelPath;
    RunOptions? runOptions;
    bool disposed;

    public RmvpeTranscriber() {
        modelPath = ResolveModelPath();
        if (!File.Exists(modelPath)) {
            throw new MessageCustomizableException(
                "RMVPE not found",
                "<translate:errors.failed.transcribe.rmvpe>",
                new FileNotFoundException(modelPath),
                false,
                new[] { modelPath });
        }
        Log.Information("RMVPE loading model from {ModelPath}", modelPath);
        session = Onnx.getInferenceSession(modelPath, OnnxRunnerChoice.CPU);
        waveformInputName = ResolveWaveformInputName(session);
        thresholdInputName = ResolveThresholdInputName(session);
        f0OutputName = ResolveF0OutputName(session);
        uvOutputName = ResolveUvOutputName(session);
        Log.Information(
            "RMVPE session ready. inputWaveform={WaveformInput} inputThreshold={ThresholdInput} outputF0={F0Output} outputUv={UvOutput}",
            waveformInputName,
            thresholdInputName,
            f0OutputName,
            uvOutputName);
        runOptions = new RunOptions();
    }

    public static bool IsInstalled() {
        return File.Exists(ResolveModelPath());
    }

    public static string GetModelPath() {
        return ResolveModelPath();
    }

    static string ResolveModelPath() {
        var candidates = new List<string> {
            Path.Combine(PathManager.Inst.DependencyPath, "rmvpe", "rmvpe.onnx"),
            Path.Combine(PathManager.Inst.DependencyPath, "RMVPE", "rmvpe.onnx"),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? Path.Combine(PathManager.Inst.DependencyPath, "rmvpe", "rmvpe.onnx");
    }

    public RmvpeResult? Infer(UWavePart wavePart, double startMs = 0, double endMs = 0) {
        int startSample = 0;
        int endSample = wavePart.Samples.Length / wavePart.channels;
        int totalSamples = endSample;
        if (startMs > 0 || endMs > 0) {
            if (startMs > 0) {
                startSample = (int)(startMs * wavePart.sampleRate / 1000);
            }
            if (endMs > 0) {
                endSample = (int)(endMs * wavePart.sampleRate / 1000);
            }
            startSample = Math.Clamp(startSample, 0, totalSamples);
            endSample = Math.Clamp(endSample, startSample, totalSamples);
        }
        if (endSample <= startSample) {
            return null;
        }
        var mono = ToMono(wavePart.Samples, startSample, endSample, wavePart.channels);
        var resampled = ResampleTo16k(mono, wavePart.sampleRate);
        var waveform = new DenseTensor<float>(new[] { 1, resampled.Length });
        for (int i = 0; i < resampled.Length; ++i) {
            waveform[0, i] = Math.Clamp(resampled[i], -1f, 1f);
        }
        var threshold = new DenseTensor<float>(new[] { Threshold }, Array.Empty<int>());
        try {
            using var outputs = session.Run(new[] {
                NamedOnnxValue.CreateFromTensor(waveformInputName, waveform),
                NamedOnnxValue.CreateFromTensor(thresholdInputName, threshold),
            }, session.OutputNames, runOptions);
            var f0Tensor = outputs.First(output => output.Name == f0OutputName).AsTensor<float>();
            var uvTensor = outputs.First(output => output.Name == uvOutputName).AsTensor<bool>();
            var f0 = f0Tensor.ToArray();
            var uv = uvTensor.ToArray();
            if (f0.Length != uv.Length) {
                throw new InvalidDataException($"Unexpected RMVPE output sizes: f0={f0.Length}, uv={uv.Length}");
            }
            var midiPitch = ConvertToInterpolatedMidiPitch(f0, uv);
            return new RmvpeResult {
                TimeStepSeconds = (double)HopLength / SampleRate,
                MidiPitch = midiPitch,
            };
        } catch (OnnxRuntimeException) {
            if (runOptions != null && runOptions.Terminate) {
                return null;
            }
            throw;
        }
    }

    static float[] ConvertToInterpolatedMidiPitch(float[] f0, bool[] uv) {
        var midi = new float[f0.Length];
        for (int i = 0; i < midi.Length; ++i) {
            var voiced = !uv[i] && f0[i] > 0;
            midi[i] = voiced
                ? (float)(69.0 + 12.0 * Math.Log2(f0[i] / 440.0))
                : float.NaN;
        }
        InterpolateMidiPitch(midi);
        return midi;
    }

    static void InterpolateMidiPitch(float[] midi) {
        int firstVoiced = -1;
        for (int i = 0; i < midi.Length; ++i) {
            if (!float.IsNaN(midi[i])) {
                firstVoiced = i;
                break;
            }
        }
        if (firstVoiced < 0) {
            return;
        }
        for (int i = 0; i < firstVoiced; ++i) {
            midi[i] = midi[firstVoiced];
        }
        int previousVoiced = firstVoiced;
        int index = firstVoiced + 1;
        while (index < midi.Length) {
            if (!float.IsNaN(midi[index])) {
                previousVoiced = index;
                ++index;
                continue;
            }
            int gapStart = index;
            while (index < midi.Length && float.IsNaN(midi[index])) {
                ++index;
            }
            if (index < midi.Length) {
                var left = midi[previousVoiced];
                var right = midi[index];
                var gapLength = index - previousVoiced;
                for (int i = 1; i < gapLength; ++i) {
                    var ratio = (float)i / gapLength;
                    midi[previousVoiced + i] = left + (right - left) * ratio;
                }
                previousVoiced = index;
            } else {
                for (int i = gapStart; i < midi.Length; ++i) {
                    midi[i] = midi[previousVoiced];
                }
            }
        }
    }

    static string ResolveWaveformInputName(InferenceSession session) {
        return session.InputNames.FirstOrDefault(name =>
                string.Equals(name, "waveform", StringComparison.OrdinalIgnoreCase))
            ?? session.InputNames.First();
    }

    static string ResolveThresholdInputName(InferenceSession session) {
        return session.InputNames.FirstOrDefault(name =>
                string.Equals(name, "threshold", StringComparison.OrdinalIgnoreCase))
            ?? session.InputNames.ElementAtOrDefault(1)
            ?? throw new InvalidDataException("RMVPE model must expose a threshold input.");
    }

    static string ResolveF0OutputName(InferenceSession session) {
        return session.OutputNames.FirstOrDefault(name =>
                string.Equals(name, "f0", StringComparison.OrdinalIgnoreCase))
            ?? session.OutputNames.First();
    }

    static string ResolveUvOutputName(InferenceSession session) {
        return session.OutputNames.FirstOrDefault(name =>
                string.Equals(name, "uv", StringComparison.OrdinalIgnoreCase))
            ?? session.OutputNames.ElementAtOrDefault(1)
            ?? throw new InvalidDataException("RMVPE model must expose a uv output.");
    }

    static float[] ToMono(float[] samples, int startSample, int endSample, int channels) {
        if (channels == 1 && startSample == 0 && endSample == samples.Length) {
            return samples;
        }
        var mono = new float[endSample - startSample];
        for (int i = 0; i < mono.Length; ++i) {
            float sum = 0;
            var offset = (startSample + i) * channels;
            for (int ch = 0; ch < channels; ++ch) {
                sum += samples[offset + ch];
            }
            mono[i] = sum / channels;
        }
        return mono;
    }

    static float[] ResampleTo16k(float[] samples, int sourceSampleRate) {
        if (sourceSampleRate == SampleRate) {
            return samples;
        }
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sourceSampleRate, 1);
        ISampleProvider provider = new RawSourceWaveStream(
            new MemoryStream(System.Runtime.InteropServices.MemoryMarshal
                .AsBytes(samples.AsSpan()).ToArray()),
            format).ToSampleProvider();
        provider = new WdlResamplingSampleProvider(provider, SampleRate);
        var result = new List<float>();
        var buffer = new float[SampleRate];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0) {
            for (int i = 0; i < n; ++i) {
                result.Add(buffer[i]);
            }
        }
        return result.ToArray();
    }

    void Dispose(bool disposing) {
        if (!disposed) {
            if (disposing) {
                session.Dispose();
                runOptions?.Dispose();
            }
            disposed = true;
        }
    }

    public void Interrupt() {
        if (!disposed && runOptions != null) {
            runOptions.Terminate = true;
        }
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
