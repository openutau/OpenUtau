using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger Thai Phonemizer", "DIFFS TH", language: "TH")]
    public class DiffSingerThaiPhonemizer : DiffSingerG2pPhonemizer {
        protected override string GetDictionaryName() => "dsdict-th.yaml";
        public override string GetLangCode() => "th";
        protected override IG2p LoadBaseG2p() => new ThaiG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "i", "u", "e", "o", "A", "O", "E",
            "Ua", "U", "ia", "ua", "I", "au"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "ch", "d", "f", "h", "j", "k", "kk", "l", "m",
            "n", "ng", "p", "pp", "r", "s", "t", "tt", "w", "W",
            "y", "Y", "B", "D", "K"
        };
    }
}
