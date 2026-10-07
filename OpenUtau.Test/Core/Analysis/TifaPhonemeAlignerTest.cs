using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.Analysis;

public class TifaPhonemeDataTest {
    [Fact]
    public void MandarinSyllableTableUsesTwoSegmentScheme() {
        Assert.Equal(new[] { "j", "ia" }, TifaPhonemeData.Lookup(TifaLanguage.Zh, "jia"));
        Assert.Equal(new[] { "zh", "ir" }, TifaPhonemeData.Lookup(TifaLanguage.Zh, "zhi"));
        Assert.Equal(new[] { "c", "i0" }, TifaPhonemeData.Lookup(TifaLanguage.Zh, "ci"));
        Assert.Equal(new[] { "y", "iao" }, TifaPhonemeData.Lookup(TifaLanguage.Zh, "yao"));
        Assert.Equal(new[] { "y0", "van" }, TifaPhonemeData.Lookup(TifaLanguage.Zh, "yuan"));
    }

    [Fact]
    public void CantoneseAndJapaneseTablesResolve() {
        Assert.NotNull(TifaPhonemeData.Lookup(TifaLanguage.Yue, "nei"));
        Assert.Equal(new[] { "k", "a" }, TifaPhonemeData.Lookup(TifaLanguage.Ja, "ka"));
        Assert.Equal(new[] { "N" }, TifaPhonemeData.Lookup(TifaLanguage.Ja, "n"));
        Assert.Equal(new[] { "cl" }, TifaPhonemeData.Lookup(TifaLanguage.Ja, "cl"));
    }

    [Fact]
    public void PhoneInventoryMatchesModel() {
        Assert.Contains("ir", TifaPhonemeData.Phones(TifaLanguage.Zh));
        Assert.Contains("_r", TifaPhonemeData.Phones(TifaLanguage.En));
        Assert.Contains("gw", TifaPhonemeData.Phones(TifaLanguage.Yue));
        Assert.DoesNotContain("ii", TifaPhonemeData.Phones(TifaLanguage.Zh));
    }

    [Fact]
    public void PinyinNormalizationHandlesUmlautAndTones() {
        Assert.Equal("lve", TifaPhonemeAligner.NormalizePinyin("lüe"));
        Assert.Equal("nv", TifaPhonemeAligner.NormalizePinyin("nü3"));
        Assert.Equal("jia", TifaPhonemeAligner.NormalizePinyin("jiā"));
        Assert.Equal("jia", TifaPhonemeAligner.ChineseSyllable("家"));
        Assert.Equal("jia", TifaPhonemeAligner.ChineseSyllable("jia1"));
        Assert.Null(TifaPhonemeAligner.ChineseSyllable(""));
    }

    [Fact]
    public void JyutpingStripsToneDigits() {
        Assert.Equal("nei", TifaPhonemeAligner.JyutpingSyllable("nei5"));
        Assert.Equal("gwok", TifaPhonemeAligner.JyutpingSyllable("gwok3"));
    }
}

public class TifaTextGridTest {
    const string Sample = """
        File type = "ooTextFile"
        Object class = "TextGrid"

        xmin = 0
        xmax = 7.08
        tiers? <exists>
        size = 3
        item []:
        	item [1]:
        		class = "IntervalTier"
        		name = "texts"
        		xmin = 0
        		xmax = 7.08
        		intervals: size = 1
        			intervals [1]:
        				xmin = 0
        				xmax = 6.28
        				text = ""
        	item [2]:
        		class = "IntervalTier"
        		name = "phones"
        		xmin = 0
        		xmax = 7.08
        		intervals: size = 3
        			intervals [1]:
        				xmin = 0.04
        				xmax = 0.07
        				text = "zh/ao"
        			intervals [2]:
        				xmin = 0.07
        				xmax = 0.08
        				text = ""
        			intervals [3]:
        				xmin = 0.41
        				xmax = 1.01
        				text = "zh/e"
        """;

