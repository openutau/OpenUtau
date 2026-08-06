// Made And Checked By DELTA SYNTH & Gemini AI
// Original by DELTA
// Version: v1.1 | Date: 2026-08-03
// Summary: AutoPitchRandomizerBatchEdit — six pitch patterns with an optional per-note random mix.
using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Editing {
    public class AutoPitchRandomizerBatchEdit : BatchEdit {
        public const int RandomPattern = -1;
        public const int PatternCount = 6;

        private const double PointSpacingMs = 125.0;
        private const int MaximumSegments = 20;

        private readonly int pattern;
        private readonly double intensity;

        public AutoPitchRandomizerBatchEdit(int pattern, double intensity) {
            if (pattern < RandomPattern || pattern >= PatternCount) {
                throw new ArgumentOutOfRangeException(nameof(pattern));
            }
            if (!double.IsFinite(intensity)) {
                throw new ArgumentOutOfRangeException(nameof(intensity));
            }
            this.pattern = pattern;
            this.intensity = Math.Clamp(intensity, 0, 100);
        }

        public string Name => pattern == RandomPattern
            ? "AutoPitch All in 1"
            : "Auto Pitch Randomizer";

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            if (notes.Count == 0) {
                return;
            }

            var random = new Random();
            docManager.StartUndoGroup("command.batch.note", true);
            try {
                foreach (var note in notes) {
                    for (int i = note.pitch.data.Count - 1; i >= 0; i--) {
                        docManager.ExecuteCmd(new DeletePitchPointCommand(part, note, i));
                    }

                    double durationMs = project.timeAxis.MsBetweenTickPos(
                        part.position + note.position,
                        part.position + note.End);
                    int currentPattern = ResolvePattern(pattern, random);
                    var points = GeneratePatternPoints(currentPattern, durationMs, intensity, random);
                    for (int i = 0; i < points.Count; i++) {
                        docManager.ExecuteCmd(new AddPitchPointCommand(part, note, points[i], i));
                    }
                }
                docManager.ExecuteCmd(new ShowPitchNotification());
            } finally {
                docManager.EndUndoGroup();
            }
        }

        internal static int ResolvePattern(int requestedPattern, Random random) {
            if (requestedPattern < RandomPattern || requestedPattern >= PatternCount) {
                throw new ArgumentOutOfRangeException(nameof(requestedPattern));
            }
            ArgumentNullException.ThrowIfNull(random);
            return requestedPattern == RandomPattern
                ? random.Next(PatternCount)
                : requestedPattern;
        }

        internal static List<PitchPoint> GeneratePatternPoints(
            int pattern,
            double durationMs,
            double intensity,
            Random random) {
            if (pattern < 0 || pattern >= PatternCount) {
                throw new ArgumentOutOfRangeException(nameof(pattern));
            }
            if (!double.IsFinite(durationMs) || durationMs < 0) {
                throw new ArgumentOutOfRangeException(nameof(durationMs));
            }
            if (!double.IsFinite(intensity)) {
                throw new ArgumentOutOfRangeException(nameof(intensity));
            }
            ArgumentNullException.ThrowIfNull(random);

            float maxDeviation = (float)Math.Clamp(intensity, 0, 100);
            if (durationMs == 0) {
                return new List<PitchPoint> {
                    new PitchPoint(0, 0, PitchPointShape.io),
                };
            }
            int segments = Math.Clamp(
                (int)Math.Round(durationMs / PointSpacingMs),
                2,
                MaximumSegments);
            var points = new List<PitchPoint>(segments + 1);

            float currentY = 0;
            for (int i = 0; i <= segments; i++) {
                float progress = (float)i / segments;
                float x = (float)(durationMs * progress);
                float y;
                PitchPointShape shape;

                switch (pattern) {
                    case 0: // Wave
                        y = i == 0 || i == segments
                            ? 0
                            : (i % 2 == 0 ? maxDeviation : -maxDeviation);
                        shape = PitchPointShape.io;
                        break;
                    case 1: // Drunk walk
                        if (i == 0 || i == segments) {
                            currentY = 0;
                        } else {
                            float change = (float)(random.NextDouble() * 2 - 1) * (maxDeviation / 1.5f);
                            currentY = Math.Clamp(currentY + change, -maxDeviation, maxDeviation);
                        }
                        y = currentY;
                        shape = PitchPointShape.io;
                        break;
                    case 2: // Drop
                        y = -maxDeviation * progress;
                        shape = PitchPointShape.o;
                        break;
                    case 3: // Rise
                        y = maxDeviation * progress;
                        shape = PitchPointShape.o;
                        break;
                    case 4: // Triangle
                        y = progress <= 0.5f
                            ? maxDeviation * progress * 2
                            : maxDeviation * (1 - progress) * 2;
                        shape = PitchPointShape.l;
                        break;
                    default: // Jitter
                        y = i == 0 || i == segments
                            ? 0
                            : (float)(random.NextDouble() * 2 - 1) * maxDeviation;
                        shape = PitchPointShape.l;
                        break;
                }

                points.Add(new PitchPoint(x, y, shape));
            }
            return points;
        }
    }
}
