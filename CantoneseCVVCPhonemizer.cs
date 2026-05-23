using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using Pinyin;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Cantonese CVVC phonemizer.
    /// It works similarly to the Chinese CVVC phonemizer, including presamp.ini requirement.
    /// The big difference is that it converts hanzi to jyutping instead of pinyin.
    /// </summary>
    [Phonemizer("Cantonese CVVC Phonemizer", "ZH-YUE CVVC", "Lotte V", language: "UTAU")]
    public class CantoneseCVVCPhonemizer : ChineseCVVCPhonemizer {
        protected override string[] Romanize(IEnumerable<string> lyrics) {
            return Pinyin.Jyutping.Instance.HanziToPinyin(lyrics.ToList(), CanTone.Style.NORMAL, Pinyin.Error.Default).Select(res => res.pinyin).ToArray();
        }
    }
}
