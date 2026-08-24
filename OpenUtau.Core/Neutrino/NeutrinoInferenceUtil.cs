using System;
using System.Collections.Generic;
using System.IO;

namespace OpenUtau.Core.Neutrino {
    internal readonly struct NeutrinoScorePhoneInput {
        public long PhonemeId { get; }
        public int SourceIndex { get; }
        public double? ManualBoundarySeconds { get; }

        public NeutrinoScorePhoneInput(
            long phonemeId,
            int sourceIndex = -1,
            double? manualBoundarySeconds = null) {

            PhonemeId = phonemeId;
            SourceIndex = sourceIndex;
            ManualBoundarySeconds = manualBoundarySeconds;
        }
    }

    internal readonly struct NeutrinoScoreNoteInput {
        public float PitchHz { get; }
        public float DurationSeconds { get; }
        public bool IsExtension { get; }
        public NeutrinoScorePhoneInput[] Phones { get; }

        public NeutrinoScoreNoteInput(
            float pitchHz,
            float durationSeconds,
            bool isExtension,
            NeutrinoScorePhoneInput[] phones) {

            PitchHz = pitchHz;
            DurationSeconds = durationSeconds;
            IsExtension = isExtension;
            Phones = phones ?? Array.Empty<NeutrinoScorePhoneInput>();
        }
    }

    internal sealed class NeutrinoScoreSequence {
        public long[] PhonemeIds { get; }
        public float[] ScorePitchesHz { get; }
        public float[] ScoreDurations { get; }
        public long[] PhonePositions { get; }
        public int[] SourcePhoneIndices { get; }
        public double?[] ManualBoundaries { get; }

        public NeutrinoScoreSequence(
            long[] phonemeIds,
            float[] scorePitchesHz,
            float[] scoreDurations,
            long[] phonePositions,
            int[] sourcePhoneIndices,
            double?[] manualBoundaries) {

            PhonemeIds = phonemeIds;
            ScorePitchesHz = scorePitchesHz;
            ScoreDurations = scoreDurations;
            PhonePositions = phonePositions;
            SourcePhoneIndices = sourcePhoneIndices;
            ManualBoundaries = manualBoundaries;
        }
    }

    internal readonly struct NeutrinoPhoneChunk {
        public int PhoneStart { get; }
        public int PhoneCount { get; }
        public bool IsActive { get; }

        public NeutrinoPhoneChunk(int phoneStart, int phoneCount, bool isActive) {
            PhoneStart = phoneStart;
            PhoneCount = phoneCount;
            IsActive = isActive;
        }
    }

    internal readonly struct NeutrinoFrameChunk {
        public int PhoneStart { get; }
        public int PhoneCount { get; }
        public int FrameStart { get; }
        public int FrameCount { get; }
        public bool IsActive { get; }

        public NeutrinoFrameChunk(
            int phoneStart,
            int phoneCount,
            int frameStart,
            int frameCount,
            bool isActive) {

            PhoneStart = phoneStart;
            PhoneCount = phoneCount;
            FrameStart = frameStart;
            FrameCount = frameCount;
            IsActive = isActive;
        }
    }

    internal static class NeutrinoInferenceUtil {
        public static bool IsExtensionLyric(string lyric) {
            return lyric == "-" || lyric?.StartsWith("+", StringComparison.Ordinal) == true;
        }

