using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core {
    public abstract class MachineLearningPhonemizer : Phonemizer
    {
        //The results of the timing model are stored in partResult
        //key: tick position of each note.
        //value: a list of phonemes and their positions in the note
        protected Dictionary<int, List<Tuple<string, int>>> partResult = new Dictionary<int, List<Tuple<string, int>>>();
        protected Dictionary<int, string> unrecognizedLyrics = new Dictionary<int, string>();
        private Exception? lastProcessPartException;
        //Called when the note is changed, and the entire song is passed into the SetUp function as long as the note is changed
        //groups is a two-dimensional array of Note, each Note[] represents a lyrical note and its following slur notes
        //Run phoneme timing model in sections to prevent butterfly effect
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            SetUpException = null;
            lastProcessPartException = null;
            partResult.Clear();
            unrecognizedLyrics.Clear();
            if (groups.Length == 0) {
                return;
            }
            //Lyrics romanization (hanzi to pinyin)
            var RomanizedLyrics = Romanize(groups.Select(group => group[0].lyric));
            Enumerable.Zip(groups, RomanizedLyrics, ChangeLyric).Last();
            //Split song into sentences (phrases)
            var phrase = new List<Note[]> { groups[0] };
            for (int i = 1; i < groups.Length; ++i) {
                //If the previous and current notes are connected, do not split the sentence
                if (groups[i - 1][^1].position + groups[i - 1][^1].duration == groups[i][0].position) {
                    phrase.Add(groups[i]);
                } else {
                    //If the previous and current notes are not connected, process the current sentence and start the next sentence
                    try{
                        ProcessPart(phrase.ToArray());
                    }catch(Exception e){
                        lastProcessPartException = e;
                        var sentenceLyrics = string.Join(" ", phrase.Select(group => group[0].lyric));
                        Log.Error($"Failed to phonemize sentence {sentenceLyrics}: {e.Message}");
                    }
                    phrase.Clear();
                    phrase.Add(groups[i]);
                }
            }
            if (phrase.Count > 0) {
                try{
                    ProcessPart(phrase.ToArray());
                }catch(Exception e){
                    lastProcessPartException = e;
                    var sentenceLyrics = string.Join(" ", phrase.Select(group => group[0].lyric));
                    Log.Error($"Failed to phonemize sentence {sentenceLyrics}: {e.Message}");
                }
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (unrecognizedLyrics.TryGetValue(notes[0].position, out var lyric)) {
                if (string.IsNullOrEmpty(lyric)) {
                    throw new Exception("Phoneme not found for this note");
                }
                throw new Exception($"Unrecognized phoneme \"{lyric}\"");
            }
            if (!partResult.TryGetValue(notes[0].position, out var phonemes)) {
                var cause = lastProcessPartException ?? SetUpException;
                if (cause != null) {
                    throw new Exception("Phonemizer failed to process.", cause);
                }
                throw new Exception("Part result not found");
            }
            return new Result {
                phonemes = phonemes
                    .Select((tu) => new Phoneme() {
                        phoneme = tu.Item1,
                        position = tu.Item2,
                    })
                    .ToArray(),
            };
        }

        public override void CleanUp() {
            partResult.Clear();
            unrecognizedLyrics.Clear();
        }

        //Run timing model for a sentence, and put the results into partResult
        protected abstract void ProcessPart(Note[][] phrase);

        //Romanize lyrics for Mandarin and Yue Chinese
        protected virtual string[] Romanize(IEnumerable<string> lyrics){
            return lyrics.ToArray();
        }

        protected static Note[] ChangeLyric(Note[] group, string lyric) {
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
    }
}
