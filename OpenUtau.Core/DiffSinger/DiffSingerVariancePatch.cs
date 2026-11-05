using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    internal sealed class VariancePatchState {
        public readonly float[] pitch;
        public readonly float[]? speakerEmbed;
        public readonly VarianceResult result;

        public VariancePatchState(float[] pitch, float[]? speakerEmbed, VarianceResult result) {
            this.pitch = pitch.ToArray();
            this.speakerEmbed = speakerEmbed?.ToArray();
            this.result = DiffSingerVariancePatch.CloneResult(result);
        }
    }

    internal sealed class VariancePatchStateCache {
        readonly int capacity;
        readonly Dictionary<ulong, LinkedListNode<(ulong key, VariancePatchState state)>> entries = new();
        readonly LinkedList<(ulong key, VariancePatchState state)> recency = new();

        internal VariancePatchStateCache(int capacity) {
            if (capacity <= 0) {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            this.capacity = capacity;
        }

        internal int Count => entries.Count;

        internal bool TryGetValue(ulong key, out VariancePatchState state) {
            if (!entries.TryGetValue(key, out var node)) {
                state = null!;
                return false;
            }
            recency.Remove(node);
            recency.AddFirst(node);
            state = node.Value.state;
            return true;
        }

        internal void Set(ulong key, VariancePatchState state) {
            if (entries.TryGetValue(key, out var existing)) {
                existing.Value = (key, state);
                recency.Remove(existing);
                recency.AddFirst(existing);
                return;
            }
            var node = recency.AddFirst((key, state));
            entries.Add(key, node);
            if (entries.Count <= capacity) {
                return;
            }
            var oldest = recency.Last!;
            recency.RemoveLast();
            entries.Remove(oldest.Value.key);
        }
    }

    /// <summary>
    /// Persists <see cref="VariancePatchState"/> across sessions. Without it a
    /// reloaded project has no previous variance prediction, so the first pitch
    /// edit re-predicts the whole phrase and the take lock has nothing local to
    /// work with.
    /// </summary>
    internal static class DiffSingerVarianceStateStore {
        const string Magic = "DSVSTATE";
        const int Version = 1;
        const string Prefix = "ds-vstate-";
        const string Suffix = ".state";

        static readonly object gate = new object();

        public static string GetFilename(ulong key) => $"{Prefix}{key:x16}{Suffix}";

        static string GetPath(ulong key) =>
            Path.Join(PathManager.Inst.CachePath, GetFilename(key));

        public static bool Exists(ulong key) => File.Exists(GetPath(key));

        public static bool TryLoad(ulong key, out VariancePatchState state) {
            state = null!;
            lock (gate) {
                string path = GetPath(key);
                if (!File.Exists(path)) {
                    return false;
                }
                try {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = new BinaryReader(stream);
                    if (reader.ReadString() != Magic || reader.ReadInt32() != Version) {
                        throw new InvalidDataException("Unexpected variance state header");
                    }
                    var pitch = ReadFloats(reader);
                    bool hasSpeaker = reader.ReadBoolean();
                    var speaker = hasSpeaker ? ReadFloats(reader) : null;
                    var result = new VarianceResult {
                        frameMs = reader.ReadSingle(),
                        headFrames = reader.ReadInt32(),
                        tailFrames = reader.ReadInt32(),
                        totalFrames = reader.ReadInt32(),
                        energy = ReadOptionalFloats(reader),
                        breathiness = ReadOptionalFloats(reader),
                        voicing = ReadOptionalFloats(reader),
                        tension = ReadOptionalFloats(reader),
                    };
                    state = new VariancePatchState(pitch, speaker, result);
                    return true;
                } catch (Exception e) {
                    Log.Warning(e, "Failed to read DiffSinger variance state, discarding it");
                    try {
                        File.Delete(path);
                    } catch { }
                    return false;
                }
            }
        }

        public static void Save(ulong key, VariancePatchState state) {
            lock (gate) {
                string path = GetPath(key);
                string temp = path + ".tmp";
                try {
                    Directory.CreateDirectory(PathManager.Inst.CachePath);
                    using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        using var writer = new BinaryWriter(stream);
                        writer.Write(Magic);
                        writer.Write(Version);
                        WriteFloats(writer, state.pitch);
                        writer.Write(state.speakerEmbed != null);
                        if (state.speakerEmbed != null) {
                            WriteFloats(writer, state.speakerEmbed);
                        }
                        var result = state.result;
                        writer.Write(result.frameMs);
                        writer.Write(result.headFrames);
                        writer.Write(result.tailFrames);
                        writer.Write(result.totalFrames);
                        WriteOptionalFloats(writer, result.energy);
                        WriteOptionalFloats(writer, result.breathiness);
                        WriteOptionalFloats(writer, result.voicing);
                        WriteOptionalFloats(writer, result.tension);
                    }
                    File.Move(temp, path, overwrite: true);
                } catch (Exception e) {
                    Log.Warning(e, "Failed to write DiffSinger variance state");
                    try {
                        File.Delete(temp);
                    } catch { }
                }
            }
        }

        static void WriteFloats(BinaryWriter writer, float[] values) {
            writer.Write(values.Length);
            foreach (float value in values) {
                writer.Write(value);
            }
        }

        static float[] ReadFloats(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length < 0 || length > 1 << 26) {
                throw new InvalidDataException("Invalid variance state array length");
            }
            var values = new float[length];
            for (int i = 0; i < length; i++) {
                values[i] = reader.ReadSingle();
            }
            return values;
        }

        static void WriteOptionalFloats(BinaryWriter writer, float[]? values) {
            writer.Write(values != null);
            if (values != null) {
                WriteFloats(writer, values);
            }
        }

        static float[]? ReadOptionalFloats(BinaryReader reader) {
            return reader.ReadBoolean() ? ReadFloats(reader) : null;
        }
    }

    internal static class DiffSingerVariancePatch {
        public static ulong BuildStateKey(ulong baseHash, int phrasePosition, int phraseEnd) {
            unchecked {
                ulong hash = baseHash;
                hash = (hash ^ (uint)phrasePosition) * 1099511628211UL;
                hash = (hash ^ (uint)phraseEnd) * 1099511628211UL;
                return hash;
            }
        }

        internal static bool[] BuildChangedFrameMask(
            IReadOnlyList<float> previous,
            IReadOnlyList<float> current,
            float epsilon) {
            int length = Math.Max(previous.Count, current.Count);
            var mask = new bool[length];
            for (int i = 0; i < length; i++) {
                mask[i] = i >= previous.Count || i >= current.Count ||
                    Math.Abs(previous[i] - current[i]) > epsilon;
            }
            return mask;
        }

        internal static bool[] BuildChangedFrameMask(
            IReadOnlyList<float> previous,
            IReadOnlyList<float> current,
            int frameCount,
            float epsilon) {
            if (frameCount <= 0) {
                return Array.Empty<bool>();
            }
            if (previous.Count != current.Count || previous.Count % frameCount != 0) {
                return Enumerable.Repeat(true, frameCount).ToArray();
            }
            int valuesPerFrame = previous.Count / frameCount;
            var mask = new bool[frameCount];
            for (int frame = 0; frame < frameCount; frame++) {
                int offset = frame * valuesPerFrame;
                for (int i = 0; i < valuesPerFrame; i++) {
                    if (Math.Abs(previous[offset + i] - current[offset + i]) > epsilon) {
                        mask[frame] = true;
                        break;
                    }
                }
            }
            return mask;
        }

        internal static bool[] ExpandToChannels(
            IReadOnlyList<bool> frameMask,
            int channelCount) {
            if (channelCount < 0) {
                throw new ArgumentOutOfRangeException(nameof(channelCount));
            }
            var mask = new bool[frameMask.Count * channelCount];
            for (int frame = 0; frame < frameMask.Count; frame++) {
                if (!frameMask[frame]) continue;
                for (int channel = 0; channel < channelCount; channel++) {
                    mask[frame * channelCount + channel] = true;
                }
            }
            return mask;
        }

        internal static VarianceResult HardCompose(
            VarianceResult previous,
            VarianceResult predicted,
            IReadOnlyList<bool> retakeMask,
            int channelCount) {
            if (!IsCompatible(previous, predicted) ||
                retakeMask.Count != previous.totalFrames * channelCount) {
                return CloneResult(predicted);
            }
            int channel = 0;
            var energy = ComposeEnabledChannel(previous.energy, predicted.energy, retakeMask, previous.totalFrames, ref channel, channelCount);
            var breathiness = ComposeEnabledChannel(previous.breathiness, predicted.breathiness, retakeMask, previous.totalFrames, ref channel, channelCount);
            var voicing = ComposeEnabledChannel(previous.voicing, predicted.voicing, retakeMask, previous.totalFrames, ref channel, channelCount);
            var tension = ComposeEnabledChannel(previous.tension, predicted.tension, retakeMask, previous.totalFrames, ref channel, channelCount);
            return new VarianceResult {
                energy = energy,
                breathiness = breathiness,
                voicing = voicing,
                tension = tension,
                frameMs = predicted.frameMs,
                headFrames = predicted.headFrames,
                tailFrames = predicted.tailFrames,
                totalFrames = predicted.totalFrames,
            };
        }

        static float[]? ComposeEnabledChannel(
            float[]? previous,
            float[]? predicted,
            IReadOnlyList<bool> mask,
            int frameCount,
            ref int channel,
            int channelCount) {
            if (previous == null && predicted == null) {
                return null;
            }
            int currentChannel = channel++;
            return ComposeChannel(previous, predicted, mask, frameCount, currentChannel, channelCount);
        }

        static float[]? ComposeChannel(
            float[]? previous,
            float[]? predicted,
            IReadOnlyList<bool> mask,
            int frameCount,
            int channel,
            int channelCount) {
            if (previous == null || predicted == null) {
                return predicted?.ToArray();
            }
            if (previous.Length != frameCount || predicted.Length != frameCount) {
                return predicted.ToArray();
            }
            var result = previous.ToArray();
            for (int frame = 0; frame < frameCount; frame++) {
                if (mask[frame * channelCount + channel]) {
                    result[frame] = predicted[frame];
                }
            }
            return result;
        }

        internal static bool IsMetadataCompatible(VarianceResult previous, VarianceResult current) {
            return previous.totalFrames == current.totalFrames &&
                previous.headFrames == current.headFrames &&
                previous.tailFrames == current.tailFrames &&
                Math.Abs(previous.frameMs - current.frameMs) < 1e-4f;
        }

        internal static bool IsChannelLayoutCompatible(
            VarianceResult result,
            int totalFrames,
            bool predictEnergy,
            bool predictBreathiness,
            bool predictVoicing,
            bool predictTension) {
            return ChannelMatches(result.energy, predictEnergy, totalFrames) &&
                ChannelMatches(result.breathiness, predictBreathiness, totalFrames) &&
                ChannelMatches(result.voicing, predictVoicing, totalFrames) &&
                ChannelMatches(result.tension, predictTension, totalFrames);
        }

        internal static bool IsCompatible(VarianceResult previous, VarianceResult current) {
            return IsMetadataCompatible(previous, current) &&
                SameLength(previous.energy, current.energy) &&
                SameLength(previous.breathiness, current.breathiness) &&
                SameLength(previous.voicing, current.voicing) &&
                SameLength(previous.tension, current.tension);
        }

        static bool ChannelMatches(float[]? values, bool enabled, int totalFrames) {
            return enabled ? values?.Length == totalFrames : values == null;
        }

        static bool SameLength(float[]? a, float[]? b) {
            return (a == null) == (b == null) && (a == null || a.Length == b!.Length);
        }

        internal static VarianceResult CloneResult(VarianceResult result) {
            return new VarianceResult {
                energy = result.energy?.ToArray(),
                breathiness = result.breathiness?.ToArray(),
                voicing = result.voicing?.ToArray(),
                tension = result.tension?.ToArray(),
                frameMs = result.frameMs,
                headFrames = result.headFrames,
                tailFrames = result.tailFrames,
                totalFrames = result.totalFrames,
            };
        }
    }
}
