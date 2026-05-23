using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class FrenchG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "-", "a", "b", "c", "d", "e", "f", "g",
            "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r",
            "s", "t", "u", "v", "w", "x", "y", "z", "à", "á", "â", "ä",
            "æ", "ç", "è", "é", "ê", "ë", "î", "ï", "ñ", "ô", "ö",
            "ù", "ú", "û", "ü", "ÿ"
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "aa", "ai", "an", "au", "bb", "ch", "dd",
            "ee", "ei", "eu", "ff", "gg", "gn", "ii", "in", "jj", "kk",
            "ll", "mm", "nn", "oe", "on", "oo", "ou", "pp", "rr", "ss",
            "tt", "un", "uu", "uy", "vv", "ww", "yy", "zz"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public FrenchG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_fr);
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
