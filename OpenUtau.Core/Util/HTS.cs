using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

//This file implement utaupy.hts python library's function
//https://github.com/oatsu-gh/utaupy/hts.py

//HTS labels use b instead of #
//In HTS labels, "xx" is a preserved keyword that means null
namespace OpenUtau.Core.Util {
    public static class HTS {
        public static readonly string[] KeysInOctave = {
            "C",
            "Db",
            "D",
            "Eb",
            "E",
            "F",
            "Gb",
            "G",
            "Ab",
            "A",
            "Bb",
            "B" ,
        };

        public static readonly Dictionary<string, int> NameInOctave = new Dictionary<string, int> {
            { "C", 0 }, { "C#", 1 }, { "Db", 1 },
            { "D", 2 }, { "D#", 3 }, { "Eb", 3 },
            { "E", 4 },
            { "F", 5 }, { "F#", 6 }, { "Gb", 6 },
            { "G", 7 }, { "G#", 8 }, { "Ab", 8 },
            { "A", 9 }, { "A#", 10 }, { "Bb", 10 },
            { "B", 11 },
        };

        public static string GetToneName(int noteNum) {
            return noteNum < 0 ? string.Empty : KeysInOctave[noteNum % 12] + (noteNum / 12 - 1).ToString();
        }

        public static string GetOctaveNum(int noteNum) {
            return noteNum < 0 ? string.Empty : (noteNum / 12 - 1).ToString();
        }

        //return -1 if error
        public static int NameToTone(string name) {
            if (name.Length < 2) {
                return -1;
            }
            var str = name.Substring(0, (name[1] == '#' || name[1] == 'b') ? 2 : 1);
            var num = name.Substring(str.Length);
            if (!int.TryParse(num, out int octave)) {
                return -1;
            }
            if (!NameInOctave.TryGetValue(str, out int inOctave)) {
                return -1;
            }
            return 12 * (octave + 1) + inOctave;
        }

        public static string WriteInt(int integer) {
            return (integer >= 0 ? "p" : "m") + Math.Abs(integer).ToString();
        }
    }

    public static class HTSContextBuilder {
        public static bool HasPauseLikePhoneme(IEnumerable<string> symbols, Func<string, bool> isPauseLike) {
            return symbols.Any(symbol => isPauseLike(symbol.ToLowerInvariant()));
        }

        public static HTSNote BuildNote(
                string[] symbols,
                int tone,
                bool isSlur,
                string lang,
                int key,
                TimeAxis timeAxis,
                int noteStartTick,
                int noteEndTick,
                int phraseStartTick,
                int startMsOffset,
                Func<string, bool> isPauseLike) {
            UTimeSignature sig = timeAxis.TimeSignatureAtTick(noteStartTick);
            timeAxis.TickPosToBarBeat(noteStartTick, out int bar, out int beat, out int _);
            var isRest = HasPauseLikePhoneme(symbols, isPauseLike);
            return new HTSNote(
                symbols: symbols,
                tone: tone,
                isSlur: isSlur,
                isRest: isRest,
                lang: isRest ? string.Empty : lang,
                accent: string.Empty,
                beatPerBar: sig.beatPerBar,
                beatUnit: sig.beatUnit,
                positionBar: bar,
                positionBeat: beat,
                key: key,
                bpm: timeAxis.GetBpmAtTick(noteStartTick),
                startms: (int)timeAxis.MsBetweenTickPos(phraseStartTick, noteStartTick) + startMsOffset,
                endms: (int)timeAxis.MsBetweenTickPos(phraseStartTick, noteEndTick) + startMsOffset,
                positionTicks: noteStartTick,
                durationTicks: noteEndTick - noteStartTick);
        }

        public static int FindFirstVowelIndex(IReadOnlyList<string> symbols, Func<string, bool> isVowel) {
            for (int i = 0; i < symbols.Count; i++) {
                if (isVowel(symbols[i])) {
                    return i;
                }
            }
            return 0;
        }

