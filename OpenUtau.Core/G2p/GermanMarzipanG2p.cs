using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class GermanMarzipanG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "a", "b", "c", "d", "e", "f", "g", "h",
            "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s",
            "t", "u", "v", "w", "x", "y", "z", "ä", "ë", "ö", "ü", "ß",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "er", "eh", "e", "ih", "i", "uh",
            "u", "oh", "o", "ueh", "ue", "oeh", "oe", "ex", "ei", "au",
            "eu", "w", "j", "p", "t", "k", "f", "s", "sh", "ch",
            "xh", "h", "pf", "ts", "tsh", "th", "m", "n", "ng", "b",
            "d", "g", "v", "z", "l", "r", "dsh", "zh", "rh", "rr",
            "rx", "dh", "q", "vf", "cl",
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public GermanMarzipanG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_de_marzipan);
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
