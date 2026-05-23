using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    public class NoResamplerException : Exception { }
    public class NoWavtoolException : Exception { }

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

    /// <summary>
    /// Interface of phrase-based renderer.
    /// </summary>
    public interface IRenderer {
        USingerType SingerType { get; }
        bool SupportsRenderPitch { get; }
        bool SupportsRealCurve { get { return false; } }
        bool SupportsExpression(UExpressionDescriptor descriptor);
        RenderResult Layout(RenderPhrase phrase);
        Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender = false);
        RenderPitchResult LoadRenderedPitch(RenderPhrase phrase);
        List<RenderRealCurveResult> LoadRenderedRealCurves(RenderPhrase phrase) { return new List<RenderRealCurveResult>(0);}
        UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings);
    }
}