        public static List<double> AlignTimingPositions(
                IReadOnlyList<double> durationsMs,
                IReadOnlyList<Tuple<int, double>> phAlignPoints) {
            var positions = new List<double>();
            if (durationsMs.Count == 0 || phAlignPoints.Count == 0) {
                return positions;
            }
            var firstCount = Math.Max(0, phAlignPoints[0].Item1 - 1);
            var initialGroup = durationsMs.Take(firstCount).ToList();
            positions.AddRange(Stretch(initialGroup, 1, phAlignPoints[0].Item2));
            foreach (var pair in phAlignPoints.Zip(phAlignPoints.Skip(1), Tuple.Create)) {
                var currAlignPoint = pair.Item1;
                var nextAlignPoint = pair.Item2;
                var count = nextAlignPoint.Item1 - currAlignPoint.Item1;
                if (count <= 0) {
                    continue;
                }
                var alignGroup = durationsMs.Skip(currAlignPoint.Item1).Take(count).ToList();
                if (alignGroup.Count == 0) {
                    continue;
                }
                var sum = alignGroup.Sum();
                var ratio = sum == 0 ? 0 : (nextAlignPoint.Item2 - currAlignPoint.Item2) / sum;
                positions.AddRange(Stretch(alignGroup, ratio, nextAlignPoint.Item2));
            }
            return positions;
        }

        public static List<Tuple<string, int>> BuildAlignedNoteTimingResult(
                IReadOnlyList<string> phonemes,
                int startIndex,
                int endIndex,
                IReadOnlyList<double> positionsMs,
                double notePosMs,
                Func<double, double, int> ticksBetweenMsPos) {
            var noteResult = new List<Tuple<string, int>>();
            for (int phIndex = startIndex; phIndex < endIndex; ++phIndex) {
                if (phIndex < 0 || phIndex >= phonemes.Count) {
                    continue;
                }
                var phoneme = phonemes[phIndex];
                if (string.IsNullOrEmpty(phoneme)) {
                    continue;
                }
                var positionIndex = phIndex - 1;
                if (positionIndex < 0 || positionIndex >= positionsMs.Count) {
                    continue;
                }
                noteResult.Add(Tuple.Create(
                    phoneme,
                    ticksBetweenMsPos(notePosMs, positionsMs[positionIndex])));
            }
            return noteResult;
        }

        public static List<double> Stretch(IList<double> source, double ratio, double endPos) {
            double startPos = endPos - source.Sum() * ratio;
            var result = CumulativeSum(source.Select(x => x * ratio).Prepend(0), startPos).ToList();
            result.RemoveAt(result.Count - 1);
            return result;
        }

        public static IEnumerable<double> CumulativeSum(IEnumerable<double> sequence, double start = 0) {
            double sum = start;
            foreach (var item in sequence) {
                sum += item;
                yield return sum;
            }
        }
    }

    public class HTSPhoneme {
        public string symbol;
        public string flag1 = "xx";
        public string flag2 = "xx";

        //Links to this phoneme's neighbors and parent
        public HTSPhoneme? prev;
        public HTSPhoneme? next;
        public HTSNote parent;

        //informations about this phoneme
        //v:vowel, c:consonant, p:pause, s:silence, b:break
        public string type = "xx";
        //(number of phonemes before this phoneme in this note) + 1
        public int position = 1;
        //(number of phonemes after this phoneme in this note) + 1
        public int position_backward = 1;
        //Here -1 means null
        //distances to vowels in this note, -1 for vowels themselves
        public int prev_vowel_distance = 0;
        public int next_vowel_distance = 0;

        public HTSPhoneme(string phoneme, HTSNote note) {
            this.symbol = phoneme;
            this.parent = note;
        }

        public HTSPhoneme? beforePrev {
            get {
                if (prev == null) { return null; } else { return prev.prev; }
            }
        }

        public HTSPhoneme? afterNext {
            get {
                if (next == null) { return null; } else { return next.next; }
            }
        }

