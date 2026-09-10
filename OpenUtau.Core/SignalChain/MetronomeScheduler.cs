using System;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.SignalChain {
    class MetronomeScheduler {
        public int NextTick { get; private set; } = -1;
        public int NextBar { get; private set; }
        public int NextBeat { get; private set; }
        public double NextMs { get; private set; } = double.NaN;

        public bool IsScheduled => NextTick >= 0 && !double.IsNaN(NextMs);

        public void Reset(TimeAxis timeAxis, int tick) {
            ArgumentNullException.ThrowIfNull(timeAxis);
            if (tick < 0) {
                Clear();
                return;
            }
            timeAxis.TickPosToBarBeat(tick, out var bar, out var beat, out var remainingTicks);
            if (remainingTicks > 0) {
                timeAxis.NextBarBeat(bar, beat, out bar, out beat);
            }
            NextBar = bar;
            NextBeat = beat;
            NextTick = timeAxis.BarBeatToTickPos(bar, beat);
            NextMs = timeAxis.TickPosToMsPos(NextTick);
        }

        public void Advance(TimeAxis timeAxis) {
            ArgumentNullException.ThrowIfNull(timeAxis);
            if (NextTick < 0) {
                return;
            }
            timeAxis.NextBarBeat(NextBar, NextBeat, out var nextBar, out var nextBeat);
            NextBar = nextBar;
            NextBeat = nextBeat;
            NextTick = timeAxis.BarBeatToTickPos(NextBar, NextBeat);
            NextMs = timeAxis.TickPosToMsPos(NextTick);
        }

        public void SkipPast(TimeAxis timeAxis, double currentMs) {
            ArgumentNullException.ThrowIfNull(timeAxis);
            while (IsScheduled && NextMs <= currentMs) {
                Advance(timeAxis);
            }
        }

        public void Clear() {
            NextTick = -1;
            NextBar = 0;
            NextBeat = 0;
            NextMs = double.NaN;
        }
    }
}
