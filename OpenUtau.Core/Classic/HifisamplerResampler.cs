using System.IO;
using OpenUtau.Classic.Hifisampler;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.Classic {
    /// <summary>The built-in port of hifisampler (<see cref="HifiSampler"/>).</summary>
    internal class HifisamplerResampler : IResampler {
        // Part of ResamplerItem.Hash(), so it must not change.
        public const string name = "hifisampler";
        public string FilePath { get; private set; }
        public bool NoWrapperScript { get; private set; }

        public HifisamplerResampler() {
            // Nothing is executed from here: NoWrapperScript makes the wavtools call DoResampler.
            FilePath = Path.Join(PathManager.Inst.RootPath, name);
            NoWrapperScript = true;
        }

        public float[] DoResampler(ResamplerItem item, ILogger logger) {
            try {
                return HifiSampler.Resample(item);
            } catch (SynthRequestError e) {
                if (e is CutOffExceedDurationError) {
                    throw new MessageCustomizableException(
                        $"Failed to render\n Oto error: cutoff exceeds audio duration \n{item.phone.phoneme}",
                        $"<translate:errors.failed.synth.cutoffexceedduration>\n{item.phone.phoneme}",
                        e);
                }
                if (e is CutOffBeforeOffsetError) {
                    throw new MessageCustomizableException(
                        $"Failed to render\n Oto error: cutoff before offset \n{item.phone.phoneme}",
                        $"<translate:errors.failed.synth.cutoffbeforeoffset>\n{item.phone.phoneme}",
                        e);
                }
                if (e is ConsonantExceedsCutoffError) {
                    throw new MessageCustomizableException(
                        $"Failed to render\n Oto error: consonant exceeds cutoff \n{item.phone.phoneme}",
                        $"<translate:errors.failed.synth.consonantexceedscutoff>\n{item.phone.phoneme}",
                        e);
                }
                throw;
            }
        }

        public string DoResamplerReturnsFile(ResamplerItem item, ILogger logger) {
            var samples = DoResampler(item, logger);
            lock (Renderers.GetCacheLock(item.outputFile)) {
                Wave.WriteMono16Wav(item.outputFile, samples);
            }
            return item.outputFile;
        }

        public void CheckPermissions() { }

        public ResamplerManifest Manifest { get; } = new ResamplerManifest();

        public bool SupportsFlag(string abbr) {
            return true;
        }

        public override string ToString() => name;
    }
}
