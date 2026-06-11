using System.Collections.Generic;

namespace OpenUtau.Core.SignalChain.Effects {
    /// <summary>
    /// Static lookup tables for the small set of presets exposed in the
    /// Mix-FX dialog.  All preset names are stable identifiers persisted
    /// in <c>Preferences</c>; user-facing labels live in i18n resources
    /// under the <c>mixfx.preset.*</c> keys.
    /// </summary>
    public static class FxPresets {
        public const string Off = "off";

        // ---------- EQ ----------
        public struct EqParams {
            public double LowDb;
            public double MidFreq;
            public double MidQ;
            public double MidDb;
            public double HighDb;
        }

        public static readonly IReadOnlyDictionary<string, EqParams> Eq = new Dictionary<string, EqParams> {
            // off = flat; no allocation needed because BiquadEQ bypasses on zero gains
            ["off"]       = new EqParams { LowDb = 0,  MidFreq = 1000, MidQ = 0.707, MidDb = 0,  HighDb = 0 },
            ["vocal_air"] = new EqParams { LowDb = 0,  MidFreq = 3000, MidQ = 0.707, MidDb = 1.5, HighDb = 3.0 },
            ["warm"]      = new EqParams { LowDb = 2,  MidFreq = 500,  MidQ = 0.707, MidDb = 0,  HighDb = -1.0 },
            ["demud"]     = new EqParams { LowDb = 0,  MidFreq = 200,  MidQ = 1.0,   MidDb = -3, HighDb = 0 },
            ["telephone"] = new EqParams { LowDb = -6, MidFreq = 1500, MidQ = 0.707, MidDb = 4,  HighDb = -6 },
        };
        public static readonly string[] EqPresetNames = { "off", "vocal_air", "warm", "demud", "telephone" };

        // ---------- Compressor ----------
        public struct CompParams {
            public double ThresholdDb;
            public double Ratio;
            public double AttackMs;
            public double ReleaseMs;
            public double MakeupDb;
        }

        public static readonly IReadOnlyDictionary<string, CompParams> Comp = new Dictionary<string, CompParams> {
            ["off"]    = new CompParams { ThresholdDb = 0,   Ratio = 1.0,  AttackMs = 10, ReleaseMs = 100, MakeupDb = 0 },
            ["gentle"] = new CompParams { ThresholdDb = -18, Ratio = 2.0,  AttackMs = 10, ReleaseMs = 120, MakeupDb = 2.5 },
            ["pop"]    = new CompParams { ThresholdDb = -14, Ratio = 3.0,  AttackMs = 5,  ReleaseMs = 80,  MakeupDb = 4.0 },
            ["limit"]  = new CompParams { ThresholdDb = -3,  Ratio = 10.0, AttackMs = 1,  ReleaseMs = 50,  MakeupDb = 0 },
        };
        public static readonly string[] CompPresetNames = { "off", "gentle", "pop", "limit" };

        // ---------- Reverb ----------
        public struct ReverbParams {
            public double RoomSize;
            public double Damp;
            public double Width;
            public double Wet;
            public double Dry;
            public double PreDelayMs;
        }

        public static readonly IReadOnlyDictionary<string, ReverbParams> Reverb = new Dictionary<string, ReverbParams> {
            ["off"]          = new ReverbParams { RoomSize = 0,    Damp = 0,   Width = 1.0, Wet = 0.0,  Dry = 1.0,  PreDelayMs = 0 },
            ["small_room"]   = new ReverbParams { RoomSize = 0.30, Damp = 0.7, Width = 0.8, Wet = 0.18, Dry = 0.85, PreDelayMs = 12 },
            ["vocal_plate"]  = new ReverbParams { RoomSize = 0.55, Damp = 0.3, Width = 1.0, Wet = 0.22, Dry = 0.85, PreDelayMs = 25 },
            ["hall"]         = new ReverbParams { RoomSize = 0.85, Damp = 0.4, Width = 1.0, Wet = 0.28, Dry = 0.80, PreDelayMs = 40 },
            ["ambient"]      = new ReverbParams { RoomSize = 0.92, Damp = 0.2, Width = 1.0, Wet = 0.35, Dry = 0.70, PreDelayMs = 60 },
        };
        public static readonly string[] ReverbPresetNames = { "off", "small_room", "vocal_plate", "hall", "ambient" };
    }
}
