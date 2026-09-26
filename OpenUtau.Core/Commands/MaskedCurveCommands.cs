using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    /// <summary>A change to one masked curve of a part, undone by restoring a copy of the curve.</summary>
    public abstract class MaskedCurveCommand : ExpCommand {
        readonly UMaskedCurve? before;

        protected MaskedCurveCommand(UVoicePart part, string abbr) : base(part) {
            Key = abbr;
            before = part.maskedCurves.FirstOrDefault(c => c.abbr == abbr)?.Clone();
        }

        public override ValidateOptions ValidateOptions => new ValidateOptions {
            SkipTiming = true,
            Part = Part,
            SkipPhonemizer = true,
            SkipPhoneme = true,
        };

        public override void Execute() {
            var curve = Part.maskedCurves.FirstOrDefault(c => c.abbr == Key);
            if (curve == null) {
                curve = new UMaskedCurve(Key);
                Part.maskedCurves.Add(curve);
            }
            Change(curve);
            // A curve without values isn't kept.
            if (curve.IsEmpty) {
                Part.maskedCurves.Remove(curve);
            }
        }

        public override void Unexecute() {
            Part.maskedCurves.RemoveAll(c => c.abbr == Key);
            if (before != null) {
                Part.maskedCurves.Add(before.Clone());
            }
        }

        protected abstract void Change(UMaskedCurve curve);
    }

    /// <summary>Draws a straight line of values on a masked curve.</summary>
    public class SetMaskedCurveCommand : MaskedCurveCommand {
        readonly int x0, x1;
        readonly float y0, y1;

        public SetMaskedCurveCommand(UVoicePart part, string abbr, int x0, float y0, int x1, float y1) : base(part, abbr) {
            (this.x0, this.y0, this.x1, this.y1) = (x0, y0, x1, y1);
        }

        public override string ToString() => "Edit masked curve";
        protected override void Change(UMaskedCurve curve) => curve.Set(x0, y0, x1, y1);
    }

    /// <summary>Sets values of a masked curve at their ticks.</summary>
    public class SetMaskedCurveValuesCommand : MaskedCurveCommand {
        readonly (int x, float y)[] values;

        public SetMaskedCurveValuesCommand(UVoicePart part, string abbr, System.Collections.Generic.IEnumerable<(int x, float y)> values) : base(part, abbr) {
            this.values = values.ToArray();
        }

        public override string ToString() => "Edit masked curve";
        protected override void Change(UMaskedCurve curve) => curve.SetValues(values);
    }

    /// <summary>Removes the values of a masked curve between two ticks.</summary>
    public class ClearMaskedCurveCommand : MaskedCurveCommand {
        readonly int x0, x1;

        public ClearMaskedCurveCommand(UVoicePart part, string abbr, int x0, int x1) : base(part, abbr) {
            (this.x0, this.x1) = (x0, x1);
        }

        public override string ToString() => "Clear masked curve";
        protected override void Change(UMaskedCurve curve) => curve.Clear(x0, x1);
    }

    /// <summary>Replaces all values of a masked curve.</summary>
    public class ReplaceMaskedCurveCommand : MaskedCurveCommand {
        readonly UMaskedCurve value;

        public ReplaceMaskedCurveCommand(UVoicePart part, string abbr, UMaskedCurve value) : base(part, abbr) {
            this.value = value.Clone();
            this.value.abbr = abbr;
        }

        public override string ToString() => "Replace masked curve";
        protected override void Change(UMaskedCurve curve) => curve.runs = value.Clone().runs;
    }
}
