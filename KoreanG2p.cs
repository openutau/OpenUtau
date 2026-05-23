using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.ML.OnnxRuntime;

using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class KoreanG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ",
            "ㄸ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄾ", "ㅀ", "ㅁ", "ㅂ", "ㅃ",
            "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ",
            "ㅎ", "ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ",
            "ㅘ", "ㅙ", "ㅚ", "ㅛ", "ㅜ", "ㅝ", "ㅞ", "ㅟ", "ㅠ", "ㅡ",
            "ㅢ", "ㅣ",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "K", "L", "M", "N", "NG", "P", "T",
            "a", "b", "ch", "d", "e", "eo", "eu", "g", "h",
            "i", "j", "jj", "k", "kk", "m", "n", "o", "p", "pp",
            "r", "s", "ss", "t", "tt", "u", "w", "y",
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public KoreanG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(
                        Data.Resources.g2p_ko,
                        s => s.ToLowerInvariant(),
                        s => RemoveTailDigits(s.ToLowerInvariant()));
                    dict = tuple.Item1;
                    session = tuple.Item2;
                }
            }
            GraphemeIndexes = graphemeIndexes;
            Phonemes = phonemes;
            Dict = dict;
            Session = session;
            PredCache = predCache;
        }

        protected override string[] Predict(string grapheme) {
            var sb = new StringBuilder();
            foreach (var item in grapheme) {
                if (TryDivideHangeul(item, out var jamo)) {
                    sb.Append(jamo);
                } else {
                    sb.Append(item);
                }
            }

            return base.Predict(sb.ToString());
        }

        private static readonly string onset = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        private static readonly string nucleus = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";
        private static readonly string coda = " ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

        private static readonly ushort UnicodeHangeulBase = 0xAC00;
        private static readonly ushort UnicodeHangeulLast = 0xD79F;

        public bool TryDivideHangeul(char c, out string result) {
            ushort check = Convert.ToUInt16(c);

            if (check > UnicodeHangeulLast || check < UnicodeHangeulBase) {
                result = "";
                return false;
            }

            int Code = check - UnicodeHangeulBase;

            int codaCode = Code % 28;
            Code = (Code - codaCode) / 28;

            int nucleusCode = Code % 21;
            Code = (Code - nucleusCode) / 21;

            int onsetCode = Code;

            result = $"{onset[onsetCode]}{nucleus[nucleusCode]}{coda[codaCode]}";
            return true;
        }
    }
}
