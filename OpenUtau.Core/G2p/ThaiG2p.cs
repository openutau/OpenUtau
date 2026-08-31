using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class ThaiG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "ก", "ข", "ฃ", "ค", "ฅ", "ฆ", "ง", "จ",
            "ฉ", "ช", "ซ", "ฌ", "ญ", "ฎ", "ฏ", "ฐ", "ฑ", "ฒ", "ณ",
            "ด", "ต", "ถ", "ท", "ธ", "น", "บ", "ป", "ผ", "ฝ", "พ", "ฟ",
            "ภ", "ม", "ย", "ร", "ฤ", "ล", "ฦ", "ว", "ศ", "ษ", "ส", "ห",
            "ฬ", "อ", "ฮ", "ฯ", "ะ", "ั", "า", "ำ", "ิ", "ี", "ึ", "ื",
            "ุ", "ู", "ฺ", "฿", "เ", "แ", "โ", "ใ", "ไ", "ๅ", "ๆ", "็", "่",
            "้", "๊", "๋", "์", "ํ", "๎",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "i", "u", "e", "o", "A", "O", "E",
            "Ua", "U", "ia", "ua", "I", "au", "b", "ch", "d", "f", "h",
            "j", "k", "kk", "l", "m", "n", "ng", "p", "pp", "r", "s", "t",
            "tt", "w", "W", "y", "Y", "B", "D", "K",
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public ThaiG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_th);
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
    }
}