        public string dump() {
            //Write phoneme as an HTS line
            // 100ns単位出力時にintオーバーフローを避けるためlongへ
            string result =
                $"{(long)Math.Round(parent.startMs * 10000.0)} {(long)Math.Round(parent.endMs * 10000.0)} "
                //Phoneme informations
                + string.Format("{0}@{1}^{2}-{3}+{4}={5}_{6}%{7}^{8}_{9}~{10}-{11}!{12}[{13}${14}]{15}", p())
                //Syllable informations
                + string.Format("/A:{0}-{1}-{2}@{3}~{4}", a())
                + string.Format("/B:{0}_{1}_{2}@{3}|{4}", b())
                + string.Format("/C:{0}+{1}+{2}@{3}&{4}", c())
                //Note informations
                + string.Format("/D:{0}!{1}#{2}${3}%{4}|{5}&{6};{7}-{8}", d())
                + string.Format(
                    "/E:{0}]{1}^{2}={3}~{4}!{5}@{6}#{7}+{8}]{9}${10}|{11}[{12}&{13}]{14}={15}^{16}~{17}#{18}_{19};{20}${21}&{22}%{23}[{24}|{25}]{26}-{27}^{28}+{29}~{30}={31}@{32}${33}!{34}%{35}#{36}|{37}|{38}-{39}&{40}&{41}+{42}[{43};{44}]{45};{46}~{47}~{48}^{49}^{50}@{51}[{52}#{53}={54}!{55}~{56}+{57}!{58}^{59}",
                    e())
                + string.Format("/F:{0}#{1}#{2}-{3}${4}${5}+{6}%{7};{8}", f())
                + string.Format("/G:{0}_{1}", g())
                + string.Format("/H:{0}_{1}", h())
                + string.Format("/I:{0}_{1}", i())
                + string.Format("/J:{0}~{1}@{2}", j())
                ;
            return result;
        }

        public string[] p() {
            var result = Enumerable.Repeat("xx", 16).ToArray();
            result[0] = type;
            result[1] = (beforePrev == null) ? "xx" : beforePrev.symbol;
            result[2] = (prev == null) ? "xx" : prev.symbol;
            result[3] = symbol;
            result[4] = (next == null) ? "xx" : next.symbol;
            result[5] = (afterNext == null) ? "xx" : afterNext.symbol;
            result[6] = (beforePrev == null) ? "xx" : beforePrev.flag1;
            result[7] = (prev == null) ? "xx" : prev.flag1;
            result[8] = flag1;
            result[9] = (next == null) ? "xx" : next.flag1;
            result[10] = (afterNext == null) ? "xx" : afterNext.flag1;
            result[11] = position.ToString();
            result[12] = position_backward.ToString();
            result[13] = prev_vowel_distance == 0 ? "xx" : prev_vowel_distance.ToString();
            result[14] = next_vowel_distance == 0 ? "xx" : next_vowel_distance.ToString();
            result[15] = flag2;

            return result;
        }

        public string[] a() {
            return parent.a();
        }

        public string[] b() {
            return parent.b();
        }

        public string[] c() {
            return parent.c();
        }

        public string[] d() {
            return parent.d();
        }

        public string[] e() {
            return parent.e();
        }

        public string[] f() {
            return parent.f();
        }

        public string[] g() {
            return parent.g();
        }

        public string[] h() {
            return parent.h();
        }

        public string[] i() {
            return parent.i();
        }

        public string[] j() {
            return parent.j();
        }
    }

    // TODO: Keep HTS note-context generation centralized here.
    // Remaining E-context slots that stay "xx" today should only be filled after
    // their HTS/NEUTRINO semantics are confirmed against the target implementation.
    public class HTSNote {
        public double startMs = 0;
        public double endMs = 0;
        public int positionTicks;
        public int durationTicks = 0;
        public int index = 0;//index of this note in sentence
        public int indexBackwards = 0;
        public double sentenceDurMs = 0;
        public int sentenceDurTicks = 0;
        public double startMsPercent = 0;

        //TimeSignatures
        public int beatPerBar = 0;
        public int beatUnit = 0;

