using System;
using System.Linq;
using OpenUtau.Core.Render;
using Xunit;

namespace OpenUtau.Classic.Hifisampler {
    public class HifiNoteTimingTest {
        static readonly double Thop = 512 / 44100.0;

        // offset 100 ms, consonant 50 ms, cutoff -300 ms (end at 400 ms), length 500 ms, on a
        // 1000-frame (2.902 s) source. Expected values worked out by hand from resampler.py.
        static HifiNoteTiming Timing(bool loop, int velocity = 100, int melFrames = 1000) =>
            new HifiNoteTiming(new HifiSamplerConfig(), melFrames, velocity, 100, 50, -300, 500, loop);

        [Fact]
        public void StretchTiming() {
            var t = Timing(loop: false);
            Assert.Equal(1.0, t.Vel);
            Assert.Equal(0.4, t.End, 12);
            Assert.Equal(2.0, t.ScalingRatio, 12);  // 250 ms of vowel stretched to 500 ms
            Assert.Equal(488, t.StretchedNFrames);
            Assert.Equal(3, t.CutLeftMelFrames);
            Assert.Equal(426, t.CutRightMelFrames);
            Assert.Equal(0.1 - 3 * Thop, t.NewStart, 12);
            Assert.Equal(0.65 - 3 * Thop, t.NewEnd, 12);
            Assert.Equal(59, t.SourceTimes().Length);
        }

        [Fact]
        public void LoopTiming() {
            var t = Timing(loop: true);
            Assert.Equal(52, t.ConFrame);
            Assert.Equal(138, t.EndFrame);
            Assert.Equal(173, t.PadLoopSize);
            Assert.Equal(1.0, t.ScalingRatio);  // the loop already covers the length
            Assert.Equal(311 * 128 / 44100.0, t.TotalTime, 12);
            Assert.Equal(78, t.StretchedNFrames);
            Assert.Equal(16, t.CutRightMelFrames);
            Assert.Equal(0.1 - 3 * Thop, t.NewStart, 12);
            Assert.Equal(0.65 - 3 * Thop, t.NewEnd, 12);
        }

        [Fact]
        public void VoicedRatioCountsTheKeptFrames() {
            var t = Timing(loop: false);
            // 10 ms f0 frames over the source.
            Assert.Equal(1.0, t.VoicedRatio(Enumerable.Repeat(200.0, 300).ToArray(), 0.01, 40));
            Assert.Equal(0.0, t.VoicedRatio(new double[300], 0.01, 40));
            // Unvoiced up to the consonant end (150 ms): of the kept 500 ms, the first 50 ms are the
            // consonant and the rest the stretched vowel.
            var vowelOnly = Enumerable.Range(0, 300).Select(i => i >= 15 ? 200.0 : 0).ToArray();
            Assert.InRange(t.VoicedRatio(vowelOnly, 0.01, 40), 0.85, 0.95);
        }

        [Fact]
        public void OutputIsLengthPlusStretchedConsonant() {
            // hifisampler renders length + velocity-stretched consonant, kept as is.
            var t = Timing(loop: false, velocity: 0);
            Assert.Equal(2.0, t.Vel);
            Assert.Equal(0.5 + 0.05 * 2, t.NewEnd - t.NewStart, 12);
        }

        [Fact]
        public void LoopReflectsTheVowelFrames() {
            var t = Timing(loop: true);
            var mel = Enumerable.Range(0, 1000).Select(i => new float[] { i }).ToArray();
            var rendered = t.RenderMel(mel);
            var times = t.SourceTimes();
            Assert.Equal(times.Length, rendered.Length);
            for (int i = 0; i < times.Length; i++) {
                double frame = rendered[i][0];
                if (times[i] > (t.ConFrame + 1) * 128 / 44100.0) {
                    // Past the consonant every value lies between the looped frames.
                    Assert.InRange(frame, t.ConFrame, t.EndFrame - 1);
                }
            }
        }

        [Fact]
        public void CurvesMapBackToTheSource() {
            var t = Timing(loop: false);
            // A tension ramp over the note's pitch grid (120 bpm: 5.2 ms a point).
            var ramp = Enumerable.Range(0, 130).Select(i => (float)Math.Min(100, i)).ToArray();
            var curves = t.SourceCurves(ramp, null, null, null, 60, new int[130], 120, 128);
            Assert.Equal(1000, curves.Tension.Length);
            Assert.All(curves.Breathiness, v => Assert.Equal(0, v));
            Assert.All(curves.Voicing, v => Assert.Equal(100, v));
            // Stretching keeps the order: the ramp rises along the used source frames.
            for (int m = 1; m < curves.Tension.Length; m++) {
                Assert.True(curves.Tension[m] >= curves.Tension[m - 1] - 1e-9);
            }
            Assert.Equal(0, curves.Tension[0], 6);  // before the offset: the first value
            Assert.True(curves.Tension[^1] > 50);  // after the cutoff: the last values
            Assert.False(t.SourceCurves(Enumerable.Repeat(0f, 130).ToArray(), null, null, null, 60, new int[130], 120, 128).HasTension);
            Assert.All(curves.Gender, v => Assert.Equal(0, v));
            Assert.All(curves.TargetPitch, v => Assert.Equal(60, v));
            // A pitch bend of +200 cents reaches the source frames as MIDI 62.
            var bent = t.SourceCurves(null, null, null, null, 60, Enumerable.Repeat(200, 130).ToArray(), 120, 128);
            Assert.All(bent.TargetPitch, v => Assert.Equal(62, v, 6));
        }

        [Fact]
        public void LoopedFramesAverageTheirUses() {
            var t = Timing(loop: true);
            // 30 over the consonant, 90 over the rest of the note: the loop region, used only
            // after the consonant, gets 90; the consonant frames keep 30.
            int conPoints = (int)((t.Con * t.Vel - t.Start * t.Vel) / (60.0 / (120 * 96)));
            var curve = Enumerable.Range(0, 130).Select(i => i < conPoints ? 30f : 90f).ToArray();
            var curves = t.SourceCurves(null, curve, null, null, 60, new int[130], 120, 128);
            Assert.Equal(30, curves.Breathiness[t.ConFrame - 10], 6);
            for (int m = t.ConFrame + 2; m < t.EndFrame; m++) {
                Assert.Equal(90, curves.Breathiness[m], 6);
            }
        }

        [Fact]
        public void OtoErrors() {
            Assert.Throws<CutOffBeforeOffsetError>(() =>
                new HifiNoteTiming(new HifiSamplerConfig(), 1000, 100, 100, 50, 2900, 500, false));
            Assert.Throws<ConsonantExceedsCutoffError>(() =>
                new HifiNoteTiming(new HifiSamplerConfig(), 1000, 100, 100, 400, -300, 500, true));
        }

        [Fact]
        public void PitchAppendsTheBaseTone() {
            var t = Enumerable.Range(0, 200).Select(i => i * Thop).ToArray();
            var pitches = Enumerable.Repeat(100, 8).ToArray();
            var pitch = HifiNotePitch.Render(pitches, 60, 120, 0.05, t);
            double step = 60.0 / (120 * 96);
            // Flat 61 over the pitchbend, falling to the appended 60 at its end and held there.
            Assert.Equal(61, pitch[(int)(0.06 / Thop)], 9);
            Assert.Equal(60, pitch[^1], 9);
            Assert.Equal(60, HifiNotePitch.Render(pitches, 60, 120, 0.05, new[] { 0.05 + 8 * step })[0], 9);
        }
    }
}
