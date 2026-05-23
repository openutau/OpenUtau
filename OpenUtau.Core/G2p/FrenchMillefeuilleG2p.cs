using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    public class FrenchMillefeuilleG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "'", "-", "a", "b", "c", "d", "e", "f",
            "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q",
            "r", "s", "t", "u", "v", "w", "x", "y", "z", "é",
            "è", "ê", "à", "â", "î", "ô", "ù", "û", "ç", "œ",
            "ï", "(", ")", "0", "1", "2", "3", "4", "5", "6",
            "7", "8", "9",
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "ah", "eh", "ae", "ee", "oe", "ih", "oh", "oo", "ou",
            "uh", "en", "in", "on", "uy", "y", "w", "f", "k", "p", "s", "sh",
            "t", "h", "b", "d", "g", "l", "m", "n", "r", "v", "z", "j", "ng", "q",
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public FrenchMillefeuilleG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_fr_millefeuille);
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
