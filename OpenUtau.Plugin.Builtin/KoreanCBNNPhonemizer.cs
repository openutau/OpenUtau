using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core;

namespace OpenUtau.Plugin.Builtin {
    /// Phonemizer for 'KOR CBNN' ///
    [Phonemizer("Korean CBNN Phonemizer", "KO CBNN", "EX3", language: "KO")]

    public class KoreanCBNNPhonemizer : BaseKoreanPhonemizer {

        public override void SetSinger(USinger singer) {
            if (this.singer == singer) {return;}
            this.singer = singer;
            if (this.singer == null) {return;}

            if (this.singer.SingerType != USingerType.Classic){return;}
        }


        static readonly Dictionary<string, string> FIRST_CONSONANTS = new Dictionary<string, string>(){
            {"ㄱ", "g"},
            {"ㄲ", "gg"},
            {"ㄴ", "n"},
            {"ㄷ", "d"},
            {"ㄸ", "dd"},
            {"ㄹ", "r"},
            {"ㅁ", "m"},
            {"ㅂ", "b"},
            {"ㅃ", "bb"},
            {"ㅅ", "s"},
            {"ㅆ", "ss"},
            {"ㅇ", ""},
            {"ㅈ", "j"},
            {"ㅉ", "jj"},
            {"ㅊ", "ch"},
            {"ㅋ", "k"},
            {"ㅌ", "t"},
            {"ㅍ", "p"},
            {"ㅎ", "h"},
            {"null", ""} // 뒤 글자가 없을 때를 대비
            };
        
        static readonly Dictionary<string, string[]> MIDDLE_VOWELS = new Dictionary<string, string[]>(){
            {"ㅏ", new string[3]{"a", "", "a"}},
            {"ㅐ", new string[3]{"e", "", "e"}},
            {"ㅑ", new string[3]{"ya", "y", "a"}},
            {"ㅒ", new string[3]{"ye", "y", "e"}},
            {"ㅓ", new string[3]{"eo", "", "eo"}},
            {"ㅔ", new string[3]{"e", "", "e"}},
            {"ㅕ", new string[3]{"yeo", "y", "eo"}},
            {"ㅖ", new string[3]{"ye", "y", "e"}},
            {"ㅗ", new string[3]{"o", "", "o"}},
            {"ㅘ", new string[3]{"wa", "w", "a"}},
            {"ㅙ", new string[3]{"we", "w", "e"}},
            {"ㅚ", new string[3]{"we", "w", "e"}},
            {"ㅛ", new string[3]{"yo", "y", "o"}},
            {"ㅜ", new string[3]{"u", "", "u"}},
            {"ㅝ", new string[3]{"weo", "w", "eo"}},
            {"ㅞ", new string[3]{"we", "w", "e"}},
            {"ㅟ", new string[3]{"wi", "w", "i"}},
            {"ㅠ", new string[3]{"yu", "y", "u"}},
            {"ㅡ", new string[3]{"eu", "", "eu"}},
            {"ㅢ", new string[3]{"i", "", "i"}}, // ㅢ는 ㅣ로 발음
            {"ㅣ", new string[3]{"i", "", "i"}},
            {"null", new string[3]{"", "", ""}} // 뒤 글자가 없을 때를 대비
            };
        static readonly Dictionary<string, string[]> LAST_CONSONANTS = new Dictionary<string, string[]>(){
             //ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ
            {"ㄱ", new string[]{"k", ""}},
            {"ㄲ", new string[]{"k", ""}},
            {"ㄳ", new string[]{"k", ""}},
            {"ㄴ", new string[]{"n", "2"}},
            {"ㄵ", new string[]{"n", "2"}},
            {"ㄶ", new string[]{"n", "2"}},
            {"ㄷ", new string[]{"t", "1"}},
            {"ㄹ", new string[]{"l", "4"}},
            {"ㄺ", new string[]{"k", ""}},
            {"ㄻ", new string[]{"m", "1"}},
            {"ㄼ", new string[]{"l", "4"}},
            {"ㄽ", new string[]{"l", "4"}},
            {"ㄾ", new string[]{"l", "4"}},
            {"ㄿ", new string[]{"p", "1"}},
            {"ㅀ", new string[]{"l", "4"}},
            {"ㅁ", new string[]{"m", "1"}},
            {"ㅂ", new string[]{"p", "1"}},
            {"ㅄ", new string[]{"p", "1"}},
            {"ㅅ", new string[]{"t", "1"}},
            {"ㅆ", new string[]{"t", "1"}},
            {"ㅇ", new string[]{"ng", "3"}},
            {"ㅈ", new string[]{"t", "1"}},
            {"ㅊ", new string[]{"t", "1"}},
            {"ㅋ", new string[]{"k", ""}},
            {"ㅌ", new string[]{"t", "1"}},
            {"ㅍ", new string[]{"p", "1"}},
            {"ㅎ", new string[]{"t", "1"}},
            {" ", new string[]{"", ""}}, // no batchim
            {"null", new string[]{"", ""}} // 뒤 글자가 없을 때를 대비
            };
        
