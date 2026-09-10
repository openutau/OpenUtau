using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Pipeline {
    /// <summary>
    /// A copy of the project time axis at one moment; the stamp identifies
    /// the document state it came from.
    /// </summary>
    public sealed class TimeAxisSnapshot {
        public readonly TimeAxis Axis;
        public readonly long Timestamp;

        private TimeAxisSnapshot(TimeAxis axis, long timestamp) {
            Axis = axis.Clone();
            Timestamp = timestamp;
        }

        public static TimeAxisSnapshot Of(TimeAxis axis) => new TimeAxisSnapshot(axis, axis.Timestamp);
    }

    public sealed record SubbankView(string Color, string Suffix);

    public sealed class TrackSnapshot {
        public readonly int TrackNo;
        public readonly string RendererId;
        public readonly string Resampler;
        public readonly string Wavtool;
        public readonly string SingerId;
        public readonly USingerType SingerType;
        public readonly double Volume;
        public readonly double Pan;
        public readonly bool Muted;
        public readonly IReadOnlyList<SubbankView> Subbanks;

        private TrackSnapshot(int trackNo, string rendererId, string resampler, string wavtool,
                string singerId, USingerType singerType, double volume, double pan, bool muted,
                IReadOnlyList<SubbankView> subbanks) {
            TrackNo = trackNo;
            RendererId = rendererId;
            Resampler = resampler;
            Wavtool = wavtool;
            SingerId = singerId;
            SingerType = singerType;
            Volume = volume;
            Pan = pan;
            Muted = muted;
            Subbanks = subbanks;
        }

        public static TrackSnapshot Of(UTrack track) {
            var singer = track.Singer;
            return new TrackSnapshot(
                track.TrackNo,
                track.RendererSettings?.renderer,
                track.RendererSettings?.resampler,
                track.RendererSettings?.wavtool,
                singer?.Id,
                singer?.SingerType ?? USingerType.Classic,
                track.Volume,
                track.Pan,
                track.Muted,
                (singer?.Subbanks ?? Array.Empty<USubbank>())
                    .Select(s => new SubbankView(s.Color, s.Suffix))
                    .ToArray());
        }
    }

    /// <summary>
    /// One part's snapshot at one document revision; <see cref="Source"/> is
    /// null when the part has no usable phonemes yet.
    /// </summary>
    public sealed class PartSnapshot {
        public readonly PartId PartId;
        public readonly int TrackNo;
        public readonly DocRevision Revision;
        public readonly PhraseSource Source;
        public readonly long Generation;

        public PartSnapshot(PartId partId, int trackNo, DocRevision revision,
                PhraseSource source, long generation) {
            PartId = partId;
            TrackNo = trackNo;
            Revision = revision;
            Source = source;
            Generation = generation;
        }
    }

    public sealed class ProjectSnapshot {
        public readonly DocRevision Revision;
        public readonly TimeAxisSnapshot TimeAxis;
        public readonly IReadOnlyList<TrackSnapshot> Tracks;
        public readonly IReadOnlyDictionary<PartId, PartSnapshot> Parts;

        public ProjectSnapshot(DocRevision revision, TimeAxisSnapshot timeAxis,
                IReadOnlyList<TrackSnapshot> tracks, IReadOnlyDictionary<PartId, PartSnapshot> parts) {
            Revision = revision;
            TimeAxis = timeAxis;
            Tracks = tracks;
            Parts = parts;
        }
    }

    /// <summary>
    /// The incremental snapshot builder. Commands invalidate through their
    /// <see cref="ImpactSet"/>; revalidated parts and tracks replace their
    /// entries, untouched ones are kept. Internally locked, so it is also
    /// safe for test hosts that validate off the UI thread.
    /// </summary>
    public sealed class DocumentSnapshotStore : SingletonBase<DocumentSnapshotStore> {
        private readonly object lockObj = new object();
        private DocRevision revision;
        private readonly Dictionary<PartId, PartSnapshot> parts = new Dictionary<PartId, PartSnapshot>();
        private readonly Dictionary<int, TrackSnapshot> tracks = new Dictionary<int, TrackSnapshot>();

        public DocRevision Revision {
            get {
                lock (lockObj) {
                    return revision;
                }
            }
        }

        public void SetRevision(DocRevision newRevision) {
            lock (lockObj) {
                revision = newRevision;
            }
        }

        public void SetTrack(UTrack track) {
            lock (lockObj) {
                tracks[track.TrackNo] = TrackSnapshot.Of(track);
            }
        }

        public void SetPart(UVoicePart part, PhraseSource source) {
            lock (lockObj) {
                parts[part.Id] = new PartSnapshot(
                    part.Id, part.trackNo, revision, source,
                    source?.Generation ?? 0);
            }
        }

        public void RemovePart(PartId id) {
            lock (lockObj) {
                parts.Remove(id);
            }
        }

        /// <summary>
        /// Drops the snapshot entries a command's blast radius invalidates.
        /// Mix-only changes invalidate no snapshot data.
        /// </summary>
        public void Invalidate(ImpactSet impact) {
            lock (lockObj) {
                switch (impact.Kind) {
                    case ImpactKind.None:
                    case ImpactKind.Mix:
                        break;
                    case ImpactKind.Part:
                    case ImpactKind.Curves:
                        if (impact.Part != null) {
                            parts.Remove(impact.Part.Id);
                        }
                        break;
                    case ImpactKind.Track:
                        if (impact.Track != null) {
                            int trackNo = impact.Track.TrackNo;
                            tracks.Remove(trackNo);
                            foreach (var id in parts.Keys
                                    .Where(id => parts[id].TrackNo == trackNo)
                                    .ToArray()) {
                                parts.Remove(id);
                            }
                        }
                        break;
                    case ImpactKind.Project:
                        parts.Clear();
                        break;
                }
            }
        }

        public bool TryGetPart(PartId id, out PartSnapshot snapshot) {
            lock (lockObj) {
                return parts.TryGetValue(id, out snapshot);
            }
        }

        public bool TryGetTrack(int trackNo, out TrackSnapshot snapshot) {
            lock (lockObj) {
                return tracks.TryGetValue(trackNo, out snapshot);
            }
        }

        public ProjectSnapshot Snapshot(UProject project) {
            lock (lockObj) {
                return new ProjectSnapshot(
                    revision,
                    TimeAxisSnapshot.Of(project.timeAxis),
                    tracks.Values.OrderBy(t => t.TrackNo).ToArray(),
                    parts.ToDictionary(p => p.Key, p => p.Value));
            }
        }

        public void ForgetAll() {
            lock (lockObj) {
                parts.Clear();
                tracks.Clear();
            }
        }
    }
}
