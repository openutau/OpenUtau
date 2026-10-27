using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Analysis;

/// <summary>One phoneme of a part, snapshotted for background alignment.</summary>
public class TifaPhonemeSnapshot {
    public UNote Note = null!;
    public int Index;
    public string Phoneme = string.Empty;
    /// <summary>Part-relative tick position including any existing override.</summary>
    public int PositionTick;
    /// <summary>Audible onset in absolute project ms (position - preutter for
    /// classic singers, position for models without an oto).</summary>
    public double StartMs;
    /// <summary>End in absolute project ms.</summary>
    public double EndMs;
    public double Preutter;
}

/// <summary>A note and the phonemes it produced, in phoneme order.</summary>
public class TifaNoteSnapshot {
    public UNote Note = null!;
    public string Lyric = string.Empty;
    public int ExtendedEndTick;
    public List<TifaPhonemeSnapshot> Phonemes = new();
}

/// <summary>
/// The phone list handed to the aligner, produced from the part's lyrics.
/// Phones are stored bare; the language prefix is added when writing the CLI
/// input.
/// </summary>
public class TifaSequence {
    public List<TifaNoteSnapshot> Notes = new();
    /// <summary>Index into <see cref="Notes"/> for each phone, or -1.</summary>
    public List<int> PhoneNote = new();
    public List<string> Phones = new();
    /// <summary>Lyrics that could not be turned into phones.</summary>
    public List<string> Unresolved = new();
    /// <summary>Total number of phonemes carried by the resolved notes.</summary>
    public int PhonemeCount => Notes.Sum(n => n.Phonemes.Count);
}

/// <summary>A planned phoneme move: new part-relative tick position.</summary>
public class TifaPhonemeMove {
    public UNote Note = null!;
    public int Index;
    public int OldPositionTick;
    public int NewPositionTick;
    public bool Clamped;
    public int Offset => NewPositionTick - OldPositionTick;
}

/// <summary>
/// Turns an OpenUtau part into an aligner phone sequence and maps the
/// aligner's phone spans back onto the part's phonemes.
///
/// Mapping rules:
/// <list type="bullet">
/// <item>One note is one syllable/mora in Chinese, Cantonese and Japanese, so
/// its phone list comes from that syllable's dictionary entry, not from the
/// bank's own split. This makes a 3-segment bank and the aligner's 2-segment
/// table meet in the middle.</item>
/// <item>The aligner returns one span per phone; per note the spans are
/// collapsed into a single window.</item>
/// <item>The note's phonemes are remapped onto that window proportionally to
/// their original timing, so the bank's internal duration allocation
/// (compound vowels, VC transitions) is preserved and only the window
/// boundaries come from the recording.</item>
/// </list>
///
/// Classic singers are positioned by their audible onset (position -
/// preutter) so the rendered consonant lands where the recording has it;
/// model singers have no oto, so their position is already the audible
/// timing.
/// </summary>
public static class TifaPhonemeAligner {
    public static bool IsSyllableLanguage(TifaLanguage language) =>
        language is TifaLanguage.Zh or TifaLanguage.Yue;

    static readonly string[] NonPhoneticLyrics = {
        "R", "r", "-", "+", "+~", "+*", "SP", "AP", "sil", "pau", "br", "",
    };

