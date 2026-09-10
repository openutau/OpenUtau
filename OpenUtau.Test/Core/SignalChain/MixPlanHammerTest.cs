using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.SignalChain {
    /// <summary>
    /// A fake device thread mixes while a second thread churns the plan
    /// (re-registering pcm, evicting, re-building sessions). Each phrase's pcm
    /// is a flat tag that changes every cycle, so a
    /// torn read — one Mix call seeing two mid-swap pcm arrays of the same
    /// phrase — shows up as two tags inside one phrase region. The read latency
    /// is checked against the 2 ms p99 budget.
    /// </summary>
    public class MixPlanHammerTest {
        const int Channels = 2;
        const int SampleCount = 512 * Channels; // one callback of 512 frames
        const int PhraseMs = 500;
        const int PhraseSamples = PhraseMs * 44100 / 1000; // mono
        const int PhraseRegion = PhraseSamples * Channels; // interleaved
        const int Phrases = 4;
        const int SessionSamples = PhraseMs * Phrases * 44100 / 1000 * Channels;
        static readonly TimeSpan RunTime = TimeSpan.FromSeconds(2);

        private sealed class SourceHolder {
            public volatile SlotMixSource Current;
            public volatile bool Stop;
        }

        [Fact]
        public void MixUnderChurnStaysConsistentAndWithinBudget() {
            var planner = new MixPlanner();
            var part = new UVoicePart { trackNo = 0 };
            var specs = new List<MixPlanner.SlotSpec>();
            for (int i = 0; i < Phrases; ++i) {
                specs.Add(new MixPlanner.SlotSpec(part, 0, (ulong)(i + 1), i * PhraseMs, PhraseMs, 1));
            }
            planner.BeginSession(specs);

            var holder = new SourceHolder { Current = planner.GetTrackSource(0) };
            var durations = new List<double>();
            var violations = 0;
            Exception readerFailure = null;

            var reader = new Task(() => {
                try {
                    var buffer = new float[SampleCount];
                    int position = 0;
                    while (!Volatile.Read(ref holder.Stop)) {
                        Array.Clear(buffer, 0, buffer.Length);
                        var source = holder.Current;
                        var watch = Stopwatch.StartNew();
                        source.Mix(position, buffer, 0, SampleCount);
                        watch.Stop();
                        lock (durations) {
                            durations.Add(watch.Elapsed.TotalMilliseconds);
                        }
                        // Each phrase region must hold at most one tag: a region's
                        // samples all come from that phrase's single pcm array.
                        float tag = 0;
                        int region = -1;
                        for (int b = 0; b < SampleCount; b += Channels) {
                            int p = (position + b) / PhraseRegion;
                            if (p != region) {
                                region = p;
                                tag = 0;
                            }
                            float value = buffer[b];
                            if (value == 0) {
                                continue;
                            }
                            if (tag == 0) {
                                tag = value;
                            } else if (value != tag) {
                                Interlocked.Increment(ref violations);
                                return;
                            }
                        }
                        position = (position + SampleCount) % SessionSamples;
                    }
                } catch (Exception e) {
                    readerFailure = e;
                }
            });

            var writer = new Task(() => {
                int[] versions = new int[Phrases];
                int cycle = 0;
                while (!Volatile.Read(ref holder.Stop)) {
                    for (int i = 0; i < Phrases; ++i) {
                        ++versions[i];
                        var pcm = new float[PhraseSamples];
                        float tag = 1.0f + versions[i] * 0.00001f;
                        Array.Fill(pcm, tag);
                        planner.RegisterPcm(part, (ulong)(i + 1), i * PhraseMs, PhraseMs, 1, pcm);
                    }
                    if (cycle % 37 == 0) {
                        planner.EvictPart(part);
                    } else if (cycle % 73 == 0) {
                        planner.Clear();
                        planner.BeginSession(specs);
                        holder.Current = planner.GetTrackSource(0);
                    }
                    ++cycle;
                }
            });

            reader.Start();
            Thread.Sleep(50); // let the reader settle into its loop
            writer.Start();
            Thread.Sleep((int)RunTime.TotalMilliseconds);
            Volatile.Write(ref holder.Stop, true);
            writer.Wait();
            reader.Wait();

            Assert.Null(readerFailure);
            Assert.Equal(0, Volatile.Read(ref violations));
            lock (durations) {
                Assert.True(durations.Count > 1000, $"too few reads: {durations.Count}");
                durations.Sort();
                double p99 = durations[(int)(durations.Count * 0.99)];
                Assert.True(p99 < 2.0, $"p99 read {p99:F3} ms exceeds the 2 ms budget");
            }
        }
    }
}
