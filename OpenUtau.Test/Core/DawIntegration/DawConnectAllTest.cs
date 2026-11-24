using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// "Connect all" and auto-connect: several plugin instances, one per DAW track, are
    /// connected without picking each one in the dialog.
    /// </summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawConnectAllTest : IDisposable {
        private readonly string directory;
        private readonly DawServerFinder finder;
        private readonly UProject project;
        private readonly DawManager manager;
        private readonly List<DawTestPlugin> plugins = new List<DawTestPlugin>();
        private DawAutoConnector? connector;

        public DawConnectAllTest() {
            directory = Path.Combine(Path.GetTempPath(), "OpenUtauDawTest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            finder = new DawServerFinder(directory);
            project = new UProject {
                FilePath = Path.Combine(Path.GetTempPath(), "connect-all-test.ustx"),
            };
            manager = new DawManager(null, null, null) {
                UseTimerPump = false,
                ProjectSource = () => project,
            };
        }

        public void Dispose() {
            connector?.Dispose();
            manager.Dispose();
            foreach (var plugin in plugins) {
                plugin.Dispose();
            }
            try {
                Directory.Delete(directory, recursive: true);
            } catch (Exception) {
                // A leftover temp directory is not worth failing a test over.
            }
        }

        private DawTestPlugin StartPlugin(string name, string? apiVersion = null) {
            var plugin = DawTestPlugin.Start(name, directory, apiVersion);
            plugins.Add(plugin);
            return plugin;
        }

        private DawAutoConnector NewConnector(TimeSpan? poll = null) {
            // A long poll by default, so only the watcher, notifications and explicit passes connect.
            connector = new DawAutoConnector(manager, finder,
                TimeSpan.FromMilliseconds(20), poll ?? TimeSpan.FromHours(1));
            return connector;
        }

        private bool IsConnected(DawTestPlugin plugin) =>
            manager.Connections.Any(c => c.Port == plugin.Port && c.State == DawConnectionState.Connected);

        private static async Task WaitUntil(Func<bool> condition, string what) {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!condition()) {
                if (DateTime.UtcNow > deadline) {
                    throw new TimeoutException($"Timed out waiting for {what}.");
                }
                await Task.Delay(20);
            }
        }

        private int InitCount(DawTestPlugin plugin) =>
            plugin.Received.Count(kind => kind == DawMessageKind.Init);

        [Fact]
        public async Task ConnectAllConnectsEveryNewCompatiblePlugin() {
            var first = StartPlugin("First");
            var second = StartPlugin("Second");
            var incompatible = StartPlugin("Incompatible", "9.0");
            await manager.ConnectAsync(first.Advertisement);

            var result = await manager.ConnectAllAsync(finder.Scan());

            Assert.Equal(new[] { second.Port }, result.Connected.Select(s => s.Port));
            Assert.Equal(new[] { incompatible.Port }, result.Incompatible.Select(s => s.Port));
            Assert.Empty(result.Failed);
            Assert.True(IsConnected(first));
            Assert.True(IsConnected(second));
            Assert.Equal(2, manager.Connections.Count);
            // The connection that already existed was left alone, not replaced.
            Assert.Equal(1, InitCount(first));
        }

        [Fact]
        public async Task ConnectAllKeepsGoingPastAPluginThatFails() {
            var gone = StartPlugin("Gone");
            var advertisedGone = gone.Advertisement;
            gone.StopListening();
            var alive = StartPlugin("Alive");

            var result = await manager.ConnectAllAsync(new[] { advertisedGone, alive.Advertisement });

            Assert.Equal(new[] { gone.Port }, result.Failed.Select(f => f.Server.Port));
            Assert.Equal(new[] { alive.Port }, result.Connected.Select(s => s.Port));
            Assert.True(IsConnected(alive));
        }

        [Fact]
        public async Task ConnectAllAsksBeforeEachConnect() {
            var first = StartPlugin("First");
            var second = StartPlugin("Second");
            var asked = new List<int>();

            var result = await manager.ConnectAllAsync(finder.Scan(), default, server => {
                asked.Add(server.Port);
                return server.Port != second.Port;
            });

            Assert.Equal(new[] { first.Port }, result.Connected.Select(s => s.Port));
            Assert.Equal(2, asked.Count);
            Assert.False(IsConnected(second));
            Assert.Equal(0, InitCount(second));
        }

        [Fact]
        public async Task ConnectAllStopsOnceForAnUnsavedProject() {
            project.FilePath = string.Empty;
            StartPlugin("First");
            StartPlugin("Second");

            await Assert.ThrowsAsync<MessageCustomizableException>(
                () => manager.ConnectAllAsync(finder.Scan()));

            Assert.Empty(manager.Connections);
        }

        [Fact]
        public async Task AutoConnectPicksUpRunningAndNewlyLoadedPlugins() {
            var running = StartPlugin("Running");
            // The watcher normally sees the new file within the debounce; the poll is only a
            // backstop for platforms whose watcher is slow.
            var auto = NewConnector(TimeSpan.FromSeconds(2));

            auto.Enabled = true;
            await WaitUntil(() => IsConnected(running), "the running plugin");

            // A plugin loaded later publishes its advertisement; the watcher sees it.
            var added = StartPlugin("Added");
            await WaitUntil(() => IsConnected(added), "the newly loaded plugin");
            Assert.Equal(1, InitCount(running));
        }

        [Fact]
        public async Task AutoConnectWaitsForTheFirstSave() {
            project.FilePath = string.Empty;
            var plugin = StartPlugin("Plugin");
            var auto = NewConnector();
            auto.Enabled = true;
            var result = await auto.PassAsync();
            Assert.Empty(result.Connected);
            Assert.Empty(manager.Connections);

            project.FilePath = Path.Combine(Path.GetTempPath(), "connect-all-test.ustx");
            auto.OnNext(new SaveProjectNotification(project.FilePath), false);

            await WaitUntil(() => IsConnected(plugin), "a connection after the save");
        }

        [Fact]
        public async Task AutoConnectLeavesAManualDisconnectAlone() {
            var plugin = StartPlugin("Plugin");
            var auto = NewConnector();
            auto.Enabled = true;
            await WaitUntil(() => IsConnected(plugin), "the first connection");

            auto.Skip(plugin.Port);
            await manager.DisconnectAsync(plugin.Port);
            var result = await auto.PassAsync();
            Assert.Empty(result.Connected);
            Assert.Empty(manager.Connections);

            // "Connect all" forgets the skip.
            auto.ClearSkips();
            result = await auto.PassAsync();
            Assert.Equal(new[] { plugin.Port }, result.Connected.Select(s => s.Port));
        }

        [Fact]
        public async Task DisabledAutoConnectDoesNothing() {
            var plugin = StartPlugin("Plugin");
            var auto = NewConnector();

            var result = await auto.PassAsync();

            Assert.Empty(result.Connected);
            Assert.Empty(manager.Connections);
            Assert.Equal(0, InitCount(plugin));
        }
    }
}
