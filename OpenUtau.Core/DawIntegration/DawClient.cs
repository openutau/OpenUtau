using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace OpenUtau.Core.DawIntegration {
    public sealed class DawClient : IDisposable {
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(15);

        public readonly DawServer server;

        private readonly TcpClient tcpClient = new TcpClient();
        private readonly SemaphoreSlim writerSemaphore = new SemaphoreSlim(1, 1);
        private readonly object handlersLock = new object();
        private readonly Dictionary<string, Action<string>> handlers = new Dictionary<string, Action<string>>();
        private readonly Dictionary<string, TaskCompletionSource<string>> pendingRequests =
            new Dictionary<string, TaskCompletionSource<string>>();
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        private NetworkStream? stream;
        private Task? receiver;
        private Task? heartbeatMonitor;
        private long lastMessageTicks = DateTime.UtcNow.Ticks;
        private int disconnected;

        public event Action<DawClient, Exception?>? Disconnected;
        public event Action? PlaybackStarted;

        private DawClient(DawServer server) {
            this.server = server;
        }

        public static async Task<(DawClient, string)> Connect(
            DawServer server,
            CancellationToken cancellationToken = default) {
            var client = new DawClient(server);
            try {
                await client.tcpClient.ConnectAsync(
                    "127.0.0.1", server.Port, cancellationToken);
                client.stream = client.tcpClient.GetStream();
                client.RegisterNotification<PingNotification>("ping", _ => { });
                client.RegisterNotification<PlaybackStartedNotification>(
                    "playbackStarted", _ => client.PlaybackStarted?.Invoke());
                client.receiver = client.StartReceiver(client.lifetimeCancellation.Token);
                client.heartbeatMonitor = client.MonitorHeartbeat(client.lifetimeCancellation.Token);

                using var initTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                initTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var initMessage = await client.SendRequest<InitResponse>(
                    new InitRequest(), initTimeout.Token);
                return (client, initMessage.ustx);
            } catch {
                client.Disconnect();
                throw;
            }
        }

        private async Task StartReceiver(CancellationToken token) {
            if (stream == null) {
                throw new InvalidOperationException("DAW stream is not initialized.");
            }

            Exception? disconnectError = null;
            var currentMessageBuffer = new List<byte>();
            var buffer = new byte[16 * 1024];
            try {
                while (!token.IsCancellationRequested) {
                    var bytesRead = await stream.ReadAsync(buffer, token);
                    if (bytesRead == 0) {
                        throw new EndOfStreamException("DAW closed the connection.");
                    }
                    Interlocked.Exchange(ref lastMessageTicks, DateTime.UtcNow.Ticks);
                    for (var i = 0; i < bytesRead; ++i) {
                        if (buffer[i] == (byte)'\n') {
                            HandleMessage(Encoding.UTF8.GetString(currentMessageBuffer.ToArray()));
                            currentMessageBuffer.Clear();
                        } else {
                            currentMessageBuffer.Add(buffer[i]);
                        }
                    }
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            } catch (Exception e) {
                disconnectError = e;
            } finally {
                DisconnectCore(disconnectError);
            }
        }

        private void HandleMessage(string message) {
            var parts = message.Split(' ', 2);
            if (parts.Length != 2) {
                throw new InvalidDataException("Malformed DAW message.");
            }

            Action<string>? notificationHandler = null;
            TaskCompletionSource<string>? requestHandler = null;
            lock (handlersLock) {
                if (handlers.TryGetValue(parts[0], out var handler)) {
                    notificationHandler = handler;
                } else if (pendingRequests.Remove(parts[0], out var pending)) {
                    requestHandler = pending;
                }
            }

            if (notificationHandler != null) {
                notificationHandler(parts[1]);
            } else if (requestHandler != null) {
                requestHandler.TrySetResult(parts[1]);
            } else {
                Log.Warning("Unhandled DAW message: {Kind}", parts[0]);
            }
        }

        private async Task MonitorHeartbeat(CancellationToken token) {
            try {
                while (!token.IsCancellationRequested) {
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
                    var lastMessage = new DateTime(
                        Interlocked.Read(ref lastMessageTicks), DateTimeKind.Utc);
                    if (DateTime.UtcNow - lastMessage > HeartbeatTimeout) {
                        DisconnectCore(new TimeoutException("DAW heartbeat timed out."));
                        return;
                    }
                }
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
            }
        }

        private async Task SendMessage(
            string header,
            DawMessage data,
            CancellationToken cancellationToken) {
            if (stream == null || Volatile.Read(ref disconnected) != 0) {
                throw new IOException("DAW is disconnected.");
            }

            var message = Encoding.UTF8.GetBytes(
                $"{header} {JsonConvert.SerializeObject(data)}\n");
            await writerSemaphore.WaitAsync(cancellationToken);
            try {
                await stream.WriteAsync(message, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            } catch (Exception e) when (
                e is IOException || e is SocketException || e is ObjectDisposedException) {
                DisconnectCore(e);
                throw;
            } finally {
                writerSemaphore.Release();
            }
        }

        public async Task<T> SendRequest<T>(
            DawDawRequest data,
            CancellationToken cancellationToken = default) where T : DawDawResponse {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DefaultRequestTimeout);

            var uuid = Guid.NewGuid().ToString();
            var responseKind = $"response:{uuid}";
            var responseSource = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (handlersLock) {
                if (Volatile.Read(ref disconnected) != 0) {
                    throw new IOException("DAW is disconnected.");
                }
                pendingRequests.Add(responseKind, responseSource);
            }

            using var registration = timeout.Token.Register(() =>
                responseSource.TrySetException(new TimeoutException(
                    $"DAW request '{data.kind}' timed out.")));
            try {
                await SendMessage($"request:{uuid}:{data.kind}", data, timeout.Token);
                var message = await responseSource.Task;
                var result = JsonConvert.DeserializeObject<DawResult<T>>(message)
                    ?? throw new InvalidDataException("Invalid DAW response.");
                if (!result.success) {
                    throw new InvalidOperationException(
                        $"DAW returned error to request {data.kind}: {result.error}");
                }
                return result.data
                    ?? throw new InvalidDataException("DAW response did not contain data.");
            } catch (TimeoutException e) {
                DisconnectCore(e);
                throw;
            } finally {
                lock (handlersLock) {
                    pendingRequests.Remove(responseKind);
                }
            }
        }

        public Task SendNotification(
            DawDawNotification data,
            CancellationToken cancellationToken = default) {
            return SendMessage($"notification:{data.kind}", data, cancellationToken);
        }

        public void RegisterNotification<T>(string kind, Action<T> handler)
            where T : DawOuNotification {
            lock (handlersLock) {
                handlers[$"notification:{kind}"] = message => {
                    var notification = JsonConvert.DeserializeObject<T>(message)
                        ?? throw new InvalidDataException($"Invalid {kind} notification.");
                    handler(notification);
                };
            }
        }

        public void Disconnect() {
            DisconnectCore(null);
        }

        private void DisconnectCore(Exception? error) {
            if (Interlocked.Exchange(ref disconnected, 1) != 0) {
                return;
            }

            lifetimeCancellation.Cancel();
            try {
                tcpClient.Close();
            } catch {
            }

            List<TaskCompletionSource<string>> pending;
            lock (handlersLock) {
                pending = new List<TaskCompletionSource<string>>(pendingRequests.Values);
                pendingRequests.Clear();
            }
            var disconnectException = error ?? new IOException("DAW disconnected.");
            foreach (var request in pending) {
                request.TrySetException(disconnectException);
            }
            Disconnected?.Invoke(this, error);
        }

        public void Dispose() {
            Disconnect();
            lifetimeCancellation.Dispose();
            writerSemaphore.Dispose();
        }
    }
}
