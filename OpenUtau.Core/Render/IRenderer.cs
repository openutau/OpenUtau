using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    public class NoResamplerException : Exception { }
    public class NoWavtoolException : Exception { }
    public class ResamplerFailedException : Exception {
        public ResamplerFailedException(string message) : base(message) {}
    }

    /// <summary>
    /// Render result of a phrase.
    /// </summary>
    public class RenderResult {
        public float[] samples;

        /// <summary>
        /// The length of leading samples.
        /// </summary>
        public double leadingMs;

        /// <summary>
        /// Start position of non-leading samples.
        /// </summary>
        public double positionMs;

        /// <summary>
        /// Length estimated before actual render.
        /// </summary>
        public double estimatedLengthMs;
    }

    public class RenderPitchResult {
        /// <summary>
        /// Ticks relative to the start of the phrase.
        /// </summary>
        public float[] ticks;

        /// <summary>
        /// Semitone values in MIDI scale.
        /// </summary>
        public float[] tones;

        /// <summary>
        /// Per-frame mask indicating retaken frames. Null means full retake.
        /// </summary>
        public bool[]? retakeMask;
    }

    public class RenderRealCurveResult {
        /// <summary>
        /// Abbreviation of the expression.
        /// </summary>
        public string abbr;

        /// <summary>
        /// Ticks relative to the start of the phrase.
        /// </summary>
        public float[] ticks;

        /// <summary>
        /// Values normalized between 0 and 1.
        /// </summary>
        public float[] values;
    }

    public class RenderPhraseEvents {
        readonly Action<IReadOnlyList<RenderRealCurveResult>>? realCurvesCallback;

        public RenderPhraseEvents(Action<IReadOnlyList<RenderRealCurveResult>>? realCurvesCallback = null) {
            this.realCurvesCallback = realCurvesCallback;
        }

        public void ReportRealCurves(IReadOnlyList<RenderRealCurveResult> realCurves) {
            if (realCurves.Count > 0) {
                realCurvesCallback?.Invoke(realCurves);
            }
        }
    }

    /// <summary>
    /// Interface of phrase-based renderer.
    /// </summary>
    public interface IRenderer {
        USingerType SingerType { get; }
        bool SupportsRenderPitch { get; }
        bool SupportsRealCurve { get { return false; } }
        bool SupportsExpression(UExpressionDescriptor descriptor);
        RenderResult Layout(RenderPhrase phrase);

        /// <summary>
        /// How much (ms) this renderer pads the rendered audio before the first
        /// phoneme (head) and after the last phoneme (tail) of a phrase.
        /// </summary>
        (double HeadMs, double TailMs) PhrasePadding(USinger singer, IEnumerable<UPhoneme> phonemes) { return (0, 0); }

        /// <summary>
        /// Whether two adjacent phoneme groups (prev then next, separated by a
        /// gap) should stay in one phrase because their padded audio overlaps.
        /// </summary>
        bool ShouldMergePhrases(UProject project, UTrack track, UPhoneme prev, UPhoneme next) {
            if (prev == null || next == null) {
                return false;
            }
            double gapMs = next.PositionMs - prev.EndMs;
            var (_, tailMs) = PhrasePadding(track.Singer, new[] { prev });
            var (headMs, _) = PhrasePadding(track.Singer, new[] { next });
            return gapMs < headMs + tailMs;
        }

        Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender = false, RenderPhraseEvents? renderEvents = null);
        RenderPitchResult LoadRenderedPitch(RenderPhrase phrase);
        RenderPitchResult LoadRenderedPitch(RenderPhrase phrase, HashSet<int> selectedNotePositions) { return LoadRenderedPitch(phrase); }
        List<RenderRealCurveResult> LoadRenderedRealCurves(RenderPhrase phrase) { return new List<RenderRealCurveResult>(0);}
        void ScheduleRealCurveRefresh(UProject project, UVoicePart part, UCommand command) { }
        UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings);
    }
}
