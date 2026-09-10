using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// Puts every DawIntegration test class into one non-parallel collection. These tests run
    /// real loopback sockets with real pumps and real second-scale timeouts; running them
    /// concurrently with each other (xUnit's default is one collection per class) is what
    /// showed up as random <c>init request timed out after 5.0s</c> failures on loaded CI
    /// runners, where the pumps' thread-pool continuations queue behind the rest of the suite.
    /// <c>DisableParallelization</c> keeps them off every other collection's threads, too.
    /// </summary>
    [CollectionDefinition(nameof(DawIntegrationCollection), DisableParallelization = true)]
    public sealed class DawIntegrationCollection {
        public const string Name = nameof(DawIntegrationCollection);
    }

    /// <summary>
    /// Raises the thread-pool minimums before anything in this assembly runs. A test process
    /// that mixes CPU-heavy suites with socket pumps that must answer inside 5 s starves the
    /// pool by default: it grows by roughly one thread per half second, which is exactly the
    /// shape of "passes locally, times out on CI". No test semantics are touched.
    /// </summary>
    internal static class DawTestBootstrap {
        [ModuleInitializer]
        internal static void Init() {
            ThreadPool.SetMinThreads(workerThreads: 32, completionPortThreads: 32);
        }
    }
}
