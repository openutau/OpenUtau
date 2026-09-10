using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// Serializes the test classes that touch process-wide singletons — the
    /// DocManager command routing seam and the singer search paths.
    /// </summary>
    [CollectionDefinition(nameof(RenderSingletonCollection))]
    public sealed class RenderSingletonCollection {
        public const string Name = nameof(RenderSingletonCollection);
    }
}