        public int positionBar = 1; //bar number in the sentence, starting from 1
        public int positionBeat = 1; //unit number in the bar, starting from 1

        public double key = 0;
        public double bpm = 0;
        public int tone = 0;
        public bool isSlur = false;
        public bool isRest = true;
        public string[] symbols;
        public string lang = string.Empty;
        public string langDependent = "xx";
        public string accent = string.Empty;

        public HTSNote? prev;
        public HTSNote? next;
        public HTSPhrase parent;

        public HTSNote(string[] symbols, int beatPerBar, int beatUnit, int positionBar, int positionBeat, int key, double bpm, int tone, bool isSlur, bool isRest, string lang, string accent, double startms, double endms, int positionTicks, int durationTicks) {
            this.startMs = startms;
            this.endMs = endms;
            this.beatPerBar = beatPerBar;
            this.beatUnit = beatUnit;
            this.positionBar = positionBar;
            this.positionBeat = positionBeat;
            this.key = key;
            this.bpm = bpm;
            this.tone = tone;
            this.isSlur = isSlur;
            this.isRest = isRest;
            this.lang = lang;
            this.accent = accent;
            this.symbols = symbols;
            this.positionTicks = positionTicks;
            this.durationTicks = durationTicks;
        }

        public double durationMs {
            get { return endMs - startMs; }
        }

        private double startMsBackwards {
            get { return sentenceDurMs - startMs; }
        }

        private int positionTickBackwards {
            get { return sentenceDurTicks - positionTicks; }
        }


        public int? measureIndexForward;
        public double? measureMsForward;
        public int? measureTickForward;
        public int? measurePercentForward;
        public int? measureIndexBackward;
        public double? measureMsBackward;
        public int? measureTickBackward;
        public int? measurePercentBackward;

        public int? accentIndexForward;
        public double? accentMsForward;
        public int? accentTickForward;
        public int? accentIndexBackward;
        public double? accentMsBackward;
        public int? accentTickBackward;

        public string[] a() {
            if (prev == null) {
                return Enumerable.Repeat("xx", 5).ToArray();
            } else if (prev.isRest) {
                return Enumerable.Repeat("xx", 5).ToArray();
            } else {
                return prev.b();
            }
        }

        public string[] b() {
            return new string[] {
                symbols.Length.ToString(),
                "1",
                "1",
                lang != string.Empty ? lang : "xx",
                langDependent,
            };
        }

        public string[] c() {
            if (next == null) {
                return Enumerable.Repeat("xx", 5).ToArray();
            } else if (next.isRest) {
                return Enumerable.Repeat("xx", 5).ToArray();
            } else {
                return next.b();
            }
        }

        public string[] d() {
            if (prev == null) {
                return Enumerable.Repeat("xx", 60).ToArray();
            } else if (prev.isRest) {
                return Enumerable.Repeat("xx", 60).ToArray();
            } else {
                return prev.e();
            }
        }

