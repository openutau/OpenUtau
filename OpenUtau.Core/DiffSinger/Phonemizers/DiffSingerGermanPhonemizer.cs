using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger German Phonemizer", "DIFFS DE", language: "DE", engine: "Diffsinger")]
    public class DiffSingerGermanPhonemizer : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName()=>"dsdict-de.yaml";
        public override string GetLangCode()=>"de";
        protected override IG2p LoadBaseG2p() => new GermanG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "aa", "ae", "ah", "ao", "aw", "ax", "ay", "ee", "eh", "er", "ex", "ih", "iy", "oe", "ohh", "ooh", "oy", "ue", "uh", "uw", "yy"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "b", "cc", "ch", "d", "dh", "f", "g", "hh", "jh", "k", "l", "m",
            "n", "ng", "p", "pf", "q", "r", "rr", "s", "sh", "t", "th", "ts", "v", "w", "x", "y", "z", "zh"
        };
    }
}
