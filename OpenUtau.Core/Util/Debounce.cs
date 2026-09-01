using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.Core.Util {
    public sealed class Debounce {
        private readonly object cancellationLock = new object();
        private CancellationTokenSource? cancellation;
        private Func<Task>? pendingCallback;

        public void Do(TimeSpan timeSpan, Func<Task> callback) {
            CancellationTokenSource currentCancellation;
            lock (cancellationLock) {
                cancellation?.Cancel();
                cancellation?.Dispose();
                cancellation = currentCancellation = new CancellationTokenSource();
                pendingCallback = callback;
            }
            _ = Run(timeSpan, currentCancellation);
        }

        public void Cancel() {
            lock (cancellationLock) {
                cancellation?.Cancel();
                cancellation?.Dispose();
                cancellation = null;
                pendingCallback = null;
            }
        }

        public Task Flush() {
            Func<Task>? callback;
            lock (cancellationLock) {
                cancellation?.Cancel();
                cancellation?.Dispose();
                cancellation = null;
                callback = pendingCallback;
                pendingCallback = null;
            }
            return callback?.Invoke() ?? Task.CompletedTask;
        }

        private async Task Run(
            TimeSpan timeSpan,
            CancellationTokenSource currentCancellation) {
            try {
                await Task.Delay(timeSpan, currentCancellation.Token);
                Func<Task>? callback;
                lock (cancellationLock) {
                    if (!ReferenceEquals(cancellation, currentCancellation)) {
                        return;
                    }
                    cancellation = null;
                    callback = pendingCallback;
                    pendingCallback = null;
                }
                if (callback == null) {
                    return;
                }
                await callback();
            } catch (OperationCanceledException)
                when (currentCancellation.IsCancellationRequested) {
            } catch (Exception e) {
                Log.Error(e, "Debounced operation failed.");
            } finally {
                lock (cancellationLock) {
                    if (ReferenceEquals(cancellation, currentCancellation)) {
                        cancellation = null;
                        pendingCallback = null;
                    }
                }
                currentCancellation.Dispose();
            }
        }
    }
}
