using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using K4os.Hash.xxHash;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// One frame-aligned condition channel the acoustic model consumes, used to
    /// detect which frames an edit actually changed.
    /// </summary>
    internal sealed class AcousticCondition {
        public readonly string Name;
        public readonly float[] Values;
        /// <summary>1 for a per-frame scalar, or the width of a per-frame vector (spk_embed).</summary>
        public readonly int ValuesPerFrame;

        public AcousticCondition(string name, float[] values, int valuesPerFrame = 1) {
            Name = name;
            Values = values ?? Array.Empty<float>();
            ValuesPerFrame = Math.Max(1, valuesPerFrame);
        }

        public AcousticCondition Clone() =>
            new AcousticCondition(Name, (float[])Values.Clone(), ValuesPerFrame);

        public bool SameLayout(AcousticCondition? other) =>
            other != null &&
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
            ValuesPerFrame == other.ValuesPerFrame &&
            Values.Length == other.Values.Length;
    }

    /// <summary>
    /// The last acoustic result of a phrase: the exact per-frame conditions it was
    /// rendered with, the raw mel (model output domain) and the phrase identity.
    /// Kept so the next render can pass the mel back as <c>ref_mel</c> and only
    /// regenerate the frames whose conditions changed.
    /// </summary>
    internal sealed class AcousticInpaintState {
        public readonly ulong Key;
        public readonly ulong AcousticHash;
        public readonly int TotalFrames;
        public readonly int Bins;
        public readonly float FrameMs;
        /// <summary>Model output in its own domain, TotalFrames * Bins, before any mel base conversion.</summary>
        public readonly float[] Mel;
        public readonly AcousticCondition[] Conditions;
        /// <summary>Structural identity of the phrase; curves are deliberately excluded.</summary>
        public readonly ulong PreEffectHash;
        /// <summary>
        /// Phrase hash whose audio this mel currently backs. Updated when the mel is
        /// reused for a new phrase hash (an edit that did not touch the acoustic
        /// conditions), so a wav cache hit can tell whether this state still matches.
        /// </summary>
        public ulong PhraseHash;

        public AcousticInpaintState(
                ulong key, ulong acousticHash, int totalFrames, int bins, float frameMs,
                float[] mel, IReadOnlyList<AcousticCondition> conditions,
                ulong preEffectHash, ulong phraseHash) {
            Key = key;
            AcousticHash = acousticHash;
            TotalFrames = totalFrames;
            Bins = bins;
            FrameMs = frameMs;
            Mel = (float[])mel.Clone();
            Conditions = conditions.Select(condition => condition.Clone()).ToArray();
            PreEffectHash = preEffectHash;
            PhraseHash = phraseHash;
        }

        public bool IsCompatible(
                int totalFrames, int bins, float frameMs,
                IReadOnlyList<AcousticCondition> current, ulong acousticHash) {
            if (AcousticHash != acousticHash ||
                TotalFrames != totalFrames ||
                Bins != bins ||
                Math.Abs(FrameMs - frameMs) > 1e-3f ||
                Mel.Length != totalFrames * bins ||
                Conditions.Length != current.Count) {
                return false;
            }
            for (int i = 0; i < Conditions.Length; i++) {
                if (!Conditions[i].SameLayout(current[i])) {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Least-recently-used store of <see cref="AcousticInpaintState"/>, per singer.</summary>
    internal sealed class AcousticInpaintStateCache {
        readonly int capacity;
        readonly object gate = new object();
        readonly Dictionary<ulong, LinkedListNode<(ulong key, AcousticInpaintState state)>> entries = new();
        readonly LinkedList<(ulong key, AcousticInpaintState state)> recency = new();

        public AcousticInpaintStateCache(int capacity) {
            if (capacity <= 0) {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            this.capacity = capacity;
        }

        public int Count {
            get {
                lock (gate) {
                    return entries.Count;
                }
            }
        }

        public AcousticInpaintState? TryGet(ulong key) {
            lock (gate) {
                if (!entries.TryGetValue(key, out var node)) {
                    return null;
                }
                recency.Remove(node);
                recency.AddFirst(node);
                return node.Value.state;
            }
        }

        public void Set(AcousticInpaintState state) {
            lock (gate) {
                if (entries.TryGetValue(state.Key, out var existing)) {
                    existing.Value = (state.Key, state);
                    recency.Remove(existing);
                    recency.AddFirst(existing);
                    return;
                }
                var node = recency.AddFirst((state.Key, state));
                entries.Add(state.Key, node);
                if (entries.Count <= capacity) {
                    return;
                }
                var oldest = recency.Last!;
                recency.RemoveLast();
                entries.Remove(oldest.Value.key);
            }
        }

        public void Remove(ulong key) {
            lock (gate) {
                RemoveLocked(key);
            }
        }

        /// <summary>
        /// Drops the states of a phrase that do not belong to <paramref name="phraseHash"/>.
        /// Called when a render was served from the wav cache: that audio is authoritative
        /// for its hash, and a mel kept for another version of the phrase would silently
        /// put the wrong take under the untouched frames of the next edit.
        /// </summary>
        public void RemoveForPhrase(ulong preEffectHash, ulong phraseHash) {
            lock (gate) {
                var stale = entries.Values
                    .Where(node => node.Value.state.PreEffectHash == preEffectHash
                        && node.Value.state.PhraseHash != phraseHash)
                    .Select(node => node.Value.key)
                    .ToArray();
                foreach (var key in stale) {
                    RemoveLocked(key);
                }
            }
        }

        public void Clear() {
            lock (gate) {
                entries.Clear();
                recency.Clear();
            }
        }

        /// <summary>A snapshot for diagnostics and tests; most recent first.</summary>
        public IReadOnlyList<AcousticInpaintState> Snapshot() {
            lock (gate) {
                return entries.Values.Select(node => node.Value.state).ToArray();
            }
        }

        void RemoveLocked(ulong key) {
            if (!entries.TryGetValue(key, out var node)) {
                return;
            }
            entries.Remove(key);
            recency.Remove(node);
        }
    }

    /// <summary>
    /// Frame-domain math for DiffSinger acoustic inpainting (PR #360): which frames
    /// changed, how far to expand the mask, and the ONNX tensors that carry the kept
    /// mel back into the model. Kept free of ONNX sessions so it can be unit tested.
    /// </summary>
    internal static class DiffSingerAcousticInpaint {
        /// <summary>Below this two floats are the same conditioning.</summary>
        public const float ConditionEpsilon = 1e-4f;
        /// <summary>f0 is in Hz; a thousandth of a hertz is far below one cent at any singable pitch.</summary>
        public const float F0Epsilon = 1e-3f;
        /// <summary>
        /// Frames added on each side of the changed range. The model pins the kept
        /// frames exactly, so the regenerated frames need a little context to meet
        /// them without a seam. One frame is 11.6 ms at 44.1 kHz / hop 512.
        /// </summary>
        public const int MarginFrames = 1;
        /// <summary>Bumped when the state key layout changes.</summary>
        const int KeyVersion = 1;

        public const string RefMelInputName = "ref_mel";
        public const string MaskInputName = "inpaint_mask";

        /// <summary>
        /// Checks the inputs a voicebank that declares <c>use_inpaint: true</c> must
        /// export, and returns the element type of <c>inpaint_mask</c> (bool for current
        /// exports, float for older ones). Throws with an actionable message when the
        /// graph cannot honour the declaration: a broken voicebank must fail loudly
        /// instead of silently rendering without the kept region.
        /// </summary>
        public static Type ValidateModelInputs(
                string acousticFile, IReadOnlyList<string> inputNames,
                Func<string, Type?> elementTypeOf) {
            var missing = new[] { RefMelInputName, MaskInputName }
                .Where(name => !inputNames.Contains(name))
                .ToArray();
            if (missing.Length > 0) {
                throw new InvalidDataException(
                    $"The voicebank declares use_inpaint: true, but \"{acousticFile}\" has no " +
                    $"\"{string.Join("\", \"", missing)}\" input. Re-export the acoustic model with " +
                    "\"--inpaint\", or remove use_inpaint from dsconfig.yaml.");
            }
            var refMelType = elementTypeOf(RefMelInputName);
            if (refMelType != typeof(float)) {
                throw new InvalidDataException(
                    $"The acoustic model's \"{RefMelInputName}\" input must be float32, " +
                    $"but \"{acousticFile}\" declares {refMelType?.Name ?? "an unknown type"}.");
            }
            var maskType = elementTypeOf(MaskInputName);
            if (maskType != typeof(bool) && maskType != typeof(float)) {
                throw new InvalidDataException(
                    $"The acoustic model's \"{MaskInputName}\" input must be bool, " +
                    $"but \"{acousticFile}\" declares {maskType?.Name ?? "an unknown type"}.");
            }
            return maskType;
        }

        /// <summary>
        /// Structural identity of a render: everything that decides the mel except the
        /// curves. Curves are absent on purpose - they are compared frame by frame, so
        /// an edit must not miss the previous state.
        /// </summary>
        public static ulong BuildKey(
                ulong acousticHash, IReadOnlyList<long> tokens, IReadOnlyList<int> durations,
                IReadOnlyList<long>? languages, int totalFrames, int bins, float frameMs,
                double depth, int steps) {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(KeyVersion);
                writer.Write(acousticHash);
                writer.Write(totalFrames);
                writer.Write(bins);
                writer.Write(frameMs);
                writer.Write(depth);
                writer.Write(steps);
                writer.Write(tokens.Count);
                foreach (var token in tokens) {
                    writer.Write(token);
                }
                writer.Write(durations.Count);
                foreach (var duration in durations) {
                    writer.Write(duration);
                }
                writer.Write(languages?.Count ?? -1);
                if (languages != null) {
                    foreach (var language in languages) {
                        writer.Write(language);
                    }
                }
            }
            return XXH64.DigestOf(stream.ToArray());
        }

        /// <summary>
        /// Frames whose acoustic conditioning differs from the previous state. A layout
        /// mismatch means the caller cannot compare the channels, so everything is new.
        /// </summary>
        public static bool[] BuildChangedMask(
                IReadOnlyList<AcousticCondition> previous,
                IReadOnlyList<AcousticCondition> current,
                int totalFrames) {
            var changed = new bool[Math.Max(0, totalFrames)];
            if (totalFrames <= 0) {
                return changed;
            }
            if (previous.Count != current.Count) {
                Array.Fill(changed, true);
                return changed;
            }
            for (int c = 0; c < previous.Count; c++) {
                var before = previous[c];
                var after = current[c];
                if (!before.SameLayout(after)) {
                    Array.Fill(changed, true);
                    return changed;
                }
                float epsilon = string.Equals(before.Name, "f0", StringComparison.Ordinal)
                    ? F0Epsilon
                    : ConditionEpsilon;
                bool[] mask = before.ValuesPerFrame <= 1
                    ? DiffSingerVariancePatch.BuildChangedFrameMask(before.Values, after.Values, epsilon)
                    : DiffSingerVariancePatch.BuildChangedFrameMask(
                        before.Values, after.Values, totalFrames, epsilon);
                for (int frame = 0; frame < totalFrames && frame < mask.Length; frame++) {
                    changed[frame] |= mask[frame];
                }
            }
            return changed;
        }

        /// <summary>Grows every changed frame by <paramref name="margin"/> frames on both sides, clipped to the phrase.</summary>
        public static bool[] Expand(IReadOnlyList<bool> mask, int margin) {
            int frames = mask.Count;
            var result = new bool[frames];
            if (margin <= 0) {
                for (int i = 0; i < frames; i++) {
                    result[i] = mask[i];
                }
                return result;
            }
            for (int i = 0; i < frames; i++) {
                if (!mask[i]) {
                    continue;
                }
                int start = Math.Max(0, i - margin);
                int end = Math.Min(frames - 1, i + margin);
                for (int j = start; j <= end; j++) {
                    result[j] = true;
                }
            }
            return result;
        }

        public static bool AnyChanged(IReadOnlyList<bool> mask) {
            for (int i = 0; i < mask.Count; i++) {
                if (mask[i]) {
                    return true;
                }
            }
            return false;
        }

        public static bool AllChanged(IReadOnlyList<bool> mask) {
            if (mask.Count == 0) {
                return false;
            }
            for (int i = 0; i < mask.Count; i++) {
                if (!mask[i]) {
                    return false;
                }
            }
            return true;
        }

        public static int CountChanged(IReadOnlyList<bool> mask) {
            int count = 0;
            for (int i = 0; i < mask.Count; i++) {
                if (mask[i]) {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// The mel the model may keep frames from. A null state (or one of the wrong
        /// size) becomes zeros: with an all-true mask the values are ignored, so this
        /// is the plain full sampling pass.
        /// </summary>
        public static NamedOnnxValue CreateRefMelInput(
                AcousticInpaintState? state, int totalFrames, int bins) {
            float[] data = state != null && state.Mel.Length == totalFrames * bins
                ? state.Mel
                : new float[totalFrames * bins];
            return NamedOnnxValue.CreateFromTensor(RefMelInputName,
                new DenseTensor<float>(data, new[] { 1, totalFrames, bins }));
        }

        /// <summary>
        /// The mask input. Null means "regenerate everything". The dtype follows the
        /// exported graph: bool is the interface, float is accepted for exports made
        /// before the mask was changed to bool.
        /// </summary>
        public static NamedOnnxValue CreateMaskInput(
                IReadOnlyList<bool>? mask, int totalFrames, bool asBool) {
            bool complete = mask != null && mask.Count == totalFrames;
            if (asBool) {
                var data = new bool[totalFrames];
                for (int i = 0; i < totalFrames; i++) {
                    data[i] = !complete || mask![i];
                }
                return NamedOnnxValue.CreateFromTensor(MaskInputName,
                    new DenseTensor<bool>(data, new[] { 1, totalFrames }));
            } else {
                var data = new float[totalFrames];
                for (int i = 0; i < totalFrames; i++) {
                    data[i] = !complete || mask![i] ? 1f : 0f;
                }
                return NamedOnnxValue.CreateFromTensor(MaskInputName,
                    new DenseTensor<float>(data, new[] { 1, totalFrames }));
            }
        }

        /// <summary>
        /// Verifies that the model kept the frames outside the mask. The graph is
        /// supposed to return <paramref name="refMel"/> there exactly; anything else
        /// means the export does not honour the inputs and the caller should warn.
        /// </summary>
        public static bool VerifyRetainedFrames(
                IReadOnlyList<float> mel, IReadOnlyList<float> refMel, IReadOnlyList<bool> mask,
                int totalFrames, int bins, out float maxDifference) {
            maxDifference = 0f;
            if (mel.Count < totalFrames * bins || refMel.Count < totalFrames * bins) {
                return false;
            }
            for (int frame = 0; frame < totalFrames; frame++) {
                if (frame < mask.Count && mask[frame]) {
                    continue;
                }
                int offset = frame * bins;
                for (int c = 0; c < bins; c++) {
                    float difference = Math.Abs(mel[offset + c] - refMel[offset + c]);
                    if (difference > maxDifference) {
                        maxDifference = difference;
                    }
                }
            }
            return maxDifference <= 1e-6f;
        }
    }
}
