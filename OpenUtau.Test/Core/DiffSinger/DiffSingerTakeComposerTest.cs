using System;
using System.Linq;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerTakeComposerTest {
        [Fact]
        public void BuildWeightsKeepsChangedFramesAndRampsOutside() {
            var changed = Enumerable.Repeat(false, 20).ToArray();
            changed[10] = true;
            changed[11] = true;

            var weights = DiffSingerTakeComposer.BuildWeights(changed, 4, 20);

            Assert.Equal(1f, weights[10]);
            Assert.Equal(1f, weights[11]);
            Assert.Equal(0.75f, weights[9]);
            Assert.Equal(0.5f, weights[8]);
            Assert.Equal(0.25f, weights[7]);
            Assert.Equal(0f, weights[6]);
            Assert.Equal(0.75f, weights[12]);
            Assert.Equal(0.5f, weights[13]);
            Assert.Equal(0.25f, weights[14]);
            Assert.Equal(0f, weights[15]);
            Assert.Equal(0f, weights[0]);
        }

        [Fact]
        public void BuildWeightsWithoutFadeIsBinary() {
            var changed = new[] { false, true, true, false };
            var weights = DiffSingerTakeComposer.BuildWeights(changed, 0, 4);
            Assert.Equal(new[] { 0f, 1f, 1f, 0f }, weights);
        }

        [Fact]
        public void ApplyProtectForcesProtectedFramesToOldAndRampsOutside() {
            var weights = Enumerable.Repeat(1f, 20).ToArray();

            DiffSingerTakeComposer.ApplyProtect(weights, new[] { (8, 12) }, 2);

            for (int i = 8; i < 12; i++) {
                Assert.Equal(0f, weights[i]);
            }
            Assert.Equal(0f, weights[7]);
            Assert.Equal(0.5f, weights[6]);
            Assert.Equal(1f, weights[5]);
            Assert.Equal(0f, weights[12]);
            Assert.Equal(0.5f, weights[13]);
            Assert.Equal(1f, weights[14]);
        }

        [Fact]
        public void ProtectWinsOverAChangedFrame() {
            var changed = Enumerable.Repeat(true, 10).ToArray();
            var weights = DiffSingerTakeComposer.BuildWeights(changed, 2, 10);

            DiffSingerTakeComposer.ApplyProtect(weights, new[] { (4, 6) }, 2);

            Assert.Equal(0f, weights[4]);
            Assert.Equal(0f, weights[5]);
            Assert.Equal(0f, weights[3]);
            Assert.Equal(0f, weights[6]);
            Assert.Equal(0.5f, weights[2]);
            Assert.Equal(0.5f, weights[7]);
        }

        [Fact]
        public void DiffConditionsFindsTheChangedFrame() {
            var previous = new[] {
                Condition("f0", 100f, 100f, 100f, 100f),
                Condition("energy", 0f, 0f, 0f, 0f),
            };
            var current = new[] {
                Condition("f0", 100f, 100f, 100f, 100f),
                Condition("energy", 0f, 0f, 1f, 0f),
            };

            var changed = DiffSingerTakeComposer.DiffConditions(previous, current, 4);

            Assert.Equal(new[] { false, false, true, false }, changed);
        }

        [Fact]
        public void DiffConditionsFindsASpeakerEmbeddingFrame() {
            var previous = new[] {
                new DiffSingerTakeCondition {
                    Name = "spk_embed",
                    ValuesPerFrame = 2,
                    Values = new[] { 0f, 0f, 0f, 0f, 0f, 0f },
                },
            };
            var current = new[] {
                new DiffSingerTakeCondition {
                    Name = "spk_embed",
                    ValuesPerFrame = 2,
                    Values = new[] { 0f, 0f, 0f, 5f, 0f, 0f },
                },
            };

            var changed = DiffSingerTakeComposer.DiffConditions(previous, current, 3);

            Assert.Equal(new[] { false, true, false }, changed);
        }

        [Fact]
        public void BlendMelUsesOldInsideProtectedAndNewInsideReroll() {
            var previous = new float[] { 0, 0, 0, 0, 0, 0 };
            var current = new float[] { 10, 10, 10, 10, 10, 10 };
            var weights = new[] { 0f, 0.5f, 1f };

            var blended = DiffSingerTakeComposer.BlendMel(previous, current, weights, 3, 2);

            Assert.Equal(new[] { 0f, 0f, 5f, 5f, 10f, 10f }, blended);
        }

        [Fact]
        public void BlendF0InterpolatesInLogFrequency() {
            var previous = new[] { 100f, 100f, 100f };
            var current = new[] { 400f, 400f, 400f };
            var weights = new[] { 0f, 0.5f, 1f };

            var blended = DiffSingerTakeComposer.BlendF0(previous, current, weights, 3);

            Assert.Equal(100f, blended[0], 3);
            Assert.Equal(200f, blended[1], 3);
            Assert.Equal(400f, blended[2], 3);
        }

        [Fact]
        public void ToFrameRangesMapsTicksToThePhraseGrid() {
            var project = new UProject();
            // Default tempo: 480 ticks are 500 ms.
            var ranges = DiffSingerTakeComposer.ToFrameRanges(
                new[] { (0, 480), (480, 960) }, project.timeAxis, 0, 10f, 100);

            Assert.Equal(2, ranges.Count);
            Assert.Equal((0, 50), ranges[0]);
            Assert.Equal((50, 100), ranges[1]);
        }

        [Fact]
        public void ToFrameRangesClipsToThePhrase() {
            var project = new UProject();
            var ranges = DiffSingerTakeComposer.ToFrameRanges(
                new[] { (-480, 96) }, project.timeAxis, 0, 10f, 40);

            Assert.Single(ranges);
            Assert.Equal((0, 10), ranges[0]);
        }

        [Fact]
        public void IsAllOldAndIsAllNewDetectPureTakes() {
            Assert.True(DiffSingerTakeComposer.IsAllOld(new[] { 0f, 0f, 0f }));
            Assert.False(DiffSingerTakeComposer.IsAllOld(new[] { 0f, 0.25f, 0f }));
            Assert.True(DiffSingerTakeComposer.IsAllNew(new[] { 1f, 1f }));
            Assert.False(DiffSingerTakeComposer.IsAllNew(new[] { 1f, 0.5f }));
        }

        [Fact]
        public void TakeCompatibilityRejectsModelAndLayoutChanges() {
            var take = new DiffSingerTake {
                TotalFrames = 4,
                Bins = 2,
                FrameMs = 10,
                Mel = new float[8],
                F0 = new float[4],
                AcousticHash = 1,
                VocoderHash = 2,
                Conditions = new[] { Condition("f0", 100f, 100f, 100f, 100f) },
            };
            var conditions = new[] { Condition("f0", 100f, 100f, 100f, 100f) };

            Assert.True(take.IsCompatible(4, 2, 10, conditions, 1, 2));
            Assert.False(take.IsCompatible(4, 2, 10, conditions, 9, 2));
            Assert.False(take.IsCompatible(4, 2, 10, conditions, 1, 9));
            Assert.False(take.IsCompatible(5, 2, 10, conditions, 1, 2));
            Assert.False(take.IsCompatible(4, 3, 10, conditions, 1, 2));
            Assert.False(take.IsCompatible(4, 2, 20, conditions, 1, 2));
        }

        static DiffSingerTakeCondition Condition(string name, params float[] values) {
            return new DiffSingerTakeCondition {
                Name = name,
                Values = values,
                ValuesPerFrame = 1,
            };
        }
    }
}
