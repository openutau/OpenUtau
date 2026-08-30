using System;
using System.IO;
using System.Linq;
using OpenUtau.Core.Format;
using OpenUtau.Core.Neutrino;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;
using FormatUstx = OpenUtau.Core.Format.Ustx;

namespace OpenUtau.Core.Test.Neutrino {
    public class NeutrinoRendererTest {
        [Theory]
        [InlineData(FormatUstx.DYN)]
        [InlineData(FormatUstx.PITD)]
        [InlineData(FormatUstx.SHFC)]
        public void SupportsCoreAndStyleShiftExpressions(string abbr) {
            var descriptor = new UExpressionDescriptor(abbr, abbr, -100, 100, 0) {
                type = UExpressionType.Curve,
            };

            Assert.True(new NeutrinoRenderer().SupportsExpression(descriptor));
        }

        [Theory]
        [InlineData(FormatUstx.GENC)]
        [InlineData(FormatUstx.BREC)]
        [InlineData(FormatUstx.TENC)]
        [InlineData(FormatUstx.VOIC)]
        public void DoesNotAdvertiseHnsepExpressions(string abbr) {
            var descriptor = new UExpressionDescriptor(abbr, abbr, -100, 100, 0) {
                type = UExpressionType.Curve,
            };

            Assert.False(new NeutrinoRenderer().SupportsExpression(descriptor));
        }

        [Fact]
        public void RegistersNeutrinoV3Renderer() {
            Assert.Equal(
                new[] { Renderers.NEUTRINO },
                Renderers.GetSupportedRenderers(USingerType.Neutrino));
            Assert.Equal(Renderers.NEUTRINO, Renderers.GetDefaultRenderer(USingerType.Neutrino));
            Assert.IsType<NeutrinoRenderer>(Renderers.CreateRenderer(Renderers.NEUTRINO));
        }

        [Theory]
        [InlineData("か")]
        [InlineData("カ")]
        [InlineData("ka")]
        public void BuiltInDictionarySupportsKanaAndRomaji(string lyric) {
            Assert.Equal(new[] { "k", "a" }, NeutrinoPhoneme.KanaToPhonemes(lyric));
        }

        [Fact]
        public void StyleShiftAppliesOnlyItsPredictedPitchDifference() {
            var editorF0 = new[] { 220f, 330f, 0f };
            NeutrinoRenderer.ApplyStyleShiftContour(
                editorF0,
                new[] { 200f, 300f, 0f },
                new[] { 210f, 270f, 100f });

            Assert.Equal(231f, editorF0[0], 3);
            Assert.Equal(297f, editorF0[1], 3);
            Assert.Equal(0f, editorF0[2]);
        }

        [Fact]
        public void V3ModelSignatureRequiresAllFourModels() {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"neutrino-v3-model-{Guid.NewGuid():N}");
            try {
                Directory.CreateDirectory(directory);
                foreach (string fileName in new[] { "t.bin", "p.bin", "s.bin" }) {
                    File.WriteAllBytes(Path.Combine(directory, fileName), Array.Empty<byte>());
                }
                Assert.False(NeutrinoSinger.IsV3ModelDirectory(directory));

                File.WriteAllBytes(Path.Combine(directory, "v.bin"), Array.Empty<byte>());
                Assert.True(NeutrinoSinger.IsV3ModelDirectory(directory));
            } finally {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void RenderPhoneKeepsPanelConsonantN() {
            Assert.Equal(new[] { "N" }, NeutrinoPhoneme.KanaToPhonemes("n"));
            Assert.Equal(new[] { "n" }, NeutrinoPhoneme.RenderPhoneToPhonemes("n"));
            Assert.Equal(new[] { "n", "o" }, NeutrinoPhoneme.RenderPhoneToPhonemes("no"));
        }

        [Fact]
        public void FrameMapAssignsUncoveredFramesToFinalPhone() {
            Assert.Equal(
                new long[] { 2 },
                NeutrinoRenderer.BuildFramePhonemeMap(new[] { 0.001f, 0.001f }, 1));
            Assert.Equal(
                new long[] { 1, 2 },
                NeutrinoRenderer.BuildFramePhonemeMap(new[] { 0.011f, 0.001f }, 2));
        }

        [Fact]
        public void InferenceChunksSplitAfterBreathAndAroundPauses() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] {
                NeutrinoPhoneme.PAU,
                NeutrinoPhoneme.PAU,
                1,
                NeutrinoPhoneme.BR,
                2,
                NeutrinoPhoneme.PAU,
                4,
            });

