using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core;

namespace OpenUtau.Plugin.KoreanCOC
{
    /// <summary>
    /// Phonemizer for 'KOR COC' (Simplified CVVC)
    /// </summary>
    [Phonemizer("Korean COC Phonemizer", "KO COC", "COCTeam", language: "KO")]
    public class KoreanCOCPhonemizer : Phonemizer
    {

        private USinger singer;

        public override void SetSinger(USinger singer)
        {
            this.singer = singer;
        }

        // ==============================================================================
        // 1. 자음 (First Consonants)
        // ==============================================================================
        static readonly Dictionary<string, string> FIRST_CONSONANTS = new Dictionary<string, string>(){
            {"ㄱ", "g"}, {"ㄴ", "n"}, {"ㄷ", "d"}, {"ㄹ", "r"}, {"ㅁ", "m"},
            {"ㅂ", "b"}, {"ㅅ", "s"}, {"ㅇ", ""}, {"ㅈ", "j"}, {"ㅊ", "ch"},
            {"ㅋ", "k"}, {"ㅌ", "t"}, {"ㅍ", "p"}, {"ㅎ", "h"}, {"ㄲ", "kk"},
            {"ㄸ", "tt"}, {"ㅃ", "pp"}, {"ㅉ", "jj"}, {"ㅆ", "ss"},
            {"null", ""},
            {"f", "f"}, {"z", "z"}, {"v", "v"}, {"c", "c"}
        };

        // ==============================================================================
        // 2. 모음 (Middle Vowels)
        // ==============================================================================
        static readonly Dictionary<string, string[]> MIDDLE_VOWELS = new Dictionary<string, string[]>(){
            {"ㅏ", new string[3]{"a", "", "a"}}, {"ㅣ", new string[3]{"i", "", "i"}},
            {"ㅜ", new string[3]{"u", "", "u"}}, {"ㅔ", new string[3]{"e", "", "e"}},
            {"ㅐ", new string[3]{"e", "", "e"}}, {"ㅗ", new string[3]{"o", "", "o"}},
            {"ㅡ", new string[3]{"eu", "", "eu"}}, {"ㅓ", new string[3]{"eo", "", "eo"}},
            {"ㅑ", new string[3]{"ya", "y", "a"}}, {"ㅠ", new string[3]{"yu", "y", "u"}},
            {"ㅖ", new string[3]{"ye", "y", "e"}}, {"ㅒ", new string[3]{"ye", "y", "e"}},
            {"ㅛ", new string[3]{"yo", "y", "o"}}, {"ㅕ", new string[3]{"yeo", "y", "eo"}},
            {"ㅘ", new string[3]{"wa", "w", "a"}}, {"ㅟ", new string[3]{"wi", "w", "i"}},
            {"ㅞ", new string[3]{"we", "w", "e"}}, {"ㅚ", new string[3]{"we", "w", "e"}},
            {"ㅙ", new string[3]{"we", "w", "e"}}, {"ㅝ", new string[3]{"weo", "w", "eo"}},
            {"ㅢ", new string[3]{"eui", "", "i"}},
            {"null", new string[3]{"", "", ""}}
        };

        // ==============================================================================
        // 3. 종성 (Last Consonants)
        // ==============================================================================
        static readonly Dictionary<string, string[]> LAST_CONSONANTS = new Dictionary<string, string[]>(){
            {"ㄱ", new string[]{"K", ""}}, {"ㄴ", new string[]{"N", ""}}, {"ㄷ", new string[]{"T", ""}},
            {"ㄹ", new string[]{"L", ""}}, {"ㅁ", new string[]{"M", ""}}, {"ㅂ", new string[]{"P", ""}},
            {"ㅇ", new string[]{"NG", ""}},
            {"ㄲ", new string[]{"K", ""}}, {"ㄳ", new string[]{"K", ""}}, {"ㄵ", new string[]{"N", ""}},
            {"ㄶ", new string[]{"N", ""}}, {"ㄺ", new string[]{"K", ""}}, {"ㄻ", new string[]{"M", ""}},
            {"ㄼ", new string[]{"L", ""}}, {"ㄽ", new string[]{"L", ""}}, {"ㄾ", new string[]{"L", ""}},
            {"ㄿ", new string[]{"P", ""}}, {"ㅀ", new string[]{"L", ""}}, {"ㅄ", new string[]{"P", ""}},
            {"ㅅ", new string[]{"T", ""}}, {"ㅆ", new string[]{"T", ""}}, {"ㅈ", new string[]{"T", ""}},
            {"ㅊ", new string[]{"T", ""}}, {"ㅋ", new string[]{"K", ""}}, {"ㅌ", new string[]{"T", ""}},
            {"ㅍ", new string[]{"P", ""}}, {"ㅎ", new string[]{"T", ""}},
            {" ", new string[]{"", ""}}, {"null", new string[]{"", ""}}
        };

