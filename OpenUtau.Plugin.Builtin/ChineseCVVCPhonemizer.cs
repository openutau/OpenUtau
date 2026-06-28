using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Chinese CVVC Phonemizer", "ZH CVVC", language: "ZH")]
    public class ChineseCVVCPhonemizer : Phonemizer {
        private Dictionary<string, string> vowels = new Dictionary<string, string>();
        private Dictionary<string, string> consonants = new Dictionary<string, string>();
        private Dictionary<string, string> replace = new Dictionary<string, string>();
        private USinger singer;
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var lyric = notes[0].lyric;
            foreach (var pair in replace) { // replace (exact match)
                if (pair.Key == lyric) {
                    lyric = pair.Value;
                }
            }
            string consonant = consonants.TryGetValue(lyric, out consonant) ? consonant : lyric;
            string prevVowel = "-";
            if (prevNeighbour != null) {
                var prevLyric = prevNeighbour.Value.lyric;
                if (vowels.TryGetValue(prevLyric, out var vowel)) {
                    prevVowel = vowel;
                }
            };
            var attr0 = notes[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            var attr1 = notes[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;
            var attr2 = notes[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 2) ?? default;
            if (lyric == "-" || lyric.ToLowerInvariant() == "r") {
                if (singer.TryGetMappedOto($"{prevVowel} R", notes[0].tone + attr0.toneShift, attr0.voiceColor, out var oto1)) {
                    return MakeSimpleResult(oto1.Alias);
                }
                return MakeSimpleResult($"{prevVowel} R");
            }
            string currVowel = vowels.TryGetValue(lyric, out currVowel) ? currVowel : lyric;
            int totalDuration = notes.Sum(n => n.duration); // totalDuration of current note

            if (singer.TryGetMappedOto($"{prevVowel} {lyric}", notes[0].tone + attr0.toneShift, attr0.voiceColor, out var oto)) {
                if (nextNeighbour == null && singer.TryGetMappedOto($"{currVowel} R", notes[0].tone + attr1.toneShift, attr1.voiceColor, out var oto1)) {
                    // automatically add ending if present
                    return new Result {
                        phonemes = new Phoneme[] {
                                new Phoneme() {
                                    phoneme = oto.Alias,
                                },
                                new Phoneme() {
                                    phoneme = oto1.Alias,
                                    position = totalDuration - Math.Min(totalDuration / 6, 60),
                                },
                            },
                    };
                }
                return MakeSimpleResult(oto.Alias);
            }
            int vcLen = 120;
            int endTick = notes[^1].position + notes[^1].duration;
            if (singer.TryGetMappedOto(lyric, notes[0].tone + attr1.toneShift, attr1.voiceColor, out var cvOto)) {
                 vcLen = -timeAxis.MsToTickAt(-cvOto.Preutter, endTick);
                if (cvOto.Overlap == 0 && vcLen < 120) {
                    vcLen = Math.Min(120, vcLen * 2); // explosive consonant with short preutter.
                }
                if (cvOto.Overlap < 0) {
                     vcLen = -timeAxis.MsToTickAt(-(cvOto.Preutter - cvOto.Overlap), endTick);
                }
            }

            if (singer.TryGetMappedOto(lyric, notes[0].tone + attr0.toneShift, attr0.voiceColor, out var cvOtoSimple)) {
                lyric = cvOtoSimple.Alias;
            }

            var vcPhoneme = $"{prevVowel} {consonant}";
            if (prevNeighbour != null) {
                if (singer.TryGetMappedOto(vcPhoneme, prevNeighbour.Value.tone + attr0.toneShift, attr0.voiceColor, out oto)) {
                    vcPhoneme = oto.Alias;
                }
                // prevDuration calculated on basis of previous note length
                int prevDuration = notes[0].position - prevNeighbour.Value.position;
                // vcLength depends on the Vel of the current base note
                vcLen = Convert.ToInt32(Math.Min(prevDuration / 1.5, Math.Max(30, vcLen * (attr1.consonantStretchRatio ?? 1))));
            } else {
                if (singer.TryGetMappedOto(vcPhoneme, notes[0].tone + attr0.toneShift, attr0.voiceColor, out oto)) {
                    vcPhoneme = oto.Alias;
                }
                // no previous note, so length can be minimum velocity regardless of oto
                vcLen = Convert.ToInt32(Math.Min(vcLen * 2, Math.Max(30, vcLen * (attr1.consonantStretchRatio ?? 1))));
            }

            if (nextNeighbour == null) { // automatically add ending if present
                if (singer.TryGetMappedOto($"{prevVowel} {lyric}", notes[0].tone + attr0.toneShift, attr0.voiceColor, out var oto0)) {
                    if (singer.TryGetMappedOto($"{currVowel} R", notes[0].tone + attr1.toneShift, attr1.voiceColor, out var otoEnd)) {
                        // automatically add ending if present
                        return new Result {
                            phonemes = new Phoneme[] {
                                new Phoneme() {
                                    phoneme = oto0.Alias,
                                },
                                new Phoneme() {
                                    phoneme = otoEnd.Alias,
                                    position = totalDuration - Math.Min(totalDuration / 6, 60),
                                },
                            },
                        };
                    }
                } else {
                    // use vc if present
                    if (prevNeighbour == null && singer.TryGetMappedOto(vcPhoneme, notes[0].tone + attr0.toneShift, attr0.voiceColor, out var vcOto1)) {
                        vcPhoneme = vcOto1.Alias;
                        // automatically add ending if present
                        if (singer.TryGetMappedOto($"{currVowel} R", notes[0].tone + attr2.toneShift, attr2.voiceColor, out var otoEnd)) {
                            return new Result {
                                phonemes = new Phoneme[] {
                                    new Phoneme() {
                                        phoneme = vcPhoneme,
                                        position = -vcLen,
                                },
                                    new Phoneme() {
                                        phoneme = cvOto?.Alias ?? lyric,
                                },
                                    new Phoneme() {
                                        phoneme = otoEnd.Alias,
                                        position = totalDuration - Math.Min(totalDuration / 6, 60),
                                },
                            },
                            };
                        }
                    } else if (prevNeighbour != null && singer.TryGetMappedOto(vcPhoneme, prevNeighbour.Value.tone + attr0.toneShift, attr0.voiceColor, out var vcOto2)) {
                        vcPhoneme = vcOto2.Alias;
                        // automatically add ending if present
                        if (singer.TryGetMappedOto($"{currVowel} R", notes[0].tone + attr2.toneShift, attr2.voiceColor, out var otoEnd)) {
                            return new Result {
                                phonemes = new Phoneme[] {
                                    new Phoneme() {
                                        phoneme = vcPhoneme,
                                        position = -vcLen,
                                },
                                    new Phoneme() {
                                        phoneme = cvOto?.Alias ?? lyric,
                                },
                                    new Phoneme() {
                                        phoneme = otoEnd.Alias,
                                        position = totalDuration - Math.Min(totalDuration / 6, 60),
                                },
                            },
                            };
                        }
                    } // just base note and ending
                    if (singer.TryGetMappedOto($"{currVowel} R", notes[0].tone + attr1.toneShift, attr1.voiceColor, out var otoEnd1)) {
                        return new Result {
                            phonemes = new Phoneme[] {
                                new Phoneme() {
                                    phoneme = cvOtoSimple?.Alias ?? lyric,
                                },
                                new Phoneme() {
                                    phoneme = otoEnd1.Alias,
                                    position = totalDuration - Math.Min(totalDuration / 6, 60),
                                },
                            },
                        };
                    }
                }
            }

            if (singer.TryGetMappedOto(vcPhoneme, notes[0].tone + attr0.toneShift, attr0.voiceColor, out oto)) {
                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = vcPhoneme,
                            position = -vcLen,
                        },
                        new Phoneme() {
                            phoneme = cvOto?.Alias ?? lyric,
                        },
                    },
                };
            }
            return MakeSimpleResult(cvOtoSimple?.Alias ?? lyric);
        }

        public override void SetSinger(USinger singer) {
            if (this.singer == singer) {
                return;
            }
            this.singer = singer;
            vowels.Clear();
            consonants.Clear();
            replace.Clear();
            if (this.singer == null) {
                return;
            }
            try {
                string file = Path.Combine(singer.Location, "presamp.ini");
                using (var reader = new StreamReader(file, singer.TextFileEncoding)) {
                    var blocks = Ini.ReadBlocks(reader, file, @"\[\w+\]");
                    var vowelLines = blocks.Find(block => block.header == "[VOWEL]").lines;
                    foreach (var iniLine in vowelLines) {
                        var parts = iniLine.line.Split('=');
                        if (parts.Length >= 3) {
                            string vowelLower = parts[0];
                            string vowelUpper = parts[1];
                            string[] sounds = parts[2].Split(',');
                            foreach (var sound in sounds) {
                                vowels[sound] = vowelLower;
                            }
                        }
                    }
                    var consonantLines = blocks.Find(block => block.header == "[CONSONANT]").lines;
                    foreach (var iniLine in consonantLines) {
                        var parts = iniLine.line.Split('=');
                        if (parts.Length >= 3) {
                            string consonant = parts[0];
                            string[] sounds = parts[1].Split(',');
                            foreach (var sound in sounds) {
                                consonants[sound] = consonant;
                            }
                        }
                    }
                    var priority = blocks.Find(block => block.header == "PRIORITY");
                    var replaceLines = blocks.Find(block => block.header == "[REPLACE]").lines;
                    foreach (var iniLine in replaceLines) {
                        var parts = iniLine.line.Split('=');
                        replace[parts[0]]=parts[1];
                    }
                    var alias = blocks.Find(block => block.header == "ALIAS");
                }
            } catch (Exception e) {
                Log.Error(e, "failed to load presamp.ini");
            }
        }

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

        protected virtual string[] Romanize(IEnumerable<string> lyrics) {
            return BaseChinesePhonemizer.Romanize(lyrics);
        }

        protected void RomanizeNotes(Note[][] groups) {
            var ResultLyrics = Romanize(groups.Select(group => group[0].lyric));
            Enumerable.Zip(groups, ResultLyrics, ChangeLyric).Last();
        }

        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            RomanizeNotes(groups);
        }
    }
}