using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class SpanishG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "\'", "-", "a", "b", "c", "d", "e",
            "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p",
            "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
            "Á", "É", "Í", "Ó", "Ú", "á", "ã", "é", "ë", "ê", "í",
            "ñ", "ó", "ú", "ü"
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "b", "B", "ch", "d", "D", "e", "f",
            "g", "G", "gn", "i", "I", "k", "l", "ll", "m", "n", "o",
            "p", "r", "rr", "s", "t", "u", "U", "w", "x", "y", "Y", "z"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public SpanishG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(
                        Data.Resources.g2p_es,
                        s => s.ToLowerInvariant());
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