        public string[] e() {
            var result = Enumerable.Repeat("xx", 60).ToArray();
            result[0] = isRest ? "xx" : HTS.GetToneName(tone);
            result[1] = isRest ? "xx" : HTS.GetOctaveNum(tone);
            result[2] = ((int)Math.Round(key)).ToString();
            result[3] = $"{beatPerBar}/{beatUnit}";
            result[4] = ((int)Math.Round(bpm)).ToString();
            result[5] = "1";

            int lengthCs = Math.Max(0, (int)Math.Round(durationMs / 10.0));
            int ticksPer96th = (parent != null && parent.resolution > 0) ? parent.resolution / 24 : 0;
            int length96 = (ticksPer96th > 0) ? (int)Math.Round((double)durationTicks / ticksPer96th) : 0;
            result[6] = lengthCs.ToString();
            result[7] = length96.ToString();

            result[9] = measureIndexForward != null ? measureIndexForward.ToString() : "xx";   // e10
            result[10] = measureIndexBackward != null ? measureIndexBackward.ToString() : "xx"; // e11
            result[11] = measureMsForward != null ? ((int)Math.Round(measureMsForward.Value)).ToString() : "xx";         // e12 (centisecond already)
            result[12] = measureMsBackward != null ? ((int)Math.Round(measureMsBackward.Value)).ToString() : "xx";       // e13
            result[13] = measureTickForward != null ? measureTickForward.ToString() : "xx";     // e14 (96th already)
            result[14] = measureTickBackward != null ? measureTickBackward.ToString() : "xx";   // e15
            result[15] = measurePercentForward != null ? measurePercentForward.ToString() : "xx"; // e16
            result[16] = measurePercentBackward != null ? measurePercentBackward.ToString() : "xx"; // e17

            if (!isRest) {
                result[17] = index <= 0 ? "xx" : index.ToString();
                result[18] = indexBackwards <= 0 ? "xx" : indexBackwards.ToString();
                result[19] = ((int)Math.Round(startMs / 10)).ToString(); // 10ms単位
                result[20] = ((int)Math.Round(startMsBackwards / 10)).ToString();

                // e22/e23: phrase-level position by 96th note, resolution independent
                if (ticksPer96th > 0 && parent != null && parent.notes != null && index > 0) {
                    int firstPhraseTick = parent.notes
                        .Select(note => note.positionTicks)
                        .DefaultIfEmpty(positionTicks)
                        .Min();
                    int lastPhraseTick = parent.notes
                        .Select(note => note.positionTicks)
                        .DefaultIfEmpty(positionTicks)
                        .Max();
                    int forwardTicks = Math.Max(0, positionTicks - firstPhraseTick);
                    int backwardTicks = Math.Max(0, lastPhraseTick - positionTicks);
                    result[21] = ((forwardTicks + ticksPer96th / 2) / ticksPer96th).ToString();
                    result[22] = ((backwardTicks + ticksPer96th / 2) / ticksPer96th).ToString();
                } else {
                    result[21] = "xx";
                    result[22] = "xx";
                }

                int totalNotes = parent?.totalNotes ?? 0;
                if (totalNotes > 1) {
                    result[23] = ((index - 1) * 100 / (totalNotes - 1)).ToString();
                    result[24] = ((indexBackwards - 1) * 100 / (totalNotes - 1)).ToString();
                } else {
                    result[23] = "xx";
                    result[24] = "xx";
                }

            }

            if (prev != null) {
                result[25] = prev.isSlur && isSlur ? "1" : "0";
            } else {
                result[25] = "0";
            }
            if (next != null) {
                result[26] = next.isSlur && isSlur ? "1" : "0";
            } else {
                result[26] = "0";
            }
                result[27] = "n";
            result[28] = accentIndexBackward.HasValue ? accentIndexBackward.Value.ToString() : "xx";
            result[29] = accentIndexForward.HasValue ? accentIndexForward.Value.ToString() : "xx";
            result[30] = accentMsBackward.HasValue ? ((int)Math.Round(accentMsBackward.Value / 10.0)).ToString() : "xx";
            result[31] = accentMsForward.HasValue ? ((int)Math.Round(accentMsForward.Value / 10.0)).ToString() : "xx";
            result[32] = (accentTickBackward.HasValue && ticksPer96th > 0) ? ((int)Math.Round((double)accentTickBackward.Value / ticksPer96th)).ToString() : "xx";
            result[33] = (accentTickForward.HasValue && ticksPer96th > 0) ? ((int)Math.Round((double)accentTickForward.Value / ticksPer96th)).ToString() : "xx";

            // TODO: e34-e56 remain intentionally "xx" until OpenUtau adopts a
            // verified mapping for staccato / crescendo / decrescendo related
            // score-label contexts. Keep current behavior visible instead of
            // guessing values from timing-label-only information.

            if (!isRest && this.tone > 0) {
                result[56] = (prev == null || prev.isRest || prev.tone <= 0) ? "xx" : HTS.WriteInt(prev.tone - tone);
                result[57] = (next == null || next.isRest || next.tone <= 0) ? "xx" : HTS.WriteInt(next.tone - tone);
            } else {
                result[56] = "xx";
                result[57] = "xx";
            }
            return result;
        }

