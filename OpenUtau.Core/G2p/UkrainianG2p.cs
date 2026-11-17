using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class UkrainianG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "'", "а", "б", "в", "г", "д", "е",
            "ж", "з", "и", "й", "к", "л", "м", "н", "о", "п", "р", "с", "т", "у",
            "ф", "х", "ц", "ч", "ш", "щ", "ь", "ю", "я", "є", "і", "ї", "ґ"
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "b", "bj", "c", "ch", "cj",
            "d", "dj", "e", "f", "fj", "g", "gh", "ghj", "gj", "h", "hj", "i",
            "j", "k", "kj", "l", "lj", "m", "mj", "n", "nj", "o", "p", "pj", "r",
            "rj", "s", "sh", "shj", "sj", "t", "tj", "u", "v", "vj", "y", "z",
            "zh", "zhj", "zj"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public UkrainianG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                    .Skip(4)
                    .Select((g, i) => Tuple.Create(g, i))
                    .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_uk);
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
