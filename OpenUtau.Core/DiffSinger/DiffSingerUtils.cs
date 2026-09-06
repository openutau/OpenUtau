using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Core.Render;

namespace OpenUtau.Core.DiffSinger {
    public static class DiffSingerUtils {
        public const string VELC = "velc";
        public const string ENE = "ene";
        public const string PEXP = "pexp";
        public const string VoiceColorHeader = "cl";
        public const int headFrames = 8;
        public const int tailFrames = 8;

        public static readonly Dictionary<string, Func<float, float, float>> VarianceDeltaFunctions =
            new() {
                {ENE, (x, y) => x + y * 12 / 100},
                {Format.Ustx.BREC, (x, y) => x + y * 12 / 100},
                {Format.Ustx.VOIC, (x, y) => x + (y - 100) * 12 / 100},
                {Format.Ustx.TENC, (x, y) => x + y / 20},
            };

        public static float GetHeadMs(double frameMs) {
            return (float)(frameMs * headFrames);
        }

        public static float GetHeadMs(RenderPhrase phrase) {
            var singer = phrase.singer as DiffSingerSinger;
            if (singer == null) {
                throw new InvalidDataException("Singer is not DiffSingerSinger.");
            }
            return GetHeadMs(singer.dsConfig.frameMs());
        }

        public static float GetTailMs(double frameMs) {
            return (float)(frameMs * tailFrames);
        }

        public static float GetTailMs(RenderPhrase phrase) {
            var singer = phrase.singer as DiffSingerSinger;
            if (singer == null) {
                throw new InvalidDataException("Singer is not DiffSingerSinger.");
            }
            return GetTailMs(singer.dsConfig.frameMs());
        }

        public static int[] DurationsMsToFrames(IEnumerable<double> durationsMs, double frameMs) {
            var result = new List<int>();
            double accumulatedMs = 0;
            int previousFrame = 0;
            foreach (var durationMs in durationsMs) {
                if (durationMs < 0) {
                    throw new InvalidDataException($"Negative DiffSinger duration: {durationMs} ms.");
                }
                accumulatedMs += durationMs;
                int frame = (int)Math.Round(accumulatedMs / frameMs + 0.5, MidpointRounding.ToEven);
                result.Add(frame - previousFrame);
                previousFrame = frame;
            }
            return result.ToArray();
        }

        /// <summary>
        /// Model segments of a phrase: head "SP", phonemes (with an "SP"
        /// silence segment per inter-phoneme gap, since merged phrases can
        /// bridge gaps), and tail "SP". PhoneIndex is -1 for non-phonemes.
        /// </summary>
        public static List<(string Phoneme, double DurationMs, int PhoneIndex)> PaddedSegments(
            RenderPhrase phrase, double frameMs, int headFrames, int tailFrames) {
            var result = new List<(string, double, int)> { ("SP", headFrames * frameMs, -1) };
            for (int i = 0; i < phrase.phones.Length; ++i) {
                if (i > 0) {
                    double gapMs = phrase.phones[i].positionMs - phrase.phones[i - 1].endMs;
                    if (gapMs > 0) {
                        result.Add(("SP", gapMs, -1));
                    }
                }
                result.Add((phrase.phones[i].phoneme, phrase.phones[i].durationMs, i));
            }
            result.Add(("SP", tailFrames * frameMs, -1));
            return result;
        }

        public static int[] PaddedPhoneDurations(RenderPhrase phrase, double frameMs, int headFrames, int tailFrames) {
            return DurationsMsToFrames(
                PaddedSegments(phrase, frameMs, headFrames, tailFrames).Select(s => s.DurationMs),
                frameMs);
        }

        public static long[] PaddedLanguageIds(
            RenderPhrase phrase, double frameMs, int headFrames, int tailFrames, Func<string, long> langIdByPhoneme) {
            return PaddedSegments(phrase, frameMs, headFrames, tailFrames)
                .Select(s => s.PhoneIndex >= 0 ? langIdByPhoneme(phrase.phones[s.PhoneIndex].phoneme) : 0L)
                .ToArray();
        }

