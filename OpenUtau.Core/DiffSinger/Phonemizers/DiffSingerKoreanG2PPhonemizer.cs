using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger Korean G2P Phonemizer", "DIFFS KO", language: "KO", author: "Cardroid6", engine: "Diffsinger")]
    public class DiffSingerKoreanG2PPhonemizer : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName() => "dsdict-ko.yaml";
        public override string GetLangCode()=>"ko";
        protected override IG2p LoadBaseG2p() => new KoreanG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "e", "eo", "eu", "i", "o", "u", "w", "y"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "K", "L", "M", "N", "NG", "P", "T", "b", "ch", "d",
            "g", "h", "j", "jj", "k", "kk", "m", "n", "p", "pp",
            "r", "s", "ss", "t", "tt"
        };
    }
}
