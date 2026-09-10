using System;
using System.Threading;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class WaveformRefreshTest : IDisposable {
        int posts;

        public WaveformRefreshTest() {
            posts = 0;
            DocManager.Inst.CommandSink = cmd => {
                if (cmd is WaveformReadyNotification) {
                    Interlocked.Increment(ref posts);
                }
            };
            // Let a pump left over from an earlier test in the collection retire.
            Thread.Sleep(300);
        }

        public void Dispose() => DocManager.Inst.CommandSink = null;

        static bool WaitUntil(Func<bool> condition, int timeoutMs) {
            var deadline = Environment.TickCount + timeoutMs;
            while (!condition()) {
                if (Environment.TickCount > deadline) {
                    return false;
                }
                Thread.Sleep(10);
            }
            return true;
        }

        [Fact]
        public void CoalescesBurstsAndPostsPromptly() {
            var baseline = posts;
            for (int i = 0; i < 50; ++i) {
                WaveformRefresh.Request();
            }
            Assert.True(WaitUntil(() => posts >= baseline + 1, 2000), "first post did not arrive");
            Thread.Sleep(300); // the burst window has passed; the pump retires
            var afterBurst = posts;
            Assert.InRange(afterBurst, baseline + 1, baseline + 3);

            // A single later request must still be posted promptly.
            WaveformRefresh.Request();
            Assert.True(WaitUntil(() => posts > afterBurst, 2000), "follow-up post did not arrive");
        }
    }
}
