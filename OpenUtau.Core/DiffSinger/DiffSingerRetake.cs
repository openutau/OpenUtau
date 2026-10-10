using System;
using System.Collections.Generic;

namespace OpenUtau.Core.DiffSinger {
    public static class DiffSingerRetake {
        // paddedToRealNoteIndex must be the same length as paddedNoteDurations.
        // Each entry is the real-note index the padded segment should follow for retake purposes,
        // or -1 for a segment that is never retaken regardless of selection.
        public static bool[] BuildRetakeFrameMask(
            IReadOnlyList<int> paddedNoteDurations,
            IReadOnlyList<int> paddedToRealNoteIndex,
            IReadOnlyCollection<int>? retakeNoteIndexes,
            int totalFrames) {
            var mask = new bool[totalFrames];
            if (retakeNoteIndexes == null || retakeNoteIndexes.Count == 0 || paddedNoteDurations.Count == 0) {
                return mask;
            }
            var lookup = retakeNoteIndexes as ISet<int> ?? new HashSet<int>(retakeNoteIndexes);
            int padded = paddedNoteDurations.Count;
            int frameOffset = 0;
            for (int segIdx = 0; segIdx < padded; segIdx++) {
                int realIdx = paddedToRealNoteIndex[segIdx];
                bool shouldRetake = realIdx >= 0 && lookup.Contains(realIdx);
                int dur = paddedNoteDurations[segIdx];
                for (int f = 0; f < dur; f++) {
                    int fi = frameOffset + f;
                    if (fi < totalFrames) {
                        mask[fi] = shouldRetake;
                    }
                }
                frameOffset += dur;
            }
            return mask;
        }
    }
}
