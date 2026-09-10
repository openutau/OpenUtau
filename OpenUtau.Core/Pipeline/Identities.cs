using System;
using System.Collections.Generic;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Pipeline {
    public readonly record struct PartId(Guid Value) : IComparable<PartId> {
        public static PartId New() => new(Guid.NewGuid());
        public int CompareTo(PartId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString("N");
    }

    public readonly record struct DocRevision(long Value) : IComparable<DocRevision> {
        public int CompareTo(DocRevision other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString();
    }

    public enum ImpactKind {
        /// <summary>The command touches no render-relevant state.</summary>
        None = 0,
        /// <summary>One part's structure changed (notes, phonemes, grouping).</summary>
        Part = 1,
        /// <summary>Curve points of one part changed (feeds phrase source and derived data).</summary>
        Curves = 2,
        /// <summary>Only mix parameters changed (volume / pan / mute / fx).</summary>
        Mix = 4,
        /// <summary>A track's configuration changed (singer, renderer, expressions).</summary>
        Track = 8,
        /// <summary>Project-level change (time axis, project expressions); everything is affected.</summary>
        Project = 16,
    }

    /// <summary>
    /// The blast radius a command declares, driving snapshot invalidation.
    /// </summary>
    public readonly struct ImpactSet {
        public readonly ImpactKind Kind;
        public readonly UPart Part;
        public readonly UTrack Track;
        public readonly IReadOnlyList<string> CurveAbbrs;

        public ImpactSet(ImpactKind kind, UPart part = null, UTrack track = null,
                IReadOnlyList<string> curveAbbrs = null) {
            Kind = kind;
            Part = part;
            Track = track;
            CurveAbbrs = curveAbbrs;
        }

        public static ImpactSet All { get; } = new ImpactSet(ImpactKind.Project);

        public static ImpactSet None { get; } = new ImpactSet(ImpactKind.None);

        public static ImpactSet PartOf(UPart part) => new ImpactSet(ImpactKind.Part, part);

        public static ImpactSet CurvesOf(UPart part, params string[] abbrs) =>
            new ImpactSet(ImpactKind.Curves, part, curveAbbrs: abbrs);

        public static ImpactSet MixOnly { get; } = new ImpactSet(ImpactKind.Mix);

        public static ImpactSet TrackOf(UTrack track) => new ImpactSet(ImpactKind.Track, track: track);
    }
}
