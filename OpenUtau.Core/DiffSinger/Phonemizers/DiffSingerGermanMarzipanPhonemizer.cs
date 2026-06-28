using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger German Marzipan Phonemizer", "DIFFS DE MARZ", language: "DE")]
    public class DiDiffSingerGermanMarzipanPhonemizerr : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName()=> "dsdict-de-marzipan.yaml";
        public override string GetLangCode()=>"de";
        protected override IG2p LoadBaseG2p() => new GermanMarzipanG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "a", "er", "eh", "e", "ih", "i", "uh", "u", "oh", "o", "ueh", "ue", "oeh", "oe", "ex", "ei", "au", "eu"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "j", "p", "t", "k", "f", "s", "sh", "ch", "xh", "h", "pf", "ts",
            "tsh", "th", "m", "n", "ng", "b", "d", "g", "v", "z", "l", "r", "dsh", "zh", "rh", "rr",
            "rx", "dh", "q", "vf", "cl"
        };
    }
}
