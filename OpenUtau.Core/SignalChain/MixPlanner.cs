using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core {
    /// <summary>
    /// The per-phrase PCM registry and slot publisher for the transport.
    ///
    /// Two lifetimes:
    ///  <b>Cache</b> — document-lifetime, keyed by (part, phraseHash). Feeds the
    ///  waveform canvas / DAW extraction and seeds new sessions, so a pre-rendered
    ///  phrase plays back instantly. phraseHash is <c>RenderPhrase.Hash(true)</c> —
    ///  a content hash — so an edited phrase never hits a stale entry (moving a
    ///  part keeps the same hash, which is correct: rendered pcm does not depend on
    ///  the part's absolute position).
    ///  <b>Session</b> — one playback at a time. <see cref="BeginSession"/> builds
    ///  Pending slots (or Ready, when the cache already holds the pcm); the render
    ///  pass flips slots Ready as it finishes phrases; <see cref="MarkFailed"/> /
    ///  <see cref="EvictPart"/> handle failures and "clear phrase cache".
    ///
    /// Thread model: every mutating method takes a short lock (callers are the
    /// render-setup thread, the render loop, and the UI scheduler). The audio
    /// callback never takes the lock — it reads the volatile slot arrays published
    /// by the rebuilds. PCM crosses into the planner only as a frozen/owned array.
    /// </summary>
    public sealed class MixPlanner {
        // ==================== cache ====================

        private sealed class CachedPcm {
            public Frozen<float> pcm;
            public double posMs;
            public double durMs;
            public int channels;
        }

        private readonly Dictionary<UPart, Dictionary<ulong, CachedPcm>> cache =
            new Dictionary<UPart, Dictionary<ulong, CachedPcm>>();

        // ==================== session ====================

        /// <summary>
        /// One PCM placement in the session. The spec list order within a track is
        /// the mix order (part order, then phrase order). Wave parts use PhraseHash 0.
        /// </summary>
        public readonly struct SlotSpec {
            public readonly UPart Part;
            public readonly int TrackNo;
            public readonly ulong PhraseHash;
            public readonly double OffsetMs;
            public readonly double EstimatedLengthMs;
            public readonly int Channels;

            public SlotSpec(UPart part, int trackNo, ulong phraseHash,
                    double offsetMs, double estimatedLengthMs, int channels) {
                Part = part;
                TrackNo = trackNo;
                PhraseHash = phraseHash;
                OffsetMs = offsetMs;
                EstimatedLengthMs = estimatedLengthMs;
                Channels = channels;
            }
        }

        private sealed class PartSession {
            public readonly UPart part;
            public int trackNo;

            public PartSession(UPart part, int trackNo) {
                this.part = part;
                this.trackNo = trackNo;
            }
            // Parallel to <see cref="samples"/>: the placement of each slot.
            public readonly List<(ulong hash, double offsetMs, double estimatedLengthMs, int channels)> specs =
                new List<(ulong, double, double, int)>();
            public readonly List<SampleSlot> samples = new List<SampleSlot>();
        }

        private sealed class TrackState {
            public readonly int TrackNo;
            public readonly SlotMixSource Source = new SlotMixSource();

            public TrackState(int trackNo) {
                TrackNo = trackNo;
            }
        }

        private readonly object lockObj = new object();
        private List<PartSession> sessionParts; // null: no active session
        private readonly Dictionary<int, TrackState> tracks = new Dictionary<int, TrackState>();
        private readonly List<TrackState> trackOrder = new List<TrackState>();
        // Parts whose last render pass finished every phrase, with the phrase hashes
        // that completion covered. Content-keyed: edited phrases are no longer in the
        // set, so the part reads as unrendered again.
        private readonly Dictionary<UPart, HashSet<ulong>> completeParts = new Dictionary<UPart, HashSet<ulong>>();

        // ==================== session setup ====================

        /// <summary>
        /// Starts a playback session. Every spec becomes a slot: Ready with cached
        /// pcm when the cache holds the (part, hash) entry, Pending otherwise.
        /// </summary>
        public void BeginSession(IReadOnlyList<SlotSpec> specs) {
            lock (lockObj) {
                trackOrder.Clear();
                tracks.Clear();
                sessionParts = new List<PartSession>();
                var partByRef = new Dictionary<UPart, PartSession>();
                foreach (var spec in specs) {
                    if (!tracks.TryGetValue(spec.TrackNo, out var track)) {
                        track = new TrackState(spec.TrackNo);
                        tracks[spec.TrackNo] = track;
                        trackOrder.Add(track);
                    }
                    if (!partByRef.TryGetValue(spec.Part, out var part)) {
                        part = new PartSession(spec.Part, spec.TrackNo);
                        partByRef[spec.Part] = part;
                        sessionParts.Add(part);
                    }
                    part.specs.Add((spec.PhraseHash, spec.OffsetMs, spec.EstimatedLengthMs, spec.Channels));
                    SampleSlot slot;
                    if (TryGetCache(spec.Part, spec.PhraseHash, out var cached)) {
                        slot = new SampleSlot(spec.OffsetMs, spec.EstimatedLengthMs, spec.Channels,
                            cached.pcm, SlotState.Ready);
                    } else {
                        slot = new SampleSlot(spec.OffsetMs, spec.EstimatedLengthMs, spec.Channels);
                    }
                    part.samples.Add(slot);
                }
                RebuildAll();
            }
        }

        /// <summary>
        /// The track's slot source. Tracks with no specs (empty tracks) get an
        /// empty slot array — IsReady true, Mix 0.
        /// </summary>
        public SlotMixSource GetTrackSource(int trackNo) {
            lock (lockObj) {
                if (!tracks.TryGetValue(trackNo, out var track)) {
                    track = new TrackState(trackNo);
                    tracks[trackNo] = track;
                    trackOrder.Add(track);
                    track.Source.SetSlots(Array.Empty<SampleSlot>());
                }
                return track.Source;
            }
        }

        // ==================== pcm publication ====================

        /// <summary>
        /// Publishes one rendered phrase. Takes ownership of <paramref name="pcm"/>.
        /// Updates the cache and, when the part is in the active session, flips the
        /// matching slot Ready.
        /// </summary>
        public void RegisterPcm(UPart part, ulong phraseHash,
                double posMs, double durMs, int channels, float[] pcm) {
            lock (lockObj) {
                Frozen<float> frozen = pcm.Freeze();
                if (!cache.TryGetValue(part, out var partCache)) {
                    partCache = new Dictionary<ulong, CachedPcm>();
                    cache[part] = partCache;
                }
                partCache[phraseHash] = new CachedPcm { pcm = frozen, posMs = posMs, durMs = durMs, channels = channels };
                if (sessionParts != null) {
                    RebuildPartSlots(part, phraseHash, frozen);
                }
            }
        }

        /// <summary>Publishes a wave part's trimmed pcm (hash 0).</summary>
        public void RegisterWavePcm(UPart part, double posMs, double durMs, int channels, float[] pcm) {
            RegisterPcm(part, 0, posMs, durMs, channels, pcm);
        }

        // ==================== invalidation ====================

        /// <summary>
        /// Marks a phrase failed: drops its cached pcm and flips the session slot to
        /// Failed. A Failed slot never blocks playback; it contributes silence.
        /// </summary>
        public void MarkFailed(UPart part, ulong phraseHash) {
            lock (lockObj) {
                if (cache.TryGetValue(part, out var partCache)) {
                    partCache.Remove(phraseHash);
                }
                if (sessionParts != null) {
                    for (int p = 0; p < sessionParts.Count; ++p) {
                        var ps = sessionParts[p];
                        if (!ReferenceEquals(ps.part, part)) {
                            continue;
                        }
                        for (int i = 0; i < ps.samples.Count; ++i) {
                            if (ps.specs[i].hash == phraseHash) {
                                var spec = ps.specs[i];
                                ps.samples[i] = new SampleSlot(spec.offsetMs, spec.estimatedLengthMs, spec.channels,
                                    null, SlotState.Failed);
                            }
                        }
                    }
                    RebuildAll();
                }
            }
        }

        /// <summary>
        /// "Clear phrase cache" for one part: drops every cached pcm and puts all
        /// session slots back to Pending. The part stays in the session so the next
        /// render pass can re-register.
        /// </summary>
        public void EvictPart(UPart part) {
            lock (lockObj) {
                cache.Remove(part);
                completeParts.Remove(part);
                if (sessionParts != null) {
                    for (int p = 0; p < sessionParts.Count; ++p) {
                        var ps = sessionParts[p];
                        if (!ReferenceEquals(ps.part, part)) {
                            continue;
                        }
                        for (int i = 0; i < ps.samples.Count; ++i) {
                            var spec = ps.specs[i];
                            ps.samples[i] = new SampleSlot(spec.offsetMs, spec.estimatedLengthMs, spec.channels);
                        }
                    }
                    RebuildAll();
                }
            }
        }

        /// <summary>Drops the cache and the session (project load).</summary>
        public void Clear() {
            lock (lockObj) {
                cache.Clear();
                completeParts.Clear();
                sessionParts = null;
                tracks.Clear();
                trackOrder.Clear();
            }
        }

        // ==================== completeness ====================

        /// <summary>
        /// Called by the render pass at the moment every phrase of a part has samples.
        /// </summary>
        public void MarkPartComplete(UPart part, IEnumerable<ulong> phraseHashes) {
            lock (lockObj) {
                completeParts[part] = new HashSet<ulong>(phraseHashes);
            }
        }

        /// <summary>
        /// Whether the part can serve a full, correct extraction (DAW gate). When the
        /// active session covers the part, every session slot must be Ready. Otherwise
        /// a completion mark must exist and cover every one of the part's current
        /// <paramref name="currentHashes"/> (content hashes, so an edited part is
        /// incomplete until re-rendered).
        /// </summary>
        public bool IsPartReady(UPart part, IEnumerable<ulong> currentHashes = null) {
            lock (lockObj) {
                if (sessionParts != null) {
                    for (int p = 0; p < sessionParts.Count; ++p) {
                        var ps = sessionParts[p];
                        if (!ReferenceEquals(ps.part, part)) {
                            continue;
                        }
                        foreach (var s in ps.samples) {
                            if (s.State != SlotState.Ready) {
                                return false;
                            }
                        }
                        return true;
                    }
                }
                if (currentHashes == null) {
                    return false;
                }
                var hashes = currentHashes as ICollection<ulong> ?? currentHashes.ToList();
                if (hashes.Count == 0) {
                    return false;
                }
                if (!completeParts.TryGetValue(part, out var done)) {
                    return false;
                }
                foreach (var h in hashes) {
                    if (!done.Contains(h)) {
                        return false;
                    }
                }
                return true;
            }
        }

        // ==================== readers (UI / DAW) ====================

        /// <summary>
        /// Content lookup: the rendered pcm for one (part, phrase) pair, or the
        /// wave part's whole placement under hash 0. The store is
        /// content-addressed; callers decide which entries are current by asking
        /// about their own phrases — stale entries are simply never asked about.
        /// </summary>
        public bool TryGetPhrasePcm(UPart part, ulong phraseHash,
                out (double posMs, double durMs, int channels, Frozen<float> pcm) placement) {
            lock (lockObj) {
                if (cache.TryGetValue(part, out var partCache) &&
                    partCache.TryGetValue(phraseHash, out var c)) {
                    placement = (c.posMs, c.durMs, c.channels, c.pcm);
                    return true;
                }
                placement = default;
                return false;
            }
        }

        /// <summary>
        /// One placement per phrase the caller currently has, when its pcm has
        /// rendered. Geometry comes from the caller's live phrase ranges, never
        /// from the store, and the playback session is not involved: display and
        /// extraction follow the document, not the playback that happens to run.
        /// </summary>
        public static bool TryGetPartPlacements(MixPlanner planner, UPart part,
                IEnumerable<(ulong hash, double startMs, double endMs)> phrases,
                out List<(double posMs, double durMs, int channels, Frozen<float> pcm)> pcmList) {
            pcmList = new List<(double, double, int, Frozen<float>)>();
            if (part is UWavePart) {
                if (planner.TryGetPhrasePcm(part, 0, out var wave)) {
                    pcmList.Add(wave);
                }
                return pcmList.Count > 0;
            }
            foreach (var (hash, startMs, endMs) in phrases) {
                if (planner.TryGetPhrasePcm(part, hash, out var c)) {
                    pcmList.Add((startMs, endMs - startMs, c.channels, c.pcm));
                }
            }
            return pcmList.Count > 0;
        }

        // ==================== internals ====================

        private bool TryGetCache(UPart part, ulong phraseHash, out CachedPcm pcm) {
            pcm = null;
            return cache.TryGetValue(part, out var partCache) && partCache.TryGetValue(phraseHash, out pcm);
        }

        private void RebuildPartSlots(UPart part, ulong phraseHash, Frozen<float> frozen) {
            for (int p = 0; p < sessionParts.Count; ++p) {
                var ps = sessionParts[p];
                if (!ReferenceEquals(ps.part, part)) {
                    continue;
                }
                for (int i = 0; i < ps.samples.Count; ++i) {
                    if (ps.specs[i].hash == phraseHash) {
                        var spec = ps.specs[i];
                        ps.samples[i] = new SampleSlot(spec.offsetMs, spec.estimatedLengthMs, spec.channels,
                            frozen, SlotState.Ready);
                    }
                }
            }
            RebuildAll();
        }

        /// <summary>
        /// Publishes a fresh slot array per track. Order within a track follows the
        /// spec order (part order, then phrase order); a part's wave slot (hash 0,
        /// added last by the engine) therefore comes after its phrases.
        /// </summary>
        private void RebuildAll() {
            foreach (var track in trackOrder) {
                var list = new List<SampleSlot>();
                for (int p = 0; p < sessionParts.Count; ++p) {
                    var ps = sessionParts[p];
                    if (ps.trackNo != track.TrackNo) {
                        continue;
                    }
                    list.AddRange(ps.samples);
                }
                track.Source.SetSlots(list.ToArray());
            }
        }
    }
}
