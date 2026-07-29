namespace OpenUtau.Core.Ustx {
    /// <summary>
    /// Per-track post-processing effects state.  Persisted in ustx alongside
    /// the rest of the track.  When a UTrack's MixFx is null or has Enabled =
    /// false, the render path bypasses the entire FX chain (zero overhead).
    /// </summary>
    public class UMixFx {
        public bool Enabled { get; set; } = false;

        // Preset name keys (kept for UI display only).  Slider values below
        // are the source of truth for the actual DSP.
        public string EqPreset { get; set; } = "vocal_air";
        public string CompPreset { get; set; } = "gentle";
        public string ReverbPreset { get; set; } = "small_room";

        public double EqLowDb { get; set; } = 0.0;
        public double EqMidFreq { get; set; } = 3000.0;
        public double EqMidDb { get; set; } = 1.5;
        public double EqHighDb { get; set; } = 3.0;

        public double CompThresholdDb { get; set; } = -18.0;
        public double CompRatio { get; set; } = 2.0;
        public double CompMakeupDb { get; set; } = 2.5;

        public double ReverbSize { get; set; } = 0.30;
        public double ReverbDamp { get; set; } = 0.7;
        public double ReverbWet { get; set; } = 1.0;
        // Default 0 (not 12) so legacy ustx files without this field deserialize
        // to the same audio they previously rendered.  The recommended-rack
        // builder and reverb preset loader assign explicit non-zero values
        // for new projects.
        public double ReverbPreDelayMs { get; set; } = 0.0;

        public UMixFx Clone() {
            return new UMixFx {
                Enabled = Enabled,
                EqPreset = EqPreset,
                CompPreset = CompPreset,
                ReverbPreset = ReverbPreset,
                EqLowDb = EqLowDb,
                EqMidFreq = EqMidFreq,
                EqMidDb = EqMidDb,
                EqHighDb = EqHighDb,
                CompThresholdDb = CompThresholdDb,
                CompRatio = CompRatio,
                CompMakeupDb = CompMakeupDb,
                ReverbSize = ReverbSize,
                ReverbDamp = ReverbDamp,
                ReverbWet = ReverbWet,
                ReverbPreDelayMs = ReverbPreDelayMs,
            };
        }
    }
}
