using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core;

namespace OpenUtau.Plugin.Builtin {
    /// Phonemizer for 'KOR CVVC' ///
    [Phonemizer("Korean CVVC Phonemizer", "KO CVVC", "RYUUSEI & EX3", language: "KO")]
    public class KoreanCVVCPhonemizer : BaseKoreanPhonemizer {
        public override void SetSinger(USinger singer) {
            if (this.singer == singer) {return;}
            this.singer = singer;
            if (this.singer == null) {return;}

            if (this.singer.SingerType != USingerType.Classic){return;}
        }

        protected override bool additionalTest(string lyric) {
            return IsENPhoneme(lyric);
        }

        static readonly Dictionary<string, string> FIRST_CONSONANTS = new Dictionary<string, string>(){
            {"ㄱ", "g"},
            {"ㄲ", "kk"},
            {"ㄴ", "n"},
            {"ㄷ", "d"},
            {"ㄸ", "tt"},
            {"ㄹ", "r"},
            {"ㅁ", "m"},
            {"ㅂ", "b"},
            {"ㅃ", "pp"},
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
            {"null", ""}, // 뒤 글자가 없을 때를 대비
            // EN Phonemes
            {"th", "th"},
            {"v", "v"},
            {"f", "f"},
            {"rr", "rr"}
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
            {"ㅢ", new string[3]{"eui", "", "i"}}, 
            {"ㅣ", new string[3]{"i", "", "i"}},
            {"null", new string[3]{"", "", ""}} // 뒤 글자가 없을 때를 대비
            };
        static readonly Dictionary<string, string[]> LAST_CONSONANTS = new Dictionary<string, string[]>(){
             //ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ
            {"ㄱ", new string[]{"K", ""}},
            {"ㄲ", new string[]{"K", ""}},
            {"ㄳ", new string[]{"K", ""}},
            {"ㄴ", new string[]{"n", "2"}},
            {"ㄵ", new string[]{"n", "2"}},
            {"ㄶ", new string[]{"n", "2"}},
            {"ㄷ", new string[]{"T", "1"}},
            {"ㄹ", new string[]{"l", "4"}},
            {"ㄺ", new string[]{"K", ""}},
            {"ㄻ", new string[]{"m", "1"}},
            {"ㄼ", new string[]{"l", "4"}},
            {"ㄽ", new string[]{"l", "4"}},
            {"ㄾ", new string[]{"l", "4"}},
            {"ㄿ", new string[]{"P", "1"}},
            {"ㅀ", new string[]{"l", "4"}},
            {"ㅁ", new string[]{"m", "1"}},
            {"ㅂ", new string[]{"P", "1"}},
            {"ㅄ", new string[]{"P", "1"}},
            {"ㅅ", new string[]{"T", "1"}},
            {"ㅆ", new string[]{"T", "1"}},
            {"ㅇ", new string[]{"ng", "3"}},
            {"ㅈ", new string[]{"T", "1"}},
            {"ㅊ", new string[]{"T", "1"}},
            {"ㅋ", new string[]{"L", ""}},
            {"ㅌ", new string[]{"T", "1"}},
            {"ㅍ", new string[]{"P", "1"}},
            {"ㅎ", new string[]{"T", "1"}},
            {" ", new string[]{"", ""}}, // no batchim
            {"null", new string[]{"", ""}} // 뒤 글자가 없을 때를 대비
            };

        static readonly Dictionary<string, string> BATCHIM_ROMAJ = new Dictionary<string, string>(){ // to handle EN Phoneme's batchim, for example: theong = theo , eo ng 
            {"K", "ㄱ"},
            {"k", "ㄱ"},
            {"T", "ㄷ"},
            {"t", "ㄷ"},
            {"P", "ㅂ"},
            {"p", "ㅂ"},
            {"ng", "ㅇ"},
            {"n", "ㄴ"},
            {"l", "ㄹ"},
            {"m", "ㅁ"},  
        };

