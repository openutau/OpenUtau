using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>What a pending sync covers. Values are ordered as PROTOCOL.md §7 requires them sent.</summary>
    public enum DawSyncKind {
        Ustx = 0,
        Tracks = 1,
        PartLayout = 2,
        ProjectInfo = 3,
    }

    /// <summary>
    /// Trailing-edge debounce for the sync streams (PROTOCOL.md §7): 1 s for
    /// <c>updateUstx</c>/<c>updateTracks</c>/<c>updateProjectInfo</c>, 5 s for
    /// <c>updatePartLayout</c> and its audio.
    /// </summary>
    /// <remarks>
    /// Time is passed in rather than read from the clock, so the pump can be a timer in
    /// production and a plain loop in tests without any sleeping.
    /// </remarks>
    public sealed class DawSyncScheduler {
        public static readonly TimeSpan DefaultFastDebounce = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan DefaultSlowDebounce = TimeSpan.FromSeconds(5);

        private readonly TimeSpan fast;
        private readonly TimeSpan slow;
        private readonly object lockObj = new object();
        private readonly Dictionary<DawSyncKind, DateTime> due = new Dictionary<DawSyncKind, DateTime>();

        public DawSyncScheduler(TimeSpan? fastDebounce = null, TimeSpan? slowDebounce = null) {
            fast = fastDebounce ?? DefaultFastDebounce;
            slow = slowDebounce ?? DefaultSlowDebounce;
        }

        public TimeSpan DebounceFor(DawSyncKind kind) =>
            kind == DawSyncKind.PartLayout ? slow : fast;

        public bool HasPending {
            get {
                lock (lockObj) {
                    return due.Count > 0;
                }
            }
        }

        /// <summary>Marks a stream dirty, pushing its due time out to a full debounce from now.</summary>
        public void Touch(DawSyncKind kind, DateTime now) {
            lock (lockObj) {
                due[kind] = now + DebounceFor(kind);
            }
        }

        /// <summary>Makes every pending stream due immediately — the <c>playbackStarted</c> flush (§7).</summary>
        public void FlushPending(DateTime now) {
            lock (lockObj) {
                foreach (var kind in due.Keys.ToList()) {
                    due[kind] = now;
                }
            }
        }

        /// <summary>Marks one stream dirty and immediately due, bypassing its debounce.</summary>
        public void MakeDue(DawSyncKind kind, DateTime now) {
            lock (lockObj) {
                due[kind] = now;
            }
        }

        /// <summary>Marks every stream dirty and immediately due. Used for the post-(re)connect full sync.</summary>
        public void RequestFullSync(DateTime now) {
            lock (lockObj) {
                foreach (DawSyncKind kind in Enum.GetValues<DawSyncKind>()) {
                    due[kind] = now;
                }
            }
        }

        public void Clear() {
            lock (lockObj) {
                due.Clear();
            }
        }

        /// <summary>Takes the streams whose debounce has elapsed, in §7 send order.</summary>
        public DawSyncKind[] TryTake(DateTime now) {
            lock (lockObj) {
                var ready = due
                    .Where(entry => entry.Value <= now)
                    .Select(entry => entry.Key)
                    .OrderBy(kind => (int)kind)
                    .ToArray();
                foreach (var kind in ready) {
                    due.Remove(kind);
                }
                return ready;
            }
        }
    }

    /// <summary>
    /// Bounded hash → PCM store. <c>updatePartLayout</c> advertises hashes and the plugin pulls
    /// them later with <c>getAudio</c> (PROTOCOL.md §6.2), so the bytes have to outlive the
    /// message that named them.
    /// </summary>
    /// <remarks>
    /// A three-minute stereo part is roughly 63 MB of float32, so the store is capped and evicts
    /// least-recently-used entries. A miss is recoverable: <see cref="DawManager"/> re-extracts
    /// the audio from the part that owns the hash.
    /// </remarks>
    public sealed class DawAudioCache {
        public const long DefaultCapacityBytes = 256L * 1024 * 1024;

        private sealed class Entry {
            public byte[] Pcm = Array.Empty<byte>();
            public long Stamp;
        }

        private readonly long capacity;
        private readonly object lockObj = new object();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private long stamp;
        private long sizeBytes;

        public DawAudioCache(long capacityBytes = DefaultCapacityBytes) {
            capacity = Math.Max(1, capacityBytes);
        }

        public long SizeBytes { get { lock (lockObj) { return sizeBytes; } } }
        public int Count { get { lock (lockObj) { return entries.Count; } } }

        public void Put(string hash, byte[] pcm) {
            lock (lockObj) {
                if (entries.TryGetValue(hash, out var previous)) {
                    sizeBytes -= previous.Pcm.Length;
                }
                entries[hash] = new Entry { Pcm = pcm, Stamp = ++stamp };
                sizeBytes += pcm.Length;
                Evict();
            }
        }

        public bool TryGet(string hash, out byte[] pcm) {
            lock (lockObj) {
                if (entries.TryGetValue(hash, out var entry)) {
                    entry.Stamp = ++stamp;
                    pcm = entry.Pcm;
                    return true;
                }
            }
            pcm = Array.Empty<byte>();
            return false;
        }

        /// <summary>Drops every hash the current layout no longer advertises.</summary>
        public void Retain(ICollection<string> keep) {
            lock (lockObj) {
                foreach (string hash in entries.Keys.Where(hash => !keep.Contains(hash)).ToList()) {
                    sizeBytes -= entries[hash].Pcm.Length;
                    entries.Remove(hash);
                }
            }
        }

        public void Clear() {
            lock (lockObj) {
                entries.Clear();
                sizeBytes = 0;
            }
        }

        /// <summary>Keeps one entry whatever its size, so an oversized part can still be served.</summary>
        private void Evict() {
            while (sizeBytes > capacity && entries.Count > 1) {
                string oldest = entries.OrderBy(entry => entry.Value.Stamp).First().Key;
                sizeBytes -= entries[oldest].Pcm.Length;
                entries.Remove(oldest);
            }
        }
    }

    /// <summary>Connection state, surfaced to the UI.</summary>
    public enum DawConnectionState {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
    }

    /// <summary>One plugin connection, as the connection list shows it.</summary>
    public sealed class DawConnectionInfo {
        public int Port { get; }
        public string Name { get; }
        public DawConnectionState State { get; }

        public DawConnectionInfo(int port, string name, DawConnectionState state) {
            Port = port;
            Name = name;
            State = state;
        }
    }

    /// <summary>
    /// Drives every plugin connection: the <see cref="ICmdSubscriber"/> subscription that marks
    /// streams dirty, the debounced sync pump broadcast to all connections, audio serving and
    /// per-connection reconnect backoff (PROTOCOL.md §7, §9).
    /// </summary>
    /// <remarks>
    /// Each plugin instance in the DAW binds one OpenUtau track, so several instances are
    /// expected to be connected at once. Project state (the debounce scheduler, the audio
    /// cache, the hash owners) is shared; connection state (transport, reconnect ladder) is
    /// per connection.
    /// </remarks>
    public sealed class DawManager : SingletonBase<DawManager>, ICmdSubscriber, IDisposable {
        /// <summary>§3: 500 ms, 1 s, 2 s, then give up and tell the user.</summary>
        public static readonly TimeSpan[] DefaultReconnectBackoff = {
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };

        /// <summary>§3: the init handshake gets 5 s, not the ordinary 10 s request budget.</summary>
        public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Pump period. Well under the 1 s fast debounce, so it never dominates latency.</summary>
        public static readonly TimeSpan DefaultPumpInterval = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Playhead updates smaller than this are not forwarded to the document, so a parked
        /// DAW transport does not re-seek OpenUtau on every notification.
        /// </summary>
        public const int PlayheadEpsilonTicks = 5;

        /// <summary>Per-connection state. Guarded by <see cref="stateLock"/>.</summary>
        private sealed class Connection {
            public DawServer Server = null!;
            public DawTransport? Transport;
            public bool ClosingLocally;
            public bool Reconnecting;
            /// <summary>The init answer's minor (§4.2). Defaults to this build's own, so a
            /// connection that has not finished its handshake is treated as fully capable.</summary>
            public int NegotiatedMinor = DawApiVersion.Current.Minor;
        }

        private readonly DawSyncScheduler scheduler;
        private readonly DawAudioCache audioCache;
        private readonly TimeSpan[] reconnectBackoff;
        private readonly SemaphoreSlim syncGate = new SemaphoreSlim(1, 1);
        private readonly object stateLock = new object();

        /// <summary>Advertised hash → the part that produced it, so a cache miss can be re-extracted.</summary>
        private readonly Dictionary<string, UVoicePart> hashOwners = new Dictionary<string, UVoicePart>();

        private readonly List<Connection> connections = new List<Connection>();
        private Timer? pump;
        private bool subscribed;
        private int disposed;

        // BPM-mismatch reporting state (document thread only).
        private double lastDawBpm = double.NaN;
        private double lastWarnedProjectBpm = double.NaN;

        public DawManager() : this(null) { }

        public DawManager(
            DawSyncScheduler? syncScheduler,
            DawAudioCache? cache = null,
            TimeSpan[]? backoff = null) {
            scheduler = syncScheduler ?? new DawSyncScheduler();
            audioCache = cache ?? new DawAudioCache();
            reconnectBackoff = backoff ?? DefaultReconnectBackoff;
        }

        public DawConnectionState State { get; private set; } = DawConnectionState.Disconnected;
        public bool IsConnected => LiveConnections().Any();
        public string ServerName {
            get {
                lock (stateLock) {
                    return connections.FirstOrDefault(c => c.Transport?.IsConnected == true)
                        ?.Server.Name ?? string.Empty;
                }
            }
        }
        public DawSyncScheduler Scheduler => scheduler;
        public DawAudioCache AudioCache => audioCache;

        /// <summary>Injectable clock, so debounce tests never sleep.</summary>
        public Func<DateTime> NowUtc { get; set; } = () => DateTime.UtcNow;

        /// <summary>
        /// The project being synced. Defaults to the open document; injectable so tests can drive
        /// a hand-built project without standing up <see cref="DocManager"/>'s UI thread.
        /// </summary>
        public Func<UProject> ProjectSource { get; set; } = () => DocManager.Inst.Project;

        public DawTransportOptions TransportOptions { get; set; } = DawTransportOptions.Default;

        /// <summary>Set to false in tests, which drive <see cref="PumpOnceAsync"/> by hand.</summary>
        public bool UseTimerPump { get; set; } = true;

        public event Action<DawConnectionState>? StateChanged;

        /// <summary>Raised whenever a connection is added, dropped or changes state.</summary>
        public event Action? ConnectionsChanged;

        /// <summary>Raised when reconnection is exhausted. The UI turns this into a visible error.</summary>
        public event Action<string>? ConnectionLost;

        /// <summary>The connections currently held, in connect order, for the UI list.</summary>
        public IReadOnlyList<DawConnectionInfo> Connections {
            get {
                lock (stateLock) {
                    return connections
                        .Select(c => new DawConnectionInfo(c.Server.Port, c.Server.Name, StateOf(c)))
                        .ToList();
                }
            }
        }

        private static DawConnectionState StateOf(Connection c) {
            if (c.Transport?.IsConnected == true) {
                return DawConnectionState.Connected;
            }
            return c.Reconnecting ? DawConnectionState.Reconnecting : DawConnectionState.Connecting;
        }

        private List<Connection> LiveConnections() {
            lock (stateLock) {
                return connections.Where(c => c.Transport?.IsConnected == true).ToList();
            }
        }

        /// <summary>
        /// Connects to a discovered plugin, performs the init handshake and starts syncing.
        /// Several plugins may be connected at once, one per DAW plugin instance; connecting a
        /// port that is already connected replaces that connection.
        /// </summary>
        /// <exception cref="DawProtocolException">
        /// The advertisement, or the plugin's own init answer, is an api major this build cannot speak (§4).
        /// </exception>
        public async Task ConnectAsync(DawServer target, CancellationToken cancellation = default) {
            if (!target.IsCompatible) {
                throw new DawProtocolException(
                    $"Plugin '{target.Name}' speaks api '{target.Info.ApiVersion}', " +
                    $"this build speaks {DawApiVersion.CurrentString}.");
            }
            Connection? replaced;
            lock (stateLock) {
                replaced = connections.FirstOrDefault(c => c.Server.Port == target.Port);
            }
            if (replaced != null) {
                await CloseConnectionAsync(replaced, finalSync: false);
            }
            var conn = new Connection { Server = target };
            lock (stateLock) {
                connections.Add(conn);
            }
            RecomputeState();
            try {
                await OpenConnectionAsync(conn, cancellation);
            } catch {
                lock (stateLock) {
                    connections.Remove(conn);
                }
                RecomputeState();
                // OpenConnectionAsync may already have subscribed and started the pump before a
                // post-init sync failed; a failed connect must not leave either behind (matching
                // CloseConnectionAsync and the reconnect ladder's teardown).
                StopPumpIfIdle();
                UnsubscribeIfIdle();
                throw;
            }
        }

        /// <summary>
        /// The per-connection half of the old OpenAsync: transport, init handshake, then the
        /// streams §9 still owes the plugin (tracks, layout, project info — init already
        /// carried the USTX baseline).
        /// </summary>
        private async Task OpenConnectionAsync(Connection conn, CancellationToken cancellation) {
            var opened = await DawTransport.ConnectAsync(
                conn.Server.Port, TransportOptions, NowUtc, cancellation);
            Action<string, JsonElement?> onNotification =
                (kind, payload) => OnPluginNotification(conn, kind, payload);
            Action<DawDisconnectReason, string> onDisconnected =
                (reason, detail) => OnTransportDisconnected(conn, reason, detail);
            opened.Notification += onNotification;
            opened.Disconnected += onDisconnected;
            opened.RequestHandler = ServeRequestAsync;
            lock (stateLock) {
                conn.Transport = opened;
            }
            try {
                // §6.1 as decided: init carries the USTX baseline and the answer is the api version.
                string ustx = await SerializeProjectAsync();
                var response = await opened.SendRequestAsync<InitResponse>(
                    DawMessageKind.Init, new InitRequest { Ustx = ustx }, InitTimeout, cancellation);
                if (!DawApiVersion.TryParse(response.ApiVersion, out var version)
                    || !version.IsCompatibleWith(DawApiVersion.Current)) {
                    throw new DawProtocolException(
                        $"Plugin answered init with api '{response.ApiVersion}', " +
                        $"which this build cannot speak.");
                }
                lock (stateLock) {
                    conn.NegotiatedMinor = version.Minor;
                }
                Subscribe();
                RecomputeState();
                StartPump();
                // init already delivered the USTX, so only tracks, layout and project info are
                // outstanding (§7, §9). These go to this connection alone: the others already hold
                // the same state.
                await SyncConnectionAsync(conn, DawSyncKind.Tracks, cancellation);
                await SyncConnectionAsync(conn, DawSyncKind.ProjectInfo, cancellation);
                await SyncConnectionAsync(conn, DawSyncKind.PartLayout, cancellation);
            } catch {
                // A failed open must not leave a wired, connected transport behind: if the peer
                // later dropped it, the disconnected callback would start a second reconnect
                // ladder while the caller is still unwinding this one. Detach before disposing
                // so the synchronous callback never fires. The whole open — init, subscription,
                // pump and the initial per-connection syncs — is covered, so a post-init
                // failure (a layout request timing out, an unsaved project) cannot leave a
                // live transport behind either.
                opened.Disconnected -= onDisconnected;
                bool stillCurrent;
                lock (stateLock) {
                    stillCurrent = ReferenceEquals(conn.Transport, opened);
                    if (stillCurrent) {
                        conn.Transport = null;
                    }
                }
                opened.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Serializes the open project to USTX YAML — byte-identical to what <c>Ustx.Save</c>
        /// writes. <see cref="DawUstx.Serialize"/> runs <c>BeforeSave</c>/<c>AfterSave</c>, which
        /// mutate the project, so it has to happen on the document thread.
        /// </summary>
        private Task<string> SerializeProjectAsync() {
            return OnDocumentThreadAsync(() => DawUstx.Serialize(ProjectSource()));
        }

        /// <summary>
        /// Runs work on the document thread. <see cref="DocManager.MainScheduler"/> is null until
        /// the UI calls <c>Initialize</c>, which is the case in tests; then the work runs inline.
        /// </summary>
        private Task<T> OnDocumentThreadAsync<T>(Func<T> work) {
            var docScheduler = DocManager.Inst.MainScheduler;
            if (docScheduler == null) {
                return Task.FromResult(work());
            }
            return Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.None, docScheduler);
        }

        private void Subscribe() {
            lock (stateLock) {
                // A reconnect handshake can complete after Dispose ran: without this check the
                // disposed manager would re-register on DocManager for the process lifetime.
                if (subscribed || Volatile.Read(ref disposed) != 0) {
                    return;
                }
                DocManager.Inst.AddSubscriber(this);
                subscribed = true;
            }
        }

        private void UnsubscribeIfIdle() {
            lock (stateLock) {
                if (!subscribed || connections.Count > 0) {
                    return;
                }
                DocManager.Inst.RemoveSubscriber(this);
                subscribed = false;
            }
        }

        private void RecomputeState() {
            DawConnectionState next;
            bool changed;
            // One atomic scope: concurrent callers (the transport read loop, reconnects, user
            // connects) would otherwise interleave between computing and assigning, and a later
            // caller could publish an older value.
            lock (stateLock) {
                if (connections.Any(c => c.Transport?.IsConnected == true)) {
                    next = DawConnectionState.Connected;
                } else if (connections.Any(c => c.Reconnecting)) {
                    next = DawConnectionState.Reconnecting;
                } else if (connections.Count > 0) {
                    next = DawConnectionState.Connecting;
                } else {
                    next = DawConnectionState.Disconnected;
                }
                changed = State != next;
                State = next;
            }
            if (changed) {
                StateChanged?.Invoke(next);
            }
            ConnectionsChanged?.Invoke();
        }

        private void StartPump() {
            if (!UseTimerPump) {
                return;
            }
            lock (stateLock) {
                pump?.Dispose();
                pump = new Timer(
                    _ => PumpOnceAsync(CancellationToken.None).ContinueWith(
                        task => Log.Error(task.Exception!, "DAW: sync pump failed."),
                        TaskContinuationOptions.OnlyOnFaulted),
                    null,
                    DefaultPumpInterval,
                    DefaultPumpInterval);
            }
        }

        private void StopPumpIfIdle() {
            lock (stateLock) {
                if (connections.Count > 0) {
                    return;
                }
                pump?.Dispose();
                pump = null;
            }
        }

        /// <summary>
        /// The document command stream. Runs on the document thread for every single edit, so it
        /// only ever sets flags — the pump does the work (§7).
        /// </summary>
        public void OnNext(UCommand cmd, bool isUndo) {
            if (!IsConnected) {
                return;
            }
            var now = NowUtc();
            switch (cmd) {
                case VolumeChangeNotification:
                case PanChangeNotification:
                case SoloTrackNotification:
                    scheduler.Touch(DawSyncKind.Tracks, now);
                    break;
                case PartRenderedNotification:
                    scheduler.Touch(DawSyncKind.PartLayout, now);
                    break;
                case LoadProjectNotification:
                    // A different project: nothing any plugin holds is valid any more.
                    audioCache.Clear();
                    lock (stateLock) {
                        hashOwners.Clear();
                    }
                    scheduler.RequestFullSync(now);
                    break;
                case SaveProjectNotification:
                    // Saving for the first time is what turns an unsaved project syncable, and
                    // the file name is what the plugins' info windows show.
                    scheduler.Touch(DawSyncKind.ProjectInfo, now);
                    break;
                case UNotification:
                    // Transient UI state — play position, selection, progress. Not project data.
                    break;
                case TrackCommand:
                    // Adding, removing or renaming a track moves parts between track numbers too.
                    scheduler.Touch(DawSyncKind.Tracks, now);
                    scheduler.Touch(DawSyncKind.Ustx, now);
                    scheduler.Touch(DawSyncKind.PartLayout, now);
                    break;
                default:
                    // A real edit. The audio follows once the renderer reports back.
                    scheduler.Touch(DawSyncKind.Ustx, now);
                    scheduler.Touch(DawSyncKind.PartLayout, now);
                    break;
            }
        }

        /// <summary>
        /// Sends whatever the debounce has made due, in §7 order, to every live connection. A
        /// gate serializes ticks so a slow layout sync can never overlap the next one.
        /// </summary>
        public async Task PumpOnceAsync(CancellationToken cancellation = default) {
            if (!IsConnected || !scheduler.HasPending) {
                return;
            }
            if (!await syncGate.WaitAsync(0, cancellation)) {
                // Already syncing. Whatever is due simply waits for the next tick.
                return;
            }
            try {
                foreach (var kind in scheduler.TryTake(NowUtc())) {
                    if (!IsConnected) {
                        break;
                    }
                    await SyncAsync(kind, cancellation);
                }
            } catch (OperationCanceledException) {
                // Shutting down.
            } catch (Exception e) {
                // A refused envelope or a serialization fault leaves the stream coherent, so the
                // connection survives and the stream is retried on the next edit.
                Log.Error(e, "DAW: sync failed.");
            } finally {
                syncGate.Release();
            }
        }

        /// <summary>
        /// Sends one stream to every live connection. Shared payloads (the USTX YAML, the
        /// track list, the part layout and its hashes) are built once, not once per
        /// connection; only the sends are per connection. A request timeout drops only the
        /// connection it happened on (§8); the others stay synced.
        /// </summary>
        public async Task SyncAsync(DawSyncKind kind, CancellationToken cancellation = default) {
            var live = LiveConnections();
            if (live.Count == 0) {
                return;
            }
            switch (kind) {
                case DawSyncKind.Ustx: {
                    // Serialize once: BeforeSave/AfterSave mutate the project's serialization
                    // views, so N connections must not mean N mutation round trips.
                    var payload = new UpdateUstxNotification { Ustx = await SerializeProjectAsync() };
                    await BroadcastAsync(live, (conn, token) =>
                        conn.Transport!.SendNotificationAsync(DawMessageKind.UpdateUstx, payload, token),
                        cancellation);
                    break;
                }
                case DawSyncKind.Tracks: {
                    // §10: the v1.2 informational fields are omitted for peers that negotiated a
                    // lower minor, so the payload carries the minimum across the targets.
                    var payload = await BuildTracksAsync(MinNegotiatedMinor(live));
                    await BroadcastAsync(live, (conn, token) =>
                        conn.Transport!.SendNotificationAsync(DawMessageKind.UpdateTracks, payload, token),
                        cancellation);
                    break;
                }
                case DawSyncKind.ProjectInfo: {
                    var payload = await BuildProjectInfoAsync();
                    await BroadcastAsync(live, (conn, token) =>
                        conn.Transport!.SendNotificationAsync(DawMessageKind.UpdateProjectInfo, payload, token),
                        cancellation);
                    break;
                }
                case DawSyncKind.PartLayout: {
                    var layout = await BuildPartLayoutAsync();
                    await BroadcastAsync(live, async (conn, token) => {
                        var response = await conn.Transport!.SendRequestAsync<UpdatePartLayoutResponse>(
                            DawMessageKind.UpdatePartLayout,
                            new UpdatePartLayoutRequest { Parts = layout },
                            cancellation: token);
                        if (response.MissingAudios.Count > 0) {
                            // The plugin pulls each one itself with getAudio (§6.2); we just keep them warm.
                            Log.Information(
                                $"DAW: plugin '{conn.Server.Name}' is missing " +
                                $"{response.MissingAudios.Count} of {layout.Count} part audios.");
                        }
                    }, cancellation);
                    break;
                }
            }
        }

        /// <summary>
        /// Sends one pre-built payload to every live connection. Any failure a connection raises
        /// drops only that connection (§8); the others still receive the payload.
        /// </summary>
        private async Task BroadcastAsync(List<Connection> targets,
                Func<Connection, CancellationToken, Task> send, CancellationToken cancellation) {
            foreach (var conn in targets) {
                var t = conn.Transport;
                if (t == null || !t.IsConnected) {
                    continue;
                }
                try {
                    await send(conn, cancellation);
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception e) {
                    // Not only timeouts: a protocol fault or socket error on one connection must
                    // not leave the remaining targets stale until the next edit re-arms the stream.
                    Log.Warning(e, $"DAW: sync to '{conn.Server.Name}' failed; dropping it.");
                    await DropConnectionAsync(conn);
                }
            }
        }

        /// <summary>Sends one stream to one connection. Public-per-kind so tests can force a sync.</summary>
        private async Task SyncConnectionAsync(Connection conn, DawSyncKind kind, CancellationToken cancellation) {
            var live = conn.Transport;
            if (live == null || !live.IsConnected) {
                return;
            }
            switch (kind) {
                case DawSyncKind.Ustx:
                    await live.SendNotificationAsync(
                        DawMessageKind.UpdateUstx,
                        new UpdateUstxNotification { Ustx = await SerializeProjectAsync() },
                        cancellation);
                    break;
                case DawSyncKind.Tracks:
                    await live.SendNotificationAsync(
                        DawMessageKind.UpdateTracks,
                        await BuildTracksAsync(minor: GetNegotiatedMinor(conn)), cancellation);
                    break;
                case DawSyncKind.ProjectInfo:
                    await live.SendNotificationAsync(
                        DawMessageKind.UpdateProjectInfo, await BuildProjectInfoAsync(), cancellation);
                    break;
                case DawSyncKind.PartLayout:
                    await SyncPartLayoutAsync(conn, live, cancellation);
                    break;
            }
        }

        /// <summary>The lowest minor any of the targets negotiated, so a shared payload is safe for all of them.</summary>
        private int MinNegotiatedMinor(List<Connection> targets) {
            lock (stateLock) {
                return targets.Aggregate(DawApiVersion.Current.Minor, (min, conn) => Math.Min(min, conn.NegotiatedMinor));
            }
        }

        private int GetNegotiatedMinor(Connection conn) {
            lock (stateLock) {
                return conn.NegotiatedMinor;
            }
        }

        private Task<UpdateTracksNotification> BuildTracksAsync(int minor) {
            bool emitV12Fields = minor >= 2;
            return OnDocumentThreadAsync(() => new UpdateTracksNotification {
                Tracks = ProjectSource().tracks
                    .Select(track => new DawTrackInfo {
                        Name = track.TrackName,
                        Volume = track.Volume,
                        Pan = track.Pan,
                        // Kept on the wire for compatibility, but the bridge sends pre-fader
                        // audio and leaves gain, pan, mute and solo to the DAW mixer.
                        Muted = track.Muted,
                        // v1.2: informational fields for the plugin's GUI. Singer changes and
                        // renderer changes are TrackCommands, so the same Tracks sync keeps
                        // these fresh without new triggers. Omitted for lower minors (§10).
                        Singer = emitV12Fields ? track.Singer?.Name ?? string.Empty : null,
                        Engine = emitV12Fields ? track.RendererSettings.renderer ?? string.Empty : null,
                    })
                    .ToList(),
            });
        }

        private Task<UpdateProjectInfoNotification> BuildProjectInfoAsync() {
            return OnDocumentThreadAsync(() => {
                var project = ProjectSource();
                bool saved = !string.IsNullOrEmpty(project.FilePath);
                return new UpdateProjectInfoNotification {
                    Saved = saved,
                    Name = saved
                        ? System.IO.Path.GetFileNameWithoutExtension(project.FilePath)
                        : string.Empty,
                };
            });
        }

        /// <summary>A part's layout plus the signal it was mixed into, captured on the document thread.</summary>
        private sealed class PartSnapshot {
            public UProject Project = null!;
            public UVoicePart Part = null!;
            public int TrackNo;
            public double StartMs;
            public double EndMs;
        }

        private Task<List<PartSnapshot>> SnapshotPartsAsync() {
            return OnDocumentThreadAsync(() => {
                var project = ProjectSource();
                return project.parts
                    .OfType<UVoicePart>()
                    .Select(part => new PartSnapshot {
                        Project = project,
                        Part = part,
                        TrackNo = part.trackNo,
                        StartMs = project.timeAxis.TickPosToMsPos(part.position),
                        EndMs = project.timeAxis.TickPosToMsPos(part.End),
                    })
                    .ToList();
            });
        }

        /// <summary>
        /// Reports the layout, hashing each part's rendered audio on the way.
        /// </summary>
        /// <remarks>
        /// Parts whose render is unfinished are left out entirely rather than advertised with a
        /// placeholder hash, because <see cref="DawAudio.TryExtractPart"/> can only produce a
        /// correct hash for finished audio. <c>PartRenderedNotification</c> marks the stream dirty
        /// again, so they appear in a later sync. The hash owner map and the audio cache are
        /// shared between connections: every plugin is offered every track, and chooses its own
        /// (PROTOCOL.md §6.1).
        /// </remarks>
        private async Task SyncPartLayoutAsync(Connection conn, DawTransport live, CancellationToken cancellation) {
            var layout = await BuildPartLayoutAsync();
            var response = await live.SendRequestAsync<UpdatePartLayoutResponse>(
                DawMessageKind.UpdatePartLayout,
                new UpdatePartLayoutRequest { Parts = layout },
                cancellation: cancellation);
            if (response.MissingAudios.Count > 0) {
                // The plugin pulls each one itself with getAudio (§6.2); we just keep them warm.
                Log.Information(
                    $"DAW: plugin '{conn.Server.Name}' is missing " +
                    $"{response.MissingAudios.Count} of {layout.Count} part audios.");
            }
        }

        /// <summary>
        /// Builds the part layout payload — extracting, hashing and caching every rendered
        /// part — and updates the shared hash owner map. Done once per sync, never once per
        /// connection: a part can be tens of megabytes.
        /// </summary>
        private async Task<List<DawPartLayout>> BuildPartLayoutAsync() {
            var snapshot = await SnapshotPartsAsync();
            var layout = new List<DawPartLayout>(snapshot.Count);
            var owners = new Dictionary<string, UVoicePart>();
            foreach (var entry in snapshot) {
                if (!TryHashPart(entry, out string hash, out byte[] pcm)) {
                    continue;
                }
                owners[hash] = entry.Part;
                audioCache.Put(hash, pcm);
                layout.Add(new DawPartLayout {
                    TrackNo = entry.TrackNo,
                    StartMs = entry.StartMs,
                    EndMs = entry.EndMs,
                    AudioHash = hash,
                });
            }
            lock (stateLock) {
                hashOwners.Clear();
                foreach (var pair in owners) {
                    hashOwners[pair.Key] = pair.Value;
                }
            }
            audioCache.Retain(owners.Keys);
            return layout;
        }

        /// <summary>
        /// Extracts and hashes one part. Runs off the document thread on purpose: a part can be
        /// tens of megabytes, and concurrent reads of a part's mix are what playback already does.
        /// </summary>
        private static bool TryHashPart(PartSnapshot entry, out string hash, out byte[] pcm) {
            hash = string.Empty;
            pcm = Array.Empty<byte>();
            if (!DawAudio.TryExtractPart(entry.Project, entry.Part, out var samples)) {
                return false;
            }
            pcm = DawAudio.ToPcmBytes(samples);
            hash = DawAudio.FormatHash(DawAudio.Hash(pcm));
            return true;
        }

        /// <summary>
        /// Serves a plugin's requests. Only <c>getAudio</c> is inbound in v1 (§6.2), and it is
        /// answered with a data-plane frame rather than an envelope.
        /// </summary>
        private async Task ServeRequestAsync(DawInboundRequest request) {
            if (request.Kind != DawMessageKind.GetAudio) {
                await request.RespondAsync(DawResult.Fail($"Unsupported request '{request.Kind}'."));
                return;
            }
            string hash;
            try {
                hash = request.ReadPayload<GetAudioRequest>().Hash;
            } catch (Exception e) {
                await request.RespondAsync(DawResult.Fail(e.Message));
                return;
            }
            if (!TryResolveAudio(hash, out byte[] pcm)) {
                await request.RespondAsync(DawResult.Fail($"No audio for hash {hash}."));
                return;
            }
            if (pcm.Length > DawAudio.MaxFrameBytes) {
                // A frame above the bound would make the receiver refuse the header and stop the
                // transport (§6.1, §8); refuse here with an envelope instead of wedging the peer.
                await request.RespondAsync(DawResult.Fail(
                    $"Audio for hash {hash} is {pcm.Length} bytes, above the " +
                    $"{DawAudio.MaxFrameBytes}-byte data-plane bound."));
                return;
            }
            await request.RespondWithAudioAsync(hash, pcm);
        }

        /// <summary>
        /// Finds the audio behind an advertised hash: the cache first, then a re-extraction from
        /// the part that produced it. The re-extracted bytes still have to hash to the requested
        /// value — if they do not, the part was re-rendered and the plugin is asking about audio
        /// that no longer exists, which the next layout sync will correct.
        /// </summary>
        private bool TryResolveAudio(string hash, out byte[] pcm) {
            if (audioCache.TryGet(hash, out pcm)) {
                return true;
            }
            UVoicePart? part;
            lock (stateLock) {
                hashOwners.TryGetValue(hash, out part);
            }
            if (part == null) {
                return false;
            }
            var entry = new PartSnapshot { Project = ProjectSource(), Part = part };
            if (!TryHashPart(entry, out string actual, out var bytes) || actual != hash) {
                return false;
            }
            audioCache.Put(hash, bytes);
            pcm = bytes;
            return true;
        }

        private void OnPluginNotification(Connection conn, string kind, JsonElement? payload) {
            switch (kind) {
                case DawMessageKind.Ping:
                    // Liveness only — the transport already refreshed its heartbeat clock (§3).
                    break;
                case DawMessageKind.PlaybackStarted:
                    // §7: the DAW is about to play, so everything pending goes out now.
                    scheduler.FlushPending(NowUtc());
                    FireAndForget(PumpOnceAsync(), "playbackStarted flush");
                    break;
                case DawMessageKind.Playhead:
                    if (payload.HasValue) {
                        var note = payload.Value.Deserialize<PlayheadNotification>(DawJson.Options);
                        if (note != null) {
                            HandlePlayhead(note);
                        }
                    }
                    break;
                case DawMessageKind.Bpm:
                    if (payload.HasValue) {
                        var note = payload.Value.Deserialize<BpmNotification>(DawJson.Options);
                        if (note != null) {
                            HandleBpm(note.Bpm);
                        }
                    }
                    break;
                default:
                    Log.Information($"DAW: ignoring unknown notification '{kind}'.");
                    break;
            }
        }

        /// <summary>
        /// v1.1 one-way playhead sync: the DAW's position simply overwrites OpenUtau's. The
        /// reverse direction does not exist — OpenUtau never reports its own position.
        /// </summary>
        private void HandlePlayhead(PlayheadNotification note) {
            FireAndForget(OnDocumentThreadAsync(() => {
                var project = ProjectSource();
                int tick = Math.Max(0, project.timeAxis.MsPosToTickPos(note.PositionMs));
                if (Math.Abs(tick - DocManager.Inst.playPosTick) < PlayheadEpsilonTicks) {
                    return 0;
                }
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(tick));
                return 0;
            }), "playhead sync");
        }

        /// <summary>
        /// v1.1 tempo guard: without ARA there is no tempo-map sync, so a DAW tempo that does
        /// not match the project's first tempo is reported once per distinct mismatch instead
        /// of silently misaligning bars.
        /// </summary>
        private void HandleBpm(double dawBpm) {
            FireAndForget(OnDocumentThreadAsync(() => {
                var project = ProjectSource();
                double projectBpm = project.tempos.Count > 0 ? project.tempos[0].bpm : 120.0;
                if (Math.Abs(dawBpm - projectBpm) < 0.5) {
                    return 0;
                }
                if (dawBpm == lastDawBpm && projectBpm == lastWarnedProjectBpm) {
                    // Already told the user about exactly this mismatch.
                    return 0;
                }
                lastDawBpm = dawBpm;
                lastWarnedProjectBpm = projectBpm;
                string detail = $"DAW: {dawBpm:0.##} / OpenUtau: {projectBpm:0.##}";
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(
                    new MessageCustomizableException(
                        $"The DAW project's tempo ({dawBpm:0.##} BPM) does not match this " +
                        $"project's tempo ({projectBpm:0.##} BPM). Align them, or the DAW " +
                        $"timeline and OpenUtau's will only agree in seconds, not in bars.",
                        $"<translate:dawintegration.bpmmismatch> ({detail})",
                        new InvalidOperationException(), false)));
                return 0;
            }), "bpm check");
        }

        /// <summary>
        /// End of one connection. A close we asked for stays closed; anything else climbs the
        /// §3 backoff ladder for that connection alone.
        /// </summary>
        private void OnTransportDisconnected(Connection conn, DawDisconnectReason reason, string detail) {
            if (conn.ClosingLocally) {
                return;
            }
            lock (stateLock) {
                // The dead transport is left to the GC, as before the multi-instance split:
                // disposing it from inside its own read-loop callback risks a self-join.
                conn.Transport = null;
                conn.Reconnecting = true;
            }
            RecomputeState();
            FireAndForget(ReconnectAsync(conn, reason, detail), "reconnect");
        }

        /// <summary>
        /// Retries the same port on the §3 ladder — 500 ms, 1 s, 2 s. A plugin that really
        /// exited never answers, so the ladder ends and the user is told once.
        /// </summary>
        private async Task ReconnectAsync(Connection conn, DawDisconnectReason reason, string detail) {
            for (int attempt = 0; attempt < reconnectBackoff.Length; attempt++) {
                await Task.Delay(reconnectBackoff[attempt]);
                bool cancelled;
                lock (stateLock) {
                    cancelled = conn.ClosingLocally || !connections.Contains(conn);
                }
                if (cancelled || Volatile.Read(ref disposed) != 0) {
                    return;
                }
                try {
                    await OpenConnectionAsync(conn, CancellationToken.None);
                    lock (stateLock) {
                        conn.Reconnecting = false;
                    }
                    RecomputeState();
                    Log.Information(
                        $"DAW: reconnected to '{conn.Server.Name}' on attempt {attempt + 1}.");
                    return;
                } catch (Exception e) {
                    Log.Warning(e, $"DAW: reconnect attempt {attempt + 1} of {reconnectBackoff.Length} failed.");
                }
            }
            lock (stateLock) {
                connections.Remove(conn);
            }
            RecomputeState();
            StopPumpIfIdle();
            UnsubscribeIfIdle();
            ConnectionLost?.Invoke($"{conn.Server.Name}: {reason}: {detail}");
        }

        /// <summary>
        /// Drops one connection after a protocol-level failure, letting its disconnect handler
        /// start the reconnect ladder.
        /// </summary>
        private async Task DropConnectionAsync(Connection conn) {
            var live = conn.Transport;
            if (live == null) {
                return;
            }
            try {
                await live.CloseAsync();
            } catch (Exception e) {
                Log.Warning(e, "DAW: closing the transport failed.");
            }
        }

        /// <summary>
        /// User-initiated teardown of every connection: flush what is pending so the DAW is
        /// left holding the final state, then send the bare <c>close</c> (§9).
        /// </summary>
        public async Task DisconnectAsync() {
            List<Connection> snapshot;
            lock (stateLock) {
                snapshot = connections.ToList();
            }
            foreach (var conn in snapshot) {
                await CloseConnectionAsync(conn, finalSync: true);
            }
        }

        /// <summary>User-initiated teardown of the connection on one port, if it exists.</summary>
        public async Task DisconnectAsync(int port) {
            Connection? conn;
            lock (stateLock) {
                conn = connections.FirstOrDefault(c => c.Server.Port == port);
            }
            if (conn != null) {
                await CloseConnectionAsync(conn, finalSync: true);
            }
        }

        private async Task CloseConnectionAsync(Connection conn, bool finalSync) {
            conn.ClosingLocally = true;
            var live = conn.Transport;
            if (finalSync && live != null && live.IsConnected) {
                // Flush and drain the pending streams, so the due entries are consumed rather
                // than replayed by the next pump tick (§9: the DAW keeps the final state).
                scheduler.FlushPending(NowUtc());
                try {
                    foreach (var kind in scheduler.TryTake(NowUtc())) {
                        await SyncAsync(kind);
                    }
                } catch (Exception e) {
                    Log.Warning(e, "DAW: the final sync before closing failed.");
                }
            }
            if (live != null) {
                try {
                    await live.CloseAsync();
                } catch (Exception e) {
                    Log.Warning(e, "DAW: closing the transport failed.");
                }
                live.Dispose();
            }
            lock (stateLock) {
                conn.Transport = null;
                connections.Remove(conn);
            }
            RecomputeState();
            StopPumpIfIdle();
            UnsubscribeIfIdle();
        }

        private static void FireAndForget(Task task, string what) {
            task.ContinueWith(
                faulted => Log.Error(faulted.Exception!, $"DAW: {what} failed."),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Drops every connection without a final sync. Deliberately does no blocking wait:
        /// dispose runs on application shutdown, and a final sync needs the document thread,
        /// which would deadlock if that thread is the one disposing.
        /// </summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }
            List<Connection> snapshot;
            lock (stateLock) {
                snapshot = connections.ToList();
                connections.Clear();
                pump?.Dispose();
                pump = null;
                hashOwners.Clear();
            }
            foreach (var conn in snapshot) {
                conn.ClosingLocally = true;
                conn.Transport?.Dispose();
                conn.Transport = null;
            }
            // Same lock discipline as Subscribe/UnsubscribeIfIdle: a reconnect handshake racing
            // the dispose must not re-add the subscriber after it is removed here.
            lock (stateLock) {
                if (subscribed) {
                    DocManager.Inst.RemoveSubscriber(this);
                    subscribed = false;
                }
            }
            scheduler.Clear();
            audioCache.Clear();
            // Deliberately not disposing syncGate: StopPump does not join an in-flight timer
            // callback, and notifications start PumpOnceAsync without awaiting it, so a pump
            // can still enter WaitAsync/Release after this point. A SemaphoreSlim holds no
            // unmanaged state, so leaving it to the GC is safe; disposing it here could throw
            // ObjectDisposedException into an awaited DisconnectAsync on the UI thread.
            State = DawConnectionState.Disconnected;
        }
    }
}
