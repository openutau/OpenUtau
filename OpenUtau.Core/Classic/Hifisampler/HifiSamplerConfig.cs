// Ported from hifisampler's config.py and config.default.yaml (https://github.com/openhachimi/hifisampler),
// licensed under the Apache License 2.0; see LICENSE in this folder.
// Modified for OpenUtau: translated to C#, reading OpenUtau's resampler arguments.

using System;
using OpenUtau.Core.DiffSinger;

namespace OpenUtau.Classic.Hifisampler {
    /// <summary>
    /// config.py / config.default.yaml. The audio settings must match the vocoder; the
    /// processing settings are config.default.yaml's. Loop mode is the e flag, per note, rather
    /// than a global setting; P is Worldline's auto gain rather than loudness normalization.
    /// </summary>
    public class HifiSamplerConfig {
        public int SampleRate = 44100;
        public int WinSize = 2048;
        public int HopSize = 512;
        /// <summary>The analysis hop before the mel is interpolated to <see cref="HopSize"/>.</summary>
        public int OriginHopSize = 128;
        public int NMels = 128;
        public int NFft = 2048;
        public double MelFmin = 40;
        public double MelFmax = 16000;
        /// <summary>Mel frames kept beyond the note on each side, for the vocoder's context.</summary>
        public int Fill = 6;
        public double PeakLimit = 1.0;

        /// <summary>Throws when the vocoder was trained with other mel settings.</summary>
        public void Validate(DsVocoderConfig vocoder) {
            void Check(bool ok, string what, object expected, object actual) {
                if (!ok) {
                    throw new Exception($"Vocoder \"{vocoder.name}\" {what} is {actual}, hifisampler needs {expected}.");
                }
            }
            Check(vocoder.sample_rate == SampleRate, "sample_rate", SampleRate, vocoder.sample_rate);
            Check(vocoder.hop_size == HopSize, "hop_size", HopSize, vocoder.hop_size);
            Check(vocoder.win_size == WinSize, "win_size", WinSize, vocoder.win_size);
            Check(vocoder.fft_size == NFft, "fft_size", NFft, vocoder.fft_size);
            Check(vocoder.num_mel_bins == NMels, "num_mel_bins", NMels, vocoder.num_mel_bins);
            Check(vocoder.mel_fmin == MelFmin, "mel_fmin", MelFmin, vocoder.mel_fmin);
            Check(vocoder.mel_fmax == MelFmax, "mel_fmax", MelFmax, vocoder.mel_fmax);
            Check(vocoder.mel_base == "e", "mel_base", "e", vocoder.mel_base);
            Check(vocoder.mel_scale == "slaney", "mel_scale", "slaney", vocoder.mel_scale);
        }
    }
}
