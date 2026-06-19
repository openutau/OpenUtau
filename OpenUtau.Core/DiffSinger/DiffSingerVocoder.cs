using System;
using System.IO;
using K4os.Hash.xxHash;
using Microsoft.ML.OnnxRuntime;

namespace OpenUtau.Core.DiffSinger {
    public class DsVocoder : IDisposable {
        public string Location;
        public DsVocoderConfig config;
        public ulong hash;
        public InferenceSession session;

        public int num_mel_bins => config.num_mel_bins;
        public int hop_size => config.hop_size;
        public int win_size => config.win_size;
        public int fft_size => config.fft_size;
        public int sample_rate => config.sample_rate;
        public double mel_fmin => config.mel_fmin;
        public double mel_fmax => config.mel_fmax;
        public string mel_base => config.mel_base;
        public string mel_scale => config.mel_scale;
        public bool pitch_controllable => config.pitch_controllable;

        public DsVocoder(string location) {
            byte[] model;
            try {
                Location = location;
                config = Core.Yaml.DefaultDeserializer.Deserialize<DsVocoderConfig>(
                    File.ReadAllText(Path.Combine(Location, "vocoder.yaml"),
                        System.Text.Encoding.UTF8));
                model = File.ReadAllBytes(Path.Combine(Location, config.model));
            }
            catch (Exception ex) {
                throw new MessageCustomizableException(
                    $"Error loading vocoder from \"{location}\"",
                    $"<translate:errors.diffsinger.downloadvocoder>",
                    ex,
                    true,
                    new string[] { Path.GetFileName(location), "https://github.com/xunmengshe/OpenUtau/wiki/Vocoders" });
            }
            if (config.num_mel_bins < 1 || config.num_mel_bins > DsVocoderConfig.MaxMelBins) {
                throw new MessageCustomizableException(
                    $"Invalid num_mel_bins in \"{Path.Combine(location, "vocoder.yaml")}\"",
                    $"<translate:errors.diffsinger.downloadvocoder>",
                    new Exception(
                        $"num_mel_bins must be between 1 and {DsVocoderConfig.MaxMelBins}, got {config.num_mel_bins}"),
                    true,
                    new string[] { Path.GetFileName(location), "https://github.com/xunmengshe/OpenUtau/wiki/Vocoders" });
            }
            hash = XXH64.DigestOf(model);
            session = Onnx.getInferenceSession(model);
        }

        public float frameMs() {
            return 1000f * config.hop_size / config.sample_rate;
        }

        private bool disposedValue;

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    session?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

    }

    [Serializable]
    public class DsVocoderConfig {
        // Hard cap on the number of mel bins a vocoder may declare. DiffSinger
        // vocoders in the wild use 80, 128 or similar values; values larger than
        // this are treated as invalid so that vocoders cannot smuggle through
        // onnx models that are actually something else (e.g. a full DiffSinger
        // acoustic model masquerading as a vocoder). Kept as a hard-coded
        // constant rather than a configurable field on purpose: lowering it
        // here is a deliberate code change, not something the voicebank author
        // can override.
        public const int MaxMelBins = 255;
        public string name = "vocoder";
        public string model = "model.onnx";
        public int sample_rate = 44100;
        public int hop_size = 512;
        public int win_size = 2048;
        public int fft_size = 2048;
        public int num_mel_bins = 128;
        public double mel_fmin = 40;
        public double mel_fmax = 16000;
        public string mel_base = "10";  // or "e"
        public string mel_scale = "slaney";  // or "htk"
        public bool pitch_controllable = false;
    }
}
