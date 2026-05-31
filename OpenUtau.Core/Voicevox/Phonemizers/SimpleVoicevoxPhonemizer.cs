using System;
using System.Collections.Generic;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Voicevox;

namespace Voicevox {
    [Phonemizer("Simple Voicevox Japanese Phonemizer", "S-VOICEVOX JA", language: "JA")]
    public class SimpleVoicevoxPhonemizer : Phonemizer {

        protected VoicevoxSinger singer;

        public override void SetSinger(USinger singer) {
            this.singer = singer as VoicevoxSinger;
            if (this.singer != null) {
                VoicevoxUtils.Loaddic(this.singer);
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            List<Phoneme> phonemes = new List<Phoneme>();
            for (int i = 0; i < notes.Length; i++) {
                var currentLyric = notes[i].lyric.Normalize();
                var lyricList = currentLyric.Split(" ");
                if (lyricList.Length > 1) {
                    currentLyric = lyricList[1];
                }
                if (!VoicevoxUtils.IsSyllableVowelExtensionNote(currentLyric)) {
                    if (VoicevoxUtils.TryGetPau(currentLyric, out string pau)) {
                        phonemes.Add(new Phoneme { phoneme = pau });
                    } else if (VoicevoxUtils.phoneme_List.kanas.ContainsKey(currentLyric)) {
                        phonemes.Add(new Phoneme { phoneme = currentLyric });
                    } else if (VoicevoxUtils.dic.IsDic(currentLyric)) {
                        phonemes.Add(new Phoneme { phoneme = VoicevoxUtils.dic.Lyrictodic(currentLyric) });
                    } else {
                        throw new Exception($"Unrecognized lyric \"{currentLyric}\"");
                    }
                }
            }
            return new Result { phonemes = phonemes.ToArray() };
        }
    }
}
