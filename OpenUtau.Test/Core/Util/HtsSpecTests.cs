using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util.nnmnkwii.frontend;
using OpenUtau.Core.Util.nnmnkwii.io.hts;
using Xunit;

namespace OpenUtau.Core.Util {
    public class HtsSpecTests {
        private static readonly Regex CurrentPhonemePattern = new(@"^[^@]+@[^\^]+\^[^-]+-(?<phoneme>[^+]+)\+", RegexOptions.Compiled);
        protected Dictionary<string, string[]> phoneDict = new Dictionary<string, string[]>();
        protected List<string> vowels = new List<string>() {"a","i","u","e","o" };
        protected List<string> consonants = new List<string>() {"k","s","t","n","h","m","y","r","w","g","z","d","b","p" };
        protected List<string> breaks = new List<string>();
        protected List<string> pauses = new List<string>() { "pau", "sil" };
        protected List<string> silences = new List<string>();
        protected List<string> unvoiced = new List<string>();

        private string GetPhonemeType(string phoneme) {
            if (phoneme == "xx") {
                return "xx";
            }
            if (vowels.Contains(phoneme)) {
                return "v";
            }
            if (pauses.Contains(phoneme)) {
                return "p";
            }
            if (silences.Contains(phoneme)) {
                return "s";
            }
            if (breaks.Contains(phoneme)) {
                return "b";
            }
            //if (unvoiced.Contains(phoneme)) {
            //    return "c";
            //}
            return "c";
        }

        private HTSNote MakeNote(int startMs, int endMs, int positionTicks, int durationTicks, int positionBar, string accent = "") {
            var symbols = new[] { "a" };
            var beatPerBar = 4;
            var beatUnit = 4;
            var key = 0;
            double bpm = 120;
            var tone = 60; // C4
            var isSlur = false;
            var isRest = false;
            var lang = "JPN";
            var accentStr = accent;
            var note = new HTSNote(symbols, beatPerBar, beatUnit, positionBar, 0, key, bpm, tone, isSlur, isRest, lang, accentStr, startMs, endMs, positionTicks, durationTicks);
            return note;
        }

        private HTSPhrase BuildPhrase(HTSNote[] notes, int resolution) {
            var phrase = new HTSPhrase(notes);
            phrase.UpdateResolution(resolution);
            var sentenceDurMs = notes.Sum(n => n.durationMs);
            var sentenceDurTicks = notes.Sum(n => n.durationTicks);
            for (var i = 0; i < notes.Length; i++) {
                var n = notes[i];
                n.parent = phrase;
                n.index = i + 1;
                n.indexBackwards = notes.Length - i;
                n.sentenceDurMs = sentenceDurMs;
                n.sentenceDurTicks = sentenceDurTicks;
                if (i > 0) {
                    notes[i - 1].next = n;
                    n.prev = notes[i - 1];
                }
            }
            return phrase;
        }

        private TimeAxis BuildDefaultTimeAxis() {
            var timeAxis = new TimeAxis();
            var project = new UProject();
            timeAxis.BuildSegments(project);
            return timeAxis;
        }

