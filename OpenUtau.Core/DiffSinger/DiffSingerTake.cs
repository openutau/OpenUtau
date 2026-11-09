using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using K4os.Hash.xxHash;
using Serilog;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.DiffSinger {
    /// <summary>
    /// One frame-aligned condition channel fed to the acoustic model. Used to
    /// detect which frames a re-render actually has to re-sample.
    /// </summary>
    internal sealed class DiffSingerTakeCondition {
        public string Name = string.Empty;
        public float[] Values = Array.Empty<float>();
        /// <summary>1 for per-frame scalars, or the speaker embedding width.</summary>
        public int ValuesPerFrame = 1;
    }

    /// <summary>
    /// The last accepted acoustic result of a phrase: the exact conditioning it
    /// was rendered with, its mel and the f0 the vocoder saw. This is what lets a
    /// re-render keep the parts the user did not touch.
    /// </summary>
    internal sealed class DiffSingerTake {
        public ulong Key;
        public int TotalFrames;
        public int Bins;
        public float FrameMs;
        /// <summary>TotalFrames * Bins, already transformed for the vocoder's mel base.</summary>
        public float[] Mel = Array.Empty<float>();
        /// <summary>Vocoder f0 in Hz, TotalFrames long.</summary>
        public float[] F0 = Array.Empty<float>();
        public DiffSingerTakeCondition[] Conditions = Array.Empty<DiffSingerTakeCondition>();
        public ulong AcousticHash;
        public ulong VocoderHash;
        /// <summary>File name of the wav this take's audio was written to, if any.</summary>
        public string? WavFile;
        public long SavedUtcTicks;

        public bool IsCompatible(int totalFrames, int bins, float frameMs,
                IReadOnlyList<DiffSingerTakeCondition> current,
                ulong acousticHash, ulong vocoderHash) {
            if (AcousticHash != acousticHash || VocoderHash != vocoderHash) {
                return false;
            }
            if (TotalFrames != totalFrames || Bins != bins || Math.Abs(FrameMs - frameMs) > 1e-3f) {
                return false;
            }
            if (Mel.Length != totalFrames * bins || F0.Length != totalFrames) {
                return false;
            }
            if (Conditions.Length != current.Count) {
                return false;
            }
            for (int i = 0; i < Conditions.Length; i++) {
                var a = Conditions[i];
                var b = current[i];
                if (a.Name != b.Name || a.ValuesPerFrame != b.ValuesPerFrame || a.Values.Length != b.Values.Length) {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Disk-backed store of <see cref="DiffSingerTake"/>s, keyed by the phrase's
    /// structural identity (tokens and durations), never by its curves or the
    /// session timestamp. That is what makes a take survive edits and reloads.
    /// </summary>
    internal static class DiffSingerTakeStore {
        const string Magic = "DSTAKE2";
        // Bumped when the key layout changes. Old take files are unreachable anyway
        // (their keys differ) and are swept by the age cleanup.
        const int Version = 2;
        const int MemoryCapacity = 8;
        const string FilePrefix = "ds-take-";
        const string FileSuffix = ".take";

        static readonly object gate = new object();
        static readonly Dictionary<ulong, LinkedListNode<(ulong key, DiffSingerTake take)>> memory = new();
        static readonly LinkedList<(ulong key, DiffSingerTake take)> recency = new();
        static bool cleanedUp;

        /// <summary>
        /// Structural identity of a phrase: who sings it, where it starts, with which
        /// sampler, and exactly which phones at which times. The track and absolute
        /// start tick keep two identical phrases (same lyrics and timing, e.g. a
        /// repeated chorus) from sharing one take and deleting each other's audio.
        /// Curves, expression values and the session timestamp are deliberately absent,
        /// so a take survives edits and reloads. Model files are hashed into the take
        /// and checked on load, so a swapped voicebank still invalidates it.
        /// </summary>
        public static ulong ComputeKey(
                RenderPhrase phrase, string singerId, int trackNo, int phraseStartTick,
                float frameMs, int bins, double depth, int steps, bool xsyVariant) {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Version);
            writer.Write(singerId ?? string.Empty);
            writer.Write(trackNo);
            writer.Write(phraseStartTick);
            writer.Write(frameMs);
            writer.Write(bins);
            writer.Write(depth);
            writer.Write(steps);
            writer.Write(xsyVariant);
            writer.Write(phrase.phones.Length);
            // Phones are relative to the phrase; track + start tick pin the phrase.
            foreach (var phone in phrase.phones) {
                writer.Write(phone.phoneme ?? string.Empty);
                writer.Write(phone.positionMs - phrase.positionMs);
                writer.Write(phone.durationMs);
            }
            return XXH64.DigestOf(stream.ToArray());
        }

        /// <summary>
        /// Stable hash of the protected time ranges. It goes into the take-locked wav
        /// name, so painting or erasing a range cannot be served from audio that was
        /// rendered under the previous protection.
        /// </summary>
        public static ulong ComputePreserveHash(IReadOnlyList<(int start, int end)>? ranges,
                int startTick = int.MinValue, int endTick = int.MaxValue) {
            if (ranges == null || ranges.Count == 0) {
                return 0;
            }
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            bool any = false;
            foreach (var (start, end) in ranges.OrderBy(r => r.start).ThenBy(r => r.end)) {
                int clippedStart = Math.Max(start, startTick);
                int clippedEnd = Math.Min(end, endTick);
                if (clippedEnd <= clippedStart) {
                    continue;
                }
                writer.Write(clippedStart);
                writer.Write(clippedEnd);
                any = true;
            }
            return any ? XXH64.DigestOf(stream.ToArray()) : 0;
        }

        public static string GetFilename(ulong key) => $"{FilePrefix}{key:x16}{FileSuffix}";

        static string GetPath(ulong key) => Path.Join(PathManager.Inst.CachePath, GetFilename(key));

        public static DiffSingerTake? TryLoad(ulong key) {
            lock (gate) {
                if (memory.TryGetValue(key, out var node)) {
                    recency.Remove(node);
                    recency.AddFirst(node);
                    return node.Value.take;
                }
            }
            DiffSingerTake? take = LoadFromDisk(key);
            if (take != null) {
                lock (gate) {
                    Remember(take);
                }
            }
            return take;
        }

        public static void Save(DiffSingerTake take) {
            take.SavedUtcTicks = DateTime.UtcNow.Ticks;
            string path = GetPath(take.Key);
            string? previousWav = null;
            lock (gate) {
                if (memory.TryGetValue(take.Key, out var node)) {
                    previousWav = node.Value.take.WavFile;
                }
                try {
                    WriteToDisk(take, path);
                } catch (Exception e) {
                    // The audio already rendered; a take that cannot be persisted only
                    // means the next render starts from scratch.
                    Log.Warning(e, "Failed to persist DiffSinger take");
                    return;
                }
                Remember(take);
            }
            // While the lock owns the render cache, the take points at a new wav and
            // the old one is unreachable. Without the lock, leave it to
            // DeleteCacheFiles: an undo can still ask for the previous phrase hash.
            if (Preferences.Default.DiffSingerTakeLock &&
                !string.IsNullOrEmpty(previousWav) && previousWav != take.WavFile) {
                DeleteWav(previousWav!);
            }
        }

        /// <summary>
        /// Drops the in-memory copy when a cache cleanup removes the backing file, so
        /// <see cref="TryLoad"/> cannot resurrect a take whose audio was cleared.
        /// </summary>
        public static void DeleteByFilename(string filenameOrPath) {
            string name = Path.GetFileNameWithoutExtension(filenameOrPath);
            if (!name.StartsWith(FilePrefix, StringComparison.Ordinal)) {
                return;
            }
            if (ulong.TryParse(name.Substring(FilePrefix.Length), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ulong key)) {
                Delete(key);
            }
        }

        public static void Delete(ulong key) {
            lock (gate) {
                if (memory.TryGetValue(key, out var node)) {
                    memory.Remove(key);
                    recency.Remove(node);
                }
            }
            try {
                string path = GetPath(key);
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception e) {
                Log.Warning(e, "Failed to delete DiffSinger take file");
            }
        }

        /// <summary>
        /// Deletes the take only if the stored instance is still the one that produced
        /// <paramref name="observedWavFile"/>. A newer take (a different wav) is kept.
        /// The identity check and the file deletion share <see cref="Save"/>'s lock, so
        /// a concurrent save cannot slip a fresh take under the deletion.
        /// </summary>
        public static void DeleteIfMatches(ulong key, string? observedWavFile) {
            lock (gate) {
                DiffSingerTake? take;
                LinkedListNode<(ulong key, DiffSingerTake take)>? node = null;
                if (memory.TryGetValue(key, out node)) {
                    take = node.Value.take;
                } else {
                    try {
                        take = LoadFromDisk(key);
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to read DiffSinger take for deletion");
                        return;
                    }
                    if (take == null) {
                        return;
                    }
                }
                if (take.WavFile != observedWavFile) {
                    return;
                }
                if (node != null) {
                    memory.Remove(key);
                    recency.Remove(node);
                }
                try {
                    string path = GetPath(key);
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                } catch (Exception e) {
                    Log.Warning(e, "Failed to delete DiffSinger take file");
                }
            }
        }

        static void Remember(DiffSingerTake take) {
            if (memory.TryGetValue(take.Key, out var existing)) {
                existing.Value = (take.Key, take);
                recency.Remove(existing);
                recency.AddFirst(existing);
                return;
            }
            var node = recency.AddFirst((take.Key, take));
            memory.Add(take.Key, node);
            if (memory.Count <= MemoryCapacity) {
                return;
            }
            var oldest = recency.Last!;
            recency.RemoveLast();
            memory.Remove(oldest.Value.key);
        }

        static void DeleteWav(string wavFile) {
            try {
                string path = Path.Join(PathManager.Inst.CachePath, wavFile);
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception e) {
                Log.Debug(e, "Failed to delete superseded DiffSinger render");
            }
        }

        static void CleanupOnce() {
            if (cleanedUp) {
                return;
            }
            cleanedUp = true;
            try {
                if (!Directory.Exists(PathManager.Inst.CachePath)) {
                    return;
                }
                var cutoff = DateTime.UtcNow.AddDays(-30);
                foreach (var file in Directory.EnumerateFiles(PathManager.Inst.CachePath, $"{FilePrefix}*{FileSuffix}")) {
                    if (File.GetLastWriteTimeUtc(file) < cutoff) {
                        File.Delete(file);
                    }
                }
            } catch (Exception e) {
                Log.Debug(e, "Failed to clean up old DiffSinger takes");
            }
        }

        static DiffSingerTake? LoadFromDisk(ulong key) {
            CleanupOnce();
            string path = GetPath(key);
            if (!File.Exists(path)) {
                return null;
            }
            try {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);
                if (reader.ReadString() != Magic) {
                    throw new InvalidDataException("Unexpected take header");
                }
                int version = reader.ReadInt32();
                if (version != Version) {
                    throw new InvalidDataException($"Unsupported take version {version}");
                }
                var take = new DiffSingerTake {
                    Key = key,
                    TotalFrames = reader.ReadInt32(),
                    Bins = reader.ReadInt32(),
                    FrameMs = reader.ReadSingle(),
                };
                take.Mel = ReadFloats(reader);
                take.F0 = ReadFloats(reader);
                int conditionCount = reader.ReadInt32();
                var conditions = new DiffSingerTakeCondition[conditionCount];
                for (int i = 0; i < conditionCount; i++) {
                    conditions[i] = new DiffSingerTakeCondition {
                        Name = reader.ReadString(),
                        ValuesPerFrame = reader.ReadInt32(),
                        Values = ReadFloats(reader),
                    };
                }
                take.Conditions = conditions;
                take.AcousticHash = reader.ReadUInt64();
                take.VocoderHash = reader.ReadUInt64();
                take.WavFile = reader.ReadBoolean() ? reader.ReadString() : null;
                take.SavedUtcTicks = reader.ReadInt64();
                if (take.Mel.Length != take.TotalFrames * take.Bins || take.F0.Length != take.TotalFrames) {
                    throw new InvalidDataException("Take file is inconsistent");
                }
                return take;
            } catch (Exception e) {
                Log.Warning(e, "Failed to read DiffSinger take, discarding it");
                try {
                    File.Delete(path);
                } catch { }
                return null;
            }
        }

        static void WriteToDisk(DiffSingerTake take, string path) {
            Directory.CreateDirectory(PathManager.Inst.CachePath);
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                using var writer = new BinaryWriter(stream);
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(take.TotalFrames);
                writer.Write(take.Bins);
                writer.Write(take.FrameMs);
                WriteFloats(writer, take.Mel);
                WriteFloats(writer, take.F0);
                writer.Write(take.Conditions.Length);
                foreach (var condition in take.Conditions) {
                    writer.Write(condition.Name);
                    writer.Write(condition.ValuesPerFrame);
                    WriteFloats(writer, condition.Values);
                }
                writer.Write(take.AcousticHash);
                writer.Write(take.VocoderHash);
                writer.Write(take.WavFile != null);
                if (take.WavFile != null) {
                    writer.Write(take.WavFile);
                }
                writer.Write(take.SavedUtcTicks);
            }
            File.Move(temp, path, overwrite: true);
        }

        static void WriteFloats(BinaryWriter writer, float[] values) {
            writer.Write(values.Length);
            foreach (float value in values) {
                writer.Write(value);
            }
        }

        static float[] ReadFloats(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length < 0 || length > 1 << 28) {
                throw new InvalidDataException("Invalid take array length");
            }
            var values = new float[length];
            for (int i = 0; i < length; i++) {
                values[i] = reader.ReadSingle();
            }
            return values;
        }
    }
}
