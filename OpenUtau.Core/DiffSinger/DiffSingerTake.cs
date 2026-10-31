using System;
using System.Collections.Generic;
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
        const int Version = 1;
        const int MemoryCapacity = 8;
        const string FilePrefix = "ds-take-";
        const string FileSuffix = ".take";

        static readonly object gate = new object();
        static readonly Dictionary<ulong, LinkedListNode<(ulong key, DiffSingerTake take)>> memory = new();
        static readonly LinkedList<(ulong key, DiffSingerTake take)> recency = new();
        static bool cleanedUp;

        /// <summary>
        /// Structural identity of a phrase: who sings it, with which sampler, and
        /// exactly which phones at which times. Curves, expression values and the
        /// session timestamp are deliberately absent, so a take survives edits and
        /// reloads. Model files are hashed into the take and checked on load, so a
        /// swapped voicebank still invalidates it.
        /// </summary>
        public static ulong ComputeKey(
                RenderPhrase phrase, string singerId,
                float frameMs, int bins, double depth, int steps, bool xsyVariant) {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Version);
            writer.Write(singerId ?? string.Empty);
            writer.Write(frameMs);
            writer.Write(bins);
            writer.Write(depth);
            writer.Write(steps);
            writer.Write(xsyVariant);
            writer.Write(phrase.phones.Length);
            // Relative to the phrase so moving a part does not throw the take away.
            foreach (var phone in phrase.phones) {
                writer.Write(phone.phoneme ?? string.Empty);
                writer.Write(phone.positionMs - phrase.positionMs);
                writer.Write(phone.durationMs);
            }
            return XXH64.DigestOf(stream.ToArray());
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
            // The take now points at a new wav; the old one is unreachable.
            if (!string.IsNullOrEmpty(previousWav) && previousWav != take.WavFile) {
                DeleteWav(previousWav!);
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
