using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>Discovery directory and version negotiation (PROTOCOL.md §4).</summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawServerFinderTest : IDisposable {
        private readonly string directory;

        public DawServerFinderTest() {
            directory = Path.Combine(Path.GetTempPath(), "OpenUtauDawTest", Guid.NewGuid().ToString("N"));
        }

        public void Dispose() {
            try {
                if (Directory.Exists(directory)) {
                    Directory.Delete(directory, recursive: true);
                }
            } catch (Exception) {
                // A leftover temp directory is not worth failing a test over.
            }
        }

        /// <summary>Binds a loopback port and keeps it bound for as long as the test needs it.</summary>
        private static TcpListener Listen() {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return listener;
        }

        private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

        /// <summary>A port that was bound and released, so nothing answers on it any more.</summary>
        private static int FreePort() {
            var listener = Listen();
            int port = Port(listener);
            listener.Stop();
            return port;
        }

        [Fact]
        public void DefaultDirectoryIsTheProtocolPath() {
            Assert.Equal(
                Path.Combine(Path.GetTempPath(), "OpenUtau", "PluginServers"),
                DawServerFinder.DefaultDirectory);
        }

        [Fact]
        public void MissingDirectoryScansEmpty() {
            var finder = new DawServerFinder(directory);

            Assert.Empty(finder.Scan());
        }

        [Fact]
        public void LiveAdvertisementIsFound() {
            using var listener = Listen();
            var finder = new DawServerFinder(directory);
            finder.Publish("TestPlugin", Port(listener));

            var found = Assert.Single(finder.Scan());

            Assert.Equal(Port(listener), found.Port);
            Assert.Equal("TestPlugin", found.Name);
            Assert.True(found.IsCompatible);
        }

        [Fact]
        public void StaleAdvertisementIsDeleted() {
            var finder = new DawServerFinder(directory);
            string path = finder.Publish("CrashedPlugin", FreePort());

            Assert.Empty(finder.Scan());

            // §4: this is how the directory recovers from a DAW that died without cleaning up.
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void StaleAdvertisementIsKeptWhenSweepingIsOff() {
            var finder = new DawServerFinder(directory);
            string path = finder.Publish("CrashedPlugin", FreePort());

            Assert.Empty(finder.Scan(removeStale: false));

            Assert.True(File.Exists(path));
        }

        [Fact]
        public void UnreadableFileIsSkipped() {
            using var listener = Listen();
            Directory.CreateDirectory(directory);
            // Half-written files are normal if a plugin is starting while we scan.
            File.WriteAllText(Path.Combine(directory, "torn.json"), "{\"port\": ");
            var finder = new DawServerFinder(directory);
            finder.Publish("Good", Port(listener));

            var found = Assert.Single(finder.Scan());

            Assert.Equal("Good", found.Name);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(70000)]
        public void UnusablePortIsSkipped(int port) {
            var finder = new DawServerFinder(directory);
            finder.Publish("Broken", port);

            Assert.Empty(finder.Scan());
        }

        [Fact]
        public void IncompatibleMajorIsListedButRefused() {
            using var listener = Listen();
            var finder = new DawServerFinder(directory);
            finder.Publish("FuturePlugin", Port(listener), "2.0");

            var found = Assert.Single(finder.Scan());

            // Listed so the UI can explain itself, but not connectable.
            Assert.False(found.IsCompatible);
            Assert.Equal(2, found.Version.Major);
        }

        [Fact]
        public void NewerMinorStillConnects() {
            using var listener = Listen();
            var finder = new DawServerFinder(directory);
            finder.Publish("NewerPlugin", Port(listener), "1.7");

            Assert.True(Assert.Single(finder.Scan()).IsCompatible);
        }

        [Fact]
        public void UnparseableVersionIsRefused() {
            using var listener = Listen();
            var finder = new DawServerFinder(directory);
            finder.Publish("MysteryPlugin", Port(listener), "not-a-version");

            Assert.False(Assert.Single(finder.Scan()).IsCompatible);
        }

        [Fact]
        public void NameFallsBackToTheFileName() {
            using var listener = Listen();
            var finder = new DawServerFinder(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "AnonymousPlugin.json"),
                DawJson.Serialize(new DawServerInfo { Port = Port(listener), ApiVersion = "1.0" }));

            Assert.Equal("AnonymousPlugin", Assert.Single(finder.Scan()).Name);
        }

        [Fact]
        public void PortProbeDistinguishesBoundFromFree() {
            using var listener = Listen();

            // ExclusiveAddressUse is what makes this reliable on Windows; without it a second bind
            // would succeed through SO_REUSEADDR and every live server would look stale.
            Assert.True(DawServerFinder.IsPortAlive(Port(listener)));
            Assert.False(DawServerFinder.IsPortAlive(FreePort()));
        }

        [Fact]
        public void RemoveDeletesTheAdvertisement() {
            var finder = new DawServerFinder(directory);
            string path = finder.Publish("Departing", 1);

            finder.Remove(path);

            Assert.False(File.Exists(path));
        }

        [Theory]
        [InlineData("1.0", 1, 0, true)]
        [InlineData("1.4", 1, 4, true)]
        [InlineData("2.0", 2, 0, false)]
        [InlineData("0.9", 0, 9, false)]
        public void VersionCompatibilityIsMajorOnly(string text, int major, int minor, bool compatible) {
            Assert.True(DawApiVersion.TryParse(text, out var version));

            Assert.Equal(major, version.Major);
            Assert.Equal(minor, version.Minor);
            Assert.Equal(compatible, version.IsCompatibleWith(DawApiVersion.Current));
        }

        [Theory]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("1.x")]
        [InlineData("x.0")]
        public void MalformedVersionsDoNotParse(string text) {
            Assert.False(DawApiVersion.TryParse(text, out _));
        }
    }
}
