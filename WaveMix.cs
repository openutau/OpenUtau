using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core.SignalChain {
    public class WaveMix : ISignalSource {
        private readonly List<ISignalSource> sources;

        public WaveMix(IEnumerable<ISignalSource> sources) {
            this.sources = sources.ToList();
        }

        public bool IsReady(int position, int count) {
            return sources.Count == 0 || sources.All(source => source.IsReady(position, count));
        }

        public int Mix(int position, float[] buffer, int index, int count) {
            if (sources.Count == 0) {
                return 0;
            }
            return sources.Max(source => source.Mix(position, buffer, index, count));
        }
    }
}