    [Fact]
    public void DropsGapIntervalsAddedByNewerClis() {
        var withGaps = new List<TifaInterval> {
            new TifaInterval { Start = 0.00, End = 0.04, Text = "y" },
            new TifaInterval { Start = 0.04, End = 0.28, Text = "iao" },
            new TifaInterval { Start = 0.28, End = 0.42, Text = "SP" },
            new TifaInterval { Start = 0.42, End = 1.00, Text = "van" },
        };
        var phones = TifaTextGrid.DropGapIntervals(withGaps, 3);
        Assert.Equal(3, phones.Count);
        Assert.Equal(new[] { "y", "iao", "van" }, phones.Select(p => p.Text));

        // A tier that already matches the phone list is left alone, even if a
        // real phone happens to carry the filler label.
        var exact = new List<TifaInterval> {
            new TifaInterval { Start = 0, End = 1, Text = "a" },
            new TifaInterval { Start = 1, End = 2, Text = "SP" },
        };
        Assert.Same(exact, TifaTextGrid.DropGapIntervals(exact, 2));

        // Unrelated extra intervals are not silently dropped.
        var odd = new List<TifaInterval> {
            new TifaInterval { Start = 0, End = 1, Text = "a" },
            new TifaInterval { Start = 1, End = 2, Text = "b" },
            new TifaInterval { Start = 2, End = 3, Text = "SP" },
        };
        Assert.Equal(3, TifaTextGrid.DropGapIntervals(odd, 1).Count);
    }

    [Fact]
    public void ParsesPhonesTierInOrder() {
        string path = Path.Combine(Path.GetTempPath(), $"tifa-test-{Guid.NewGuid():N}.TextGrid");
        try {
            File.WriteAllText(path, Sample);
            Assert.Equal(7.08, TifaTextGrid.ParseXmax(path), 3);
            var intervals = TifaTextGrid.ParseTier(path, "phones");
            Assert.Equal(3, intervals.Count);
            Assert.Equal("zh/ao", intervals[0].Text);
            Assert.Equal(0.04, intervals[0].Start, 3);
            Assert.Equal(string.Empty, intervals[1].Text);
            Assert.Equal("zh/e", intervals[2].Text);
            Assert.Equal(1.01, intervals[2].End, 3);
        } finally {
            File.Delete(path);
        }
    }
}

public class TifaPhonemeAlignerTest {
    static UProject MakeProject() {
        var project = new UProject();
        project.tempos = new List<UTempo> { new UTempo(0, 120) };
        project.timeSignatures = new List<UTimeSignature> { new UTimeSignature(0, 4, 4) };
        return project;
    }

    static UNote AddNote(UProject project, UVoicePart part, int position, int duration, string lyric) {
        var note = project.CreateNote(60, position, duration);
        note.lyric = lyric;
        part.notes.Add(note);
        return note;
    }

    static UPhoneme AddPhoneme(UVoicePart part, UNote note, int position, int index, string phoneme) {
        var uPhoneme = new UPhoneme {
            Parent = note,
            index = index,
            position = position,
            rawPosition = position,
            phoneme = phoneme,
            rawPhoneme = phoneme,
        };
        part.phonemes.Add(uPhoneme);
        return uPhoneme;
    }

    [Fact]
    public void SequenceSkipsVcDuplicates() {
        var project = MakeProject();
        var part = new UVoicePart { position = 0, duration = 1920 };
        var note1 = AddNote(project, part, 0, 480, "a");
        AddPhoneme(part, note1, 0, 0, "a");
        var note2 = AddNote(project, part, 480, 480, "ka");
        AddPhoneme(part, note2, 400, 0, "a k");
        AddPhoneme(part, note2, 480, 1, "k a");

        var notes = TifaPhonemeAligner.Snapshot(part, project, audibleOnset: false);
        var sequence = TifaPhonemeAligner.BuildSequence(notes, TifaLanguage.Ja);

        Assert.Equal(2, sequence.Notes.Count);
        Assert.Equal(new[] { "a", "k", "a" }, sequence.Phones);
        Assert.Equal(new[] { 0, 1, 1 }, sequence.PhoneNote);
    }

    [Fact]
    public void RepeatedVowelAcrossNotesIsKept() {
        var project = MakeProject();
        var part = new UVoicePart { position = 0, duration = 960 };
        var note1 = AddNote(project, part, 0, 480, "a");
        AddPhoneme(part, note1, 0, 0, "a");
        var note2 = AddNote(project, part, 480, 480, "a");
        AddPhoneme(part, note2, 480, 0, "a");

        var notes = TifaPhonemeAligner.Snapshot(part, project, audibleOnset: false);
        var sequence = TifaPhonemeAligner.BuildSequence(notes, TifaLanguage.Ja);

        Assert.Equal(new[] { "a", "a" }, sequence.Phones);
    }

