using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Pinyin;

namespace OpenUtau.Core {
    public abstract class BaseChinesePhonemizer : Phonemizer {
        public static Note[] ChangeLyric(Note[] group, string lyric) {
            var oldNote = group[0];
            group[0] = new Note {
                lyric = lyric,
                phoneticHint = oldNote.phoneticHint,
                tone = oldNote.tone,
                position = oldNote.position,
                duration = oldNote.duration,
                phonemeAttributes = oldNote.phonemeAttributes,
            };
            return group;
        }

        public static string[] Romanize(IEnumerable<string> lyrics) {
            var lyricsArray = lyrics.ToArray();
            var hanziLyrics = lyricsArray
                .Where(Pinyin.Pinyin.Instance.IsHanzi)
                .ToList();
            var pinyinResult = Pinyin.Pinyin.Instance.HanziToPinyin(hanziLyrics, ManTone.Style.NORMAL, Pinyin.Error.Default, false, false, false).ToStrList();
            if (pinyinResult == null) {
                return lyricsArray;
            }
            var pinyinIndex = 0;
            for (int i = 0; i < lyricsArray.Length; i++) {
                if (lyricsArray[i].Length == 1 && Pinyin.Pinyin.Instance.IsHanzi(lyricsArray[i])) {
                    lyricsArray[i] = pinyinResult[pinyinIndex];
                    pinyinIndex++;
                }
            }
            return lyricsArray;
        }

        public static void RomanizeNotes(Note[][] groups) {
            var ResultLyrics = Romanize(groups.Select(group => group[0].lyric));
            Enumerable.Zip(groups, ResultLyrics, ChangeLyric).Last();
        }

        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            base.SetUp(groups, project, track);
            RomanizeNotes(groups);
        }
    }
}
