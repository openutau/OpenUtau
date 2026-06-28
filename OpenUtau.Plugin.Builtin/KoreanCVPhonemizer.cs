using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core;
using System.IO;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// Phonemizer for 'KOR CV' ///
    [Phonemizer("Korean CV Phonemizer", "KO CV", "EX3", language: "KO")]

    public class KoreanCVPhonemizer : BaseKoreanPhonemizer {

        // 1. Load Singer and Settings
        private KoreanCVSetting kocvS;

        public override void SetSinger(USinger singer) {
            if (this.singer == singer) { return; }
            this.singer = singer;
            if (this.singer == null) { return; }

            if (this.singer.SingerType != USingerType.Classic) { return; }

            LoadKOCVSetting();
        }

        protected void LoadKOCVSetting() {
            // Load KO-CV Phonemizer Setting from plugin folder.
            string path = Path.Combine(PluginDir, "kocv.yaml");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.kocv_template);
            } else {
                try {
                    string settingText = File.ReadAllText(path, encoding: System.Text.Encoding.UTF8);
                    kocvS = Yaml.DefaultDeserializer.Deserialize<KoreanCVSetting>(settingText);
                    
                } catch (Exception e) {
                    Log.Error(e, $"[KO CV] Failed to load {path}. Regenerating kocv.yaml in Plugin Directory...");
                    Directory.CreateDirectory(PluginDir);
                    File.WriteAllBytes(path, Data.Resources.kocv_template);
                }
            }

            // If singer's setting file exists, use it instead
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "kocv.yaml");
                if (File.Exists(file)) {
                    try {
                        string settingText = File.ReadAllText(file, encoding: System.Text.Encoding.UTF8);
                        kocvS = Yaml.DefaultDeserializer.Deserialize<KoreanCVSetting>(settingText);
                    } catch (Exception e) {
                        Log.Error(e, $"[KO CV] Failed to load {file}. Use Default Settings in Plugin Directory.");
                    }
                }

                // If there's legacy ko-CV.ini, tries to delete it
                string legacyFile = Path.Combine(singer.Location, "ko-CV.ini");
                if (File.Exists(legacyFile)) {
                    try {
                        File.Delete(legacyFile);
                    } catch (Exception e) {
                        Log.Error(e, $"[KO CV] Failed to delete {legacyFile}. ko-CV.ini is not used anymore, please remove it from your singer path manually.");
                    }
                }
            }
        }

        [Serializable]
        private class KoreanCVSetting {
            private static readonly Version CURRENT_VERSION = new Version(1, 0);
            public string version { get; set; } 
            public Settings settings { get; set; }
            public List<BatchimConnection> batchim_connections { get; set; }
        
            [Serializable]
            public class Settings {
                public bool use_rentan { get; set; }
                public string eui_fallback { get; set; }
                public bool use_shi_phoneme { get; set; }
                public bool use_ax_batchim_phoneme { get; set; }
                public bool use_ax_batchim_phoneme_only { get; set; }
                public bool use_capital_batchim { get; set; }
                public bool use_capital_batchim_only { get; set; }
                public bool attach_empty_phoneme_after_batchims { get; set; }
                public bool use_batchim_C { get; set; }
                public bool use_batchim_C_only { get; set; }
            }

            [Serializable]
            public class BatchimConnection {
                public string if_prev_batchim_is { get; set; }            // "ㄹ"
                public string if_current_consonant { get; set; }          // "ㄹ"
                public string use_current_consonant_phoneme_as { get; set; }  // "l"
            }

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
            {"ㅢ", new string[3]{"eui", "eu", "i"}}, // ㅢ는 ㅣ로 발음
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
            {" ", new string[]{""}}, // no batchim
            {"null", new string[]{"", ""}} // 뒤 글자가 없을 때를 대비
            };

        private bool CheckThisPhonemeExists(string phoneme, Note note) {
            return FindInOto(phoneme, note, true) != null;
        }

        private void SetbatchimIfExists(ref string _batchim, string __batchim, Note note) {
            if (CheckThisPhonemeExists(__batchim, note)) {
                _batchim = __batchim;
            }
        }
        private Result ConvertForCV(Note[] notes, string[] prevLyric, string[] thisLyric, string[] nextLyric) {
            string thisMidVowelHead;
            string thisMidVowelTail;

            int totalDuration = notes.Sum(n => n.duration);
            Note note = notes[0];
            bool isItNeedsFrontCV;
            bool isRelaxedVC;
            isItNeedsFrontCV = prevLyric[0] == "null" || prevLyric[1] == "null" || (prevLyric[2] != "null" && HARD_BATCHIMS.Contains(prevLyric[2]) && prevLyric[2] != "ㅁ");
            isRelaxedVC = nextLyric[0] == "null" || nextLyric[1] == "null";

            string firstConsonant = FIRST_CONSONANTS[thisLyric[0]];
            if (thisLyric.All(part => part == null)) {
                return GenerateResult(FindInOto(note.lyric, note));
            } else {
                thisMidVowelHead = $"{MIDDLE_VOWELS[thisLyric[1]][1]}";
                thisMidVowelTail = $"{MIDDLE_VOWELS[thisLyric[1]][2]}";

                if (kocvS.settings.use_shi_phoneme) { // 시를 shi로 사용할수 있을 시 사용
                    if (thisLyric[1][0] == 'ㅅ' && thisLyric[1][1] == 'ㅣ') {
                        if (CheckThisPhonemeExists("shi", note)) {
                            firstConsonant = "sh";
                        }
                    }
                }
            }

            string CV = $"{firstConsonant}{thisMidVowelHead}{thisMidVowelTail}";
            string frontCV;
            string batchim;

            if (FindInOto(CV, note, true) == null) {
                // ㅢ 대체 필요할 경우 대체
                if (thisLyric[1] == "ㅢ") {
                    if (MIDDLE_VOWELS.ContainsKey(kocvS.settings.eui_fallback)) {
                        thisMidVowelHead = $"{MIDDLE_VOWELS[kocvS.settings.eui_fallback][1]}";
                        thisMidVowelTail = $"{MIDDLE_VOWELS[kocvS.settings.eui_fallback][2]}";
                    } else {
                        thisMidVowelHead = $"{MIDDLE_VOWELS["ㅔ"][1]}";
                        thisMidVowelTail = $"{MIDDLE_VOWELS["ㅔ"][2]}";
                    }
                }

                // Regenerate CV
                CV = $"{firstConsonant}{thisMidVowelHead}{thisMidVowelTail}";
            }
            foreach (var batchimConnection in kocvS.batchim_connections) {
                // 앞 노트의 종성과 현재 노트의 초성을 설정 파일에 맞춰 조정 
                if (prevLyric[2] == batchimConnection.if_prev_batchim_is && thisLyric[0] == batchimConnection.if_current_consonant) {
                    string CV_ = $"{batchimConnection.use_current_consonant_phoneme_as}{thisMidVowelHead}{thisMidVowelTail}";
                    if (CheckThisPhonemeExists(CV_, note)) {
                        CV = CV_;
                    }
                    break;
                }
            }

            if (kocvS.settings.use_rentan) { // 연단음 적용
                frontCV = $"- {CV}";
                if (!CheckThisPhonemeExists(frontCV, note)) {
                    frontCV = $"-{CV}";
                    if (!CheckThisPhonemeExists(frontCV, note)) {
                        frontCV = CV;
                    }
                }
            } else {
                frontCV = CV;
            }

            if (thisLyric[2] == " ") { // no batchim
                if (isItNeedsFrontCV) {
                    return GenerateResult(FindInOto(frontCV, note));
                }
                return GenerateResult(FindInOto(CV, note));
            }

            // batchim
            // priority(example): a n -> an -> a N or aN -> n or N

            // tries a x
            string _batchim = $"{thisMidVowelTail} {LAST_CONSONANTS[thisLyric[2]][0]}";
            string __batchim; // temporary variable


            // tries ax
            if (kocvS.settings.use_ax_batchim_phoneme) {
                if (kocvS.settings.use_ax_batchim_phoneme_only) {
                    __batchim = $"{thisMidVowelTail}{LAST_CONSONANTS[thisLyric[2]][0]}";
                    SetbatchimIfExists(ref _batchim, __batchim, note);
                }
                if (!CheckThisPhonemeExists(_batchim, note)) {
                    __batchim = $"{thisMidVowelTail}{LAST_CONSONANTS[thisLyric[2]][0]}";
                    SetbatchimIfExists(ref _batchim, __batchim, note);
                }
            }

            // tries a X / aX
            if (kocvS.settings.use_capital_batchim) {
                if (kocvS.settings.use_capital_batchim_only) {
                    __batchim = $"{thisMidVowelTail} {LAST_CONSONANTS[thisLyric[2]][0].ToUpper()}";
                    SetbatchimIfExists(ref _batchim, __batchim, note);
                } else {
                    __batchim = $"{thisMidVowelTail}{LAST_CONSONANTS[thisLyric[2]][0].ToUpper()}";
                    SetbatchimIfExists(ref _batchim, __batchim, note);
                }
            }

            // tries x / X
            if (kocvS.settings.use_batchim_C) {
                if (kocvS.settings.use_batchim_C_only) {
                    if (kocvS.settings.use_capital_batchim) {
                        __batchim = $"{LAST_CONSONANTS[thisLyric[2]][0]}";
                        if (kocvS.settings.use_capital_batchim_only || CheckThisPhonemeExists(__batchim, note)) {
                            __batchim = $"{LAST_CONSONANTS[thisLyric[2]][0].ToUpper()}";
                            SetbatchimIfExists(ref _batchim, __batchim, note);
                        }
                    } else {
                        __batchim = $"{LAST_CONSONANTS[thisLyric[2]][0]}";
                        SetbatchimIfExists(ref _batchim, __batchim, note);
                    }
                }
            }
            
            batchim = _batchim;

            if (thisLyric[2] == "ㅁ" || !HARD_BATCHIMS.Contains(thisLyric[2])) { // batchim ㅁ + ㄴ ㄹ ㅇ
                if (isItNeedsFrontCV) {
                    return isRelaxedVC ?
                         GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                         : (kocvS.settings.attach_empty_phoneme_after_batchims ?
                             GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), "", totalDuration, Math.Max(totalDuration / 2, 120), 3, 5)
                             : GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, Math.Max(totalDuration / 2, 120), 2));
                }
                return isRelaxedVC ?
                    GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                    : (kocvS.settings.attach_empty_phoneme_after_batchims ?
                        GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), "", totalDuration, Math.Max(totalDuration / 2, 120), 3, 5)
                        : GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, Math.Max(totalDuration / 2, 120), 3));
            } else {
                if (isItNeedsFrontCV) {
                    return isRelaxedVC ?
                    GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                    : GenerateResult(FindInOto(frontCV, note), FindInOto(batchim, note), totalDuration, Math.Max(totalDuration / 2, 120), 3);
                }
                return isRelaxedVC ?
                GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, 120, 8)
                : GenerateResult(FindInOto(CV, note), FindInOto(batchim, note), totalDuration, Math.Max(totalDuration / 2, 120), 3);
            }
        }

        private string? FindInOto(String phoneme, Note note, bool nullIfNotFound=false){
            return BaseKoreanPhonemizer.FindInOto(singer, phoneme, note, nullIfNotFound);
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
            
            return ConvertForCV(notes, prevLyric, thisLyric, nextLyric);

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

            

            if (prevLyric[1] == "ㅢ") {
                bool euiExists = CheckThisPhonemeExists("eui", note);
                if (!euiExists && MIDDLE_VOWELS.ContainsKey(kocvS.settings.eui_fallback)) {
                    prevMidVowel = $"{MIDDLE_VOWELS[kocvS.settings.eui_fallback][2]}";
                } else if (!euiExists) {
                    prevMidVowel = $"{MIDDLE_VOWELS["ㅔ"][2]}";
                } else {
                    prevMidVowel = $"{MIDDLE_VOWELS["ㅢ"][2]}";
                }
            } else {
                prevMidVowel = MIDDLE_VOWELS.ContainsKey(soundBeforeEndSound) ? MIDDLE_VOWELS[soundBeforeEndSound][2] : LAST_CONSONANTS[soundBeforeEndSound][0];
            }
            
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