using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger Filipino Phonemizer", "DIFFS FIL", language: "FIL", author: "julieraptor")]
    public class DiffSingerFilipinoPhonemizer : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName()=>"dsdict-fil.yaml";
        public override string GetLangCode()=>"fil";
        protected override IG2p LoadBaseG2p() => new FilipinoG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "e", "i", "o", "u"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "q", "b", "d", "dy", "f", "g", "H", "hh", "j", "k", "l",
            "m", "n", "ng", "ny", "p", "dx", "s", "sy", "t", "th",
            "ch", "v", "w", "z"
        };
    }
}