        [Fact]
        public void MeasureForwardBackwardAreComputedPerBar() {
            var res = 480; // ticks per quarter
            var ticksPer96 = res / 24; // 20
            var n0 = MakeNote(0, 1000, 0, 480, 0);
            var n1 = MakeNote(1000, 2000, 480, 480, 0);
            var n2 = MakeNote(2000, 3000, 960, 480, 0);
            var phrase = BuildPhrase(new[] { n0, n1, n2 }, res);

            var e0 = n0.e();
            var e1 = n1.e();
            var e2 = n2.e();

            // forward index (e10)
            Assert.Equal("1", e0[9]);
            Assert.Equal("2", e1[9]);
            Assert.Equal("3", e2[9]);
            // backward index (e11)
            Assert.Equal("3", e0[10]);
            Assert.Equal("2", e1[10]);
            Assert.Equal("1", e2[10]);

            // forward ms in centiseconds (e12)
            Assert.Equal("0", e0[11]);
            Assert.Equal("10", e1[11]);
            Assert.Equal("20", e2[11]);
            // backward ms in centiseconds (e13)
            Assert.Equal("20", e0[12]);
            Assert.Equal("10", e1[12]);
            Assert.Equal("0", e2[12]);

            // forward 96th (e14)
            Assert.Equal("0", e0[13]);
            Assert.Equal((480 / ticksPer96).ToString(), e1[13]);
            Assert.Equal((960 / ticksPer96).ToString(), e2[13]);
            // backward 96th (e15)
            Assert.Equal((960 / ticksPer96).ToString(), e0[14]);
            Assert.Equal((480 / ticksPer96).ToString(), e1[14]);
            Assert.Equal("0", e2[14]);

            // forward percent (e16)
            Assert.Equal("0", e0[15]);
            Assert.Equal("50", e1[15]);
            Assert.Equal("100", e2[15]);
            // backward percent (e17)
            Assert.Equal("100", e0[16]);
            Assert.Equal("50", e1[16]);
            Assert.Equal("0", e2[16]);
        }

        [Fact]
        public void AccentDistancesForwardBackward() {
            var res = 480;
            var ticksPer96 = res / 24; // 20
            var n0 = MakeNote(0, 1000, 0, 480, 0, accent: "");
            var n1 = MakeNote(1000, 2000, 480, 480, 0, accent: "A");
            var n2 = MakeNote(2000, 3000, 960, 480, 0, accent: "");
            var n3 = MakeNote(3000, 4000, 1440, 480, 0, accent: "A");
            var phrase = BuildPhrase(new[] { n0, n1, n2, n3 }, res);

            var e0 = n0.e();
            var e1 = n1.e();
            var e2 = n2.e();
            var e3 = n3.e();

            // For n2 (between accents): distances should be 1 note, 100 cs, 24 (96th)
            Assert.Equal("1", e2[28]); // next accent (notes)
            Assert.Equal("1", e2[29]); // prev accent (notes)
            Assert.Equal("100", e2[30]); // next accent (cs)
            Assert.Equal("100", e2[31]); // prev accent (cs)
            Assert.Equal((480 / ticksPer96).ToString(), e2[32]); // next (96th)
            Assert.Equal((480 / ticksPer96).ToString(), e2[33]); // prev (96th)

            // For n1 (accent): prev distance is 0, next accent is one note away (n2)
            Assert.Equal("1", e1[28]); // next accent (n3 via one note n2)
            Assert.Equal("0", e1[29]); // prev accent (itself)
            Assert.Equal("100", e1[30]); // next accent (cs)
            Assert.Equal("0", e1[31]); // prev accent (cs)
        }

        [Fact]
        public void NoteToPhonemesKeepsSharedNoteTiming() {
            var note = new HTSNote(
                new[] { "k", "a", "pau" },
                4,
                4,
                0,
                0,
                0,
                120,
                60,
                false,
                false,
                "JPN",
                string.Empty,
                120,
                360,
                0,
                480);

            var htsPhonemes = note.symbols.Select(x => new HTSPhoneme(x, note)).ToArray();
            int prevVowelPos = -1;
            foreach (int i in Enumerable.Range(0, htsPhonemes.Length)) {
                htsPhonemes[i].position = i + 1;
                htsPhonemes[i].position_backward = htsPhonemes.Length - i;
                htsPhonemes[i].type = GetPhonemeType(htsPhonemes[i].symbol);
                if (htsPhonemes[i].type == "v") {
                    prevVowelPos = i;
                } else {
                    if (prevVowelPos > 0) {
                        htsPhonemes[i].prev_vowel_distance = i - prevVowelPos;
                    }
                }
            }
            int nextVowelPos = -1;
            for (int i = htsPhonemes.Length - 1; i > 0; --i) {
                if (htsPhonemes[i].type == "v") {
                    nextVowelPos = i;
                } else {
                    if (nextVowelPos > 0) {
                        htsPhonemes[i].next_vowel_distance = nextVowelPos - i;
                    }
                }
            }

            Assert.Equal(3, htsPhonemes.Length);
            Assert.All(htsPhonemes, phoneme => Assert.Same(note, phoneme.parent));
            Assert.All(htsPhonemes, phoneme => Assert.Equal(120, phoneme.parent.startMs));
            Assert.All(htsPhonemes, phoneme => Assert.Equal(360, phoneme.parent.endMs));
            Assert.Equal(new[] { 1, 2, 3 }, htsPhonemes.Select(phoneme => phoneme.position).ToArray());
            Assert.Equal(new[] { 3, 2, 1 }, htsPhonemes.Select(phoneme => phoneme.position_backward).ToArray());
            Assert.Equal(new[] { "c", "v", "p" }, htsPhonemes.Select(phoneme => phoneme.type).ToArray());
            Assert.Equal(1, htsPhonemes[2].prev_vowel_distance);
        }

