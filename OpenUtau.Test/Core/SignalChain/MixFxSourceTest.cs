using System;
using System.Linq;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.SignalChain {
    public class MixFxSourceTest {
        const int Block = 512;

        /// <summary>Stereo sine at 8 kHz, the EQ high-shelf corner (half the shelf gain applies).</summary>
        class SineSource : ISignalSource {
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) {
                for (int i = 0; i < count; i++) {
                    int frame = (position + i) / 2;
                    buffer[index + i] += 0.2f * MathF.Sin(2 * MathF.PI * 8000 * frame / 44100f);
                }
                return position + count;
            }
        }

        static UMixFx BrightEq(bool enabled) => new UMixFx {
            Enabled = enabled,
            EqHighDb = 12, EqMidDb = 0, EqLowDb = 0,
            CompPreset = "off", CompRatio = 1, CompMakeupDb = 0,
            ReverbPreset = "off", ReverbWet = 0,
        };

        static float[] Render(ISignalSource source, ref int position, int blocks) {
            var output = new float[blocks * Block];
            for (int b = 0; b < blocks; b++) {
                position = source.Mix(position, output, b * Block, Block);
            }
            return output;
        }

        static float Peak(float[] samples, int from) => samples.Skip(from).Max(MathF.Abs);

        [Fact]
        public void FollowsTrackEditsWhilePlaying() {
            var track = new UTrack { MixFx = BrightEq(false) };
            var source = MixFxSource.WrapLive(new SineSource(), track);
            int position = 0;

            var dry = Render(source, ref position, 8);
            Assert.InRange(Peak(dry, 0), 0.19f, 0.21f);

            track.MixFx = BrightEq(true);
            var wet = Render(source, ref position, 16);
            // +12 dB shelf is +6 dB at its corner: 0.2 -> ~0.4.
            Assert.InRange(Peak(wet, wet.Length / 2), 0.35f, 0.45f);

            track.MixFx.EqHighDb = 0;
            var flat = Render(source, ref position, 16);
            Assert.InRange(Peak(flat, flat.Length / 2), 0.19f, 0.21f);
        }

        [Fact]
        public void MasterAndModuleSwitchesReturnToExactlyDry() {
            var track = new UTrack { MixFx = BrightEq(true) };
            var inner = new SineSource();
            var source = MixFxSource.WrapLive(inner, track);
            int position = 0;
            Render(source, ref position, 8);

            foreach (Action<UMixFx> switchOff in new Action<UMixFx>[] { fx => fx.Enabled = false, fx => fx.EqEnabled = false }) {
                track.MixFx = BrightEq(true);
                Render(source, ref position, 8);
                switchOff(track.MixFx);
                Render(source, ref position, 4);
                int start = position;
                var output = Render(source, ref position, 4);
                int reference = start;
                var expected = Render(inner, ref reference, 4);
                Assert.Equal(expected, output);
            }
        }

        [Fact]
        public void SwitchingCrossfadesInsteadOfJumping() {
            var track = new UTrack { MixFx = BrightEq(false) };
            var source = MixFxSource.WrapLive(new SineSource(), track);
            int position = 0;
            Render(source, ref position, 4);

            track.MixFx.Enabled = true;
            var fadeIn = Render(source, ref position, 4);
            // A hard cut would jump by up to ~0.4 on the first sample.
            float first = fadeIn[0];
            float dryFirst = 0.2f * MathF.Sin(2 * MathF.PI * 8000 * (4 * Block / 2) / 44100f);
            Assert.True(MathF.Abs(first - dryFirst) < 0.05f);
        }

        [Fact]
        public void ExportSkipsWrappingWhenNothingWouldChange() {
            var inner = new SineSource();
            Assert.Same(inner, MixFxSource.WrapWith(inner, null));
            Assert.Same(inner, MixFxSource.WrapWith(inner, BrightEq(false)));
            var allOff = BrightEq(true);
            allOff.EqEnabled = false;
            Assert.Same(inner, MixFxSource.WrapWith(inner, allOff));
            Assert.NotSame(inner, MixFxSource.WrapWith(inner, BrightEq(true)));
        }
    }
}
