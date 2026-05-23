using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class ArpabetG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "\'", "-", "a", "b", "c", "d", "e",
            "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p",
            "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "aa", "ae", "ah", "ao", "aw", "ay", "b", "ch",
            "d", "dh", "eh", "er", "ey", "f", "g", "hh", "ih", "iy", "jh",
            "k", "l", "m", "n", "ng", "ow", "oy", "p", "r", "s", "sh", "t",
            "th", "uh", "uw", "v", "w", "y", "z", "zh",
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public ArpabetG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(
                        Data.Resources.g2p_arpabet,
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
    }
}
