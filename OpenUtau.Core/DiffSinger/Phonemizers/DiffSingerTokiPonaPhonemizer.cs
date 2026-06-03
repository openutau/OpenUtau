using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Toki Pona Phonemizer", "DIFFS TP", language: "TP")]
    public class DiffSingerTokiPonaPhonemizer : DiffSingerBasePhonemizer {
        protected override string GetDictionaryName() => "dsdict-tp.yaml";

        public override string GetLangCode() => "tp";

        protected override string[] Romanize(IEnumerable<string> lyrics) {
            var lyricsArray = lyrics.ToArray();
            return lyricsArray;
        }
    }
}