        public static NeutrinoScoreSequence BuildScoreSequence(
            IReadOnlyList<NeutrinoScoreNoteInput> notes) {

            var phonemeIds = new List<long>();
            var scorePitchesHz = new List<float>();
            var scoreDurations = new List<float>();
            var phonePositions = new List<long>();
            var sourcePhoneIndices = new List<int>();
            var manualBoundaries = new List<double?>();
            long? sustainPhonemeId = null;

            foreach (var note in notes) {
                var phones = note.Phones;
                if (phones.Length == 0) {
                    if (!note.IsExtension || !sustainPhonemeId.HasValue) {
                        if (!note.IsExtension) {
                            sustainPhonemeId = null;
                        }
                        continue;
                    }

                    // Official long-mark labels repeat the preceding note's final
                    // phoneme, while keeping the extension note's pitch and duration.
                    phones = new[] { new NeutrinoScorePhoneInput(sustainPhonemeId.Value) };
                }

                float durationSeconds = Math.Max(0.001f, note.DurationSeconds);
                for (int position = 0; position < phones.Length; position++) {
                    var phone = phones[position];
                    phonemeIds.Add(phone.PhonemeId);
                    scorePitchesHz.Add(phone.PhonemeId == NeutrinoPhoneme.PAU ? 0 : note.PitchHz);
                    scoreDurations.Add(durationSeconds);
                    phonePositions.Add(position);
                    sourcePhoneIndices.Add(phone.SourceIndex);
                    manualBoundaries.Add(phone.ManualBoundarySeconds);
                }
                sustainPhonemeId = phones[^1].PhonemeId;
            }

            manualBoundaries.Add(null);
            return new NeutrinoScoreSequence(
                phonemeIds.ToArray(),
                scorePitchesHz.ToArray(),
                scoreDurations.ToArray(),
                phonePositions.ToArray(),
                sourcePhoneIndices.ToArray(),
                manualBoundaries.ToArray());
        }

        public static float[] RequireLength(float[] values, int expectedLength, string outputName) {
            if (values.Length != expectedLength) {
                throw new InvalidDataException(
                    $"{outputName} length mismatch: actual {values.Length}, expected {expectedLength}.");
            }
            return values;
        }

        public static float[] RequireTimingBoundaryLength(
            float[] values,
            int phonemeCount,
            string outputName) {

            return RequireLength(values, checked(phonemeCount + 1), outputName);
        }

        public static NeutrinoPhoneChunk[] BuildPhoneChunks(long[] phonemeIds) {
            var chunks = new System.Collections.Generic.List<NeutrinoPhoneChunk>();
            if (phonemeIds.Length == 0) {
                return chunks.ToArray();
            }

            int chunkStart = 0;
            bool chunkIsActive = true;
            bool inPause = false;
            bool afterBreath = false;
            for (int phone = 0; phone < phonemeIds.Length; phone++) {
                if (phonemeIds[phone] == NeutrinoPhoneme.PAU) {
                    if (!inPause) {
                        if (phone > chunkStart) {
                            chunks.Add(new NeutrinoPhoneChunk(
                                chunkStart, phone - chunkStart, chunkIsActive));
                        }
                        chunkStart = phone;
                        chunkIsActive = false;
                        inPause = true;
                        afterBreath = false;
                    }
                    continue;
                }

                if (phonemeIds[phone] == NeutrinoPhoneme.BR) {
                    inPause = false;
                    afterBreath = true;
                    continue;
                }

                if (inPause || afterBreath) {
                    chunks.Add(new NeutrinoPhoneChunk(
                        chunkStart, phone - chunkStart, chunkIsActive));
                    chunkStart = phone;
                    chunkIsActive = true;
                    inPause = false;
                    afterBreath = false;
                }
            }
            chunks.Add(new NeutrinoPhoneChunk(
                chunkStart, phonemeIds.Length - chunkStart, chunkIsActive));
            return chunks.ToArray();
        }

        public static double[] BuildTimingBoundaries(
            float[] scoreDurations,
            long[] phonePositions,
            NeutrinoPhoneChunk[] chunks,
            double frameSeconds,
            Func<NeutrinoPhoneChunk, float[]> predictBoundaryShifts,
            double? leadingContextSeconds = null) {

            if (scoreDurations.Length != phonePositions.Length) {
                throw new ArgumentException("Score duration and phone position lengths must match.");
            }

            var baseBoundaries = BuildBaseBoundaryTimes(scoreDurations, phonePositions);
            var globalBoundaryShifts = new float[baseBoundaries.Length];
            foreach (var chunk in chunks) {
                if (!chunk.IsActive) {
                    continue;
                }
                var chunkShifts = predictBoundaryShifts(chunk);
                if (chunkShifts == null || chunkShifts.Length < chunk.PhoneCount) {
                    throw new InvalidDataException(
                        $"Timing chunk output is too short: actual {chunkShifts?.Length ?? 0}, " +
                        $"expected at least {chunk.PhoneCount}.");
                }

                // The official loader copies one value per phone and discards the
                // model's extra final value before applying shifts globally.
                Array.Copy(
                    chunkShifts,
                    0,
                    globalBoundaryShifts,
                    chunk.PhoneStart,
                    chunk.PhoneCount);
            }
            return ApplyTimingBoundaryShifts(
                baseBoundaries,
                globalBoundaryShifts,
                frameSeconds,
                leadingContextSeconds);
        }

