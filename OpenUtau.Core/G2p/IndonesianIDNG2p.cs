using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class IndonesianG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "\'", "-", "a", "b", "c", "d", "e",
            "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p",
            "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
            "A", "B", "C", "D", "E",
            "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P",
            "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "ax", "b", "c", "d", "e",
            "f", "g", "h", "i", "j", "k", "kh",
            "l", "m", "n", "ng", "ny", "o", "p", "r", "s", "sy",
            "t" ,"u", "v", "w", "y", "z"
        };

        private static object lockObj = new object();
        private static IG2p dict;
        private static Dictionary<string, int> graphemeIndexes;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public IndonesianG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(
                        Data.Resources.g2p_bahasa,
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