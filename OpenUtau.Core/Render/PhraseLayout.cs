namespace OpenUtau.Core.Render {
    /// <summary>
    /// The [StartMs, EndMs) range (absolute ms) of a phrase's rendered audio,
    /// including the leading pre-utter and the release tail, matching the slot
    /// layout used by the mix. Precomputed once when the phrase is built: a
    /// phrase is immutable and its layout depends only on the phrase and its
    /// renderer, both of which are fixed for the phrase's lifetime.
    /// </summary>
    public readonly struct PhraseLayout {
        public readonly double StartMs;
        public readonly double EndMs;
        public readonly double LeadingMs;
        public readonly double EstimatedMs;

        public PhraseLayout(double startMs, double endMs, double leadingMs, double estimatedMs) {
            StartMs = startMs;
            EndMs = endMs;
            LeadingMs = leadingMs;
            EstimatedMs = estimatedMs;
        }

        public (double StartMs, double EndMs) Range => (StartMs, EndMs);
    }
}