        public string[] f() {
            if (next == null) {
                return Enumerable.Repeat("xx", 60).ToArray();
            } else if (next.isRest) {
                return Enumerable.Repeat("xx", 60).ToArray();
            } else {
                return next.e();
            }
        }

        public string[] g() {
            //TODO Calculate using HTSPhrase
            if (prev != null) {
                if (isRest) {
                    return prev.h();
                }
            }
            return parent.g();
        }

        public string[] h() {
            // TODO Calculate using HTSPhrase
            if (isRest) {
                return Enumerable.Repeat("xx", 2).ToArray();
            }
            return parent.h();
        }

        public string[] i() {
            //TODO Calculate using HTSPhrase
            if (next != null) {
                if (isRest) {
                    return next.h();
                }
            }
            return parent.i();
        }

        public string[] j() {
            return parent.j();
        }
    }

    public class HTSPhrase {
        public int resolution = 480;
        public int totalPhrases;
        public int totalNotes;
        public int totalPhonemes;

        public HTSPhrase? prev;
        public HTSPhrase? next;
        public HTSNote[] notes;

        public HTSPhrase(HTSNote[] notes) {
            this.notes = notes;
            RecalculateDerivedContexts();
        }

        public void UpdateResolution(int resolution) {
            this.resolution = resolution;
            RecalculateDerivedContexts();
        }

        void RecalculateDerivedContexts() {
            foreach (var note in notes) {
                note.accentIndexForward = null;
                note.accentMsForward = null;
                note.accentTickForward = null;
                note.accentIndexBackward = null;
                note.accentMsBackward = null;
                note.accentTickBackward = null;
                note.measureIndexForward = null;
                note.measureMsForward = null;
                note.measureTickForward = null;
                note.measurePercentForward = null;
                note.measureIndexBackward = null;
                note.measureMsBackward = null;
                note.measureTickBackward = null;
                note.measurePercentBackward = null;
            }

            // アクセント（forward）
            int accentIndexForwardSum = 0;
            double accentMsForwardSum = 0;
            int accentTickForwardSum = 0;
            for (int i = 0; i < notes.Length; i++) {
                var note = notes[i];
                if (note.isRest) {
                    accentIndexForwardSum = 0;
                    accentMsForwardSum = 0;
                    accentTickForwardSum = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    note.accentIndexForward = 0;
                    note.accentMsForward = 0;
                    note.accentTickForward = 0;
                    
                    accentIndexForwardSum = 1;
                    accentMsForwardSum = note.durationMs;
                    accentTickForwardSum = note.durationTicks;
                } else {
                    if (accentIndexForwardSum != 0) {
                        note.accentIndexForward = accentIndexForwardSum;
                        accentIndexForwardSum += 1;
                    }
                    if (accentMsForwardSum != 0) {
                        note.accentMsForward = accentMsForwardSum;
                        accentMsForwardSum += note.durationMs;
                    }
                    if (accentTickForwardSum != 0) {
                        note.accentTickForward = accentTickForwardSum;
                        accentTickForwardSum += note.durationTicks;
                    }
                }
            }

            // アクセント（backward）
            int accentIndexBackwardSum = 0;
            double accentMsBackwardSum = 0;
            int accentTickBackwardSum = 0;
            int lastAccentIndexContribution = 0;
            double lastAccentMs = 0;
            int lastAccentTicks = 0;
            for (int i = notes.Length - 1; i >= 0; i--) {
                var note = notes[i];
                if (note.isRest) {
                    accentIndexBackwardSum = 0;
                    accentMsBackwardSum = 0;
                    accentTickBackwardSum = 0;
                    lastAccentIndexContribution = 0;
                    lastAccentMs = 0;
                    lastAccentTicks = 0;
                } else if (!string.IsNullOrEmpty(note.accent)) {
                    note.accentIndexBackward = Math.Max(0, accentIndexBackwardSum - lastAccentIndexContribution);
                    note.accentMsBackward = Math.Max(0, accentMsBackwardSum - lastAccentMs);
                    note.accentTickBackward = Math.Max(0, accentTickBackwardSum - lastAccentTicks);

                    lastAccentIndexContribution = 1;
                    lastAccentMs = note.durationMs;
                    lastAccentTicks = note.durationTicks;

                    accentIndexBackwardSum = 1;
                    accentMsBackwardSum = note.durationMs;
                    accentTickBackwardSum = note.durationTicks;
                } else {
                    if (accentIndexBackwardSum != 0) {
                        note.accentIndexBackward = accentIndexBackwardSum;
                        accentIndexBackwardSum += 1;
                    }
                    if (accentMsBackwardSum != 0) {
                        note.accentMsBackward = accentMsBackwardSum;
                        accentMsBackwardSum += note.durationMs;
                    }
                    if (accentTickBackwardSum != 0) {
                        note.accentTickBackward = accentTickBackwardSum;
                        accentTickBackwardSum += note.durationTicks;
                    }

                }
            }

            // 小節ごとのグルーピング（positionBar 基準）
            var groups = notes
                .GroupBy(n => n.positionBar)
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(n => n.positionTicks).ToList())
                .ToList();

