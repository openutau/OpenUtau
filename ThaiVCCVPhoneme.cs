#nullable disable
// ==========================================
// สร้างและตรวจสอบโดย DELTA SYNTH & Gemini AI
// ต้นฉบับโดย: OpenUtau Contributors, Ferina และ Printmov
// เวอร์ชัน: 13.0 (เสถียรภาพสูง, เอื้อนอัตโนมัติ, กรองหน่วยเสียง, ลบตัวเลข)
// ==========================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Thai VCCV Phonemizer v13.0", "TH VCCV Delta", "DELTA SYNTH", language: "TH")]
    public class ThaiVCCVPhonemizer : Phonemizer {

        // ==========================================
        // 🔤 ตารางสระ พยัญชนะ และคำควบกล้ำ
        // ==========================================
        readonly string[] vowels = {
            "ia", "ua", "ay", "aw", "am",
            "6", "@", "3", "Q", "1",
            "a", "i", "u", "e", "o"
        };

        readonly string[] diphthongs = { "r", "l", "w" };

        // เรียงจากยาวไปสั้น เพื่อให้ match ถูกต้อง (kh ก่อน k)
        readonly string[] consonants = {
            "kh", "ph", "th", "ch",
            "b", "d", "f", "g", "h", "j", "k", "l", "m", "n",
            "p", "r", "s", "t", "w", "y"
        };

        readonly string[] endingConsonants = {
            "kh", "ph", "th", "ch",
            "b", "d", "f", "g", "h", "j", "k", "l", "m", "n",
            "p", "r", "s", "t", "w", "y"
        };

        // ==========================================
        // 🗺️ ตารางแปลงสระไทย → สัทอักษร VCCV
        // ==========================================
        private readonly Dictionary<string, string> VowelMapping = new Dictionary<string, string> {
            {"เcือะ", "6"}, {"เcือx", "6"}, {"แcะ", "@"}, {"แcx", "@"},
            {"เcอะ", "3"}, {"เcอ", "3"}, {"ไc", "ay"}, {"ใc", "ay"},
            {"เcาะ", "Q"}, {"cอx", "Q"}, {"cืx", "1"}, {"cึx", "1"},
            {"cือ", "1"}, {"cะ", "a"}, {"cัx", "a"}, {"cาx", "a"},
            {"cรรx", "a"}, {"เcา", "aw"}, {"เcะ", "e"}, {"เcx", "e"},
            {"cิx", "i"}, {"cีx", "i"}, {"เcียะ", "ia"}, {"เcียx", "ia"},
            {"โcะ", "o"}, {"โcx", "o"}, {"cุx", "u"}, {"cูx", "u"},
            {"cัวะ", "ua"}, {"cัว", "ua"}, {"cำ", "am"}, {"เcิx", "3"}, {"เcิ", "3"}
        };

        // ==========================================
        // 🗺️ ตารางแปลงพยัญชนะไทย → สัทอักษรพยัญชนะต้น
        // ==========================================
        private readonly Dictionary<char, string> CMapping = new Dictionary<char, string> {
            {'ก', "k"}, {'ข', "kh"}, {'ค', "kh"}, {'ฆ', "kh"}, {'ฅ', "kh"}, {'ฃ', "kh"},
            {'จ', "j"}, {'ฉ', "ch"}, {'ช', "ch"}, {'ฌ', "ch"},
            {'ฎ', "d"}, {'ด', "d"}, {'ต', "t"}, {'ฏ', "t"},
            {'ถ', "th"}, {'ฐ', "th"}, {'ฑ', "th"}, {'ฒ', "th"}, {'ธ', "th"}, {'ท', "th"},
            {'บ', "b"}, {'ป', "p"}, {'พ', "ph"}, {'ผ', "ph"}, {'ภ', "ph"}, {'ฟ', "f"}, {'ฝ', "f"},
            {'ห', "h"}, {'ฮ', "h"},
            {'ม', "m"}, {'น', "n"}, {'ณ', "n"}, {'ร', "r"}, {'ล', "l"}, {'ฤ', "r"},
            {'ส', "s"}, {'ศ', "s"}, {'ษ', "s"}, {'ซ', "s"},
            {'ง', "g"}, {'ย', "y"}, {'ญ', "y"}, {'ว', "w"}, {'ฬ', "r"}
        };

        // ==========================================
        // 🗺️ ตารางแปลงพยัญชนะไทย → สัทอักษรตัวสะกด
        // ==========================================
        private readonly Dictionary<char, string> XMapping = new Dictionary<char, string> {
            {'บ', "b"}, {'ป', "b"}, {'พ', "b"}, {'ฟ', "b"}, {'ภ', "b"},
            {'ด', "d"}, {'จ', "d"}, {'ช', "d"}, {'ซ', "d"}, {'ฎ', "d"}, {'ฏ', "d"}, {'ฐ', "d"},
            {'ฑ', "d"}, {'ฒ', "d"}, {'ต', "d"}, {'ถ', "d"}, {'ท', "d"}, {'ธ', "d"},
            {'ศ', "d"}, {'ษ', "d"}, {'ส', "d"},
            {'ก', "k"}, {'ข', "k"}, {'ค', "k"}, {'ฆ', "k"},
            {'ว', "w"}, {'ย', "y"}, {'ง', "g"}, {'ม', "m"},
            {'น', "n"}, {'ญ', "n"}, {'ณ', "n"}, {'ร', "n"}, {'ล', "n"}, {'ฬ', "n"}
        };

        // ==========================================
        // 📖 พจนานุกรมที่กำหนดเอง (words_th.txt)
        // ==========================================
        private USinger singer;

        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        // ==========================================
        // 🔢 ลบตัวเลขอารบิก (0-9) และเลขไทย (๐-๙)
        // ==========================================
        private static readonly Regex NumberStripRegex =
            new Regex(@"[0-9\u0E50-\u0E59]+", RegexOptions.Compiled);
        private static string StripNumbers(string s) =>
            string.IsNullOrEmpty(s) ? s : NumberStripRegex.Replace(s, "");

        // ==========================================
        // 🔍 ตรวจสอบว่า Alias มีอยู่จริงใน OTO หรือไม่
        // ==========================================
        private bool IsValidAlias(string alias, Note note) {
            if (singer == null || string.IsNullOrEmpty(alias)) return false;
            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            return singer.TryGetMappedOto(alias, note.tone + attr.toneShift, attr.voiceColor, out _);
        }

        // ==========================================
        // 🔍 ค้นหา OTO แบบลองหลายตัวเลือก
        // ==========================================
        private bool checkOtoUntilHit(string[] candidates, Note note, out UOto oto) {
            oto = default;
            if (singer == null) return false;
            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            foreach (var test in candidates) {
                if (!string.IsNullOrEmpty(test) &&
                    singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var hit)) {
                    oto = hit;
                    return true;
                }
            }
            return false;
        }

        // ==========================================
        // 🎤 Process: ประมวลผลโน้ตเพื่อสร้างหน่วยเสียง
        // ==========================================
        public override Result Process(Note[] notes, Note? prev, Note? next,
            Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {

            var note = notes[0];

            // 1. อ่านเนื้อร้อง (ใช้ Phonetic Hint ถ้ามี) และลบตัวเลขออก
            string baseLyric = StripNumbers(
                string.IsNullOrEmpty(note.phoneticHint)
                    ? note.lyric.Normalize()
                    : note.phoneticHint.Normalize());

            // 2. แยกพยางค์จากพจนานุกรมหรือไม้ยมก
            string[] syllables = SplitToSyllables(baseLyric);

            // 3. คำนวณ Duration และ Offset ของแต่ละพยางค์
            int totalDuration = notes.Sum(n => n.duration);
            int[] syllableDurations = new int[syllables.Length];
            int[] syllableOffsets = new int[syllables.Length];
            DistributeDurations(notes, syllables.Length, totalDuration,
                ref syllableDurations, ref syllableOffsets);

            // 4. กำหนดบริบทเริ่มต้น
            var phonemes = new List<Phoneme>();
            bool isStartGlobal = IsStartOfPhrase(prevNeighbour);
            var prevTh = prevNeighbour.HasValue
                ? ParseInput(prevNeighbour.Value.lyric)
                : default;

            // 5. วนประมวลผลแต่ละพยางค์
            for (int k = 0; k < syllables.Length; k++) {
                string syl = syllables[k];
                int dur = syllableDurations[k];
                int offset = syllableOffsets[k];

                var tests = BuildPhonemeList(syl, note, k, syllables, prevTh,
                    isStartGlobal, nextNeighbour, out var noteTh, out bool forceClose);

                // Auto-Melisma: เอื้อนสระเดิมซ้ำถ้าไม่มีพยัญชนะต้น
                if (TryAutoMelisma(k, noteTh, prevTh, tests, note, offset, phonemes)) {
                    prevTh = noteTh;
                    continue;
                }

                // กรองเฉพาะ alias ที่มีใน OTO
                var valid = FilterValidAliases(tests, noteTh, note);

                // คำนวณ Timing และเพิ่ม Phoneme
                EmitPhonemes(valid, noteTh, dur, offset, note, phonemes);

                prevTh = noteTh;
            }

            return new Result { phonemes = phonemes.ToArray() };
        }

        // ==========================================
        // 📐 แยกพยางค์จากเนื้อร้อง
        // ==========================================
        private string[] SplitToSyllables(string lyric) {
            if (string.IsNullOrEmpty(lyric)) return new[] { lyric };

            // ไม้ยมก (ๆ): ซ้ำคำก่อนหน้า
            if (lyric.Contains("ๆ")) {
                string bw = lyric.Replace("ๆ", "").Trim();
                if (!string.IsNullOrEmpty(bw)) return new[] { bw, bw };
            }

            // ค้นหาในพจนานุกรม
            if (ThaiDictionaryLoader.Dictionary.TryGetValue(lyric, out string mapped) && !string.IsNullOrEmpty(mapped)) {
                var parts = mapped.Split(new char[] { ' ', ',', '|' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1) return parts;
            }

            return new[] { lyric };
        }

        // ==========================================
        // ⏱️ กระจาย Duration ให้แต่ละพยางค์
        // ==========================================
        private void DistributeDurations(Note[] notes, int count, int total,
            ref int[] durations, ref int[] offsets) {
            if (notes.Length >= count) {
                // มีโน้ตพอ: ใช้ duration จากโน้ตจริง
                for (int k = 0; k < count; k++) {
                    durations[k] = notes[k].duration;
                    offsets[k] = notes[k].position - notes[0].position;
                }
                // โน้ตพยางค์สุดท้ายกินส่วนที่เหลือทั้งหมด
                if (notes.Length > count) {
                    durations[count - 1] = total - offsets[count - 1];
                }
            } else {
                // โน้ตไม่พอ: แบ่งเท่าๆ กัน
                int cursor = 0;
                for (int k = 0; k < count; k++) {
                    int d = total / count;
                    if (k == count - 1) d = total - cursor;
                    durations[k] = Math.Max(1, d);
                    offsets[k] = cursor;
                    cursor += d;
                }
            }
        }

        // ==========================================
        // 🔨 สร้างรายการ Alias สำหรับพยางค์หนึ่ง
        // ==========================================
        private List<string> BuildPhonemeList(string syl, Note note, int k,
            string[] syllables,
            (string Consonant, string Dipthong, string Vowel, string EndingConsonant) prevTh,
            bool isStartGlobal, Note? nextNeighbour,
            out (string Consonant, string Dipthong, string Vowel, string EndingConsonant) noteTh,
            out bool forceClose) {

            var tests = new List<string>();
            forceClose = false;

            // ตรวจสอบ force-close (ลงท้ายด้วย -)
            if (syl.EndsWith("-") && syl.Length > 1) {
                forceClose = true;
                syl = syl.Substring(0, syl.Length - 1);
            }

            // กรณีโน้ตเชื่อม (-)
            if (syl == "-") {
                noteTh = default;
                string endSound = prevTh.EndingConsonant ?? prevTh.Vowel;
                if (endSound != null &&
                    checkOtoUntilHit(new[] { endSound + " -", endSound + "-" }, note, out var t1))
                    tests.Add(t1.Alias);
                if (tests.Count == 0 &&
                    checkOtoUntilHit(new[] { "-" }, note, out var t2))
                    tests.Add(t2.Alias);
                return tests;
            }

            noteTh = ParseInput(syl);

            // โน้ตถัดไปในคำเดียวกัน หรือโน้ตถัดไปจริง
            var nextTh = (k < syllables.Length - 1)
                ? ParseInput(syllables[k + 1])
                : (nextNeighbour.HasValue ? ParseInput(nextNeighbour.Value.lyric) : default);

            bool isFirst = isStartGlobal && k == 0;
            bool isLast = (k == syllables.Length - 1) &&
                          (nextNeighbour == null || forceClose ||
                           nextNeighbour.Value.lyric == "-" ||
                           nextNeighbour.Value.lyric.ToLower() == "r");

            // 1️⃣ CV / CCV — พยัญชนะต้น
            if (noteTh.Consonant != null) {
                if (noteTh.Dipthong == null && noteTh.Vowel != null) {
                    if (checkOtoUntilHit(new[] {
                        noteTh.Consonant + noteTh.Vowel,
                        noteTh.Consonant + " " + noteTh.Vowel }, note, out var t))
                        tests.Add(t.Alias);
                } else if (noteTh.Dipthong != null && noteTh.Vowel != null) {
                    if (checkOtoUntilHit(new[] {
                        noteTh.Consonant + noteTh.Dipthong + noteTh.Vowel }, note, out var t)) {
                        tests.Add(t.Alias);
                    } else {
                        if (checkOtoUntilHit(new[] {
                            noteTh.Consonant + " " + noteTh.Dipthong,
                            noteTh.Consonant + noteTh.Dipthong,
                            noteTh.Consonant }, note, out t))
                            tests.Add(t.Alias);
                        if (checkOtoUntilHit(new[] {
                            noteTh.Dipthong + noteTh.Vowel }, note, out t))
                            tests.Add(t.Alias);
                    }
                }
            }

            // 2️⃣ VCCV Transition — V ต่อ V (ไม่มีพยัญชนะต้น)
            if (noteTh.Consonant == null && noteTh.Vowel != null) {
                string prevSound = prevTh.EndingConsonant ?? prevTh.Vowel;
                if (!string.IsNullOrEmpty(prevSound)) {
                    if (checkOtoUntilHit(new[] {
                        prevSound + " " + noteTh.Vowel,
                        prevSound + noteTh.Vowel }, note, out var t))
                        tests.Add(t.Alias);
                    else if (checkOtoUntilHit(new[] { noteTh.Vowel }, note, out t))
                        tests.Add(t.Alias);
                } else {
                    if (checkOtoUntilHit(new[] { noteTh.Vowel }, note, out var t))
                        tests.Add(t.Alias);
                }
            }

            // 3️⃣ VC — ตัวสะกด
            if (noteTh.EndingConsonant != null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new[] {
                    noteTh.Vowel + " " + noteTh.EndingConsonant,
                    noteTh.Vowel + noteTh.EndingConsonant }, note, out var t))
                    tests.Add(t.Alias);
                else if (checkOtoUntilHit(new[] { noteTh.EndingConsonant }, note, out t))
                    tests.Add(t.Alias);
            } else if (noteTh.Vowel != null && noteTh.EndingConsonant == null &&
                       nextTh.Consonant != null) {
                // เชื่อม V กับ C ตัวถัดไป
                if (checkOtoUntilHit(new[] {
                    noteTh.Vowel + " " + nextTh.Consonant,
                    noteTh.Vowel + nextTh.Consonant }, note, out var t))
                    tests.Add(t.Alias);
            }

            // 4️⃣ เริ่มประโยค — ใส่ [- C] หรือ [- V]
            if (isFirst && tests.Count >= 1) {
                if (checkOtoUntilHit(new[] { "- " + tests[0], "-" + tests[0] }, note, out var t)) {
                    tests[0] = t.Alias;
                } else if (noteTh.Consonant != null &&
                           checkOtoUntilHit(new[] {
                               "- " + noteTh.Consonant,
                               "-" + noteTh.Consonant }, note, out t)) {
                    tests.Insert(0, t.Alias);
                } else if (noteTh.Vowel != null &&
                           checkOtoUntilHit(new[] {
                               "- " + noteTh.Vowel,
                               "-" + noteTh.Vowel }, note, out t)) {
                    tests.Insert(0, t.Alias);
                }
            }

            // 5️⃣ ปิดประโยค — ใส่ [V -] เฉพาะสระปลาย
            if (isLast && noteTh.EndingConsonant == null && noteTh.Vowel != null) {
                if (checkOtoUntilHit(new[] {
                    noteTh.Vowel + " -",
                    noteTh.Vowel + "-" }, note, out var t))
                    tests.Add(t.Alias);
            }

            // Fallback สุดท้าย
            if (tests.Count == 0 &&
                checkOtoUntilHit(new[] { syl }, note, out var fb))
                tests.Add(fb.Alias);

            return tests;
        }

        // ==========================================
        // 🎵 Auto-Melisma: เอื้อนสระซ้ำ (V V)
        // ==========================================
        private bool TryAutoMelisma(int k,
            (string Consonant, string Dipthong, string Vowel, string EndingConsonant) noteTh,
            (string Consonant, string Dipthong, string Vowel, string EndingConsonant) prevTh,
            List<string> tests, Note note, int offset, List<Phoneme> phonemes) {

            if (k == 0 || noteTh.Consonant != null || string.IsNullOrEmpty(noteTh.Vowel)) return false;
            if (prevTh.EndingConsonant != null) return false;
            if (noteTh.Vowel != prevTh.Vowel) return false;
            if (tests.Count > 0) return false;

            string melisma = noteTh.Vowel + " " + noteTh.Vowel;
            if (!IsValidAlias(melisma, note)) melisma = noteTh.Vowel;
            if (!IsValidAlias(melisma, note)) return false;

            phonemes.Add(new Phoneme { phoneme = melisma, position = offset });
            return true;
        }

        // ==========================================
        // 🧹 กรองเฉพาะ Alias ที่มีใน OTO จริง
        // ==========================================
        private List<string> FilterValidAliases(List<string> tests,
            (string Consonant, string Dipthong, string Vowel, string EndingConsonant) noteTh,
            Note note) {
            var valid = new List<string>();
            foreach (var raw in tests) {
                if (IsValidAlias(raw, note)) {
                    valid.Add(raw);
                } else {
                    // พยายาม fallback: "A B" → "B"
                    var parts = raw.Split(' ');
                    if (parts.Length == 2 && parts[0] != "-") {
                        string fb = parts[1] == "-" ? parts[0] : parts[1];
                        if (IsValidAlias(fb, note)) { valid.Add(fb); continue; }
                    }
                    // ข้ามไปเลยถ้าไม่มีเลย
                }
            }
            // Last-resort fallback: ใช้สระเปล่า
            if (valid.Count == 0 && noteTh.Vowel != null && IsValidAlias(noteTh.Vowel, note))
                valid.Add(noteTh.Vowel);
            return valid;
        }

        // ==========================================
        // ⏱️ กำหนดตำแหน่ง Timing และเพิ่ม Phoneme
        // ==========================================
        private void EmitPhonemes(List<string> valid,
            (string Consonant, string Dipthong, string Vowel, string EndingConsonant) noteTh,
            int dur, int offset, Note note, List<Phoneme> phonemes) {

            bool hasCluster = noteTh.Dipthong != null;

            for (int i = 0; i < valid.Count; i++) {
                string alias = valid[i];
                int pos = ComputePosition(alias, noteTh, dur, hasCluster);
                pos = Math.Max(0, Math.Min(pos, Math.Max(0, dur - 10)));
                phonemes.Add(new Phoneme { phoneme = alias, position = pos + offset });
            }
        }

        // ==========================================
        // ⏱️ คำนวณตำแหน่งเริ่มต้นของแต่ละ Alias
        // ==========================================
        private int ComputePosition(string alias,
            (string Consonant, string Dipthong, string Vowel, string EndingConsonant) noteTh,
            int dur, bool hasCluster) {

            bool isEnd  = alias.EndsWith("-");
            bool isStart = alias.StartsWith("-");
            bool isVC = !isEnd && !isStart && alias.Contains(" ") &&
                        vowels.Any(v => alias.StartsWith(v + " "));
            if (!isVC && !isEnd && !isStart)
                isVC = vowels.Any(v => alias.StartsWith(v)) &&
                       endingConsonants.Any(c => alias.EndsWith(c));
            bool isCV = !isVC && !isEnd && !isStart && vowels.Any(v => alias.EndsWith(v));

            if (isStart)  return 0;
            if (isEnd)    return Math.Max((int)(dur * 0.85), dur - 30);
            if (isVC)     return (int)(dur * 0.55);          // VC เริ่มที่ 55%
            if (isCV) {
                if (hasCluster && alias.Contains(noteTh.Consonant ?? ""))
                    return (int)(dur * 0.05);
                if (hasCluster)
                    return (int)(dur * 0.12);
                return 0;
            }
            return 0;
        }

        // ==========================================
        // 🔍 ตรวจสอบว่าเป็นเริ่มต้นประโยคหรือไม่
        // ==========================================
        private static bool IsStartOfPhrase(Note? prevNeighbour) {
            if (!prevNeighbour.HasValue) return true;
            string lyr = prevNeighbour.Value.lyric?.ToLower() ?? "";
            return lyr == "-" || lyr == "r" || lyr == "";
        }

        // ==========================================
        // 🔬 ParseInput: แยกพยางค์ → C, Dip, V, X
        // ==========================================
        private (string Consonant, string Dipthong, string Vowel, string EndingConsonant)
            ParseInput(string input) {
            if (string.IsNullOrEmpty(input)) return default;
            input = WordToPhonemes(input);
            if (string.IsNullOrEmpty(input)) return default;

            string consonant = null, diphthong = null, vowel = null, endingConsonant = null;

            // พยัญชนะต้น (เรียงจากยาวสุด)
            foreach (var c in consonants)
                if (input.StartsWith(c) && (consonant == null || c.Length > consonant.Length))
                    consonant = c;

            int idx = consonant?.Length ?? 0;
            string rest = input.Substring(idx);

            // คำควบกล้ำ
            foreach (var d in diphthongs)
                if (rest.StartsWith(d) && (diphthong == null || d.Length > diphthong.Length))
                    diphthong = d;

            idx += diphthong?.Length ?? 0;
            rest = input.Substring(idx);

            // สระ (เรียงจากยาวสุด)
            foreach (var v in vowels)
                if (rest.StartsWith(v) && (vowel == null || v.Length > vowel.Length))
                    vowel = v;

            // ตัวสะกด (เรียงจากยาวสุด)
            foreach (var x in endingConsonants)
                if (input.EndsWith(x) && (endingConsonant == null || x.Length > endingConsonant.Length))
                    endingConsonant = x;

            // ป้องกัน endingConsonant ชนกับ vowel
            if (vowel != null && endingConsonant != null && vowel.EndsWith(endingConsonant))
                endingConsonant = null;

            return (consonant, diphthong, vowel, endingConsonant);
        }

        // ==========================================
        // 🔄 WordToPhonemes: แปลงคำไทย → สัทอักษร
        // ==========================================
        public string WordToPhonemes(string input) {
            if (string.IsNullOrEmpty(input)) return input;
            input = input.Replace(" ", "");
            input = StripNumbers(input);

            // ตรวจพจนานุกรมก่อน
            if (ThaiDictionaryLoader.Dictionary.TryGetValue(input, out string mapped)) return mapped;

            // คำข้อยกเว้นพิเศษ
            if (input == "ก็")  return "kQ";
            if (input == "ณ")   return "na";
            if (input == "ธ")   return "tha";
            if (input == "ฤ")   return "rue";
            if (input == "ฤๅ")  return "rue";

            // ปรับแก้ก่อน Regex
            input = input.Replace("\u0E40\u0E40", "\u0E41");
            input = RemoveInvalidLetters(input);

            if (!Regex.IsMatch(input, @"[ก-ฮ]")) return input;

            // จับคู่กับตาราง VowelMapping
            foreach (var kv in VowelMapping) {
                string pattern = "^" + kv.Key
                    .Replace("c", @"([ก-ฮ][ลรว]?|อ[ย]?|ห[ก-ฮ]?)")
                    .Replace("x", @"([ก-ฮ]?)") + "$";
                var m = Regex.Match(input, pattern);
                if (!m.Success) continue;

                string c = m.Groups[1].Value;
                string x = m.Groups.Count > 2 ? m.Groups[2].Value : "";

                // กฎ รร
                if (kv.Key == "cรรx" && x == "") x = "น";
                else if (kv.Key.EndsWith("x") && x == "") {
                    string[] trueClusters = {
                        "กร","กล","กว","ขร","ขล","ขว","คร","คล","คว",
                        "ปร","ปล","พร","พล","ตร","ผล","บร","บล","ฟร","ฟล","ดร","ทร"
                    };
                    if (!trueClusters.Contains(c) && c.Length > 1) {
                        x = c.Substring(c.Length - 1);
                        c = c.Substring(0, c.Length - 1);
                    }
                }

                if (c.Length >= 2 && (c.StartsWith("ห") || c.StartsWith("อ")))
                    c = c.Substring(1);

                string cc = ConvertC(c);
                string xc = ConvertX(x);

                // กรณีพิเศษ สระ a + ว = ua
                if (kv.Value == "a" && input.Contains("ั") && x == "ว") return cc + "ua";
                // กรณีพิเศษ เ + e + ย = 3 (เสียงเออ)
                if (kv.Value == "e" && x == "ย") return cc + "3" + xc;

                return cc + kv.Value + xc;
            }

            // คำสั้นไม่มีสระ
            if (!Regex.IsMatch(input, @"[ะาิีึืุูเแโไใโั]")) {
                if (input.Contains("ว") && input.Length >= 3) {
                    int wi = input.IndexOf('ว');
                    string xs = input.Substring(wi + 1);
                    if (xs.Length > 1) xs = xs.Substring(0, 1);
                    return ConvertC(input.Substring(0, wi)) + "ua" + ConvertX(xs);
                }
                if (input.Length >= 2) {
                    string[] clusters = {
                        "กร","กล","กว","ขร","ขล","ขว","คร","คล","คว","ปร","ปล",
                        "พร","พล","ตร","ผล","บร","บล","ฟร","ฟล","ดร","ทร",
                        "หง","หญ","หน","หม","หย","หร","หล","หว","อย"
                    };
                    string cs = input[0].ToString();
                    string xs = input.Substring(1);
                    if (input.Length >= 3 && clusters.Contains(input.Substring(0, 2))) {
                        cs = input.Substring(0, 2);
                        xs = input.Substring(2);
                    }
                    if (xs.Length > 1) xs = xs.Substring(0, 1);
                    return ConvertC(cs) + "o" + ConvertX(xs);
                }
                if (input.Length == 1) return ConvertC(input);
            }

            return input;
        }

        // ==========================================
        // 🔡 แปลงพยัญชนะต้น
        // ==========================================
        private string ConvertC(string input) {
            if (string.IsNullOrEmpty(input) || input == "อ") return "";
            if (input == "ทร" || input == "สร" || input == "ศร" || input == "ซร") return "s";
            if (input == "จร") return "j";
            if (input == "อย") return "y";
            if (input.Length >= 2 && (input.StartsWith("ห") || input.StartsWith("อ")))
                input = input.Substring(1);
            if (string.IsNullOrEmpty(input)) return "";
            char first = input[0];
            char? second = input.Length > 1 ? input[1] : (char?)null;
            if (CMapping.TryGetValue(first, out string fc)) {
                if (second.HasValue && CMapping.TryGetValue(second.Value, out string sc))
                    return fc + sc;
                return fc;
            }
            return input;
        }

        // ==========================================
        // 🔡 แปลงตัวสะกด
        // ==========================================
        private string ConvertX(string input) {
            if (string.IsNullOrEmpty(input)) return "";
            return XMapping.TryGetValue(input[0], out string r) ? r : input;
        }

        // ==========================================
        // 🧹 ลบตัวอักษรที่ไม่มีผลต่อเสียง
        // ==========================================
        private string RemoveInvalidLetters(string input) {
            // ลบการันต์ (์) พร้อมตัวอักษรประกอบ
            input = Regex.Replace(input, @"[ก-ฮ][ิุ]?์", "");
            input = Regex.Replace(input, @"[ก-ฮ]์", "");
            // ลบวรรณยุกต์
            input = Regex.Replace(input, @"[่้๊๋็]", "");
            // ลบตัวเลขที่หลุดมา
            input = NumberStripRegex.Replace(input, "");
            return input;
        }
    }
}