        public static NeutrinoFrameChunk[] BuildFrameChunks(
            NeutrinoPhoneChunk[] phoneChunks,
            double[] boundaries,
            int totalFrames,
            double frameSeconds) {

            var chunks = new NeutrinoFrameChunk[phoneChunks.Length];
            for (int i = 0; i < chunks.Length; i++) {
                var chunk = phoneChunks[i];
                int frameStart = Math.Clamp(
                    (int)Math.Round(boundaries[chunk.PhoneStart] / frameSeconds),
                    0,
                    totalFrames);
                int frameEnd = Math.Clamp(
                    (int)Math.Round(boundaries[chunk.PhoneStart + chunk.PhoneCount] / frameSeconds),
                    frameStart,
                    totalFrames);
                chunks[i] = new NeutrinoFrameChunk(
                    chunk.PhoneStart,
                    chunk.PhoneCount,
                    frameStart,
                    frameEnd - frameStart,
                    chunk.IsActive);
            }
            return chunks;
        }

        public static T[] Slice<T>(T[] values, int start, int length) {
            var result = new T[length];
            Array.Copy(values, start, result, 0, length);
            return result;
        }

        public static double NormalizeBoundaryStart(double[] boundaries) {
            if (boundaries.Length == 0) {
                return 0;
            }
            double start = boundaries[0];
            for (int i = 0; i < boundaries.Length; i++) {
                boundaries[i] -= start;
            }
            return start;
        }

        static double[] BuildBaseBoundaryTimes(float[] scoreDurations, long[] phonePositions) {
            int numPhones = scoreDurations.Length;
            var boundaries = new double[numPhones + 1];
            double time = 0;
            for (int i = 0; i < numPhones; i++) {
                boundaries[i] = time;
                long nextPosition = i + 1 < numPhones ? phonePositions[i + 1] : -1;
                if (i == numPhones - 1 || nextPosition <= phonePositions[i]) {
                    time += scoreDurations[i];
                }
            }
            boundaries[numPhones] = time;
            return boundaries;
        }

        static double[] ApplyTimingBoundaryShifts(
            double[] baseBoundaries,
            float[] boundaryShifts,
            double frameSeconds,
            double? leadingContextSeconds) {

            var boundaries = (double[])baseBoundaries.Clone();
            if (boundaries.Length > 1 && leadingContextSeconds.HasValue) {
                // Official labels normally have a leading pau, so the first active
                // phone is not global boundary zero and receives gluon[0]. Emulate
                // that context when an OpenUtau phrase starts directly with a phone.
                double contextSeconds = Math.Max(0, leadingContextSeconds.Value);
                double minBoundary = Math.Min(0, -contextSeconds + frameSeconds);
                double shifted = baseBoundaries[0] + boundaryShifts[0];
                boundaries[0] = Math.Round(
                    Math.Max(shifted, minBoundary) * 1000.0) / 1000.0;
            }
            for (int i = 1; i < boundaries.Length - 1; i++) {
                double shifted = baseBoundaries[i] + boundaryShifts[i];
                boundaries[i] = Math.Round(
                    Math.Max(shifted, boundaries[i - 1] + frameSeconds) * 1000.0) / 1000.0;
            }
            for (int i = 1; i < boundaries.Length; i++) {
                if (boundaries[i] <= boundaries[i - 1]) {
                    boundaries[i] = Math.Round(
                        (boundaries[i - 1] + frameSeconds) * 1000.0) / 1000.0;
                }
            }
            return boundaries;
        }
    }
}
