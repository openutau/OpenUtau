using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class BrapaG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "a", "b", "c", "d", "e", "f", "g",
            "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r",
            "s", "t", "u", "v", "w", "x", "y", "z", "á", "â",
            "ã","à","ç","é","è","ê","í","ì","ó","ò","ô","õ","ñ",
            "ú","ü","ũ","\'","#","-",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "ae", "ah", "an", "ax", "b", "ch", "d", "dj", "e",
            "eh", "en", "f", "g", "h", "h9", "hr", "i", "i0", "in", "j", "k",
            "l", "lh", "m", "n", "ng", "nh", "o", "oh", "on", "p", "r", "r9",
            "rh", "rr", "rw", "s", "s9", "sh", "t", "u", "u0", "un", "v", "w", "wn",
            "x","y","yn","z","z9","cl","vf"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();
        public BrapaG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_brapa);
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
