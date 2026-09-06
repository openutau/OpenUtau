using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// Framing, correlation, timeout and heartbeat behaviour over a real loopback socket
    /// (PROTOCOL.md §3, §5, §8).
    /// </summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawTransportTest : IDisposable {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly List<IDisposable> owned = new List<IDisposable>();

        /// <summary>A mutable clock, so heartbeat death is provoked without waiting 15 s.</summary>
        private sealed class Clock {
            public DateTime Now;

            public Clock(DateTime now) {
                Now = now;
            }

            public DateTime Read() => Now;
        }

        /// <summary>
        /// A connected pair: <c>openutau</c> is the side that dials, <c>plugin</c> the side that
        /// listens. Both are the shipping transport, so the framing is only implemented once.
        /// </summary>
        /// <remarks>
        /// <paramref name="pluginOptions"/> exists because only OpenUtau watches its peer's pings
        /// (§3). A heartbeat test that tightened the threshold on both ends would have the plugin
        /// declare OpenUtau dead first and close the socket from the wrong side.
        /// </remarks>
        private async Task<(DawTransport openutau, DawTransport plugin)> PairAsync(
            DawTransportOptions? options = null,
            Func<DateTime>? nowUtc = null,
            DawTransportOptions? pluginOptions = null) {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var accepting = listener.AcceptTcpClientAsync();
                var openutau = await DawTransport.ConnectAsync(port, options, nowUtc);
                var plugin = DawTransport.Adopt(await accepting, pluginOptions ?? options, nowUtc);
                owned.Add(openutau);
                owned.Add(plugin);
                return (openutau, plugin);
            } finally {
                listener.Stop();
            }
        }

        public void Dispose() {
            foreach (var item in owned) {
                item.Dispose();
            }
        }

        [Fact]
        public async Task RequestIsCorrelatedWithItsResponse() {
            var (openutau, plugin) = await PairAsync();
            plugin.RequestHandler = request => request.RespondAsync(
                DawResult.Ok(new InitResponse { ApiVersion = "1.0" }));

            var response = await openutau.SendRequestAsync<InitResponse>(
                DawMessageKind.Init, new InitRequest { Ustx = "name: test" });

            Assert.Equal("1.0", response.ApiVersion);
        }

        [Fact]
        public async Task ConcurrentRequestsDoNotCrossOver() {
            var (openutau, plugin) = await PairAsync();
            plugin.RequestHandler = async request => {
                // Answer slowly and out of order, so uuid correlation is what makes this work.
                var payload = request.ReadPayload<UpdateUstxNotification>();
                await Task.Delay(payload.Ustx == "slow" ? 120 : 10);
                await request.RespondAsync(DawResult.Ok(new UpdateUstxNotification { Ustx = payload.Ustx }));
            };

            var slow = openutau.SendRequestAsync<UpdateUstxNotification>(
                DawMessageKind.UpdateUstx, new UpdateUstxNotification { Ustx = "slow" });
            var fast = openutau.SendRequestAsync<UpdateUstxNotification>(
                DawMessageKind.UpdateUstx, new UpdateUstxNotification { Ustx = "fast" });

            Assert.Equal("slow", (await slow).Ustx);
            Assert.Equal("fast", (await fast).Ustx);
        }

        [Fact]
        public async Task NotificationsArriveWithTheirPayload() {
            var (openutau, plugin) = await PairAsync();
            var seen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            plugin.Notification += (kind, payload) => seen.TrySetResult(
                $"{kind}:{payload?.Deserialize<UpdateTracksNotification>(DawJson.Options)?.Tracks.Count}");

            await openutau.SendNotificationAsync(DawMessageKind.UpdateTracks, new UpdateTracksNotification {
                Tracks = new List<DawTrackInfo> { new DawTrackInfo { Name = "Track1" } },
            });

            Assert.Equal($"{DawMessageKind.UpdateTracks}:1", await seen.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task UnknownNotificationIsSurvivedNotFatal() {
            var (openutau, plugin) = await PairAsync();
            plugin.RequestHandler = request => request.RespondAsync(DawResult.Ok());

            // §10: a newer plugin may send kinds this build has never heard of.
            await openutau.SendNotificationAsync("somethingFromTheFuture", new DawEmptyPayload());
            var result = await openutau.SendRequestAsync(DawMessageKind.Init, new DawEmptyPayload());

            Assert.True(result.Success);
            Assert.True(openutau.IsConnected);
        }

        [Fact]
        public async Task AudioFrameSurvivesNewlinesInThePayload() {
            var (openutau, plugin) = await PairAsync();
            // Every byte value, so the payload contains 0x0A and 0x0D. A line-oriented reader that
            // did not honour the length prefix would cut the frame here.
            byte[] pcm = Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();
            string hash = DawAudio.FormatHash(DawAudio.Hash(pcm));
            plugin.RequestHandler = request => request.RespondWithAudioAsync(
                request.ReadPayload<GetAudioRequest>().Hash, pcm);

            byte[] received = await openutau.GetAudioAsync(hash);

            Assert.Equal(pcm, received);
        }

        [Fact]
        public async Task ControlLineFollowingAFrameIsNotLost() {
            var (openutau, plugin) = await PairAsync();
            byte[] pcm = Enumerable.Range(0, 8192).Select(i => (byte)(i % 256)).ToArray();
            string hash = DawAudio.FormatHash(DawAudio.Hash(pcm));
            var pinged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            openutau.Notification += (kind, _) => pinged.TrySetResult(kind);
            plugin.RequestHandler = async request => {
                await request.RespondWithAudioAsync(request.ReadPayload<GetAudioRequest>().Hash, pcm);
                // Arrives in the same read buffer as the tail of the frame.
                await plugin.SendNotificationAsync(DawMessageKind.Ping, new DawEmptyPayload());
            };

            Assert.Equal(pcm, await openutau.GetAudioAsync(hash));
            Assert.Equal(DawMessageKind.Ping, await pinged.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task RequestTimeoutIsReported() {
            var (openutau, plugin) = await PairAsync();
            // A handler that never answers is exactly the §8 case: the peer is wedged.
            plugin.RequestHandler = _ => new TaskCompletionSource<bool>().Task;

            await Assert.ThrowsAsync<TimeoutException>(() => openutau.SendRequestAsync(
                DawMessageKind.Init, new DawEmptyPayload(), TimeSpan.FromMilliseconds(150)));
        }

        [Fact]
        public async Task UnhandledRequestIsRefusedRatherThanIgnored() {
            var (openutau, plugin) = await PairAsync();
            plugin.RequestHandler = null;

            // A silent drop would cost the caller a full 10 s timeout instead of an answer.
            // The refusal is immediate, but on loaded CI the transport pumps may lag;
            // a generous budget keeps the assertion about refusal semantics, not timing.
            var result = await openutau.SendRequestAsync(
                DawMessageKind.Init, new DawEmptyPayload(), TimeSpan.FromSeconds(30));

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task RefusedAudioPullFailsWithoutWaitingForTheTimeout() {
            var (openutau, plugin) = await PairAsync();
            plugin.RequestHandler = request => request.RespondAsync(DawResult.Fail("no such hash"));

            // The reply is an envelope, not a frame, so the hash-keyed wait must not hang.
            var error = await Assert.ThrowsAsync<DawProtocolException>(
                () => openutau.GetAudioAsync("42", TimeSpan.FromSeconds(5)));

            Assert.Contains("no such hash", error.Message);
        }

        [Fact]
        public async Task BareCloseEndsTheConnection() {
            var (openutau, plugin) = await PairAsync();
            var ended = new TaskCompletionSource<DawDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            openutau.Disconnected += (reason, _) => ended.TrySetResult(reason);

            await plugin.CloseAsync();

            Assert.Equal(DawDisconnectReason.PluginClosed, await ended.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(openutau.IsConnected);
        }

        [Fact]
        public async Task SilentPeerIsDeclaredDeadAfterTheHeartbeatThreshold() {
            var clock = new Clock(T0);
            var options = new DawTransportOptions {
                HeartbeatPollInterval = TimeSpan.FromMilliseconds(20),
                HeartbeatDeadThreshold = TimeSpan.FromMilliseconds(100),
            };
            var (openutau, _) = await PairAsync(options, clock.Read, DawTransportOptions.Default);
            var ended = new TaskCompletionSource<DawDisconnectReason>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            openutau.Disconnected += (reason, _) => ended.TrySetResult(reason);

            // §3: no ping for longer than the threshold means the plugin is gone, even though the
            // socket itself is still open.
            clock.Now = T0.AddSeconds(30);

            Assert.Equal(
                DawDisconnectReason.HeartbeatTimeout, await ended.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task TrafficKeepsTheConnectionAlive() {
            var clock = new Clock(T0);
            var options = new DawTransportOptions {
                HeartbeatPollInterval = TimeSpan.FromMilliseconds(20),
                HeartbeatDeadThreshold = TimeSpan.FromMilliseconds(100),
            };
            var (openutau, plugin) = await PairAsync(options, clock.Read, DawTransportOptions.Default);

            for (int tick = 0; tick < 6; tick++) {
                clock.Now = T0.AddMilliseconds(80 * (tick + 1));
                await plugin.SendNotificationAsync(DawMessageKind.Ping, new DawEmptyPayload());
                await Task.Delay(30);
            }

            Assert.True(openutau.IsConnected);
        }

        [Fact]
        public async Task DeadConnectionFailsEveryOutstandingWait() {
            var (openutau, plugin) = await PairAsync();
            plugin.RequestHandler = _ => new TaskCompletionSource<bool>().Task;
            var pending = openutau.SendRequestAsync(
                DawMessageKind.Init, new DawEmptyPayload(), TimeSpan.FromSeconds(30));

            plugin.Dispose();

            // No caller may hang on a socket that has gone away.
            await Assert.ThrowsAnyAsync<Exception>(() => pending);
            Assert.False(openutau.IsConnected);
        }
    }
}
