using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Serilog;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// GAME inference backend that drives the native GAME-ggml CLI in long-lived
/// <c>serve</c> mode. Communication is a binary stdin → JSON stdout protocol:
///
/// On <see cref="EnsureLoaded"/>:
///   1. Resolve the CLI binary and GGUF weights under the OpenUtau Dependencies folder.
///   2. Spawn <c>game_ggml_cli serve <gguf></c> with redirected stdin/stdout/stderr.
///   3. Wait for the <c>{"type":"ready"}</c> line on stdout.
///
/// Per inference (<see cref="RunInference"/>):
///   • Write a 36-byte request header + the float32 waveform to stdin.
///   • Read one JSON object back describing the transcribed notes for this chunk.
///
/// Cancelation (<see cref="Interrupt"/>) writes the quit magic and reaps the process.
/// One <see cref="GameGgmlBackend"/> instance owns exactly one subprocess; the
/// subclass <see cref="Game"/> disposes it after the whole transcription.
/// </summary>
///
/// Recommended GGML configuration (7-channel 60 s benchmark, nsteps=8,
/// frame-level metrics vs torch-CUDA fp32 no-cache baseline — all EPS ≥ 0.97 RPA):
///
///   | platform      | GPU           | weights | EP offered by oudep pkg |
///   |---------------|---------------|---------|--------------------------|
///   | Windows       | NVIDIA        | F32     | CUDA (fallback Vulkan)   |
///   | Windows       | Intel/AMD/etc | F32     | Vulkan                   |
///   | Windows       | integrated    | F32/Q8  | CPU (+ DBCache): 0.44x wall |
///   | Linux         | NVIDIA        | F32     | CUDA (fallback Vulkan)   |
///   | Linux         | Nouveau/AMD   | F32     | Vulkan                   |
///   | macOS         | Apple Silicon | F32     | Metal (only EP)          |
///   | macOS         | Intel         | F32     | Metal (cross-compiled)   |
///
/// Rules of thumb (no user config required — CLI picks backend from GGUF/EP):
///  * GPU present → F32 weights; CPU-only → CPU EP with DBCache on by default
///  * on GPU, Vulkan is our smallest-VRAM (≈+0.3 GiB) and CUDA the fastest
///  * Q8 saves VRAM/RAM (~3.4x smaller weights) at near-lossless quality but may
///    flip a boundary note on Vulkan — prefer the -full package if you need
///    bit-consistent output; both are equally valid in practice.
public class GameGgmlBackend : IGameBackend {
    private const string PackageId = "game";
    // Single oudep package contains both the CLI binary and the GGUF weights.
    private const string GgmlPackageId = "game-ggml-medium";

    // Must match serv_proto::MAGIC_INFERENCE / MAGIC_QUIT in src/cli/main.cpp.
    private const uint MAGIC_INFERENCE = 0x53455256u;  // "VRES"
    private const uint MAGIC_QUIT = 0x54495155u;        // "UQIT"

    const int RequestHeaderBytes = 36;

    readonly GameConfig config;
    readonly string cliPath;
    readonly string ggufPath;

    Process? process;
    BinaryWriter? stdinWriter;
    StreamReader? stdoutReader;
    volatile bool disposed = false;
    volatile bool stopping = false;

    public string Name => "GGML";
    public GameConfig Config => config;

    private GameGgmlBackend(GameConfig config, string cliPath, string ggufPath) {
        this.config = config;
        this.cliPath = cliPath;
        this.ggufPath = ggufPath;
    }

    /// <summary>Locate the shipped CLI binary, platform-aware.</summary>
    public static string? ResolveCliPath() {
        string dep = PathManager.Inst.DependencyPath;
        string exeName = OS.IsWindows() ? "game_ggml_cli.exe" : "game_ggml_cli";
        // The game-ggml-medium oudep package ships the CLI under its root.
        string cliDir = Path.Combine(dep, GgmlPackageId);
        string exe = Path.Combine(cliDir, exeName);
        if (File.Exists(exe)) return exe;
        return null;
    }

    /// <summary>Locate the largest .gguf weight file. An explicit location is
    /// honored first; otherwise checks the dedicated game-ggml-medium package,
    /// then the shared game package for backward compatibility.</summary>
    public static string? ResolveGgufPath(string? location = null) {
        if (location != null) {
            return FindLargestGguf(location);
        }
        string ggmlDir = Path.Combine(PathManager.Inst.DependencyPath, GgmlPackageId);
        string? gguf = FindLargestGguf(ggmlDir);
        if (gguf != null) return gguf;

        string? gameLoc = PackageManager.Inst.GetInstalledPath(PackageId);
        return gameLoc == null ? null : FindLargestGguf(gameLoc);
    }

