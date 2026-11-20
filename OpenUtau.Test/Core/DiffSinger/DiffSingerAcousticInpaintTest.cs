using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// Frame-domain logic of DiffSinger acoustic inpainting: which frames changed,
    /// how the mask is expanded, the state identity and the ONNX tensors that carry
    /// the kept mel back into the model.
    /// </summary>
    public class DiffSingerAcousticInpaintTest {
        static AcousticCondition Cond(string name, params float[] values) =>
            new AcousticCondition(name, values);

        static AcousticInpaintState State(
                ulong key, ulong preEffectHash, ulong phraseHash,
                float[] mel, params AcousticCondition[] conditions) =>
            new AcousticInpaintState(
                key, 42, mel.Length / 2, 2, 11.6f, mel, conditions, preEffectHash, phraseHash);

        [Fact]
        public void BuildChangedMask_MarksOnlyTheFramesThatDiffer() {
            var previous = new[] { Cond("energy", 0, 0, 0, 0) };
            var current = new[] { Cond("energy", 0, 0, 0.5f, 0) };

            var mask = DiffSingerAcousticInpaint.BuildChangedMask(previous, current, 4);

            Assert.Equal(new[] { false, false, true, false }, mask);
        }

        [Fact]
        public void BuildChangedMask_UsesATighterEpsilonForF0ThanForOtherChannels() {
            // 5e-4 is below the f0 epsilon but above the others, so the same delta is
            // not a change for f0 while it is one for a variance channel.
            var f0 = DiffSingerAcousticInpaint.BuildChangedMask(
                new[] { Cond("f0", 440f) }, new[] { Cond("f0", 440.0005f) }, 1);
            Assert.False(f0[0]);

            var energy = DiffSingerAcousticInpaint.BuildChangedMask(
                new[] { Cond("energy", 0f) }, new[] { Cond("energy", 0.0005f) }, 1);
            Assert.True(energy[0]);
        }

        [Fact]
        public void BuildChangedMask_MarksAFrameWhenAnyValueOfItsVectorDiffers() {
            var previous = new[] {
                new AcousticCondition("spk_embed", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 2 }, 3),
            };
            var current = new[] {
                new AcousticCondition("spk_embed", new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 3 }, 3),
            };

            var mask = DiffSingerAcousticInpaint.BuildChangedMask(previous, current, 3);

            Assert.Equal(new[] { false, false, true }, mask);
        }

        [Fact]
        public void BuildChangedMask_TreatsALayoutMismatchAsAChangedPhrase() {
            var previous = new[] { Cond("energy", 0, 0, 0) };
            var current = new[] { Cond("energy", 0, 0) };

            var mask = DiffSingerAcousticInpaint.BuildChangedMask(previous, current, 3);

            Assert.All(mask, changed => Assert.True(changed));
        }

        [Fact]
        public void Expand_GrowsTheMaskSymmetricallyAndClipsAtTheEdges() {
            var mask = new[] { false, false, false, true, false, false, false };

            var expanded = DiffSingerAcousticInpaint.Expand(mask, 1);

            Assert.Equal(new[] { false, false, true, true, true, false, false }, expanded);

            var first = DiffSingerAcousticInpaint.Expand(new[] { true, false, false }, 1);
            Assert.Equal(new[] { true, true, false }, first);

            var last = DiffSingerAcousticInpaint.Expand(new[] { false, false, true }, 1);
            Assert.Equal(new[] { false, true, true }, last);
        }

        [Fact]
        public void Expand_KeepsAnEmptyMaskEmpty() {
            var expanded = DiffSingerAcousticInpaint.Expand(new[] { false, false, false }, 2);
            Assert.DoesNotContain(true, expanded);
        }

        [Fact]
        public void BuildKey_IsStableForTheSameStructureAndChangesWithIt() {
            var tokens = new long[] { 1, 2, 3 };
            var durations = new int[] { 4, 5, 6 };
            var languages = new long[] { 0, 0, 0 };

            ulong key = DiffSingerAcousticInpaint.BuildKey(42, tokens, durations, languages, 15, 128, 11.61f, 0.6, 20);

            Assert.Equal(key, DiffSingerAcousticInpaint.BuildKey(
                42, tokens.ToArray(), durations.ToArray(), languages.ToArray(), 15, 128, 11.61f, 0.6, 20));
            Assert.NotEqual(key, DiffSingerAcousticInpaint.BuildKey(
                42, tokens, new[] { 4, 5, 7 }, languages, 16, 128, 11.61f, 0.6, 20));
            Assert.NotEqual(key, DiffSingerAcousticInpaint.BuildKey(
                42, new long[] { 1, 2, 4 }, durations, languages, 15, 128, 11.61f, 0.6, 20));
            Assert.NotEqual(key, DiffSingerAcousticInpaint.BuildKey(
                42, tokens, durations, languages, 15, 128, 11.61f, 0.6, 10));
            Assert.NotEqual(key, DiffSingerAcousticInpaint.BuildKey(
                42, tokens, durations, languages, 15, 128, 11.61f, 0.4, 20));
            Assert.NotEqual(key, DiffSingerAcousticInpaint.BuildKey(
                43, tokens, durations, languages, 15, 128, 11.61f, 0.6, 20));
        }

        [Fact]
        public void State_IsCompatibleOnlyWithTheSameLayoutAndModel() {
            var state = State(1, 2, 3, new float[] { 0, 0, 0, 0 }, Cond("energy", 0, 0));

            Assert.True(state.IsCompatible(2, 2, 11.6f, new[] { Cond("energy", 0, 0) }, 42));
            Assert.False(state.IsCompatible(2, 2, 11.6f, new[] { Cond("energy", 0, 0) }, 43));
            Assert.False(state.IsCompatible(3, 2, 11.6f, new[] { Cond("energy", 0, 0, 0) }, 42));
            Assert.False(state.IsCompatible(2, 2, 11.6f, new[] { Cond("velocity", 0, 0) }, 42));
            Assert.False(state.IsCompatible(2, 2, 11.6f,
                new[] { Cond("energy", 0, 0), Cond("f0", 0, 0) }, 42));
        }

        [Fact]
        public void State_ClonesItsInputs() {
            var mel = new float[] { 1, 2, 3, 4 };
            var condition = Cond("energy", 1, 2);
            var state = State(1, 2, 3, mel, condition);

            mel[0] = 100;
            condition.Values[0] = 100;

            Assert.Equal(1f, state.Mel[0]);
            Assert.Equal(1f, state.Conditions[0].Values[0]);
        }

        [Fact]
        public void CreateRefMelInput_UsesTheKeptMelOrZeros() {
            var state = State(1, 2, 3, new float[] { 1, 2, 3, 4 }, Cond("energy", 0, 0));

            var kept = DiffSingerAcousticInpaint.CreateRefMelInput(state, 2, 2);
            Assert.Equal("ref_mel", kept.Name);
            Assert.Equal(new[] { 1, 2, 2 }, kept.AsTensor<float>().Dimensions.ToArray());
            Assert.Equal(new[] { 1f, 2f, 3f, 4f }, kept.AsTensor<float>().ToArray());

            var zeros = DiffSingerAcousticInpaint.CreateRefMelInput(null, 2, 2);
            Assert.Equal(new[] { 0f, 0f, 0f, 0f }, zeros.AsTensor<float>().ToArray());
        }

        [Fact]
        public void CreateMaskInput_UsesBoolWhenTheGraphDoes() {
            var input = DiffSingerAcousticInpaint.CreateMaskInput(
                new[] { true, false, true }, 3, asBool: true);

            Assert.Equal("inpaint_mask", input.Name);
            Assert.Equal(new[] { true, false, true }, input.AsTensor<bool>().ToArray());
        }

        [Fact]
        public void CreateMaskInput_UsesFloatForOlderExports() {
            var input = DiffSingerAcousticInpaint.CreateMaskInput(
                new[] { true, false, true }, 3, asBool: false);

            Assert.Equal(new[] { 1f, 0f, 1f }, input.AsTensor<float>().ToArray());
        }

        [Fact]
        public void CreateMaskInput_DefaultsToRegenerateEverything() {
            var tensor = DiffSingerAcousticInpaint.CreateMaskInput(null, 4, asBool: true);
            Assert.All(tensor.AsTensor<bool>().ToArray(), value => Assert.True(value));

            var asFloat = DiffSingerAcousticInpaint.CreateMaskInput(null, 4, asBool: false);
            Assert.All(asFloat.AsTensor<float>().ToArray(), value => Assert.Equal(1f, value));
        }

        [Fact]
        public void VerifyRetainedFrames_IgnoresMaskedFramesAndCatchesChangedKeptOnes() {
            var reference = new float[] { 1, 2, 3, 4, 5, 6 };
            var mask = new[] { true, false, false };

            Assert.True(DiffSingerAcousticInpaint.VerifyRetainedFrames(
                new float[] { 9, 9, 3, 4, 5, 6 }, reference, mask, 3, 2, out _));

            Assert.False(DiffSingerAcousticInpaint.VerifyRetainedFrames(
                new float[] { 9, 9, 3, 4, 5, 7 }, reference, mask, 3, 2, out float difference));
            Assert.Equal(1f, difference);
        }

        [Fact]
        public void Cache_EvictsTheLeastRecentlyUsedState() {
            var cache = new AcousticInpaintStateCache(2);
            cache.Set(State(1, 10, 100, new float[] { 1, 1, 1, 1 }, Cond("energy", 0, 0)));
            cache.Set(State(2, 20, 200, new float[] { 2, 2, 2, 2 }, Cond("energy", 0, 0)));

            Assert.NotNull(cache.TryGet(1));
            cache.Set(State(3, 30, 300, new float[] { 3, 3, 3, 3 }, Cond("energy", 0, 0)));

            Assert.NotNull(cache.TryGet(1));
            Assert.Null(cache.TryGet(2));
            Assert.NotNull(cache.TryGet(3));
        }

        [Fact]
        public void Cache_RemoveForPhraseDropsOnlyOtherVersionsOfTheSamePhrase() {
            var cache = new AcousticInpaintStateCache(8);
            cache.Set(State(1, 10, 100, new float[] { 1, 1, 1, 1 }, Cond("energy", 0, 0)));
            cache.Set(State(2, 10, 200, new float[] { 2, 2, 2, 2 }, Cond("energy", 0, 0)));
            cache.Set(State(3, 20, 300, new float[] { 3, 3, 3, 3 }, Cond("energy", 0, 0)));

            // The wav for phrase hash 200 was served from disk: the state for the
            // other version (hash 100) must go, the matching one and other phrases stay.
            cache.RemoveForPhrase(10, 200);

            Assert.Null(cache.TryGet(1));
            Assert.NotNull(cache.TryGet(2));
            Assert.NotNull(cache.TryGet(3));
        }

        [Fact]
        public void LocalRetake_MigratesTheLegacyBooleanOnce() {
            var legacyOn = new Preferences.SerializablePreferences {
                DiffSingerVarianceLocalPitchPatch = true,
            };
            legacyOn.MigrateDiffSingerLocalRetake();
            Assert.Equal((int)DiffSingerLocalRetake.Variance, legacyOn.DiffSingerLocalRetakeLevel);

            var legacyOff = new Preferences.SerializablePreferences();
            legacyOff.MigrateDiffSingerLocalRetake();
            Assert.Equal((int)DiffSingerLocalRetake.Off, legacyOff.DiffSingerLocalRetakeLevel);

            var explicitChoice = new Preferences.SerializablePreferences {
                DiffSingerVarianceLocalPitchPatch = false,
                DiffSingerLocalRetakeLevel = (int)DiffSingerLocalRetake.VarianceAndAcoustic,
            };
            explicitChoice.MigrateDiffSingerLocalRetake();
            Assert.Equal((int)DiffSingerLocalRetake.VarianceAndAcoustic, explicitChoice.DiffSingerLocalRetakeLevel);
        }

        [Fact]
        public void LocalRetake_ClampsOutOfRangeValues() {
            var prefs = new Preferences.SerializablePreferences { DiffSingerLocalRetakeLevel = 99 };
            prefs.MigrateDiffSingerLocalRetake();
            Assert.Equal((int)DiffSingerLocalRetake.Off, prefs.DiffSingerLocalRetakeLevel);
        }

        [Fact]
        public void VoiceColorCurveEditsDoNotRunThePhonemizer() {
            // Curve commands must not invalidate phonemes: that is what keeps the frame
            // grid stable, and what makes a voice-color curve edit local for inpainting.
            var project = new UProject();
            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);

            var command = new SetCurveCommand(project, part, DiffSingerUtils.VoiceColorHeader + "01", 0, 50, 0, 40);

            Assert.True(command.ValidateOptions.SkipPhonemizer);
            Assert.True(command.ValidateOptions.SkipPhoneme);
        }

        [Fact]
        public void ValidateModelInputs_AcceptsBothMaskElementTypes() {
            var names = new[] { "tokens", "ref_mel", "inpaint_mask" };
            Func<string, Type?> boolMask = name => name == "inpaint_mask" ? typeof(bool) : typeof(float);
            Func<string, Type?> floatMask = name => name == "inpaint_mask" ? typeof(float) : typeof(float);

            Assert.Equal(typeof(bool), DiffSingerAcousticInpaint.ValidateModelInputs("a.onnx", names, boolMask));
            Assert.Equal(typeof(float), DiffSingerAcousticInpaint.ValidateModelInputs("a.onnx", names, floatMask));
        }

        [Fact]
        public void ValidateModelInputs_RejectsAMissingInpaintInput() {
            var names = new[] { "tokens", "ref_mel" };

            var error = Assert.Throws<InvalidDataException>(() =>
                DiffSingerAcousticInpaint.ValidateModelInputs("a.onnx", names, _ => typeof(float)));

            Assert.Contains("inpaint_mask", error.Message);
            Assert.Contains("--inpaint", error.Message);
        }

        [Fact]
        public void ValidateModelInputs_RejectsWrongElementTypes() {
            var names = new[] { "ref_mel", "inpaint_mask" };

            var refMel = Assert.Throws<InvalidDataException>(() =>
                DiffSingerAcousticInpaint.ValidateModelInputs(
                    "a.onnx", names, name => name == "ref_mel" ? typeof(double) : typeof(bool)));
            Assert.Contains("ref_mel", refMel.Message);

            var mask = Assert.Throws<InvalidDataException>(() =>
                DiffSingerAcousticInpaint.ValidateModelInputs(
                    "a.onnx", names, name => name == "ref_mel" ? typeof(float) : typeof(long)));
            Assert.Contains("inpaint_mask", mask.Message);
        }
    }
}
