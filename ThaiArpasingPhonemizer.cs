// ==========================================
// สร้างและตรวจสอบโดย DELTA SYNTH & Gemini AI
// ต้นฉบับโดย: OpenUtau Contributors
// เวอร์ชัน: 5.0 (เสถียรภาพสูง, เอื้อนอัตโนมัติ, กรองหน่วยเสียง, ลบตัวเลข)
// ==========================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Thai Arpasing Phonemizer v5.0", "TH Arpasing Delta", "DELTA SYNTH", language: "TH")]
    public class ThaiArpasingPhonemizer : Phonemizer {

        private USinger singer;

        // ==========================================
        // 🔤 ตารางสระ พยัญชนะ และคำควบกล้ำ (Arpasing)
        // ==========================================
        readonly string[] vowels = {
            "ia", "ua", "ay", "aw", "am",
            "ah", "ih", "uh", "eh", "oh",
            "ae", "ao", "er", "ue", "eu"
        };

        readonly string[] diphthongs = { "r", "l", "w" };

        // เรียงจากยาวไปสั้น (kh ก่อน k)
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
        // 🗺️ ตารางแปลงสระไทย → Arpasing
        // ==========================================
        private readonly Dictionary<string, string> VowelMapping = new Dictionary<string, string> {
            {"เcือะ", "ue"}, {"เcือx", "ue"}, {"แcะ", "ae"}, {"แcx", "ae"},
            {"เcอะ", "er"}, {"เcอ", "er"}, {"ไc", "ay"}, {"ใc", "ay"},
            {"เcาะ", "ao"}, {"cอx", "ao"}, {"cืx", "eu"}, {"cึx", "eu"},
            {"cือ", "eu"}, {"cะ", "ah"}, {"cัx", "ah"}, {"cาx", "ah"},
            {"cรรx", "ah"}, {"เcา", "aw"}, {"เcะ", "eh"}, {"เcx", "eh"},
            {"cิx", "ih"}, {"cีx", "ih"}, {"เcียะ", "ia"}, {"เcียx", "ia"},
            {"โcะ", "oh"}, {"โcx", "oh"}, {"cุx", "uh"}, {"cูx", "uh"},
            {"cัวะ", "ua"}, {"cัว", "ua"}, {"cำ", "am"}, {"เcิx", "er"}, {"เcิ", "er"}
        };

        // ==========================================
        // 🗺️ ตารางแปลงพยัญชนะต้น และตัวสะกด
        // ==========================================
        private readonly Dictionary<char, string> CMapping = new Dictionary<char, string> {
            {'ก', "k"}, {'ข', "kh"}, {'ค', "kh"}, {'ฆ', "kh"}, {'ฅ', "kh"}, {'ฃ', "kh"},
            {'จ', "j"}, {'ฉ', "ch"}, {'ช', "ch"}, {'ฌ', "ch"},
            {'ฎ', "d"}, {'ด', "d"}, {'ต', "t"}, {'ฏ', "t"},
            {'ถ', "th"}, {'ฐ', "th"}, {'ฑ', "th"}, {'ฒ', "th"}, {'ธ', "th"}, {'ท', "th"},
            {'บ', "b"}, {'ป', "p"}, {'พ', "ph"}, {'ผ', "ph"}, {'ภ', "ph"}, {'ฟ', "f"}, {'ฝ', "f"},
            {'ห', "h"}, {'ฮ', "h"}, {'ม', "m"}, {'น', "n"}, {'ณ', "n"},
            {'ร', "r"}, {'ล', "l"}, {'ฤ', "r"},
            {'ส', "s"}, {'ศ', "s"}, {'ษ', "s"}, {'ซ', "s"},
            {'ง', "g"}, {'ย', "y"}, {'ญ', "y"}, {'ว', "w"}, {'ฬ', "r"}
        };

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
        // ⚙️ SetSinger
        // ==========================================
        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        // ==========================================
        // 🔢 ลบตัวเลขอารบิก + ไทยออก
        // ==========================================
        private static readonly Regex NumberStripRegex =
            new Regex(@"[0-9\u0E50-\u0E59]+", RegexOptions.Compiled);
        private static string StripNumbers(string s) =>
            string.IsNullOrEmpty(s) ? s : NumberStripRegex.Replace(s, "");

        // ==========================================
        // 🔍 ตรวจสอบ Alias ในฐานข้อมูลเสียง OTO
        // ==========================================
        private bool IsValidAlias(string alias, Note note) {
            if (singer == null || string.IsNullOrEmpty(alias)) return false;
            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            return singer.TryGetMappedOto(alias, note.tone + attr.toneShift, attr.voiceColor, out _);
        }

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
        // 🎤 Process: ประมวลผลโน้ต → หน่วยเสียง Arpasing
        // ==========================================
        public override Result Process(Note[] notes, Note? prev, Note? next,
            Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {

            var note = notes[0];

            // 1. อ่านเนื้อร้อง ลบตัวเลขออก
            string baseLyric = StripNumbers(
                string.IsNullOrEmpty(note.phoneticHint)
                    ? note.lyric.Normalize()
                    : note.phoneticHint.Normalize());

            // 2. แยกพยางค์
            string[] syllables = SplitToSyllables(baseLyric);

            // 3. กระจาย Duration
            int total = notes.Sum(n => n.duration);
            int[] durations = new int[syllables.Length];
            int[] offsets   = new int[syllables.Length];
            DistributeDurations(notes, syllables.Length, total, ref durations, ref offsets);

            // 4. บริบทเริ่มต้น
            var phonemes = new List<Phoneme>();
            bool isStart = IsStartOfPhrase(prevNeighbour);
            string prevPhoneme = "-";
            if (!isStart && prevNeighbour.HasValue) {
                var pTh = ParseInput(prevNeighbour.Value.lyric);
                prevPhoneme = pTh.EndingConsonant ?? pTh.Vowel ?? "-";
            }

            // 5. วนประมวลผลแต่ละพยางค์
            for (int k = 0; k < syllables.Length; k++) {
                string syl = syllables[k];
                int dur    = durations[k];
                int offset = offsets[k];

                bool forceClose = false;
                if (syl.EndsWith("-") && syl.Length > 1) {
                    forceClose = true;
                    syl = syl.Substring(0, syl.Length - 1);
                }

                var (C, Dip, V, X) = ParseInput(syl);
                bool hasCluster = !string.IsNullOrEmpty(Dip);
                bool hasEnding  = !string.IsNullOrEmpty(X);
                bool isFirst = isStart && k == 0;
                bool isLast  = k == syllables.Length - 1 &&
                               (nextNeighbour == null || forceClose ||
                                nextNeighbour.Value.lyric == "-" ||
                                nextNeighbour.Value.lyric.ToLower() == "r");

                // Auto-Melisma: เอื้อนสระซ้ำ
                if (k > 0 && !string.IsNullOrEmpty(V) && string.IsNullOrEmpty(X) && string.IsNullOrEmpty(C)
                    && (V == prevPhoneme || prevPhoneme.EndsWith(V))) {
                    string mel = V + " " + V;
                    if (!IsValidAlias(mel, note)) mel = V;
                    if (IsValidAlias(mel, note)) {
                        phonemes.Add(new Phoneme { phoneme = mel, position = offset });
                        prevPhoneme = V;
                        continue;
                    }
                }

                // สร้างรายการ Alias พร้อมระบุประเภท (0=Onset, 1=Cluster, 2=Vowel, 3=VC, 4=End)
                var raw = new List<(string alias, int type)>();

                if (syl == "-") {
                    raw.Add(($"{prevPhoneme} -", 4));
                } else {
                    // lead: C หรือ V
                    string lead = C ?? V;
                    if (!string.IsNullOrEmpty(lead))
                        raw.Add((isFirst ? $"- {lead}" : $"{prevPhoneme} {lead}", 0));

                    if (!string.IsNullOrEmpty(C)) {
                        if (hasCluster) {
                            raw.Add(($"{C} {Dip}", 1));
                            if (!string.IsNullOrEmpty(V)) raw.Add(($"{Dip} {V}", 2));
                        } else if (!string.IsNullOrEmpty(V)) {
                            raw.Add(($"{C} {V}", 2));
                        }
                    }

                    if (hasEnding && !string.IsNullOrEmpty(V))
                        raw.Add(($"{V} {X}", 3));

                    if (isLast) {
                        string tail = X ?? V;
                        if (!string.IsNullOrEmpty(tail)) raw.Add(($"{tail} -", 4));
                    }
                }

                if (raw.Count == 0) raw.Add((string.IsNullOrEmpty(syl) ? "ah" : syl, 2));

                // กรอง alias ที่มีใน OTO จริง
                var valid = new List<(string alias, int type)>();
                foreach (var item in raw) {
                    if (IsValidAlias(item.alias, note)) { valid.Add(item); continue; }
                    // fallback: "A B" → "B"
                    var p = item.alias.Split(' ');
                    if (p.Length == 2 && p[0] != "-") {
                        string fb = p[1] == "-" ? p[0] : p[1];
                        if (IsValidAlias(fb, note)) { valid.Add((fb, item.type)); continue; }
                    }
                    // ข้าม
                }
                // last-resort fallback
                if (valid.Count == 0 && !string.IsNullOrEmpty(V) && IsValidAlias(V, note))
                    valid.Add((V, 2));

                // Emit phonemes ด้วย Timing ที่ปรับแล้วตามกฎเปอร์เซ็นต์
                for (int i = 0; i < valid.Count; i++) {
                    var item = valid[i];
                    int pos = ComputePosition(item.alias, item.type, dur, hasCluster);
                    phonemes.Add(new Phoneme { phoneme = item.alias, position = pos + offset });
                }

                prevPhoneme = X ?? V ?? "-";
            }

            return new Result { phonemes = phonemes.ToArray() };
        }

        // ==========================================
        // ⏱️ คำนวณตำแหน่ง Timing สำหรับแต่ละ Alias (อิงตามกฎเปอร์เซ็นต์ใหม่)
        // ==========================================
        private int ComputePosition(string alias, int type, int dur, bool hasCluster) {
            bool isDiffSinger = singer != null && singer.SingerType == USingerType.DiffSinger;
            bool isCVType = !alias.Contains(" "); // ถ้าเป็น phoneme แบบ C+V เดี่ยวๆ ไม่มีเว้นวรรค
            
            bool isException = isDiffSinger || isCVType;

            if (isException) {
                // Rule B: กฎยกเว้นสำหรับกลุ่ม C+V และ Diffsinger
                switch (type) {
                    case 0: // Onset (- C หรือ prevV C)
                        return hasCluster ? -(int)(dur * 0.15) : -(int)(dur * 0.10);
                    case 1: // Cluster (C Dip)
                        return -(int)(dur * 0.05);
                    case 2: // Vowel (C V หรือ Dip V)
                        return 0; // Vowel ไม่จำกัดพื้นที่ (เริ่มที่ 0)
                    case 3: // VC (ตัวสะกด)
                        return dur - (int)(dur * 0.30); // C ตัวสะกดใช้พื้นที่ 30%
                    case 4: // End (C - หรือ V -)
                        return dur;
                }
            } else {
                // Rule A: กฎมาตรฐานสำหรับ Thai Arpasing
                switch (type) {
                    case 0: // Onset
                        if (alias.StartsWith("- ")) {
                            return -(int)(dur * 0.20); // นำหน้าประโยค ใช้ความยาว 20% จากนอกมิดี้โน้ต
                        } else {
                            if (hasCluster) {
                                return -(int)(dur * 0.10); // คำควบกล้ำ CCV ใช้ไม่เกิน 10%
                            } else {
                                return -(int)(dur * 0.50); // พยัญชนะแม่ ก กา CV ใช้ 50% ขึ้นไป (ขยายแถบชมพู)
                            }
                        }
                    case 1: // Cluster (C Dip)
                        return -(int)(dur * 0.05); // ส่วนหนึ่งของคำควบกล้ำ (รวมแล้วไม่เกิน 10%)
                    case 2: // Vowel
                        return 0;
                    case 3: // VC (ตัวสะกด)
                        return dur - (int)(dur * 0.40); // ตัวสะกด VC ใช้ 40%
                    case 4: // End
                        return dur; // ท้ายประโยค 15% จากนอกมิดี้โน้ต (เริ่มที่ขอบโน้ตเพื่อลากออกไป)
                }
            }
            return 0;
        }

        // ==========================================
        // 📐 แยกพยางค์จากเนื้อร้อง
        // ==========================================
        private string[] SplitToSyllables(string lyric) {
            if (string.IsNullOrEmpty(lyric)) return new[] { lyric };
            if (lyric.Contains("ๆ")) {
                string bw = lyric.Replace("ๆ", "").Trim();
                if (!string.IsNullOrEmpty(bw)) return new[] { bw, bw };
            }
            if (ThaiDictionaryLoader.Dictionary.TryGetValue(lyric, out string mapped) && !string.IsNullOrEmpty(mapped)) {
                var parts = mapped.Split(new[] { ' ', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1) return parts;
            }
            return new[] { lyric };
        }

        // ==========================================
        // ⏱️ กระจาย Duration
        // ==========================================
        private void DistributeDurations(Note[] notes, int count, int total,
            ref int[] durations, ref int[] offsets) {
            if (notes.Length >= count) {
                for (int k = 0; k < count; k++) {
                    durations[k] = notes[k].duration;
                    offsets[k]   = notes[k].position - notes[0].position;
                }
                if (notes.Length > count)
                    durations[count - 1] = total - offsets[count - 1];
            } else {
                int cursor = 0;
                for (int k = 0; k < count; k++) {
                    int d = total / count;
                    if (k == count - 1) d = total - cursor;
                    durations[k] = Math.Max(1, d);
                    offsets[k]   = cursor;
                    cursor += d;
                }
            }
        }

        // ==========================================
        // 🔍 ตรวจสอบเริ่มต้นประโยค
        // ==========================================
        private static bool IsStartOfPhrase(Note? prev) {
            if (!prev.HasValue) return true;
            string lyr = prev.Value.lyric?.ToLower() ?? "";
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

            string C = null, Dip = null, V = null, X = null;

            foreach (var c in consonants)
                if (input.StartsWith(c) && (C == null || c.Length > C.Length)) C = c;

            int idx = C?.Length ?? 0;
            string rest = input.Substring(idx);

            foreach (var d in diphthongs)
                if (rest.StartsWith(d) && (Dip == null || d.Length > Dip.Length)) Dip = d;

            idx += Dip?.Length ?? 0;
            rest = input.Substring(idx);

            foreach (var v in vowels)
                if (rest.StartsWith(v) && (V == null || v.Length > V.Length)) V = v;

            foreach (var x in endingConsonants)
                if (input.EndsWith(x) && (X == null || x.Length > X.Length)) X = x;

            // ป้องกัน X ชนกับ V
            if (V != null && X != null && V.EndsWith(X)) X = null;

            return (C, Dip, V, X);
        }

        // ==========================================
        // 🔄 WordToPhonemes: แปลงคำไทย → สัทอักษร Arpasing
        // ==========================================
        public string WordToPhonemes(string input) {
            if (string.IsNullOrEmpty(input)) return input;
            input = input.Replace(" ", "");
            input = StripNumbers(input);

            if (ThaiDictionaryLoader.Dictionary.TryGetValue(input, out string mapped)) return mapped;

            // คำข้อยกเว้น
            input = input
                .Replace("ก็", "ก้อ")
                .Replace("บ่", "บ่อ")
                .Replace("ฤทธิ์", "ริด")
                .Replace("อังกฤษ", "อังกิด");

            input = input.Replace("\u0E40\u0E40", "\u0E41");
            // ลบการันต์และวรรณยุกต์
            input = Regex.Replace(input, @"[ก-ฮ][ิุ]?์", "");
            input = Regex.Replace(input, @"[ก-ฮ]์", "");
            input = Regex.Replace(input, @"[่้๊๋็]", "");
            input = NumberStripRegex.Replace(input, "");

            if (!Regex.IsMatch(input, @"[ก-ฮ]")) return input;

            foreach (var kv in VowelMapping) {
                string pattern = "^" + kv.Key
                    .Replace("c", @"([ก-ฮ][ลรว]?|อ[ย]?|ห[ก-ฮ]?)")
                    .Replace("x", @"([ก-ฮ]*)") + "$";
                var m = Regex.Match(input, pattern);
                if (!m.Success) continue;

                string c = m.Groups[1].Value;
                string x = m.Groups.Count > 2 ? m.Groups[2].Value : "";

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

                if (x.Length > 1) x = x.Substring(0, 1);
                if (c.Length >= 2 && (c.StartsWith("ห") || c.StartsWith("อ")))
                    c = c.Substring(1);

                string cc = ConvertC(c);
                string xc = ConvertX(x);

                if (kv.Value == "ah" && input.Contains("ั") && x == "ว") return cc + "ua";
                if (kv.Value == "eh" && x == "ย") return cc + "er" + xc;
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
                    string cs = input[0].ToString(), xs = input.Substring(1);
                    if (input.Length >= 3 && clusters.Contains(input.Substring(0, 2))) {
                        cs = input.Substring(0, 2);
                        xs = input.Substring(2);
                    }
                    if (xs.Length > 1) xs = xs.Substring(0, 1);
                    return ConvertC(cs) + "oh" + ConvertX(xs);
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
    }
}