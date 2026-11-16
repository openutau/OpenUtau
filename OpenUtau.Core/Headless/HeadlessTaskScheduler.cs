using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtau.Core.Headless {
    internal sealed class HeadlessTaskScheduler : TaskScheduler, IDisposable {
        private readonly Thread ownerThread;
        private readonly ConcurrentQueue<Task> tasks = new ConcurrentQueue<Task>();
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        private bool disposed;

        public HeadlessTaskScheduler(Thread ownerThread) {
            this.ownerThread = ownerThread;
        }

        public bool IsOwnerThread => Thread.CurrentThread == ownerThread;

        public void Post(Action action) {
            Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.None, this);
        }

        public bool RunOne(TimeSpan timeout) {
            if (!tasks.TryDequeue(out var task)) {
                signal.WaitOne(timeout);
                if (!tasks.TryDequeue(out task)) {
                    return false;
                }
            }
            TryExecuteTask(task);
            return true;
        }

        public void RunAvailable() {
            while (tasks.TryDequeue(out var task)) {
                TryExecuteTask(task);
            }
        }

        protected override IEnumerable<Task> GetScheduledTasks() {
            return tasks.ToArray();
        }

        protected override void QueueTask(Task task) {
            if (disposed) {
                throw new ObjectDisposedException(nameof(HeadlessTaskScheduler));
            }
            tasks.Enqueue(task);
            signal.Set();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
            if (!IsOwnerThread || taskWasPreviouslyQueued) {
                return false;
            }
            return TryExecuteTask(task);
        }

        public void Dispose() {
            disposed = true;
            signal.Dispose();
        }
    }

    internal sealed class HeadlessSynchronizationContext : SynchronizationContext {
        private readonly HeadlessTaskScheduler scheduler;

        public HeadlessSynchronizationContext(HeadlessTaskScheduler scheduler) {
            this.scheduler = scheduler;
        }

        public override void Post(SendOrPostCallback d, object? state) {
            scheduler.Post(() => d(state));
        }

        public override void Send(SendOrPostCallback d, object? state) {
            if (scheduler.IsOwnerThread) {
                d(state);
                return;
            }
            using var done = new ManualResetEventSlim(false);
            Exception? exception = null;
            scheduler.Post(() => {
                try {
                    d(state);
                } catch (Exception e) {
                    exception = e;
                } finally {
                    done.Set();
                }
            });
            done.Wait();
            if (exception != null) {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        public override SynchronizationContext CreateCopy() {
            return new HeadlessSynchronizationContext(scheduler);
        }
    }
}
