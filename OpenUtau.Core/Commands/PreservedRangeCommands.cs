using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    /// <summary>
    /// Replaces the part's preserved tick ranges, the region the take lock must not
    /// re-sample. Deliberately outside the phonemizer and snapshot paths: it only
    /// affects renders, which is why the live render phrases are updated in place.
    /// </summary>
    public class SetPreservedRangesCommand : UCommand {
        readonly UVoicePart part;
        readonly List<UTickRange>? before;
        readonly List<UTickRange>? after;

        public SetPreservedRangesCommand(UVoicePart part, IEnumerable<UTickRange>? ranges) {
            this.part = part;
            before = part.preservedRanges?.Select(r => r.Clone()).ToList();
            after = Normalize(ranges);
        }

        static List<UTickRange>? Normalize(IEnumerable<UTickRange>? ranges) {
            if (ranges == null) {
                return null;
            }
            var merged = new List<UTickRange>();
            foreach (var range in ranges
                    .Where(r => r != null && r.end > r.start)
                    .OrderBy(r => r.start)) {
                if (merged.Count > 0 && range.start <= merged[^1].end) {
                    merged[^1].end = Math.Max(merged[^1].end, range.end);
                } else {
                    merged.Add(range.Clone());
                }
            }
            return merged.Count == 0 ? null : merged;
        }

        public override void Execute() => Apply(after);

        public override void Unexecute() => Apply(before);

        void Apply(List<UTickRange>? ranges) {
            part.preservedRanges = ranges?.Select(r => r.Clone()).ToList();
            var absolute = part.preservedRanges == null
                ? Array.Empty<(int start, int end)>()
                : part.preservedRanges
                    .Select(r => (part.position + r.start, part.position + r.end))
                    .ToArray();
            lock (part) {
                foreach (var phrase in part.renderPhrases) {
                    phrase.preservedRanges = absolute;
                }
            }
        }

        public override string ToString() => "Set preserved ranges";

        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            SkipPhonemizer = true,
            Part = part,
        };

        public override ImpactSet Impact => ImpactSet.None;
    }
}