        // ==============================================================================
        // 4. 한글 처리 유틸리티
        // ==============================================================================
        private static class KoreanParser
        {
            private static readonly char[] CHOSUNG = {
                'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
                'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'
            };
            private static readonly char[] JUNGSUNG = {
                'ㅏ', 'ㅐ', 'ㅑ', 'ㅒ', 'ㅓ', 'ㅔ', 'ㅕ', 'ㅖ', 'ㅗ', 'ㅘ',
                'ㅙ', 'ㅚ', 'ㅛ', 'ㅜ', 'ㅝ', 'ㅞ', 'ㅟ', 'ㅠ', 'ㅡ', 'ㅢ', 'ㅣ'
            };
            private static readonly char[] JONGSUNG = {
                ' ', 'ㄱ', 'ㄲ', 'ㄳ', 'ㄴ', 'ㄵ', 'ㄶ', 'ㄷ', 'ㄹ', 'ㄺ',
                'ㄻ', 'ㄼ', 'ㄽ', 'ㄾ', 'ㄿ', 'ㅀ', 'ㅁ', 'ㅂ', 'ㅄ', 'ㅅ',
                'ㅆ', 'ㅇ', 'ㅈ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'
            };

            public static bool IsHangeul(string text)
            {
                if (string.IsNullOrEmpty(text)) return false;
                char c = text[0];
                return c >= 0xAC00 && c <= 0xD7A3;
            }

            public static string[] Separate(string text)
            {
                if (!IsHangeul(text)) return new string[] { text, "null", "null" };

                char c = text[0];
                int code = c - 0xAC00;
                int jong = code % 28;
                int jung = (code / 28) % 21;
                int cho = code / 28 / 21;

                string sCho = CHOSUNG[cho].ToString();
                string sJung = JUNGSUNG[jung].ToString();
                string sJong = JONGSUNG[jong] == ' ' ? " " : JONGSUNG[jong].ToString();

                return new string[] { sCho, sJung, sJong };
            }
        }

        private string NormalizeJamo(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length != 1) return input;
            char c = input[0];
            if (c >= 0x3131 && c <= 0x314E) return input;
            return input;
        }

        private string[] ParseRomaji(string lyric)
        {
            if (string.IsNullOrEmpty(lyric)) { return new string[] { "null", "null", "null" }; }

            string[] doubleConsonants = { "kk", "tt", "pp", "jj", "ss", "ch", "ng", "th", "ph", "gh", "wh" };
            foreach (var c in doubleConsonants)
            {
                if (lyric.StartsWith(c))
                {
                    string vowel = lyric.Substring(c.Length);
                    return new string[] { c, vowel, " " };
                }
            }

            string[] singleConsonants = { "k", "t", "p", "n", "m", "s", "r", "l", "g", "d", "b", "j", "h", "f", "v", "z", "c", "w", "y" };
            foreach (var c in singleConsonants)
            {
                if (lyric.StartsWith(c))
                {
                    string vowel = lyric.Substring(c.Length);
                    return new string[] { c, vowel, " " };
                }
            }

            return new string[] { "", lyric, " " };
        }

        // [복구 완료] IsRomanized 메서드 추가 (CS0103 오류 해결)
        private bool IsRomanized(string phoneme)
        {
            if (string.IsNullOrEmpty(phoneme)) { return false; }
            string[] validStarts = {
                "kk", "tt", "pp", "jj", "ss", "ch", "ng", "th", "ph", "gh", "wh",
                "k", "t", "p", "n", "m", "s", "r", "l", "g", "d", "b", "j", "h", "f", "v", "z", "c", "w", "y",
                "a", "e", "i", "o", "u"
            };
            foreach (var start in validStarts)
            {
                if (phoneme.StartsWith(start)) return true;
            }
            return false;
        }

