using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Voicevox {
    [Phonemizer("Voicevox Japanese Phonemizer", "VOICEVOX JA", language: "JA")]
    public class VoicevoxPhonemizer : MachineLearningPhonemizer {

        protected VoicevoxSinger? singer;
        VoicevoxConfig? config;

        public override void SetSinger(USinger singer) {
            this.singer = singer as VoicevoxSinger;
        }

        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            if (singer == null) {
                throw new VoicevoxException("Singer is not a VOICEVOX singer.");
            }
            if (singer.voicevoxConfig == null) {
                // The engine may have been started after OpenUtau. VoicevoxClient's cooldown keeps this cheap while it is down.
                singer.Reload();
            }
            config = singer.voicevoxConfig
                ?? throw new VoicevoxException("Could not connect to the VOICEVOX engine. Please start the engine.");
            // Splits the part into phrases at rests and calls ProcessPart for each,
            // the same way the renderer queries the engine per phrase.
            base.SetUp(groups, project, track);
        }

        protected override void ProcessPart(Note[][] phrase) {
            var config = this.config ?? throw new VoicevoxException("VOICEVOX singer is not loaded.");
            VoicevoxNote[] vNotes = new VoicevoxNote[phrase.Length];
            for (int i = 0; i < phrase.Length; i++) {
                var currentLyric = phrase[i][0].lyric.Normalize();
                var lyricList = currentLyric.Split(" ");
                if (!VoicevoxUtils.IsSyllableVowelExtensionNote(lyricList[^1])) {
                    if (VoicevoxUtils.IsPau(lyricList[^1])) {
                        currentLyric = string.Empty;
                    } else if (VoicevoxUtils.dic.IsDic(lyricList[^1])) {
                        currentLyric = VoicevoxUtils.dic.Lyrictodic(lyricList[^1]);
                    } else if (VoicevoxUtils.IsKana(lyricList[^1])) {
                        currentLyric = lyricList[^1];
                    } else {
                        currentLyric = string.Empty;
                    }
                }
                vNotes[i] = new VoicevoxNote() {
                    lyric = currentLyric,
                    positionMs = timeAxis.TickPosToMsPos(phrase[i][0].position),
                    durationMs = timeAxis.TickPosToMsPos(phrase[i][0].position + phrase[i][0].duration) - timeAxis.TickPosToMsPos(phrase[i][0].position),
                    tone = phrase[i][0].tone + (phrase[i][0].phonemeAttributes.FirstOrDefault().toneShift ?? 0)
                };
            }
            VoicevoxQueryMain vqMain = VoicevoxUtils.NoteGroupsToVQuery(vNotes, timeAxis);
            VoicevoxSynthParams vsParams = new VoicevoxSynthParams();
            string singerID = VoicevoxUtils.defaultID;
            if (config.base_singer_style != null) {
                foreach (var s in config.base_singer_style) {
                    if (s.name.Equals(config.base_singer_name)) {
                        vsParams = VoicevoxUtils.VoicevoxVoiceBase(vqMain, s.styles.id.ToString());
                        if (s.styles.name.Equals(config.base_singer_style_name)) {
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
                    if (list.Count > 0) {
                        list.RemoveAt(0);
                    }
                    continue;
                }
                var noteGroup = phrase[note.vqnindex];
                int notePos = noteGroup[0].position;
                // Positions are relative to the start of the note.
                var phonemes = new List<Tuple<string, int>>();
                while (list.Count > 0) {
                    var current = list[0];
                    list.RemoveAt(0);
                    if (VoicevoxUtils.phoneme_List.vowels.Contains(current.phoneme)) {
                        phonemes.Add(Tuple.Create(current.phoneme, 0));
                        break;
                    } else if (VoicevoxUtils.phoneme_List.consonants.Contains(current.phoneme)) {
                        double consonantMs = (current.frame_length / VoicevoxUtils.fps) * 1000;
                        int consonantStart = timeAxis.MsPosToTickPos(timeAxis.TickPosToMsPos(notePos) - consonantMs);
                        phonemes.Add(Tuple.Create(current.phoneme, consonantStart - notePos));
                    }
                }
                partResult[notePos] = phonemes;
            }
        }
    }
}
