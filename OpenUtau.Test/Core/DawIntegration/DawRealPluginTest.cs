using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.DawIntegration {
    /// <summary>
    /// The one test that is not run against a stand-in. Everything else here drives
    /// <see cref="DawManager"/> against <see cref="DawTestPlugin"/>, and the plugin repository's
    /// tests drive its session against a fake OpenUtau — two implementations that each agree with
    /// their own fake can still disagree with each other, and nothing else would notice.
    ///
    /// Opt-in, because it needs the C++ plugin built and running. Start its standalone host with a
    /// scratch discovery directory, then point this at the same directory:
    ///
    /// <code>
    /// bridge-host.exe --dir /tmp/bridge-live --rate 48000
    /// OPENUTAU_BRIDGE_DISCOVERY=/tmp/bridge-live dotnet test --filter DawRealPluginTest
    /// </code>
    ///
    /// Skipped when that variable is unset, so CI and everyday runs are unaffected.
    /// </summary>
    [Collection(DawIntegrationCollection.Name)]
    public class DawRealPluginTest : IDisposable {
        /// <summary>Bounded, unlike the ramp the other tests use: the plugin's peak meter is read
        /// as a number here, so the signal has to mean something at the far end.</summary>
        private const float Level = 0.25f;

        private sealed class ConstantSource : ISignalSource {
            public bool IsReady(int position, int count) => true;

            public int Mix(int position, float[] buffer, int index, int count) {
                for (int i = 0; i < count; i++) {
                    buffer[index + i] += Level;
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

        private DawManager? manager;
        private readonly Xunit.Abstractions.ITestOutputHelper output;

        /// <summary>
        /// xUnit 2.x has no runtime skip API (that arrived in v3), so a missing
        /// OPENUTAU_BRIDGE_DISCOVERY cannot mark the run skipped. It is reported through the test
        /// output instead of silently passing, and the early return keeps CI unaffected.
        /// </summary>
        public DawRealPluginTest(Xunit.Abstractions.ITestOutputHelper output) {
            this.output = output;
        }

        public void Dispose() => manager?.Dispose();

        /// <summary>
        /// One track at unity and centre, one part from 2000 ms to 3000 ms. Project defaults are
        /// 4/4 at 120 bpm with 480 ticks per beat, so 480 ticks is 500 ms: the part sits at ticks
        /// 1920..2880.
        /// </summary>
        private static UProject BuildProject() {
            var built = new UProject();
            built.tracks.Clear();
            built.tracks.Add(new UTrack("Live") { TrackNo = 0, Volume = 0, Pan = 0 });
            var live = new UVoicePart { name = "Live A", trackNo = 0, position = 1920, duration = 960 };
            live.SetMix(new ConstantSource());
            built.parts.Add(live);
            built.timeAxis.BuildSegments(built);
            return built;
        }

        [Fact]
        public async Task RealPluginCompletesTheHandshakeAndPullsAudio() {
            string? directory = Environment.GetEnvironmentVariable("OPENUTAU_BRIDGE_DISCOVERY");
            if (string.IsNullOrEmpty(directory)) {
                // Opt-in live test: needs a running bridge-host publishing its discovery file.
                // xUnit v2 on this branch has no runtime skip, so report and return instead.
                output.WriteLine(
                    "SKIPPED (reported as pass): set OPENUTAU_BRIDGE_DISCOVERY to a running " +
                    "bridge-host's discovery directory to run this live handshake test.");
                return;
            }
            Assert.True(Directory.Exists(directory), $"No such directory: {directory}");

            // Discovery is exercised for real: the file was written by the C++ side, and this is
            // the shipping scanner reading it rather than a fixture.
            var servers = new DawServerFinder(directory!).Scan();
            var server = Assert.Single(servers);
            Assert.True(server.IsCompatible, $"Advertised api {server.Info.ApiVersion}.");

            var project = BuildProject();
            manager = new DawManager(null, null, null) {
                UseTimerPump = false,
                ProjectSource = () => project,
            };

            await manager.ConnectAsync(server);

            Assert.Equal(DawConnectionState.Connected, manager.State);
            Assert.True(manager.IsConnected);

            // The plugin pulls what the layout named, on its own worker thread. Serving those
            // requests is the manager's job, so the wait is for the pulls to stop arriving —
            // there is no message that says "I have everything" (§6.2).
            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.True(manager.IsConnected, "The plugin dropped the connection mid-run.");
            Assert.Equal(DawConnectionState.Connected, manager.State);

            // The last message of the flow, in the direction only the plugin can start it: an edit
            // arms the debounce, and a looping bridge-host reports a fresh start every time the
            // transport jumps back, which §7 says makes everything pending due at once.
            manager.OnNext(new FakeEdit(), false);
            await manager.PumpOnceAsync();
            Assert.True(manager.Scheduler.HasPending, "The edit did not arm the debounce.");

            Assert.True(await WaitUntilAsync(() => !manager!.Scheduler.HasPending),
                "No playbackStarted arrived, so the pending sync was never flushed. " +
                "Run bridge-host with --loop for this half of the flow.");

            // Whether the audio actually landed is visible at the other end, in bridge-host's peak
            // meter: one track at unity and centre, so the constant-power pan law puts
            // 0.25 * 0.7071 = 0.1768 in each channel between 2 s and 3 s, and silence either side.
            await manager.DisconnectAsync();

            Assert.False(manager.IsConnected);
        }

        /// <summary>Polls a condition the plugin's own timing decides. False if it never held.</summary>
        private static async Task<bool> WaitUntilAsync(Func<bool> ready) {
            for (int i = 0; i < 100; i++) {
                if (ready()) {
                    return true;
                }
                await Task.Delay(100);
            }
            return false;
        }
    }
}
