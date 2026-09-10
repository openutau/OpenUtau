using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public class TimeAxis {
        class TimeSigSegment {
            public int barPos;
            public int barEnd = int.MaxValue;
            public int tickPos;
            public int tickEnd = int.MaxValue;

            public int beatPerBar;
            public int beatUnit;

            public int ticksPerBar;
            public int ticksPerBeat;
        }

        class TempoSegment {
            public int tickPos;
            public int tickEnd = int.MaxValue;

            public double bpm;
            public int beatPerBar;
            public int beatUnit;

            public double msPos;
            public double msEnd = double.MaxValue;
            public double msPerTick;
            public double ticksPerMs;

            public int Ticks => tickEnd - tickPos;
        }

        const double DefaultBpm = 120.0;
        const int DefaultBeatPerBar = 4;
        const int DefaultBeatUnit = 4;

        readonly List<TimeSigSegment> timeSigSegments = new List<TimeSigSegment>();
        readonly List<TempoSegment> tempoSegments = new List<TempoSegment>();

        public long Timestamp { get; private set; }

        /// <summary>
        /// A bpm is only usable if it can be divided by. Zero, NaN or infinity turns
        /// msPerTick into infinity or NaN, which then poisons msPos of every following
        /// segment and every ms value derived from them.
        /// </summary>
        static bool IsValidBpm(double bpm) {
            return double.IsFinite(bpm) && bpm > 0;
        }

        public void BuildSegments(UProject project) {
            Timestamp = DateTime.Now.ToFileTimeUtc();
            timeSigSegments.Clear();
            for (var i = 0; i < project.timeSignatures.Count; ++i) {
                var timesig = project.timeSignatures[i];
                var posTick = 0;
                if (i > 0) {
                    var lastBarPos = project.timeSignatures[i - 1].barPosition;
                    posTick = timeSigSegments.Last().tickPos
                        + timeSigSegments.Last().ticksPerBar * (timesig.barPosition - lastBarPos);
                } else {
                    if(timesig.barPosition != 0) {
                        throw new Exception("First time signature must be at bar 0.");
                    }
                }
                // A non-positive beat unit divides by zero here, and a non-positive
                // beat per bar makes ticksPerBar zero, which divides by zero later.
                var beatPerBar = timesig.beatPerBar > 0 ? timesig.beatPerBar : DefaultBeatPerBar;
                var beatUnit = timesig.beatUnit > 0 ? timesig.beatUnit : DefaultBeatUnit;
                timeSigSegments.Add(new TimeSigSegment {
                    barPos = timesig.barPosition,
                    tickPos = posTick,
                    beatPerBar = beatPerBar,
                    beatUnit = beatUnit,
                    ticksPerBar = project.resolution * 4 * beatPerBar / beatUnit,
                    ticksPerBeat = project.resolution * 4 / beatUnit,
                });
            }
            if (timeSigSegments.Count == 0) {
                // Without this a project with no time signature leaves every bar/beat
                // lookup with nothing to return, and leaves tempoSegments empty too.
                timeSigSegments.Add(new TimeSigSegment {
                    barPos = 0,
                    tickPos = 0,
                    beatPerBar = DefaultBeatPerBar,
                    beatUnit = DefaultBeatUnit,
                    ticksPerBar = project.resolution * 4 * DefaultBeatPerBar / DefaultBeatUnit,
                    ticksPerBeat = project.resolution * 4 / DefaultBeatUnit,
                });
            }
            for (var i = 0; i < timeSigSegments.Count - 1; ++i) {
                timeSigSegments[i].barEnd = timeSigSegments[i + 1].barPos;
                timeSigSegments[i].tickEnd = timeSigSegments[i + 1].tickPos;
            }

            tempoSegments.Clear();
            tempoSegments.AddRange(timeSigSegments.Select(sigseg => new TempoSegment {
                tickPos = sigseg.tickPos,
                beatPerBar = sigseg.beatPerBar,
                beatUnit = sigseg.beatUnit,
            }));
            for (var i = 0; i < project.tempos.Count; ++i) {
                var tempo = project.tempos[i];
                if (i == 0) {
                    if(tempo.position != 0) {
                        throw new Exception("First tempo must be at tick 0.");
                    }
                }
                var index = tempoSegments.FindIndex(seg => seg.tickPos >= tempo.position);
                if (index < 0) {
                    tempoSegments.Add(new TempoSegment {
                        tickPos = tempo.position,
                        bpm = tempo.bpm,
                        beatPerBar = tempoSegments.Last().beatPerBar,
                        beatUnit = tempoSegments.Last().beatUnit,
                    });
                } else if (tempoSegments[index].tickPos == tempo.position) {
                    tempoSegments[index].bpm = tempo.bpm;
                } else {
                    tempoSegments.Insert(index, new TempoSegment {
                        tickPos = tempo.position,
                        bpm = tempo.bpm,
                        beatPerBar = tempoSegments[index - 1].beatPerBar,
                        beatUnit = tempoSegments[index - 1].beatUnit,
                    });
                }
            }
            if (!IsValidBpm(tempoSegments[0].bpm)) {
                // No tempo at tick 0, or one that cannot be divided by. Everything
                // after inherits from here, so this segment must be valid.
                tempoSegments[0].bpm = DefaultBpm;
            }
            for (var i = 0; i < tempoSegments.Count - 1; ++i) {
                if (!IsValidBpm(tempoSegments[i + 1].bpm)) {
                    tempoSegments[i + 1].bpm = tempoSegments[i].bpm;
                }
                tempoSegments[i].tickEnd = tempoSegments[i + 1].tickPos;
            }
            for (var i = 0; i < tempoSegments.Count; ++i) {
                tempoSegments[i].msPerTick = 60.0 * 1000.0 / (tempoSegments[i].bpm * project.resolution);
                tempoSegments[i].ticksPerMs = tempoSegments[i].bpm * project.resolution / (60.0 * 1000.0);
                if (i > 0) {
                    tempoSegments[i].msPos = tempoSegments[i - 1].msPos + tempoSegments[i - 1].Ticks * tempoSegments[i - 1].msPerTick;
                    tempoSegments[i - 1].msEnd = tempoSegments[i].msPos;
                }
            }
        }

        /// <summary>
        /// Finds the tempo segment containing a tick. Never throws: a tick past the
        /// last segment, or a NaN one, is clamped instead. These lookups run on the
        /// render thread, where an exception takes the whole app down.
        /// </summary>
        TempoSegment TempoSegmentAtTick(double tick) {
            if (!double.IsNaN(tick)) {
                for (var i = 0; i < tempoSegments.Count; ++i) { // TODO: optimize
                    var segment = tempoSegments[i];
                    if (segment.tickPos == tick || segment.tickEnd > tick) {
                        return segment;
                    }
                }
            }
            return tempoSegments[tempoSegments.Count - 1];
        }

        /// <summary>
        /// Ms counterpart of <see cref="TempoSegmentAtTick"/>. Also never throws.
        /// </summary>
        TempoSegment TempoSegmentAtMsPos(double ms) {
            if (!double.IsNaN(ms)) {
                for (var i = 0; i < tempoSegments.Count; ++i) { // TODO: optimize
                    var segment = tempoSegments[i];
                    if (segment.msPos == ms || segment.msEnd > ms) {
                        return segment;
                    }
                }
            }
            return tempoSegments[tempoSegments.Count - 1];
        }

        TimeSigSegment TimeSigSegmentAtTick(int tick) {
            for (var i = 0; i < timeSigSegments.Count; ++i) { // TODO: optimize
                var segment = timeSigSegments[i];
                if (segment.tickPos == tick || segment.tickEnd > tick) {
                    return segment;
                }
            }
            return timeSigSegments[timeSigSegments.Count - 1];
        }

        TimeSigSegment TimeSigSegmentAtBar(int bar) {
            for (var i = 0; i < timeSigSegments.Count; ++i) { // TODO: optimize
                var segment = timeSigSegments[i];
                if (segment.barPos == bar || segment.barEnd > bar) {
                    return segment;
                }
            }
            return timeSigSegments[timeSigSegments.Count - 1];
        }

        public double GetBpmAtTick(int tick) {
            var segment = TempoSegmentAtTick(tick);
            return segment.bpm;
        }

        public double TickPosToMsPos(double tick) {
            var segment = TempoSegmentAtTick(tick);
            return segment.msPos + segment.msPerTick * (tick - segment.tickPos);
        }

        public double MsPosToNonExactTickPos(double ms) {
            var segment = TempoSegmentAtMsPos(ms);
            double tickPos = segment.tickPos + (ms - segment.msPos) * segment.ticksPerMs;
            return tickPos;
        }

        public int MsPosToTickPos(double ms) {
            var segment = TempoSegmentAtMsPos(ms);
            double tickPos = segment.tickPos + (ms - segment.msPos) * segment.ticksPerMs;
            if (!double.IsFinite(tickPos)) {
                // Casting NaN or infinity saturates silently, which hides the bad
                // value. Fall back to the segment start instead.
                return segment.tickPos;
            }
            return (int)Math.Round(Math.Clamp(tickPos, int.MinValue, int.MaxValue));
        }

        public int TicksBetweenMsPos(double msPos, double msEnd) {
            return MsPosToTickPos(msEnd) - MsPosToTickPos(msPos);
        }

        public double MsBetweenTickPos(double tickPos, double tickEnd) {
            return TickPosToMsPos(tickEnd) - TickPosToMsPos(tickPos);
        }

        /// <summary>
        /// Convert ms duration to tick at a given reference tick position
        /// </summary>
        /// <param name="durationMs">Duration in ms, positive value means starting from refTickPos, negative value means ending at refTickPos</param>
        /// <param name="refTickPos">Reference tick position</param>
        /// <returns>Duration in ticks</returns>
        public int MsToTickAt(double offsetMs, int refTickPos) {
            return TicksBetweenMsPos(
                TickPosToMsPos(refTickPos), 
                TickPosToMsPos(refTickPos) + offsetMs);
        }

        public void TickPosToBarBeat(int tick, out int bar, out int beat, out int remainingTicks) {
            var segment = TimeSigSegmentAtTick(tick);
            bar = segment.barPos + (tick - segment.tickPos) / segment.ticksPerBar;
            int tickInBar = tick - segment.tickPos - segment.ticksPerBar * (bar - segment.barPos);
            beat = tickInBar / segment.ticksPerBeat;
            remainingTicks = tickInBar - beat * segment.ticksPerBeat;
        }

        public int BarBeatToTickPos(int bar, int beat) {
            var segment = TimeSigSegmentAtBar(bar);
            return segment.tickPos + segment.ticksPerBar * (bar - segment.barPos) + segment.ticksPerBeat * beat;
        }

        public void NextBarBeat(int bar, int beat, out int nextBar, out int nextBeat) {
            nextBar = bar;
            nextBeat = beat + 1;
            var segment = TimeSigSegmentAtBar(bar);
            if (nextBeat >= segment.beatPerBar) {
                nextBar++;
                nextBeat = 0;
            }
        }

        public UTempo[] TemposBetweenTicks(int start, int end) {
            var list = tempoSegments
                .Where(tempo => start < tempo.tickEnd && tempo.tickPos < end)
                .Select(tempo => new UTempo { position = tempo.tickPos, bpm = tempo.bpm })
                .ToArray();
            return list;
        }

        public UTimeSignature TimeSignatureAtTick(int tick) {
            var segment = TimeSigSegmentAtTick(tick);
            return new UTimeSignature {
                barPosition = segment.barPos,
                beatPerBar = segment.beatPerBar,
                beatUnit = segment.beatUnit,
            };
        }

        public UTimeSignature TimeSignatureAtBar(int bar) {
            var segment = TimeSigSegmentAtBar(bar);
            return new UTimeSignature {
                barPosition = segment.barPos,
                beatPerBar = segment.beatPerBar,
                beatUnit = segment.beatUnit,
            };
        }

        public TimeAxis Clone() {
            var clone = new TimeAxis();
            // Shallow copy segments since they are unmodified after built.
            clone.timeSigSegments.AddRange(timeSigSegments);
            clone.tempoSegments.AddRange(tempoSegments);
            return clone;
        }
    }
}
