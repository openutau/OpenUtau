using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Voicevox {
    [Phonemizer("Voicevox Japanese Phonemizer", "VOICEVOX JA", language: "JA")]
    public class VoicevoxPhonemizer : Phonemizer {

        protected VoicevoxSinger singer;
        Dictionary<Note[], Phoneme[]> partResult = new Dictionary<Note[], Phoneme[]>();

        public override void SetSinger(USinger singer) {
            this.singer = singer as VoicevoxSinger;
        }

        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            partResult.Clear();
            VoicevoxNote[] vNotes = new VoicevoxNote[notes.Length];
            for (int i = 0; i < notes.Length; i++) {
                var currentLyric = notes[i][0].lyric.Normalize();
                var lyricList = currentLyric.Split(" ");
                if (lyricList.Length > 1) {
                    currentLyric = lyricList[1];
                }
                if (!VoicevoxUtils.IsSyllableVowelExtensionNote(currentLyric)) {
                    if (VoicevoxUtils.IsDicPau(currentLyric)) {
                        currentLyric = string.Empty;
                    } else if (VoicevoxUtils.dic.IsDic(currentLyric)) {
                        currentLyric = VoicevoxUtils.dic.Lyrictodic(currentLyric);
                    } else if (!VoicevoxUtils.IsDicKana(currentLyric)) {
                        currentLyric = string.Empty;
                    }
                }
                vNotes[i] = new VoicevoxNote() {
                    lyric = currentLyric,
                    positionMs = timeAxis.TickPosToMsPos(notes[i][0].position),
                    durationMs = timeAxis.TickPosToMsPos(notes[i][0].duration),
                    tone = (int)(notes[i][0].tone + (notes[i][0].phonemeAttributes.Length > 0 ? notes[i][0].phonemeAttributes[0].toneShift : 0))
                };
            }
            VoicevoxQueryMain vqMain = VoicevoxUtils.NoteGroupsToVQuery(vNotes, timeAxis);
            VoicevoxSynthParams vsParams = new VoicevoxSynthParams();
            string singerID = VoicevoxUtils.defaultID;
            if (this.singer.voicevoxConfig.base_singer_style != null) {
                foreach (var s in this.singer.voicevoxConfig.base_singer_style) {
                    if (s.name.Equals(this.singer.voicevoxConfig.base_singer_name)) {
                        vsParams = VoicevoxUtils.VoicevoxVoiceBase(vqMain, s.styles.id.ToString());
                        if (s.styles.name.Equals(this.singer.voicevoxConfig.base_singer_style_name)) {
                            break;
                        }
                    } else {
                        vsParams = VoicevoxUtils.VoicevoxVoiceBase(vqMain, singerID);
                        break;
                    }
                }
            } else {
                vsParams = VoicevoxUtils.VoicevoxVoiceBase(vqMain, singerID);
            }

            List<Phonemes> list = vsParams.phonemes;
            foreach (var note in vqMain.notes) {
                if (note.vqnindex < 0) {
                    list.Remove(list[0]);
                    continue;
                }
                var noteGroup = notes[note.vqnindex];
                var phoneme = new List<Phoneme>();
                int index = 0;
                while (list.Count > 0) {
                    if (VoicevoxUtils.phoneme_List.vowels.Contains(list[0].phoneme)) {
                        phoneme.Add(new Phoneme() { phoneme = list[0].phoneme, position = noteGroup[0].position });
                        index++;
                        list.Remove(list[0]);
                        break;
                    } else if (VoicevoxUtils.phoneme_List.consonants.Contains(list[0].phoneme)) {
                        phoneme.Add(new Phoneme() { phoneme = list[0].phoneme, position = noteGroup[0].position - (int)timeAxis.MsPosToTickPos((list[0].frame_length / VoicevoxUtils.fps) * 1000) });
                    }
                    list.Remove(list[0]);
                }
                partResult[noteGroup] = phoneme.ToArray();
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            var ps = new List<Phoneme>();
            if (partResult.TryGetValue(notes, out var phonemes)) {
                return new Result {
                    phonemes = phonemes.Select(p => {
                        p.position = p.position - notes[0].position;
                        return p;
                    }).ToArray(),
                };
            }
            if (SetUpException != null) {
                throw new Exception("Phonemizer failed to process.", SetUpException);
            }
            throw new Exception("Part result not found");

        }

        public override void CleanUp() {
            partResult.Clear();
        }
    }
}
