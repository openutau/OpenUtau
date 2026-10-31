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
            lock (gate) {
                Purge();
                ranges = null;
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
            }
        }

        static void Purge() {
            var cutoff = DateTime.UtcNow.AddSeconds(-RequestTtlSeconds);
            requests.RemoveAll(r => r.CreatedUtc < cutoff);
        }
    }
}