    /// <summary>
    /// Snapshot the part's notes and phonemes. Must be called on a thread that
    /// can read the project (the UI thread); the result is safe to use from a
    /// background thread.
    /// </summary>
    public static List<TifaNoteSnapshot> Snapshot(UVoicePart part, UProject project, bool audibleOnset) {
        var axis = project.timeAxis;
        var byNote = new Dictionary<UNote, List<UPhoneme>>();
        foreach (var phoneme in part.phonemes) {
            var note = phoneme.Parent;
            if (note == null || phoneme.Error) {
                continue;
            }
            if (!byNote.TryGetValue(note, out var list)) {
                list = new List<UPhoneme>();
                byNote[note] = list;
            }
            list.Add(phoneme);
        }

        var snapshots = new List<TifaNoteSnapshot>();
        foreach (var note in part.notes) {
            if (note.Extends != null || note.Error || note.OverlapError) {
                continue;
            }
            if (!byNote.TryGetValue(note, out var phonemes)) {
                continue;
            }
            var ordered = phonemes.OrderBy(p => p.position).ToList();
            if (ordered.Count == 0) {
                continue;
            }
            var snapshot = new TifaNoteSnapshot {
                Note = note,
                Lyric = note.lyric ?? string.Empty,
                // ExtendedEnd wins when the note is extended by "+" notes;
                // otherwise the note's own end is the phoneme boundary.
                ExtendedEndTick = Math.Max(note.End, note.ExtendedEnd),
            };
            for (int i = 0; i < ordered.Count; ++i) {
                var phoneme = ordered[i];
                // Mirror UPhoneme.ValidateDuration: a phoneme runs until the
                // next phoneme or the end of its (possibly extended) note.
                int endTick = i + 1 < ordered.Count ? ordered[i + 1].position : snapshot.ExtendedEndTick;
                double startMs = axis.TickPosToMsPos(part.position + phoneme.position);
                if (audibleOnset) {
                    startMs -= phoneme.preutter;
                }
                snapshot.Phonemes.Add(new TifaPhonemeSnapshot {
                    Note = note,
                    Index = phoneme.index,
                    Phoneme = phoneme.phoneme ?? string.Empty,
                    PositionTick = phoneme.position,
                    StartMs = startMs,
                    EndMs = axis.TickPosToMsPos(part.position + endTick),
                    Preutter = phoneme.preutter,
                });
            }
            snapshots.Add(snapshot);
        }
        return snapshots;
    }

    /// <summary>
    /// Build the phone sequence from the snapped notes. Chinese and Cantonese
    /// songs resolve each note's lyric through the aligner's syllable table;
    /// Japanese and English read the phonemes the singer already produced.
    /// </summary>
    public static TifaSequence BuildSequence(List<TifaNoteSnapshot> notes, TifaLanguage language) {
        var sequence = new TifaSequence();
        var emitted = new List<string>();
        foreach (var note in notes) {
            var units = ResolveUnits(note, language);
            if (units.Count == 0) {
                sequence.Unresolved.Add(string.IsNullOrWhiteSpace(note.Lyric) ? "(empty)" : note.Lyric);
                continue;
            }
            int noteIndex = sequence.Notes.Count;
            sequence.Notes.Add(note);
            foreach (var unit in units) {
                // A VC-style phoneme repeats the previous note's tail; drop the
                // repeated head so the aligner sees the phonetic sequence once.
                // Only multi-phone units may be trimmed, so a deliberate
                // repeated vowel across two notes is kept.
                int skip = unit.Count >= 2 ? LongestOverlap(emitted, unit) : 0;
                var kept = unit.Skip(skip).ToList();
                if (kept.Count == 0) {
                    kept = unit;
                }
                foreach (var phone in kept) {
                    sequence.Phones.Add(phone);
                    sequence.PhoneNote.Add(noteIndex);
                    emitted.Add(phone);
                }
            }
        }
        return sequence;
    }

    /// <summary>Length of the longest suffix of <paramref name="previous"/>
    /// that is also a prefix of <paramref name="current"/>.</summary>
    static int LongestOverlap(List<string> previous, List<string> current) {
        int max = Math.Min(previous.Count, current.Count);
        for (int length = max; length > 0; --length) {
            bool match = true;
            for (int i = 0; i < length; ++i) {
                if (!string.Equals(previous[previous.Count - length + i], current[i], StringComparison.Ordinal)) {
                    match = false;
                    break;
                }
            }
            if (match) {
                return length;
            }
        }
        return 0;
    }