        static readonly string[][] EN_PHONEMES = {
            "theui=th,ㅢ theu=th,ㅡ theo=th,ㅓ tha=th,ㅏ thi=th,ㅣ thu=th,ㅜ the=th,ㅔ tho=th,ㅗ".Split(),
            "thyeo=th,ㅕ thya=th,ㅑ thyu=th,ㅜ thye=th,ㅖ thyo=th,ㅛ".Split(),
            "thweo=th,ㅝ thwa=th,ㅘ thwi=th,ㅟ thwe=th,ㅞ".Split(),

            "veui=v,ㅢ veu=v,ㅡ veo=v,ㅓ va=v,ㅏ vi=v,ㅣ vu=v,ㅜ ve=v,ㅔ vo=v,ㅗ".Split(),
            "vyeo=v,ㅕ vya=v,ㅑ vyu=v,ㅜ vye=v,ㅖ vyo=v,ㅛ".Split(),
            "vweo=v,ㅝ vwa=v,ㅘ vwi=v,ㅟ vwe=v,ㅞ".Split(),

            "feui=f,ㅢ feu=f,ㅡ feo=f,ㅓ fa=f,ㅏ fi=f,ㅣ fu=f,ㅜ fe=f,ㅔ fo=f,ㅗ".Split(),
            "fyeo=f,ㅕ fya=f,ㅑ fyu=f,ㅜ fye=f,ㅖ fyo=f,ㅛ".Split(),
            "fweo=f,ㅝ fwa=f,ㅘ fwi=f,ㅟ fwe=f,ㅞ".Split(),

            "rreui=rr,ㅢ rreu=rr,ㅡ rreo=rr,ㅓ rra=rr,ㅏ rri=rr,ㅣ rru=rr,ㅜ rre=rr,ㅔ rro=rr,ㅗ".Split(),
            "rryeo=rr,ㅕ rrya=rr,ㅑ rryu=rr,ㅜ rrye=rr,ㅖ rryo=rr,ㅛ".Split(),
            "rrweo=rr,ㅝ rrwa=rr,ㅘ rrwi=rr,ㅟ rrwe=rr,ㅞ".Split(),
        };


        private Result ConvertForCVVC(Note[] notes, string[] prevLyric, string[] thisLyric, string[] nextLyric, Note? nextNeighbour) {
            string thisMidVowelHead;
            string thisMidVowelTail;
            
            int totalDuration = notes.Sum(n => n.duration);
            Note note = notes[0];

            string soundBeforeEndSound = thisLyric[2] == " " ? thisLyric[1] : thisLyric[2];
            string thisMidVowelForEnd;

            thisMidVowelForEnd = MIDDLE_VOWELS.ContainsKey(soundBeforeEndSound) ? MIDDLE_VOWELS[soundBeforeEndSound][2] : LAST_CONSONANTS[soundBeforeEndSound][0];
            string endSound = $"{thisMidVowelForEnd} R";

            bool isItNeedsFrontCV;
            bool isItNeedsVC;
            bool isItNeedsVV;
            bool isItNeedsVSv; // V + Semivowel, example) a y, a w 
            bool isItNeedsEndSound;
            
            isItNeedsFrontCV = prevLyric[0] == "null" || prevLyric[1] == "null";
            isItNeedsEndSound = (nextLyric[0] == "null" || nextLyric[1] == "null") && nextNeighbour == null;
            if (thisLyric.All(part => part == null)) {
                return GenerateResult(FindInOto(note.lyric, note));
            }
            else {
                thisMidVowelHead = $"{MIDDLE_VOWELS[thisLyric[1]][1]}";
                thisMidVowelTail = $"{MIDDLE_VOWELS[thisLyric[1]][2]}";
            }
            
            string CV;
            if (thisLyric[0] == "ㄹ" && prevLyric[2] == "ㄹ"){ // ㄹㄹ = l
                CV = $"l{MIDDLE_VOWELS[thisLyric[1]][0]}"; 
            }
            else {
                CV = $"{FIRST_CONSONANTS[thisLyric[0]]}{MIDDLE_VOWELS[thisLyric[1]][0]}"; 
            }

            string frontCV;
            string batchim = null;
            string VC = $"{thisMidVowelTail} {FIRST_CONSONANTS[nextLyric[0]]}{MIDDLE_VOWELS[nextLyric[1]][1]}";
            string VV = $"{MIDDLE_VOWELS[prevLyric[1]][2]} {thisMidVowelHead}{thisMidVowelTail}";
            string VSv = $"{thisMidVowelTail} {MIDDLE_VOWELS[nextLyric[1]][1]}";
            string CC = null;

            isItNeedsVV = prevLyric[2] == " " && thisLyric[0] == "ㅇ";
            if (prevLyric[2] == "ㅇ" && thisLyric[0] == "ㅇ") { // 
                isItNeedsVV = true; 
                VV = $"{LAST_CONSONANTS["ㅇ"][0]} {thisMidVowelHead}{thisMidVowelTail}";
            }
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

            if (isItNeedsVV && FindInOto(VV, note, true) != null) {
                CV = VV;
                if (isItNeedsVSv) { // if use a wa, don't use a w wa
                    isItNeedsVSv = false;
                }
            }
        

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
            
            batchim = $"{thisMidVowelTail} {LAST_CONSONANTS[thisLyric[2]][0]}";
            
            if (FindInOto(batchim, note, true) == null) {
                batchim = batchim.ToLower(); // try to use lower-cased batchim
            }

            if (nextLyric[0] == "null" || nextLyric[0] == "ㅇ") { // batchim, doesn't need CC
                if (isItNeedsFrontCV ){
                    return GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, 8);
                }
                return GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, 8);
            }
            CC = $"{LAST_CONSONANTS[thisLyric[2]][0]} {FIRST_CONSONANTS[nextLyric[0]]}{MIDDLE_VOWELS[nextLyric[1]][1]}";
            
