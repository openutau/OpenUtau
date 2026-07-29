using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Turkish CVVC Phonemizer", "TR CVVC", "ise", language: "TR")]
    // Contributed by ise with the help of Japanese CVVC phonemizer by TUBS
    public class TurkishCVVCPhonemizer : Phonemizer {
        static readonly string[] glottalStops = new string[] { "?", "q" };
        static readonly string[] vowels = new string[] { "a", "e", "ae", "eu", "i", "o", "oe", "u", "ue" };
        static readonly string[] sustainedConsonants = new string[] { "Y", "L", "LY", "M", "N", "NG" };
        static readonly string[] consonants = "9,b,c,ch,d,f,g,h,j,k,l,m,n,ng,p,r,rr,r',s,sh,t,v,w,y,z,by,dy,gy,hy,ky,ly,my,ny,py,ry,ty,Y,L,LY,M,N,NG,-,?,q".Split(',');

        // Store singer in field, will try reading presamp.ini later
        private USinger singer;
        public override void SetSinger(USinger singer) => this.singer = singer;

        // make it quicker to check multiple oto occurrences at once rather than spamming if else if
        private bool checkOtoUntilHit(string[] input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            string color = attr.voiceColor ?? GetParentVoiceColor();
            int shift = attr.toneShift ?? GetParentToneShift();
            int? alt = attr.alternate ?? GetParentAlternate();

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + alt, note.tone + shift, color, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            if (otos.Count > 0) {
                if (otos.Any(oto => (oto.Color ?? string.Empty) == color)) {
                    oto = otos.Find(oto => (oto.Color ?? string.Empty) == color);
                    return true;
                } else {
                    oto = otos.First();
                    return true;
                }
            }
            return false;
        }

        private string[] getNoteStart(SegmentedLyric phonemesCurrent, SegmentedLyric phonemesPrev) {
            string noteStart = phonemesCurrent.StartC1 + phonemesCurrent.StartC2 + phonemesCurrent.Vow;
            bool hasNoPrevNeighbour = (phonemesPrev.StartC1 == "") && (phonemesPrev.Vow == "");
            string[] result = new string[] { "- " + noteStart, noteStart, phonemesCurrent.Lyric };

            if (hasNoPrevNeighbour) {
                return result;
            }

            if (phonemesCurrent.StartC1 == "") {
                if (phonemesPrev.hasConsonantAfterVowel()) { //vc + V
                    if (sustainedConsonants.Contains(phonemesPrev.EndC1.ToUpper())) {
                        result[0] = phonemesPrev.EndC1.ToUpper() + " " + phonemesCurrent.Vow;
                    }
                } else if (phonemesPrev.Vow != "") { //v + V
                    if (phonemesCurrent.Has9BeforeVow) {
                        result[0] = phonemesPrev.Vow + " 9" + phonemesCurrent.Vow;
                    } else {
                        result[0] = phonemesPrev.Vow + " " + phonemesCurrent.Vow;
                    }
                }
                return result;
            }

            return new string[] { noteStart, phonemesCurrent.Lyric };
        }

        private string getAlternativeConsonant(string consonant, string vow) {
            if (vow == "e" || vow == "i" || vow == "ue" || vow == "oe") {
                string y = "y";
                if (consonant.ToUpper() == consonant) {
                    y = "Y";
                }
                if (consonants.Contains(consonant + y)) {
                    return consonant + y;
                }
            }
            return consonant;
        }

        private string[] getConsonantEnding(SegmentedLyric current, bool hasNext, SegmentedLyric next) {
            string v_ = current.Vow + " ";

            if (glottalStops.Contains(current.EndC1))
                return new string[] { v_ + current.EndC1 };

            if (hasNext) {
                if (current.EndC1 != "" && current.EndC1 == next.StartC1)
                    return new string[] { v_ + getAlternativeConsonant(current.EndC1, current.Vow) };

                if (next.StartC1 == "g" || next.StartC1 == "k") {
                    if (current.EndC1 == "n") {
                        current.EndC1 = "ng";
                    } else if (current.EndC1 == "N") {
                        current.EndC1 = "NG";
                    }
                }
            }

            if (!current.hasConsonantAfterVowel()) {
                if (!hasNext) {
                    return new string[] { v_ + "-" };
                } else if (next.hasConsonantBeforeVowel()) { // V + c
                    return new string[] { v_ + getAlternativeConsonant(next.StartC1, next.Vow) };
                } else {//V + v
                    return new string[] { "" };
                }
            }

            return new string[] {
                v_ + current.EndC1 + "-",
                v_ + current.EndC1 };
        }

        private string checkPChTK(string c) {
            if (c == "p" || c == "ch" || c == "t" || c == "k") {
                return "";
            }
            return "-";
        }

        private string convertToOtoStyledLyric(string lyric) {
            lyric = lyric.Replace("ç", "ch");
            lyric = lyric.Replace("ş", "sh");
            lyric = lyric.Replace("ğ", "9");
            lyric = lyric.Replace("æ", "ae");
            lyric = lyric.Replace("E", "ae");
            lyric = lyric.Replace("ı", "eu");
            lyric = lyric.Replace("ö", "oe");
            lyric = lyric.Replace("ü", "ue");
            return lyric;
        }

        private struct SegmentedLyric {
            public string Lyric;
            public string StartC1;
            public string StartC2;
            public string Vow;
            public string EndC1;
            public string EndC2;
            public bool Has9BeforeVow;
            public SegmentedLyric(string originalLyric, string[] phonemes, bool has9BeforeVow) {
                Lyric = originalLyric;
                StartC1 = phonemes[0];
                StartC2 = phonemes[1];
                Vow = phonemes[2];
                EndC1 = phonemes[3];
                EndC2 = phonemes[4];
                Has9BeforeVow = has9BeforeVow;
            }
            public SegmentedLyric(bool has9BeforeVow) {
                Lyric = "";
                StartC1 = StartC2 = Vow = EndC1 = EndC2 = "";
                Has9BeforeVow = has9BeforeVow;
            }
            public bool hasConsonantBeforeVowel() {
                return StartC1 != "";
            }
            public bool hasConsonantAfterVowel() {
                return EndC1 != "";
            }
        }

        private SegmentedLyric getSegmentedPhonemes(string lyric) { //CCVCC
            lyric = convertToOtoStyledLyric(lyric);
            string[] phonemes = new string[] { "", "", "", "", "" };
            bool has9BeforeVow = false;
            int charIndex = 0;

            for (int i = 0; i < 5; i++) {
                string twoCharPhoneme = "";
                if (charIndex + 2 <= lyric.Length) {
                    twoCharPhoneme = lyric.Substring(charIndex, 2);
                }
                string oneCharPhoneme = lyric.Substring(charIndex, 1);

                if (i < 2 && oneCharPhoneme == "9") {
                    has9BeforeVow |= true;
                    charIndex += 1;
                } else if (vowels.Contains(twoCharPhoneme)) {
                    i = 2;
                    phonemes[i] = twoCharPhoneme;
                    charIndex += 2;
                } else if (vowels.Contains(oneCharPhoneme)) {
                    i = 2;
                    phonemes[i] = oneCharPhoneme;
                    charIndex += 1;
                } else if (consonants.Contains(twoCharPhoneme) && i != 2) {
                    phonemes[i] = twoCharPhoneme;
                    charIndex += 2;
                } else if (consonants.Contains(oneCharPhoneme) && i != 2) {
                    phonemes[i] = oneCharPhoneme;
                    charIndex += 1;
                } else { // not found
                    i -= 1;
                    charIndex += 1;
                }

                if (charIndex == lyric.Length) {
                    break;
                }
            }
            return new SegmentedLyric(lyric, phonemes, has9BeforeVow);
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentLyric = note.lyric.Normalize();

            if (currentLyric[0] == '.') {
                return new Result {
                    phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = currentLyric.Substring(1),
                            }
                    },
                };
            }

            SegmentedLyric phonemesCurrent = getSegmentedPhonemes(currentLyric);
            SegmentedLyric phonemesPrev = new SegmentedLyric(false);
            SegmentedLyric phonemesNext = new SegmentedLyric(false);

            if (prevNeighbour != null) {
                phonemesPrev = getSegmentedPhonemes(prevNeighbour.Value.lyric.Normalize());
            }
            if (nextNeighbour != null) {
                phonemesNext = getSegmentedPhonemes(nextNeighbour.Value.lyric.Normalize());
            }

            string[] noteStartInput = getNoteStart(phonemesCurrent, phonemesPrev);
            string[] noteEndInput = getConsonantEnding(phonemesCurrent, nextNeighbour.HasValue, phonemesNext);//phonemesCurrent.EndC1 + "-";
            string noteStart = "", noteEnd = "", noteEndCC = "";

            if (phonemesCurrent.EndC2 != "") { // + VCC
                noteEndInput = new string[] { phonemesCurrent.Vow + " " + phonemesCurrent.EndC1 + checkPChTK(phonemesCurrent.EndC1) };
                noteEndCC = phonemesCurrent.EndC1 + phonemesCurrent.EndC2 + " -";
            }


            if (checkOtoUntilHit(noteStartInput, note, out var o1)) {
                noteStart = o1.Alias;
            }

            if (checkOtoUntilHit(noteEndInput, note, out var o2)) {
                noteEnd = o2.Alias;
            } else {
                noteEnd = "";
            }

            var input = new string[] { noteEndCC };
            if (checkOtoUntilHit(input, note, out var o3)) {
                noteEndCC = o3.Alias;
            } else {
                noteEndCC = "";
            }

            if (noteStart != "" && noteEnd == "" && noteEndCC == "") {
                return new Result {
                    phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = noteStart,
                            }
                    },
                };
            } else if (noteStart != "" && noteEnd != "") {
                int totalDuration = notes.Sum(n => n.duration);
                int lastLengthFromOto = 120;
                double isCVCoeff = 1;
                if (phonemesCurrent.hasConsonantAfterVowel())
                    isCVCoeff = 1.8;
                int isEndCoeff = 2;
                if (nextNeighbour != null) {
                    isEndCoeff = 1;
                    var attr0 = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default; //next first
                    if (singer.TryGetMappedOto(getNoteStart(phonemesNext, phonemesCurrent)[0], note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto0)) {
                        // If overlap is a negative value, vcLength is longer than Preutter
                        if (oto0.Overlap < 0)
                            lastLengthFromOto = timeAxis.MsPosToTickPos(oto0.Preutter - oto0.Overlap);
                        else
                            lastLengthFromOto = timeAxis.MsPosToTickPos(oto0.Preutter);
                    }
                }
                // vcLength depends on the Vel of the note
                var attr1 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default; // current last (noteEnd)
                var vcLength = Convert.ToInt32(Math.Min(totalDuration / (2 * isEndCoeff), lastLengthFromOto * isCVCoeff * (attr1.consonantStretchRatio ?? GetParentConsonantStretchRatio())));

                if (noteEndCC == "") {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = noteStart,
                            },
                            new Phoneme() {
                                phoneme = noteEnd,
                                position = totalDuration - vcLength,
                            },
                        },
                    };
                } else {
                    int ccLengthFromOto = 60;
                    var attr2 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 2) ?? default;
                    if (nextNeighbour != null) {
                        if (singer.TryGetMappedOto(noteEndCC, note.tone + (attr2.toneShift ?? GetParentToneShift()), attr2.voiceColor ?? GetParentVoiceColor(), out var oto1)) {
                            // If overlap is a negative value, vcLength is longer than Preutter
                            if (oto1.Overlap < 0) {
                                ccLengthFromOto = timeAxis.MsPosToTickPos(oto1.Preutter - oto1.Overlap);
                            } else {
                                ccLengthFromOto = timeAxis.MsPosToTickPos(oto1.Preutter);
                            }
                        }
                    }
                    vcLength = Convert.ToInt32(Math.Min(totalDuration / 3, ccLengthFromOto * (attr1.consonantStretchRatio ?? GetParentConsonantStretchRatio())));
                    var ccLength = Convert.ToInt32(Math.Min(totalDuration / 3, lastLengthFromOto * (attr2.consonantStretchRatio ?? GetParentConsonantStretchRatio())));

                    List<PhonemeExpression> exp = new List<PhonemeExpression>();
                    PhonemeExpression e = new PhonemeExpression() { abbr = Core.Format.Ustx.VOL, value = 70 };
                    exp.Add(e);

                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = noteStart,
                            },
                            new Phoneme() {
                                phoneme = noteEnd,
                                position = totalDuration - vcLength - ccLength,
                            },
                            new Phoneme() {
                                phoneme = noteEndCC,
                                position = totalDuration - ccLength,
                                expressions = exp,
                            },
                        },
                    };
                }
            }

            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = currentLyric,
                    }
                },
            };
        }
    }
}
