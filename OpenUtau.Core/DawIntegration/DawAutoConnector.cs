using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// Connects plugin instances as soon as they are advertised, so a DAW project with several
    /// OpenUtau Bridge instances needs no trip to the DAW Integration dialog. Off by default;
    /// the dialog's checkbox turns it on and stores the choice in the preferences.
    /// </summary>
    /// <remarks>
    /// A pass scans the discovery directory and hands every new compatible advertisement to
    /// <see cref="DawManager.ConnectAllAsync"/>. Passes run when an advertisement is written
    /// (a <see cref="FileSystemWatcher"/>, debounced), when the project is saved or loaded,
    /// and on a slow poll that covers events the watcher drops. Nothing here shows a dialog:
    /// an unsaved project simply waits for its first save, and failures are logged.
    /// <para>
    /// A plugin is not retried after it fails until it rewrites its advertisement, so one
    /// that keeps refusing is not hammered. A plugin the user disconnects by hand is left
    /// alone until its advertisement goes away (the instance is unloaded), so a manual
    /// disconnect sticks.
    /// </para>
    /// <para>
    /// Scans delete stale advertisements, as the dialog's Refresh does. That is safe because
    /// a plugin publishes its file only once its port is bound (PROTOCOL.md §4).
    /// </para>
    /// </remarks>
    public sealed class DawAutoConnector : SingletonBase<DawAutoConnector>, ICmdSubscriber, IDisposable {
        public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

        /// <summary>An advertisement passes leave alone.</summary>
        private sealed class SkipEntry {
            /// <summary>The advertisement file, when known.</summary>
            public string? FilePath;
            /// <summary>The file's write time when it failed; a rewrite ends the skip.</summary>
            public DateTime WriteTime;
            /// <summary>A manual disconnect: skipped until the advertisement goes away.</summary>
            public bool UntilGone;
        }

        private readonly DawManager manager;
        private readonly DawServerFinder finder;
        private readonly TimeSpan debounce;
        private readonly TimeSpan pollInterval;
        private readonly object lockObj = new object();
        private readonly SemaphoreSlim passGate = new SemaphoreSlim(1, 1);

        /// <summary>By port.</summary>
        private readonly Dictionary<int, SkipEntry> skipped = new Dictionary<int, SkipEntry>();

        /// <summary>Port → advertisement file, from the last successful scan.</summary>
        private Dictionary<int, string> lastSeen = new Dictionary<int, string>();

        private FileSystemWatcher? watcher;
        private Timer? timer;
        private CancellationTokenSource? running;
        private bool enabled;
        private bool subscribed;
        private bool disposed;
        /// <summary>A trigger arrived after the running pass scanned; run again soon, not at
        /// the next poll.</summary>
        private bool rerunRequested;

        private DawAutoConnector() : this(DawManager.Inst, DawServerFinder.Default) { }

        public DawAutoConnector(DawManager manager, DawServerFinder finder,
            TimeSpan? debounce = null, TimeSpan? pollInterval = null) {
            this.manager = manager;
            this.finder = finder;
            this.debounce = debounce ?? DefaultDebounce;
            this.pollInterval = pollInterval ?? DefaultPollInterval;
        }

        /// <summary>Raised after every pass that ran while enabled, on a pool thread.</summary>
        public event Action<DawConnectAllResult>? PassCompleted;

        /// <summary>Turning it on runs a pass right away; turning it off leaves existing
        /// connections alone and abandons the plugins a running pass had not reached.</summary>
        public bool Enabled {
            get {
                lock (lockObj) {
                    return enabled;
                }
            }
            set {
                lock (lockObj) {
                    if (disposed || enabled == value) {
                        return;
                    }
                    enabled = value;
                    if (value) {
                        Start();
                    } else {
                        Stop();
                    }
                }
            }
        }

        /// <summary>
        /// Keeps passes from reconnecting what the user just disconnected, for as long as that
        /// plugin instance stays advertised.
        /// </summary>
        public void Skip(int port) {
            lock (lockObj) {
                lastSeen.TryGetValue(port, out string? path);
                skipped[port] = new SkipEntry { FilePath = path, UntilGone = true };
            }
        }

        /// <summary>Forgets every skip, e.g. when the user asks to connect all.</summary>
        public void ClearSkips() {
            lock (lockObj) {
                skipped.Clear();
            }
        }

        // Called with lockObj held.
        private void Start() {
            // Disconnects made while this was off were not about auto-connect.
            skipped.Clear();
            running = new CancellationTokenSource();
            timer ??= new Timer(_ => FireAndForget(PassAsync(CurrentToken())));
            try {
                if (OperatingSystem.IsWindows()) {
                    Directory.CreateDirectory(finder.Directory);
                } else {
                    // Owner-only from the start, as DawServerFinder requires (§11).
                    Directory.CreateDirectory(finder.Directory,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                watcher = new FileSystemWatcher(finder.Directory, "*.json") {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                };
                watcher.Created += OnDirectoryChanged;
                watcher.Changed += OnDirectoryChanged;
                watcher.Renamed += OnDirectoryChanged;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
            } catch (Exception e) {
                // The poll still finds plugins, only later.
                Log.Warning(e, $"DAW: cannot watch '{finder.Directory}'; falling back to polling.");
                watcher?.Dispose();
                watcher = null;
            }
            if (!subscribed) {
                DocManager.Inst.AddSubscriber(this);
                subscribed = true;
            }
            timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            Log.Information("DAW: auto-connect on.");
        }

        // Called with lockObj held.
        private void Stop() {
            running?.Cancel();
            running?.Dispose();
            running = null;
            watcher?.Dispose();
            watcher = null;
            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (subscribed) {
                DocManager.Inst.RemoveSubscriber(this);
                subscribed = false;
            }
            Log.Information("DAW: auto-connect off.");
        }

        private CancellationToken CurrentToken() {
            lock (lockObj) {
                return running?.Token ?? new CancellationToken(canceled: true);
            }
        }

        private void OnDirectoryChanged(object sender, FileSystemEventArgs e) => Schedule(debounce);

        private void OnWatcherError(object sender, ErrorEventArgs e) {
            Log.Warning(e.GetException(), "DAW: discovery watcher overflowed; rescanning.");
            Schedule(debounce);
        }

        /// <summary>A save gives an unsaved project the path a connection needs; a load is a
        /// new project that may have been waiting for one.</summary>
        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SaveProjectNotification || cmd is LoadProjectNotification) {
                Schedule(debounce);
            }
        }

        private void Schedule(TimeSpan delay) {
            lock (lockObj) {
                rerunRequested = true;
                if (enabled && !disposed) {
                    timer?.Change(delay, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private void ScheduleNext() {
            lock (lockObj) {
                if (enabled && !disposed) {
                    timer?.Change(rerunRequested ? debounce : pollInterval, Timeout.InfiniteTimeSpan);
                }
            }
        }

        /// <summary>
        /// One scan-and-connect pass. Public so tests can drive it; overlapping calls run one
        /// after the other.
        /// </summary>
        public async Task<DawConnectAllResult> PassAsync(CancellationToken cancellation = default) {
            var result = new DawConnectAllResult();
            await passGate.WaitAsync(cancellation);
            try {
                lock (lockObj) {
                    if (!enabled) {
                        return result;
                    }
                    rerunRequested = false;
                }
                // DawUstx refuses an unsaved project, and the error is the user's to see only
                // when they connect by hand. Here the save notification brings us back.
                if (string.IsNullOrEmpty(manager.ProjectSource()?.FilePath)) {
                    return result;
                }
                var found = Scan();
                if (found == null) {
                    return result;
                }
                lock (lockObj) {
                    lastSeen = found.ToDictionary(s => s.Port, s => s.FilePath);
                    foreach (var (port, entry) in skipped.ToList()) {
                        bool gone = entry.FilePath != null
                            ? !File.Exists(entry.FilePath)
                            : !lastSeen.ContainsKey(port);
                        if (gone) {
                            skipped.Remove(port);
                        }
                    }
                }
                // Asked again right before each connect: the user may disconnect a plugin while
                // an earlier one is still handshaking.
                result = await manager.ConnectAllAsync(found, cancellation, server => !IsSkipped(server));
                lock (lockObj) {
                    foreach (var server in result.Failed.Select(f => f.Server).Concat(result.Incompatible)) {
                        if (skipped.TryGetValue(server.Port, out var existing) && existing.UntilGone) {
                            continue;
                        }
                        skipped[server.Port] = new SkipEntry {
                            FilePath = server.FilePath,
                            WriteTime = WriteTimeOf(server),
                        };
                    }
                }
                foreach (var server in result.Connected) {
                    Log.Information($"DAW: auto-connected to {server}.");
                }
            } catch (MessageCustomizableException e) {
                Log.Information($"DAW: auto-connect waits: {e.Message}");
            } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
                throw;
            } catch (Exception e) {
                Log.Warning(e, "DAW: auto-connect pass failed.");
            } finally {
                passGate.Release();
                ScheduleNext();
            }
            PassCompleted?.Invoke(result);
            return result;
        }

        private bool IsSkipped(DawServer server) {
            lock (lockObj) {
                if (!skipped.TryGetValue(server.Port, out var entry)) {
                    return false;
                }
                if (entry.FilePath != null && entry.FilePath != server.FilePath) {
                    // A different advertisement now holds the port.
                    skipped.Remove(server.Port);
                    return false;
                }
                if (entry.UntilGone || entry.WriteTime == WriteTimeOf(server)) {
                    return true;
                }
                skipped.Remove(server.Port);
                return false;
            }
        }

        /// <summary>Null when the scan itself failed, so no skip is dropped on bad data.</summary>
        private List<DawServer>? Scan() {
            try {
                return finder.Scan();
            } catch (Exception e) {
                Log.Warning(e, $"DAW: scanning '{finder.Directory}' failed.");
                return null;
            }
        }

        private static DateTime WriteTimeOf(DawServer server) {
            try {
                return File.GetLastWriteTimeUtc(server.FilePath);
            } catch (Exception) {
                return DateTime.MinValue;
            }
        }

        private static void FireAndForget(Task task) {
            task.ContinueWith(t => Log.Warning(t.Exception, "DAW: auto-connect pass faulted."),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        public void Dispose() {
            lock (lockObj) {
                if (disposed) {
                    return;
                }
                if (enabled) {
                    Stop();
                }
                enabled = false;
                disposed = true;
                timer?.Dispose();
                timer = null;
            }
        }
    }
}