        // ==============================================================================
        // [중요] RomajiCVVCPhonemizer 스타일의 Helper 메서드 이식
        // note.color 대신 note.phonemeAttributes를 사용하여 API 호환성 및 접미사 문제 해결
        // ==============================================================================

        // OTO가 존재하는지 확인하는 함수 (접미사/Color 고려)
        private bool CheckOto(Note note, string phoneme)
        {
            if (singer == null || string.IsNullOrEmpty(phoneme)) return false;

            // Romaji 예시처럼 phonemeAttributes에서 정보 추출
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;

            // alternate(특수 기호 등)가 있으면 붙여서 먼저 시도
            string inputWithAlt = phoneme + attr.alternate;
            if (singer.TryGetMappedOto(inputWithAlt, note.tone + attr.toneShift, attr.voiceColor, out var _))
            {
                return true;
            }

            // 없으면 기본 음소로 시도
            return singer.TryGetMappedOto(phoneme, note.tone + attr.toneShift, attr.voiceColor, out var _);
        }

        // Phoneme 객체를 생성하는 함수 (접미사 적용된 Alias 반환)
        private Phoneme CreatePhoneme(Note note, string phoneme, int position)
        {
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;

            // 1. Alternate 적용 시도
            string inputWithAlt = phoneme + attr.alternate;
            if (singer.TryGetMappedOto(inputWithAlt, note.tone + attr.toneShift, attr.voiceColor, out var otoAlt))
            {
                return new Phoneme { phoneme = otoAlt.Alias, position = position };
            }

            // 2. 기본 음소 시도 (Color 적용됨)
            if (singer.TryGetMappedOto(phoneme, note.tone + attr.toneShift, attr.voiceColor, out var oto))
            {
                return new Phoneme { phoneme = oto.Alias, position = position };
            }

            // 3. 실패 시 입력 그대로 반환
            return new Phoneme { phoneme = phoneme, position = position };
        }

