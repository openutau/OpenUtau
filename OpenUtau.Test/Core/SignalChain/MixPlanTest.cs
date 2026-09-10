using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.SignalChain {
    /// <summary>
    /// Behaviour gate for the frozen-slot transport: slot placement and mixing
    /// arithmetic, hold/passthrough readiness semantics, and the planner's
    /// session/cache lifecycle.
    /// </summary>
    public class MixPlanTest {
        class Phrase {
            public double OffsetMs;
            public double DurMs;
            public float[] Samples;
        }

        class TrackScenario {
            public int TrackNo;
            public List<List<Phrase>> Parts = new List<List<Phrase>>();
            public Phrase Wave;
        }

        static TrackScenario[] MakeScenario() {
            var rnd = new Random(42);
            var tracks = new List<TrackScenario>();
            double t = 0;
            for (int tr = 0; tr < 2; tr++) {
                var ts = new TrackScenario { TrackNo = tr };
                for (int p = 0; p < 2; p++) {
                    var part = new List<Phrase>();
                    for (int ph = 0; ph < 2; ph++) {
                        double durMs = 200 + rnd.Next(300);
                        int n = (int)(durMs * 44100 / 1000);
                        var samples = new float[n];
                        for (int i = 0; i < n; i++) {
                            samples[i] = (float)(rnd.NextDouble() - 0.5) * 0.5f;
                        }
                        part.Add(new Phrase { OffsetMs = t, DurMs = durMs, Samples = samples });
                        t += durMs * (tr == 0 ? 1.0 : 0.4);
                    }
                    ts.Parts.Add(part);
                }
                if (tr == 0) {
                    // A stereo "wave part" slot (channels = 2), like a UWavePart.
                    double durMs = 800;
                    int n = (int)(durMs * 44100 / 1000) * 2;
                    var samples = new float[n];
                    for (int i = 0; i < n; i++) {
                        samples[i] = (float)Math.Sin(i / 1000.0) * 0.25f;
                    }
                    ts.Wave = new Phrase { OffsetMs = 300, DurMs = durMs, Samples = samples };
                }
                tracks.Add(ts);
            }
            return tracks.ToArray();
        }

        [Fact]
        public void FailedSlotNeverBlocksAndReadsSilence() {
            // 50 ms slot at 50 ms: interleaved [4410, 8820)
            var failed = new SampleSlot(50, 50, 1, null, SlotState.Failed);
            Assert.True(failed.IsReady(4410, 100)); // must not hold playback
            var buffer = new float[100];
            Assert.Equal(100, failed.Mix(0, buffer, 0, 100));       // before window: position + count
            Assert.Equal(4510, failed.Mix(4410, buffer, 0, 100));   // inside: silence, end candidate
            Assert.Equal(10000, failed.Mix(10000, buffer, 0, 100)); // past estimated end: position
            Assert.True(buffer.All(x => x == 0f));
        }

        [Fact]
        public void PendingSlotInPassthroughReadsSilenceThroughEstimatedEnd() {
            var pending = new SampleSlot(0, 50, 1, null, SlotState.Pending);
            var buffer = new float[4410];
            Assert.Equal(4410, pending.Mix(0, buffer, 0, 4410)); // silence through estimated end
            Assert.True(buffer.All(x => x == 0f));
            Assert.Equal(5000, pending.Mix(5000, buffer, 0, 4410)); // past end: position
        }

        [Fact]
        public void AdapterHoldsWhenUnreadyInHoldMode() {
            var source = new SlotMixSource();
            source.SetSlots(new[] { new SampleSlot(0, 100, 1, null, SlotState.Pending) });
            var adapter = new MasterAdapter(source);
            Assert.True(adapter.HoldWhenUnready);
            var buffer = new float[4410];
            Assert.Equal(4410, adapter.Read(buffer, 0, buffer.Length));
            Assert.Equal(4410, adapter.Waited);
            Assert.True(adapter.IsWaiting);
            Assert.True(buffer.All(x => x == 0f));
            // position did not advance: the next read waits again
            Assert.Equal(4410, adapter.Read(buffer, 0, buffer.Length));
            Assert.Equal(8820, adapter.Waited);
        }

        [Fact]
        public void AdapterPassesThroughInLoopMode() {
            var source = new SlotMixSource();
            source.SetSlots(new[] { new SampleSlot(0, 100, 1, null, SlotState.Pending) });
            var adapter = new MasterAdapter(source) { HoldWhenUnready = false };
            var buffer = new float[4410];
            Assert.Equal(4410, adapter.Read(buffer, 0, buffer.Length));
            Assert.Equal(0, adapter.Waited);
            Assert.False(adapter.IsWaiting);
            Assert.True(buffer.All(x => x == 0f)); // silence, but position advanced
            // the slot spans 100 ms (8820 interleaved): the second read is still inside it
            Assert.Equal(4410, adapter.Read(buffer, 0, buffer.Length));
            // third read is past the estimated end -> stream exhausted
            Assert.Equal(0, adapter.Read(buffer, 0, buffer.Length));
        }

        [Fact]
        public void PlannerSessionPublishesSlots() {
            var scenario = MakeScenario();
            var planner = new MixPlanner();
            var specs = new List<MixPlanner.SlotSpec>();
            var pcm = new List<(UPart part, ulong hash, float[] samples)>();
            foreach (var tr in scenario) {
                foreach (var part in tr.Parts) {
                    var up = new UVoicePart { trackNo = tr.TrackNo };
                    ulong hash = 1;
                    foreach (var ph in part) {
                        specs.Add(new MixPlanner.SlotSpec(up, tr.TrackNo, hash, ph.OffsetMs, ph.DurMs, 1));
                        pcm.Add((up, hash, ph.Samples));
                        hash++;
                    }
                }
                if (tr.Wave != null) {
                    // A wave part is a UPart with hash 0 and its own channel count.
                    var wavePart = new UVoicePart { trackNo = tr.TrackNo };
                    specs.Add(new MixPlanner.SlotSpec(wavePart, tr.TrackNo, 0, tr.Wave.OffsetMs, tr.Wave.DurMs, 2));
                    pcm.Add((wavePart, 0, tr.Wave.Samples));
                }
            }
            planner.BeginSession(specs);
            Assert.False(planner.GetTrackSource(0).IsReady(0, 4410));

            foreach (var (part, hash, samples) in pcm) {
                // channels here only feeds the cache entry; session slots use the spec's channels.
                planner.RegisterPcm(part, hash, 0, 0, 1, samples);
            }
            Assert.True(planner.GetTrackSource(0).IsReady(0, 4410));
            Assert.True(planner.GetTrackSource(1).IsReady(0, 4410));

            // Track 0, interleaved [0, 4410): only the first phrase (at 0 ms, mono) overlaps.
            // The mix must duplicate its samples into both channels.
            var firstPhrase = scenario[0].Parts[0][0];
            var buffer = new float[4410];
            planner.GetTrackSource(0).Mix(0, buffer, 0, buffer.Length);
            for (int j = 0; j < 4410 / 2; ++j) {
                Assert.Equal(firstPhrase.Samples[j], buffer[2 * j]);
                Assert.Equal(firstPhrase.Samples[j], buffer[2 * j + 1]);
            }
        }

        [Fact]
        public void PlannerFailedEvictAndReRegister() {
            var planner = new MixPlanner();
            var part = new UVoicePart { trackNo = 0 };
            planner.BeginSession(new[] {
                new MixPlanner.SlotSpec(part, 0, 1, 0, 100, 1),   // interleaved [0, 8820)
                new MixPlanner.SlotSpec(part, 0, 2, 500, 100, 1), // interleaved [44100, 61380)
            });
            var src = planner.GetTrackSource(0);
            Assert.False(src.IsReady(0, 4410)); // slot 1 pending inside the window

            planner.MarkFailed(part, 1);
            // slot 1 failed (ready), slot 2 is after the window -> track ready
            Assert.True(src.IsReady(0, 4410));
            var buffer = new float[4410];
            src.Mix(0, buffer, 0, buffer.Length);
            Assert.True(buffer.All(x => x == 0f)); // failed phrase reads as silence

            planner.EvictPart(part);
            Assert.False(src.IsReady(0, 4410)); // everything back to pending

            var samples = new float[4410];
            for (int i = 0; i < samples.Length; i++) {
                samples[i] = 0.25f;
            }
            planner.RegisterPcm(part, 1, 0, 100, 1, samples);
            Assert.True(src.IsReady(0, 4410));
            src.Mix(0, buffer, 0, buffer.Length);
            Assert.True(buffer.Take(4410).All(x => x == 0.25f)); // mono duplicated to stereo
        }

        [Fact]
        public void PlannerSeedsSessionFromCache() {
            var planner = new MixPlanner();
            var part = new UVoicePart { trackNo = 0 };
            // A pre-render pass with no session only fills the cache.
            var samples = new float[4410];
            for (int i = 0; i < samples.Length; i++) {
                samples[i] = 0.5f;
            }
            planner.RegisterPcm(part, 42, 0, 100, 1, samples);

            // A new session with the same (part, hash) starts Ready.
            planner.BeginSession(new[] { new MixPlanner.SlotSpec(part, 0, 42, 0, 100, 1) });
            var src = planner.GetTrackSource(0);
            Assert.True(src.IsReady(0, 4410));
            var buffer = new float[4410];
            src.Mix(0, buffer, 0, buffer.Length);
            Assert.True(buffer.All(x => x == 0.5f));

            // The same phrase moved on the timeline: same hash, new placement.
            // The slot must use the fresh placement with the cached pcm.
            // A new session publishes fresh track sources — refetch.
            planner.BeginSession(new[] { new MixPlanner.SlotSpec(part, 0, 42, 200, 100, 1) });
            src = planner.GetTrackSource(0);
            Assert.True(src.IsReady(0, 4410));
            var before = new float[17640];
            src.Mix(0, before, 0, before.Length);
            Assert.True(before.All(x => x == 0f));
            var at = new float[4410];
            src.Mix(17640, at, 0, at.Length);
            Assert.True(at.All(x => x == 0.5f));
        }

        [Fact]
        public void PhrasePcmStoreIsContentAddressed() {
            var planner = new MixPlanner();
            var part = new UVoicePart { trackNo = 0 };
            Assert.False(planner.TryGetPhrasePcm(part, 7, out _));

            planner.RegisterPcm(part, 7, 100, 200, 1, new float[] { 0.1f, 0.2f });
            Assert.True(planner.TryGetPhrasePcm(part, 7, out var p));
            Assert.Equal(100, p.posMs);
            Assert.Equal(200, p.durMs);
            Assert.Equal(1, p.channels);
            Assert.Equal(new float[] { 0.1f, 0.2f }, p.pcm.Buffer);

            planner.Clear();
            Assert.False(planner.TryGetPhrasePcm(part, 7, out _));
        }

        [Fact]
        public void PlacementsFollowCurrentPhrasesOnly() {
            var planner = new MixPlanner();
            var part = new UVoicePart { trackNo = 0 };

            // The store holds a current phrase (1), plus stale entries from earlier
            // edits (2, 4). The current phrase set is 1 and an unrendered 3.
            planner.RegisterPcm(part, 1, 0, 100, 1, new float[] { 0.5f });
            planner.RegisterPcm(part, 2, 500, 100, 1, new float[] { 0.25f });
            planner.RegisterPcm(part, 4, 1500, 100, 1, new float[] { 0.5f });
            var phrases = new (ulong hash, double startMs, double endMs)[] {
                (1, 10, 110),
                (3, 900, 1000),
            };

            // Only the current, rendered phrase appears, at the caller's live
            // range (10..110), not the store's (0..100).
            Assert.True(MixPlanner.TryGetPartPlacements(planner, part, phrases, out var list));
            Assert.Single(list);
            Assert.Equal(10, list[0].posMs);
            Assert.Equal(100, list[0].durMs);
            Assert.Equal(1, list[0].channels);
            Assert.Equal(new float[] { 0.5f }, list[0].pcm.Buffer);

            // A playback session running on top changes nothing for display.
            planner.BeginSession(new[] { new MixPlanner.SlotSpec(part, 0, 1, 0, 100, 1) });
            Assert.True(MixPlanner.TryGetPartPlacements(planner, part, phrases, out list));
            Assert.Single(list);
            Assert.Equal(10, list[0].posMs);

            // Nothing rendered: blank, whatever the store holds.
            var empty = new UVoicePart { trackNo = 1 };
            Assert.False(MixPlanner.TryGetPartPlacements(planner, empty, Array.Empty<(ulong, double, double)>(), out _));
        }

        [Fact]
        public void PlacementsServeWavePartsFromHashZero() {
            var planner = new MixPlanner();
            var wave = new UWavePart { trackNo = 0 };
            planner.RegisterWavePcm(wave, 300, 800, 2, new float[] { 0.1f, 0.2f, 0.3f, 0.4f });
            Assert.True(MixPlanner.TryGetPartPlacements(planner, wave, null, out var list));
            Assert.Single(list);
            Assert.Equal(300, list[0].posMs);
            Assert.Equal(800, list[0].durMs);
            Assert.Equal(2, list[0].channels);
        }
    }
}
