using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OpenUtau.Core.Analysis;

/// <summary>One interval of a TextGrid IntervalTier.</summary>
public struct TifaInterval {
    public double Start;
    public double End;
    public string Text;
}

/// <summary>
/// Minimal parser for the Praat long TextGrid written by tifa_ggml_cli.
/// TIFA writes three IntervalTiers in a fixed order (texts, words, phones);
/// only the phones tier is needed here. Skipped tokens are preserved as
/// intervals with an empty text when the CLI runs with
/// <c>--skip-handling preserve</c>, which keeps interval order aligned with the
/// phone list passed in.
/// </summary>
public static class TifaTextGrid {
    /// <summary>Default label of the intervals the CLI inserts for stretches
    /// no phone covers (<c>--fill-gaps</c>). No OpenUtau phonemizer produces
    /// this symbol, so it identifies filler unambiguously.</summary>
    public const string GapLabel = "SP";

    /// <summary>
    /// tifa.cpp v0.1.5 fills the holes in every tier with an interval labelled
    /// by <c>--fill-gaps</c>, so the phones tier holds more intervals than the
    /// phone list. Drop those fillers when that makes the counts line up;
    /// otherwise return the input unchanged (older CLIs emit the phones only).
    /// </summary>
    public static List<TifaInterval> DropGapIntervals(List<TifaInterval> spans, int phoneCount) {
        if (spans.Count == phoneCount) {
            return spans;
        }
        var filtered = new List<TifaInterval>(spans.Count);
        foreach (var span in spans) {
            if (string.Equals(span.Text?.Trim(), GapLabel, StringComparison.Ordinal)) {
                continue;
            }
            filtered.Add(span);
        }
        return filtered.Count == phoneCount ? filtered : spans;
    }

    public static double ParseXmax(string path) {
        foreach (var line in File.ReadLines(path)) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("xmax =", StringComparison.Ordinal)) {
                if (double.TryParse(trimmed.Substring(6).Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double xmax)) {
                    return xmax;
                }
            }
        }
        return 0;
    }

    /// <summary>Read the intervals of the tier named <paramref name="tierName"/>.</summary>
    public static List<TifaInterval> ParseTier(string path, string tierName = "phones") {
        var intervals = new List<TifaInterval>();
        using var reader = new StreamReader(path);
        bool inTargetTier = false;
        bool inInterval = false;
        double xmin = 0, xmax = 0;
        string text = string.Empty;
        string? line;
        while ((line = reader.ReadLine()) != null) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("item [", StringComparison.Ordinal)) {
                // Starting a new tier: flush any pending interval of the old one.
                Flush(intervals, ref inInterval, ref xmin, ref xmax, ref text, false);
                inTargetTier = false;
                continue;
            }
            if (trimmed.StartsWith("name =", StringComparison.Ordinal)) {
                var value = Unquote(trimmed.Substring(6).Trim());
                inTargetTier = value == tierName;
                continue;
            }
            if (!inTargetTier) {
                continue;
            }
            if (trimmed.StartsWith("intervals [", StringComparison.Ordinal)) {
                Flush(intervals, ref inInterval, ref xmin, ref xmax, ref text, true);
                inInterval = true;
                continue;
            }
            if (!inInterval) {
                continue;
            }
            if (trimmed.StartsWith("xmin =", StringComparison.Ordinal)) {
                ParseDouble(trimmed.Substring(6), out xmin);
            } else if (trimmed.StartsWith("xmax =", StringComparison.Ordinal)) {
                ParseDouble(trimmed.Substring(6), out xmax);
            } else if (trimmed.StartsWith("text =", StringComparison.Ordinal)) {
                text = Unquote(trimmed.Substring(6).Trim());
            }
        }
        Flush(intervals, ref inInterval, ref xmin, ref xmax, ref text, true);
        return intervals;
    }

    static void Flush(List<TifaInterval> intervals, ref bool inInterval,
        ref double xmin, ref double xmax, ref string text, bool keep) {
        if (inInterval && keep) {
            intervals.Add(new TifaInterval { Start = xmin, End = xmax, Text = text });
        }
        inInterval = false;
        xmin = 0;
        xmax = 0;
        text = string.Empty;
    }

    static bool ParseDouble(string raw, out double value) {
        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    static string Unquote(string raw) {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"') {
            return raw.Substring(1, raw.Length - 2);
        }
        return raw;
    }
}
