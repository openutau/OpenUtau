using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger French Millefeuille Phonemizer", "DIFFS FR MILLE", language: "FR")]
    public class DiffSingerFrenchMillfeuillePhonemizer : DiffSingerG2pPhonemizer
    {
        protected override string GetDictionaryName()=> "dsdict-fr-millefeuille.yaml";
        public override string GetLangCode()=>"fr";
        protected override IG2p LoadBaseG2p() => new FrenchMillefeuilleG2p();
        protected override string[] GetBaseG2pVowels() => new string[] {
            "ah", "eh", "ae", "ee", "oe", "ih", "oh", "oo", "ou", "uh", "en", "in", "on"
        };

        protected override string[] GetBaseG2pConsonants() => new string[] {
            "y", "w", "f", "k", "p", "s", "sh", "t", "h", "b", "d", "g", "l",
            "m", "n", "r", "v", "z", "j", "ng", "q", "uy", "vf", "cl"
        };
    }
}
