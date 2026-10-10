using System;
using System.Collections.Generic;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("German CV", "German CV", "LilyMakesStuff", language: "DE")]
    public class GermanCVPhonemizer : Phonemizer {

        static readonly (string input, string output)[] vowels = new[] {
            ("äu", "eu"), ("ie", "i"), ("ei", "ai"), ("ae", "ae"),
            ("oe", "oe"), ("ue", "ue"), ("ä", "ae"), ("ö", "oe"),
            ("ü", "ue"), ("ai", "ai"), ("au", "au"), ("eu", "eu"),
            ("a", "a"), ("e", "e"), ("i", "i"), ("o", "o"), ("u", "u"),
        };

        static readonly (string input, string output)[] consonants = new[] {
            ("tsch", "sh"), ("sch", "sh"), ("ach", "ach"), ("pf", "pf"),
            ("qu", "kv"), ("ng", "ng"), ("tz", "ts"), ("sp", "sp"),
            ("st", "st"), ("ck", "k"), ("ch", "ch"), ("ss", "s"),
            ("b", "b"), ("d", "d"), ("f", "f"), ("g", "g"), ("h", "h"),
            ("j", "j"), ("k", "k"), ("l", "l"), ("m", "m"), ("n", "n"),
            ("p", "p"), ("r", "r"), ("s", "s"), ("t", "t"), ("v", "v"),
            ("w", "v"), ("x", "s"), ("y", "i"), ("z", "ts"), ("ß", "s"),
        };

        static readonly HashSet<string> endingConsonants = new HashSet<string> {
            "b", "d", "f", "g", "k", "l", "m", "n", "ng",
            "p", "r", "s", "sh", "t", "ts", "v", "ch", "ach"
        };

        USinger singer;
        public override void SetSinger(USinger singer) => this.singer = singer;

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            var note = notes[0];
            var lyric = note.lyric.ToLower().Trim();
            var phonemeList = G2P(lyric);
            if (phonemeList.Count == 0) return MakeSimpleResult(lyric);
            if (phonemeList.Count == 1) return MakeSimpleResult(phonemeList[0]);
            var phonemes = new Phoneme[phonemeList.Count];
            phonemes[0] = new Phoneme { phoneme = phonemeList[0] };
            for (int i = 1; i < phonemeList.Count; i++) {
                phonemes[i] = new Phoneme { phoneme = phonemeList[i], position = note.duration * i / phonemeList.Count };
            }
            return new Result { phonemes = phonemes };
        }

        List<string> G2P(string word) {
            var result = new List<string>();
            int i = 0;
            while (i < word.Length) {
                string cons = null, consOut = null;
                foreach (var (input, output) in consonants) {
                    if (word.Substring(i).StartsWith(input)) { cons = input; consOut = output; break; }
                }
                if (cons != null) {
                    int j = i + cons.Length;
                    string vow = null, vowOut = null;
                    foreach (var (input, output) in vowels) {
                        if (j < word.Length && word.Substring(j).StartsWith(input)) { vow = input; vowOut = output; break; }
                    }
                    if (vow != null) { result.Add(consOut + vowOut); i = j + vow.Length; }
                    else { if (endingConsonants.Contains(consOut)) result.Add(consOut); i += cons.Length; }
                } else {
                    bool found = false;
                    foreach (var (input, output) in vowels) {
                        if (word.Substring(i).StartsWith(input)) { result.Add(output); i += input.Length; found = true; break; }
                    }
                    if (!found) i++;
                }
            }
            return result;
        }
    }
}
