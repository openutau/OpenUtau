using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Ukrainian Phonemizer", "DIFFS UK", language: "UK")]
    public class DiffSingerUkrainianPhonemizer : DiffSingerG2pPhonemizer {
        protected override string GetDictionaryName() => "dsdict-uk.yaml";
        public override string GetLangCode() => "uk";
        protected override IG2p LoadBaseG2p() => new UkrainianG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "e", "i", "o", "u", "y"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "bj", "c", "ch", "cj", "d", "dj", "f", "fj", "g", "gh", "ghj",
            "gj", "h", "hj", "j", "k", "kj", "l", "lj", "m", "mj", "n", "nj",
            "p", "pj", "r", "rj", "s", "sh", "shj", "sj", "t", "tj", "v", "vj",
            "z", "zh", "zhj", "zj"
        };
    }
}
