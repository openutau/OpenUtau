using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Vietnamese VCV Phonemizer", "VIE VCV", "Jani Tran", language:"VI")]
    public class VietnameseVCVPhonemizer : Phonemizer {
        /// <summary>
        /// The lookup table to convert a hiragana to its tail vowel.
        /// </summary>
        static readonly string[] vowels = new string[] {
            "a=a,à,á,ả,ã,ạ,ă,ằ,ắ,ẳ,ẵ,ặ,A,À,Á,Ả,Ã,Ạ,Ă,Ằ,Ắ,Ẳ,Ẵ,Ặ",
            "A=â,ầ,ấ,ẩ,ẫ,ậ,Â,Ầ,Ấ,Ẩ,Ẫ,Ậ",
            "@=ơ,ờ,ớ,ở,ỡ,ợ,Ơ,Ờ,Ớ,Ở,Ỡ,Ợ,@",
            "i=i,y,ì,í,ỉ,ĩ,ị,ỳ,ý,ỷ,ỹ,ỵ,I,Y,Ì,Í,Ỉ,Ĩ,Ị,Ỳ,Ý,Ỷ,Ỹ,Ỵ",
            "e=e,è,é,ẻ,ẽ,ẹ,E,È,É,Ẻ,Ẽ,Ẹ",
            "E=ê,ề,ế,ể,ễ,ệ,Ê,Ề,Ế,Ể,Ễ,Ệ",
            "o=o,ò,ó,ỏ,õ,ọ,O,Ò,Ó,Ỏ,Õ,Ọ",
            "O=ô,ồ,ố,ổ,ỗ,ộ,Ô,Ồ,Ố,Ổ,Ỗ,Ộ",
            "u=u,ù,ú,ủ,ũ,ụ,U,Ù,Ú,Ủ,Ũ,Ụ",
            "U=ư,ừ,ứ,ử,ữ,ự,Ư,Ừ,Ứ,Ử,Ữ,Ự",
            "m=m,M",
            "n=n,N",
            "ng=g,G",
            "nh=h,H",
            "-=c,C,t,T,-,p,P,R,1,2,3,4,5",
        };

        static readonly Dictionary<string, string> vowelLookup;

        static VietnameseVCVPhonemizer() {
            vowelLookup = vowels.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        private USinger singer;

        public override void SetSinger(USinger singer) => this.singer = singer;
        
        // Legacy mapping. Might adjust later to new mapping style.
		public override bool LegacyMapping => true;

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme {
                            phoneme = note.phoneticHint,
                        }
                    },
                };
            }
            int totalDuration = notes.Sum(n => n.duration);
            int Short = 0;
            int Long = 0;
            int Medium = 0;
            int End = 0;
            int ViTri = 0;
            if (totalDuration < 350) {
                Short = totalDuration * 4 / 8;
                Long = totalDuration / 6;
                Medium = totalDuration / 3;
                End = totalDuration * 4 / 5;
                ViTri = Short;
            } else {
                Short = totalDuration - 170;
                Long = 90;
                Medium = 180;
                End = totalDuration - 50;
                ViTri = Short;
            }
            bool a;
            bool NoNext = nextNeighbour == null && note.lyric != "R";
            var loi = note.lyric;
            if (note.lyric.StartsWith("?")) {
            } else {
                if (note.lyric != "R") {
                    loi = note.lyric.ToLower();
                    note.lyric = note.lyric.ToLower();
                }
            }
            note.lyric = note.lyric.Replace('à', 'a').Replace('á', 'a').Replace('ả', 'a').Replace('ã', 'a').Replace('ạ', 'a');
            note.lyric = note.lyric.Replace('ằ', 'ă').Replace('ắ', 'ă').Replace('ẳ', 'ă').Replace('ẵ', 'ă').Replace('ặ', 'ă');
            note.lyric = note.lyric.Replace('ầ', 'â').Replace('ấ', 'â').Replace('ẩ', 'â').Replace('ẫ', 'â').Replace('ậ', 'â');
            note.lyric = note.lyric.Replace('ờ', 'ơ').Replace('ớ', 'ơ').Replace('ở', 'ơ').Replace('ỡ', 'ơ').Replace('ợ', 'ơ');
            note.lyric = note.lyric.Replace('ì', 'i').Replace('í', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i').Replace('ị', 'i');
            note.lyric = note.lyric.Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y').Replace('ỵ', 'y');
            note.lyric = note.lyric.Replace('è', 'e').Replace('é', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e').Replace('ẹ', 'e');
            note.lyric = note.lyric.Replace('ề', 'ê').Replace('ế', 'ê').Replace('ể', 'ê').Replace('ễ', 'ê').Replace('ệ', 'ê');
            note.lyric = note.lyric.Replace('ò', 'o').Replace('ó', 'o').Replace('ỏ', 'o').Replace('õ', 'o').Replace('ọ', 'o');
            note.lyric = note.lyric.Replace('ồ', 'ô').Replace('ố', 'ô').Replace('ổ', 'ô').Replace('ỗ', 'ô').Replace('ộ', 'ô');
            note.lyric = note.lyric.Replace('ù', 'u').Replace('ú', 'u').Replace('ủ', 'u').Replace('ũ', 'u').Replace('ụ', 'u');
            note.lyric = note.lyric.Replace('ừ', 'ư').Replace('ứ', 'ư').Replace('ử', 'ư').Replace('ữ', 'ư').Replace('ự', 'ư');
            if (note.lyric == "quôc") {
                note.lyric = "quâc";
            }
            if (note.lyric != "gi" && note.lyric != "gin" && note.lyric != "gim" && note.lyric != "ginh" && note.lyric != "ging" && note.lyric != "git" && note.lyric != "gip" && note.lyric != "gic" && note.lyric != "gich") {
                loi = note.lyric.Replace('à', 'a').Replace('á', 'a').Replace('ả', 'a').Replace('ã', 'a').Replace('ạ', 'a');
                loi = note.lyric.Replace('ằ', 'ă').Replace('ắ', 'ă').Replace('ẳ', 'ă').Replace('ẵ', 'ă').Replace('ặ', 'ă');
                loi = note.lyric.Replace('ầ', 'â').Replace('ấ', 'â').Replace('ẩ', 'â').Replace('ẫ', 'â').Replace('ậ', 'â');
                loi = note.lyric.Replace('ờ', 'ơ').Replace('ớ', 'ơ').Replace('ở', 'ơ').Replace('ỡ', 'ơ').Replace('ợ', 'ơ');
                loi = note.lyric.Replace('ì', 'i').Replace('í', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i').Replace('ị', 'i');
                loi = note.lyric.Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y').Replace('ỵ', 'y');
                loi = note.lyric.Replace('è', 'e').Replace('é', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e').Replace('ẹ', 'e');
                loi = note.lyric.Replace('ề', 'ê').Replace('ế', 'ê').Replace('ể', 'ê').Replace('ễ', 'ê').Replace('ệ', 'ê');
                loi = note.lyric.Replace('ò', 'o').Replace('ó', 'o').Replace('ỏ', 'o').Replace('õ', 'o').Replace('ọ', 'o');
                loi = note.lyric.Replace('ồ', 'ô').Replace('ố', 'ô').Replace('ổ', 'ô').Replace('ỗ', 'ô').Replace('ộ', 'ô');
                loi = note.lyric.Replace('ù', 'u').Replace('ú', 'u').Replace('ủ', 'u').Replace('ũ', 'u').Replace('ụ', 'u');
                loi = note.lyric.Replace('ừ', 'ư').Replace('ứ', 'ư').Replace('ử', 'ư').Replace('ữ', 'ư').Replace('ự', 'ư');
                loi = note.lyric.Replace("ch", "C").Replace("d", "z").Replace("đ", "d").Replace("ph", "f").Replace("ch", "C")
                    .Replace("gi", "z").Replace("gh", "g").Replace("c", "k").Replace("kh", "K").Replace("ng", "N")
                    .Replace("ngh", "N").Replace("nh", "J").Replace("x", "s").Replace("tr", "Z").Replace("th", "T")
                    .Replace("q", "k").Replace("r", "z");
            } else {
                loi = note.lyric.Replace('ì', 'i').Replace('í', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i').Replace('ị', 'i');
                loi = loi.Replace("gi", "zi").Replace("ng", "N").Replace("nh", "J").Replace("ch", "C").Replace("c", "k");
            }
            bool tontaiVVC = (loi.EndsWith("iên") || loi.EndsWith("iêN") || loi.EndsWith("iêm") || loi.EndsWith("iêt") || loi.EndsWith("iêk") || loi.EndsWith("iêp") || loi.EndsWith("iêu")
                           || loi.EndsWith("yên") || loi.EndsWith("yêN") || loi.EndsWith("yêm") || loi.EndsWith("yêt") || loi.EndsWith("yêk") || loi.EndsWith("yêp") || loi.EndsWith("yêu")
                           || loi.EndsWith("uôn") || loi.EndsWith("uôN") || loi.EndsWith("uôm") || loi.EndsWith("uôt") || loi.EndsWith("uôk") || loi.EndsWith("uôi")
                           || loi.EndsWith("ươn") || loi.EndsWith("ươN") || loi.EndsWith("ươm") || loi.EndsWith("ươt") || loi.EndsWith("ươk") || loi.EndsWith("ươp") || loi.EndsWith("ươi"));
            bool koVVCchia;
            if (tontaiVVC == true) {
                koVVCchia = false;
            } else
                koVVCchia = true;
            bool tontaiCcuoi = (loi.EndsWith("k") || loi.EndsWith("t") || loi.EndsWith("C") || loi.EndsWith("p"));
            bool kocoCcuoi;
            if (tontaiCcuoi == true) {
                kocoCcuoi = false;
            } else
                kocoCcuoi = true;
            bool tontaiC = (loi.StartsWith("b") || loi.StartsWith("C") || loi.StartsWith("d") || loi.StartsWith("f")
                         || loi.StartsWith("g") || loi.StartsWith("h") || loi.StartsWith("k") || loi.StartsWith("K")
                         || loi.StartsWith("l") || loi.StartsWith("m") || loi.StartsWith("n") || loi.StartsWith("N")
                         || loi.StartsWith("J") || loi.StartsWith("r") || loi.StartsWith("s") || loi.StartsWith("t")
                         || loi.StartsWith("T") || loi.StartsWith("Z") || loi.StartsWith("v") || loi.StartsWith("w")
                         || loi.StartsWith("z") || loi.StartsWith("p"));
            bool kocoC;
            if (tontaiC == true) {
                kocoC = false;
            } else
                kocoC = true;
            bool BR = note.lyric.StartsWith("breath");
            bool tontaiVV = (loi.EndsWith("ai") || loi.EndsWith("ơi") || loi.EndsWith("oi") || loi.EndsWith("ôi") || loi.EndsWith("ui") || loi.EndsWith("ưi")
                          || loi.EndsWith("ao") || loi.EndsWith("eo") || loi.EndsWith("êu") || loi.EndsWith("iu")
                          || loi.EndsWith("an") || loi.EndsWith("ơn") || loi.EndsWith("in") || loi.EndsWith("en") || loi.EndsWith("ên") || loi.EndsWith("on") || loi.EndsWith("ôn") || loi.EndsWith("un") || loi.EndsWith("ưn")
                          || loi.EndsWith("am") || loi.EndsWith("ơm") || loi.EndsWith("im") || loi.EndsWith("em") || loi.EndsWith("êm") || loi.EndsWith("om") || loi.EndsWith("ôm") || loi.EndsWith("um") || loi.EndsWith("ưm")
                          || loi.EndsWith("aN") || loi.EndsWith("ơN") || loi.EndsWith("iN") || loi.EndsWith("eN") || loi.EndsWith("êN") || loi.EndsWith("ưN")
                          || loi.EndsWith("aJ") || loi.EndsWith("iJ") || loi.EndsWith("êJ")
                          || loi.EndsWith("at") || loi.EndsWith("ơt") || loi.EndsWith("it") || loi.EndsWith("et") || loi.EndsWith("êt") || loi.EndsWith("ot") || loi.EndsWith("ôt") || loi.EndsWith("ut") || loi.EndsWith("ưt")
                          || loi.EndsWith("aC") || loi.EndsWith("iC") || loi.EndsWith("êC")
                          || loi.EndsWith("ak") || loi.EndsWith("ơk") || loi.EndsWith("ik") || loi.EndsWith("ek") || loi.EndsWith("êk") || loi.EndsWith("ok") || loi.EndsWith("ôk") || loi.EndsWith("uk") || loi.EndsWith("ưk")
                          || loi.EndsWith("ap") || loi.EndsWith("ơp") || loi.EndsWith("ip") || loi.EndsWith("ep") || loi.EndsWith("êp") || loi.EndsWith("op") || loi.EndsWith("ôp") || loi.EndsWith("up") || loi.EndsWith("ưp")
                          || loi.EndsWith("ia") || loi.EndsWith("ua") || loi.EndsWith("ưa")
                          || loi.EndsWith("ay") || loi.EndsWith("ây") || loi.EndsWith("uy")
                          || loi.EndsWith("au") || loi.EndsWith("âu")
                          || loi.EndsWith("oa") || loi.EndsWith("oe") || loi.EndsWith("uê"));
            bool ViTriNgan = (loi.EndsWith("ai") || loi.EndsWith("ơi") || loi.EndsWith("oi") || loi.EndsWith("ôi") || loi.EndsWith("ui") || loi.EndsWith("ưi")
                  || loi.EndsWith("ao") || loi.EndsWith("eo") || loi.EndsWith("êu") || loi.EndsWith("iu")
                  || loi.EndsWith("an") || loi.EndsWith("ơn") || loi.EndsWith("in") || loi.EndsWith("en") || loi.EndsWith("ên") || loi.EndsWith("on") || loi.EndsWith("ôn") || loi.EndsWith("un") || loi.EndsWith("ưn")
                  || loi.EndsWith("am") || loi.EndsWith("ơm") || loi.EndsWith("im") || loi.EndsWith("em") || loi.EndsWith("êm") || loi.EndsWith("om") || loi.EndsWith("ôm") || loi.EndsWith("um") || loi.EndsWith("ưm")
                  || loi.EndsWith("aN") || loi.EndsWith("ơN") || loi.EndsWith("iN") || loi.EndsWith("eN") || loi.EndsWith("êN") || loi.EndsWith("ưN")
                  || loi.EndsWith("at") || loi.EndsWith("ơt") || loi.EndsWith("it") || loi.EndsWith("et") || loi.EndsWith("êt") || loi.EndsWith("ot") || loi.EndsWith("ôt") || loi.EndsWith("ut") || loi.EndsWith("ưt")
                  || loi.EndsWith("ak") || loi.EndsWith("ơk") || loi.EndsWith("ik") || loi.EndsWith("ek") || loi.EndsWith("êk") || loi.EndsWith("ok") || loi.EndsWith("ôk") || loi.EndsWith("uk") || loi.EndsWith("ưk")
                  || loi.EndsWith("ap") || loi.EndsWith("ơp") || loi.EndsWith("ip") || loi.EndsWith("ep") || loi.EndsWith("êp") || loi.EndsWith("op") || loi.EndsWith("ôp") || loi.EndsWith("up") || loi.EndsWith("ưp")
                  || loi.EndsWith("ia") || loi.EndsWith("ua") || loi.EndsWith("ưa") || loi.EndsWith("uôN")
                  || loi.EndsWith("yt") || loi.EndsWith("yn") || loi.EndsWith("ym") || loi.EndsWith("yC") || loi.EndsWith("yp") || loi.EndsWith("yk") || loi.EndsWith("yN")
                  || loi.EndsWith("uya") && (note.lyric != "qua"));
            bool ViTriDai = (loi.EndsWith("ay") || loi.EndsWith("ây") || loi.EndsWith("uy")
                  || loi.EndsWith("au") || loi.EndsWith("âu")
                  || loi.EndsWith("oa") || loi.EndsWith("oe") || loi.EndsWith("uê") || note.lyric.EndsWith("qua"));
            bool ViTriTB = loi.EndsWith("ăt") || loi.EndsWith("ât")
                  || loi.EndsWith("ăk") || loi.EndsWith("âk")
                  || loi.EndsWith("ăp") || loi.EndsWith("âp")
                  || loi.EndsWith("ăn") || loi.EndsWith("ân")
                  || loi.EndsWith("ăN") || loi.EndsWith("âN")
                  || loi.EndsWith("ăm") || loi.EndsWith("âm")
                  || loi.EndsWith("aJ") || loi.EndsWith("iJ") || loi.EndsWith("êJ") || loi.EndsWith("yJ")
                  || loi.EndsWith("ôN") || loi.EndsWith("uN") || loi.EndsWith("oN")
                  || loi.EndsWith("aC") || loi.EndsWith("iC") || loi.EndsWith("êC") || loi.EndsWith("yC");
            bool XO = false;
            if (ViTriTB) {
                ViTri = Medium;
            }
            if (ViTriNgan) {
                ViTri = Short;
            }
            if (ViTriDai) {
                ViTri = Long;
            }
            if (loi.EndsWith("uôN")) {
                ViTri = Short;
            }
            var dem = loi.Length;
            var phoneme = "";
            var phonemes = new List<Phoneme>();
            if (note.lyric.StartsWith("?")) {
                phoneme = note.lyric.Substring(1);
            } else {
                // 1 kí tự 
                if (dem == 1) {
                    string N = loi;
                    N = N.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                             .Replace("ư", "U").Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh")
                             .Replace("Z", "tr").Replace("T", "th");
                    if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {N}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                        }
                    } else
                        if (prevNeighbour == null) {
                        phonemes.Add(
                        new Phoneme { phoneme = $"- {N}" });
                    }
                }
                // 2 kí tự CV, ví dụ: "ba"
                if ((dem == 2) && tontaiC) {
                    string N = loi;
                    string N2 = loi.Substring(1, 1);
                    N = N.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                             .Replace("ư", "U").Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh")
                             .Replace("Z", "tr").Replace("T", "th");
                    N2 = N2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                             .Replace("ư", "U");
                    if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {N}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{N2} -", position = End });
                        }
                    } else if (prevNeighbour == null) {
                        phonemes.Add(
                        new Phoneme { phoneme = $"- {N}" });
                    }
                }
                // 2 kí tự VV/VC, ví dụ: "oa" "an"
                if ((dem == 2) && kocoC && kocoCcuoi) {
                    string V1 = loi.Substring(0, 1);
                    string V2 = loi.Substring(1, 1);
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                         .Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                 .Replace("ư", "U").Replace("N", "ng").Replace("J", "nh");
                    a = (loi.EndsWith("ia") || loi.EndsWith("ua") || loi.EndsWith("ưa"));
                    if (a) {
                        V2 = "A";
                    }
                    if (loi.EndsWith("oa") || loi.EndsWith("oe")) {
                        V1 = "u";
                    }
                    if (note.lyric == "ao" || note.lyric == "eo") {
                        V2 = "u";
                    }
                    string N = V2;
                    if (loi == "ôN" || loi == "uN" || loi == "oN") {
                        N = "m";
                    }
                    if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                        }
                    } else {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                        }
                    }
                }
                // 2 kí tự VC, ví dụ "át"
                if ((dem == 2) && tontaiCcuoi) {
                    string V1 = loi.Substring(0, 1);
                    string C = loi.Substring(1, 1);
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                         .Replace("ư", "U");
                    C = C.Replace("C", "ch");
                    if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1}{C}", position = ViTri });
                    }
                }
                // 3 kí tự VVC chia 3 nốt, ví dụ: "oát"
                if ((dem == 3) && tontaiCcuoi && koVVCchia && kocoC) {
                    string V1 = loi.Substring(0, 1);
                    string V2 = loi.Substring(1, 1);
                    string VC = loi.Substring(1);
                    if (loi.StartsWith("oa") || loi.StartsWith("oe")) {
                        V1 = "u";
                    }
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U");
                    VC = VC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U").Replace("C", "ch");
                    if (ViTriDai) {
                        ViTri = Medium;
                    }
                    if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{VC}", position = ViTri });
                    }
                }
                // 3 kí tự VVV chia 3 nốt, ví dụ: "oan" "oai"
                if ((dem == 3) && koVVCchia && kocoC) {
                    string V1 = loi.Substring(0, 1);
                    string V2 = loi.Substring(1, 1);
                    string V3 = loi.Substring(2);
                    if (loi.EndsWith("uya")) {
                        V3 = "A";
                    }
                    if (loi.StartsWith("oa") || loi.StartsWith("oe")) {
                        V1 = "u";
                    }
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U");
                    V3 = V3.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U").Replace("N", "ng").Replace("J", "nh");
                    if (ViTriNgan) {
                        ViTri = Short;
                    } else {
                        ViTri = Medium;
                    }
                    if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V3} -", position = End });
                        }
                    } else if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                    }
                }
                // 3 kí tự VVV/VVC chia 2 nốt, ví dụ: "yên" "ướt"
                if ((dem == 3) && tontaiVVC && kocoC) {
                    string V1 = loi.Substring(0, 1);
                    string VVC = loi.Substring(0);
                    string N = loi.Substring(2);
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U");
                    VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                           .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    if (NoNext && tontaiCcuoi) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                        }
                    } else if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                                new Phoneme { phoneme = $"- {V1}" });
                            phonemes.Add(
                                new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            phonemes.Add(
                                new Phoneme { phoneme = $"{N} -", position = End });
                        }
                    } else if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                    }
                }
                // 3 kí tự CVC, ví dụ: "hát"
                if (dem == 3 && tontaiC && tontaiCcuoi) {
                    string C = loi.Substring(0, 1);
                    string V1 = loi.Substring(1, 1);
                    string V2 = loi.Substring(2);
                    C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    V2 = V2.Replace("C", "ch");
                    if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1}{V2}", position = ViTri });
                    }
                }
                // 3 kí tự CVV/CVC, ví dụ: "hoa" "han"
                if (dem == 3 && tontaiC && kocoCcuoi) {
                    string C = loi.Substring(0, 1);
                    string V1 = loi.Substring(1, 1);
                    string V2 = loi.Substring(2);
                    if (loi.EndsWith("oa") || loi.EndsWith("oe")) {
                        V1 = "u";
                    }
                    C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U")
                        .Replace("N", "ng").Replace("J", "nh");
                    a = (loi.EndsWith("ia") || loi.EndsWith("ua") || loi.EndsWith("ưa"));
                    if (a && (note.lyric != "qua")) {
                        V2 = "A";
                    }
                    if (note.lyric.EndsWith("ao") || note.lyric.EndsWith("eo")) {
                        V2 = "u";
                    }
                    string N = V2;
                    if (loi.EndsWith("ôN") || loi.EndsWith("uN") || loi.EndsWith("oN")) {
                        N = "m";
                    }
                    if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                        }
                    } else {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                        }
                    }
                }
                // 4 kí tự VVVC có VVC liền, chia 3 nốt, ví dụ "uyết" "uyên"
                if (dem == 4 && kocoC && tontaiVVC) {
                    string V1 = loi.Substring(0, 1);
                    string V2 = loi.Substring(1, 1);
                    string VVC = loi.Substring(1);
                    string N = loi.Substring(3);
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                 .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    if (ViTriNgan) {
                        ViTri = Short;
                    } else {
                        ViTri = Medium;
                    }
                    if (NoNext && tontaiCcuoi) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                        }
                    } else if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                        }
                    } else if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                    }
                }
                // 4 kí tự CVVC, có VVC liền, chia 2 nốt, ví dụ "thiết" "tiên"
                if (dem == 4 && tontaiVVC && tontaiC) {
                    string C = loi.Substring(0, 1);
                    string V1 = loi.Substring(1, 1);
                    string VVC = loi.Substring(1);
                    string N = loi.Substring(3);
                    C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                 .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    if (NoNext && tontaiCcuoi) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                        }
                    } else if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                        }
                    } else if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                    }
                } else XO = true;

                // 4 kí tự CVVC, chia 3 nốt, ví dụ "thoát"
                if (dem == 4 && tontaiC && tontaiCcuoi && XO) {
                    string C = loi.Substring(0, 1);
                    string V1 = loi.Substring(1, 1);
                    string V2 = loi.Substring(2, 1);
                    string VC = loi.Substring(2);
                    if (V1 + V2 == "oa" || V1 + V2 == "oe") {
                        V1 = "u";
                    }
                    C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    VC = VC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                 .Replace("ư", "U").Replace("C", "ch");
                    if (ViTriNgan) {
                        ViTri = Short;
                    } else {
                        ViTri = Medium;
                    }
                    if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{VC}", position = ViTri });
                    }
                }
                // 4 kí tự CVVV/CVVC, chia 3 nốt, ví dụ "ngoại" "ngoan"
                if (dem == 4 && kocoCcuoi && tontaiC && XO) {
                    string C = loi.Substring(0, 1);
                    string V1 = loi.Substring(1, 1);
                    string V2 = loi.Substring(2, 1);
                    string V3 = loi.Substring(3);
                    if (loi.EndsWith("uya")) {
                        V3 = "A";
                    }
                    if (V1 + V2 == "oa" || V1 + V2 == "oe") {
                        V1 = "u";
                    }
                    C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    V3 = V3.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U")
                        .Replace("N", "ng").Replace("J", "nh");
                    if (ViTriNgan) {
                        ViTri = Short;
                    } else {
                        ViTri = Medium;
                    }
                    if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V3} -", position = End });
                        }
                    } else if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                    }
                }
                // 5 kí tự CVVVC, có VVC liền, chia 3 nốt, ví dụ "thuyết" "thuyền"
                if (dem == 5 && tontaiVVC && tontaiC) {
                    string C = loi.Substring(0, 1);
                    string V1 = loi.Substring(1, 1);
                    string V2 = loi.Substring(2, 1);
                    string VVC = loi.Substring(2);
                    string N = loi.Substring(4);
                    C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                    V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                    VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                 .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                    if (ViTriNgan) {
                        ViTri = Short;
                    } else {
                        ViTri = Medium;
                    }
                    if (NoNext && tontaiCcuoi) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                        }
                    } else if (NoNext) {
                        if (prevNeighbour == null) {
                            phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                        }
                    } else if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"- {C}{V1}" });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                        phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                    }
                }
                if (BR) {
                    string num = loi.Substring(5);
                    if (num == "") {
                        num = "1";
                    }
                    if (prevNeighbour == null) {
                        phonemes.Add(
                            new Phoneme { phoneme = $"breath{num}" });
                    }
                }
            }
            if (prevNeighbour != null) {
                var lyric = prevNeighbour?.phoneticHint ?? prevNeighbour?.lyric;
                var unicode = ToUnicodeElements(lyric);
                if (vowelLookup.TryGetValue(unicode.LastOrDefault() ?? string.Empty, out var vow)) {
                    string PR = prevNeighbour?.lyric;
                    if (PR.StartsWith("?")) {
                        vow = PR.Substring(PR.Length - 1, 1);
                        if (PR.EndsWith("nh")) {
                            vow = "nh";
                        }
                        if (PR.EndsWith("ng")) {
                            vow = "ng";
                        }
                        if (PR.EndsWith("ch") || PR.EndsWith("t") || PR.EndsWith("k") || PR.EndsWith("p")) {
                            vow = "-";
                        }
                    }
                    if (PR != "R") {
                        PR = PR.ToLower();
                    }
                    if (PR == "gi") {
                        PR = "zi";
                    }
                    PR = PR.Replace('à', 'a').Replace('á', 'a').Replace('ả', 'a').Replace('ã', 'a').Replace('ạ', 'a');
                    PR = PR.Replace('ằ', 'ă').Replace('ắ', 'ă').Replace('ẳ', 'ă').Replace('ẵ', 'ă').Replace('ặ', 'ă');
                    PR = PR.Replace('ầ', 'â').Replace('ấ', 'â').Replace('ẩ', 'â').Replace('ẫ', 'â').Replace('ậ', 'â');
                    PR = PR.Replace('ờ', 'ơ').Replace('ớ', 'ơ').Replace('ở', 'ơ').Replace('ỡ', 'ơ').Replace('ợ', 'ơ');
                    PR = PR.Replace('ì', 'i').Replace('í', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i').Replace('ị', 'i');
                    PR = PR.Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y').Replace('ỵ', 'y');
                    PR = PR.Replace('è', 'e').Replace('é', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e').Replace('ẹ', 'e');
                    PR = PR.Replace('ề', 'ê').Replace('ế', 'ê').Replace('ể', 'ê').Replace('ễ', 'ê').Replace('ệ', 'ê');
                    PR = PR.Replace('ò', 'o').Replace('ó', 'o').Replace('ỏ', 'o').Replace('õ', 'o').Replace('ọ', 'o');
                    PR = PR.Replace('ồ', 'ô').Replace('ố', 'ô').Replace('ổ', 'ô').Replace('ỗ', 'ô').Replace('ộ', 'ô');
                    PR = PR.Replace('ù', 'u').Replace('ú', 'u').Replace('ủ', 'u').Replace('ũ', 'u').Replace('ụ', 'u');
                    PR = PR.Replace('ừ', 'ư').Replace('ứ', 'ư').Replace('ử', 'ư').Replace('ữ', 'ư').Replace('ự', 'ư');
                    PR = PR.Replace("ch", "C").Replace("d", "z").Replace("đ", "d").Replace("ph", "f").Replace("ch", "C")
                        .Replace("gi", "z").Replace("gh", "g").Replace("c", "k").Replace("kh", "K").Replace("ng", "N")
                        .Replace("ngh", "N").Replace("nh", "J").Replace("x", "s").Replace("tr", "Z").Replace("th", "T")
                        .Replace("qu", "w");
                    a = (PR.EndsWith("ua") || PR.EndsWith("ưa") || PR.EndsWith("ia") || PR.EndsWith("uya"));
                    if (a) {
                        vow = "A";
                    }
                    a = (PR.EndsWith("uN") || PR.EndsWith("ôN") || PR.EndsWith("oN"));
                    if (PR.EndsWith("uôN")) {
                        vow = "ng";
                    } else if (a) {
                        vow = "m";
                    }
                    a = (PR.EndsWith("breaT") || PR.EndsWith("C"));
                    if (a) {
                        vow = "-";
                    }
                    if (PR.EndsWith("ao") || PR.EndsWith("eo")) {
                        vow = "u";
                    }
                    XO = false;
                    if (note.lyric.StartsWith("?")) {
                        phoneme = note.lyric.Substring(1);
                    } else {
                        // 1 kí tự 
                        if (dem == 1) {
                            string N = loi;
                            N = N.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                     .Replace("ư", "U").Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh")
                                     .Replace("Z", "tr").Replace("T", "th");
                            if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {N}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                            } else phonemes.Add(
                                new Phoneme { phoneme = $"{vow} {N}" });
                        }
                        // 2 kí tự CV, ví dụ: "ba"
                        if ((dem == 2) && tontaiC) {
                            string N = loi;
                            string N2 = loi.Substring(1, 1);
                            N = N.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                     .Replace("ư", "U").Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh")
                                     .Replace("Z", "tr").Replace("T", "th");
                            N2 = N2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                     .Replace("ư", "U");
                            if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {N}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{N2} -", position = End });
                            } else phonemes.Add(
                                 new Phoneme { phoneme = $"{vow} {N}" });
                        }
                        // 2 kí tự VV/VC, ví dụ: "oa" "an"
                        if ((dem == 2) && kocoC && kocoCcuoi) {
                            string V1 = loi.Substring(0, 1);
                            string V2 = loi.Substring(1, 1);
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                 .Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                         .Replace("ư", "U").Replace("N", "ng").Replace("J", "nh");
                            a = (loi.EndsWith("ia") || loi.EndsWith("ua") || loi.EndsWith("ưa"));
                            if (a) {
                                V2 = "A";
                            }
                            if (loi.EndsWith("oa") || loi.EndsWith("oe")) {
                                V1 = "u";
                            }
                            if (note.lyric == "ao" || note.lyric == "eo") {
                                V2 = "u";
                            }
                            string N = V2;
                            if (loi == "ôN" || loi == "uN" || loi == "oN") {
                                N = "m";
                            }
                            if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                            }
                        }
                        // 2 kí tự VC, ví dụ "át"
                        if ((dem == 2) && tontaiCcuoi) {
                            string V1 = loi.Substring(0, 1);
                            string V2 = loi.Substring(1, 1);
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                 .Replace("ư", "U");
                            V2 = V2.Replace("C", "ch");
                            phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1}{V2}", position = ViTri });
                        }
                        // 3 kí tự VVC chia 3 nốt, ví dụ: "oát"
                        if ((dem == 3) && tontaiCcuoi && koVVCchia && kocoC) {
                            string V1 = loi.Substring(0, 1);
                            string V2 = loi.Substring(1, 1);
                            string VC = loi.Substring(1);
                            if (loi.StartsWith("oa") || loi.StartsWith("oe")) {
                                V1 = "u";
                            }
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U");
                            VC = VC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U").Replace("C", "ch");
                            if (ViTriDai) {
                                ViTri = Medium;
                            }
                            phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VC}", position = ViTri });
                        }
                        // 3 kí tự VVV chia 3 nốt, ví dụ: "oan" "oai"
                        if ((dem == 3) && koVVCchia && kocoC) {
                            string V1 = loi.Substring(0, 1);
                            string V2 = loi.Substring(1, 1);
                            string V3 = loi.Substring(2);
                            if (loi.EndsWith("uya")) {
                                V3 = "A";
                            }
                            if (loi.StartsWith("oa") || loi.StartsWith("oe")) {
                                V1 = "u";
                            }
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U");
                            V3 = V3.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U").Replace("N", "ng").Replace("J", "nh");
                            if (ViTriNgan) {
                                ViTri = Short;
                            } else {
                                ViTri = Medium;
                            }
                            if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V3} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                            }
                        }
                        // 3 kí tự VVV/VVC chia 2 nốt, ví dụ: "yên" "ướt"
                        if ((dem == 3) && tontaiVVC && kocoC) {
                            string V1 = loi.Substring(0, 1);
                            string VVC = loi.Substring(0);
                            string N = loi.Substring(2);
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U");
                            VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                   .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            if (NoNext && tontaiCcuoi) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            } else if (NoNext) {
                                phonemes.Add(
                                new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                                new Phoneme { phoneme = $"{VVC}", position = ViTri });
                                phonemes.Add(
                                new Phoneme { phoneme = $"{N} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            }
                        }
                        // 3 kí tự CVC, ví dụ: "hát"
                        if (dem == 3 && tontaiC && tontaiCcuoi) {
                            string C = loi.Substring(0, 1);
                            string V1 = loi.Substring(1, 1);
                            string V2 = loi.Substring(2);
                            C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            V2 = V2.Replace("C", "ch");
                            phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1}{V2}", position = ViTri });
                        }
                        // 3 kí tự CVV/CVC, ví dụ: "hoa" "han"
                        if (dem == 3 && tontaiC && kocoCcuoi) {
                            string C = loi.Substring(0, 1);
                            string V1 = loi.Substring(1, 1);
                            string V2 = loi.Substring(2);
                            if (loi.EndsWith("oa") || loi.EndsWith("oe")) {
                                V1 = "u";
                            }
                            C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U")
                                .Replace("N", "ng").Replace("J", "nh");
                            a = (loi.EndsWith("ia") || loi.EndsWith("ua") || loi.EndsWith("ưa"));
                            if (a && (note.lyric != "qua")) {
                                V2 = "A";
                            }
                            if (note.lyric.EndsWith("ao") || note.lyric.EndsWith("eo")) {
                                V2 = "u";
                            }
                            string N = V2;
                            if (loi.EndsWith("ôN") || loi.EndsWith("uN") || loi.EndsWith("oN")) {
                                N = "m";
                            }
                            if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = ViTri });
                            }
                        }
                        // 4 kí tự VVVC có VVC liền, chia 3 nốt, ví dụ "uyết" "uyên"
                        if (dem == 4 && kocoC && tontaiVVC) {
                            string V1 = loi.Substring(0, 1);
                            string V2 = loi.Substring(1, 1);
                            string VVC = loi.Substring(1);
                            string N = loi.Substring(3);
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                         .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            if (ViTriNgan) {
                                ViTri = Short;
                            } else {
                                ViTri = Medium;
                            }
                            if (NoNext && tontaiCcuoi) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            } else if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            }
                        }
                        // 4 kí tự CVVC, có VVC liền, chia 2 nốt, ví dụ "thiết" "tiên"
                        if (dem == 4 && tontaiVVC && tontaiC) {
                            string C = loi.Substring(0, 1);
                            string V1 = loi.Substring(1, 1);
                            string VVC = loi.Substring(1);
                            string N = loi.Substring(3);
                            C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                         .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            if (NoNext && tontaiCcuoi) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            } else if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            }
                        } else XO = true;
                        // 4 kí tự CVVC, chia 3 nốt, ví dụ "thoát"
                        if (dem == 4 && tontaiC && tontaiCcuoi && XO) {
                            string C = loi.Substring(0, 1);
                            string V1 = loi.Substring(1, 1);
                            string V2 = loi.Substring(2, 1);
                            string VC = loi.Substring(2);
                            if (V1 + V2 == "oa" || V1 + V2 == "oe") {
                                V1 = "u";
                            }
                            C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            VC = VC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                         .Replace("ư", "U").Replace("C", "ch");
                            if (ViTriNgan) {
                                ViTri = Short;
                            } else {
                                ViTri = Medium;
                            }
                            phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                            phonemes.Add(
                            new Phoneme { phoneme = $"{VC}", position = ViTri });
                        }
                        // 4 kí tự CVVV/CVVC, chia 3 nốt, ví dụ "ngoại" "ngoan"
                        if (dem == 4 && kocoCcuoi && tontaiC && XO) {
                            string C = loi.Substring(0, 1);
                            string V1 = loi.Substring(1, 1);
                            string V2 = loi.Substring(2, 1);
                            string V3 = loi.Substring(3);
                            if (loi.EndsWith("uya")) {
                                V3 = "A";
                            }
                            if (V1 + V2 == "oa" || V1 + V2 == "oe") {
                                V1 = "u";
                            }
                            C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            V3 = V3.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U")
                                .Replace("N", "ng").Replace("J", "nh");
                            if (ViTriNgan) {
                                ViTri = Short;
                            } else {
                                ViTri = Medium;
                            }
                            if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V3} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V2} {V3}", position = ViTri });
                            }
                        }
                        // 5 kí tự CVVVC, có VVC liền, chia 3 nốt, ví dụ "thuyết" "thuyền"
                        if (dem == 5 && tontaiVVC && tontaiC) {
                            string C = loi.Substring(0, 1);
                            string V1 = loi.Substring(1, 1);
                            string V2 = loi.Substring(2, 1);
                            string VVC = loi.Substring(2);
                            string N = loi.Substring(4);
                            C = C.Replace("C", "ch").Replace("K", "kh").Replace("N", "ng").Replace("J", "nh").Replace("Z", "tr").Replace("T", "th");
                            V1 = V1.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            V2 = V2.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O").Replace("ư", "U");
                            VVC = VVC.Replace("ă", "a").Replace("â", "A").Replace("ơ", "@").Replace("y", "i").Replace("ê", "E").Replace("ô", "O")
                                         .Replace("ư", "U").Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            N = N.Replace("C", "ch").Replace("N", "ng").Replace("J", "nh");
                            if (ViTriNgan) {
                                ViTri = Short;
                            } else {
                                ViTri = Medium;
                            }
                            if (NoNext && tontaiCcuoi) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            } else if (NoNext) {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{N} -", position = End });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} {C}{V1}" });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{V1} {V2}", position = Long });
                                phonemes.Add(
                            new Phoneme { phoneme = $"{VVC}", position = ViTri });
                            }
                        }
                        if (BR) {
                            string num = loi.Substring(5);
                            if (num == "") {
                                num = "1";
                            }
                            if (vow == "-") {
                                phonemes.Add(
                            new Phoneme { phoneme = $"breath{num}" });
                            } else {
                                phonemes.Add(
                            new Phoneme { phoneme = $"{vow} -", position = -60 });
                                phonemes.Add(
                            new Phoneme { phoneme = $"breath{num}" });
                            }
                        }
                    }
                }
            }
            int noteIndex = 0;
            for (int i = 0; i < phonemes.Count; i++) {
                var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == i) ?? default;
                string alt = (attr.alternate ?? GetParentAlternate())?.ToString() ?? string.Empty;
                string color = attr.voiceColor ?? GetParentVoiceColor();
                int toneShift = attr.toneShift ?? GetParentToneShift();
                var phoneme1 = phonemes[i];
                while (noteIndex < notes.Length - 1 && notes[noteIndex].position - note.position < phoneme1.position) {
                    noteIndex++;
                }
                int tone = (i == 0 && prevNeighbours != null && prevNeighbours.Length > 0)
                    ? prevNeighbours.Last().tone : notes[noteIndex].tone;
                if (singer.TryGetMappedOto($"{phoneme1.phoneme}{alt}", note.tone + toneShift, color, out var oto)) {
                    phoneme1.phoneme = oto.Alias;
                }
                phonemes[i] = phoneme1;
            }
            return new Result { phonemes = phonemes.ToArray() };
        }
    }
}
