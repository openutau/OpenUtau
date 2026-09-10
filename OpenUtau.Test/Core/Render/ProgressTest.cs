using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace OpenUtau.Core {
    [Collection(RenderSingletonCollection.Name)]
    public class ProgressTest : IDisposable {
        public void Dispose() => DocManager.Inst.CommandSink = null;

        static bool WaitUntil(Func<bool> condition, int timeoutMs) {
            var deadline = Environment.TickCount + timeoutMs;
            while (!condition()) {
                if (Environment.TickCount > deadline) {
                    return false;
                }
                Thread.Sleep(5);
            }
            return true;
        }

        [Fact]
        public void CoalescesPingsWhileDispatchInFlight() {
            // Payload-based assertions: a stray dispatch left over from an
            // earlier test in this collection carries a different payload and
            // cannot skew the expectations.
            var seen = new List<(double progress, string info)>();
            var seenLock = new object();
            var gateEntered = new ManualResetEventSlim(false);
            var release = new ManualResetEventSlim(false);
            DocManager.Inst.CommandSink = cmd => {
                if (cmd is not ProgressBarNotification n) {
                    return;
                }
                lock (seenLock) {
                    seen.Add((n.Progress, n.Info));
                }
                if (n.Info == "a") {
                    gateEntered.Set();
                    release.Wait(5000);
                }
            };

            var progress = new Render.Progress(100);
            progress.Complete(10, "a");
            Assert.True(gateEntered.Wait(5000));
            // Pings while the first dispatch is in flight must not each start
            // a task: none of b/c/d may be dispatched while the gate is held.
            progress.Complete(20, "b");
            progress.Complete(30, "c");
            progress.Complete(40, "d");
            Thread.Sleep(50);
            lock (seenLock) {
                Assert.DoesNotContain(seen, t => t.info is "b" or "c" or "d");
            }
            release.Set();
            // The in-flight dispatch re-arms with the latest coalesced value:
            // "d" is posted once; the intermediate pings are never posted.
            // (Complete(n) adds n to the total of 100, so "d" is 100% and "e" 150%.)
            Assert.True(WaitUntil(() =>
            {
                lock (seenLock) {
                    return seen.Contains((100.0, "d"));
                }
            }, 5000));
            Assert.True(WaitUntil(() => !progress.DispatchInFlight, 5000));
            progress.Complete(50, "e");
            Assert.True(WaitUntil(() =>
            {
                lock (seenLock) {
                    return seen.Contains((150.0, "e"));
                }
            }, 5000));
            Thread.Sleep(100); // let any (buggy) late dispatches land
            lock (seenLock) {
                Assert.DoesNotContain(seen, t => t.info is "b" or "c");
                Assert.Equal(1, seen.Count(t => t.info == "d"));
                Assert.Equal(1, seen.Count(t => t.info == "e"));
            }
        }
    }
}