        [Fact]
        public void PhraseResolutionUpdateRecomputesMeasureTicks() {
            var note0 = MakeNote(0, 1000, 0, 960, 0);
            var note1 = MakeNote(1000, 2000, 960, 960, 0);
            var phrase = new HTSPhrase(new[] { note0, note1 });
            note0.parent = phrase;
            note1.parent = phrase;
            note0.index = 1;
            note1.index = 2;
            note0.indexBackwards = 2;
            note1.indexBackwards = 1;
            note0.next = note1;
            note1.prev = note0;
            note0.sentenceDurMs = 2000;
            note1.sentenceDurMs = 2000;
            note0.sentenceDurTicks = 1920;
            note1.sentenceDurTicks = 1920;

            phrase.UpdateResolution(960);

            var e1 = note1.e();
            Assert.Equal("24", e1[13]);
            Assert.Equal("24", e1[21]);
        }

        [Fact]
        public void RestNoteMasksPitchFields() {
            var rest = MakeNote(0, 500, 0, 480, 0);
            rest.isRest = true;
            rest.tone = 0;

            var phrase = BuildPhrase(new[] { rest }, 480);
            var e = rest.e();

            Assert.Equal("xx", e[0]);
            Assert.Equal("xx", e[1]);
            Assert.Equal("xx", e[56]);
            Assert.Equal("xx", e[57]);
        }

        [Fact]
        public void PitchDifferenceToRestNeighborsUsesXx() {
            var restStart = MakeNote(0, 500, 0, 480, 0);
            restStart.isRest = true;
            restStart.tone = 0;
            var note = MakeNote(500, 1000, 480, 480, 0);
            var restEnd = MakeNote(1000, 1500, 960, 480, 0);
            restEnd.isRest = true;
            restEnd.tone = 0;

            var phrase = BuildPhrase(new[] { restStart, note, restEnd }, 480);
            var e = note.e();

            Assert.Equal("xx", e[56]);
            Assert.Equal("xx", e[57]);
        }

        [Fact]
        public void AlignTimingPositionsFollowsAnchorPoints() {
            var durations = new[] { 20d, 10d, 30d };
            var alignPoints = new[] {
                Tuple.Create(1, 100d),
                Tuple.Create(3, 160d),
            };

            var positions = HTSContextBuilder.AlignTimingPositions(durations, alignPoints);

            Assert.Equal(2, positions.Count);
            Assert.Equal(100d, positions[0]);
            Assert.Equal(115d, positions[1]);
        }

        [Fact]
        public void BuildAlignedNoteTimingResultReturnsNoteRelativeTicks() {
            var result = HTSContextBuilder.BuildAlignedNoteTimingResult(
                new[] { "pau", "a", "b", "c" },
                1,
                4,
                new[] { 80d, 100d, 120d },
                50d,
                (start, end) => (int)Math.Round(end - start));

            Assert.Equal(3, result.Count);
            Assert.Equal(Tuple.Create("a", 30), result[0]);
            Assert.Equal(Tuple.Create("b", 50), result[1]);
            Assert.Equal(Tuple.Create("c", 70), result[2]);
        }
    }
}