            int ticksPer96th = (resolution > 0) ? (resolution / 24) : 0;

            foreach (var group in groups) {
                double totalDurationMs = group.Sum(n => n.durationMs);
                int totalDurationTicks = group.Sum(n => n.durationTicks);
                int totalNotesInMeasure = group.Count;
                // forward（小節先頭からの位置）
                double accMsF = 0;
                int accTicksF = 0;
                for (var noteIndex = 0; noteIndex < group.Count; noteIndex++) {
                    var note = group[noteIndex];
                    note.measureIndexForward = noteIndex + 1;
                    note.measureMsForward = (int)Math.Round(accMsF / 100.0);
                    note.measureTickForward = ticksPer96th > 0 ? (int)Math.Round((double)accTicksF / ticksPer96th) : 0;
                    note.measurePercentForward = totalNotesInMeasure > 1 ? (noteIndex * 100) / (totalNotesInMeasure - 1) : 0;

                    accMsF += note.durationMs;
                    accTicksF += note.durationTicks;
                }

                // backward
                double accMsB = 0;
                int accTicksB = 0;
                for (int noteIndex = group.Count - 1; noteIndex >= 0; --noteIndex) {
                    var note = group[noteIndex];
                    int backwardIndex = group.Count - noteIndex;
                    note.measureIndexBackward = backwardIndex;
                    note.measureMsBackward = (int)Math.Round(accMsB / 100.0);
                    note.measureTickBackward = ticksPer96th > 0 ? (int)Math.Round((double)accTicksB / ticksPer96th) : 0;
                    note.measurePercentBackward = totalNotesInMeasure > 1 ? ((backwardIndex - 1) * 100) / (totalNotesInMeasure - 1) : 0;

                    accMsB += note.durationMs;
                    accTicksB += note.durationTicks;
                }
            }
        }
        private int barCount {
            get { return notes[^1].positionBar - notes[0].positionBar + 1; }
        }

        public string[] g() {
            var result = Enumerable.Repeat("xx", 2).ToArray();
            if (prev == null) {
                return result;
            } else {
                return prev.h();
            }
        }

        public string[] h() {
            var result = Enumerable.Repeat("xx", 2).ToArray();
            result[0] = notes.Length.ToString();
            result[1] = notes.Select(note => note.symbols.Length).Sum().ToString();
            return result;
        }

        public string[] i() {
            var result = Enumerable.Repeat("xx", 2).ToArray();
            if (next == null) {
                return result;
            } else {
                return next.h();
            }
        }

        public string[] j() {
            var result = Enumerable.Repeat("xx", 3).ToArray();
            result[0] = (barCount > 0 ? (totalNotes / barCount).ToString() : "xx");
            result[1] = (barCount > 0 ? (totalPhonemes / barCount).ToString() : "xx");
            result[2] = totalPhrases.ToString();
            return result;
        }
    }
}
