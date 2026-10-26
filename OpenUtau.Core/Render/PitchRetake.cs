using System;
using System.Collections.Generic;

namespace OpenUtau.Core.Render {
    public static class PitchRetake {
        public static HashSet<int> MapSelectedPositionsToNoteIndexes(
            int phrasePosition,
            IReadOnlyList<int> noteRelativePositions,
            IReadOnlyCollection<int>? selectedAbsolutePositions) {
            var result = new HashSet<int>();
            if (selectedAbsolutePositions == null || selectedAbsolutePositions.Count == 0) {
                return result;
            }
            var lookup = selectedAbsolutePositions as ISet<int> ?? new HashSet<int>(selectedAbsolutePositions);
            for (int i = 0; i < noteRelativePositions.Count; i++) {
                if (lookup.Contains(phrasePosition + noteRelativePositions[i])) {
                    result.Add(i);
                }
            }
            return result;
        }

        /// <summary>
        /// Write-back mask for renderers that regenerate the whole phrase (no retakeMask):
        /// true only for frames owned by the selected notes, so pitch drawn on other notes
        /// in the phrase is kept. Ownership follows DiffSinger's retake: a gap belongs to the
        /// preceding note, frames before the first note to the first note.
        /// Returns null (write back everything) when none or all notes are selected.
        /// </summary>
        /// <param name="ticks">Frame ticks relative to the phrase, in ascending order.</param>
        public static bool[]? BuildWriteBackMask(
            int phrasePosition,
            IReadOnlyList<int> noteRelativePositions,
            IReadOnlyCollection<int>? selectedAbsolutePositions,
            IReadOnlyList<float> ticks) {
            var retakeNoteIndexes = MapSelectedPositionsToNoteIndexes(
                phrasePosition, noteRelativePositions, selectedAbsolutePositions);
            if (retakeNoteIndexes.Count == 0 || retakeNoteIndexes.Count == noteRelativePositions.Count) {
                return null;
            }
            var mask = new bool[ticks.Count];
            int owner = 0;
            for (int i = 0; i < ticks.Count; i++) {
                while (owner + 1 < noteRelativePositions.Count && noteRelativePositions[owner + 1] <= ticks[i]) {
                    owner++;
                }
                mask[i] = retakeNoteIndexes.Contains(owner);
            }
            return mask;
        }

        /// <summary>
        /// Per-frame blend weights for <see cref="BuildWriteBackMask"/>: 0 keeps the existing pitch,
        /// 1 uses the new pitch. Each written range fades in from the kept frames next to it
        /// over <paramref name="fadeMs"/>, measured from its first/last voiced frame, and never
        /// longer than the range allows. A side touching the phrase start or end does not fade.
        /// </summary>
        public static float[] BuildCrossfadeWeights(
            IReadOnlyList<bool> writeMask,
            IReadOnlyList<double> frameMs,
            IReadOnlyList<bool>? voiced,
            double fadeMs) {
            int count = Math.Min(writeMask.Count, frameMs.Count);
            var weights = new float[count];
            foreach (var (start, end) in GetRetakeFrameRanges(writeMask, count)) {
                bool fadeIn = start > 0;
                bool fadeOut = end < count;
                int first = start;
                int last = end - 1;
                if (voiced != null) {
                    while (first <= last && first < voiced.Count && !voiced[first]) {
                        first++;
                    }
                    while (last >= first && last < voiced.Count && !voiced[last]) {
                        last--;
                    }
                }
                int sides = (fadeIn ? 1 : 0) + (fadeOut ? 1 : 0);
                double fade = first <= last && sides > 0
                    ? Math.Min(fadeMs, (frameMs[last] - frameMs[first]) / sides)
                    : 0;
                for (int i = start; i < end; i++) {
                    double w = 1;
                    if (fade > 0) {
                        if (fadeIn) {
                            w = Math.Min(w, (frameMs[i] - frameMs[first]) / fade);
                        }
                        if (fadeOut) {
                            w = Math.Min(w, (frameMs[last] - frameMs[i]) / fade);
                        }
                    }
                    weights[i] = (float)Math.Clamp(w, 0, 1);
                }
            }
            return weights;
        }

        /// <summary>
        /// Splits <see cref="RenderPitchResult.retakeMask"/> into contiguous frame ranges to write back.
        /// A null mask means the whole phrase was retaken.
        /// </summary>
        public static IEnumerable<(int start, int end)> GetRetakeFrameRanges(
            IReadOnlyList<bool>? retakeMask, int frameCount) {
            if (retakeMask == null) {
                if (frameCount > 0) {
                    yield return (0, frameCount);
                }
                yield break;
            }
            int limit = Math.Min(retakeMask.Count, frameCount);
            int start = -1;
            for (int i = 0; i < limit; i++) {
                if (retakeMask[i]) {
                    if (start < 0) {
                        start = i;
                    }
                } else if (start >= 0) {
                    yield return (start, i);
                    start = -1;
                }
            }
            if (start >= 0) {
                yield return (start, limit);
            }
        }
    }
}
