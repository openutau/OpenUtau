using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Japanese Phonemizer", "DIFFS JA", language: "JA", engine: "Diffsinger")]
    public class DiffSingerJapanesePhonemizer : DiffSingerBasePhonemizer {
        protected override string GetDictionaryName() => "dsdict-ja.yaml";

        public override string GetLangCode() => "ja";

        protected override string[] Romanize(IEnumerable<string> lyrics) {
            var lyricsArray = lyrics.ToArray();
            var kanaLyrics = lyricsArray
                .Where(Kana.Kana.IsKana)
                .ToList();
            var kanaResult = Kana.Kana.KanaToRomaji(kanaLyrics.ToList(), Kana.Error.Default, false).ToStrList();
            if (kanaResult == null) {
                return lyricsArray;
            }
            var kanaIndex = 0;
            for (int i = 0; i < lyricsArray.Length; i++) {
                if (Kana.Kana.IsKana(lyricsArray[i])) {
                    lyricsArray[i] = kanaResult[kanaIndex];
                    kanaIndex++;
                }
            }
            return lyricsArray;
        }
    }
}