            Assert.Equal(5, chunks.Length);
            AssertChunk(chunks[0], 0, 2, false);
            AssertChunk(chunks[1], 2, 2, true);
            AssertChunk(chunks[2], 4, 1, true);
            AssertChunk(chunks[3], 5, 1, false);
            AssertChunk(chunks[4], 6, 1, true);
        }

        [Fact]
        public void ConsecutiveBreathsStayInTheSameActiveChunk() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] {
                1,
                NeutrinoPhoneme.BR,
                NeutrinoPhoneme.BR,
                2,
            });

            Assert.Equal(2, chunks.Length);
            AssertChunk(chunks[0], 0, 3, true);
            AssertChunk(chunks[1], 3, 1, true);
        }

        [Fact]
        public void BreathAfterPauseRemainsInTheInactiveChunk() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] {
                NeutrinoPhoneme.PAU,
                NeutrinoPhoneme.BR,
                1,
            });

            Assert.Equal(2, chunks.Length);
            AssertChunk(chunks[0], 0, 2, false);
            AssertChunk(chunks[1], 2, 1, true);
        }

        [Fact]
        public void FrameChunksUseGlobalRoundedBoundariesWithoutGaps() {
            var phoneChunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] {
                1,
                NeutrinoPhoneme.BR,
                NeutrinoPhoneme.PAU,
                2,
            });
            var frameChunks = NeutrinoInferenceUtil.BuildFrameChunks(
                phoneChunks,
                new[] { 0.0, 0.011, 0.024, 0.032, 0.051 },
                totalFrames: 5,
                frameSeconds: 0.01);

            Assert.Equal(3, frameChunks.Length);
            AssertFrameChunk(frameChunks[0], 0, 2, 0, 2, true);
            AssertFrameChunk(frameChunks[1], 2, 1, 2, 1, false);
            AssertFrameChunk(frameChunks[2], 3, 1, 3, 2, true);
            Assert.Equal(5, frameChunks.Sum(chunk => chunk.FrameCount));
        }

        [Fact]
        public void ChunkedTimingKeepsNextActiveChunkInitialShift() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] {
                1,
                NeutrinoPhoneme.PAU,
                2,
            });

            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                new[] { 0.3f, 0.2f, 0.4f },
                new long[] { 0, 0, 0 },
                chunks,
                frameSeconds: 0.01,
                chunk => chunk.PhoneStart switch {
                    0 => new[] { 0f, 123f },
                    2 => new[] { -0.05f, 123f },
                    _ => throw new InvalidOperationException(),
                });

            Assert.Equal(0.0, boundaries[0], 3);
            Assert.Equal(0.3, boundaries[1], 3);
            Assert.Equal(0.45, boundaries[2], 3);
            Assert.Equal(0.9, boundaries[3], 3);
        }

        [Fact]
        public void LeadingContextKeepsFirstActiveChunkInitialShift() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] { 2, 24 });

            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                new[] { 0.5f, 0.5f },
                new long[] { 0, 1 },
                chunks,
                frameSeconds: 0.01,
                chunk => new[] { -0.07f, 0.01f, 123f },
                leadingContextSeconds: 0.5);

            Assert.Equal(-0.07, boundaries[0], 3);
            Assert.Equal(0.01, boundaries[1], 3);
            Assert.Equal(0.5, boundaries[2], 3);
            Assert.Equal(0.08, boundaries[1] - boundaries[0], 3);

            double start = NeutrinoInferenceUtil.NormalizeBoundaryStart(boundaries);
            Assert.Equal(-0.07, start, 3);
            Assert.Equal(0.0, boundaries[0], 3);
            Assert.Equal(0.08, boundaries[1], 3);
            Assert.Equal(0.57, boundaries[2], 3);
        }

        [Fact]
        public void LeadingContextClampsFirstPhoneInsideVirtualPause() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] { 2, 24 });

            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                new[] { 0.5f, 0.5f },
                new long[] { 0, 1 },
                chunks,
                frameSeconds: 0.01,
                chunk => new[] { -1f, 0.01f, 123f },
                leadingContextSeconds: 0.5);

            Assert.Equal(-0.49, boundaries[0], 3);
            Assert.Equal(0.01, boundaries[1], 3);
        }

        [Fact]
        public void ShortLeadingContextCannotOverlapPreviousPhrase() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] { 2, 24 });

            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                new[] { 0.5f, 0.5f },
                new long[] { 0, 1 },
                chunks,
                frameSeconds: 0.01,
                chunk => new[] { -0.07f, 0.01f, 123f },
                leadingContextSeconds: 0.03);

            Assert.Equal(-0.02, boundaries[0], 3);
            Assert.Equal(0.01, boundaries[1], 3);
        }

        [Fact]
        public void ZeroLeadingContextKeepsFirstPhoneAtScoreStart() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] { 2, 24 });

            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                new[] { 0.5f, 0.5f },
                new long[] { 0, 1 },
                chunks,
                frameSeconds: 0.01,
                chunk => new[] { -0.07f, 0.01f, 123f },
                leadingContextSeconds: 0);

            Assert.Equal(0, boundaries[0], 3);
            Assert.Equal(0.01, boundaries[1], 3);
        }

        [Fact]
        public void ManualFirstBoundaryCanExtendConsonantIntoLeadingContext() {
            var boundaries = new[] { -0.057, 0.003, 0.5 };

            NeutrinoRenderer.ApplyManualBoundaryOverrides(
                boundaries,
                new double?[] { -0.1, null, null },
                leadingContextSeconds: 0.5);

            Assert.Equal(-0.1, boundaries[0], 3);
            Assert.Equal(0.003, boundaries[1], 3);
            Assert.Equal(0.103, boundaries[1] - boundaries[0], 3);
        }

        [Fact]
        public void ManualFirstBoundaryCannotExceedLeadingContext() {
            var boundaries = new[] { -0.02, 0.01, 0.5 };

            NeutrinoRenderer.ApplyManualBoundaryOverrides(
                boundaries,
                new double?[] { -0.1, null, null },
                leadingContextSeconds: 0.03);

            Assert.Equal(-0.02, boundaries[0], 3);
            Assert.Equal(0.01, boundaries[1], 3);
        }

        [Fact]
        public void ChunkedTimingDoesNotRepeatOneNoteDuration() {
            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(new long[] {
                1,
                NeutrinoPhoneme.PAU,
                2,
            });

            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                new[] { 0.5f, 0.5f, 0.5f },
                new long[] { 0, 1, 2 },
                chunks,
                frameSeconds: 0.01,
                chunk => new float[chunk.PhoneCount + 1]);

            Assert.Equal(0.5, boundaries[^1], 3);
        }

        [Theory]
        [InlineData("+")]
        [InlineData("+~")]
        [InlineData("+*")]
        [InlineData("+anything")]
        public void PlusPrefixedLyricsMatchOpenUtauExtensionSemantics(string lyric) {
            Assert.True(NeutrinoInferenceUtil.IsExtensionLyric(lyric));
        }

        [Fact]
        public void LegacyMinusExtensionRemainsSupported() {
            Assert.True(NeutrinoInferenceUtil.IsExtensionLyric("-"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("~+")]
        public void NonExtensionLyricsRemainIndependent(string lyric) {
            Assert.False(NeutrinoInferenceUtil.IsExtensionLyric(lyric));
        }

        [Fact]
        public void ExtensionNotesRepeatSustainPhoneWithTheirOwnPitchAndDuration() {
            int h = NeutrinoPhoneme.GetPhonemeId("h");
            int o = NeutrinoPhoneme.GetPhonemeId("o");
            var sequence = NeutrinoInferenceUtil.BuildScoreSequence(new[] {
                new NeutrinoScoreNoteInput(
                    392f,
                    0.5f,
                    false,
                    new[] {
                        new NeutrinoScorePhoneInput(h, sourceIndex: 0),
                        new NeutrinoScorePhoneInput(o, sourceIndex: 1),
                    }),
                new NeutrinoScoreNoteInput(
                    329.63f,
                    0.25f,
                    true,
                    Array.Empty<NeutrinoScorePhoneInput>()),
                new NeutrinoScoreNoteInput(
                    293.66f,
                    0.75f,
                    true,
                    Array.Empty<NeutrinoScorePhoneInput>()),
            });

            Assert.Equal(new long[] { h, o, o, o }, sequence.PhonemeIds);
            Assert.Equal(new[] { 392f, 392f, 329.63f, 293.66f }, sequence.ScorePitchesHz);
            Assert.Equal(new[] { 0.5f, 0.5f, 0.25f, 0.75f }, sequence.ScoreDurations);
            Assert.Equal(new long[] { 0, 1, 0, 0 }, sequence.PhonePositions);
            Assert.Equal(new[] { 0, 1, -1, -1 }, sequence.SourcePhoneIndices);
            Assert.Equal(5, sequence.ManualBoundaries.Length);

            var boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                sequence.ScoreDurations,
                sequence.PhonePositions,
                NeutrinoInferenceUtil.BuildPhoneChunks(sequence.PhonemeIds),
                frameSeconds: 0.01,
                chunk => new float[chunk.PhoneCount + 1]);
            Assert.Equal(1.5, boundaries[^1], 3);
        }

        [Fact]
        public void IndependentNoteWithoutPhonesStopsExtensionCarry() {
            int o = NeutrinoPhoneme.GetPhonemeId("o");
            var sequence = NeutrinoInferenceUtil.BuildScoreSequence(new[] {
                new NeutrinoScoreNoteInput(
                    392f,
                    0.5f,
                    false,
                    new[] { new NeutrinoScorePhoneInput(o) }),
                new NeutrinoScoreNoteInput(
                    349.23f,
                    0.5f,
                    false,
                    Array.Empty<NeutrinoScorePhoneInput>()),
                new NeutrinoScoreNoteInput(
                    329.63f,
                    0.5f,
                    true,
                    Array.Empty<NeutrinoScorePhoneInput>()),
            });

            Assert.Equal(new long[] { o }, sequence.PhonemeIds);
        }

        [Fact]
        public void FixedShapeModelOutputsRejectLengthMismatch() {
            var output = new[] { 0.1f, 0.2f };
            Assert.Same(output, NeutrinoInferenceUtil.RequireLength(output, 2, "test output"));

            var error = Assert.Throws<InvalidDataException>(
                () => NeutrinoInferenceUtil.RequireLength(output, 3, "test output"));
            Assert.Equal("test output length mismatch: actual 2, expected 3.", error.Message);
        }

        [Fact]
        public void TimingModelReturnsOneMoreBoundaryThanPhonemes() {
            var boundaries = new[] { 0f, 0.1f, 0.2f };
            Assert.Same(
                boundaries,
                NeutrinoInferenceUtil.RequireTimingBoundaryLength(boundaries, 2, "timing output"));

            var error = Assert.Throws<InvalidDataException>(
                () => NeutrinoInferenceUtil.RequireTimingBoundaryLength(
                    new[] { 0f, 0.1f }, 2, "timing output"));
            Assert.Equal("timing output length mismatch: actual 2, expected 3.", error.Message);
        }

        static void AssertChunk(
            NeutrinoPhoneChunk chunk,
            int phoneStart,
            int phoneCount,
            bool isActive) {

            Assert.Equal(phoneStart, chunk.PhoneStart);
            Assert.Equal(phoneCount, chunk.PhoneCount);
            Assert.Equal(isActive, chunk.IsActive);
        }

        static void AssertFrameChunk(
            NeutrinoFrameChunk chunk,
            int phoneStart,
            int phoneCount,
            int frameStart,
            int frameCount,
            bool isActive) {

            Assert.Equal(phoneStart, chunk.PhoneStart);
            Assert.Equal(phoneCount, chunk.PhoneCount);
            Assert.Equal(frameStart, chunk.FrameStart);
            Assert.Equal(frameCount, chunk.FrameCount);
            Assert.Equal(isActive, chunk.IsActive);
        }
    }
}
