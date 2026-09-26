using System.Collections.Generic;
using System.IO;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Classic {
    internal class WorldlineResampler : IResampler {
        public const string name = "worldline";
        public string FilePath { get; private set; }
        public bool NoWrapperScript { get; private set; }

        public WorldlineResampler() {
            string ext = OS.IsWindows() ? ".dll" : OS.IsMacOS() ? ".dylib" : ".so";
            FilePath = Path.Join(PathManager.Inst.RootPath, name + ext);
            NoWrapperScript = true;
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
            lock (Renderers.GetCacheLock(item.outputFile)) {
                Wave.WriteMono16Wav(item.outputFile, samples);
            }
            return item.outputFile;
        }

        public void CheckPermissions() { }

        public ResamplerManifest Manifest { get; } = new ResamplerManifest() {
            expressions = new Dictionary<string, UExpressionDescriptor> {
                { "ten", new UExpressionDescriptor("tension","ten",-100,100,0,"Mt") },
                { "brea", new UExpressionDescriptor("breathiness","brea",-100,100,0,"Mb") },
                { "voi", new UExpressionDescriptor("voicing","voi",0,100,100,"Mv") }
            },
            expressionFilter = false
        };

        public bool SupportsFlag(string abbr) {
            return true;
        }

        public override string ToString() => name;
    }
}
