using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Enunu {
    [Phonemizer("Enunu English Phonemizer", "ENUNU EN", "O3", language:"EN")]
    public class EnunuEnglishPhonemizer : EnunuPhonemizer {
        readonly string PhonemizerType = "ENUNU EN";

        protected IG2p g2p;
        //index,position,is_start
        protected readonly List<Tuple<int, int, bool>> alignments = new List<Tuple<int, int, bool>>();

        protected IG2p LoadG2p() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            
            string path = Path.Combine(PluginDir, "arpasing.yaml");
            /*
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.arpasing_template);
            }*/
            if (File.Exists(path)) {
                try {
                    g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load {path}");
                }
            }

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "arpasing.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }

            // Load base g2p.
            g2ps.Add(new ArpabetG2p());

            return new G2pFallbacks(g2ps.ToArray());
        }


        public override void SetSinger(USinger singer) {
            this.singer = singer as EnunuSinger;
            g2p = LoadG2p();
        }

        string[] GetSymbols(Note note) {
            if (string.IsNullOrEmpty(note.phoneticHint)) {
                // User has not provided hint, query CMUdict.
                return g2p.Query(note.lyric.ToLowerInvariant());
            }
            // Split space-separated symbols into an array.
            return note.phoneticHint.Split()
                .Where(s => g2p.IsValidSymbol(s)) // skip the invalid symbols.
                .ToArray();
        }

        protected EnunuNote[] MakeSimpleResult(string lyric,int length, int noteNum) {
            return new EnunuNote[]{new EnunuNote {
                    lyric = lyric,
                    length = length,
                    noteNum = noteNum,
                    noteIndex = -1,
                } };
        }

        protected EnunuNote[] ProcessWord(Note[] notes, int noteIndex) {
            var note = notes[0];
            var totalLength = notes.Sum(n => n.duration);
            if (!string.IsNullOrEmpty(note.lyric) && note.lyric[0] == '?') {
                return MakeSimpleResult(note.lyric.Substring(1), totalLength, note.tone);
            }
            // Get the symbols of current note.
            var symbols = GetSymbols(note);
            if (symbols == null || symbols.Length == 0) {
                // No symbol is found for current note.
                return MakeSimpleResult(note.lyric, totalLength, note.tone);
            }
            // Find phone types of symbols.
            var isVowel = symbols.Select(s => g2p.IsVowel(s)).ToArray();
            // Arpasing aligns the first vowel at 0 and shifts leading consonants to negative positions,
            // so we need to find the first vowel.

            // Alignments
            // - Tries to align every note to one syllable.
            // - "+n" manually aligns to n-th phoneme.
            alignments.Clear();
            //notes except those whose lyrics start witn "+*" or "+~"
            var nonExtensionNotes = notes.Where(n=>!IsSyllableVowelExtensionNote(n)).ToArray();
            for (int i = 0; i < symbols.Length; i++) {
                if (isVowel[i] && alignments.Count < nonExtensionNotes.Length) {
                    alignments.Add(Tuple.Create(i, nonExtensionNotes[alignments.Count].position - notes[0].position, false));
                }
            }
            int position = notes[0].duration;
            for (int i = 1; i < notes.Length; ++i) {
                if (int.TryParse(notes[i].lyric.Substring(1), out var idx)) {
                    alignments.Add(Tuple.Create(idx - 1, position, true));
                }
                position += notes[i].duration;
            }
            alignments.Add(Tuple.Create(symbols.Length, position, true));
            alignments.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            for (int i = 0; i < alignments.Count; ++i) {
                if (alignments[i].Item3) {
                    while (i > 0 && (alignments[i - 1].Item2 >= alignments[i].Item2 ||
                        alignments[i - 1].Item1 == alignments[i].Item1)) {
                        alignments.RemoveAt(i - 1);
                        i--;
                    }
                    while (i < alignments.Count - 1 && (alignments[i + 1].Item2 <= alignments[i].Item2 ||
                        alignments[i + 1].Item1 == alignments[i].Item1)) {
                        alignments.RemoveAt(i + 1);
                    }
                }
            }
            alignments.RemoveAt(0);

            var enunuNotes = new List<EnunuNote>();
            int startIndex = 0;
            int endIndex = 0;
            int firstVowel = Array.IndexOf(isVowel, true);
            int startTick = 0;

            foreach (var alignment in alignments) {
                // Distributes phonemes between two aligment points.
                EnunuNote enunuNote= new EnunuNote {
                    lyric = "",
                    length = alignment.Item2-startTick,
                    noteNum = note.tone,
                    noteIndex = noteIndex,

                };
                endIndex= alignment.Item1;

                for(int index = startIndex; index < endIndex; index++) {
                    enunuNote.lyric += symbols[index] + " ";
                }
                enunuNotes.Add(enunuNote);
                startIndex = endIndex;
                
                startTick = alignment.Item2;
            }
            alignments.Clear();
            return enunuNotes.ToArray();
        }

        /// <summary>
        /// Does this note extend the previous syllable?
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        protected override EnunuNote[] NoteGroupsToEnunu(Note[][] notes) {
            var result = new List<EnunuNote>();
            int position = 0;
            int index = 0;
            while (index < notes.Length) {
                if (position < notes[index][0].position) {
                    result.Add(new EnunuNote {
                        lyric = "R",
                        length = notes[index][0].position - position,
                        noteNum = 60,
                        noteIndex = -1,
                    });
                    position = notes[index][0].position;
                } else {
                    /*var lyric = notes[index][0].lyric;
                    if (lyric.Length > 0 && PinyinHelper.IsChinese(lyric[0])) {
                        lyric = PinyinHelper.GetPinyin(lyric).ToLowerInvariant();
                    }*/
                    var wordEnunuNotes = ProcessWord(notes[index],index);
                    result.AddRange(wordEnunuNotes);
                    foreach(var enunuNote in wordEnunuNotes) {
                        position += enunuNote.length;
                    }
                    index++;
                }
            }
            return result.ToArray();
        }
    }
}
