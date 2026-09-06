using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>Why a <see cref="DawTransport"/> stopped. Drives the reconnect decision (PROTOCOL.md §8).</summary>
    public enum DawDisconnectReason {
        /// <summary>The plugin sent a bare <c>close</c>.</summary>
        PluginClosed,
        /// <summary>Nothing arrived within the heartbeat dead threshold.</summary>
        HeartbeatTimeout,
        /// <summary>Framing violation. Never retried without a fresh handshake.</summary>
        ProtocolError,
        /// <summary>A control request outlived its timeout budget, so the peer is wedged (§8).</summary>
        RequestTimeout,
        /// <summary>Socket ended or faulted.</summary>
        StreamClosed,
        /// <summary>We asked to close.</summary>
        LocalClose,
    }

    /// <summary>Tunable protocol timings. Defaults are the PROTOCOL.md §3 values; tests shorten them.</summary>
    public sealed class DawTransportOptions {
        public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan HeartbeatPollInterval { get; init; } = TimeSpan.FromSeconds(2);
        public TimeSpan HeartbeatDeadThreshold { get; init; } = TimeSpan.FromSeconds(15);
        public static DawTransportOptions Default { get; } = new DawTransportOptions();
    }

    /// <summary>
    /// Reads the two planes off one socket: LF-terminated UTF-8 control lines and
    /// exact-length binary payloads, sharing a single buffer so a data frame that is
    /// partially buffered behind its header is not lost (PROTOCOL.md §5).
    /// </summary>
    internal sealed class DawFrameReader {
        /// <summary>Guards against a peer that never sends a newline. USTX lines are legitimately multi-MB.</summary>
        private const int MaxLineBytes = 256 * 1024 * 1024;

        private readonly Stream stream;
        private byte[] buffer = new byte[16 * 1024];
        private int start;
        private int end;

        public DawFrameReader(Stream stream) {
            this.stream = stream;
        }

        /// <summary>Returns the next control line, or null at a clean end of stream.</summary>
        public async Task<string?> ReadLineAsync(CancellationToken cancellation) {
            while (true) {
                int newline = Array.IndexOf(buffer, (byte)'\n', start, end - start);
                if (newline >= 0) {
                    int length = newline - start;
                    // Tolerate CRLF from hand-written or Windows-side test peers.
                    if (length > 0 && buffer[start + length - 1] == (byte)'\r') {
                        length--;
                    }
                    var line = Encoding.UTF8.GetString(buffer, start, length);
                    start = newline + 1;
                    return line;
                }
                if (end - start >= MaxLineBytes) {
                    throw new DawProtocolException($"Control line exceeded {MaxLineBytes} bytes without a newline.");
                }
                if (!await FillAsync(cancellation)) {
                    if (end > start) {
                        throw new DawProtocolException($"Stream ended mid-line with {end - start} bytes buffered.");
                    }
                    return null;
                }
            }
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes of a data-plane payload.</summary>
        public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken cancellation) {
            var result = new byte[count];
            int copied = 0;
            while (copied < count) {
                int available = Math.Min(end - start, count - copied);
                if (available > 0) {
                    Buffer.BlockCopy(buffer, start, result, copied, available);
                    start += available;
                    copied += available;
                    continue;
                }
                if (!await FillAsync(cancellation)) {
                    throw new DawProtocolException(
                        $"Stream ended after {copied} of {count} payload bytes.");
                }
            }
            return result;
        }

        private async Task<bool> FillAsync(CancellationToken cancellation) {
            if (start == end) {
                start = 0;
                end = 0;
            } else if (end == buffer.Length) {
                if (start > 0) {
                    Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                    end -= start;
                    start = 0;
                } else {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }
            }
            int read = await stream.ReadAsync(buffer.AsMemory(end, buffer.Length - end), cancellation);
            if (read <= 0) {
                return false;
            }
            end += read;
            return true;
        }
    }

    /// <summary>
    /// An inbound <c>request:&lt;uuid&gt;:&lt;kind&gt;</c>. Exactly one of the respond methods must be
    /// called: <c>getAudio</c> answers with a data-plane frame, every other kind with an envelope
    /// (PROTOCOL.md §5.1, §6.1).
    /// </summary>
    public sealed class DawInboundRequest {
        private readonly DawTransport transport;
        private int answered;

        public string Uuid { get; }
        public string Kind { get; }
        public JsonElement? Payload { get; }

        internal DawInboundRequest(DawTransport transport, string uuid, string kind, JsonElement? payload) {
            this.transport = transport;
            Uuid = uuid;
            Kind = kind;
            Payload = payload;
        }

        internal bool IsAnswered => Volatile.Read(ref answered) != 0;

        public Task RespondAsync(DawResult result, CancellationToken cancellation = default) {
            Claim();
            return transport.SendLineAsync($"response:{Uuid} {DawJson.Serialize(result)}", cancellation);
        }

        /// <summary>Answers with the binary frame that <c>getAudio</c> expects instead of an envelope.</summary>
        public Task RespondWithAudioAsync(string hash, byte[] pcm, CancellationToken cancellation = default) {
            Claim();
            return transport.SendAudioFrameAsync(hash, pcm, cancellation);
        }

        public T ReadPayload<T>() {
            if (Payload == null) {
                throw new DawProtocolException($"Request '{Kind}' carried no payload.");
            }
            return Payload.Value.Deserialize<T>(DawJson.Options)
                ?? throw new DawProtocolException($"Request '{Kind}' payload could not be read as {typeof(T).Name}.");
        }

        private void Claim() {
            if (Interlocked.Exchange(ref answered, 1) != 0) {
                throw new InvalidOperationException($"Request {Uuid} ({Kind}) was already answered.");
            }
        }
    }

    /// <summary>
    /// One plugin connection: read loop, request correlation, write mutex and heartbeat
    /// liveness (PROTOCOL.md §3, §5). Symmetric on purpose — both OpenUtau and a test peer
    /// playing the plugin drive the same class, so the framing can only be implemented once.
    /// </summary>
    public sealed class DawTransport : IDisposable {
        /// <summary>Every inbound notification, including kinds this version does not know (§5.1).</summary>
        public event Action<string, JsonElement?>? Notification;

        /// <summary>Raised once when the connection ends, whatever the cause.</summary>
        public event Action<DawDisconnectReason, string>? Disconnected;

        /// <summary>Serves inbound requests. Unset means every request is refused with a failed envelope.</summary>
        public Func<DawInboundRequest, Task>? RequestHandler { get; set; }

        public DawTransportOptions Options { get; }
        public bool IsConnected => Volatile.Read(ref stopped) == 0;

        private readonly TcpClient client;
        private readonly Stream stream;
        private readonly DawFrameReader reader;
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<DawResult>> pendingRequests
            = new ConcurrentDictionary<string, TaskCompletionSource<DawResult>>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> pendingAudio
            = new ConcurrentDictionary<string, TaskCompletionSource<byte[]>>();
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly Func<DateTime> nowUtc;
        private long lastMessageUtcTicks;
        private int stopped;
        private int disposed;

        private DawTransport(TcpClient client, DawTransportOptions options, Func<DateTime>? nowUtc) {
            this.client = client;
            this.nowUtc = nowUtc ?? (() => DateTime.UtcNow);
            Options = options;
            stream = client.GetStream();
            reader = new DawFrameReader(stream);
            lastMessageUtcTicks = this.nowUtc().Ticks;
        }

        /// <summary>Connects to a plugin listening on loopback and starts the read + heartbeat loops.</summary>
        public static async Task<DawTransport> ConnectAsync(
            int port,
            DawTransportOptions? options = null,
            Func<DateTime>? nowUtc = null,
            CancellationToken cancellation = default) {
            var client = new TcpClient();
            try {
                client.NoDelay = true;
                await client.ConnectAsync(IPAddress.Loopback, port, cancellation);
            } catch {
                client.Dispose();
                throw;
            }
            return Adopt(client, options, nowUtc);
        }

        /// <summary>Wraps an already-accepted socket. Used by the peer side in tests and conformance tooling.</summary>
        public static DawTransport Adopt(
            TcpClient client,
            DawTransportOptions? options = null,
            Func<DateTime>? nowUtc = null) {
            client.NoDelay = true;
            var transport = new DawTransport(client, options ?? DawTransportOptions.Default, nowUtc);
            transport.Start();
            return transport;
        }

        private void Start() {
            _ = Task.Run(() => ReadLoopAsync(shutdown.Token));
            _ = Task.Run(() => HeartbeatLoopAsync(shutdown.Token));
        }

        private async Task ReadLoopAsync(CancellationToken cancellation) {
            try {
                while (!cancellation.IsCancellationRequested) {
                    string? line = await reader.ReadLineAsync(cancellation);
                    if (line == null) {
                        Stop(DawDisconnectReason.StreamClosed, "Peer closed the socket.");
                        return;
                    }
                    Volatile.Write(ref lastMessageUtcTicks, nowUtc().Ticks);
                    if (DawAudio.IsFrameHeader(line)) {
                        await ReadAudioFrameAsync(line, cancellation);
                    } else {
                        DispatchControl(line);
                    }
                }
            } catch (OperationCanceledException) {
                // Local shutdown.
            } catch (DawProtocolException e) {
                Stop(DawDisconnectReason.ProtocolError, e.Message);
            } catch (Exception e) {
                Stop(DawDisconnectReason.StreamClosed, e.Message);
            }
        }

        private async Task ReadAudioFrameAsync(string header, CancellationToken cancellation) {
            if (!DawAudio.TryParseFrameHeader(header, out string hash, out int length)) {
                throw new DawProtocolException($"Malformed audio frame header: '{header}'.");
            }
            // A short read here is unrecoverable: the stream position is lost (§8).
            byte[] payload = await reader.ReadExactlyAsync(length, cancellation);
            // §5.2: the header names the hash of the payload that follows. A peer that labels
            // unrelated bytes with the requested hash would otherwise be served as a cache hit,
            // so the payload is verified before any waiter is completed.
            if (!string.Equals(DawAudio.FormatHash(DawAudio.Hash(payload)), hash, StringComparison.Ordinal)) {
                throw new DawProtocolException($"Audio payload does not match its declared hash {hash}.");
            }
            if (pendingAudio.TryRemove(hash, out var waiter)) {
                waiter.TrySetResult(payload);
                return;
            }
            Log.Warning($"DAW: unsolicited audio frame for hash {hash} ({payload.Length} bytes), dropped.");
        }

        private void DispatchControl(string line) {
            if (line == "close") {
                Stop(DawDisconnectReason.PluginClosed, "Peer sent close.");
                return;
            }
            int space = line.IndexOf(' ');
            string header = space < 0 ? line : line.Substring(0, space);
            string payload = space < 0 ? string.Empty : line.Substring(space + 1);
            if (header.StartsWith("response:", StringComparison.Ordinal)) {
                CompleteResponse(header.Substring("response:".Length), payload);
            } else if (header.StartsWith("notification:", StringComparison.Ordinal)) {
                RaiseNotification(header.Substring("notification:".Length), payload);
            } else if (header.StartsWith("request:", StringComparison.Ordinal)) {
                _ = ServeRequestAsync(header, payload);
            } else {
                // Malformed control lines are logged and survived, never fatal (§8).
                Log.Warning($"DAW: unrecognized control header '{header}', ignored.");
            }
        }

        private void CompleteResponse(string uuid, string payload) {
            if (!pendingRequests.TryRemove(uuid, out var waiter)) {
                Log.Warning($"DAW: response for unknown request {uuid}, ignored.");
                return;
            }
            try {
                var result = JsonSerializer.Deserialize<DawResult>(payload, DawJson.Options);
                if (result == null) {
                    throw new DawProtocolException($"Empty response envelope for request {uuid}.");
                }
                waiter.TrySetResult(result);
            } catch (JsonException e) {
                waiter.TrySetException(new DawProtocolException($"Malformed response envelope for request {uuid}.", e));
            }
        }

        private void RaiseNotification(string kind, string payload) {
            var data = TryParsePayload(kind, payload);
            try {
                Notification?.Invoke(kind, data);
            } catch (Exception e) {
                Log.Error(e, $"DAW: handler for notification:{kind} threw.");
            }
        }

        private async Task ServeRequestAsync(string header, string payload) {
            // request:<uuid>:<kind> — the kind may itself contain ':', so split only twice.
            var parts = header.Split(':', 3);
            if (parts.Length < 3 || parts[1].Length == 0 || parts[2].Length == 0) {
                Log.Warning($"DAW: malformed request header '{header}', ignored.");
                return;
            }
            var request = new DawInboundRequest(this, parts[1], parts[2], TryParsePayload(parts[2], payload));
            try {
                if (RequestHandler == null) {
                    await request.RespondAsync(DawResult.Fail($"Unsupported request kind: {request.Kind}"));
                    return;
                }
                await RequestHandler(request);
                if (!request.IsAnswered) {
                    // A handler that returns without answering would hang the peer until its timeout.
                    await request.RespondAsync(DawResult.Fail($"Request '{request.Kind}' produced no response."));
                }
            } catch (Exception e) {
                Log.Error(e, $"DAW: serving request '{request.Kind}' failed.");
                if (!request.IsAnswered) {
                    try {
                        await request.RespondAsync(DawResult.Fail(e.Message));
                    } catch (Exception sendError) {
                        Log.Warning(sendError, $"DAW: could not report failure of '{request.Kind}'.");
                    }
                }
            }
        }

        private static JsonElement? TryParsePayload(string kind, string payload) {
            if (string.IsNullOrWhiteSpace(payload)) {
                return null;
            }
            try {
                using var document = JsonDocument.Parse(payload);
                return document.RootElement.Clone();
            } catch (JsonException e) {
                Log.Warning(e, $"DAW: malformed JSON payload on '{kind}', treated as empty.");
                return null;
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken cancellation) {
            try {
                while (!cancellation.IsCancellationRequested) {
                    await Task.Delay(Options.HeartbeatPollInterval, cancellation);
                    var last = new DateTime(Volatile.Read(ref lastMessageUtcTicks), DateTimeKind.Utc);
                    var silence = nowUtc() - last;
                    if (silence >= Options.HeartbeatDeadThreshold) {
                        Stop(DawDisconnectReason.HeartbeatTimeout,
                            $"No message from peer for {silence.TotalSeconds:F1}s.");
                        return;
                    }
                }
            } catch (OperationCanceledException) {
                // Local shutdown.
            }
        }

        /// <summary>Sends a request and awaits its envelope. A timeout means the connection is dead (§8).</summary>
        public async Task<DawResult> SendRequestAsync(
            string kind,
            object payload,
            TimeSpan? timeout = null,
            CancellationToken cancellation = default) {
            string uuid = Guid.NewGuid().ToString();
            var waiter = new TaskCompletionSource<DawResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingRequests[uuid] = waiter;
            try {
                await SendLineAsync($"request:{uuid}:{kind} {DawJson.Serialize(payload)}", cancellation);
                return await AwaitAsync(waiter.Task, timeout ?? Options.RequestTimeout, $"{kind} request", cancellation);
            } finally {
                pendingRequests.TryRemove(uuid, out _);
            }
        }

        /// <summary>Sends a request and unwraps its <c>data</c> payload, throwing on a failed envelope.</summary>
        public async Task<T> SendRequestAsync<T>(
            string kind,
            object payload,
            TimeSpan? timeout = null,
            CancellationToken cancellation = default) {
            var result = await SendRequestAsync(kind, payload, timeout, cancellation);
            return result.Unwrap<T>();
        }

        public Task SendNotificationAsync(string kind, object payload, CancellationToken cancellation = default) {
            return SendLineAsync($"notification:{kind} {DawJson.Serialize(payload)}", cancellation);
        }

        /// <summary>
        /// Pulls one audio payload by hash. The reply is a data-plane frame, so it is correlated
        /// by hash rather than uuid; a failed envelope on the same uuid also completes the wait,
        /// so a refusing peer costs nothing instead of a full timeout.
        /// </summary>
        public async Task<byte[]> GetAudioAsync(
            string hash,
            TimeSpan? timeout = null,
            CancellationToken cancellation = default) {
            string uuid = Guid.NewGuid().ToString();
            var frame = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var envelope = new TaskCompletionSource<DawResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pendingAudio.TryAdd(hash, frame)) {
                throw new InvalidOperationException($"An audio pull for hash {hash} is already outstanding.");
            }
            pendingRequests[uuid] = envelope;
            try {
                var request = new GetAudioRequest { Hash = hash };
                await SendLineAsync(
                    $"request:{uuid}:{DawMessageKind.GetAudio} {DawJson.Serialize(request)}", cancellation);
                var winner = await AwaitAsync(
                    Task.WhenAny(frame.Task, envelope.Task), timeout ?? Options.RequestTimeout, "getAudio", cancellation);
                if (winner == envelope.Task) {
                    var result = await envelope.Task;
                    throw new DawProtocolException(
                        $"Peer refused getAudio for {hash}: {result.Error ?? "(no error given)"}");
                }
                return await frame.Task;
            } finally {
                pendingAudio.TryRemove(hash, out _);
                pendingRequests.TryRemove(uuid, out _);
            }
        }

        /// <summary>
        /// Writes a data-plane frame: header line then exactly <c>pcm.Length</c> bytes, under the
        /// same write mutex so no control line can interleave into the payload (§5.2). The two
        /// writes are separate: copying header and payload into one buffer would double the peak
        /// memory of a legal 256 MiB frame.
        /// </summary>
        /// <exception cref="DawProtocolException">The payload exceeds the data-plane bound (§6.1).</exception>
        public async Task SendAudioFrameAsync(string hash, byte[] pcm, CancellationToken cancellation = default) {
            if (pcm.Length > DawAudio.MaxFrameBytes) {
                throw new DawProtocolException(
                    $"Audio frame of {pcm.Length} bytes exceeds the {DawAudio.MaxFrameBytes}-byte data-plane bound.");
            }
            var header = DawAudio.BuildFrameHeader(hash, pcm.Length);
            if (!IsConnected) {
                throw new DawProtocolException("Connection is closed.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
            await writeLock.WaitAsync(linked.Token);
            try {
                await stream.WriteAsync(header, linked.Token);
                await stream.WriteAsync(pcm, linked.Token);
                await stream.FlushAsync(linked.Token);
            } finally {
                writeLock.Release();
            }
        }

        /// <summary>User-initiated teardown: bare <c>close</c>, then stop (§5.1, §9).</summary>
        public async Task CloseAsync() {
            if (IsConnected) {
                try {
                    await SendLineAsync("close");
                } catch (Exception e) {
                    Log.Warning(e, "DAW: could not send close; tearing down anyway.");
                }
            }
            Stop(DawDisconnectReason.LocalClose, "Closed locally.");
        }

        internal Task SendLineAsync(string line, CancellationToken cancellation = default) {
            return WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellation);
        }

        private async Task WriteAsync(byte[] bytes, CancellationToken cancellation) {
            if (!IsConnected) {
                throw new DawProtocolException("Connection is closed.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
            await writeLock.WaitAsync(linked.Token);
            try {
                await stream.WriteAsync(bytes, linked.Token);
                await stream.FlushAsync(linked.Token);
            } finally {
                writeLock.Release();
            }
        }

        private async Task<T> AwaitAsync<T>(Task<T> task, TimeSpan timeout, string what, CancellationToken cancellation) {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
            try {
                return await task.WaitAsync(timeout, linked.Token);
            } catch (TimeoutException) {
                // §8: a peer that never answers is wedged — it can keep pinging forever without
                // ever answering a request. Stop the transport so the heartbeat cannot mask the
                // wedge and reconnect handling starts now, then report the timeout to the caller.
                Stop(DawDisconnectReason.RequestTimeout, $"{what} timed out after {timeout.TotalSeconds:F1}s.");
                throw new TimeoutException($"{what} timed out after {timeout.TotalSeconds:F1}s.");
            } catch (OperationCanceledException) when (shutdown.IsCancellationRequested && !cancellation.IsCancellationRequested) {
                throw new DawProtocolException($"Connection closed while waiting for {what}.");
            }
        }

        /// <summary>Ends the connection once and fails every outstanding wait so no caller hangs.</summary>
        private void Stop(DawDisconnectReason reason, string detail) {
            if (Interlocked.Exchange(ref stopped, 1) != 0) {
                return;
            }
            Log.Information($"DAW: disconnected ({reason}): {detail}");
            shutdown.Cancel();
            var error = new DawProtocolException($"Connection ended ({reason}): {detail}");
            foreach (var uuid in pendingRequests.Keys) {
                if (pendingRequests.TryRemove(uuid, out var waiter)) {
                    waiter.TrySetException(error);
                }
            }
            foreach (var hash in pendingAudio.Keys) {
                if (pendingAudio.TryRemove(hash, out var waiter)) {
                    waiter.TrySetException(error);
                }
            }
            try {
                client.Close();
            } catch (Exception e) {
                Log.Warning(e, "DAW: closing the socket failed.");
            }
            try {
                Disconnected?.Invoke(reason, detail);
            } catch (Exception e) {
                Log.Error(e, "DAW: Disconnected handler threw.");
            }
        }

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) != 0) {
                return;
            }
            Stop(DawDisconnectReason.LocalClose, "Disposed.");
            shutdown.Dispose();
            writeLock.Dispose();
            client.Dispose();
        }
    }
}
