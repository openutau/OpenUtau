using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtau.Core.Render {
    /// <summary>
    /// Coalesced waveform refresh. Render completions call <see cref="Request"/>;
    /// a single pump task posts at most one <see cref="WaveformReadyNotification"/>
    /// per throttle window, so a 200-phrase pass repaints at ~10 Hz instead of
    /// allocating one task per phrase and posting 200 repaints.
    /// </summary>
    public static class WaveformRefresh {
        const int ThrottleMs = 100;

        static readonly object Lock = new object();
        static Task pump = null;
        static bool dirty;
        // -ThrottleMs so the very first post is immediate, not delayed by a window.
        static int lastPost = -ThrottleMs;

        /// <summary>Call from a render thread when a phrase's PCM has been published.</summary>
        public static void Request() {
            Task start = null;
            lock (Lock) {
                dirty = true;
                if (pump == null || pump.IsCompleted) {
                    pump = new Task(Pump);
                    start = pump;
                }
            }
            start?.Start(TaskScheduler.Default);
        }

        static void Pump() {
            while (true) {
                lock (Lock) {
                    // Post as soon as the throttle window after the last post has
                    // elapsed; requests arriving in the meantime merge into it.
                    var deadline = Math.Max(lastPost + ThrottleMs, Environment.TickCount);
                    while (Environment.TickCount < deadline) {
                        Monitor.Wait(Lock, 10);
                    }
                    if (!dirty) {
                        // Nothing was requested in time: the pump retires.
                        pump = null;
                        return;
                    }
                    dirty = false;
                    lastPost = Environment.TickCount;
                }
                DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
            }
        }
    }
}