        // ==============================================================================
        // 핵심 로직: COC 변환
        // ==============================================================================
        private Result ConvertForCOC(Note[] notes, string[] prevLyric, string[] thisLyric, string[] nextLyric, Note? nextNeighbour)
        {
            int totalDuration = notes.Sum(n => n.duration); // Ticks 단위
            Note note = notes[0];

            string thisMidVowelTail;

            string soundBeforeEndSound = (thisLyric.Length > 2 && thisLyric[2] == " ") ? thisLyric[1] : thisLyric[2];
            if (string.IsNullOrEmpty(soundBeforeEndSound)) soundBeforeEndSound = "null";

            string thisMidVowelForEnd;
            if (MIDDLE_VOWELS.ContainsKey(soundBeforeEndSound))
            {
                thisMidVowelForEnd = MIDDLE_VOWELS[soundBeforeEndSound][2];
            }
            else if (LAST_CONSONANTS.ContainsKey(soundBeforeEndSound))
            {
                thisMidVowelForEnd = LAST_CONSONANTS[soundBeforeEndSound][0];
            }
            else
            {
                thisMidVowelForEnd = soundBeforeEndSound;
            }
            string endSound = $"{thisMidVowelForEnd} R";

            bool isItNeedsFrontCV = prevLyric[0] == "null" || prevLyric[1] == "null";
            bool isItNeedsEndSound = (nextLyric[0] == "null" || nextLyric[1] == "null") && nextNeighbour == null;

            if (thisLyric.All(part => part == null))
            {
                return new Result { phonemes = new Phoneme[] { CreatePhoneme(note, note.lyric, 0) } };
            }
            else
            {
                if (MIDDLE_VOWELS.ContainsKey(thisLyric[1]))
                {
                    thisMidVowelTail = $"{MIDDLE_VOWELS[thisLyric[1]][2]}";
                }
                else
                {
                    thisMidVowelTail = thisLyric[1];
                }
            }

            // 1. CV 생성
            string CV;
            string currConsonantHangul = NormalizeJamo(thisLyric[0]);
            string prevBatchimHangul = NormalizeJamo(prevLyric[2]);

            string currentConsonantSymbol = FIRST_CONSONANTS.ContainsKey(currConsonantHangul) ? FIRST_CONSONANTS[currConsonantHangul] : currConsonantHangul;

            if (currConsonantHangul == "ㄹ" && prevBatchimHangul == "ㄹ")
            {
                currentConsonantSymbol = "l";
                string[] diphthongs = { "ㅑ", "ㅕ", "ㅛ", "ㅠ", "ㅖ", "ㅒ", "ㅘ", "ㅙ", "ㅚ", "ㅝ", "ㅞ", "ㅟ", "ㅢ" };
                if (diphthongs.Contains(thisLyric[1]))
                {
                    currentConsonantSymbol = "";
                }
            }

            string currentVowelSymbol = MIDDLE_VOWELS.ContainsKey(thisLyric[1]) ? MIDDLE_VOWELS[thisLyric[1]][0] : thisLyric[1];
            CV = $"{currentConsonantSymbol}{currentVowelSymbol}";

            if (currentConsonantSymbol == "")
            {
                if (prevLyric[0] != "null")
                {
                    string cvStar = $"{CV} *";
                    if (CheckOto(note, cvStar))
                    {
                        CV = cvStar;
                    }
                }
            }

            string frontCV = $"- {CV}";
            if (!CheckOto(note, frontCV))
            {
                frontCV = $"-{CV}";
                if (!CheckOto(note, frontCV))
                {
                    frontCV = CV;
                }
            }

            // ==========================================================================
            // 2. VC 생성
            // ==========================================================================
            string VC = null;
            bool isItNeedsVC = thisLyric[2] == " " && nextLyric[0] != "null";

            if (isItNeedsVC)
            {
                string nextInput = NormalizeJamo(nextLyric[0]);
                string nextConsonantSymbol = "";
                bool isSpaceNeeded = true;

                string rawSymbol = nextInput;
                if (FIRST_CONSONANTS.ContainsKey(nextInput))
                {
                    rawSymbol = FIRST_CONSONANTS[nextInput];
                }

                switch (rawSymbol)
                {
                    case "kk": nextConsonantSymbol = "K"; isSpaceNeeded = false; break;
                    case "tt": nextConsonantSymbol = "T"; isSpaceNeeded = false; break;
                    case "pp": nextConsonantSymbol = "P"; isSpaceNeeded = false; break;
                    case "jj": nextConsonantSymbol = "T"; isSpaceNeeded = false; break;
                    case "ss": nextConsonantSymbol = "s"; isSpaceNeeded = true; break;
                    case "k": nextConsonantSymbol = "g"; break;
                    case "t": nextConsonantSymbol = "d"; break;
                    case "p": nextConsonantSymbol = "b"; break;
                    case "j": nextConsonantSymbol = "d"; break;
                    case "ch": nextConsonantSymbol = "d"; break;
                    case "n": nextConsonantSymbol = "N"; isSpaceNeeded = false; break;
                    case "m": nextConsonantSymbol = "M"; isSpaceNeeded = false; break;
                    default: nextConsonantSymbol = rawSymbol; break;
                }

                if (!string.IsNullOrEmpty(nextConsonantSymbol) && nextConsonantSymbol.All(char.IsUpper))
                {
                    isSpaceNeeded = false;
                }

                if (isSpaceNeeded)
                {
                    VC = $"{thisMidVowelTail} {nextConsonantSymbol}";
                }
                else
                {
                    VC = $"{thisMidVowelTail}{nextConsonantSymbol}";
                }
            }

            // 3. 종성(Batchim) 생성
            string batchim = null;
            if (thisLyric[2] != " ")
            {
                string batchimSymbol = LAST_CONSONANTS.ContainsKey(thisLyric[2]) ? LAST_CONSONANTS[thisLyric[2]][0] : "";
                batchim = $"{thisMidVowelTail}{batchimSymbol}";
            }

            // 4. 결과 반환 (타이밍 계산: r 계열 30 ticks 적용)
            Result CreateResult(string p1, string p2 = null, int tailLen = 120)
            {
                if (p2 == null)
                {
                    return new Result { phonemes = new Phoneme[] { CreatePhoneme(note, p1, 0) } };
                }

                // p2가 'r' 또는 'R'로 끝나면 tailLen을 30으로 축소 (뒤로 밀기)
                if (p2.EndsWith(" R") || p2.EndsWith(" r"))
                {
                    tailLen = 30;
                }

                int actualTailLen = tailLen;
                if (totalDuration < tailLen * 2)
                {
                    actualTailLen = totalDuration / 2;
                }

                return new Result
                {
                    phonemes = new Phoneme[] {
                        CreatePhoneme(note, p1, 0),
                        CreatePhoneme(note, p2, totalDuration - actualTailLen)
                    }
                };
            }

            if (thisLyric[2] == " ")
            { // 받침 없음
                if (isItNeedsVC && CheckOto(note, VC))
                {
                    if (isItNeedsFrontCV)
                    {
                        return CreateResult(frontCV, VC, 120);
                    }
                    return CreateResult(CV, VC, 120);
                }

                if (isItNeedsFrontCV)
                {
                    return isItNeedsEndSound ?
                        CreateResult(frontCV, endSound, 120)
                        : CreateResult(frontCV);
                }
                return isItNeedsEndSound ?
                    CreateResult(CV, endSound, 120)
                    : CreateResult(CV);
            }

            // 받침 있음
            if (isItNeedsFrontCV)
            {
                return CreateResult(frontCV, batchim, 120);
            }
            return CreateResult(CV, batchim, 120);
        }

