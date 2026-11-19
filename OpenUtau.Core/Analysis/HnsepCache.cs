using System;
using System.IO;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using Serilog;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// The harmonic part of a whole source file, saved next to it the way hifisampler does: a
/// torch.save of the [1, 1, samples] tensor as "&lt;name without extension&gt;_hnsep", so caches
/// written by hifisampler and by OpenUtau are interchangeable. Like .frq files, it is kept
/// with the voicebank; a cache of another length (an edited source) is rebuilt.
/// </summary>
public static class HnsepCache {
    public static string PathOf(string sourceFile) {
        return Path.Combine(Path.GetDirectoryName(sourceFile) ?? string.Empty,
            Path.GetFileNameWithoutExtension(sourceFile) + "_hnsep");
    }

    /// <param name="samples">The whole source file at the separator's sample rate.</param>
    /// <param name="separate">Separation for when there is no usable cache.</param>
    public static float[] Harmonic(string sourceFile, float[] samples, Func<float[], float[]> separate) {
        string path = PathOf(sourceFile);
        lock (Renderers.GetCacheLock(path)) {
            var cached = Load(path, samples.Length);
            if (cached != null) {
                return cached;
            }
            var harmonic = separate(samples);
            Save(path, harmonic);
            return harmonic;
        }
    }

    static float[]? Load(string path, int length) {
        if (!File.Exists(path)) {
            return null;
        }
        try {
            var (data, _) = TorchFile.ReadFloatTensor(path);
            if (data.Length == length) {
                return data;
            }
            Log.Information($"Rebuilding {path}: {data.Length} samples cached, {length} in the source.");
        } catch (Exception e) {
            Log.Warning(e, $"Rebuilding unreadable {path}.");
        }
        return null;
    }

    static void Save(string path, float[] harmonic) {
        string temp = path + ".tmp";
        try {
            TorchFile.WriteFloatTensor(temp, harmonic, new[] { 1, 1, harmonic.Length });
            File.Move(temp, path, overwrite: true);
        } catch (Exception e) {
            // A read-only voicebank still renders, without the cache.
            Log.Warning(e, $"Failed to save {path}.");
            try {
                File.Delete(temp);
            } catch { }
        }
    }
}
