using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using Pinyin;

namespace OpenUtau.Core.DiffSinger {
    [Phonemizer("DiffSinger Jyutping Phonemizer", "DIFFS ZH-YUE", language: "ZH-YUE", engine: "Diffsinger")]
    public class DiffSingerJyutpingPhonemizer : DiffSingerBasePhonemizer {
        protected override string GetDictionaryName() => "dsdict-zh-yue.yaml";
        public override string GetLangCode() => "yue";
        protected override string[] Romanize(IEnumerable<string> lyrics) {
            return Pinyin.Jyutping.Instance.HanziToPinyin(lyrics.ToList(), CanTone.Style.NORMAL, Pinyin.Error.Default).Select(res => res.pinyin).ToArray();
        }
    }
}
