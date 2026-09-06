using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// The end-to-end conformance run: a real plugin-side TCP server drives
    /// <c>init → updateTracks → updatePartLayout → getAudio → playbackStarted</c> against the
    /// shipping <see cref="DawManager"/>, over loopback, through the real discovery directory.
    /// </summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawConformanceTest : IDisposable {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>A ladder short enough that reconnection is observed rather than waited out.</summary>
        private static readonly TimeSpan[] FastBackoff = {
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(20),
        };

        private readonly string directory;
        private readonly DawTestPlugin plugin;
        private readonly UProject project;
        private DawManager? manager;
        private DateTime now = T0;

        public DawConformanceTest() {
            directory = Path.Combine(Path.GetTempPath(), "OpenUtauDawTest", Guid.NewGuid().ToString("N"));
            plugin = DawTestPlugin.Start("ConformancePlugin", directory);
            project = BuildProject();
        }

        public void Dispose() {
            manager?.Dispose();
            plugin.Dispose();
            try {
                if (Directory.Exists(directory)) {
                    Directory.Delete(directory, recursive: true);
                }
            } catch (Exception) {
                // A leftover temp directory is not worth failing a test over.
            }
        }

        /// <summary>Writes each slot's absolute sample index, so a pulled window describes itself.</summary>
        private sealed class RampSource : ISignalSource {
            public bool IsReady(int position, int count) => true;

            public int Mix(int position, float[] buffer, int index, int count) {
                for (int i = 0; i < count; i++) {
                    buffer[index + i] += position + i;
                }
                return position + count;
            }
        }

        /// <summary>An edit that is not a notification, i.e. something that changed the document.</summary>
        private sealed class FakeEdit : UCommand {
            public override void Execute() { }
            public override void Unexecute() { }
            public override string ToString() => "fake edit";
        }

        /// <summary>
        /// Two tracks and two rendered parts. Project defaults are 4/4 at 120 bpm with 480 ticks
        /// per beat, so 480 ticks is exactly 500 ms.
        /// </summary>
        private static UProject BuildProject() {
            var built = new UProject();
            // v1.1 refuses to sync an unsaved project (the USTX serializer needs a FilePath
            // for relative paths), so give the test project a plausible save location.
            built.FilePath = Path.Combine(Path.GetTempPath(), "conformance-test.ustx");
            built.tracks.Clear();
            built.tracks.Add(new UTrack("Lead") { TrackNo = 0, Volume = -3, Pan = -20 });
            // v1.2: the singer/engine informational fields travel on updateTracks. The values
            // are read straight off the track, so plain assignments exercise the wire format
            // without spinning up real voicebank or renderer infrastructure.
            built.tracks[0].Singer = USinger.CreateMissing("Test Singer");
            built.tracks[0].RendererSettings.renderer = "DIFFSINGER";
            // Muted, so the effective-mute field is exercised rather than defaulted; the
            // second track has no singer, so the empty-string defaults are exercised too.
            built.tracks.Add(new UTrack("Harmony") { TrackNo = 1, Volume = 0, Pan = 15, Muted = true });
            var lead = new UVoicePart { name = "Lead A", trackNo = 0, position = 0, duration = 480 };
            lead.SetMix(new RampSource());
            built.parts.Add(lead);
            var harmony = new UVoicePart { name = "Harmony A", trackNo = 1, position = 480, duration = 960 };
            harmony.SetMix(new RampSource());
            built.parts.Add(harmony);
            built.timeAxis.BuildSegments(built);
            return built;
        }

        /// <summary>
        /// A manager with the timer pump off and a frozen clock, so every sync in these tests is
        /// either explicitly pumped or triggered by the protocol itself.
        /// </summary>
        private DawManager NewManager(TimeSpan[]? backoff = null) {
            var created = new DawManager(null, null, backoff) {
                UseTimerPump = false,
                NowUtc = () => now,
                ProjectSource = () => project,
            };
            manager = created;
            return created;
        }

        /// <summary>
        /// The completion criterion: discovery → <c>init</c> → <c>updateTracks</c> →
        /// <c>updatePartLayout</c> → <c>getAudio</c> → <c>playbackStarted</c>, end to end.
        /// </summary>
        [Fact]
        public async Task FullFlowFromDiscoveryToPlaybackFlush() {
            var advertised = plugin.Advertisement;
            Assert.True(advertised.IsCompatible);
            var openutau = NewManager();

            await openutau.ConnectAsync(advertised);

            Assert.Equal(DawConnectionState.Connected, openutau.State);
            Assert.Equal(plugin.Name, openutau.ServerName);
            // §7: the connect handshake is init, then tracks, then layout, in that order.
            Assert.Equal(
                new[] { DawMessageKind.Init, DawMessageKind.UpdateTracks, DawMessageKind.UpdateProjectInfo, DawMessageKind.UpdatePartLayout },
                plugin.Received.ToArray());

            // The baseline is real USTX YAML, not a JSON projection of the project.
            Assert.Equal(DawUstx.Serialize(project), plugin.Ustx);
            Assert.Contains("ustx_version", plugin.Ustx);

            // §6.1: the fader is applied downstream of the audio the plugin pulls, so mute has to
            // travel as its own field or the plugin cannot reproduce it. v1.2 adds the singer and
            // engine informational fields a plugin GUI shows next to the track name.
            Assert.Collection(plugin.Tracks,
                track => {
                    Assert.Equal("Lead", track.Name);
                    Assert.Equal(-3d, track.Volume);
                    Assert.Equal(-20d, track.Pan);
                    Assert.False(track.Muted);
                    Assert.Equal("Test Singer", track.Singer);
                    Assert.Equal("DIFFSINGER", track.Engine);
                },
                track => {
                    Assert.Equal("Harmony", track.Name);
                    Assert.Equal(0d, track.Volume);
                    Assert.Equal(15d, track.Pan);
                    Assert.True(track.Muted);
                    Assert.Equal(string.Empty, track.Singer);
                    Assert.Equal(string.Empty, track.Engine);
                });

            // 480 ticks is 500 ms at the project defaults, so the windows are 0-500 and 500-1500.
            Assert.Collection(plugin.Layout,
                part => {
                    Assert.Equal(0, part.TrackNo);
                    Assert.Equal(0d, part.StartMs, 3);
                    Assert.Equal(500d, part.EndMs, 3);
                },
                part => {
                    Assert.Equal(1, part.TrackNo);
                    Assert.Equal(500d, part.StartMs, 3);
                    Assert.Equal(1500d, part.EndMs, 3);
                });
            Assert.All(plugin.Layout, part => Assert.NotEmpty(part.AudioHash));
            Assert.Equal(2, plugin.Layout.Select(part => part.AudioHash).Distinct().Count());

            // §5.2/§6.2: the plugin pulls what it lacks, and every frame matches its header hash.
            await plugin.PullLayoutAudioAsync();
            var parts = project.parts.OfType<UVoicePart>().ToList();
            AssertPulledMatchesPart(plugin.Layout[0], parts[0]);
            AssertPulledMatchesPart(plugin.Layout[1], parts[1]);

            // A real edit only arms the debounce; the clock is frozen, so nothing may go out yet.
            openutau.OnNext(new FakeEdit(), false);
            await openutau.PumpOnceAsync();

            Assert.True(openutau.Scheduler.HasPending);
            Assert.DoesNotContain(DawMessageKind.UpdateUstx, plugin.Received);

            // §9: playbackStarted makes everything pending due at once, so the DAW never plays
            // audio that OpenUtau has already superseded.
            await plugin.SendPlaybackStartedAsync();
            await plugin.WaitForAsync(DawMessageKind.UpdateUstx);
            await plugin.WaitForCountAsync(DawMessageKind.UpdatePartLayout, 2);

            Assert.Equal(DawUstx.Serialize(project), plugin.Ustx);

            // An inbound heartbeat is absorbed and leaves the connection working.
            await plugin.SendPingAsync();
            await openutau.SyncAsync(DawSyncKind.Ustx);
            await plugin.WaitForCountAsync(DawMessageKind.UpdateUstx, 2);

            Assert.True(openutau.IsConnected);

            await openutau.DisconnectAsync();

            Assert.Equal(DawConnectionState.Disconnected, openutau.State);
            Assert.False(openutau.IsConnected);
        }

        [Fact]
        public async Task IncompatibleAdvertisementIsRefusedWithoutDialing() {
            using var future = DawTestPlugin.Start("FuturePlugin", directory, "2.0");
            var openutau = NewManager();

            await Assert.ThrowsAsync<DawProtocolException>(
                () => openutau.ConnectAsync(future.Advertisement));

            // §4: the major mismatch is visible in the directory, so no socket is ever opened.
            Assert.Equal(DawConnectionState.Disconnected, openutau.State);
            Assert.Empty(future.Received);
        }

        [Fact]
        public async Task IncompatibleInitAnswerDropsTheConnection() {
            // Advertised 1.0 but answers 2.0, which the directory scan cannot catch.
            plugin.ApiVersion = "2.0";
            var openutau = NewManager();

            await Assert.ThrowsAsync<DawProtocolException>(
                () => openutau.ConnectAsync(plugin.Advertisement));

            Assert.Contains(DawMessageKind.Init, plugin.Received);
            Assert.DoesNotContain(DawMessageKind.UpdatePartLayout, plugin.Received);
            Assert.Equal(DawConnectionState.Disconnected, openutau.State);
            Assert.False(openutau.IsConnected);
        }

        [Fact]
        public async Task DroppedConnectionIsRecoveredOnTheBackoffLadder() {
            var openutau = NewManager(FastBackoff);
            await openutau.ConnectAsync(plugin.Advertisement);
            plugin.ExpectReconnect();

            // §8: a plugin that vanishes without sending close.
            plugin.DropConnection();

            await plugin.WaitForConnectionAsync();
            // The handshake re-runs in full, so the plugin gets a fresh baseline.
            await plugin.WaitForCountAsync(DawMessageKind.Init, 2);
            await plugin.WaitForCountAsync(DawMessageKind.UpdatePartLayout, 2);

            Assert.Equal(DawConnectionState.Connected, openutau.State);
            Assert.True(openutau.IsConnected);
        }

        [Fact]
        public async Task ExhaustedBackoffGivesUpAndReports() {
            var openutau = NewManager(FastBackoff);
            var lost = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            openutau.ConnectionLost += reason => lost.TrySetResult(reason);
            await openutau.ConnectAsync(plugin.Advertisement);

            // Nothing is left to reconnect to: the port stops answering before the socket dies.
            plugin.StopListening();
            plugin.DropConnection();

            Assert.NotEmpty(await lost.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(DawConnectionState.Disconnected, openutau.State);
            Assert.False(openutau.IsConnected);
        }

        [Fact]
        public async Task LocalDisconnectDoesNotReconnect() {
            var openutau = NewManager(FastBackoff);
            await openutau.ConnectAsync(plugin.Advertisement);
            plugin.ExpectReconnect();

            await openutau.DisconnectAsync();
            await Task.Delay(250);

            // §8: the ladder exists for failures, not for a disconnect the user asked for.
            Assert.Equal(DawConnectionState.Disconnected, openutau.State);
            Assert.Equal(1, plugin.Received.Count(kind => kind == DawMessageKind.Init));
        }

        private void AssertPulledMatchesPart(DawPartLayout advertised, UVoicePart part) {
            Assert.True(DawAudio.TryExtractPart(project, part, out float[] expected));
            Assert.Equal(expected, DawAudio.FromPcmBytes(plugin.Pulled[advertised.AudioHash]));
        }
    }
}
