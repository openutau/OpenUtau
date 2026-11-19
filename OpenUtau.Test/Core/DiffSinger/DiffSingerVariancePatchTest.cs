using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core {
    public class DiffSingerVariancePatchTest {
        static readonly string[] names = { "energy", "breathiness", "voicing", "tension" };

        [Fact]
        public void BuildChangedFrameMaskMarksOnlyChangedFrames() {
            var mask = DiffSingerVariancePatch.BuildChangedFrameMask(
                new[] { 1f, 1f, 1f, 2f },
                new[] { 1f, 2f, 1f, 2f },
                1e-4f);

            Assert.Equal(new[] { false, true, false, false }, mask);
        }

        [Fact]
        public void BuildChangedFrameMaskGroupsSpeakerEmbeddingByFrame() {
            var mask = DiffSingerVariancePatch.BuildChangedFrameMask(
                new[] { 1f, 2f, 3f, 4f, 5f, 6f },
                new[] { 1f, 2f, 3f, 40f, 5f, 6f },
                3,
                1e-4f);

            Assert.Equal(new[] { false, true, false }, mask);
        }

        [Fact]
        public void BuildChangedFrameMaskMarksAllFramesForIncompatibleEmbeddingShape() {
            var mask = DiffSingerVariancePatch.BuildChangedFrameMask(
                new[] { 1f, 2f, 3f, 4f },
                new[] { 1f, 2f, 3f },
                2,
                1e-4f);

            Assert.Equal(new[] { true, true }, mask);
        }

        [Fact]
        public void ExpandToChannelsUsesSharedFrameMask() {
            var mask = DiffSingerVariancePatch.ExpandToChannels(
                new[] { false, true, false }, 3);

            Assert.Equal(
                new[] { false, false, false, true, true, true, false, false, false },
                mask);
        }

        [Fact]
        public void HardComposePreservesUnmaskedFramesExactly() {
            var previous = Result(
                new[] { 1f, 2f, 3f, 4f },
                new[] { 5f, 6f, 7f, 8f });
            var predicted = Result(
                new[] { 10f, 20f, 30f, 40f },
                new[] { 50f, 60f, 70f, 80f });
            var mask = DiffSingerVariancePatch.ExpandToChannels(
                new[] { false, true, false, true }, 4);

            var result = DiffSingerVariancePatch.HardCompose(previous, predicted, mask, 4);

            Assert.Equal(new[] { 1f, 20f, 3f, 40f }, result.Channel("energy"));
            Assert.Equal(new[] { 5f, 60f, 7f, 80f }, result.Channel("breathiness"));
        }

        [Fact]
        public void HardComposeDoesNotLeakModelChangesOutsideMask() {
            var previous = Result(new[] { 1f, 2f, 3f });
            var predicted = Result(new[] { 100f, 200f, 300f });
            var mask = DiffSingerVariancePatch.ExpandToChannels(
                new[] { false, true, false }, 4);

            var result = DiffSingerVariancePatch.HardCompose(previous, predicted, mask, 4);

            Assert.Equal(new[] { 1f, 200f, 3f }, result.Channel("energy"));
        }

        [Fact]
        public void HardComposeKeepsPredictedChannelWhenPreviousIsMissing() {
            var previous = Result(new[] { 1f, 2f });
            var predicted = Result(new[] { 10f, 20f });
            var mask = DiffSingerVariancePatch.ExpandToChannels(new[] { false, true }, 4);

            var result = DiffSingerVariancePatch.HardCompose(previous, predicted, mask, 4);

            Assert.Equal(new[] { 1f, 20f }, result.Channel("energy"));
        }

        [Fact]
        public void HardComposeHandlesAllChannels() {
            var previous = Result(
                new[] { 1f, 2f },
                new[] { 3f, 4f },
                new[] { 5f, 6f },
                new[] { 7f, 8f });
            var predicted = Result(
                new[] { 10f, 20f },
                new[] { 30f, 40f },
                new[] { 50f, 60f },
                new[] { 70f, 80f });
            var mask = DiffSingerVariancePatch.ExpandToChannels(new[] { false, true }, 4);

            var result = DiffSingerVariancePatch.HardCompose(previous, predicted, mask, 4);

            Assert.Equal(new[] { 1f, 20f }, result.Channel("energy"));
            Assert.Equal(new[] { 3f, 40f }, result.Channel("breathiness"));
            Assert.Equal(new[] { 5f, 60f }, result.Channel("voicing"));
            Assert.Equal(new[] { 7f, 80f }, result.Channel("tension"));
        }

        [Fact]
        public void HardComposePreservesAllPreviousChannelsForFalseMask() {
            var previous = Result(
                new[] { 1f, 2f },
                new[] { 3f, 4f },
                new[] { 5f, 6f },
                new[] { 7f, 8f });
            var predicted = Result(
                new[] { 10f, 20f },
                new[] { 30f, 40f },
                new[] { 50f, 60f },
                new[] { 70f, 80f });
            var mask = DiffSingerVariancePatch.ExpandToChannels(new[] { false, false }, 4);

            var result = DiffSingerVariancePatch.HardCompose(previous, predicted, mask, 4);

            for (int i = 0; i < names.Length; i++) {
                Assert.Equal(previous.channels[i], result.channels[i]);
            }
        }

        [Fact]
        public void HardComposeFallsBackToPredictedForIncompatibleMetadata() {
            var previous = Result(new[] { 1f, 2f, 3f }, frameMs: 50);
            var predicted = Result(new[] { 10f, 20f, 30f }, frameMs: 60);
            var mask = new[] { true, false, true, true, false, true, true, false, true, true, false, true };

            var result = DiffSingerVariancePatch.HardCompose(previous, predicted, mask, 4);

            Assert.Equal(predicted.Channel("energy"), result.Channel("energy"));
        }

        [Fact]
        public void IsChannelLayoutCompatibleAcceptsExpectedChannels() {
            var result = new VarianceResult(
                new[] { "energy", "voicing" },
                new float[]?[] { new[] { 1f, 2f }, new[] { 3f, 4f } },
                50, 1, 1, 2);

            Assert.True(DiffSingerVariancePatch.IsChannelLayoutCompatible(
                result, Params(energy: true, voicing: true), 2));
        }

        [Fact]
        public void IsChannelLayoutCompatibleRejectsMissingEnabledChannel() {
            var result = Result(new[] { 1f, 2f });

            Assert.False(DiffSingerVariancePatch.IsChannelLayoutCompatible(
                result, Params(energy: true, breathiness: true), 2));
        }

        [Fact]
        public void IsChannelLayoutCompatibleRejectsWrongChannelLength() {
            var result = Result(
                new[] { 1f, 2f },
                new[] { 3f });

            Assert.False(DiffSingerVariancePatch.IsChannelLayoutCompatible(
                result, Params(energy: true, breathiness: true), 2));
        }

        [Fact]
        public void IsChannelLayoutCompatibleRejectsUnexpectedChannel() {
            var result = Result(
                new[] { 1f, 2f },
                tension: new[] { 3f, 4f });

            Assert.False(DiffSingerVariancePatch.IsChannelLayoutCompatible(
                result, Params(energy: true), 2));
        }

        [Fact]
        public void IsChannelLayoutCompatibleRejectsReorderedChannels() {
            var result = new VarianceResult(
                new[] { "breathiness", "energy" },
                new float[]?[] { new[] { 1f, 2f }, new[] { 3f, 4f } },
                50, 1, 1, 2);

            Assert.False(DiffSingerVariancePatch.IsChannelLayoutCompatible(
                result, Params(energy: true, breathiness: true), 2));
        }

        [Fact]
        public void VariancePatchStateCacheEvictsLeastRecentlyUsedState() {
            var cache = new VariancePatchStateCache(2);
            cache.Set(1, State(1));
            cache.Set(2, State(2));
            Assert.True(cache.TryGetValue(1, out _));

            cache.Set(3, State(3));

            Assert.Equal(2, cache.Count);
            Assert.True(cache.TryGetValue(1, out _));
            Assert.False(cache.TryGetValue(2, out _));
            Assert.True(cache.TryGetValue(3, out _));
        }

        [Fact]
        public void IsMetadataCompatibleRejectsFrameLayoutChanges() {
            var previous = Result(new[] { 1f, 2f, 3f });

            Assert.False(DiffSingerVariancePatch.IsMetadataCompatible(previous, 50, 1, 1, 4));
            Assert.True(DiffSingerVariancePatch.IsMetadataCompatible(previous, 50, 1, 1, 3));
        }

        static VariancePatchState State(float value) {
            return new VariancePatchState(
                new[] { value },
                null,
                Result(new[] { value }));
        }

        static IReadOnlyList<DsParam> Params(
            bool energy = false, bool breathiness = false, bool voicing = false, bool tension = false) {
            var config = new DsConfig {
                useEnergyEmbed = energy,
                useBreathinessEmbed = breathiness,
                useVoicingEmbed = voicing,
                useTensionEmbed = tension,
            };
            return DsParamRegistry.From(config).Variance;
        }

        static VarianceResult Result(
            float[] energy,
            float[]? breathiness = null,
            float[]? voicing = null,
            float[]? tension = null,
            float frameMs = 50) {
            return new VarianceResult(
                names,
                new float[]?[] { energy, breathiness, voicing, tension },
                frameMs, 1, 1, energy.Length);
        }
    }
}
