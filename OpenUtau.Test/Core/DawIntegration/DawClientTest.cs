using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenUtau.Core.DawIntegration;
using Xunit;

namespace OpenUtau.Test.Core.DawIntegration {
    public class DawClientTest {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task ConnectHandlesFragmentedAndCombinedMessages() {
            await using var server = new FakeDawServer(async (stream, _) => {
                var request = await ReadLine(stream);
                var response = InitResponseFor(request, "test-ustx");
                var split = response.Length / 2;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response[..split]));
                await Task.Delay(20);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(
                    response[split..] + "notification:ping {}\n"));
            });

            var (client, ustx) = await DawClient.Connect(server.Server);
            using (client) {
                Assert.Equal("test-ustx", ustx);
            }
        }

        [Fact]
        public async Task ConnectHandlesPingNotification() {
            await using var server = new FakeDawServer(async (stream, _) => {
                var request = await ReadLine(stream);
                var requestId = request.Split(':', 3)[1];
                await stream.WriteAsync(Encoding.UTF8.GetBytes(
                    $"response:{requestId} {{\"success\":true,\"data\":{{\"ustx\":\"\"}},\"error\":null}}\n"));
                await stream.WriteAsync(Encoding.UTF8.GetBytes(
                    "notification:ping {}\n"));
                await Task.Delay(100);
            });

            var (client, _) = await DawClient.Connect(server.Server);
            using (client) {
                await Task.Delay(150);
            }
        }

        [Fact]
        public async Task RaisesPlaybackStartedNotification() {
            var sendPlaybackStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new FakeDawServer(async (stream, _) => {
                await CompleteHandshake(stream);
                await sendPlaybackStarted.Task;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(
                    "notification:playbackStarted {}\n"));
                await Task.Delay(100);
            });

            var (client, _) = await DawClient.Connect(server.Server);
            using (client) {
                var playbackStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                client.PlaybackStarted += () => playbackStarted.TrySetResult();
                sendPlaybackStarted.TrySetResult();
                await playbackStarted.Task.WaitAsync(TestTimeout);
            }
        }

        [Fact]
        public async Task RequestCancellationRemovesPendingRequest() {
            var requestReceived = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new FakeDawServer(async (stream, token) => {
                await CompleteHandshake(stream);
                await ReadLine(stream);
                requestReceived.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            });

            var (client, _) = await DawClient.Connect(server.Server);
            using (client) {
                using var cancellation = new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAsync<TimeoutException>(() =>
                    client.SendRequest<InitResponse>(
                        new InitRequest(), cancellation.Token));
                await requestReceived.Task.WaitAsync(TestTimeout);
            }
        }

        [Fact]
        public async Task EofCompletesPendingRequestAndNotifiesOnce() {
            var closeConnection = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new FakeDawServer(async (stream, _) => {
                await CompleteHandshake(stream);
                await ReadLine(stream);
                closeConnection.TrySetResult();
            });

            var (client, _) = await DawClient.Connect(server.Server);
            using (client) {
                var disconnected = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var disconnectCount = 0;
                client.Disconnected += (_, _) => {
                    Interlocked.Increment(ref disconnectCount);
                    disconnected.TrySetResult();
                };

                var request = client.SendRequest<InitResponse>(new InitRequest());
                await closeConnection.Task.WaitAsync(TestTimeout);
                await Assert.ThrowsAnyAsync<Exception>(() => request);
                await disconnected.Task.WaitAsync(TestTimeout);

                client.Disconnect();
                Assert.Equal(1, Volatile.Read(ref disconnectCount));
            }
        }

        private static async Task CompleteHandshake(NetworkStream stream) {
            var request = await ReadLine(stream);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(
                InitResponseFor(request, "")));
        }

        private static string InitResponseFor(string request, string ustx) {
            var header = request.Split(' ', 2)[0];
            var parts = header.Split(':', 3);
            return $"response:{parts[1]} " +
                JsonConvert.SerializeObject(new {
                    success = true,
                    data = new { ustx },
                    error = string.Empty,
                }) + "\n";
        }

        private static async Task<string> ReadLine(NetworkStream stream) {
            using var buffer = new MemoryStream();
            var singleByte = new byte[1];
            while (true) {
                var read = await stream.ReadAsync(singleByte);
                if (read == 0) {
                    throw new EndOfStreamException();
                }
                if (singleByte[0] == (byte)'\n') {
                    return Encoding.UTF8.GetString(buffer.ToArray());
                }
                buffer.WriteByte(singleByte[0]);
            }
        }

        private sealed class FakeDawServer : IAsyncDisposable {
            private readonly TcpListener listener;
            private readonly CancellationTokenSource cancellation = new();
            private readonly Task serverTask;

            public DawServer Server { get; }

            public FakeDawServer(
                Func<NetworkStream, CancellationToken, Task> handler) {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Server = JsonConvert.DeserializeObject<DawServer>(
                    $"{{\"port\":{port},\"name\":\"test\"}}")!;
                serverTask = Run(handler);
            }

            private async Task Run(
                Func<NetworkStream, CancellationToken, Task> handler) {
                try {
                    using var tcpClient = await listener.AcceptTcpClientAsync(
                        cancellation.Token);
                    await handler(tcpClient.GetStream(), cancellation.Token);
                } catch (OperationCanceledException)
                    when (cancellation.IsCancellationRequested) {
                }
            }

            public async ValueTask DisposeAsync() {
                cancellation.Cancel();
                listener.Stop();
                try {
                    await serverTask;
                } catch (Exception) when (cancellation.IsCancellationRequested) {
                }
                cancellation.Dispose();
            }
        }
    }
}
