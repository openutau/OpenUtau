using System;
using System.Diagnostics;
using System.Threading;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Debug-only thread-affinity asserts. Every function that must run on a
    /// specific context calls the matching assert at its entry; in release
    /// builds the calls compile out entirely.
    /// </summary>
    public static class ThreadGuard {
        static Thread uiThread;

        /// <summary>Registers the UI thread. Call once at startup.</summary>
        public static void SetUiThread(Thread thread) {
            uiThread = thread;
        }

        [Conditional("DEBUG")]
        public static void AssertUi() {
            // Test hosts without a UI thread run the UI-affine code inline;
            // with a registered UI thread (the app), the affinity is asserted.
            if (uiThread == null) {
                return;
            }
            Debug.Assert(uiThread == Thread.CurrentThread, "ThreadGuard: expected the UI thread");
        }
    }
}
