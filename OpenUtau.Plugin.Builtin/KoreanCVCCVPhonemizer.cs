using System;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.MusicTheory;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using static OpenUtau.Api.Phonemizer;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Korean CVCCV Phonemizer", "KO CVCCV", "RYUUSEI", language:"KO")]
    public class KoreanCVCCVPhonemizer : BaseKoreanPhonemizer {
        static readonly string initialConsonantsTable = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        static readonly string vowelsTable = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        static readonly string YVowelsTable = "ㅣㅑㅖㅛㅠㅕ";
        static readonly string lastConsonantsTable = "　ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

        static readonly ushort unicodeKoreanBase = 0xAC00;
        static readonly ushort unicodeKoreanLast = 0xD79F;

        public enum TYPE_FLAG {
            VC_CV,
            VRC_CV,
            VC_RCV,
            VC_NCV,
            VC_NRCV,
            VRC_NCV,
            VC_CV_ADDED_Y,
            VCC_CV,
            VCC_CV_ADDED_Y,
            VC_CCV,
            VC_CCV_ADDED_Y,
            _VCV,
            CV,
        };

        static readonly TYPE_FLAG[,] typeTable = new TYPE_FLAG[,] {
            {   TYPE_FLAG._VCV,   TYPE_FLAG.VRC_CV,   TYPE_FLAG._VCV,     TYPE_FLAG._VCV,     TYPE_FLAG.VRC_CV,   TYPE_FLAG._VCV,     TYPE_FLAG._VCV,     TYPE_FLAG._VCV,     TYPE_FLAG.VRC_CV,   TYPE_FLAG._VCV,           TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG._VCV,     TYPE_FLAG._VCV,     TYPE_FLAG.VRC_CV,   TYPE_FLAG.VRC_CV,   TYPE_FLAG.VRC_CV,   TYPE_FLAG.VRC_CV,   TYPE_FLAG.VRC_CV, TYPE_FLAG._VCV,   }, // 받침누락
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV_ADDED_Y, TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.CV,       TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㄱ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV_ADDED_Y, TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.VC_RCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㄲ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV_ADDED_Y, TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㄳ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.CV,       TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄴ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.CV,       TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄵ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.CV,       TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄶ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,          TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG.CV,       TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㄷ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.CV,       TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄹ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄺ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄼ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄻ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄽ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄾ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㄿ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㅀ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,         TYPE_FLAG.VC_CCV_ADDED_Y,   TYPE_FLAG.CV,       TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㅁ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.CV,       TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅂ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅄ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,          TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG.CV,       TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅅ
            {   TYPE_FLAG.VRC_NCV,TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,        TYPE_FLAG.VRC_NCV,          TYPE_FLAG.VC_RCV,   TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,  TYPE_FLAG.VRC_NCV,TYPE_FLAG.VRC_NCV,}, // ㅆ
            {   TYPE_FLAG.VC_CCV, TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV,   TYPE_FLAG.VCC_CV, TYPE_FLAG.VC_CCV, }, // ㅇ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG.CV,       TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅈ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG.CV,       TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅊ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅋ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅌ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VCC_CV,         TYPE_FLAG.VCC_CV_ADDED_Y,   TYPE_FLAG.VC_NRCV,  TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅍ
            {   TYPE_FLAG.VC_NCV, TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,          TYPE_FLAG.VC_CV_ADDED_Y,    TYPE_FLAG.CV,       TYPE_FLAG.VC_NCV,   TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,    TYPE_FLAG.VC_CV,  TYPE_FLAG.VC_NCV, }, // ㅎ
                // ㄱ             // ㄲ               // ㄴ               // ㄷ               // ㄸ               // ㄹ               // ㅁ               // ㅂ               // ㅃ               // ㅅ                 // ㅆ                       // ㅇ               // ㅈ               // ㅉ               // ㅊ               // ㅋ               // ㅌ               // ㅍ             // ㅎ
        };

        private char[] SeparateHangul(char letter) {
            if (letter == 0) return new char[] { '　', '　', '　' };
            var u16 = Convert.ToUInt16(letter);

            if (u16 < unicodeKoreanBase || u16 > unicodeKoreanLast)
                return new char[] { letter };

            u16 -= unicodeKoreanBase;

            var i = u16 / (21 * 28);
            u16 %= 21 * 28;
            var v = u16 / 28;
            u16 %= 28;
            var l = u16;

            return new char[] { initialConsonantsTable[i], vowelsTable[v], lastConsonantsTable[l] };
        }

        // 초성
        static readonly string[] continuousinitialConsonants = new string[] {
            "g=ㄱ,ㄺ",
            "gg=ㄲ",
            "n=ㄴ,ㄵ,ㄶ",
            "d=ㄷ",
            "dd=ㄸ",
            "r=ㄹ",
            "m=ㅁ,ㄻ",
            "b=ㅂ,ㄼ",
            "bb=ㅃ",
            "s=ㅅ,ㅄ,ㄽ,ㄳ",
            "ss=ㅆ",
            "=ㅇ",
            "j=ㅈ",
            "jj=ㅉ",
            "ch=ㅊ",
            "k=ㅋ",
            "t=ㅌ",
            "p=ㅍ,ㄿ",
            "h=ㅎ,ㅀ",
        };

        static readonly string[] ccvContinuousinitialConsonants = new string[] {
            "g=ㄱ",
            "gg=ㄲ",
            "n=ㄴ",
            "d=ㄷ",
            "dd=ㄸ",
            "l=ㄹ,ㄺ,ㄼ,ㄻ,ㄽ,ㄾ,ㄿ,ㅀ",
            "m=ㅁ",
            "b=ㅂ",
            "bb=ㅃ",
            "s=ㅅ",
            "ss=ㅆ",
            "=ㅇ",
            "j=ㅈ",
            "jj=ㅉ",
            "ch=ㅊ",
            "k=ㅋ",
            "t=ㅌ",
            "p=ㅍ",
            "h=ㅎ",
        };

        static readonly string[] vrcContinuousinitialConsonants = new string[] {
            "gg=ㄱ,ㄲ",
            "n=ㄴ,ㄵ,ㄶ",
            "dd=ㄷ,ㄸ",
            "r=ㄹ",
            "m=ㅁ",
            "d=ㅎ",
            "bb=ㅂ,ㅃ",
            "ss=ㅅ,ㅆ",
            "=ㅇ",
            "jj=ㅈ,ㅉ",
            "ch=ㅊ",
            "k=ㅋ",
            "t=ㅌ",
            "p=ㅍ",
        };

        // 일반 모음
        static readonly string[] vowels = new string[] {
            "a=ㅏ",
            "ya=ㅑ",
            "eo=ㅓ",
            "yeo=ㅕ",
            "o=ㅗ",
            "yo=ㅛ",
            "u=ㅜ",
            "yu=ㅠ",
            "eu=ㅡ",
            "i=ㅣ",
            "e=ㅔ,ㅐ",
            "ye=ㅖ,ㅒ",
            "eui=ㅢ",
            "wa=ㅘ",
            "weo=ㅝ",
            "we=ㅞ,ㅙ,ㅚ",
            "wi=ㅟ",
        };

        static readonly string[] subsequentVowels = new string[] {
            "a=ㅏ,ㅑ,ㅘ",
            "eo=ㅓ,ㅕ,ㅝ",
            "o=ㅗ,ㅛ",
            "u=ㅜ,ㅠ",
            "eu=ㅡ",
            "e=ㅔ,ㅐ,ㅞ,ㅙ,ㅚ,ㅖ,ㅒ",
            "i=ㅣ,ㅢ,ㅟ",
        };

        static readonly string[] lastConsonants = new string[] {
            "K=ㄱ,ㅋ,ㄲ,ㄳ",
            "T=ㅅ,ㅈ,ㅌ,ㄷ,ㅆ,ㅎ,ㅊ,ㄸ",
            "P=ㅂ,ㅍ,ㅃ,ㅄ",
            "m=ㅁ",
            "n=ㄴ,ㄵ",
            "ng=ㅇ",
            "l=ㄹ,ㄺ,ㄻ,ㄼ,ㄽ,ㄾ,ㄿ,ㅀ",
            "-=　"
        };

        static readonly string[] vcLastConsonants = new string[] {
            "k=ㄱ,ㅋ,ㄲ,ㄳ",
            "t=ㅅ,ㅈ,ㅌ,ㄸ,ㅊ,ㅉ,ㄷ,ㅎ",
            "p=ㅍ,ㅃ,ㅂ,ㅄ",
            "m=ㅁ",
            "n=ㄴ,ㄵ,ㄶ",
            "ng=ㅇ",
            "l=ㄹ,ㄺ,ㄻ,ㄼ,ㄽ,ㄾ,ㄿ,ㅀ",
            "ss=ㅆ",
        };

        static readonly string[] vrcLastConsonants = new string[] {
            "k=ㄱ,ㅋ,ㄲ,ㄳ",
            "t=ㅅ,ㅈ,ㅌ,ㄸ,ㅊ,ㅉ,ㄷ,ㅎ,ㅆ",
            "p=ㅍ,ㅃ,ㅂ,ㅄ",
            "m=ㅁ",
            "n=ㄴ,ㄵ,ㄶ",
            "ng=ㅇ",
            "l=ㄹ,ㄺ,ㄻ,ㄼ,ㄽ,ㄾ,ㄿ,ㅀ",
        };

        /// <summary>
        /// Apply Korean sandhi rules to Hangeul lyrics.
        /// </summary>
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            base.SetUp(groups, project, track);
            // variate lyrics 
            KoreanPhonemizerUtil.RomanizeNotes(groups, false);
        }

        static readonly Dictionary<string, string> initialConsonantLookup;
        static readonly Dictionary<string, string> ccvContinuousinitialConsonantsLookup;
        static readonly Dictionary<string, string> vrcInitialConsonantLookup;
        static readonly Dictionary<string, string> vowelLookup;
        static readonly Dictionary<string, string> subsequentVowelLookup;
        static readonly Dictionary<string, string> lastConsonantsLookup;
        static readonly Dictionary<string, string> vcLastConsonantsLookup;
        static readonly Dictionary<string, string> vrcLastConsonantsLookup;


        static KoreanCVCCVPhonemizer() {
            initialConsonantLookup = continuousinitialConsonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            ccvContinuousinitialConsonantsLookup = ccvContinuousinitialConsonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            vrcInitialConsonantLookup = vrcContinuousinitialConsonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            vowelLookup = vowels.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            subsequentVowelLookup = subsequentVowels.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            lastConsonantsLookup = lastConsonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            vcLastConsonantsLookup = vcLastConsonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            vrcLastConsonantsLookup = vrcLastConsonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        // Store singer in field, will try reading presamp.ini later
        private USinger singer;

        public override void SetSinger(USinger singer) => this.singer = singer;
        
        // Legacy mapping. Might adjust later to new mapping style.
		public override bool LegacyMapping => true;
        
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            var prevLyric = prevNeighbour?.lyric;
            var nextLyric = nextNeighbour?.lyric;
            char[] prevKoreanLyrics = { '　', '　', '　' };
            char[] currentKoreanLyrics = { '　', '　', '　' };
            char[] nextKoreanLyrics = { '　', '　', '　' };

            int totalDuration = notes.Sum(n => n.duration);
            int vcLength = 120;
            
            var phoneticHint = RenderPhoneticHint(singer, notes[0], totalDuration);
            if (phoneticHint != null) {
                return (Result) phoneticHint;
            }

            var romaji2Korean = ConvertRomajiNoteToHangeul(notes, prevNeighbour, nextNeighbour);
            if (romaji2Korean != null) {
                notes = romaji2Korean.KoreanLyricNotes;
                prevNeighbour = romaji2Korean.KoreanLyricPrevNote;
                nextNeighbour = romaji2Korean.KoreanLyricNextNote;
                
                prevLyric = prevNeighbour?.lyric;
                nextLyric = nextNeighbour?.lyric;
            } 


            List<Phoneme> phonemesArr = new List<Phoneme>();

            var currentLyric = notes[0].lyric;
            currentKoreanLyrics = SeparateHangul(currentLyric != null ? currentLyric[0] : '\0');


            if (currentLyric[0] >= '가' && currentLyric[0] <= '힣') {

            } else {
                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme {
                        phoneme = currentLyric,
                    }
                }};
            }

            if (prevLyric != null && prevLyric[0] >= '가' && prevLyric[0] <= '힣') {
                // 앞글자 있음
                prevKoreanLyrics = SeparateHangul(prevLyric[0]);

                TYPE_FLAG type = typeTable[lastConsonantsTable.IndexOf(prevKoreanLyrics[2]), initialConsonantsTable.IndexOf(currentKoreanLyrics[0])];


                initialConsonantLookup.TryGetValue(currentKoreanLyrics[0].ToString(), out var currentConsonants);
                ccvContinuousinitialConsonantsLookup.TryGetValue(currentKoreanLyrics[0].ToString(), out var currentCCVConsonants);
                vrcInitialConsonantLookup.TryGetValue(currentKoreanLyrics[0].ToString(), out var vrcInitialConsonants);
                vowelLookup.TryGetValue(currentKoreanLyrics[1].ToString(), out var currentVowel);
                lastConsonantsLookup.TryGetValue(prevKoreanLyrics[2].ToString(), out var prevLastConsonants);
                subsequentVowelLookup.TryGetValue(currentKoreanLyrics[1].ToString(), out var currentSubsequentVowel);
                subsequentVowelLookup.TryGetValue(prevKoreanLyrics[1].ToString(), out var prevSubsequentVowel);
                initialConsonantLookup.TryGetValue(prevKoreanLyrics[2].ToString(), out var prevInitialConsonants);

                switch (type) {
                    case TYPE_FLAG.VCC_CV:
                        phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentConsonants}{currentVowel}",
                                }
                            );
                        break;

                    case TYPE_FLAG.VCC_CV_ADDED_Y:
                        phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"ss{currentVowel}",
                                }
                            );
                        break;

                    case TYPE_FLAG.VC_CCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{prevLastConsonants} {currentCCVConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_CCV_ADDED_Y:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentCCVConsonants}{currentVowel}",
                            }
                        );
                                                
                        break;

                    case TYPE_FLAG.VC_NCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"- {currentCCVConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_NRCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"- {prevInitialConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_CV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG.VRC_CV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG.VRC_NCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"- {vrcInitialConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_CV_ADDED_Y:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_RCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{prevInitialConsonants}{currentVowel}",
                            }
                        );

                        break;

                    case TYPE_FLAG._VCV:

                        phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{prevSubsequentVowel} {currentConsonants}{currentVowel}",
                                }
                            );

                        break;

                    case TYPE_FLAG.CV:
                        phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{prevSubsequentVowel} {prevInitialConsonants}{currentVowel}",
                                }
                            );

                        break;
                }

            } else {
                // 앞글자 없음

                initialConsonantLookup.TryGetValue(currentKoreanLyrics[0].ToString(), out var currentConsonants);
                vowelLookup.TryGetValue(currentKoreanLyrics[1].ToString(), out var currentVowel);

                phonemesArr.Add(
                    new Phoneme {
                        phoneme = $"- {currentConsonants}{currentVowel}",
                    }
                );
            }

            if (nextLyric != null && nextLyric[0] >= '가' && nextLyric[0] <= '힣') {
                // 뒷글자 있음
                nextKoreanLyrics = SeparateHangul(nextLyric[0]);

                TYPE_FLAG type = typeTable[lastConsonantsTable.IndexOf(currentKoreanLyrics[2]), initialConsonantsTable.IndexOf(nextKoreanLyrics[0])];

                initialConsonantLookup.TryGetValue(nextKoreanLyrics[0].ToString(), out var nextConsonants);
                vcLastConsonantsLookup.TryGetValue(nextKoreanLyrics[0].ToString(), out var nextVCCConsonants);
                vrcLastConsonantsLookup.TryGetValue(nextKoreanLyrics[0].ToString(), out var nextVRCConsonants);
                subsequentVowelLookup.TryGetValue(currentKoreanLyrics[1].ToString(), out var currentSubsequentVowel);
                lastConsonantsLookup.TryGetValue(currentKoreanLyrics[2].ToString(), out var currentLastConsonants);
                vcLastConsonantsLookup.TryGetValue(currentKoreanLyrics[2].ToString(), out var currentVCLastConsonants);


                switch (type) {
                    case TYPE_FLAG.VCC_CV:
                        phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel}{currentLastConsonants} {nextVCCConsonants}",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );

                        break;

                    case TYPE_FLAG.VCC_CV_ADDED_Y:
                        if (YVowelsTable.IndexOf(nextKoreanLyrics[1]) > 0) {
                            phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel}{currentVCLastConsonants} sy",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );
                        } else {
                            phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel}{currentVCLastConsonants} s",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );
                        }
                        
                        break;

                    case TYPE_FLAG.VC_CCV:
                        phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel} {currentVCLastConsonants}",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );

                        break;

                    case TYPE_FLAG.VC_CCV_ADDED_Y:
                        if (YVowelsTable.IndexOf(nextKoreanLyrics[1]) > 0) {
                            phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel} {nextConsonants}y",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );
                        } else {
                            phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel} {currentVCLastConsonants}",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );
                        }

                        break;

                    case TYPE_FLAG.VC_NCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentSubsequentVowel} {currentVCLastConsonants}",
                                position = totalDuration - Math.Min(totalDuration / 3, 120)
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_NRCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentSubsequentVowel} {currentVCLastConsonants}",
                                position = totalDuration - Math.Min(totalDuration / 3, 120)
                            }
                        );

                        break;

                    case TYPE_FLAG.VRC_NCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentSubsequentVowel} {currentLastConsonants}",
                                position = totalDuration - Math.Min(totalDuration / 3, 120)
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_CV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentSubsequentVowel} {currentVCLastConsonants}",
                                position = totalDuration - Math.Min(totalDuration / 3, 120)
                            }
                        );

                        break;

                    case TYPE_FLAG.VRC_CV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentSubsequentVowel} {nextVRCConsonants}",
                                position = totalDuration - Math.Min(totalDuration / 4, 100)
                            }
                        );

                        break;

                    case TYPE_FLAG.VC_CV_ADDED_Y:
                        if (YVowelsTable.IndexOf(nextKoreanLyrics[1]) > 0) {
                            phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel} {nextVCCConsonants}y",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );
                        } else {
                            phonemesArr.Add(
                                new Phoneme {
                                    phoneme = $"{currentSubsequentVowel} {nextVCCConsonants}",
                                    position = totalDuration - Math.Min(totalDuration / 3, 120)
                                }
                            );
                        }

                        break;

                    case TYPE_FLAG.VC_RCV:
                        phonemesArr.Add(
                            new Phoneme {
                                phoneme = $"{currentSubsequentVowel} {currentVCLastConsonants}",
                                position = totalDuration - Math.Min(totalDuration / 3, 120)
                            }
                        );

                        break;

                    case TYPE_FLAG.CV:

                        break;
                };

            } else {
                // 뒷글자 없음
                subsequentVowelLookup.TryGetValue(currentKoreanLyrics[1].ToString(), out var currentSubsequentVowel);
                lastConsonantsLookup.TryGetValue(currentKoreanLyrics[2].ToString(), out var currentLastConsonants);

                phonemesArr.Add(
                    new Phoneme {
                        phoneme = $"{currentSubsequentVowel} {currentLastConsonants}",
                        position = totalDuration - 60
                    }
                );

            }

            // 여기서 phonemes 바꿔줘야함
            Phoneme[] phonemes = phonemesArr.ToArray();

            return new Result {
                phonemes = phonemes
            };
        }

    }
}