    private static string? FindLargestGguf(string directory) {
        if (!Directory.Exists(directory)) return null;
        return Directory.GetFiles(directory, "*.gguf")
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    /// <summary>True when the CLI, GGUF weights and backend config are installed.</summary>
    public static bool IsInstalled(string? location = null) {
        string? gguf = ResolveGgufPath(location);
        return ResolveCliPath() != null &&
            gguf != null &&
            File.Exists(Path.Combine(Path.GetDirectoryName(gguf)!, "config.json"));
    }

    /// <summary>Load config.json next to the GGUF weights.</summary>
    public static GameConfig LoadConfig(string? location = null) {
        string? gguf = ResolveGgufPath(location);
        if (gguf == null) {
            throw new InvalidOperationException("GAME GGML backend is missing .gguf weights.");
        }
        string configPath = Path.Combine(Path.GetDirectoryName(gguf)!, "config.json");
        if (!File.Exists(configPath)) {
            throw new InvalidOperationException(
                $"GAME GGML backend is missing config.json at {configPath}");
        }
        var jsonText = File.ReadAllText(configPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<GameConfig>(jsonText)
            ?? throw new InvalidOperationException("Failed to parse GAME config.json");
    }

    /// <summary>
    /// Construct a ready-to-serve backend if installed; otherwise throws so the
    /// factory can fall back. Model loading is lazy (deferred to EnsureLoaded).
    /// </summary>
    public static GameGgmlBackend Create() {
        string? cli = ResolveCliPath();
        string? gguf = ResolveGgufPath();
        if (cli == null || gguf == null) {
            throw new InvalidOperationException(
                "GAME GGML backend is not installed: missing CLI binary or .gguf weights.");
        }
        return new GameGgmlBackend(LoadConfig(), cli, gguf);
    }

    public bool EnsureLoaded() {
        if (process != null && !process.HasExited) return true;

        Log.Information("GAME(GGML): launching serve subprocess cli={Cli} gguf={Gguf}", cliPath, ggufPath);
        EnsureExecutable(cliPath);
        var psi = new ProcessStartInfo {
            FileName = cliPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(ggufPath);
        process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start game_ggml_cli.");
        // Binary stdin: avoid a text writer wrapper to control framing exactly.
        stdinWriter = new BinaryWriter(process.StandardInput.BaseStream, Encoding.ASCII, leaveOpen: false);
        stdoutReader = process.StandardOutput;

        // Drain stderr on a background thread so it can't deadlock the pipe.
        StartStderrPump();

        // Wait for {"type":"ready"} (or {"type":"error",...}) on stdout.
        string? first = ReadJsonObject();
        if (first == null || ParseType(first) == "error") {
            string msg = first ?? "subprocess closed stdout before ready";
            if (first != null) msg = ParseError(first) ?? msg;
            throw new InvalidOperationException($"GAME GGML backend failed to initialize: {msg}");
        }
        return true;
    }

    public List<TranscribedNote> RunInference(float[] samples, GameOptions options) {
        EnsureLoaded();
        if (stopping || process == null || process.HasExited) {
            throw new OperationCanceledException();
        }

        // Resolve language id.
        int languageId = 0;
        if (config.Languages != null && options.LanguageCode != null &&
            config.Languages.TryGetValue(options.LanguageCode, out int id)) {
            languageId = id;
        }

        // Build the request header + waveform, write to stdin.
        Span<byte> header = stackalloc byte[RequestHeaderBytes];
        BinaryPrimitivesWriteU32Little(header.Slice(0, 4), MAGIC_INFERENCE);
        BinaryPrimitivesWriteI32Little(header.Slice(4, 4), languageId);
        BinaryPrimitivesWriteU64Little(header.Slice(8, 8), (ulong)options.Seed);
        BinaryPrimitivesWriteI32Little(header.Slice(16, 4), options.SamplingSteps);
        BinaryPrimitivesWriteFloatLittle(header.Slice(20, 4), options.BoundaryThreshold);
        BinaryPrimitivesWriteI32Little(header.Slice(24, 4), options.BoundaryRadius);
        BinaryPrimitivesWriteFloatLittle(header.Slice(28, 4), options.ScoreThreshold);
        BinaryPrimitivesWriteU32Little(header.Slice(32, 4), (uint)samples.Length);

        var stdin = stdinWriter!;
        stdin.Write((ReadOnlySpan<byte>)header);
        // Float32 waveform, little-endian. On x86/x64 .NET float layout is LE.
        ReadOnlySpan<byte> waveBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan());
        stdin.Write(waveBytes);
        stdin.Flush();

        // Read one JSON object back: {"type":"notes","count":N,"notes":[...]}
        string? line = ReadJsonObject();
        if (line == null) throw new OperationCanceledException("GGML subprocess closed stdout during inference.");
        string type = ParseType(line);
        if (type == "error") {
            throw new InvalidOperationException($"GGML inference error: {ParseError(line)}");
        }
        if (type != "notes") {
            throw new InvalidOperationException($"Unexpected GGML response: {line}");
        }
        return ParseNotes(line);
    }

    public void Interrupt() {
        stopping = true;
        // Send the quit magic; the subprocess exits cleanly. Fall through to
        // a hard kill if it hangs.
        try {
            if (process != null && !process.HasExited && stdinWriter != null) {
                Span<byte> quit = stackalloc byte[4];
                BinaryPrimitivesWriteU32Little(quit, MAGIC_QUIT);
                stdinWriter.Write(quit);
                stdinWriter.Flush();
                if (!process.WaitForExit(3000)) {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                }
            }
        } catch {
            // Cancellation must never throw; swallow IO errors on a dying pipe.
        }
    }

    public void Dispose() {
        if (disposed) return;
        disposed = true;
        try {
            if (process != null && !process.HasExited) {
                try {
                    if (stdinWriter != null) {
                        Span<byte> quit = stackalloc byte[4];
                        BinaryPrimitivesWriteU32Little(quit, MAGIC_QUIT);
                        stdinWriter.Write(quit);
                        stdinWriter.Flush();
                    }
                } catch { /* pipe may already be broken */ }
                if (!process.WaitForExit(2000)) {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
        } finally {
            stdinWriter?.Dispose();
            stdoutReader?.Dispose();
            process?.Dispose();
            process = null;
        }
    }

    // -------------------------------------------------------------------------
    // stdout framing: read exactly one JSON object terminated by '\n'
    // -------------------------------------------------------------------------

    private string? ReadJsonObject() {
        // ReadLine blocks until '\n' or EOF; JSON objects are single-line.
        string? line = stdoutReader!.ReadLine();
        return string.IsNullOrEmpty(line) ? null : line;
    }

    private static string? ParseType(string json) {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
    }

    private static string? ParseError(string json) {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
    }

    private static List<TranscribedNote> ParseNotes(string json) {
        var notes = new List<TranscribedNote>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("notes", out var arr)) return notes;
        // The GGML backend emitted absolute offset_seconds per note, but the
        // segmenter partitions the chunk into contiguous regions whose lengths
        // sum exactly to the chunk length — each note already carries its own
        // duration_seconds. The MidiExtractor base class positions the chunk by
        // its audio offset and accumulates note durations sequentially, so we
        // pass duration_seconds through directly and ignore offset_seconds.
        foreach (var n in arr.EnumerateArray()) {
            float duration = n.TryGetProperty("d", out var de) ? de.GetSingle() : 0f;
            float pitch = n.TryGetProperty("p", out var pe) ? pe.GetSingle() : 0f;
            bool voiced = n.TryGetProperty("v", out var ve) && ve.GetInt32() != 0;
            if (duration < 0) duration = 0;
            notes.Add(new TranscribedNote(duration, pitch, voiced));
        }
        return notes;
    }

    // -------------------------------------------------------------------------
    // stderr pump (keeps the subprocess from blocking on a full error buffer)
    // -------------------------------------------------------------------------
    private void StartStderrPump() {
        var err = process!.StandardError;
        System.Threading.Tasks.Task.Run(() => {
            string? l;
            while ((l = err.ReadLine()) != null) {
                Log.Information("GAME(GGML)[stderr] {Line}", l);
            }
        });
    }

    private static void EnsureExecutable(string path) {
        if (OS.IsWindows()) return;
        const UnixFileMode executableMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        try {
            File.SetUnixFileMode(path, executableMode);
        } catch (Exception e) {
            Log.Warning(e, "GAME(GGML): failed to set executable permission on {Cli}", path);
        }
    }

    // -------------------------------------------------------------------------
    // Little-endian binary writers (no BitConverter pinned-buffer dance needed)
    // -------------------------------------------------------------------------
    private static void BinaryPrimitivesWriteU32Little(Span<byte> dst, uint v) {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(dst, v);
    }
    private static void BinaryPrimitivesWriteI32Little(Span<byte> dst, int v) {
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dst, v);
    }
    private static void BinaryPrimitivesWriteU64Little(Span<byte> dst, ulong v) {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(dst, v);
    }
    private static unsafe void BinaryPrimitivesWriteFloatLittle(Span<byte> dst, float v) {
        int bits = *(int*)&v;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(dst, bits);
    }
}
