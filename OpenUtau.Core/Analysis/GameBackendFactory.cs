using System;
using System.IO;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// Selects and constructs the active <see cref="IGameBackend"/> for a GAME
/// inference request, based on user preferences and which backend is installed.
///
/// Preference resolution order:
///  1. <see cref="SerializablePreferences.GameBackend"/> (the user's explicit choice) —
///     used if that backend is installed.
///  2. Fall back to whichever backend *is* installed (ONNX preferred over GGML,
///     since ONNX is the trusted baseline).
///  3. If neither is installed, throw — callers should have guarded with
///     <see cref="IsAnyInstalled"/>.
/// </summary>
public static class GameBackendFactory {
    public const string OnnxValue = "onnx";
    public const string GgmlValue = "ggml";

    /// <summary>True when at least one backend's weights + binaries are in place.</summary>
    public static bool IsAnyInstalled() {
        return GameOnnxBackend.IsInstalled() || GameGgmlBackend.IsInstalled();
    }

    /// <summary>The backend id that will actually be used given prefs + installs.</summary>
    public static string ResolveChoice() {
        string pref = Preferences.Default.GameBackend ?? "";
        if (pref == GgmlValue && GameGgmlBackend.IsInstalled()) return GgmlValue;
        if (pref == OnnxValue && GameOnnxBackend.IsInstalled()) return OnnxValue;
        // Fall back to whatever is installed, preferring ONNX.
        if (GameOnnxBackend.IsInstalled()) return OnnxValue;
        if (GameGgmlBackend.IsInstalled()) return GgmlValue;
        return "";  // none
    }

    /// <summary>The display name ("ONNX" / "GGML") for the resolved backend.</summary>
    public static string ResolvedBackendName() => ResolveChoice() switch {
        GgmlValue => "GGML",
        OnnxValue => "ONNX",
        _ => "(none)",
    };

    /// <summary>
    /// Load the configuration belonging to the backend that will actually be used.
    /// This must not fall back to the ONNX package when only GGML is installed.
    /// </summary>
    public static GameConfig LoadResolvedConfig() {
        string choice = ResolveChoice();
        if (choice == OnnxValue) {
            string location = PackageManager.Inst.GetInstalledPath("game")!;
            return Game.LoadConfig(location);
        }
        if (choice == GgmlValue) {
            return GameGgmlBackend.LoadConfig();
        }
        throw new InvalidOperationException(
            "No GAME backend is installed. Install the GAME ONNX or GGML weights via the Package Manager.");
    }

    /// <summary>
    /// Construct the resolved backend and load its config from the installed
    /// GAME weights directory.
    /// </summary>
    public static IGameBackend Create() {
        string choice = ResolveChoice();
        Log.Information("GAME: backend choice resolved to {Choice}", choice);
        if (choice == OnnxValue) {
            string location = PackageManager.Inst.GetInstalledPath("game")!;
            var config = Game.LoadConfig(location);
            return new GameOnnxBackend(config, location);
        }
        if (choice == GgmlValue) {
            return GameGgmlBackend.Create();
        }
        throw new InvalidOperationException(
            "No GAME backend is installed. Install the GAME ONNX or GGML weights via the Package Manager.");
    }
}
