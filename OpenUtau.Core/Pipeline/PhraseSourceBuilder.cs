using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Pipeline {
    /// <summary>
    /// Tracks whether a part's latest phrase-source build has landed; render
    /// passes wait on it without holding the project lock.
    /// </summary>
    public sealed class PhraseBuildGate {
        private readonly object lockObj = new object();
        private readonly ManualResetEventSlim done = new ManualResetEventSlim(true);
        private long pending;
        private long completed;

        public void MarkPending(long generation) {
            lock (lockObj) {
                if (generation > pending) {
                    pending = generation;
                    done.Reset();
                }
            }
        }

        public void MarkCompleted(long generation) {
            lock (lockObj) {
                if (generation > completed) {
                    completed = generation;
                    if (completed >= pending) {
                        done.Set();
                    }
                }
            }
        }

        public bool WaitFor(long generation, TimeSpan timeout) {
            if (IsCurrent(generation)) {
                return true;
            }
            bool signaled = done.Wait(timeout);
            return IsCurrent(generation);
        }

        public bool IsCurrent(long generation) {
            lock (lockObj) {
                return completed >= generation;
            }
        }
    }

    /// <summary>
    /// Builds part phrases off the UI thread, coalesced to the latest snapshot
    /// per part; results are applied on the UI thread, stale ones dropped.
    ///
    /// The worker is single-threaded on purpose: voicebank resources are not
    /// verified thread-isolated, and the lazy <c>UOto.Frq</c> load in the MOD+
    /// path mutates shared oto state.
    /// </summary>
    public sealed class PhraseSourceBuilder : IDisposable {
        private readonly TaskScheduler mainScheduler;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly BlockingCollection<Request> requests = new BlockingCollection<Request>();
        private readonly object busyLock = new object();
        private readonly Thread thread;

        private sealed class Request {
            public PhraseSource Source;
            public UVoicePart Part;
        }

        /// <summary>
        /// The active worker, or null when the caller builds inline (test
        /// hosts).
        /// </summary>
        public static PhraseSourceBuilder Current { get; private set; }

        public PhraseSourceBuilder(TaskScheduler mainScheduler) {
            this.mainScheduler = mainScheduler;
            Current = this;
            thread = new Thread(BuilderLoop) {
                IsBackground = true,
                Name = "PhraseSourceBuilder",
            };
            thread.Start();
        }

        public bool Push(PhraseSource source, UVoicePart part) {
            if (shutdown.IsCancellationRequested) {
                return false;
            }
            try {
                requests.Add(new Request { Source = source, Part = part });
                return true;
            } catch (InvalidOperationException) {
                return false;
            }
        }

        private void BuilderLoop() {
            var latest = new Dictionary<UVoicePart, Request>();
            while (!shutdown.IsCancellationRequested) {
                lock (busyLock) {
                    while (requests.TryTake(out var request)) {
                        // Coalesce to the latest snapshot per part; a newer
                        // snapshot supersedes the older one before it starts.
                        latest[request.Part] = request;
                    }
                    if (latest.Count > 0) {
                        var batch = latest.Values.ToArray();
                        latest.Clear();
                        foreach (var request in batch) {
                            SendResult(Build(request));
                        }
                    }
                    try {
                        var request = requests.Take(shutdown.Token);
                        latest[request.Part] = request;
                    } catch (OperationCanceledException) {
                    }
                }
            }
        }

        private (PhraseSource Source, UVoicePart Part, RenderPhrase[] Phrases)? Build(Request request) {
            try {
                var phrases = request.Source.BuildPhrases();
                return (request.Source, request.Part, phrases);
            } catch (Exception e) {
                Log.Error(e, "Failed to build phrase source {part}", request.Source.PartId);
                return null;
            }
        }

        private void SendResult((PhraseSource Source, UVoicePart Part, RenderPhrase[] Phrases)? result) {
            Task.Factory.StartNew(_ => {
                if (result == null) {
                    return;
                }
                var (source, part, phrases) = result.Value;
                if (DocManager.Inst.Project?.parts.Contains(part) != true) {
                    return;
                }
                part.ApplyPhraseSourceResult(source, phrases);
            }, null, CancellationToken.None, TaskCreationOptions.None, mainScheduler);
        }

        /// <summary>Waits for the worker to drain; used by tests.</summary>
        public void Dispose() {
            shutdown.Cancel();
            requests.CompleteAdding();
            if (Current == this) {
                Current = null;
            }
        }
    }
}