    /// <summary>
    /// One unit is one phoneme's phone list. Chinese and Cantonese resolve a
    /// note from its lyric as a single unit; Japanese and English use the
    /// phones the singer produced, one unit per phoneme.
    /// </summary>
    static List<List<string>> ResolveUnits(TifaNoteSnapshot note, TifaLanguage language) {
        if (NonPhoneticLyrics.Contains(note.Lyric.Trim(), StringComparer.OrdinalIgnoreCase)) {
            return new List<List<string>>();
        }
        switch (language) {
            case TifaLanguage.Zh: {
                var syllable = ChineseSyllable(note.Lyric);
                var phones = syllable == null ? null : TifaPhonemeData.Lookup(language, syllable);
                return phones == null ? new List<List<string>>() : new List<List<string>> { phones.ToList() };
            }
            case TifaLanguage.Yue: {
                var syllable = JyutpingSyllable(note.Lyric);
                var phones = syllable == null ? null : TifaPhonemeData.Lookup(language, syllable);
                return phones == null ? new List<List<string>>() : new List<List<string>> { phones.ToList() };
            }
            default:
                return ResolveUnitsFromPhonemes(note, language);
        }
    }

    /// <summary>
    /// Japanese and English: tokenize the phonemes the singer produced. Kana
    /// aliases (VCV banks) are romanized before the mora lookup.
    /// </summary>
    static List<List<string>> ResolveUnitsFromPhonemes(TifaNoteSnapshot note, TifaLanguage language) {
        var units = new List<List<string>>();
        foreach (var phoneme in note.Phonemes) {
            var unit = new List<string>();
            foreach (var rawToken in phoneme.Phoneme.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                var token = rawToken.Trim();
                if (token.Length == 0) {
                    continue;
                }
                if (language == TifaLanguage.Ja) {
                    var romaji = ToRomaji(token);
                    if (romaji.Length == 0) {
                        continue;
                    }
                    if (TifaPhonemeData.IsKnownPhone(language, romaji)) {
                        unit.Add(romaji);
                        continue;
                    }
                    var mora = TifaPhonemeData.Lookup(language, romaji.ToLowerInvariant());
                    if (mora != null) {
                        unit.AddRange(mora);
                        continue;
                    }
                    if (TifaPhonemeData.IsKnownPhone(language, token)) {
                        unit.Add(token);
                        continue;
                    }
                    // Unknown token: the note cannot be placed reliably.
                    return new List<List<string>>();
                } else {
                    var symbol = token.ToLowerInvariant().TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                    if (symbol.Length == 0) {
                        continue;
                    }
                    if (TifaPhonemeData.IsKnownPhone(language, symbol)) {
                        unit.Add(symbol);
                    }
                }
            }
            if (unit.Count > 0) {
                units.Add(unit);
            }
        }
        return units;
    }

    static string ToRomaji(string token) {
        try {
            if (token.Any(c => c >= 0x3040 && c <= 0x30FF)) {
                return WanaKanaNet.WanaKana.ToRomaji(token).Replace(" ", string.Empty);
            }
        } catch {
            // Not kana: fall through and treat the token as romaji.
        }
        return token;
    }

    /// <summary>Hanzi (or an existing pinyin spelling) to a pinyin syllable.</summary>
    public static string? ChineseSyllable(string lyric) {
        var text = lyric.Trim();
        if (text.Length == 0) {
            return null;
        }
        if (Pinyin.Pinyin.Instance.IsHanzi(text)) {
            var romanized = BaseChinesePhonemizer.Romanize(new[] { text });
            text = romanized.Length > 0 ? romanized[0] : text;
        }
        return NormalizePinyin(text);
    }

    internal static string? NormalizePinyin(string raw) {
        var text = raw.Trim().ToLowerInvariant()
            .Replace("ü", "v")
            .Replace("u:", "v")
            .Replace("'", string.Empty)
            .Replace("’", string.Empty);
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text) {
            if (char.IsDigit(c)) {
                continue;
            }
            builder.Append(ToneMarkBase(c));
        }
        text = builder.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Strips a pinyin tone mark ("jiā" -&gt; "jia").</summary>
    static char ToneMarkBase(char c) => c switch {
        'ā' or 'á' or 'ǎ' or 'à' => 'a',
        'ē' or 'é' or 'ě' or 'è' => 'e',
        'ī' or 'í' or 'ǐ' or 'ì' => 'i',
        'ō' or 'ó' or 'ǒ' or 'ò' => 'o',
        'ū' or 'ú' or 'ǔ' or 'ù' => 'u',
        'ǖ' or 'ǘ' or 'ǚ' or 'ǜ' or 'ü' => 'v',
        _ => c,
    };

