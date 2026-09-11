using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Classic {
    public interface IResampler {
        string FilePath { get; }
        bool CallDirectly { get; }
        float[] DoResampler(ResamplerItem args, ILogger logger);
        string DoResamplerReturnsFile(ResamplerItem args, ILogger logger);
        void CheckPermissions();
        ResamplerManifest Manifest {  get; }
        bool SupportsFlag(string abbr);
    }
}
