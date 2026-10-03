using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Analysis;

public class TifaOptions {
    public string Backend { get; set; } = "auto";
    public double SkipPenalty { get; set; } = 0.5;
    /// <summary>Longest audio slice handed to one alignment pass. tifa.cpp
    /// caps the CLI at 60 s and its author recommends staying below that.</summary>
    public double MaxChunkSeconds { get; set; } = 40.0;
    /// <summary>Position phonemes by their audible onset (classic singers) or
    /// by the raw phoneme position (model singers).</summary>
    public bool AudibleOnset { get; set; }
}

public class TifaAlignOutput {
    public List<TifaInterval> Spans = new();
    public double Agreement;
    public double Confidence;
}

public class TifaExtractionResult {
    public bool Success;
    public bool Cancelled;
    public string? Error;
    public int TotalNotes;
    public int ResolvedNotes;
    public int PhonemeCount;
    public int MovedPhonemes;
    public int ClampedPhonemes;
    /// <summary>Notes whose aligned window was too short to trust.</summary>
    public int UncertainNotes;
    /// <summary>Number of alignment passes the part was split into.</summary>
    public int ChunkCount;
    public double Agreement;
    public double Confidence;
    public List<string> Unresolved = new();
    public List<TifaPhonemeMove> Moves = new();
}

/// <summary>
/// Driver for the tifa.cpp CLI (TIFA, a token-imputing forced aligner).
///
/// Given the notes and lyrics already in the project plus a vocal recording,
/// the CLI returns a phone-level segmentation of the recording. The CLI has no
/// server mode, so one process is spawned per run; the model is loaded once
/// and the whole (possibly multi-minute) part is aligned in a single pass.
///
/// The dependency ships as an OpenUtau <c>.oudep</c> package containing the
/// CLI, its runtime libraries and the quantized model under <c>models/</c>.
/// </summary>
public class Tifa {
    public const string PackageId = "tifa-ggml";

    /// <summary>Languages the packaged model supports.</summary>
    public static readonly TifaLanguage[] SupportedLanguages = {
        TifaLanguage.Zh, TifaLanguage.Ja, TifaLanguage.Yue, TifaLanguage.En,
    };

    const string CliName = "tifa_ggml_cli";

    static string CliFileName => OS.IsWindows() ? CliName + ".exe" : CliName;

    /// <summary>Directory of the installed package, or null.</summary>
    public static string? ResolveInstallDir() {
        string dependencyPath = PathManager.Inst.DependencyPath;
        if (!Directory.Exists(dependencyPath)) {
            return null;
        }
        var preferred = Path.Combine(dependencyPath, PackageId);
        if (File.Exists(Path.Combine(preferred, CliFileName))) {
            return preferred;
        }
        // The package id is not guaranteed; accept any installed folder that
        // carries the CLI so a hand-packed .oudep still works.
        foreach (var dir in Directory.EnumerateDirectories(dependencyPath)) {
            if (File.Exists(Path.Combine(dir, CliFileName))) {
                return dir;
            }
        }
        return null;
    }

    public static string? ResolveModelPath() {
        var dir = ResolveInstallDir();
        if (dir == null) {
            return null;
        }
        var candidates = Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories).ToList();
        return candidates
            .OrderByDescending(f => Path.GetFileName(f).Contains("tifa", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    public static bool IsInstalled() {
        return ResolveInstallDir() != null && ResolveModelPath() != null;
    }

    /// <summary>The three packages differ only by platform; a package built
    /// for another OS fails in a confusing way at process start, so the
    /// manifest's platform tag is checked first.</summary>
    public static string CurrentPlatformTag() {
        string os = OS.IsWindows() ? "windows" : OS.IsMacOS() ? "macos" : "linux";
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            var other => other.ToString().ToLowerInvariant(),
        };
        return $"{os}-{arch}";
    }

    static void VerifyPlatform(string installDir) {
        string configPath = Path.Combine(installDir, "config.json");
        if (!File.Exists(configPath)) {
            return;
        }
        try {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("platform", out var platform)) {
                return;
            }
            string? expected = platform.GetString();
            string current = CurrentPlatformTag();
            if (!string.IsNullOrEmpty(expected)
                && !string.Equals(expected, current, StringComparison.OrdinalIgnoreCase)
                // macOS x64 can run arm64 builds through Rosetta; the reverse
                // is not true.
                && !(expected == "macos-arm64" && current == "macos-x64")) {
                throw new InvalidOperationException(
                    $"The installed TIFA package is built for {expected}, but this machine is {current}. " +
                    "Uninstall it and install the matching package.");
            }
        } catch (InvalidOperationException) {
            throw;
        } catch (Exception e) {
            Log.Warning(e, "Failed to read TIFA package config at {Path}", configPath);
        }
    }

