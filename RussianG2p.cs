using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

namespace OpenUtau.Core.G2p {
    // Dictionary from https://sourceforge.net/projects/cmusphinx/files/Acoustic%20and%20Language%20Models/Russian/zero_ru_cont_8k_v3.tar.gz
    public class RussianG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "-", "а", "б", "в", "г", "д", "е", "ж", "з",
            "и", "й", "к", "л", "м", "н", "о", "п", "р", "с", "т", "у",
            "ф", "х", "ц", "ч", "ш", "щ", "ъ", "ы", "ь", "э", "ю", "я", "ё"
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "aa", "ay", "b", "bb", "c", "ch",
            "d", "dd", "ee", "f", "ff", "g", "gg", "h", "hh", "i", "ii",
            "j", "ja", "je", "jo", "ju", "k", "kk", "l", "ll", "m", "mm",
            "n", "nn", "oo", "p", "pp", "r", "rr", "s", "sch", "sh", "ss",
            "t", "tt", "u", "uj", "uu", "v", "vv", "y", "yy", "z", "zh", "zz"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public RussianG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                    .Skip(4)
                    .Select((g, i) => Tuple.Create(g, i))
                    .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(Data.Resources.g2p_ru);
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
