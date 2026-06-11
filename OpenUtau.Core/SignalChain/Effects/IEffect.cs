namespace OpenUtau.Core.SignalChain.Effects {
    /// <summary>
    /// In-place audio effect.  All buffers are interleaved float, channels stereo (2).
    /// The effect MUST be able to process arbitrary buffer sizes; internal state is
    /// retained across calls so streaming and offline rendering both work.
    /// </summary>
    public interface IEffect {
        /// <summary>Apply the effect in place.</summary>
        /// <param name="buffer">Interleaved float audio buffer.</param>
        /// <param name="offset">Start index into <paramref name="buffer"/>.</param>
        /// <param name="count">Sample count (frames * channels).</param>
        void Process(float[] buffer, int offset, int count);

        /// <summary>Drop any time-domain state.  Called when the playback position seeks.</summary>
        void Reset();

        /// <summary>True when the effect produces nothing different from the input.</summary>
        bool IsBypassed { get; }
    }
}
