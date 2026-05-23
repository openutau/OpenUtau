using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class ItalianG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "'", "a", "b", "c", "d", "e",
            "f", "g", "h", "i", "j", "k", "l", "m", "n",
            "o", "p", "q", "r", "s", "t", "u", "v", "w",
            "x", "y", "z", "à", "è", "é", "ì", "í", "ò",
            "ù", "ú"
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "b", "d", "dz", "dZZ", "e",
            "EE", "f", "g", "i", "j", "JJ", "k", "l", "LL",
            "m", "n", "nf", "ng", "o", "OO", "p", "r", "s",
            "SS", "t", "ts", "tSS", "u", "v", "w", "z"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public ItalianG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_it,
                        s => s,
                        s => RemoveTailDigits(s));
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