            if (FindInOto(CC, note, true) == null) {
                if (CC.EndsWith("w") || CC.EndsWith("y")) {
                    CC = CC.Substring(0, CC.Length - 1);
                }
            }

            if (FindInOto(CC, note, true) != null) { // batchim + CC
                if (isItNeedsFrontCV){
                    return GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), FindInOto(CC, note), totalDuration, 120, 2, 3);
                }
                return GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), FindInOto(CC, note), totalDuration, 120, 2, 3);
            }
            else { // batchim + no CC
                if (isItNeedsFrontCV){
                    GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, 120, 5);
                }
                return GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, 120, 5);
            }
            
        }

        private string? FindInOto(String phoneme, Note note, bool nullIfNotFound=false){
            return FindInOto(singer, phoneme, note, nullIfNotFound);
        }

        private bool IsENPhoneme(String phoneme) {
            bool isENPhoneme = false;
            if (phoneme.StartsWith("rr") || phoneme.StartsWith("th") || phoneme.StartsWith("f") || phoneme.StartsWith("v")){
                isENPhoneme = true;
            }
            return isENPhoneme;
        }

        public override Result ConvertPhonemes(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            Note note = notes[0];
            bool prevIsEN = false;
            bool currIsEN = false; 
            bool nextIsEN = false;
            string[] prevENPhones = new string[3]{"", "", ""};
            string[] currENPhones = new string[3]{"", "", ""};
            string[] nextENPhones = new string[3]{"", "", ""};
            bool exitLoop = false; 

            if (IsENPhoneme(note.lyric)) {
                foreach (string[] _ in EN_PHONEMES) {
                    if (exitLoop) {
                        exitLoop = false;
                        break;
                        }
                    foreach (string p in _){
                        string grapheme = p.Split("=")[0];
                        
                        if (!note.lyric.StartsWith(grapheme)) {
                            continue;
                        }
                        string[] temp = p.Split("=")[1].Split(",");
                        currENPhones[0] = temp[0];
                        currENPhones[1] = temp[1];

                        if (note.lyric.Length != grapheme.Length) {
                            currENPhones[2] = BATCHIM_ROMAJ[note.lyric.Substring(grapheme.Length)];
                        }
                        else {
                            currENPhones[2] = " ";
                        }

                        currIsEN = true;
                        exitLoop = true;
                        break;
                    }
                }
            }
            if (prev != null && IsENPhoneme(((Note)prev).lyric)){
                foreach (string[] _ in EN_PHONEMES) {
                    if (exitLoop) {
                        exitLoop = false;
                        break;
                        }
                    foreach (string p in _){
                        string grapheme = p.Split("=")[0];
                        
                        if (!((Note)prev).lyric.StartsWith(grapheme)) {
                            continue;
                        }
                        string[] temp = p.Split("=")[1].Split(",");
                        prevENPhones[0] = temp[0];
                        prevENPhones[1] = temp[1];

                        if (((Note)prev).lyric.Length != grapheme.Length) {
                            prevENPhones[2] = BATCHIM_ROMAJ[((Note)prev).lyric.Substring(grapheme.Length)];
                        }
                        else {
                            prevENPhones[2] = " ";
                        }
                        
                        prevIsEN = true;
                        exitLoop = true;
                        break;
                    }
                }
            }
            if (next != null && IsENPhoneme(((Note)next).lyric)){
                foreach (string[] _ in EN_PHONEMES) {
                    if (exitLoop) {
                        exitLoop = false;
                        break;
                        }
                    foreach (string p in _){
                        string grapheme = p.Split("=")[0];
                        
                        if (!((Note)next).lyric.StartsWith(grapheme)) {
                            continue;
                        }
                        string[] temp = p.Split("=")[1].Split(",");
                        nextENPhones[0] = temp[0];
                        nextENPhones[1] = temp[1];

                        if (((Note)next).lyric.Length != grapheme.Length) {
                            prevENPhones[2] = BATCHIM_ROMAJ[((Note)next).lyric.Substring(grapheme.Length)];
                        }
                        else {
                            prevENPhones[2] = " ";
                        }
                        nextIsEN = true;
                        exitLoop = true;
                        break;
                    }
                }
            }
            
            if (!KoreanPhonemizerUtil.IsHangeul(note.lyric) && !prevIsEN && !currIsEN && !nextIsEN){
                return GenerateResult(FindInOto(notes[0].lyric, notes[0]));
            }

            Hashtable lyrics;
            if (KoreanPhonemizerUtil.IsHangeul(note.lyric)){
                lyrics = KoreanPhonemizerUtil.Variate(prevNeighbour, note, nextNeighbour);
            }
            else {
                // handle current phoneme which is not hangeul, but have to supported by phonemizer - tha, thi, thu, fyeo... etc.
                lyrics = new Hashtable() { [0] = "null", [1] = "null", [2] = "null", [3] = "null", [4] = "null", [5] = "null", [6] = "null", [7] = "null", [8] = "null",};// init into all null
                
                if (prevNeighbour != null && !IsENPhoneme(((Note)prevNeighbour).lyric)) {
                    Hashtable t = KoreanPhonemizerUtil.Variate(null, (Note)prevNeighbour, null);
                    lyrics[0] = (string)t[3];
                    lyrics[1] = (string)t[4];
                    lyrics[2] = (string)t[5];
                }
                if (nextNeighbour != null && !IsENPhoneme(((Note)nextNeighbour).lyric)) {
                    Hashtable t = KoreanPhonemizerUtil.Variate(null, (Note)nextNeighbour, null);
                    lyrics[6] = (string)t[3];
                    lyrics[7] = (string)t[4];
                    lyrics[8] = (string)t[5];
                }
            }

            string[] prevLyric = new string[]{ // "ㄴ", "ㅑ", "ㅇ"
                prevIsEN ? prevENPhones[0] : (string)lyrics[0], 
                prevIsEN ? prevENPhones[1]:(string)lyrics[1], 
                prevIsEN ? prevENPhones[2]:(string)lyrics[2]
                };
            string[] thisLyric = new string[]{ // "ㄴ", "ㅑ", "ㅇ"
                currIsEN ? currENPhones[0] : (string)lyrics[3], 
                currIsEN ? currENPhones[1] : (string)lyrics[4], 
                currIsEN? currENPhones[2] : (string)lyrics[5]
                };
            string[] nextLyric = new string[]{ // "ㄴ", "ㅑ", "ㅇ"
                nextIsEN ? nextENPhones[0] : (string)lyrics[6], 
                nextIsEN ? nextENPhones[1] : (string)lyrics[7], 
                nextIsEN ? nextENPhones[2] : (string)lyrics[8]
                };

            if (thisLyric[0] == "null") { 
                return GenerateResult(FindInOto(notes[0].lyric, notes[0]));
            }
            if (prevLyric[2] != " " && prevIsEN && thisLyric[0] == "ㅇ") { // perform yeoneum when 'EN Phoneme with batchim' came
                thisLyric[0] = prevLyric[2];
                prevLyric[2] = " ";
            }
            return ConvertForCVVC(notes, prevLyric, thisLyric, nextLyric, nextNeighbour);
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
