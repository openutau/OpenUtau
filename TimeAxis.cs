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

        readonly List<TimeSigSegment> timeSigSegments = new List<TimeSigSegment>();
        readonly List<TempoSegment> tempoSegments = new List<TempoSegment>();

        public long Timestamp { get; private set; }

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
                timeSigSegments.Add(new TimeSigSegment {
                    barPos = timesig.barPosition,
                    tickPos = posTick,
                    beatPerBar = timesig.beatPerBar,
                    beatUnit = timesig.beatUnit,
                    ticksPerBar = project.resolution * 4 * timesig.beatPerBar / timesig.beatUnit,
                    ticksPerBeat = project.resolution * 4 / timesig.beatUnit,
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
            for (var i = 0; i < tempoSegments.Count - 1; ++i) {
                if (tempoSegments[i + 1].bpm == 0) {
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

        public double GetBpmAtTick(int tick) {
            var segment = tempoSegments.First(seg => seg.tickPos == tick || seg.tickEnd > tick); // TODO: optimize
            return segment.bpm;
        }

        public double TickPosToMsPos(double tick) {
            var segment = tempoSegments.First(seg => seg.tickPos == tick || seg.tickEnd > tick); // TODO: optimize
            return segment.msPos + segment.msPerTick * (tick - segment.tickPos);
        }

        public double MsPosToNonExactTickPos(double ms) {
            var segment = tempoSegments.First(seg => seg.msPos == ms || seg.msEnd > ms); // TODO: optimize
            double tickPos = segment.tickPos + (ms - segment.msPos) * segment.ticksPerMs;
            return tickPos;
        }

        public int MsPosToTickPos(double ms) {
            var segment = tempoSegments.First(seg => seg.msPos == ms || seg.msEnd > ms); // TODO: optimize
            double tickPos = segment.tickPos + (ms - segment.msPos) * segment.ticksPerMs;
            return (int)Math.Round(tickPos);
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
            var segment = timeSigSegments.First(seg => seg.tickPos == tick || seg.tickEnd > tick); // TODO: optimize
            bar = segment.barPos + (tick - segment.tickPos) / segment.ticksPerBar;
            int tickInBar = tick - segment.tickPos - segment.ticksPerBar * (bar - segment.barPos);
            beat = tickInBar / segment.ticksPerBeat;
            remainingTicks = tickInBar - beat * segment.ticksPerBeat;
        }

        public int BarBeatToTickPos(int bar, int beat) {
            var segment = timeSigSegments.First(seg => seg.barPos == bar || seg.barEnd > bar); // TODO: optimize
            return segment.tickPos + segment.ticksPerBar * (bar - segment.barPos) + segment.ticksPerBeat * beat;
        }

        public void NextBarBeat(int bar, int beat, out int nextBar, out int nextBeat) {
            nextBar = bar;
            nextBeat = beat + 1;
            var segment = timeSigSegments.First(seg => seg.barPos == bar || seg.barEnd > bar); // TODO: optimize
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
            var segment = timeSigSegments.First(seg => seg.tickPos == tick || seg.tickEnd > tick); // TODO: optimize
            return new UTimeSignature {
                barPosition = segment.barPos,
                beatPerBar = segment.beatPerBar,
                beatUnit = segment.beatUnit,
            };
        }

        public UTimeSignature TimeSignatureAtBar(int bar) {
            var segment = timeSigSegments.First(seg => seg.barPos == bar || seg.barEnd > bar); // TODO: optimize
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
