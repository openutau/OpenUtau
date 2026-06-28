using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Enunu {
    [Phonemizer("Enunu Korean Phonemizer", "ENUNU KO", "EX3", language:"KO")]
    public class EnunuKoreanPhonemizer : EnunuPhonemizer {
        readonly string PhonemizerType = "ENUNU KO";
        public string semivowelSep;
        private KoreanENUNUSetting koreanENUNUSetting; // Manages Settings
        private bool isSeparateSemiVowels; // Nanages n y a or ny a
        
        /// <summary>
        /// Default KO ENUNU first consonants table
        /// </summary>
        static readonly List<KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData> firstDefaultConsonants = new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData[19]{
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㄱ", "g"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㄲ", "kk"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㄴ", "n"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㄷ", "d"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㄸ", "tt"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㄹ", "r"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅁ", "m"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅂ", "b"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅃ", "pp"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅅ", "s"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅆ", "ss"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅇ", ""),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅈ", "j"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅉ", "jj"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅊ", "ch"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅋ", "k"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅌ", "t"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅍ", "p"),
            new KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData("ㅎ", "h")
            }.ToList();

        /// <summary>
        /// Default KO ENUNU plain vowels table
        /// </summary>
        static readonly List<KoreanPhonemizerUtil.JamoDictionary.PlainVowelData> plainDefaultVowels = new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData[7]{
            new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData("ㅏ", "a"),
            new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData("ㅣ", "i"),
            new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData("ㅜ", "u"),
            new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData("ㅔ/ㅐ", "e"),
            new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData("ㅗ", "o"),
            new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData("ㅓ", "eo"),
            new KoreanPhonemizerUtil.JamoDictionary.PlainVowelData("ㅡ", "eu")
            }.ToList();

        /// <summary>
        /// Default KO ENUNU semivowels table
        /// </summary>
        static readonly List<KoreanPhonemizerUtil.JamoDictionary.SemivowelData> semiDefaultVowels = new KoreanPhonemizerUtil.JamoDictionary.SemivowelData[2]{
            new KoreanPhonemizerUtil.JamoDictionary.SemivowelData("y", "y"),
            new KoreanPhonemizerUtil.JamoDictionary.SemivowelData("w", "w")
            }.ToList();

        /// <summary>
        /// Default KO ENUNU final consonants table
        /// </summary>
        static readonly List<KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData> finalDefaultConsonants = new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData[7]{
            new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData("ㄱ", "K"),
            new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData("ㄴ", "N"),
            new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData("ㄷ", "T"),
            new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData("ㄹ", "L"),
            new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData("ㅁ", "M"),
            new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData("ㅂ", "P"),
            new KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData("ㅇ", "NG")
            }.ToList();

        /// <summary>
        /// KO ENUNU phoneme table of first consonants. (key "null" is for Handling empty string)
        /// </summary>
        private Dictionary<string, string[]> FirstConsonants = new Dictionary<string, string[]>(){
            {"ㄱ", new string[2]{"g", ConsonantType.NORMAL.ToString()}},
            {"ㄲ", new string[2]{"kk", ConsonantType.FORTIS.ToString()}},
            {"ㄴ", new string[2]{"n", ConsonantType.NASAL.ToString()}},
            {"ㄷ", new string[2]{"d", ConsonantType.NORMAL.ToString()}},
            {"ㄸ", new string[2]{"tt", ConsonantType.FORTIS.ToString()}},
            {"ㄹ", new string[2]{"r", ConsonantType.LIQUID.ToString()}},
            {"ㅁ", new string[2]{"m", ConsonantType.NASAL.ToString()}},
            {"ㅂ", new string[2]{"b", ConsonantType.NORMAL.ToString()}},
            {"ㅃ", new string[2]{"pp", ConsonantType.FORTIS.ToString()}},
            {"ㅅ", new string[2]{"s", ConsonantType.NORMAL.ToString()}},
            {"ㅆ", new string[2]{"ss", ConsonantType.FRICATIVE.ToString()}},
            {"ㅇ", new string[2]{"", ConsonantType.NOCONSONANT.ToString()}},
            {"ㅈ", new string[2]{"j", ConsonantType.NORMAL.ToString()}},
            {"ㅉ", new string[2]{"jj", ConsonantType.FORTIS.ToString()}},
            {"ㅊ", new string[2]{"ch", ConsonantType.ASPIRATE.ToString()}},
            {"ㅋ", new string[2]{"k", ConsonantType.ASPIRATE.ToString()}},
            {"ㅌ", new string[2]{"t", ConsonantType.ASPIRATE.ToString()}},
            {"ㅍ", new string[2]{"p", ConsonantType.ASPIRATE.ToString()}},
            {"ㅎ", new string[2]{"h", ConsonantType.H.ToString()}},
            {" ", new string[2]{"", ConsonantType.NOCONSONANT.ToString()}},
            {"null", new string[2]{"", ConsonantType.PHONEME_IS_NULL.ToString()}} // 뒤 글자가 없을 때를 대비
            };

        /// <summary>
        /// KO ENUNU phoneme table of middle vowels (key "null" is for Handling empty string)
        /// </summary>
        private Dictionary<string, string[]> MiddleVowels = new Dictionary<string, string[]>(){
            {"ㅏ", new string[3]{"a", "", "a"}},
            {"ㅐ", new string[3]{"e", "", "e"}},
            {"ㅑ", new string[3]{"ya", "y", " a"}},
            {"ㅒ", new string[3]{"ye", "y", " e"}},
            {"ㅓ", new string[3]{"eo", "", "eo"}},
            {"ㅔ", new string[3]{"e", "", "e"}},
            {"ㅕ", new string[3]{"yeo", "y", " eo"}},
            {"ㅖ", new string[3]{"ye", "y", " e"}},
            {"ㅗ", new string[3]{"o", "", "o"}},
            {"ㅘ", new string[3]{"wa", "w", " a"}},
            {"ㅙ", new string[3]{"we", "w", " e"}},
            {"ㅚ", new string[3]{"we", "w", " e"}},
            {"ㅛ", new string[3]{"yo", "y", " o"}},
            {"ㅜ", new string[3]{"u", "", "u"}},
            {"ㅝ", new string[3]{"weo", "w", " eo"}},
            {"ㅞ", new string[3]{"we", "w", " e"}},
            {"ㅟ", new string[3]{"wi", "w", " i"}},
            {"ㅠ", new string[3]{"yu", "y", " u"}},
            {"ㅡ", new string[3]{"eu", "", "eu"}},
            {"ㅢ", new string[3]{"i", "", "i"}}, // ㅢ는 ㅣ로 발음
            {"ㅣ", new string[3]{"i", "", "i"}},
            {" ", new string[3]{"", "", ""}},
            {"null", new string[3]{"", "", ""}} // 뒤 글자가 없을 때를 대비
            };
        /// <summary>
        /// KO ENUNU phoneme table of last consonants. (key "null" is for Handling empty string)
        /// </summary>
        private Dictionary<string, string[]> LastConsonants = new Dictionary<string, string[]>(){
             //ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ
            {"ㄱ", new string[3]{" K", "", BatchimType.NORMAL_END.ToString()}},
            {"ㄲ", new string[3]{" K", "", BatchimType.NORMAL_END.ToString()}},
            {"ㄳ", new string[3]{" K", "", BatchimType.NORMAL_END.ToString()}},
            {"ㄴ", new string[3]{" N", "2", BatchimType.NASAL_END.ToString()}},
            {"ㄵ", new string[3]{" N", "2", BatchimType.NASAL_END.ToString()}},
            {"ㄶ", new string[3]{" N", "2", BatchimType.NASAL_END.ToString()}},
            {"ㄷ", new string[3]{" T", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㄹ", new string[3]{" L", "4", BatchimType.LIQUID_END.ToString()}},
            {"ㄺ", new string[3]{" K", "", BatchimType.NORMAL_END.ToString()}},
            {"ㄻ", new string[3]{" M", "1", BatchimType.NASAL_END.ToString()}},
            {"ㄼ", new string[3]{" L", "4", BatchimType.LIQUID_END.ToString()}},
            {"ㄽ", new string[3]{" L", "4", BatchimType.LIQUID_END.ToString()}},
            {"ㄾ", new string[3]{" L", "4", BatchimType.LIQUID_END.ToString()}},
            {"ㄿ", new string[3]{" P", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅀ", new string[3]{" L", "4", BatchimType.LIQUID_END.ToString()}},
            {"ㅁ", new string[3]{" M", "1", BatchimType.NASAL_END.ToString()}},
            {"ㅂ", new string[3]{" P", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅄ", new string[3]{" P", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅅ", new string[3]{" T", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅆ", new string[3]{" T", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅇ", new string[3]{" NG", "3", BatchimType.NG_END.ToString()}},
            {"ㅈ", new string[3]{" T", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅊ", new string[3]{" T", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅋ", new string[3]{" K", "", BatchimType.NORMAL_END.ToString()}},
            {"ㅌ", new string[3]{" T", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅍ", new string[3]{" P", "1", BatchimType.NORMAL_END.ToString()}},
            {"ㅎ", new string[3]{" T", "1", BatchimType.H_END.ToString()}},
            {" ", new string[3]{"", "", BatchimType.NO_END.ToString()}},
            {"null", new string[3]{"", "", BatchimType.PHONEME_IS_NULL.ToString()}} // 뒤 글자가 없을 때를 대비
            };
    
        struct TimingResult {
            public string path_full_timing;
            public string path_mono_timing;
        }

        struct TimingResponse {
            public string error;
            public TimingResult result;
        }
        public override void SetSinger(USinger singer) {
            if (singer.SingerType != USingerType.Enunu) {return;}

            this.singer = singer as EnunuSinger;

            koreanENUNUSetting = new KoreanENUNUSetting("jamo_dict.yaml");
            
            koreanENUNUSetting.Initialize(singer, "ko-ENUNU.ini", new Hashtable(){
                {
                    "SETTING", new Hashtable(){
                                {"Separate semivowels, like 'n y a'(otherwise 'ny a')", "True"}
                                }
                    }
                }
            );

            isSeparateSemiVowels = koreanENUNUSetting.isSeparateSemiVowels;
            semivowelSep = isSeparateSemiVowels ? " ": "";

            // Modify Phoneme Tables
            // First Consonants
            FirstConsonants["ㄱ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㄱ")}";
            FirstConsonants["ㄲ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㄲ")}";
            FirstConsonants["ㄴ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㄴ")}";
            FirstConsonants["ㄷ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㄷ")}";
            FirstConsonants["ㄸ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㄸ")}";
            FirstConsonants["ㄹ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㄹ")}";
            FirstConsonants["ㅁ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅁ")}";
            FirstConsonants["ㅂ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅂ")}";
            FirstConsonants["ㅃ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅃ")}";
            FirstConsonants["ㅅ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅅ")}";
            FirstConsonants["ㅆ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅆ")}";
            FirstConsonants["ㅇ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅇ")}";
            FirstConsonants["ㅈ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅈ")}";
            FirstConsonants["ㅉ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅉ")}";
            FirstConsonants["ㅊ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅊ")}";
            FirstConsonants["ㅋ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅋ")}";
            FirstConsonants["ㅌ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅌ")}";
            FirstConsonants["ㅍ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅍ")}";
            FirstConsonants["ㅎ"][0] = $"{koreanENUNUSetting.GetFirstConsonantPhoneme("ㅎ")}";

            
            // Vowels
            MiddleVowels["ㅑ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("y");
            MiddleVowels["ㅒ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("y");
            MiddleVowels["ㅕ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("y");
            MiddleVowels["ㅖ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("y");
            MiddleVowels["ㅘ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("w");
            MiddleVowels["ㅙ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("w");
            MiddleVowels["ㅚ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("w");
            MiddleVowels["ㅛ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("y");
            MiddleVowels["ㅝ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("w");
            MiddleVowels["ㅞ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("w");
            MiddleVowels["ㅟ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("w");
            MiddleVowels["ㅠ"][1] = koreanENUNUSetting.GetSemiVowelPhoneme("y");

            MiddleVowels["ㅏ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅏ")}";
            MiddleVowels["ㅐ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅔ/ㅐ")}";
            MiddleVowels["ㅑ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅏ")}";
            MiddleVowels["ㅒ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅔ/ㅐ")}";
            MiddleVowels["ㅓ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅓ")}";
            MiddleVowels["ㅔ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅔ/ㅐ")}";
            MiddleVowels["ㅕ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅓ")}";
            MiddleVowels["ㅖ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅔ/ㅐ")}";
            MiddleVowels["ㅗ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅗ")}";
            MiddleVowels["ㅘ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅏ")}";
            MiddleVowels["ㅙ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅔ/ㅐ")}";
            MiddleVowels["ㅚ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅔ/ㅐ")}";
            MiddleVowels["ㅛ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅗ")}";
            MiddleVowels["ㅜ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅜ")}";
            MiddleVowels["ㅝ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅓ")}";
            MiddleVowels["ㅞ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅔ/ㅐ")}";
            MiddleVowels["ㅟ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅣ")}";
            MiddleVowels["ㅠ"][2] = $" {koreanENUNUSetting.GetPlainVowelPhoneme("ㅜ")}";
            MiddleVowels["ㅡ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅡ")}";
            MiddleVowels["ㅢ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅣ")}"; // ㅢ는 ㅣ로 발음
            MiddleVowels["ㅣ"][2] = $"{koreanENUNUSetting.GetPlainVowelPhoneme("ㅣ")}";
        
        // final consonants
            LastConsonants["ㄱ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄱ")}";
            LastConsonants["ㄲ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄱ")}";
            LastConsonants["ㄳ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄱ")}";
            LastConsonants["ㄴ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄴ")}";
            LastConsonants["ㄵ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄴ")}";
            LastConsonants["ㄶ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄴ")}";
            LastConsonants["ㄷ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄷ")}";
            LastConsonants["ㄹ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄹ")}";
            LastConsonants["ㄺ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄱ")}";
            LastConsonants["ㄻ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㅁ")}";
            LastConsonants["ㄼ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄹ")}";
            LastConsonants["ㄽ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄹ")}";
            LastConsonants["ㄾ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄹ")}";
            LastConsonants["ㄿ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㅂ")}";
            LastConsonants["ㅀ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄹ")}";
            LastConsonants["ㅁ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㅁ")}";
            LastConsonants["ㅂ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㅂ")}";
            LastConsonants["ㅄ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㅂ")}";
            LastConsonants["ㅅ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄷ")}";
            LastConsonants["ㅆ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄷ")}";
            LastConsonants["ㅇ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㅇ")}";
            LastConsonants["ㅈ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄷ")}";
            LastConsonants["ㅊ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄷ")}";
            LastConsonants["ㅋ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄱ")}";
            LastConsonants["ㅌ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄷ")}";
            LastConsonants["ㅍ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㅂ")}";
            LastConsonants["ㅎ"][0] = $" {koreanENUNUSetting.GetFinalConsonantPhoneme("ㄷ")}";

        }
        private class KoreanENUNUSetting : KoreanPhonemizerUtil.BaseIniManager{
            // uses KO-ENUNU.ini, jamo_dict.yaml
            public bool isSeparateSemiVowels;
            public string yamlFileName;
            private KoreanPhonemizerUtil.JamoDictionary jamoDict;
            public KoreanENUNUSetting(string yamlFileName) {
                this.yamlFileName = yamlFileName;
            }
            protected override void IniSetUp(Hashtable iniFile) {
                // ko-ENUNU.ini + jamo_dict.yaml
                SetOrReadThisValue("SETTING", "Separate semivowels, like 'n y a'(otherwise 'ny a')", false, out var resultValue); // 반자음 떼기 유무 - 기본값 false
                isSeparateSemiVowels = resultValue;
                
                try {
                    jamoDict = Yaml.DefaultDeserializer.Deserialize<KoreanPhonemizerUtil.JamoDictionary>(File.ReadAllText(Path.Combine(singer.Location, yamlFileName)));
                    if (jamoDict == null) {
                        throw new IOException("yaml file is null");
                    }
                }
                catch (IOException e) {
                    Log.Error(e, $"Failed to read {Path.Combine(singer.Location, yamlFileName)}");

                    jamoDict = new KoreanPhonemizerUtil.JamoDictionary(firstDefaultConsonants.ToArray(), plainDefaultVowels.ToArray(), semiDefaultVowels.ToArray(), finalDefaultConsonants.ToArray());

                    File.WriteAllText(Path.Combine(singer.Location, yamlFileName), Yaml.DefaultSerializer.Serialize(jamoDict));
                }
                
            }


            public string GetFirstConsonantPhoneme(string Phoneme) {
                KoreanPhonemizerUtil.JamoDictionary.FirstConsonantData results = jamoDict.firstConsonants.ToList().Find(c => c.grapheme == Phoneme);
                string result = results.phoneme;
                if (result == null) {
                    result = firstDefaultConsonants.Find(c => c.grapheme == Phoneme).phoneme;
                }
                return result.Trim();
            }

            public string GetPlainVowelPhoneme(string Phoneme) {
                KoreanPhonemizerUtil.JamoDictionary.PlainVowelData results = jamoDict.plainVowels.ToList().Find(c => c.grapheme == Phoneme);
                string result = results.phoneme;
                if (result == null) {
                    result = plainDefaultVowels.Find(c => c.grapheme == Phoneme).phoneme;
                }
                return result.Trim();
            }

            public string GetSemiVowelPhoneme(string Phoneme) {
                KoreanPhonemizerUtil.JamoDictionary.SemivowelData results = jamoDict.semivowels.ToList().Find(c => c.grapheme == Phoneme);
                string result = results.phoneme;
                if (result == null) {
                    result = semiDefaultVowels.Find(c => c.grapheme == Phoneme).phoneme;
                }
                return result.Trim();
            }
            
            public string GetFinalConsonantPhoneme(string Phoneme) {
                KoreanPhonemizerUtil.JamoDictionary.FinalConsonantData results = jamoDict.finalConsonants.ToList().Find(c => c.grapheme == Phoneme);
                string result = results.phoneme;
                if (result == null) {
                    result = finalDefaultConsonants.Find(c => c.grapheme == Phoneme).phoneme;
                }
                return result.Trim();
            }
        }

        public enum ConsonantType{ 
                /// <summary>예사소리</summary>
                NORMAL, 
                /// <summary>거센소리</summary>
                ASPIRATE, 
                /// <summary>된소리</summary>
                FORTIS, 
                /// <summary>마찰음</summary>
                FRICATIVE, 
                /// <summary>비음</summary>
                NASAL,
                /// <summary>유음</summary>
                LIQUID, 
                /// <summary>ㅎ</summary>
                H,
                /// <summary>자음의 음소값 없음(ㅇ)</summary>
                NOCONSONANT, 
                /// <summary>음소 자체가 없음</summary>
                PHONEME_IS_NULL
            }
            
            /// <summary>
            /// Last Consonant's type.
            /// </summary>
            public enum BatchimType{ 
                /// <summary>예사소리 받침</summary>
                NORMAL_END, 
                /// <summary>비음 받침</summary>
                NASAL_END,
                /// <summary>유음 받침</summary>
                LIQUID_END, 
                /// <summary>ㅇ받침</summary>
                NG_END, 
                /// <summary>ㅎ받침</summary>
                H_END,
                /// <summary>받침이 없음</summary>
                NO_END,
                /// <summary>음소 자체가 없음</summary>
                PHONEME_IS_NULL
            }
            
        Dictionary<Note[], Phoneme[]> partResult = new Dictionary<Note[], Phoneme[]>();
        
        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            partResult.Clear();
            if (notes.Length == 0 || singer == null || !singer.Found) {
                return;
            }
            double bpm = timeAxis.GetBpmAtTick(notes[0][0].position);
            ulong hash = HashNoteGroups(notes, bpm);
            var tmpPath = Path.Join(PathManager.Inst.CachePath, $"lab-{hash:x16}");
            var ustPath = tmpPath + ".tmp";
            var enutmpPath = tmpPath + "_enutemp";
            var scorePath = Path.Join(enutmpPath, $"score.lab");
            var timingPath = Path.Join(enutmpPath, $"timing.lab");
            var enunuNotes = NoteGroupsToEnunu(notes);
            if (!File.Exists(scorePath) || !File.Exists(timingPath)) {
                EnunuUtils.WriteUst(enunuNotes, bpm, singer, ustPath);
                var response = EnunuClient.Inst.SendRequest<TimingResponse>(new string[] { "timing", ustPath });
                if (response.error != null) {
                    throw new Exception(response.error);
                }
            }
            var noteIndexes = LabelToNoteIndex(scorePath, enunuNotes);
            var timing = ParseLabel(timingPath);
            timing.Zip(noteIndexes, (phoneme, noteIndex) => Tuple.Create(phoneme, noteIndex))
                .GroupBy(tuple => tuple.Item2)
                .ToList()
                .ForEach(g => {
                    if (g.Key >= 0) {
                        var noteGroup = notes[g.Key];
                        partResult[noteGroup] = g.Select(tu => tu.Item1).ToArray();
                    }
                });
        }
        
        ulong HashNoteGroups(Note[][] notes, double bpm) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(this.PhonemizerType);
                    writer.Write(this.singer.Location);
                    writer.Write(bpm);
                    foreach (var ns in notes) {
                        foreach (var n in ns) {
                            writer.Write(n.lyric);
                            if(n.phoneticHint!= null) {
                                writer.Write("["+n.phoneticHint+"]");
                            }
                            writer.Write(n.position);
                            writer.Write(n.duration);
                            writer.Write(n.tone);
                        }
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

        static int[] LabelToNoteIndex(string scorePath, EnunuNote[] enunuNotes) {
            var result = new List<int>();
            int lastPos = 0;
            int index = 0;
            var score = ParseLabel(scorePath);
            foreach (var p in score) {
                if (p.position != lastPos) {
                    index++;
                    lastPos = p.position;
                }
                result.Add(enunuNotes[index].noteIndex);
            }
            return result.ToArray();
        }

        static Phoneme[] ParseLabel(string path) {
            var phonemes = new List<Phoneme>();
            using (var reader = new StreamReader(path, Encoding.UTF8)) {
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine();
                    var parts = line.Split();
                    if (parts.Length == 3 &&
                        long.TryParse(parts[0], out long pos) &&
                        long.TryParse(parts[1], out long end)) {
                        phonemes.Add(new Phoneme {
                            phoneme = parts[2],
                            position = (int)(pos / 1000L),
                        });
                    }
                }
            }
            return phonemes.ToArray();
        }

        protected override EnunuNote[] NoteGroupsToEnunu(Note[][] notes) {
            KoreanPhonemizerUtil.RomanizeNotes(notes, true, FirstConsonants, MiddleVowels, LastConsonants, semivowelSep);
            var result = new List<EnunuNote>();
            int position = 0;
            int index = 0;
            
            while (index < notes.Length) {
                if (position < notes[index][0].position) {
                    result.Add(new EnunuNote {
                        lyric = "R",
                        length = notes[index][0].position - position,
                        noteNum = 60,
                        noteIndex = -1,
                    });
                    position = notes[index][0].position;
                } else {
                    var lyric = notes[index][0].lyric;
                    result.Add(new EnunuNote {
                        lyric = lyric,
                        length = notes[index].Sum(n => n.duration),
                        noteNum = notes[index][0].tone,
                        noteIndex = index,
                    });
                    position += result.Last().length;
                    index++;
                }
            }
            return result.ToArray();
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (partResult.TryGetValue(notes, out var phonemes)) {
                var phonemes_ = phonemes.Select(p => {
                        double posMs = p.position * 0.1;
                        p.position = MsToTick(posMs) - notes[0].position;
                        return p;
                    }).ToArray();

                return new Result {
                    phonemes = phonemes_,
                };
            }
            if (SetUpException != null) {
                throw new Exception("Phonemizer failed to process.", SetUpException);
            }
            throw new Exception("Part result not found");
        }

        public override void CleanUp() {
            partResult.Clear();
        }

    }
}
