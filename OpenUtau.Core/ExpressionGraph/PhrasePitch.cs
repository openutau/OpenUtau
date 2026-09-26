using System;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>A pitch source a pitch_input node reads, in cents.</summary>
    public enum PitchSource {
        /// <summary>Notes and pitch bends, absolute: the note pitch included, vibrato not.</summary>
        PitchBend,
        /// <summary>Vibrato alone, relative to the pitch bend.</summary>
        Vibrato,
        /// <summary>The classic MOD+ contribution alone.</summary>
        ModPlus,
        /// <summary>Each note's tone and tuning as a step, without bends or vibrato.</summary>
        Notes,
    }

    /// <summary>
    /// A phrase's pitch sources on its 5-tick grid. Pitch is computed per phrase, so it can only be read on the
    /// phrase grid, by curve and pitch outputs.
    /// </summary>
    public sealed class PhrasePitch {
        readonly int start;
        readonly int interval;
        readonly float[] pitchBend;
        readonly float[] vibrato;
        readonly float[] modPlus;
        readonly float[] notes;

        /// <param name="notes">Each note's tone and tuning as a step, without bends or vibrato.</param>
        public PhrasePitch(int start, int interval, float[] pitchBend, float[] vibrato, float[] modPlus, float[] notes) {
            this.start = start;
            this.interval = interval;
            this.pitchBend = pitchBend;
            this.vibrato = vibrato;
            this.modPlus = modPlus;
            this.notes = notes;
        }

        /// <summary>
        /// A source at a part-relative tick; exact on the grid, linear between, held beyond the phrase.
        /// </summary>
        public float Sample(PitchSource source, int tick) {
            var values = source switch {
                PitchSource.PitchBend => pitchBend,
                PitchSource.Vibrato => vibrato,
                PitchSource.ModPlus => modPlus,
                _ => notes,
            };
            if (values.Length == 0) {
                return 0;
            }
            int offset = tick - start;
            if (offset <= 0) {
                return values[0];
            }
            int i = offset / interval;
            if (i >= values.Length - 1) {
                return values[^1];
            }
            int rest = offset - i * interval;
            return rest == 0 ? values[i] : values[i] + (values[i + 1] - values[i]) * rest / interval;
        }

        public static bool TryParse(string? text, out PitchSource source) {
            source = text switch {
                null or "" or "pitch_bend" => PitchSource.PitchBend,
                "vibrato" => PitchSource.Vibrato,
                "mod_plus" => PitchSource.ModPlus,
                "notes" => PitchSource.Notes,
                _ => (PitchSource)(-1),
            };
            return (int)source >= 0;
        }
    }
}
