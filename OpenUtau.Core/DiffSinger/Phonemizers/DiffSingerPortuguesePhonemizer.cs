using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger Portuguese Phonemizer", "DIFFS PT", language: "PT", engine: "Diffsinger")]
    public class DiffSingerPortuguesePhonemizer : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName()=>"dsdict-pt.yaml";
        public override string GetLangCode()=>"pt";
        protected override IG2p LoadBaseG2p() => new PortugueseG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "E", "O", "a", "a~", "e", "e~", "i", "i~", "o", "o~", "u", "u~"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "J", "L", "R", "S", "X", "Z", "b", "d", "dZ", "f", "g", "j", "j~", 
            "k", "l", "m", "n", "p", "r", "s", "t", "tS", "v", "w", "w~", "z"
        };
    }
}
