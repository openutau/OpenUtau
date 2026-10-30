using Xunit;

namespace OpenUtau.Core.SignalChain {
    [CollectionDefinition(nameof(TimingSensitiveCollection), DisableParallelization = true)]
    public sealed class TimingSensitiveCollection {
        public const string Name = nameof(TimingSensitiveCollection);
    }
}
