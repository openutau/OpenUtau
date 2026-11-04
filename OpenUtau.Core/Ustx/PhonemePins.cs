using System.Collections.Generic;

namespace OpenUtau.Core.Ustx {
    /// <summary>
    /// Part-relative tick positions to hold phonemes at, keyed by note and phoneme index.
    /// </summary>
    internal class PhonemePins {
        Dictionary<(UNote, int), int> positions;

        /// <summary>
        /// The edit that pinned the phonemes is over: drop the pins after the next phonemizer output.
        /// </summary>
        public bool Released { get; set; }

        public PhonemePins(Dictionary<(UNote, int), int> positions) {
            this.positions = positions;
        }

        /// <summary>Moves the pins, as when the pinned notes move while being dragged.</summary>
        public void Set(Dictionary<(UNote, int), int> positions) {
            this.positions = positions;
        }

        /// <summary>
        /// The pinned position of a phoneme. A phoneme can't be held at or past the end of its note
        /// and the notes extending it, as it would have no length there, so such a phoneme goes where
        /// the phonemizer puts it.
        /// </summary>
        public bool TryGet(UPhoneme phoneme, out int position) {
            var note = phoneme.Parent;
            if (note != null && positions.TryGetValue((note, phoneme.index), out position) && position < note.ExtendedEnd) {
                return true;
            }
            position = 0;
            return false;
        }
    }
}