    /// <summary>Jyutping with the tone digit removed ("nei5" -> "nei").</summary>
    public static string? JyutpingSyllable(string lyric) {
        var text = lyric.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        text = new string(text.Where(c => !char.IsDigit(c)).ToArray());
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Map aligner phone spans onto phoneme moves. <paramref name="spans"/>
    /// is parallel to <see cref="TifaSequence.Phones"/>; skipped phones carry
    /// an empty interval and are ignored for the window.
    /// </summary>
    /// <summary>
    /// A phone the decoder skipped is emitted by the CLI as a zero-width span
    /// inflated to one millisecond, so anything not wider than two
    /// milliseconds carries no timing information.
    /// </summary>
    const double MinimumSpanSeconds = 0.002;

    /// <summary>
    /// Windows shorter than this are decoding artifacts (a syllable resolved
    /// into one-frame phones), not a real performance; such a note is left
    /// untouched and reported as uncertain.
    /// </summary>
    const double MinimumWindowMs = 30.0;

    public static List<TifaPhonemeMove> ComputeMoves(
        TifaSequence sequence, List<TifaInterval> spans, double spanOriginMs,
        UProject project, UVoicePart part, out int clamped, out int uncertain) {
        clamped = 0;
        uncertain = 0;
        var moves = new List<TifaPhonemeMove>();
        if (spans.Count != sequence.Phones.Count) {
            return moves;
        }
        var axis = project.timeAxis;
        var windows = new Dictionary<int, (double start, double end)>();
        for (int i = 0; i < spans.Count; ++i) {
            var span = spans[i];
            if (span.End - span.Start <= MinimumSpanSeconds) {
                continue;
            }
            int noteIndex = sequence.PhoneNote[i];
            double start = spanOriginMs + span.Start * 1000.0;
            double end = spanOriginMs + span.End * 1000.0;
            if (windows.TryGetValue(noteIndex, out var window)) {
                windows[noteIndex] = (Math.Min(window.start, start), Math.Max(window.end, end));
            } else {
                windows[noteIndex] = (start, end);
            }
        }

        foreach (var (noteIndex, window) in windows.OrderBy(entry => entry.Key)) {
            var note = sequence.Notes[noteIndex];
            var phonemes = note.Phonemes;
            if (phonemes.Count == 0) {
                continue;
            }
            double originalStart = phonemes[0].StartMs;
            double originalEnd = phonemes[^1].EndMs;
            double originalLength = originalEnd - originalStart;
            double targetLength = window.end - window.start;
            if (originalLength <= 0.1) {
                continue;
            }
            if (targetLength <= MinimumWindowMs) {
                ++uncertain;
                continue;
            }
            double scale = targetLength / originalLength;
            int previousTick = int.MinValue;
            foreach (var phoneme in phonemes) {
                // Preserve the bank's internal allocation: every phoneme keeps
                // its relative position inside the note window.
                double newStartMs = window.start + (phoneme.StartMs - originalStart) * scale;
                int newTick = axis.MsPosToTickPos(newStartMs + phoneme.Preutter) - part.position;
                bool isClamped = false;
                if (previousTick != int.MinValue && newTick < previousTick + 10) {
                    newTick = previousTick + 10;
                    isClamped = true;
                }
                int maxTick = note.ExtendedEndTick - 10;
                if (newTick > maxTick) {
                    newTick = maxTick;
                    isClamped = true;
                }
                if (isClamped) {
                    ++clamped;
                }
                previousTick = newTick;
                if (newTick != phoneme.PositionTick) {
                    moves.Add(new TifaPhonemeMove {
                        Note = phoneme.Note,
                        Index = phoneme.Index,
                        OldPositionTick = phoneme.PositionTick,
                        NewPositionTick = newTick,
                        Clamped = isClamped,
                    });
                }
            }
        }
        return moves;
    }
}