        // ==============================================================================
        // MAIN ENTRY POINT
        // ==============================================================================
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours)
        {
            Note note = notes[0];

            // rr 처리
            if (note.lyric == "rr" && prevNeighbour != null)
            {
                string prevVowelTail = "";
                if (KoreanParser.IsHangeul(prevNeighbour.Value.lyric))
                {
                    string[] prevLyrics = KoreanParser.Separate(prevNeighbour.Value.lyric);
                    string prevMid = prevLyrics[1];
                    if (MIDDLE_VOWELS.ContainsKey(prevMid))
                    {
                        prevVowelTail = MIDDLE_VOWELS[prevMid][2];
                    }
                }
                else
                {
                    string[] parsed = ParseRomaji(prevNeighbour.Value.lyric);
                    prevVowelTail = parsed[1];
                }

                if (!string.IsNullOrEmpty(prevVowelTail))
                {
                    if (CheckOto(note, $"{prevVowelTail} rr"))
                        return new Result { phonemes = new Phoneme[] { CreatePhoneme(note, $"{prevVowelTail} rr", 0) } };
                }
                return new Result { phonemes = new Phoneme[] { CreatePhoneme(note, "rr", 0) } };
            }

            if (!KoreanParser.IsHangeul(note.lyric) && !IsRomanized(note.lyric))
            {
                return new Result { phonemes = new Phoneme[] { CreatePhoneme(note, note.lyric, 0) } };
            }

            string[] prevLyric = new string[] { "null", "null", "null" };
            if (prevNeighbour != null)
            {
                if (KoreanParser.IsHangeul(prevNeighbour.Value.lyric))
                {
                    prevLyric = KoreanParser.Separate(prevNeighbour.Value.lyric);
                }
                else
                {
                    prevLyric = ParseRomaji(prevNeighbour.Value.lyric);
                }
            }

            string[] thisLyric = new string[] { "null", "null", "null" };
            if (KoreanParser.IsHangeul(note.lyric))
            {
                thisLyric = KoreanParser.Separate(note.lyric);
            }
            else
            {
                thisLyric = ParseRomaji(note.lyric);
            }

            string[] nextLyric = new string[] { "null", "null", "null" };
            if (nextNeighbour != null)
            {
                if (KoreanParser.IsHangeul(nextNeighbour.Value.lyric))
                {
                    nextLyric = KoreanParser.Separate(nextNeighbour.Value.lyric);
                }
                else
                {
                    nextLyric = ParseRomaji(nextNeighbour.Value.lyric);
                }
            }

            if (thisLyric[0] == "null")
            {
                return new Result { phonemes = new Phoneme[] { CreatePhoneme(note, note.lyric, 0) } };
            }

            return ConvertForCOC(notes, prevLyric, thisLyric, nextLyric, nextNeighbour);
        }
    }
}