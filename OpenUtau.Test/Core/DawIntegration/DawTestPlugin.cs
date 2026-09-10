using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// The plugin half of the protocol, for tests and conformance runs: a loopback TCP server that
    /// publishes a discovery file, accepts one connection and answers on it.
    /// </summary>
    /// <remarks>
    /// Built on the same <see cref="DawTransport"/> as the OpenUtau side on purpose. A second,
    /// independent framing implementation here would only prove the two copies agree with each
    /// other; sharing it means these tests exercise the code that ships.
    /// </remarks>
    public sealed class DawTestPlugin : IDisposable {
        private readonly TcpListener listener;
        private readonly DawServerFinder finder;
        private TaskCompletionSource<DawTransport> connected =
            new TaskCompletionSource<DawTransport>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Every control message received, in arrival order — the §7 ordering assertion.</summary>
        public ConcurrentQueue<string> Received { get; } = new ConcurrentQueue<string>();

        /// <summary>Hash → PCM for everything pulled with <c>getAudio</c>.</summary>
        public Dictionary<string, byte[]> Pulled { get; } = new Dictionary<string, byte[]>();

        /// <summary>The baseline from <c>init</c>, then whatever <c>updateUstx</c> last carried.</summary>
        public string Ustx { get; private set; } = string.Empty;

        public List<DawPartLayout> Layout { get; private set; } = new List<DawPartLayout>();
        public List<DawTrackInfo> Tracks { get; private set; } = new List<DawTrackInfo>();

        /// <summary>What this plugin answers <c>init</c> with. Set before connecting to test §4.</summary>
        public string ApiVersion { get; set; } = DawApiVersion.CurrentString;

        public string Name { get; }
        public int Port { get; }
        public string DiscoveryPath { get; }
        public DawServerFinder Finder => finder;
        public DawTransport? Transport { get; private set; }

        private DawTestPlugin(TcpListener listener, DawServerFinder finder, string name, int port, string path) {
            this.listener = listener;
            this.finder = finder;
            Name = name;
            Port = port;
            DiscoveryPath = path;
        }

        /// <summary>Binds an ephemeral loopback port, advertises it and starts accepting.</summary>
        public static DawTestPlugin Start(
            string name, string discoveryDirectory, string? advertisedApiVersion = null) {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var finder = new DawServerFinder(discoveryDirectory);
            string path = finder.Publish(name, port, advertisedApiVersion ?? DawApiVersion.CurrentString);
            var plugin = new DawTestPlugin(listener, finder, name, port, path) {
                ApiVersion = advertisedApiVersion ?? DawApiVersion.CurrentString,
            };
            plugin.Accept();
            return plugin;
        }

        /// <summary>This plugin as OpenUtau discovers it — through a real directory scan (§4).</summary>
        public DawServer Advertisement =>
            finder.Scan(removeStale: false).Single(server => server.Port == Port);

        public Task<DawTransport> WaitForConnectionAsync(TimeSpan? timeout = null) =>
            connected.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(10));

        /// <summary>
        /// Arms a fresh connection wait, so a reconnect can be awaited without racing the one that
        /// is about to be dropped.
        /// </summary>
        public void ExpectReconnect() {
            connected = new TaskCompletionSource<DawTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>Kills the socket without a <c>close</c> — a wedged or crashed plugin.</summary>
        public void DropConnection() {
            Transport?.Dispose();
            Transport = null;
        }

        /// <summary>Stops answering entirely, so reconnection cannot succeed.</summary>
        public void StopListening() {
            try {
                listener.Stop();
            } catch (Exception) {
                // Already stopped.
            }
        }

        private void Accept() {
            _ = Task.Run(async () => {
                while (true) {
                    TcpClient client;
                    try {
                        client = await listener.AcceptTcpClientAsync();
                    } catch (Exception) {
                        // The listener was stopped; nothing more will connect.
                        return;
                    }
                    var transport = DawTransport.Adopt(client);
                    transport.RequestHandler = ServeAsync;
                    transport.Notification += OnNotification;
                    Transport = transport;
                    connected.TrySetResult(transport);
                }
            });
        }

        private void OnNotification(string kind, JsonElement? payload) {
            switch (kind) {
                case DawMessageKind.UpdateUstx:
                    Ustx = payload?.Deserialize<UpdateUstxNotification>(DawJson.Options)?.Ustx ?? string.Empty;
                    break;
                case DawMessageKind.UpdateTracks:
                    Tracks = payload?.Deserialize<UpdateTracksNotification>(DawJson.Options)?.Tracks
                        ?? new List<DawTrackInfo>();
                    break;
            }
            Received.Enqueue(kind);
        }

        private async Task ServeAsync(DawInboundRequest request) {
            switch (request.Kind) {
                case DawMessageKind.Init:
                    Ustx = request.ReadPayload<InitRequest>().Ustx;
                    Received.Enqueue(request.Kind);
                    await request.RespondAsync(DawResult.Ok(new InitResponse { ApiVersion = ApiVersion }));
                    break;
                case DawMessageKind.UpdatePartLayout:
                    Layout = request.ReadPayload<UpdatePartLayoutRequest>().Parts;
                    Received.Enqueue(request.Kind);
                    await request.RespondAsync(DawResult.Ok(new UpdatePartLayoutResponse {
                        MissingAudios = Layout
                            .Select(part => part.AudioHash)
                            .Where(hash => !Pulled.ContainsKey(hash))
                            .Distinct()
                            .ToList(),
                    }));
                    break;
                default:
                    Received.Enqueue(request.Kind);
                    await request.RespondAsync(DawResult.Fail($"Unsupported request '{request.Kind}'."));
                    break;
            }
        }

        /// <summary>
        /// Pulls every hash the last layout advertised and checks each frame against its header
        /// hash, which is the whole point of the data plane (§5.2, §6.2).
        /// </summary>
        public async Task PullLayoutAudioAsync() {
            var transport = Transport ?? throw new InvalidOperationException("Not connected yet.");
            foreach (string hash in Layout.Select(part => part.AudioHash).Distinct().ToList()) {
                byte[] pcm = await transport.GetAudioAsync(hash);
                Assert.Equal(hash, DawAudio.FormatHash(DawAudio.Hash(pcm)));
                Pulled[hash] = pcm;
            }
        }

        public Task SendPlaybackStartedAsync() =>
            Transport!.SendNotificationAsync(DawMessageKind.PlaybackStarted, new DawEmptyPayload());

        public Task SendPingAsync() =>
            Transport!.SendNotificationAsync(DawMessageKind.Ping, new DawEmptyPayload());

        /// <summary>Waits for a control message of this kind, polling instead of guessing a sleep.</summary>
        public Task WaitForAsync(string kind, TimeSpan? timeout = null) =>
            WaitForCountAsync(kind, 1, timeout);

        /// <summary>
        /// Waits until this kind has arrived at least <paramref name="count"/> times, which is how
        /// a *second* update is distinguished from the one the connect handshake already sent.
        /// </summary>
        public async Task WaitForCountAsync(string kind, int count, TimeSpan? timeout = null) {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (DateTime.UtcNow < deadline) {
                if (Received.Count(seen => seen == kind) >= count) {
                    return;
                }
                await Task.Delay(20);
            }
            throw new TimeoutException(
                $"Plugin saw '{kind}' {Received.Count(seen => seen == kind)} time(s), wanted {count}. " +
                $"Saw: {string.Join(", ", Received)}");
        }

        public void Dispose() {
            Transport?.Dispose();
            try {
                listener.Stop();
            } catch (Exception) {
                // Already torn down.
            }
            finder.Remove(DiscoveryPath);
        }
    }
}
