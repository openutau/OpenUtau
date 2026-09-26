using System;
using System.Collections.Generic;
using System.ComponentModel;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.DiffSinger {
    [Serializable]
    public class RandomPitchShifting {
        [Description("Key shift range in semitones the acoustic model was trained with, as [lowest, highest]. Limits the key shift OpenUtau sends to the model.")]
        public float[] range;
    }

    [Serializable]
    public class AugmentationArgs {
        [Description("Random pitch shifting used in training.")]
        public RandomPitchShifting randomPitchShifting;
    }

    [Serializable]
    public class DsConfig {
        [Description("Phoneme list of the models, relative to this folder.")]
        public string phonemes = "phonemes.txt";
        [Description("Language list of multi-language models, relative to this folder.")]
        public string languages;
        [Description("Acoustic model (ONNX), relative to this folder.")]
        public string acoustic;
        [Description("Vocoder, by the name of an installed dependency, e.g. nsf_hifigan.")]
        public string vocoder;
        [Description("Speakers of a multi-speaker model: embedding files without the .emb extension, relative to this folder. A subbank suffix picks the speaker.")]
        public List<string> speakers;
        [Description("Size of the speaker embeddings. Must match the model.")]
        public int hiddenSize = 256;
        [Description("The acoustic model takes a key shift input, which OpenUtau drives with the gender curve.")]
        public bool useKeyShiftEmbed = false;
        [Description("The acoustic model takes a speed input, which OpenUtau drives with the velocity curve.")]
        public bool useSpeedEmbed = false;
        [Description("The acoustic model takes an energy curve.")]
        public bool useEnergyEmbed = false;
        [Description("The acoustic model takes a breathiness curve.")]
        public bool useBreathinessEmbed = false;
        [Description("The acoustic model takes a voicing curve.")]
        public bool useVoicingEmbed = false;
        [Description("The acoustic model takes a tension curve.")]
        public bool useTensionEmbed = false;
        [Description("Data augmentation the acoustic model was trained with.")]
        public AugmentationArgs augmentationArgs;
        [Description("The models use continuous acceleration: max_depth is from 0 to 1 instead of steps out of 1000.")]
        public bool useContinuousAcceleration = false;
        [Description("The models take a language for each phoneme (multi-language models).")]
        public bool use_lang_id = false;
        [Description("Older name of use_variable_depth.")]
        [YamlMember(Alias = "use_shallow_diffusion")] public bool? _useShallowDiffusion;
        [Description("The acoustic model supports shallow diffusion, so the render depth can be lowered, down to max_depth.")]
        [YamlMember(Alias = "use_variable_depth")] public bool? _useVariableDepth;
        [YamlIgnore]
        public bool useVariableDepth {
            get {
                // coalesce _useDepth and _useShallowDiffusion
                if (_useVariableDepth.HasValue) {
                    return _useVariableDepth.Value;
                }
                if (_useShallowDiffusion.HasValue) {
                    return _useShallowDiffusion.Value;
                }
                return false;
            }
        }
        [Description("Largest diffusion depth for shallow diffusion: steps out of 1000, or from 0 to 1 with use_continuous_acceleration.")]
        [YamlMember(Alias = "max_depth")] public double _maxDepth;
        [YamlIgnore] public double maxDepth => useContinuousAcceleration ? _maxDepth : _maxDepth / 1000.0;
        [Description("Duration model (ONNX), relative to this folder.")]
        public string dur;
        [Description("Linguistic encoder (ONNX) of the duration, pitch or variance model, relative to this folder.")]
        public string linguistic;
        [Description("Pitch model (ONNX), relative to this folder.")]
        public string pitch;
        [Description("Variance model (ONNX), relative to this folder.")]
        public string variance;
        [Description("The linguistic encoder takes words (word divisions and durations) instead of phoneme durations.")]
        public bool predict_dur = true;
        [Description("The variance model predicts energy.")]
        public bool predict_energy = true;
        [Description("The variance model predicts breathiness.")]
        public bool predict_breathiness = true;
        [Description("The variance model predicts voicing.")]
        public bool predict_voicing = false;
        [Description("The variance model predicts tension.")]
        public bool predict_tension = false;
        [Description("The pitch model takes an expressiveness input.")]
        public bool use_expr = false;
        [Description("The pitch model takes which notes are rests.")]
        public bool use_note_rest = false;
        [Description("Sample rate in Hz. Must match the vocoder.")]
        public int sample_rate = 44100;
        [Description("Hop size in samples, the length of one frame. Must match the vocoder.")]
        public int hop_size = 512;
        [Description("Window size in samples. Must match the vocoder.")]
        public int win_size = 2048;
        [Description("FFT size. Must match the vocoder.")]
        public int fft_size = 2048;
        [Description("Number of mel bins. Must match the vocoder.")]
        public int num_mel_bins = 128;
        [Description("Lowest mel frequency in Hz. Must match the vocoder.")]
        public double mel_fmin = 40;
        [Description("Highest mel frequency in Hz. Must match the vocoder.")]
        public double mel_fmax = 16000;
        [Description("Logarithm base of the mel spectrogram: 10 or e. Must match the vocoder.")]
        public string mel_base = "10";
        [Description("Mel scale: slaney or htk. Must match the vocoder.")]
        public string mel_scale = "slaney";

        public float frameMs() {
            return 1000f * hop_size / sample_rate;
        }
    }
}
