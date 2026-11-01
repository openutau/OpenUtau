using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// One-shot "re-sample this timeline range" requests. The piano roll records
    /// them, the renderer consumes them when it computes its cache name, so the
    /// audio cache cannot hand back the previous take.
    /// </summary>
    public static class DiffSingerTakeReroll {
        const double RequestTtlSeconds = 120;

        sealed class RerollRequest {
            public int TrackNo;
            public int StartTick;
            public int EndTick;
            public int Nonce;
            public DateTime CreatedUtc;
        }

        static readonly object gate = new object();
        static readonly List<RerollRequest> requests = new List<RerollRequest>();
        // Bumped on every consume per track: a restore from an older render cycle
        // must not resurrect a request that a newer render already took over.
        static readonly Dictionary<int, int> trackEpoch = new Dictionary<int, int>();
        static int nextNonce;

        public static void Request(int trackNo, IEnumerable<(int start, int end)> ranges) {
            lock (gate) {
                Purge();
                int nonce = ++nextNonce;
                var now = DateTime.UtcNow;
                foreach (var (start, end) in ranges) {
                    if (end <= start) {
                        continue;
                    }
                    requests.Add(new RerollRequest {
                        TrackNo = trackNo,
                        StartTick = start,
                        EndTick = end,
                        Nonce = nonce,
                        CreatedUtc = now,
                    });
                }
            }
        }

        /// <summary>
        /// The nonce to put in the render cache name, and the requested ranges
        /// clipped to this phrase. A zero nonce means nothing was requested.
        /// Each request is consumed exactly once per overlapping phrase, so a later
        /// edit cannot silently re-sample frames the user did not ask for again.
        /// </summary>
        public static int Consume(int trackNo, int phraseStartTick, int phraseEndTick,
                out List<(int start, int end)>? ranges) {
            return Consume(trackNo, phraseStartTick, phraseEndTick, out ranges, out _, out _);
        }

        public static int Consume(int trackNo, int phraseStartTick, int phraseEndTick,
                out List<(int start, int end)>? ranges, out DateTime createdUtc) {
            return Consume(trackNo, phraseStartTick, phraseEndTick, out ranges, out createdUtc, out _);
        }

        public static int Consume(int trackNo, int phraseStartTick, int phraseEndTick,
                out List<(int start, int end)>? ranges, out DateTime createdUtc, out int epoch) {
            lock (gate) {
                Purge();
                epoch = trackEpoch.TryGetValue(trackNo, out int current) ? current + 1 : 1;
                trackEpoch[trackNo] = epoch;
                ranges = null;
                createdUtc = DateTime.UtcNow;
                int nonce = 0;
                for (int i = requests.Count - 1; i >= 0; i--) {
                    var request = requests[i];
                    if (request.TrackNo != trackNo ||
                        request.EndTick <= phraseStartTick ||
                        request.StartTick >= phraseEndTick) {
                        continue;
                    }
                    int start = Math.Max(request.StartTick, phraseStartTick);
                    int end = Math.Min(request.EndTick, phraseEndTick);
                    if (end <= start) {
                        continue;
                    }
                    ranges ??= new List<(int, int)>();
                    ranges.Add((start, end));
                    nonce = Math.Max(nonce, request.Nonce);
                    if (request.CreatedUtc < createdUtc) {
                        createdUtc = request.CreatedUtc;
                    }

                    // Consume only this phrase's slice. The remainders still apply to
                    // other phrases covered by the same request.
                    requests.RemoveAt(i);
                    if (request.StartTick < start) {
                        requests.Add(Clone(request, request.StartTick, start));
                    }
                    if (request.EndTick > end) {
                        requests.Add(Clone(request, end, request.EndTick));
                    }
                }
                return nonce;
            }
        }

        /// <summary>
        /// Puts a consumed request back when the render that took it was cancelled or
        /// failed, so a re-roll is never silently lost.
        /// </summary>
        public static void Restore(int trackNo, int nonce, IEnumerable<(int start, int end)>? ranges) {
            Restore(trackNo, nonce, DateTime.UtcNow, ranges);
        }

        public static void Restore(int trackNo, int nonce, DateTime createdUtc,
                IEnumerable<(int start, int end)>? ranges) {
            if (nonce == 0 || ranges == null) {
                return;
            }
            lock (gate) {
                if (!trackEpoch.ContainsKey(trackNo)) {
                    return;
                }
                RestoreLocked(trackNo, nonce, createdUtc, ranges);
            }
        }

        public static void Restore(int trackNo, int nonce, DateTime createdUtc, int epoch,
                IEnumerable<(int start, int end)>? ranges) {
            if (nonce == 0 || ranges == null) {
                return;
            }
            lock (gate) {
                if (!trackEpoch.TryGetValue(trackNo, out int current) || current != epoch) {
                    // A newer render cycle for this track already took over; putting the
                    // old request back would re-sample frames the user did not ask for.
                    return;
                }
                RestoreLocked(trackNo, nonce, createdUtc, ranges);
            }
        }

        static void RestoreLocked(int trackNo, int nonce, DateTime createdUtc,
                IEnumerable<(int start, int end)> ranges) {
            Purge();
            foreach (var (start, end) in ranges) {
                if (end <= start) {
                    continue;
                }
                requests.Add(new RerollRequest {
                    TrackNo = trackNo,
                    StartTick = start,
                    EndTick = end,
                    Nonce = nonce,
                    CreatedUtc = createdUtc,
                });
            }
        }

        static RerollRequest Clone(RerollRequest request, int start, int end) {
            return new RerollRequest {
                TrackNo = request.TrackNo,
                StartTick = start,
                EndTick = end,
                Nonce = request.Nonce,
                CreatedUtc = request.CreatedUtc,
            };
        }

        public static void Clear() {
            lock (gate) {
                requests.Clear();
                trackEpoch.Clear();
            }
        }

        static void Purge() {
            var cutoff = DateTime.UtcNow.AddSeconds(-RequestTtlSeconds);
            requests.RemoveAll(r => r.CreatedUtc < cutoff);
        }
    }
}


