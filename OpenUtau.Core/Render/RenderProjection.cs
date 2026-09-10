using System.Collections.Generic;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    /// <summary>
    /// One phrase of a <see cref="RenderProjection"/>: its content hash, its
    /// precomputed layout and whether its pcm has rendered.
    /// </summary>
    public readonly struct PhraseView {
        public readonly ulong Hash;
        public readonly PhraseLayout Layout;
        public readonly bool Rendered;

        public PhraseView(ulong hash, PhraseLayout layout, bool rendered) {
            Hash = hash;
            Layout = layout;
            Rendered = rendered;
        }
    }

    /// <summary>
    /// The immutable, versioned per-part snapshot of render-derived data: the
    /// document's current phrases, their precomputed layouts and which of them
    /// have rendered pcm. Built and delivered on the UI thread by
    /// <see cref="RenderView"/>. The playback session is not involved — display
    /// follows the document, not the playback that happens to run.
    /// </summary>
    public sealed class RenderProjection {
        public readonly UPart Part;
        public readonly long Revision;
        public readonly IReadOnlyList<PhraseView> Phrases;
        /// <summary>Every current phrase has rendered pcm.</summary>
        public readonly bool Ready;

        public RenderProjection(UPart part, long revision, IReadOnlyList<PhraseView> phrases, bool ready) {
            Part = part;
            Revision = revision;
            Phrases = phrases;
            Ready = ready;
        }
    }
}