    [Fact]
    public void ChineseSyllablesProduceTwoSegmentPhones() {
        var project = MakeProject();
        var part = new UVoicePart { position = 0, duration = 960 };
        var note1 = AddNote(project, part, 0, 480, "家");
        AddPhoneme(part, note1, 0, 0, "j");
        AddPhoneme(part, note1, 240, 1, "ia");
        var note2 = AddNote(project, part, 480, 480, "园");
        AddPhoneme(part, note2, 480, 0, "y");
        AddPhoneme(part, note2, 600, 1, "u");
        AddPhoneme(part, note2, 720, 2, "an");

        var notes = TifaPhonemeAligner.Snapshot(part, project, audibleOnset: false);
        var sequence = TifaPhonemeAligner.BuildSequence(notes, TifaLanguage.Zh);

        // The 3-segment bank still maps onto the aligner's 2-segment table.
        Assert.Equal(new[] { "j", "ia", "y0", "van" }, sequence.Phones);
    }

    [Fact]
    public void MovesPreserveInternalDurationAllocation() {
        var project = MakeProject();
        var part = new UVoicePart { position = 0, duration = 1920 };
        var note = AddNote(project, part, 0, 960, "jia");
        var p0 = AddPhoneme(part, note, 0, 0, "j");
        var p1 = AddPhoneme(part, note, 240, 1, "i");
        var p2 = AddPhoneme(part, note, 480, 2, "a");

        var notes = TifaPhonemeAligner.Snapshot(part, project, audibleOnset: false);
        var sequence = TifaPhonemeAligner.BuildSequence(notes, TifaLanguage.Zh);
        Assert.Equal(new[] { "j", "ia" }, sequence.Phones);

        // The recording has the syllable starting 100 ms later and 100 ms longer
        // (120 BPM, 480 ticks = 500 ms per note).
        var spans = new List<TifaInterval> {
            new TifaInterval { Start = 0.100, End = 0.250, Text = "zh/j" },
            new TifaInterval { Start = 0.250, End = 0.700, Text = "zh/ia" },
        };
        var moves = TifaPhonemeAligner.ComputeMoves(sequence, spans, 0, project, part, out int clamped, out int uncertain);

        Assert.Equal(0, clamped);
        // 120 BPM / 480 ticks = 500 ms per note. Phonemes start at 0/250/500 ms
        // of a 1000 ms note; the window becomes 100..700 ms (scale 0.6), so the
        // new starts are 100/250/400 ms = 96/240/384 ticks. Internal spacing is
        // scaled, not flattened - the middle phoneme happens to land on its old
        // tick, so it produces no move.
        var newPositions = new Dictionary<int, int>();
        foreach (var move in moves) {
            newPositions[move.OldPositionTick] = move.NewPositionTick;
        }
        Assert.Equal(96, newPositions.GetValueOrDefault(p0.position, p0.position));
        Assert.Equal(240, newPositions.GetValueOrDefault(p1.position, p1.position));
        Assert.Equal(384, newPositions.GetValueOrDefault(p2.position, p2.position));
        Assert.Equal(2, newPositions.Count);
    }

    [Fact]
    public void SkippedSpansDoNotShrinkTheNoteWindow() {
        var project = MakeProject();
        var part = new UVoicePart { position = 0, duration = 960 };
        var note = AddNote(project, part, 0, 480, "jia");
        AddPhoneme(part, note, 0, 0, "j");
        AddPhoneme(part, note, 240, 1, "ia");

        var notes = TifaPhonemeAligner.Snapshot(part, project, audibleOnset: false);
        var sequence = TifaPhonemeAligner.BuildSequence(notes, TifaLanguage.Zh);
        var spans = new List<TifaInterval> {
            new TifaInterval { Start = 0.000, End = 0.001, Text = string.Empty },
            new TifaInterval { Start = 0.200, End = 0.500, Text = "zh/ia" },
        };
        var moves = TifaPhonemeAligner.ComputeMoves(sequence, spans, 0, project, part, out _, out _);
        Assert.NotEmpty(moves);
        // The skipped phone is ignored; the note window is the surviving span.
        Assert.All(moves, m => Assert.True(m.NewPositionTick >= 0));
    }
}
