using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Api;

// The data for the G2P is sourced from https://github.com/CUNY-CL/wikipron/blob/master/data/scrape/tsv/ukr_cyrl_narrow.tsv and edited by phi_pea
// G2P was trained by FRANKENRECORDS

namespace OpenUtau.Core.G2p {
    public class UkrainianG2p : G2pPack {
        private static readonly string[] graphemes = new string[] {
            "", "", "", "", "\'", "-", "а", "б", "в", "г", "ґ", "д", "е", "є", "ж", "з", "и", "і", "ї", "й", "к", "л", "м", "н", "о", "п", "р", "с", "т", "у", "ф", "х", "ц", "ч", "ш", "щ", "ь", "ю", "я"
        };

        private static readonly string[] phonemes = new string[] {
            "", "", "", "", "a", "b","bq","d","dq","dz","dzh","dzhq","dzq","e","f","fq","g","gq","h","hq","i","j","k","kq","l","lq","m","mq","n","nq","o","p","pq","r","rq","s","sh","shq","sq","t","tq","ts","tsh","tshq","tsq","u","v","vq","x","xq","y","z","zh","zhq", "zq"
        };

        private static object lockObj = new object();
        private static Dictionary<string, int> graphemeIndexes;
        private static IG2p dict;
        private static InferenceSession session;
        private static Dictionary<string, string[]> predCache = new Dictionary<string, string[]>();

        public UkrainianG2p() {
            lock (lockObj) {
                if (graphemeIndexes == null) {
                    graphemeIndexes = graphemes
                        .Skip(4)
                        .Select((g, i) => Tuple.Create(g, i))
                        .ToDictionary(t => t.Item1, t => t.Item2 + 4);
                    var tuple = LoadPack(
                        Data.Resources.g2p_uk,
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
