using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// v1.3 track identity on the wire: ids on <c>updateTracks</c>, the plugin's
    /// <c>bindingChanged</c> report, and <c>bindTrack</c> from OpenUtau.
    /// </summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawTrackBindingTest : IDisposable {
        private readonly string directory;
        private readonly UProject project;
        private readonly DawManager manager;
        private DawTestPlugin? plugin;

        public DawTrackBindingTest() {
            directory = Path.Combine(Path.GetTempPath(), "OpenUtauDawTest", Guid.NewGuid().ToString("N"));
            project = new UProject {
                FilePath = Path.Combine(Path.GetTempPath(), "track-binding-test.ustx"),
            };
            project.tracks.Clear();
            project.tracks.Add(new UTrack("Lead") { TrackNo = 0 });
            project.tracks.Add(new UTrack("Harmony") { TrackNo = 1 });
            manager = new DawManager(null, null, null) {
                UseTimerPump = false,
                ProjectSource = () => project,
            };
        }

        public void Dispose() {
            manager.Dispose();
            plugin?.Dispose();
            try {
                Directory.Delete(directory, recursive: true);
            } catch (Exception) {
                // A leftover temp directory is not worth failing a test over.
            }
        }

        private async Task<DawTestPlugin> ConnectAsync(string? apiVersion = null) {
            plugin = DawTestPlugin.Start("Plugin", directory, apiVersion);
            await manager.ConnectAsync(plugin.Advertisement);
            return plugin;
        }

        private DawConnectionInfo Info => Assert.Single(manager.Connections);

        private static async Task WaitUntil(Func<bool> condition, string what) {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition()) {
                if (DateTime.UtcNow > deadline) {
                    throw new TimeoutException($"Timed out waiting for {what}.");
                }
                await Task.Delay(20);
            }
        }

        [Fact]
        public async Task TracksCarryTheirIds() {
            var connected = await ConnectAsync();

            Assert.Equal(project.tracks.Select(t => t.Id), connected.Tracks.Select(t => t.Id));
            Assert.Equal(3, Info.NegotiatedMinor);
        }

        [Fact]
        public async Task IdsGoToAnOlderPluginToo() {
            // One payload serves every connection, so gating the id on the minor would strip it
            // from 1.3 plugins whenever a 1.2 plugin is connected as well.
            var connected = await ConnectAsync("1.2");

            Assert.Equal(project.tracks.Select(t => t.Id), connected.Tracks.Select(t => t.Id));
            Assert.Equal(2, Info.NegotiatedMinor);
        }

        [Fact]
        public async Task ThePluginsBindingIsReported() {
            var connected = await ConnectAsync();
            Assert.Null(Info.BoundTrackId);
            int changes = 0;
            manager.ConnectionsChanged += () => Interlocked.Increment(ref changes);

            await connected.SendBindingChangedAsync(project.tracks[1].Id, "Vocals");
            await WaitUntil(() => Info.BoundTrackId != null, "the binding");

            Assert.Equal(project.tracks[1].Id, Info.BoundTrackId);
            Assert.Equal("Vocals", Info.HostTrackName);
            // The event fires just after the state is stored, so wait for it too.
            await WaitUntil(() => Volatile.Read(ref changes) > 0, "ConnectionsChanged");

            await connected.SendBindingChangedAsync(null);
            await WaitUntil(() => Info.BoundTrackId == null, "the unbinding");
            Assert.Null(Info.HostTrackName);
        }

        [Fact]
        public async Task BindTrackAsksThePluginAndItsReportFollows() {
            var connected = await ConnectAsync();
            string harmony = project.tracks[1].Id;

            await manager.BindTrackAsync(connected.Port, harmony);

            Assert.Equal(harmony, connected.BoundTrackId);
            await WaitUntil(() => Info.BoundTrackId == harmony, "the confirmed binding");
        }

        [Fact]
        public async Task BindTrackNeedsAPluginThatSpeaksIt() {
            var connected = await ConnectAsync("1.2");

            await Assert.ThrowsAsync<DawProtocolException>(
                () => manager.BindTrackAsync(connected.Port, project.tracks[0].Id));
            Assert.DoesNotContain(DawMessageKind.BindTrack, connected.Received);
        }

        [Fact]
        public async Task BindTrackRejectsAnUnknownTrack() {
            var connected = await ConnectAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.BindTrackAsync(connected.Port, "tnosuchtrack"));
            Assert.DoesNotContain(DawMessageKind.BindTrack, connected.Received);
        }

        [Fact]
        public async Task IdsMadeUpOnLoadMarkTheProjectModifiedOnceSent() {
            // As after opening a project saved before track ids existed.
            project.Saved = true;
            project.TrackIdsUnsaved = true;

            await ConnectAsync();

            Assert.True(project.TrackIdsShared);
            // Saved means "has a file", which is still true; only the changes are unsaved.
            Assert.True(project.Saved);
        }

        [Fact]
        public async Task AnOlderPluginDoesNotMakeMadeUpIdsWorthSaving() {
            // A 1.2 plugin binds by position and forgets the ids it is sent.
            project.TrackIdsUnsaved = true;

            await ConnectAsync("1.2");

            Assert.False(project.TrackIdsShared);
            Assert.True(project.TrackIdsUnsaved);
        }

        [Fact]
        public async Task ASavedProjectStaysUnmodified() {
            project.Saved = true;

            await ConnectAsync();

            Assert.False(project.TrackIdsShared);
        }

        [Fact]
        public async Task ABindingIsForgottenWhenThePluginReconnects() {
            var connected = await ConnectAsync();
            await connected.SendBindingChangedAsync(project.tracks[0].Id);
            await WaitUntil(() => Info.BoundTrackId != null, "the binding");

            // The reconnect ladder reuses the connection; the plugin on the other end may be
            // another build now, and it re-reports its binding after init anyway.
            connected.ExpectReconnect();
            connected.DropConnection();
            await connected.WaitForConnectionAsync();
            await WaitUntil(() => Info.State == DawConnectionState.Connected && Info.BoundTrackId == null,
                "the reconnected, unbound connection");
        }
    }
}