        public static (Int64[] wordDiv, Int64[] wordDur) PaddedWordDivAndDur(
            RenderPhrase phrase, int[] phDur, Func<string, bool> isVowel, double frameMs, int headFrames, int tailFrames) {
            var segments = PaddedSegments(phrase, frameMs, headFrames, tailFrames);
            if (segments.Count == 0) {
                throw new InvalidDataException("DiffSinger word mode requires at least one phoneme.");
            }
            if (phDur.Length != segments.Count) {
                throw new InvalidDataException(
                    $"DiffSinger word mode duration length mismatch: {phDur.Length} durations for {segments.Count} padded tokens.");
            }

            var vowelIds = Enumerable.Range(0, segments.Count)
                .Where(i => segments[i].PhoneIndex >= 0 && isVowel(segments[i].Phoneme))
                .ToArray();
            if (vowelIds.Length == 0) {
                // The last real phoneme (the tail "SP" is the last segment).
                vowelIds = new int[] { segments.Count - 2 };
            }

            // Vowel indexes are in segment space (phones shifted by +1 for
            // the head "SP"), hence no +1 on the first word.
            var wordDiv = vowelIds.Zip(vowelIds.Skip(1), (a, b) => (Int64)(b - a))
                .Prepend((Int64)vowelIds[0])
                .Append((Int64)segments.Count - vowelIds[^1])
                .ToArray();

            if (wordDiv.Any(d => d <= 0)) {
                throw new InvalidDataException("DiffSinger word mode generated an empty word division.");
            }
            if (wordDiv.Sum() != phDur.Length) {
                throw new InvalidDataException(
                    $"DiffSinger word mode division mismatch: word_div sums to {wordDiv.Sum()} for {phDur.Length} padded tokens.");
            }

            var wordDur = new Int64[wordDiv.Length];
            int offset = 0;
            for (int i = 0; i < wordDiv.Length; ++i) {
                int length = (int)wordDiv[i];
                long sum = 0;
                for (int j = 0; j < length; ++j) {
                    sum += phDur[offset + j];
                }
                wordDur[i] = sum;
                offset += length;
            }
            if (wordDur.Sum() != phDur.Sum()) {
                throw new InvalidDataException(
                    $"DiffSinger word mode duration mismatch: word_dur sums to {wordDur.Sum()} for {phDur.Sum()} phone frames.");
            }
            return (wordDiv, wordDur);
        }

        public static int[] FitDurationSum(int[] durations, int totalFrames) {
            if (durations.Length == 0) {
                return durations;
            }
            var result = durations.ToArray();
            int delta = totalFrames - result.Sum();
            result[^1] += delta;
            if (result[^1] < 0) {
                int deficit = -result[^1];
                result[^1] = 0;
                for (int i = result.Length - 2; i >= 0 && deficit > 0; --i) {
                    int take = Math.Min(result[i], deficit);
                    result[i] -= take;
                    deficit -= take;
                }
                if (deficit > 0) {
                    throw new InvalidDataException(
                        $"Cannot fit DiffSinger durations to {totalFrames} frames.");
                }
            }
            return result;
        }

        public static double[] SampleCurve(RenderPhrase phrase, float[] curve, double defaultValue, double frameMs, int length, int headFrames, int tailFrames, Func<double, double> convert) {
            const int interval = 5;
            var result = new double[length];
            if (curve == null || curve.Length == 0) {
                Array.Fill(result, defaultValue);
                return result;
            }

            var startMs = phrase.positionMs - headFrames * frameMs;
            for (int i = 0; i < length; i++) {
                double posMs = startMs + i * frameMs;
                int ticks = phrase.timeAxis.MsPosToTickPos(posMs) - (phrase.position - phrase.leading);
                double index = (double)ticks / interval;
                if (index <= 0) {
                    result[i] = convert(curve[0]);
                } else if (index >= curve.Length - 1) {
                    result[i] = convert(curve[^1]);
                } else {
                    int index0 = (int)Math.Floor(index);
                    int index1 = index0 + 1;
                    double value = curve[index0] + (curve[index1] - curve[index0]) * (index - index0);
                    result[i] = convert(value);
                }
            }
            return result;
        }


        //MusicMath.Linear, but float numbers are used instead of double
        public static float LinearF(float x0, float x1, float y0, float y1, float x) {
            const float ep = 0.001f;
            if(x1 - x0 < ep){
                return y1;
            }
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }

        /// <summary>
        /// Resample a curve to a new length.
        /// Used when the hopsize of the variance model is different from the hopsize of the acoustic model.
        /// </summary>
        /// <param name="curve">The curve to resample.</param>
        /// <param name="length">The new length of the curve.</param>
        public static float[] ResampleCurve(float[] curve, int length) {
            if (curve == null || curve.Length == 0) {
                return null;
            }
            if(length == curve.Length){
                return curve;
            }
            if(length == 1){
                return new float[]{curve[0]};
            }
            float[] result = new float[length];
            for (int i = 0; i < length; i++) {
                var x = (float)i / (length - 1) * (curve.Length - 1);
                int x0 = (int)x;
                int x1 = Math.Min(x0 + 1, curve.Length - 1);
                float y0 = curve[x0];
                float y1 = curve[x1];
                result[i] = LinearF(x0, x1, y0, y1, x);
            }
            return result;
        }

