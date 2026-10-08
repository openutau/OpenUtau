using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger Russian Phonemizer", "DIFFS RU", language: "RU", engine: "Diffsinger")]
    public class DiffSingerRussianPhonemizer : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName()=>"dsdict-ru.yaml";
        public override string GetLangCode()=>"ru";
        protected override IG2p LoadBaseG2p() => new RussianG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "aa", "ay", "ee", "i", "ii", "ja", "je", "jo", "ju", "oo",
            "u", "uj", "uu", "y", "yy"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "bb", "c", "ch", "d", "dd", "f", "ff", "g", "gg", "h", "hh",
            "j", "k", "kk", "l", "ll", "m", "mm", "n", "nn", "p", "pp", "r", 
            "rr", "s", "sch", "sh", "ss", "t", "tt", "v", "vv", "z", "zh", "zz"
        };
    }
}
