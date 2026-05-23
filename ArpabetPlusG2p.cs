using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class ArpabetPlusG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "\'", "-", "a", "b", "c", "d", "e",
            "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p",
            "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
            "A", "B", "C", "D", "E",
            "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P",
            "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "aa", "ae", "ah", "ao", "aw", "ax", "ay", "b", "ch",
            "d", "dh", "dr", "dx", "eh", "er", "ey", "f", "g", "hh", "ih", "iy", "jh",
            "k", "l", "m", "n", "ng", "ow", "oy", "p", "q", "r", "s", "sh", "t",
            "th", "tr", "uh", "uw", "v", "w", "y", "z", "zh",
        };

        private static object lockObj = new object();
        private static IG2p dict;
        private static Dictionary<string, int> graphemeIndexes;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public ArpabetPlusG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(
                        Data.Resources.g2p_arpabet_plus,
                        s => s.ToLowerInvariant(),
                        s => RemoveTailDigits(s.ToLowerInvariant()));
                    dict = tuple.Item1;
                    session = tuple.Item2;
                }
            }
            Dict = dict;
            PredCache = predCache;
            GraphemeIndexes = graphemeIndexes;
            Phonemes = phonemes;
            Session = session;
        }
    }
}
