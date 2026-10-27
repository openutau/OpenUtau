using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenUtau.Core.Analysis;

/// <summary>
/// Languages the TIFA aligner supports. Each maps to a language-qualified
/// prefix in the model vocabulary ("zh/e", "ja/a", ...).
/// </summary>
public enum TifaLanguage {
    Zh,
    Ja,
    Yue,
    En,
}

/// <summary>
/// Bundled language resources taken from tifa.cpp releases.
///
/// The aligner's own G2P cannot be driven from OpenUtau (it runs on raw text
/// with its own dictionaries and crashes on some builds), so OpenUtau feeds it
/// an explicit phone list. These tables turn an OpenUtau lyric into that
/// canonical phone list:
/// <list type="bullet">
/// <item>Mandarin: pinyin syllable -&gt; initial/final phones (2-segment).</item>
/// <item>Cantonese: jyutping syllable -&gt; onset/final phones.</item>
/// <item>Japanese: mora romaji -&gt; phones.</item>
/// <item>English: no table; OpenUtau's English phonemizers already emit
/// ARPAbet, which is the aligner's inventory.</item>
/// </list>
/// The phone inventory is used to drop symbols the model would reject before
/// spawning the CLI, so one bad symbol cannot fail the whole alignment.
/// </summary>
public static class TifaPhonemeData {
    const string ResourcePrefix = "OpenUtau.Core.Analysis.Tifa.Resources.";

    static readonly object loadLock = new();
    static readonly Dictionary<TifaLanguage, Dictionary<string, string[]>> syllableTables = new();
    static readonly Dictionary<TifaLanguage, HashSet<string>> phoneInventories = new();

    public static string Code(TifaLanguage language) => language switch {
        TifaLanguage.Zh => "zh",
        TifaLanguage.Ja => "ja",
        TifaLanguage.Yue => "yue",
        _ => "en",
    };

    public static bool TryParseLanguage(string? code, out TifaLanguage language) {
        switch (code?.Trim().ToLowerInvariant()) {
            case "zh":
            case "cmn":
            case "zh-cn":
                language = TifaLanguage.Zh;
                return true;
            case "ja":
            case "jp":
                language = TifaLanguage.Ja;
                return true;
            case "yue":
            case "zh-yue":
            case "cantonese":
                language = TifaLanguage.Yue;
                return true;
            case "en":
            case "eng":
                language = TifaLanguage.En;
                return true;
            default:
                language = default;
                return false;
        }
    }

    /// <summary>Phone inventory of a language, sorted.</summary>
    public static IReadOnlyCollection<string> Phones(TifaLanguage language) {
        lock (loadLock) {
            EnsureLoaded(language);
            return phoneInventories[language];
        }
    }

    public static bool IsKnownPhone(TifaLanguage language, string phone) {
        lock (loadLock) {
            EnsureLoaded(language);
            return phoneInventories[language].Contains(phone);
        }
    }

    /// <summary>
    /// Look up a canonical phone sequence for a syllable or mora. Returns null
    /// when the key is not in the table (callers fall back to per-symbol
    /// mapping or skip the note).
    /// </summary>
    public static string[]? Lookup(TifaLanguage language, string key) {
        lock (loadLock) {
            EnsureLoaded(language);
            return syllableTables[language].TryGetValue(key, out var phones)
                ? (string[])phones.Clone()
                : null;
        }
    }

    static void EnsureLoaded(TifaLanguage language) {
        if (syllableTables.ContainsKey(language)) {
            return;
        }
        syllableTables[language] = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var inventory = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in ReadResource(InventoryResource)) {
            var parts = line.Split('\t');
            if (parts.Length < 2) {
                continue;
            }
            if (parts[0].Trim() == Code(language)) {
                foreach (var phone in parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                    inventory.Add(phone);
                }
            }
        }
        var table = TableResourceName(language);
        if (table != null) {
            foreach (var line in ReadResource(table)) {
                var parts = line.Split('\t');
                if (parts.Length < 2) {
                    continue;
                }
                var key = parts[0].Trim();
                var phones = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (key.Length == 0 || phones.Length == 0) {
                    continue;
                }
                syllableTables[language][key] = phones;
                foreach (var phone in phones) {
                    inventory.Add(phone);
                }
            }
        }
        phoneInventories[language] = inventory;
    }

    const string InventoryResource = "phone-inventory.txt";

    static string? TableResourceName(TifaLanguage language) => language switch {
        TifaLanguage.Zh => "zh-pinyin-lite.txt",
        TifaLanguage.Ja => "ja-mora.txt",
        TifaLanguage.Yue => "yue-jyutping.txt",
        _ => null,
    };

    static IEnumerable<string> ReadResource(string name) {
        var assembly = typeof(TifaPhonemeData).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name);
        if (stream == null) {
            throw new FileNotFoundException(
                $"Missing embedded TIFA resource {name}. Expected {ResourcePrefix}{name}.");
        }
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null) {
            if (line.Length > 0) {
                yield return line;
            }
        }
    }
}