        private Result ConvertForCBNN(Note[] notes, string[] prevLyric, string[] thisLyric, string[] nextLyric, Note? nextNeighbour) {
            string thisMidVowelHead;
            string thisMidVowelTail;

            
            int totalDuration = notes.Sum(n => n.duration);
            Note note = notes[0];

            string soundBeforeEndSound = thisLyric[2] == " " ? thisLyric[1] : thisLyric[2];
            string thisMidVowelForEnd;

            thisMidVowelForEnd = MIDDLE_VOWELS.ContainsKey(soundBeforeEndSound) ? MIDDLE_VOWELS[soundBeforeEndSound][2] : LAST_CONSONANTS[soundBeforeEndSound][0];
            string endSound = $"{thisMidVowelForEnd} -";

            bool isItNeedsFrontCV;
            bool isRelaxedVC;
            bool isItNeedsVC;
            bool isItNeedsVV;
            bool isItNeedsVSv; // V + Semivowel, example) a y, a w 
            bool isItNeedsEndSound;

            isItNeedsVV = prevLyric[2] == " " && thisLyric[0] == "ㅇ" && PLAIN_VOWELS.Contains(thisLyric[1]);
            
            isItNeedsFrontCV = prevLyric[0] == "null" || prevLyric[1] == "null" || (prevLyric[2] != "null" && HARD_BATCHIMS.Contains(prevLyric[2]) && prevLyric[2] != "ㅁ");
            isRelaxedVC = nextLyric[0] == "null" || nextLyric[1] == "null" || ((thisLyric[2] == nextLyric[0]) && (KoreanPhonemizerUtil.nasalSounds.ContainsKey(thisLyric[2]) || thisLyric[2] == "ㄹ"));
            isItNeedsEndSound = (nextLyric[0] == "null" || nextLyric[1] == "null") && nextNeighbour == null;
            if (thisLyric.All(part => part == null)) {
                return GenerateResult(FindInOto(note.lyric, note));
            }
            else {
                thisMidVowelHead = $"{MIDDLE_VOWELS[thisLyric[1]][1]}";
                thisMidVowelTail = $"{MIDDLE_VOWELS[thisLyric[1]][2]}";
            }
            
            string CV = $"{FIRST_CONSONANTS[thisLyric[0]]}{thisMidVowelHead}{thisMidVowelTail}{LAST_CONSONANTS[thisLyric[2]][1]}"; 
            if (FindInOto(CV, note, true) == null) {
                CV = CV.Substring(0, CV.Length - 1);
            }
            string frontCV;
            string batchim;
            string VC = $"{thisMidVowelTail} {FIRST_CONSONANTS[nextLyric[0]]}{MIDDLE_VOWELS[nextLyric[1]][1]}";
            string VV = $"{MIDDLE_VOWELS[prevLyric[1]][2]} {thisMidVowelTail}";
            string VSv = $"{thisMidVowelTail} {MIDDLE_VOWELS[nextLyric[1]][1]}";
            isItNeedsVSv = thisLyric[2] == " " && nextLyric[0] == "ㅇ" && !PLAIN_VOWELS.Contains(nextLyric[1]) && FindInOto(VSv, note, true) != null;
            isItNeedsVC = thisLyric[2] == " " && nextLyric[0] != "ㅇ" && nextLyric[0] != "null";

            frontCV = $"- {CV}";
            if (FindInOto(frontCV, note, true) == null) {
                frontCV = $"-{CV}";
                if (FindInOto(frontCV, note, true) == null) {
                    frontCV = CV;
                }
            }

            if (FindInOto(VC, note, true) == null) {
                if (VC.EndsWith("w") || VC.EndsWith("y")) {
                    VC = VC.Substring(0, VC.Length - 1);
                }
                if (FindInOto(VC, note, true) == null) {
                    isItNeedsVC = false;
                }
            }

            if (isItNeedsVV) {CV = VV;}
        

            if (thisLyric[2] == " " && isItNeedsVC) { // no batchim, needs VC
                if (isItNeedsFrontCV){
                    return GenerateResult(FindInOto(frontCV, note), FindInOto(VC, note), totalDuration, 120, 3);
                }
                return GenerateResult(FindInOto(CV, note), FindInOto(VC, note), totalDuration, 120, 3);
            }

            if (thisLyric[2] == " " && isItNeedsVSv) { // no batchim, needs VSv
                if (isItNeedsFrontCV){
                    return GenerateResult(FindInOto(frontCV, note), FindInOto(VSv, note), totalDuration, 120, 3);
                }
                return GenerateResult(FindInOto(CV, note), FindInOto(VSv, note), totalDuration, 120, 3);
            }

            if (thisLyric[2] == " ") { // no batchim, doesn't need VC
                if (isItNeedsFrontCV){
                    return isItNeedsEndSound ? 
                    GenerateResult(FindInOto(frontCV, note), FindInOto(endSound, note), totalDuration, 8)
                    : GenerateResult(FindInOto(frontCV, note));
                }
                return isItNeedsEndSound ? 
                    GenerateResult(FindInOto(CV, note), FindInOto(endSound, note), totalDuration, 8)
                    : GenerateResult(FindInOto(CV, note));
            }
            
            batchim = $"{thisMidVowelTail}{LAST_CONSONANTS[thisLyric[2]][0]}";
            
            
            if (thisLyric[2] == "ㅁ" || ! HARD_BATCHIMS.Contains(thisLyric[2])) { // batchim ㅁ + ㄴ ㄹ ㅇ
                if (isItNeedsFrontCV){
                    return isRelaxedVC ? 
                    GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                    : GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), FindInOto(endSound, note), totalDuration, 120, 2, 3);
                }
                return isRelaxedVC ? 
                GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                : GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), FindInOto(endSound, note), totalDuration, 120, 2, 3);
            }
            else {
                if (isItNeedsFrontCV){
                    return isRelaxedVC ? 
                    GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                    : GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, 120, 5);
                }
                return isRelaxedVC ? 
                GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                : GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, 120, 5);
            }
            
        }

        private string? FindInOto(String phoneme, Note note, bool nullIfNotFound=false){
            return FindInOto(singer, phoneme, note, nullIfNotFound);
        }


        public override Result ConvertPhonemes(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            Note note = notes[0];

            Hashtable lyrics = KoreanPhonemizerUtil.Variate(prevNeighbour, note, nextNeighbour);
            string[] prevLyric = new string[]{ // "ㄴ", "ㅑ", "ㅇ"
                (string)lyrics[0], 
                (string)lyrics[1], 
                (string)lyrics[2]
                };
            string[] thisLyric = new string[]{ // "ㄴ", "ㅑ", "ㅇ"
                (string)lyrics[3], 
                (string)lyrics[4], 
                (string)lyrics[5]
                };
            string[] nextLyric = new string[]{ // "ㄴ", "ㅑ", "ㅇ"
                (string)lyrics[6], 
                (string)lyrics[7], 
                (string)lyrics[8]
                };

            if (thisLyric[0] == "null") { 
                return GenerateResult(FindInOto(notes[0].lyric, notes[0]));
            }
            
            return ConvertForCBNN(notes, prevLyric, thisLyric, nextLyric, nextNeighbour);

        }
        

        public override Result GenerateEndSound(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            Note note = notes[0];
            if (prevNeighbour == null) {
                return GenerateResult(FindInOto(note.lyric, note));
            }

            Note prevNeighbour_ = (Note)prevNeighbour;
            Hashtable lyrics = KoreanPhonemizerUtil.Separate(prevNeighbour_.lyric);

            string[] prevLyric = new string[]{ // "ㄴ", "ㅑ", "ㅇ"
                (string)lyrics[0], 
                (string)lyrics[1], 
                (string)lyrics[2]
                };

            string soundBeforeEndSound = prevLyric[2] == " " ? prevLyric[1] : prevLyric[2];
            string endSound = note.lyric;
            string prevMidVowel;

            prevMidVowel = MIDDLE_VOWELS.ContainsKey(soundBeforeEndSound) ? MIDDLE_VOWELS[soundBeforeEndSound][2] : LAST_CONSONANTS[soundBeforeEndSound][0];
            
            if (FindInOto($"{prevMidVowel} {endSound}", note, true) == null) {
                if (FindInOto($"{prevMidVowel}{endSound}", note, true) == null) {
                    return GenerateResult(FindInOto($"{endSound}", note));
                }
                return GenerateResult(FindInOto($"{prevMidVowel}{endSound}", note, true));
            }
            return GenerateResult(FindInOto($"{prevMidVowel} {endSound}", note));            
        }
    }
}
