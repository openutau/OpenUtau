using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class GermanG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "2", "3", "/", "a", "b", "c", "d", "e",
            "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p",
            "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
            "ä", "ë", "ö", "ü", "ß", "ê", "î", "ô", "á", "é",
            "í", "ú", "à", "è", "ù", "ę",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "aa", "ae", "ah", "ao", "aw", "ax", "ay",
            "b", "cc", "ch", "d", "dh", "ee", "eh", "er", "ex", "f",
            "g", "hh", "ih", "iy", "jh", "k", "l", "m", "n", "ng",
            "oe", "ohh", "ooh", "oy", "p", "pf", "q", "r", "rr", "s",
            "sh", "t", "th", "ts", "ue", "uh", "uw", "v", "w", "x",
            "y", "yy", "z", "zh",
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public GermanG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_de);
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
