using System.IO;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using Serilog;

namespace OpenUtau.Classic {
    internal class WorldlineResampler : IResampler {
        public const string name = "worldline";
        public string FilePath { get; private set; }
        public bool CallDirectly { get; private set; }

        public WorldlineResampler() {
            string ext = OS.IsWindows() ? ".dll" : OS.IsMacOS() ? ".dylib" : ".so";
            FilePath = Path.Join(PathManager.Inst.RootPath, name + ext);
            CallDirectly = true;
        }

        public float[] DoResampler(ResamplerItem item, ILogger logger) {
            try {
                return Worldline.Resample(item);
            } catch (SynthRequestError e) {
                if (e is CutOffExceedDurationError cee) {
                    throw new MessageCustomizableException(
                        $"Failed to render\n Oto error: cutoff exceeds audio duration \n{item.phone.phoneme}",
                        $"<translate:errors.failed.synth.cutoffexceedduration>\n{item.phone.phoneme}",
                        e);
                }
                if (e is CutOffBeforeOffsetError cbe) {
                    throw new MessageCustomizableException(
                        $"Failed to render\n Oto error: cutoff before offset \n{item.phone.phoneme}",
                        $"<translate:errors.failed.synth.cutoffbeforeoffset>\n{item.phone.phoneme}",
                        e);
                }
                throw e;
            }
        }

        public string DoResamplerReturnsFile(ResamplerItem item, ILogger logger) {
            var samples = DoResampler(item, logger);
            var source = new WaveSource(0, 0, 0, 1);
            source.SetSamples(samples);
            lock (Renderers.GetCacheLock(item.outputFile)) {
                WaveFileWriter.CreateWaveFile16(item.outputFile, new ExportAdapter(source).ToMono(1, 0));
            }
            return item.outputFile;
        }

        public void CheckPermissions() { }

        //TODO: A list of flags supported by worldline resampler
        public ResamplerManifest Manifest { get; } = new ResamplerManifest();

        public bool SupportsFlag(string abbr) {
            return true;
        }

        public override string ToString() => name;
    }
}