    /// <summary>
    /// Align one chunk's phone list against its audio slice. The returned
    /// spans are parallel to <paramref name="phones"/> and relative to the
    /// start of <paramref name="audio"/>.
    /// </summary>
    public TifaAlignOutput Align(List<string> phones, float[] audio, int channels, int sampleRate,
        TifaLanguage language, TifaOptions options, CancellationToken token) {
        if (phones.Count == 0) {
            throw new InvalidOperationException("No phonemes to align.");
        }
        string? installDir = ResolveInstallDir();
        string? modelPath = ResolveModelPath();
        if (installDir == null || modelPath == null) {
            throw new FileNotFoundException(
                "The TIFA aligner package is not installed. Install the tifa-ggml .oudep package to use this feature.");
        }
        VerifyPlatform(installDir);
        string cliPath = Path.Combine(installDir, CliFileName);
        EnsureExecutable(cliPath);

        string workDir = Path.Combine(Path.GetTempPath(), "openutau-tifa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try {
            string wavPath = Path.Combine(workDir, "input.wav");
            WriteWav16(wavPath, audio, channels, sampleRate);
            string phonesPath = Path.Combine(workDir, "phones.txt");
            string languageCode = TifaPhonemeData.Code(language);
            File.WriteAllText(phonesPath,
                string.Join(" ", phones), new UTF8Encoding(false));

            var psi = new ProcessStartInfo {
                FileName = cliPath,
                WorkingDirectory = installDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in new[] {
                "align", wavPath,
                "-m", modelPath,
                "--phones-file", phonesPath,
                "-l", languageCode,
                "-o", workDir,
                "--output-formats", "textgrid,json",
                "--skip-handling", "preserve",
                "--skip-penalty", options.SkipPenalty.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                "--backend", options.Backend,
                "--max-frames", "0",
            }) {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var stdoutDone = new ManualResetEventSlim(false);
            using var stderrDone = new ManualResetEventSlim(false);
            process.OutputDataReceived += (_, e) => {
                if (e.Data == null) {
                    stdoutDone.Set();
                } else {
                    stdout.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) => {
                if (e.Data == null) {
                    stderrDone.Set();
                } else {
                    stderr.AppendLine(e.Data);
                    Log.Debug("TIFA[stderr] {Line}", e.Data);
                }
            };
            if (!process.Start()) {
                throw new InvalidOperationException("Failed to start tifa_ggml_cli.");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var registration = token.Register(() => {
                try {
                    if (!process.HasExited) {
                        process.Kill(entireProcessTree: true);
                    }
                } catch {
                    // Cancellation must never throw.
                }
            });
            process.WaitForExit();
            stdoutDone.Wait(1000);
            stderrDone.Wait(1000);
            if (token.IsCancellationRequested) {
                throw new OperationCanceledException();
            }
            if (process.ExitCode != 0) {
                string detail = stderr.ToString().Trim();
                if (detail.Length > 800) {
                    detail = detail.Substring(detail.Length - 800);
                }
                throw new InvalidOperationException(
                    $"tifa_ggml_cli exited with code {process.ExitCode}. {detail}");
            }

            string textGridPath = Path.Combine(workDir, "input.TextGrid");
            if (!File.Exists(textGridPath)) {
                string detail = stderr.ToString().Trim();
                throw new InvalidOperationException(
                    $"tifa_ggml_cli produced no TextGrid. {detail}");
            }
            var output = new TifaAlignOutput {
                Spans = TifaTextGrid.ParseTier(textGridPath, "phones"),
            };
            ReadDiagnosis(Path.Combine(workDir, "input.diagnosis.json"), output);
            return output;
        } finally {
            try {
                Directory.Delete(workDir, recursive: true);
            } catch (Exception e) {
                Log.Warning(e, "Failed to clean up TIFA work directory {Dir}", workDir);
            }
        }
    }

    /// <summary>
    /// Aggregate alignment quality reported by the CLI. <c>agreement</c> is the
    /// fraction of frames that support the decoded segmentation; a low value
    /// means the phone list does not match the recording (wrong lyrics,
    /// different take, or audio the notes do not cover).
    /// </summary>
    static void ReadDiagnosis(string path, TifaAlignOutput output) {
        try {
            if (!File.Exists(path)) {
                return;
            }
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("agreement", out var agreement)) {
                output.Agreement = agreement.GetDouble();
            }
            if (document.RootElement.TryGetProperty("confidence", out var confidence)) {
                output.Confidence = confidence.GetDouble();
            }
        } catch (Exception e) {
            Log.Warning(e, "Failed to read TIFA diagnosis from {Path}", path);
        }
    }

    /// <summary>
    /// Audio kept on both sides of a chunk so a phone sitting on the boundary
    /// is not cut off.
    /// </summary>
    const double ChunkMarginMs = 500.0;

    /// <summary>Notes of one alignment pass.</summary>
    class TifaChunk {
        public int FirstPhone;
        public int PhoneCount;
        public List<string> Phones = new();
        public double StartMs;
        public double EndMs;
    }

    /// <summary>
    /// Full pipeline for the UI: snapshot the part, build the phone sequence,
    /// split it into aligner-sized chunks, run the CLI on each, and turn the
    /// returned spans into phoneme moves.
    /// </summary>
    public static TifaExtractionResult Extract(
        UProject project, UWavePart wavePart, UVoicePart voicePart,
        TifaLanguage language, TifaOptions options,
        Action<int, int>? progress, CancellationToken token) {
        var result = new TifaExtractionResult();
        var notes = TifaPhonemeAligner.Snapshot(voicePart, project, options.AudibleOnset);
        result.TotalNotes = notes.Count;
        if (notes.Count == 0) {
            result.Error = "The part has no phonemes. Fill in the lyrics and let the track render once.";
            return result;
        }
        var sequence = TifaPhonemeAligner.BuildSequence(notes, language);
        result.Unresolved = sequence.Unresolved;
        result.ResolvedNotes = sequence.Notes.Count;
        result.PhonemeCount = sequence.PhonemeCount;
        if (sequence.Phones.Count == 0) {
            result.Error = "No phoneme could be mapped to the aligner's phonetic inventory. "
                + "Check the track's language and phonemizer.";
            return result;
        }

        var (pcm, channels, sampleRate, audioOriginMs) = TrimmedRecording(project, wavePart);
        if (pcm.Length == 0) {
            result.Error = "The recording has no audio under this part.";
            return result;
        }
        var chunks = BuildChunks(sequence, options.MaxChunkSeconds);
        result.ChunkCount = chunks.Count;

        try {
            var tifa = new Tifa();
            var spans = new List<TifaInterval>(sequence.Phones.Count);
            double agreement = 0;
            double confidence = 0;
            for (int i = 0; i < chunks.Count; ++i) {
                token.ThrowIfCancellationRequested();
                var chunk = chunks[i];
                progress?.Invoke(i + 1, chunks.Count);
                double fromMs = chunk.StartMs - ChunkMarginMs;
                double toMs = chunk.EndMs + ChunkMarginMs;
                var (slice, sliceOriginMs) = Slice(
                    pcm, channels, sampleRate, audioOriginMs, fromMs, toMs);
                if (slice.Length == 0) {
                    // The recording does not cover this chunk (for example the
                    // wave part starts after the first notes). Zero-width
                    // placeholders leave the chunk's notes untouched while
                    // keeping the span list parallel to the phone list.
                    AddPlaceholders(spans, chunk.PhoneCount);
                    continue;
                }
                var output = tifa.Align(chunk.Phones, slice, channels, sampleRate,
                    language, options, token);
                agreement = Math.Max(agreement, output.Agreement);
                confidence = Math.Max(confidence, output.Confidence);
                if (output.Spans.Count != chunk.PhoneCount) {
                    // The CLI is expected to return one span per phone. If it
                    // does not, skip this chunk rather than mis-aligning every
                    // later phone.
                    Log.Warning("TIFA returned {Spans} spans for {Phones} phones in one chunk; skipping it.",
                        output.Spans.Count, chunk.PhoneCount);
                    AddPlaceholders(spans, chunk.PhoneCount);
                    continue;
                }
                // Rebase every span onto the project timeline; the move
                // computation then works with a zero origin.
                foreach (var span in output.Spans) {
                    spans.Add(new TifaInterval {
                        Start = (sliceOriginMs + span.Start * 1000.0) / 1000.0,
                        End = (sliceOriginMs + span.End * 1000.0) / 1000.0,
                        Text = span.Text,
                    });
                }
            }
            result.Agreement = agreement;
            result.Confidence = confidence;
            result.Moves = TifaPhonemeAligner.ComputeMoves(
                sequence, spans, 0, project, voicePart,
                out int clamped, out int uncertain);
            result.ClampedPhonemes = clamped;
            result.UncertainNotes = uncertain;
            result.MovedPhonemes = result.Moves.Count;
            result.Success = true;
            return result;
        } catch (OperationCanceledException) {
            result.Cancelled = true;
            return result;
        }
    }

    /// <summary>
    /// Split the phone sequence into chunks of at most
    /// <paramref name="maxChunkSeconds"/> seconds. Chunk borders fall where
    /// the singing pauses, because a note is never split.
    /// </summary>
    static List<TifaChunk> BuildChunks(TifaSequence sequence, double maxChunkSeconds) {
        var chunks = new List<TifaChunk>();
        int firstNote = 0;
        double startMs = sequence.Notes[0].Phonemes[0].StartMs;
        double endMs = sequence.Notes[0].Phonemes[^1].EndMs;
        for (int noteIndex = 1; noteIndex <= sequence.Notes.Count; ++noteIndex) {
            if (noteIndex < sequence.Notes.Count) {
                var note = sequence.Notes[noteIndex];
                double noteStart = note.Phonemes[0].StartMs;
                double noteEnd = note.Phonemes[^1].EndMs;
                if (noteEnd - startMs > maxChunkSeconds * 1000.0) {
                    chunks.Add(MakeChunk(sequence, firstNote, noteIndex, startMs, endMs));
                    firstNote = noteIndex;
                    startMs = noteStart;
                    endMs = noteEnd;
                    continue;
                }
                endMs = Math.Max(endMs, noteEnd);
                continue;
            }
            chunks.Add(MakeChunk(sequence, firstNote, sequence.Notes.Count, startMs, endMs));
        }
        return chunks;
    }

    static TifaChunk MakeChunk(TifaSequence sequence, int firstNote, int endNote,
        double startMs, double endMs) {
        var chunk = new TifaChunk { StartMs = startMs, EndMs = endMs };
        for (int i = 0; i < sequence.Phones.Count; ++i) {
            int noteIndex = sequence.PhoneNote[i];
            if (noteIndex < firstNote || noteIndex >= endNote) {
                continue;
            }
            if (chunk.PhoneCount == 0) {
                chunk.FirstPhone = i;
            }
            ++chunk.PhoneCount;
            chunk.Phones.Add(sequence.Phones[i]);
        }
        return chunk;
    }

    /// <summary>Zero-width spans: the move computation skips them, so the
    /// phones they stand for keep their current timing.</summary>
    static void AddPlaceholders(List<TifaInterval> spans, int count) {
        for (int i = 0; i < count; ++i) {
            spans.Add(default);
        }
    }

    /// <summary>Trimmed recording of the wave part (skip/trim/fades applied).</summary>
    static (float[] pcm, int channels, int sampleRate, double originMs) TrimmedRecording(
        UProject project, UWavePart wavePart) {
        var (offsetMs, _, channels, pcm) = wavePart.GetTrimmedSamples(project);
        if (channels <= 0) {
            channels = 1;
        }
        return (pcm, channels, wavePart.sampleRate, offsetMs);
    }

    static (float[] pcm, double originMs) Slice(float[] pcm, int channels, int sampleRate,
        double audioOriginMs, double fromMs, double toMs) {
        int startSample = (int)Math.Round((fromMs - audioOriginMs) / 1000.0 * sampleRate) * channels;
        int endSample = (int)Math.Round((toMs - audioOriginMs) / 1000.0 * sampleRate) * channels;
        startSample = Math.Clamp(startSample, 0, pcm.Length);
        endSample = Math.Clamp(endSample, startSample, pcm.Length);
        double originMs = audioOriginMs + (double)startSample / channels / sampleRate * 1000.0;
        return (pcm[startSample..endSample], originMs);
    }

    /// <summary>16-bit PCM WAV; the CLI reads wav/flac/mp3 through dr_libs.</summary>
    static void WriteWav16(string path, float[] samples, int channels, int sampleRate) {
        if (channels <= 0) {
            channels = 1;
        }
        int frames = samples.Length / channels;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        int dataBytes = frames * channels * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        for (int i = 0; i < frames * channels; ++i) {
            float value = samples[i];
            value = Math.Clamp(value, -1f, 1f);
            writer.Write((short)Math.Round(value * 32767f));
        }
    }

    static void EnsureExecutable(string path) {
        if (OS.IsWindows()) {
            return;
        }
        try {
            const UnixFileMode mode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        } catch (Exception e) {
            Log.Warning(e, "Failed to set executable permission on {Cli}", path);
        }
    }
}