        public static float[] ResamplePaddedCurve(
                float[] curve, int length,
                int sourceHeadFrames, int sourceTailFrames,
                int targetHeadFrames, int targetTailFrames,
                float sourceFrameMs, float targetFrameMs) {
            if (curve == null || curve.Length == 0) {
                return null;
            }
            if (length == curve.Length
                    && sourceHeadFrames == targetHeadFrames
                    && sourceTailFrames == targetTailFrames
                    && Math.Abs(sourceFrameMs - targetFrameMs) < 0.001f) {
                return curve;
            }
            int sourceBodyFrames = curve.Length - sourceHeadFrames - sourceTailFrames;
            int targetBodyFrames = length - targetHeadFrames - targetTailFrames;
            if (sourceBodyFrames < 0 || targetBodyFrames < 0) {
                return ResampleCurve(curve, length, sourceFrameMs, targetFrameMs);
            }

            var result = new float[length];
            CopyResampledSegment(curve, 0, sourceHeadFrames, result, 0, targetHeadFrames,
                sourceFrameMs, targetFrameMs);
            CopyResampledSegment(curve, sourceHeadFrames, sourceBodyFrames,
                result, targetHeadFrames, targetBodyFrames, sourceFrameMs, targetFrameMs);
            CopyResampledSegment(curve, curve.Length - sourceTailFrames, sourceTailFrames,
                result, length - targetTailFrames, targetTailFrames, sourceFrameMs, targetFrameMs);
            return result;
        }

        static void CopyResampledSegment(
                float[] source, int sourceStart, int sourceLength,
                float[] target, int targetStart, int targetLength,
                float sourceFrameMs, float targetFrameMs) {
            if (targetLength <= 0) {
                return;
            }
            if (sourceLength <= 0) {
                Array.Fill(target, 0f, targetStart, targetLength);
                return;
            }
            var segment = new float[sourceLength];
            Array.Copy(source, sourceStart, segment, 0, sourceLength);
            var resampled = ResampleCurve(segment, targetLength, sourceFrameMs, targetFrameMs);
            Array.Copy(resampled, 0, target, targetStart, targetLength);
        }

        public static float[] ResampleCurve(float[] curve, int length, float sourceFrameMs, float targetFrameMs) {
            if (curve == null || curve.Length == 0) {
                return null;
            }
            if (length == curve.Length && Math.Abs(sourceFrameMs - targetFrameMs) < 0.001f) {
                return curve;
            }
            if (length == 1 || curve.Length == 1 || sourceFrameMs <= 0 || targetFrameMs <= 0) {
                return ResampleCurve(curve, length);
            }

            float[] result = new float[length];
            for (int i = 0; i < length; i++) {
                var x = i * targetFrameMs / sourceFrameMs;
                if (x <= 0) {
                    result[i] = curve[0];
                } else if (x >= curve.Length - 1) {
                    result[i] = curve[^1];
                } else {
                    int x0 = (int)Math.Floor(x);
                    int x1 = x0 + 1;
                    result[i] = LinearF(x0, x1, curve[x0], curve[x1], x);
                }
            }
            return result;
        }

        /// <summary>
        /// Validate the shape of a tensor.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="tensor">Tensor to be validated</param>
        /// <param name="expectedShape">Expected shape of the tensor, -1 means the length of the axis is dynamic</param>
        /// <returns></returns>
        public static bool ValidateShape<T>(Tensor<T> tensor, int[] expectedShape){
            var shape = tensor.Dimensions;
            if(shape.Length != expectedShape.Length){
                return false;
            }
            for (int i = 0; i < shape.Length; i++) {
                if(shape[i] != expectedShape[i] && expectedShape[i] != -1){
                    return false;
                }
            }
            return true;
        }

        public static string ShapeString<T>(Tensor<T> tensor){
            var shape = tensor.Dimensions;
            return "(" + string.Join(", ", shape.ToArray()) + ")";
        }

        public static Dictionary<string, int> LoadPhonemes(string filePath){
            switch(Path.GetExtension(filePath).ToLower()){
                case ".json":
                    return LoadPhonemesFromJson(filePath);
                default:
                    return LoadPhonemesFromTxt(filePath);
            }
        }

        static Dictionary<string, int> LoadPhonemesFromJson(string filePath){
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return Json.Deserialize<Dictionary<string, int>>(json);
        }

        static Dictionary<string, int> LoadPhonemesFromTxt(string filePath){
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var result = new Dictionary<string, int>();
            for (int i = 0; i < lines.Length; i++) {
                result[lines[i]] = i;
            }
            return result;
        }

        public static Dictionary<string, int> LoadLanguageIds(string filePath){
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return Json.Deserialize<Dictionary<string, int>>(json);
        }

        public static string PhonemeLanguage(string phoneme){
            if(phoneme.Contains("/")){
                return phoneme.Split("/")[0];
            }
            return "";
        }
    }
}
