using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Vietnamese VCV Phonemizer", "VIE VCV", "Jani Tran", language: "VI")]
    public class VietnameseVCVPhonemizer : Phonemizer {
        static readonly Dictionary<char, string> Vowels = new() {
            ['a'] = "a", ['ă'] = "a", ['â'] = "A",
            ['e'] = "e", ['ê'] = "E",
            ['i'] = "i", ['y'] = "i",
            ['o'] = "o", ['ô'] = "O",
            ['ơ'] = "@",
            ['u'] = "u", ['ư'] = "U",
        };

        static readonly Dictionary<char, string> Consonants = new() {
            ['C'] = "ch",
            ['K'] = "kh",
            ['N'] = "ng",
            ['J'] = "nh",
            ['Z'] = "tr",
            ['T'] = "th",
        };

        static readonly HashSet<char> Initials = new() {
            'b', 'C', 'd', 'f', 'g', 'h', 'k', 'K', 'l',
            'm', 'n', 'N', 'J', 'r', 's', 't', 'T', 'Z',
            'v', 'w', 'z', 'p', 'y'
        };

        static readonly HashSet<char> Finals = new() {
            'k', 't', 'C', 'p', 'n', 'm', 'N', 'J'
        };

        static readonly string[] VvcEndings = {
            "iên", "iêN", "iêm", "iêt", "iêk", "iêp", "iêu",
            "yên", "yêN", "yêm", "yêt", "yêk", "yêp", "yêu",
            "uôn", "uôN", "uôm", "uôt", "uôk", "uôi",
            "ươn", "ươN", "ươm", "ươt", "ươk", "ươp", "ươi"
        };

        static readonly string[] ShortEndings = {
            "ai", "ơi", "oi", "ôi", "ui", "ưi",
            "ao", "eo", "êu", "iu", "uao", "ueo",
            "an", "ơn", "in", "en", "ên", "on", "ôn", "un", "ưn",
            "am", "ơm", "im", "em", "êm", "om", "ôm", "um", "ưm",
            "aN", "ơN", "iN", "eN", "êN", "ưN",
            "at", "ơt", "it", "et", "êt", "ot", "ôt", "ut", "ưt",
            "ak", "ơk", "ik", "ek", "êk", "ok", "ôk", "uk", "ưk",
            "ap", "ơp", "ip", "ep", "êp", "op", "ôp", "up", "ưp",
            "ia", "ua", "ưa", "uôN",
            "yt", "yn", "ym", "yC", "yp", "yk", "yN",
            "aJ", "iJ", "êJ", "yJ"
        };

        static readonly string[] LongEndings = {
            "ay", "ây", "uy", "au", "âu", "oa", "oe", "uê"
        };

        static readonly string[] MediumEndings = {
            "ăt", "ât", "ăk", "âk", "ăp", "âp",
            "ăn", "ân", "ăN", "âN", "ăm", "âm",
            "ôN", "uN", "oN",
            "aC", "iC", "êC", "yC"
        };

        static readonly Dictionary<string, string> vowelLookup;

        USinger singer;

        public override bool LegacyMapping => true;

        static VietnameseVCVPhonemizer() {
            vowelLookup = new Dictionary<string, string>();

            foreach (var c in "aàáảãạăằắẳẵặAÀÁẢÃẠĂẰẮẲẴẶ")
                vowelLookup[c.ToString()] = "a";

            foreach (var c in "âầấẩẫậÂẦẤẨẪẬ")
                vowelLookup[c.ToString()] = "A";

            foreach (var c in "ơờớởỡợƠỜỚỞỠỢ")
                vowelLookup[c.ToString()] = "@";

            foreach (var c in "iìíỉĩịyỳýỷỹỵIÌÍỈĨỊYỲÝỶỸỴ")
                vowelLookup[c.ToString()] = "i";

            foreach (var c in "eèéẻẽẹEÈÉẺẽẸ")
                vowelLookup[c.ToString()] = "e";

            foreach (var c in "êềếểễệÊỀẾỂỄỆ")
                vowelLookup[c.ToString()] = "E";

            foreach (var c in "oòóỏõọOÒÓỎÕỌ")
                vowelLookup[c.ToString()] = "o";

            foreach (var c in "ôồốổỗộÔỒỐỔỖỘ")
                vowelLookup[c.ToString()] = "O";

            foreach (var c in "uùúủũụUÙÚỦŨỤ")
                vowelLookup[c.ToString()] = "u";

            foreach (var c in "ưừứửữựƯỪỨỬỮỰ")
                vowelLookup[c.ToString()] = "U";

            vowelLookup["m"] = "m";
            vowelLookup["M"] = "m";
            vowelLookup["n"] = "n";
            vowelLookup["N"] = "ng";
            vowelLookup["g"] = "ng";
            vowelLookup["G"] = "ng";
            vowelLookup["h"] = "nh";
            vowelLookup["H"] = "nh";

            foreach (var c in "cCtTpPR12345")
                vowelLookup[c.ToString()] = "-";
        }

        public override void SetSinger(USinger singer) => this.singer = singer;

        public override Result Process(
            Note[] notes,
            Note? prev,
            Note? next,
            Note? prevNeighbour,
            Note? nextNeighbour,
            Note[] prevNeighbours) {

            var note = notes[0];

            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                return new Result {
                    phonemes = new[] {
                        new Phoneme { phoneme = note.phoneticHint }
                    }
                };
            }

            int duration = notes.Sum(n => n.duration);
            var time = Timing(duration);

            bool dotStart = note.lyric.StartsWith(".");
            bool nextDotStart = nextNeighbour != null && nextNeighbour.Value.lyric.StartsWith(".");

            bool prevIsBreath = prevNeighbour != null && prevNeighbour.Value.lyric.StartsWith("breath");

            bool noNext = (nextNeighbour == null && note.lyric != "R") || nextDotStart;
            bool breath = note.lyric.StartsWith("breath");

            string lyric = note.lyric;
            string loi = Normalize(lyric);

            if (loi == "quôc")
                loi = "quâc";

            var phonemes = new List<Phoneme>();

            bool prevHasOto = prevNeighbour != null && HasValidPrevOto(prevNeighbour.Value);

            if (prevNeighbour == null || !prevHasOto || dotStart || prevIsBreath) {
                Build(
                    phonemes,
                    loi,
                    lyric,
                    prefix: "-",
                    noNext,
                    time,
                    allowInitial: true);
            } else {
                string prefix = PreviousVowel(prevNeighbour);
                if (prefix != null) {
                    Build(
                        phonemes,
                        loi,
                        lyric,
                        prefix,
                        noNext,
                        time,
                        allowInitial: false);
                } else {
                    Build(
                        phonemes,
                        loi,
                        lyric,
                        prefix: "-",
                        noNext,
                        time,
                        allowInitial: true);
                }
            }

            if (breath && prevNeighbour == null) {
                AddBreath(phonemes, loi, time);
            }

            for (int i = 0; i < phonemes.Count; i++) {
                var p = phonemes[i];
                p.phoneme = DecodePhoneme(p.phoneme);
                phonemes[i] = p;
            }

            MapOto(phonemes, note, notes, prevNeighbours);

            return new Result {
                phonemes = phonemes.ToArray()
            };
        }

        private bool HasValidPrevOto(Note prevNote) {
            if (singer == null) return false;

            string lyric = prevNote.phoneticHint ?? prevNote.lyric ?? "";
            if (string.IsNullOrEmpty(lyric) || lyric == "R") return false;

            return true;
        }

        static TimingInfo Timing(int duration) {
            if (duration < 350) {
                return new TimingInfo(
                    shortPos: duration / 2,
                    longPos: duration / 6,
                    mediumPos: duration / 3,
                    endPos: duration * 4 / 5);
            }

            return new TimingInfo(
                shortPos: duration - 170,
                longPos: 90,
                mediumPos: 180,
                endPos: duration - 50);
        }

        static bool IsStopFinal(char c) {
            return c == 'p' || c == 't' || c == 'k' || c == 'C';
        }

        static bool IsNasalFinal(char c) {
            return c == 'n' || c == 'm' || c == 'N' || c == 'J';
        }

        static List<string> ToUnicodeElements(string input) {
            var result = new List<string>();
            if (string.IsNullOrEmpty(input)) return result;
            var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(input);
            while (enumerator.MoveNext()) {
                result.Add(enumerator.GetTextElement());
            }
            return result;
        }

        static string Normalize(string input) {
            if (string.IsNullOrEmpty(input))
                return "";

            string cleanDotInput = input.StartsWith(".") ? input[1..] : input;

            bool keepHardR = cleanDotInput.EndsWith("2");
            string cleanInput = keepHardR ? cleanDotInput[..^1] : cleanDotInput;

            string s = cleanInput == "R" ? cleanInput : cleanInput.ToLower();

            s = s
                .Replace('à', 'a').Replace('á', 'a').Replace('ả', 'a')
                .Replace('ã', 'a').Replace('ạ', 'a')
                .Replace('ằ', 'ă').Replace('ắ', 'ă').Replace('ẳ', 'ă')
                .Replace('ẵ', 'ă').Replace('ặ', 'ă')
                .Replace('ầ', 'â').Replace('ấ', 'â').Replace('ẩ', 'â')
                .Replace('ẫ', 'â').Replace('ậ', 'â')
                .Replace('ờ', 'ơ').Replace('ớ', 'ơ').Replace('ở', 'ơ')
                .Replace('ỡ', 'ơ').Replace('ợ', 'ơ')
                .Replace('ì', 'i').Replace('í', 'i').Replace('ỉ', 'i')
                .Replace('ĩ', 'i').Replace('ị', 'i')
                .Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỷ', 'y')
                .Replace('ỹ', 'y').Replace('ỵ', 'y')
                .Replace('è', 'e').Replace('é', 'e').Replace('ẻ', 'e')
                .Replace('ẽ', 'e').Replace('ẹ', 'e')
                .Replace('ề', 'ê').Replace('ế', 'ê').Replace('ể', 'ê')
                .Replace('ễ', 'ê').Replace('ệ', 'ê')
                .Replace('ò', 'o').Replace('ó', 'o').Replace('ỏ', 'o')
                .Replace('õ', 'o').Replace('ọ', 'o')
                .Replace('ồ', 'ô').Replace('ố', 'ô').Replace('ổ', 'ô')
                .Replace('ỗ', 'ô').Replace('ộ', 'ô')
                .Replace('ù', 'u').Replace('ú', 'u').Replace('ủ', 'u')
                .Replace('ũ', 'u').Replace('ụ', 'u')
                .Replace('ừ', 'ư').Replace('ứ', 'ư').Replace('ử', 'ư')
                .Replace('ữ', 'ư').Replace('ự', 'ư');

            bool giException =
                s == "gi" || s == "gin" || s == "gim" ||
                s == "ginh" || s == "ging" || s == "git" ||
                s == "gip" || s == "gic" || s == "gich";

            if (giException) {
                return s
                    .Replace("gi", "zi")
                    .Replace("ng", "N")
                    .Replace("nh", "J")
                    .Replace("ch", "C")
                    .Replace("c", "k");
            }

            string result = s
                .Replace("ch", "C")
                .Replace("d", "z")
                .Replace("đ", "d")
                .Replace("ph", "f")
                .Replace("gi", "z")
                .Replace("gh", "g")
                .Replace("ngh", "N")
                .Replace("ng", "N")
                .Replace("nh", "J")
                .Replace("tr", "Z")
                .Replace("th", "T")
                .Replace("kh", "K")
                .Replace("x", "s")
                .Replace("q", "k")
                .Replace("c", "k");

            if (!keepHardR) {
                result = result.Replace("r", "z");
            }

            return result;
        }

        static string DecodePhoneme(string phoneme) {
            if (string.IsNullOrEmpty(phoneme)) return phoneme;
            return phoneme
                .Replace("J", "nh")
                .Replace("N", "ng")
                .Replace("C", "ch")
                .Replace("K", "kh")
                .Replace("Z", "tr")
                .Replace("T", "th");
        }

        static void Build(
            List<Phoneme> output,
            string s,
            string original,
            string prefix,
            bool noNext,
            TimingInfo t,
            bool allowInitial) {

            if (string.IsNullOrEmpty(s))
                return;

            if (s == "R") {
                output.Add(new Phoneme {
                    phoneme = $"{prefix} R"
                });
                return;
            }

            bool hasInitial = Initials.Contains(s[0]);
            bool hasFinal = Finals.Contains(s[^1]);
            bool vvc = EndsWithAny(s, VvcEndings);
            bool shortPos = EndsWithAny(s, ShortEndings)
                           || s.EndsWith("uya") && original != "qua";
            bool longPos = EndsWithAny(s, LongEndings)
                           || original.EndsWith("qua")
                           || s == "ăm";
            bool mediumPos = EndsWithAny(s, MediumEndings);

            int pos = t.shortPos;
            if (mediumPos) pos = t.mediumPos;
            if (shortPos) pos = t.shortPos;
            if (longPos) pos = t.longPos;
            if (s.EndsWith("uôN")) pos = t.shortPos;

            if (s.Length >= 2 && s[0] == 'y' && Vowels.ContainsKey(s[1])) {
                string initial = "y";
                string vowel = EncodeV(s[1].ToString());

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {initial}{vowel}" : $"{prefix} {initial}{vowel}"
                });

                if (s.Length > 2) {
                    string rest = Encode(s.Substring(2));
                    output.Add(new Phoneme {
                        phoneme = $"{vowel} {rest}",
                        position = pos
                    });
                } else if (noNext) {
                    output.Add(new Phoneme {
                        phoneme = $"{vowel} -",
                        position = t.endPos
                    });
                }
                return;
            }

            if (s.Length == 1) {
                string n = Encode(s);

                if (allowInitial) {
                    output.Add(new Phoneme {
                        phoneme = $"- {n}"
                    });
                } else {
                    output.Add(new Phoneme {
                        phoneme = $"{prefix} {n}"
                    });
                }

                if (noNext) {
                    output.Add(new Phoneme {
                        phoneme = $"{n} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 2 && hasInitial) {
                string n = Encode(s);
                string v = EncodeV(s[1].ToString());

                output.Add(new Phoneme {
                    phoneme = allowInitial
                        ? $"- {n}"
                        : $"{prefix} {n}"
                });

                if (noNext) {
                    output.Add(new Phoneme {
                        phoneme = $"{v} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 2 && !hasInitial && !hasFinal) {
                string v1 = EncodeV(s[0].ToString());
                string v2 = EncodeV(s[1].ToString());

                if (s.EndsWith("ia") || s.EndsWith("ua") || s.EndsWith("ưa"))
                    v2 = "A";

                if (s == "oa") { v1 = "O"; v2 = "a"; } else if (s == "uy") { v1 = "O"; v2 = "i"; } else if (s == "oe") { v1 = "O"; v2 = "e"; } else if (s == "uê") { v1 = "O"; v2 = "E"; } else if (s == "oă") { v1 = "O"; v2 = "a"; } else if (s == "uâ") { v1 = "O"; v2 = "A"; } else if (original == "ao" || original == ".ao") { v1 = "a"; v2 = "O"; } else if (original == "eo" || original == ".eo") { v1 = "e"; v2 = "O"; }

                string tail = v2;
                if (s == "ôN" || s == "uN" || s == "oN")
                    tail = "m";

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {v1}" : $"{prefix} {v1}"
                });
                output.Add(new Phoneme {
                    phoneme = $"{v1} {v2}",
                    position = pos
                });

                if (noNext) {
                    output.Add(new Phoneme {
                        phoneme = $"{tail} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 2 && hasFinal) {
                string v = EncodeV(s[0].ToString());
                string c = EncodeFinal(s[1].ToString());
                bool isStop = IsStopFinal(s[1]);
                bool isNasal = IsNasalFinal(s[1]);

                if (s == "ăm") {
                    v = "O";
                }

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {v}" : $"{prefix} {v}"
                });
                output.Add(new Phoneme {
                    phoneme = isStop ? $"{v}{c}" : $"{v} {c}",
                    position = pos
                });

                if (noNext && isNasal) {
                    string finalTail = c;
                    if (s == "oN" || s == "ON" || s == "uN") {
                        finalTail = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{finalTail} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 3 && hasInitial && hasFinal) {
                string c = EncodeInitial(s[0].ToString());
                string v = EncodeV(s[1].ToString());
                string tail = EncodeFinal(s[2].ToString());
                bool isStop = IsStopFinal(s[2]);
                bool isNasal = IsNasalFinal(s[2]);

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {c}{v}" : $"{prefix} {c}{v}"
                });
                output.Add(new Phoneme {
                    phoneme = isStop ? $"{v}{tail}" : $"{v} {tail}",
                    position = pos
                });

                if (noNext && isNasal) {
                    string finalTail = tail;
                    if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                        finalTail = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{finalTail} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 3 && hasFinal && !hasInitial && !vvc) {
                string v1 = EncodeV(s[0].ToString());
                string v2 = EncodeV(s[1].ToString());
                string tail = EncodeFinal(s[2].ToString());
                bool isStop = IsStopFinal(s[2]);
                bool isNasal = IsNasalFinal(s[2]);

                if (s.StartsWith("oa") || s.StartsWith("oe") || s.StartsWith("oă") || s.StartsWith("uâ"))
                    v1 = "O";

                if (longPos)
                    pos = t.mediumPos;

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {v1}" : $"{prefix} {v1}"
                });
                output.Add(new Phoneme {
                    phoneme = $"{v1} {v2}",
                    position = t.longPos
                });
                output.Add(new Phoneme {
                    phoneme = isStop ? $"{v2}{tail}" : $"{v2} {tail}",
                    position = pos
                });

                if (noNext && isNasal) {
                    string finalTail = tail;
                    if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                        finalTail = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{finalTail} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 3 && !hasInitial && !vvc) {
                string v1 = EncodeV(s[0].ToString());
                string v2 = EncodeV(s[1].ToString());
                string v3 = Encode(s.Substring(2));

                if (s.EndsWith("uya"))
                    v3 = "A";

                if (s.StartsWith("oa") || s.StartsWith("oe") || s.StartsWith("oă") || s.StartsWith("uâ"))
                    v1 = "O";

                pos = shortPos ? t.shortPos : t.mediumPos;

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {v1}" : $"{prefix} {v1}"
                });
                output.Add(new Phoneme {
                    phoneme = $"{v1} {v2}",
                    position = t.longPos
                });
                output.Add(new Phoneme {
                    phoneme = $"{v2} {v3}",
                    position = pos
                });

                if (noNext) {
                    output.Add(new Phoneme {
                        phoneme = $"{v3} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 3 && vvc && !hasInitial) {
                string v1 = EncodeV(s[0].ToString());
                string vvcPart = Encode(s);
                bool isNasal = IsNasalFinal(s[^1]);

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {v1}" : $"{prefix} {v1}"
                });
                output.Add(new Phoneme {
                    phoneme = vvcPart,
                    position = pos
                });

                if (noNext && isNasal) {
                    string tailFinal = EncodeFinal(s[^1].ToString());
                    if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                        tailFinal = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{tailFinal} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 3 && hasInitial && !hasFinal) {
                string c = EncodeInitial(s[0].ToString());
                string v1 = EncodeV(s[1].ToString());
                string v2 = Encode(s.Substring(2));

                if (s == "kua" || original == "qua" || original == ".qua") {
                    v1 = "O";
                    v2 = "a";
                } else if (s.EndsWith("oa")) { v1 = "O"; v2 = "a"; } else if (s.EndsWith("uy")) { v1 = "O"; v2 = "i"; } else if (s.EndsWith("oe")) { v1 = "O"; v2 = "e"; } else if (s.EndsWith("uê")) { v1 = "O"; v2 = "E"; } else if (s.EndsWith("oă")) { v1 = "O"; v2 = "a"; } else if (s.EndsWith("uâ")) { v1 = "O"; v2 = "A"; } else if (original.EndsWith("ao")) { v1 = "a"; v2 = "O"; } else if (original.EndsWith("eo")) { v1 = "e"; v2 = "O"; }

                if ((s.EndsWith("ia") || s.EndsWith("ua") || s.EndsWith("ưa"))
                    && original != "qua" && original != ".qua")
                    v2 = "A";

                string tail = v2;
                if (s.EndsWith("ôN") || s.EndsWith("uN") || s.EndsWith("oN"))
                    tail = "m";

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {c}{v1}" : $"{prefix} {c}{v1}"
                });
                output.Add(new Phoneme {
                    phoneme = $"{v1} {v2}",
                    position = pos
                });

                if (noNext) {
                    output.Add(new Phoneme {
                        phoneme = $"{tail} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 4 && hasInitial && vvc) {
                string c = EncodeInitial(s[0].ToString());
                string v1 = EncodeV(s[1].ToString());
                string vvcPart = Encode(s.Substring(1));
                bool isNasal = IsNasalFinal(s[^1]);

                pos = shortPos ? t.shortPos : t.mediumPos;

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {c}{v1}" : $"{prefix} {c}{v1}"
                });
                output.Add(new Phoneme {
                    phoneme = vvcPart,
                    position = pos
                });

                if (noNext && isNasal) {
                    string tailFinal = EncodeFinal(s[^1].ToString());
                    if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                        tailFinal = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{tailFinal} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 4 && !hasInitial && vvc) {
                string v1 = EncodeV(s[0].ToString());
                string v2 = EncodeV(s[1].ToString());
                string vvcPart = Encode(s.Substring(1));
                bool isNasal = IsNasalFinal(s[^1]);

                pos = shortPos ? t.shortPos : t.mediumPos;

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {v1}" : $"{prefix} {v1}"
                });
                output.Add(new Phoneme {
                    phoneme = $"{v1} {v2}",
                    position = t.longPos
                });
                output.Add(new Phoneme {
                    phoneme = vvcPart,
                    position = pos
                });

                if (noNext && isNasal) {
                    string tailFinal = EncodeFinal(s[^1].ToString());
                    if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                        tailFinal = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{tailFinal} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 4 && hasInitial && hasFinal) {
                string c = EncodeInitial(s[0].ToString());
                string v1 = EncodeV(s[1].ToString());
                string v2 = EncodeV(s[2].ToString());
                string tail = EncodeFinal(s[3].ToString());
                bool isStop = IsStopFinal(s[3]);
                bool isNasal = IsNasalFinal(s[3]);

                if (s.Substring(1, 2) == "oa" || s.Substring(1, 2) == "oe" || s.Substring(1, 2) == "oă" || s.Substring(1, 2) == "uâ")
                    v1 = "O";

                pos = shortPos ? t.shortPos : t.mediumPos;

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {c}{v1}" : $"{prefix} {c}{v1}"
                });
                output.Add(new Phoneme {
                    phoneme = $"{v1} {v2}",
                    position = t.longPos
                });
                output.Add(new Phoneme {
                    phoneme = isStop ? $"{v2}{tail}" : $"{v2} {tail}",
                    position = pos
                });

                if (noNext && isNasal) {
                    string finalTail = tail;
                    if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                        finalTail = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{finalTail} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 4 && hasInitial && !hasFinal) {
                string c = EncodeInitial(s[0].ToString());
                string v1 = EncodeV(s[1].ToString());
                string v2 = EncodeV(s[2].ToString());
                string v3 = Encode(s.Substring(3));

                if (s == "kuao" || original == "quao" || original == ".quao") {
                    v1 = "O";
                    v2 = "a";
                    v3 = "O";
                } else if (s == "kueo" || original == "queo" || original == ".queo") {
                    v1 = "O";
                    v2 = "e";
                    v3 = "O";
                } else {
                    if (s.EndsWith("uya"))
                        v3 = "A";

                    if (s.Substring(1, 2) == "oa" || s.Substring(1, 2) == "oe" || s.Substring(1, 2) == "oă" || s.Substring(1, 2) == "uâ")
                        v1 = "O";
                }

                pos = shortPos ? t.shortPos : t.mediumPos;

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {c}{v1}" : $"{prefix} {c}{v1}"
                });
                output.Add(new Phoneme {
                    phoneme = $"{v1} {v2}",
                    position = t.longPos
                });
                output.Add(new Phoneme {
                    phoneme = $"{v2} {v3}",
                    position = pos
                });

                if (noNext) {
                    output.Add(new Phoneme {
                        phoneme = $"{v3} -",
                        position = t.endPos
                    });
                }

                return;
            }

            if (s.Length == 5 && hasInitial && vvc) {
                string c = EncodeInitial(s[0].ToString());
                string v1 = EncodeV(s[1].ToString());
                string vvcPart = Encode(s.Substring(2));
                bool isNasal = IsNasalFinal(s[^1]);

                if (s.Substring(1).StartsWith("uyê") || s.Substring(1).StartsWith("uyên") || s.Substring(1).StartsWith("uyêN")) {
                    v1 = "O";
                    string v2 = "i";

                    pos = shortPos ? t.shortPos : t.mediumPos;

                    output.Add(new Phoneme {
                        phoneme = allowInitial ? $"- {c}{v1}" : $"{prefix} {c}{v1}"
                    });
                    output.Add(new Phoneme {
                        phoneme = $"{v1} {v2}",
                        position = t.longPos
                    });
                    output.Add(new Phoneme {
                        phoneme = vvcPart,
                        position = pos
                    });

                    if (noNext && isNasal) {
                        string tailFinal = EncodeFinal(s[^1].ToString());
                        if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                            tailFinal = "m";
                        }
                        output.Add(new Phoneme {
                            phoneme = $"{tailFinal} -",
                            position = t.endPos
                        });
                    }
                    return;
                }

                pos = shortPos ? t.shortPos : t.mediumPos;

                output.Add(new Phoneme {
                    phoneme = allowInitial ? $"- {c}{v1}" : $"{prefix} {c}{v1}"
                });
                output.Add(new Phoneme {
                    phoneme = vvcPart,
                    position = pos
                });

                if (noNext && isNasal) {
                    string tailFinal = EncodeFinal(s[^1].ToString());
                    if (s.EndsWith("oN") || s.EndsWith("ON") || s.EndsWith("uN")) {
                        tailFinal = "m";
                    }
                    output.Add(new Phoneme {
                        phoneme = $"{tailFinal} -",
                        position = t.endPos
                    });
                }
            }
        }

        static void AddBreath(
            List<Phoneme> output,
            string loi,
            TimingInfo t) {

            string number = loi.Length > 5
                ? loi.Substring(5)
                : "1";

            output.Add(new Phoneme {
                phoneme = $"breath{number}"
            });
        }

        string PreviousVowel(Note? previous) {
            string rawLyric = previous?.phoneticHint ?? previous?.lyric ?? "";
            if (string.IsNullOrEmpty(rawLyric))
                return null;

            string lyric = rawLyric.EndsWith("2") ? rawLyric[..^1] : rawLyric;

            var unicodeList = ToUnicodeElements(lyric);
            string lastElement = unicodeList.LastOrDefault() ?? "";

            string vow = vowelLookup.TryGetValue(
                lastElement,
                out var value)
                ? value
                : null;

            if (vow == null)
                return null;

            string pr = lyric;

            if (pr != "R")
                pr = pr.ToLower();

            if (pr == "gi")
                pr = "zi";

            pr = Normalize(pr);

            if (pr.EndsWith("ua") ||
                pr.EndsWith("ưa") ||
                pr.EndsWith("ia") ||
                pr.EndsWith("uya"))
                vow = "A";

            if (pr.EndsWith("uôN"))
                vow = "ng";
            else if (
                pr.EndsWith("uN") ||
                pr.EndsWith("ôN") ||
                pr.EndsWith("oN"))
                vow = "m";

            if (pr.EndsWith("breaT") || pr.EndsWith("C"))
                vow = "-";

            if (pr.EndsWith("ao") || pr.EndsWith("eo") || pr.EndsWith("quao") || pr.EndsWith("queo"))
                vow = "O";

            return vow;
        }

        static string Encode(string s) {
            if (string.IsNullOrEmpty(s))
                return s;

            string result = "";

            foreach (char c in s) {
                if (Vowels.TryGetValue(c, out var v))
                    result += v;
                else if (Consonants.TryGetValue(c, out var consonant))
                    result += consonant;
                else if (c == 'N')
                    result += "ng";
                else if (c == 'J')
                    result += "nh";
                else if (c == 'C')
                    result += "ch";
                else
                    result += c;
            }

            return result;
        }

        static string EncodeV(string s) {
            if (string.IsNullOrEmpty(s))
                return s;

            return Vowels.TryGetValue(s[0], out var v)
                ? v
                : s;
        }

        static string EncodeInitial(string s) {
            if (string.IsNullOrEmpty(s))
                return s;

            return Consonants.TryGetValue(s[0], out var c)
                ? c
                : Encode(s);
        }

        static string EncodeFinal(string s) {
            if (string.IsNullOrEmpty(s))
                return s;

            return s[0] switch {
                'C' => "ch",
                'N' => "ng",
                'J' => "nh",
                _ => s
            };
        }

        static bool EndsWithAny(string value, IEnumerable<string> endings) {
            foreach (var ending in endings)
                if (value.EndsWith(ending))
                    return true;

            return false;
        }

        void MapOto(
            List<Phoneme> phonemes,
            Note note,
            Note[] notes,
            Note[] prevNeighbours) {

            int noteIndex = 0;

            for (int i = 0; i < phonemes.Count; i++) {
                var attr = note.phonemeAttributes?
                    .FirstOrDefault(a => a.index == i) ?? default;

                string alt =
                    (attr.alternate ?? GetParentAlternate())?
                    .ToString() ?? "";

                string color =
                    attr.voiceColor ?? GetParentVoiceColor();

                int toneShift =
                    attr.toneShift ?? GetParentToneShift();

                var p = phonemes[i];

                while (
                    noteIndex < notes.Length - 1 &&
                    notes[noteIndex].position - note.position < p.position) {
                    noteIndex++;
                }

                int tone =
                    i == 0 &&
                    prevNeighbours != null &&
                    prevNeighbours.Length > 0
                        ? prevNeighbours.Last().tone
                        : notes[noteIndex].tone;

                if (singer.TryGetMappedOto(
                    $"{p.phoneme}{alt}",
                    note.tone + toneShift,
                    color,
                    out var oto)) {

                    p.phoneme = oto.Alias;
                }

                phonemes[i] = p;
            }
        }

        readonly struct TimingInfo {
            public readonly int shortPos;
            public readonly int longPos;
            public readonly int mediumPos;
            public readonly int endPos;

            public TimingInfo(
                int shortPos,
                int longPos,
                int mediumPos,
                int endPos) {

                this.shortPos = shortPos;
                this.longPos = longPos;
                this.mediumPos = mediumPos;
                this.endPos = endPos;
            }
        }
    }
}
