using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Core {
    /// <summary>
    /// The pitch predictor and the .ds export must build their note timelines from
    /// the notes that own phone frames. A note without phonemes (an error word, a
    /// note the phonemizer skipped, or a context note from another phrase) has no
    /// frames in the phone layout, so giving it a duration shifts every following
    /// note and the rendered pitch.
    /// </summary>
    [Collection(RenderSingletonCollection.Name)]
    public class DiffSingerPitchTimelineTest : IDisposable {
        class TestSinger : USinger {
            readonly UOto otoA;
            public TestSinger(UOto a) {
                otoA = a;
                found = true;
                loaded = true;
            }
            public override string Id => "pitch-timeline-test-singer";
            public override IList<USubbank> Subbanks => new USubbank[0];
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = phoneme == "A" ? otoA : (UOto)null;
                return oto != null;
            }
            public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
                oto = null;
                return false;
            }
        }

        class TestRenderer : IRenderer {
            public USingerType SingerType => USingerType.DiffSinger;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => false;
            // Wide enough to merge the short gap of the failed-middle-note test,
            // like DiffSinger's head/tail padding does for nearby phrases.
            public (double HeadMs, double TailMs) PhrasePadding(USinger singer, IEnumerable<UPhoneme> phonemes) =>
                (100, 100);
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    CancellationTokenSource cancellation, bool isPreRender = false,
                    RenderPhraseEvents? renderEvents = null) => throw new NotImplementedException();
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        static bool IsVowel(string phoneme) => phoneme == "A";

        static UProject NewProject() {
            var project = new UProject();
            project.RegisterExpression(new UExpressionDescriptor("engine", "eng", 0, 100, 0) {
                options = new[] { "" },
            });
            project.RegisterExpression(new UExpressionDescriptor("volume", "vol", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("velocity", "vel", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("modulation", "mod", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("direct", "dir", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("shift", "shft", 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("attack", "atk", 0, 100, 100));
            project.RegisterExpression(new UExpressionDescriptor("decay", "dec", 0, 100, 100));
            return project;
        }

        static UNote AddNote(UVoicePart part, List<UNote> notes, int position, int duration, string lyric = "a") {
            var note = UNote.Create();
            note.position = position;
            note.duration = duration;
            note.ExtendedDuration = duration;
            note.tone = 60;
            note.lyric = lyric;
            if (notes.Count > 0) {
                note.Prev = notes[^1];
                notes[^1].Next = note;
            }
            notes.Add(note);
            part.notes.Add(note);
            return note;
        }

        static UPhoneme AddPhoneme(UVoicePart part, UNote note, int position) {
            var phoneme = new UPhoneme {
                position = position,
                phoneme = "A",
                Parent = note,
            };
            part.phonemes.Add(phoneme);
            return phoneme;
        }

        static void ValidatePhonemes(UVoicePart part, UProject project, UTrack track, params UPhoneme[] phonemes) {
            for (int i = 0; i < phonemes.Length; ++i) {
                phonemes[i].Prev = i > 0 ? phonemes[i - 1] : null;
                phonemes[i].Next = i + 1 < phonemes.Length ? phonemes[i + 1] : null;
            }
            foreach (var phoneme in phonemes) {
                phoneme.Validate(new ValidateOptions(), project, track, part, phoneme.Parent);
            }
        }

        static (UProject Project, UTrack Track, UVoicePart Part) NewPart() {
            var project = NewProject();
            var track = project.tracks[0];
            track.Singer = new TestSinger(UOto.OfDummy("A"));
            track.RendererSettings.Renderer = new TestRenderer();
            var part = new UVoicePart { trackNo = 0, position = 0 };
            project.parts.Add(part);
            return (project, track, part);
        }

        readonly float previousMergeSec = Preferences.Default.MergePhrasesSec;

        public DiffSingerPitchTimelineTest() {
            Preferences.Default.MergePhrasesSec = 0;
        }

        public void Dispose() {
            Preferences.Default.MergePhrasesSec = previousMergeSec;
        }

        [Fact]
        public void ErrorNoteBeforePhraseIsNotPartOfTheNoteTimeline() {
            var (project, track, part) = NewPart();
            var notes = new List<UNote>();
            var errorNote = AddNote(part, notes, 0, 480, "br2");
            var note1 = AddNote(part, notes, 480, 480);
            var note2 = AddNote(part, notes, 960, 480);
            var errorPhoneme = AddPhoneme(part, errorNote, 0);
            errorPhoneme.ErrorException = new InvalidDataException("illegal word");
            var phone1 = AddPhoneme(part, note1, 480);
            var phone2 = AddPhoneme(part, note2, 960);
            ValidatePhonemes(part, project, track, errorPhoneme, phone1, phone2);
            Assert.True(errorPhoneme.Error);

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            Assert.Equal(new[] { "br2", "a", "a" }, phrase.notes.Select(n => n.lyric).ToArray());
            Assert.Equal(new[] { 1, 2 }, phrase.phones.Select(p => p.noteIndex).ToArray());
            Assert.Equal(new[] { 1, 2 }, DiffSingerUtils.NotesForPitch(phrase).Select(n => n.noteIndex).ToArray());

            // 120 BPM: 480 ticks = 500 ms. 10 ms frames, 8 head/tail frames.
            const double frameMs = 10;
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            var phDur = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            int totalFrames = phDur.Sum();
            double startMs = phrase.phones[0].positionMs - headFrames * frameMs;

            var (noteDur, noteMidi, noteRest, paddedToReal) =
                DiffSingerUtils.PaddedNoteTimeline(phrase, startMs, frameMs, totalFrames, IsVowel);

            Assert.Equal(new[] { headFrames, 50, 50, tailFrames }, noteDur);
            Assert.Equal(totalFrames, noteDur.Sum());
            Assert.Equal(new[] { 1, 1, 2, 2 }, paddedToReal);
            Assert.Equal(new[] { 60f, 60f, 60f, 60f }, noteMidi);
            Assert.Equal(new[] { true, false, false, true }, noteRest);

            var (noteSeq, noteDurMs, noteSlur) = DiffSingerUtils.ScriptNoteTimeline(
                phrase, headFrames * frameMs, tailFrames * frameMs);
            Assert.Equal(new[] { 0, 60, 60, 0 }, noteSeq);
            Assert.Collection(noteDurMs,
                ms => Assert.Equal(80, ms, 6),
                ms => Assert.Equal(500, ms, 6),
                ms => Assert.Equal(500, ms, 6),
                ms => Assert.Equal(80, ms, 6));
            Assert.Equal(new[] { 0, 0, 0, 0 }, noteSlur);
        }

        [Fact]
        public void PartialOverlapBeforeTheFrameOriginIsTrimmed() {
            var (project, track, part) = NewPart();
            var notes = new List<UNote>();
            // The note starts at 0 ms, but its only phone starts 501 ms in. The pitch
            // frame origin is 421 ms, so the first 421 ms of the note precede it.
            var note = AddNote(part, notes, 0, 1920);
            var phone = AddPhoneme(part, note, 481);
            ValidatePhonemes(part, project, track, phone);

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            const double frameMs = 10;
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            var phDur = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            int totalFrames = phDur.Sum();
            double startMs = phrase.phones[0].positionMs - headFrames * frameMs;

            var (noteDur, _, _, paddedToReal) =
                DiffSingerUtils.PaddedNoteTimeline(phrase, startMs, frameMs, totalFrames, IsVowel);

            // 0 head rest, the note trimmed by the 421 ms before the origin, 80 ms tail.
            Assert.Equal(new[] { 0, 158, tailFrames }, noteDur);
            Assert.Equal(totalFrames, noteDur.Sum());
            Assert.Equal(new[] { 0, 0, 0 }, paddedToReal);

            var (noteSeq, noteDurMs, noteSlur) = DiffSingerUtils.ScriptNoteTimeline(
                phrase, headFrames * frameMs, tailFrames * frameMs);
            double offsetMs = phrase.phones[0].positionMs - headFrames * frameMs;
            double expectedNoteMs = phrase.notes[0].durationMs
                - Math.Max(0, offsetMs - phrase.notes[0].positionMs);
            Assert.Equal(new[] { 0, 60, 0 }, noteSeq);
            Assert.Collection(noteDurMs,
                ms => Assert.Equal(0, ms, 6),
                ms => Assert.Equal(expectedNoteMs, ms, 6),
                ms => Assert.Equal(80, ms, 6));
            Assert.Equal(new[] { 0, 0, 0 }, noteSlur);
        }

        [Fact]
        public void FailedMiddleNoteBecomesAGapRest() {
            var (project, track, part) = NewPart();
            var notes = new List<UNote>();
            var note1 = AddNote(part, notes, 0, 480);
            var errorNote = AddNote(part, notes, 480, 120, "br2");
            var note3 = AddNote(part, notes, 600, 480);
            var phone1 = AddPhoneme(part, note1, 0);
            var errorPhoneme = AddPhoneme(part, errorNote, 480);
            errorPhoneme.ErrorException = new InvalidDataException("illegal word");
            var phone3 = AddPhoneme(part, note3, 600);
            ValidatePhonemes(part, project, track, phone1, errorPhoneme, phone3);

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            Assert.Equal(new[] { "a", "br2", "a" }, phrase.notes.Select(n => n.lyric).ToArray());
            Assert.Equal(new[] { 0, 2 }, phrase.phones.Select(p => p.noteIndex).ToArray());
            Assert.Equal(new[] { 0, 2 }, DiffSingerUtils.NotesForPitch(phrase).Select(n => n.noteIndex).ToArray());

            const double frameMs = 10;
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            var phDur = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            int totalFrames = phDur.Sum();
            double startMs = phrase.phones[0].positionMs - headFrames * frameMs;

            var (noteDur, noteMidi, noteRest, paddedToReal) =
                DiffSingerUtils.PaddedNoteTimeline(phrase, startMs, frameMs, totalFrames, IsVowel);

            // The failed note's 125 ms become the gap rest between note1 and note3.
            Assert.Equal(new[] { 8, 50, 13, 50, 8 }, noteDur);
            Assert.Equal(new[] { 0, 0, 0, 2, 2 }, paddedToReal);
            Assert.Equal(new[] { true, false, true, false, true }, noteRest);
            Assert.All(noteMidi, midi => Assert.Equal(60f, midi));

            // The gap rest follows the previous real note; selecting note3 only
            // retakes note3 and the tail.
            var maskForNote3 = DiffSingerRetake.BuildRetakeFrameMask(
                noteDur, paddedToReal, new HashSet<int> { 2 }, totalFrames);
            Assert.All(maskForNote3.Take(71), retake => Assert.False(retake));
            Assert.All(maskForNote3.Skip(71).Take(50), retake => Assert.True(retake));
            Assert.All(maskForNote3.Skip(121), retake => Assert.True(retake));

            var maskForNote1 = DiffSingerRetake.BuildRetakeFrameMask(
                noteDur, paddedToReal, new HashSet<int> { 0 }, totalFrames);
            Assert.All(maskForNote1.Take(71), retake => Assert.True(retake));
            Assert.All(maskForNote1.Skip(71), retake => Assert.False(retake));
        }

        [Fact]
        public void FailedLastNoteIsExcluded() {
            var (project, track, part) = NewPart();
            var notes = new List<UNote>();
            var note1 = AddNote(part, notes, 0, 480);
            var errorNote = AddNote(part, notes, 480, 480, "br2");
            var phone1 = AddPhoneme(part, note1, 0);
            var errorPhoneme = AddPhoneme(part, errorNote, 480);
            errorPhoneme.ErrorException = new InvalidDataException("illegal word");
            ValidatePhonemes(part, project, track, phone1, errorPhoneme);

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            Assert.Equal(new[] { "a", "br2" }, phrase.notes.Select(n => n.lyric).ToArray());
            Assert.Equal(new[] { 0 }, DiffSingerUtils.NotesForPitch(phrase).Select(n => n.noteIndex).ToArray());

            const double frameMs = 10;
            int headFrames = DiffSingerUtils.headFrames;
            int tailFrames = DiffSingerUtils.tailFrames;
            var phDur = DiffSingerUtils.PaddedPhoneDurations(phrase, frameMs, headFrames, tailFrames);
            int totalFrames = phDur.Sum();
            double startMs = phrase.phones[0].positionMs - headFrames * frameMs;

            var (noteDur, _, _, paddedToReal) =
                DiffSingerUtils.PaddedNoteTimeline(phrase, startMs, frameMs, totalFrames, IsVowel);

            Assert.Equal(new[] { 8, 50, 8 }, noteDur);
            Assert.Equal(new[] { 0, 0, 0 }, paddedToReal);
        }

        [Fact]
        public void FailedRootWithExtensionIsExcluded() {
            var (project, track, part) = NewPart();
            var notes = new List<UNote>();
            var errorNote = AddNote(part, notes, 0, 480, "br2");
            var extension = AddNote(part, notes, 480, 480, "+a");
            extension.Extends = errorNote;
            errorNote.ExtendedDuration = extension.End - errorNote.position;
            var note3 = AddNote(part, notes, 960, 480);
            var errorPhoneme = AddPhoneme(part, errorNote, 0);
            errorPhoneme.ErrorException = new InvalidDataException("illegal word");
            var phone3 = AddPhoneme(part, note3, 960);
            ValidatePhonemes(part, project, track, errorPhoneme, phone3);

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            Assert.Equal(new[] { "+a", "a" }, phrase.notes.Select(n => n.lyric).ToArray());
            // The extension follows a failed root that has no phone, so it has no
            // timeline segment either.
            Assert.Equal(new[] { 2 }, DiffSingerUtils.NotesForPitch(phrase).Select(n => n.noteIndex).ToArray());
        }

        [Fact]
        public void ExtensionNeighbourWhoseRootHasPhonesElsewhereIsExcluded() {
            var (project, track, part) = NewPart();
            var notes = new List<UNote>();
            var note1 = AddNote(part, notes, 0, 480);
            var extension = AddNote(part, notes, 480, 240, "+a");
            extension.Extends = note1;
            // Force the first phone to end before the next phrase's phone so the
            // phrases split; phrase 2 then carries the extension as pitch context.
            note1.ExtendedDuration = 240;
            var note2 = AddNote(part, notes, 720, 480);
            var phone1 = AddPhoneme(part, note1, 0);
            var phone2 = AddPhoneme(part, note2, 720);
            ValidatePhonemes(part, project, track, phone1, phone2);

            var phrases = RenderPhrase.FromPart(project, track, part);
            Assert.Equal(2, phrases.Count);
            var phrase2 = phrases[1];
            Assert.Equal(new[] { 1, 2 }, phrase2.notes.Select(n => n.noteIndex).ToArray());
            Assert.Equal(new[] { 2 }, phrase2.phones.Select(p => p.noteIndex).ToArray());
            // The extension's root has a phone, but it belongs to phrase 1.
            Assert.Equal(new[] { 2 }, DiffSingerUtils.NotesForPitch(phrase2).Select(n => n.noteIndex).ToArray());
        }

        [Fact]
        public void ExtensionNotesStayInTheNoteTimeline() {
            var (project, track, part) = NewPart();
            var notes = new List<UNote>();
            var note0 = AddNote(part, notes, 0, 240);
            var note1 = AddNote(part, notes, 240, 240);
            var extension = AddNote(part, notes, 480, 240, "+a");
            extension.Extends = note1;
            note1.ExtendedDuration = extension.End - note1.position;
            var note2 = AddNote(part, notes, 720, 240);
            var phone0 = AddPhoneme(part, note0, 0);
            var phone1 = AddPhoneme(part, note1, 240);
            var phone2 = AddPhoneme(part, note2, 720);
            ValidatePhonemes(part, project, track, phone0, phone1, phone2);

            var phrase = Assert.Single(RenderPhrase.FromPart(project, track, part));
            // The extension has no phoneme of its own; it is carried by the note it
            // extends, so it must stay in the timeline.

            Assert.Equal(new[] { 0, 1, 2, 3 }, DiffSingerUtils.NotesForPitch(phrase).Select(n => n.noteIndex).ToArray());
        }
    }
}
