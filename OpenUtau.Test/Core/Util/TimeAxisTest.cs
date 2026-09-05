using OpenUtau.Core.Ustx;
using Xunit;


namespace OpenUtau.Core {
    public class TimeAxisTest {

        readonly ITestOutputHelper output;
        public TimeAxisTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void ConvertMsTest() {
            var timeAxis = new TimeAxis();
            var project = new UProject();
            project.timeSignatures.Add(new UTimeSignature(5, 3, 8));
            project.timeSignatures.Add(new UTimeSignature(11, 4, 4));
            project.tempos.Add(new UTempo(4800, 75));
            project.tempos.Add(new UTempo(9600, 90));
            project.tempos.Add(new UTempo(18000, 60));
            timeAxis.BuildSegments(project);

            Assert.Equal(0, timeAxis.TickPosToMsPos(0));
            Assert.Equal(2500, timeAxis.TickPosToMsPos(2400), 6);
            Assert.Equal(5000, timeAxis.TickPosToMsPos(4800), 6);
            Assert.Equal(9000, timeAxis.TickPosToMsPos(7200), 6);
            Assert.Equal(21833.33333333, timeAxis.TickPosToMsPos(15960), 6);
            Assert.Equal(37166.66666667, timeAxis.TickPosToMsPos(24000), 6);

            Assert.Equal(0, timeAxis.MsPosToTickPos(0));
            Assert.Equal(2400, timeAxis.MsPosToTickPos(2500));
            Assert.Equal(4800, timeAxis.MsPosToTickPos(5000));
            Assert.Equal(7200, timeAxis.MsPosToTickPos(9000));
            Assert.Equal(15960, timeAxis.MsPosToTickPos(21833.33333333));
            Assert.Equal(24000, timeAxis.MsPosToTickPos(37166.66666667));
        }

        [Fact]
        public void ConvertBarBeatTest() {
            var timeAxis = new TimeAxis();
            var project = new UProject();
            project.timeSignatures.Add(new UTimeSignature(5, 3, 8));
            project.timeSignatures.Add(new UTimeSignature(11, 4, 4));
            project.tempos.Add(new UTempo(4800, 75));
            project.tempos.Add(new UTempo(9600, 90));
            project.tempos.Add(new UTempo(18000, 60));
            timeAxis.BuildSegments(project);

            Assert.Equal(0, timeAxis.BarBeatToTickPos(0, 0));
            Assert.Equal(1440, timeAxis.BarBeatToTickPos(0, 3));
            Assert.Equal(4800, timeAxis.BarBeatToTickPos(2, 2));
            Assert.Equal(9600, timeAxis.BarBeatToTickPos(5, 0));
            Assert.Equal(13920, timeAxis.BarBeatToTickPos(11, 0));
            Assert.Equal(17760, timeAxis.BarBeatToTickPos(13, 0));
            Assert.Equal(18240, timeAxis.BarBeatToTickPos(13, 1));

            int bar;
            int beat;
            int remainingTicks;
            timeAxis.TickPosToBarBeat(0, out bar, out beat, out remainingTicks);
            Assert.Equal(0, bar);
            Assert.Equal(0, beat);
            Assert.Equal(0, remainingTicks);
            timeAxis.TickPosToBarBeat(1440, out bar, out beat, out remainingTicks);
            Assert.Equal(0, bar);
            Assert.Equal(3, beat);
            Assert.Equal(0, remainingTicks);
            timeAxis.TickPosToBarBeat(1450, out bar, out beat, out remainingTicks);
            Assert.Equal(0, bar);
            Assert.Equal(3, beat);
            Assert.Equal(10, remainingTicks);
            timeAxis.TickPosToBarBeat(4800, out bar, out beat, out remainingTicks);
            Assert.Equal(2, bar);
            Assert.Equal(2, beat);
            Assert.Equal(0, remainingTicks);
            timeAxis.TickPosToBarBeat(9600, out bar, out beat, out remainingTicks);
            Assert.Equal(5, bar);
            Assert.Equal(0, beat);
            Assert.Equal(0, remainingTicks);
            timeAxis.TickPosToBarBeat(13920, out bar, out beat, out remainingTicks);
            Assert.Equal(11, bar);
            Assert.Equal(0, beat);
            Assert.Equal(0, remainingTicks);
            timeAxis.TickPosToBarBeat(14000, out bar, out beat, out remainingTicks);
            Assert.Equal(11, bar);
            Assert.Equal(0, beat);
            Assert.Equal(80, remainingTicks);
            timeAxis.TickPosToBarBeat(17760, out bar, out beat, out remainingTicks);
            Assert.Equal(13, bar);
            Assert.Equal(0, beat);
            Assert.Equal(0, remainingTicks);
            timeAxis.TickPosToBarBeat(18000, out bar, out beat, out remainingTicks);
            Assert.Equal(13, bar);
            Assert.Equal(0, beat);
            Assert.Equal(240, remainingTicks);
            timeAxis.TickPosToBarBeat(18200, out bar, out beat, out remainingTicks);
            Assert.Equal(13, bar);
            Assert.Equal(0, beat);
            Assert.Equal(440, remainingTicks);
        }
    }
}
