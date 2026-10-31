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
        /// </summary>
        public static int Consume(int trackNo, int phraseStartTick, int phraseEndTick,
                out List<(int start, int end)>? ranges) {
            lock (gate) {
                Purge();
                ranges = null;
                var matched = requests
                    .Where(r => r.TrackNo == trackNo && r.EndTick > phraseStartTick && r.StartTick < phraseEndTick)
                    .ToList();
                if (matched.Count == 0) {
                    return 0;
                }
                ranges = matched
                    .Select(r => (Math.Max(r.StartTick, phraseStartTick), Math.Min(r.EndTick, phraseEndTick)))
                    .Where(r => r.Item2 > r.Item1)
                    .ToList();
                return matched.Max(r => r.Nonce);
            }
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


