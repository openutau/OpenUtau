using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin
{
    public abstract class PhonemeBasedPhonemizer : Phonemizer
    {
        protected Dictionary<string, string[]> vowelFallback;
        protected USinger singer;
        protected IG2p g2p;
        protected bool isDictionaryLoading;

        //[(index of phoneme, tick position from the lyrical note in notes[], is manual)]
        protected readonly List<Tuple<int, int, bool>> alignments = new List<Tuple<int, int, bool>>();

        /// <summary>
        /// This property will later be exposed in UI for user adjustment.
        /// </summary>
        public int ConsonantLength { get; set; } = 60;
        
        public bool addTail { get; set; } = true;

        public PhonemeBasedPhonemizer() {
            try {
                Initialize();
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize.");
            }
        }

        protected abstract IG2p LoadG2p();

        protected abstract Dictionary<string, string[]> LoadVowelFallbacks();

        protected void Initialize() {
            g2p = LoadG2p();
            vowelFallback = LoadVowelFallbacks();
        }

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            g2p = LoadG2p();
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            if (isDictionaryLoading) {
                return MakeSimpleResult("");
            }
            var note = notes[0];

            // Force alias using ? prefix
            if (!string.IsNullOrEmpty(note.lyric) && note.lyric[0] == '?') {
                return MakeSimpleResult(note.lyric.Substring(1));
            }

            // Get the symbols of previous note.
            var prevSymbols = prevNeighbour == null ? null : GetSymbols(prevNeighbour.Value);
            // The user is using a tail "-" note to produce a "<something> -" sound.
            if (note.lyric == "-" && prevSymbols != null) {
                var attr = note.phonemeAttributes?.FirstOrDefault() ?? default;
                string color = attr.voiceColor ?? GetParentVoiceColor();
                string alias = $"{prevSymbols.Last()} -";
                if (singer.TryGetMappedOto(alias, note.tone, color, out var oto)) {
                    return MakeSimpleResult(oto.Alias);
                }
                return MakeSimpleResult(alias);
            }
            // Get the symbols of current note.
            string[] symbols = GetSymbols(note);
            if (addTail && nextNeighbour == null) {
                // Auto add tail "-".
                symbols = symbols.Append("-").ToArray();
            }
            if (symbols == null || symbols.Length == 0) {
                // No symbol is found for current note.
                // Otherwise assumes the user put in an alias.
                return MakeSimpleResult(note.lyric);
            }
            // Find phone types of symbols.
            var isVowel = symbols.Select(g2p.IsVowel).ToArray();
            var isGlide = symbols.Select(g2p.IsGlide).ToArray();
            // Arpasing aligns the first vowel at 0 and shifts leading consonants to negative positions,
            // so we need to find the first vowel.
            var phonemes = new Phoneme[symbols.Length];

            // Alignments
            // - Tries to align every note to one syllable.
            // - "+n" manually aligns to n-th phoneme.
            alignments.Clear();
            //notes except those whose lyrics start witn "+*" or "+~"
            var nonExtensionNotes = notes.Where(n=>!IsSyllableVowelExtensionNote(n)).ToArray();
            for (int i = 0; i < symbols.Length; i++) {
                if (isVowel[i] && alignments.Count < nonExtensionNotes.Length) {
                    //Handle glide phonemes
                    //For "Consonant-Glide-Vowel" syllable, the glide phoneme is placed after the start position of the note.
                    if(i>=2 && isGlide[i-1] && !isVowel[i-2]){
                        alignments.Add(Tuple.Create(i-1, nonExtensionNotes[alignments.Count].position - notes[0].position, false));
                    } else{
                        alignments.Add(Tuple.Create(i, nonExtensionNotes[alignments.Count].position - notes[0].position, false));
                    }
                }
            }
            int position = notes[0].duration;
            for (int i = 1; i < notes.Length; ++i) {
                if (int.TryParse(notes[i].lyric.Substring(1), out var idx)) {
                    alignments.Add(Tuple.Create(idx - 1, position, true));
                }
                position += notes[i].duration;
            }
            alignments.Add(Tuple.Create(phonemes.Length, position, true));
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

            int startIndex = 0;
            int firstVowel = Array.IndexOf(isVowel, true);
            int startTick = -ConsonantLength * firstVowel;
            foreach (var alignment in alignments) {
                // Distributes phonemes between two aligment points.
                DistributeDuration(isVowel, phonemes, startIndex, alignment.Item1, startTick, alignment.Item2);
                startIndex = alignment.Item1;
                startTick = alignment.Item2;
            }
            alignments.Clear();

            // Select aliases.
            int noteIndex = 0;
            string prevSymbol = prevSymbols == null ? "-" : prevSymbols.Last();
            for (int i = 0; i < symbols.Length; i++) {
                var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == i) ?? default;
                string alt = (attr.alternate ?? GetParentAlternate())?.ToString() ?? string.Empty;
                string color = attr.voiceColor ?? GetParentVoiceColor();
                int toneShift = attr.toneShift ?? GetParentToneShift();
                var phoneme = phonemes[i];
                while (noteIndex < notes.Length - 1 && notes[noteIndex].position - note.position < phoneme.position) {
                    noteIndex++;
                }
                int tone = (i == 0 && prevNeighbours != null && prevNeighbours.Length > 0)
                    ? prevNeighbours.Last().tone : notes[noteIndex].tone;
                phoneme.phoneme = GetPhonemeOrFallback(prevSymbol, symbols[i], tone + toneShift, color, alt);
                phonemes[i] = phoneme;
                prevSymbol = symbols[i];
            }

            return new Result {
                phonemes = phonemes,
            };
        }

        /// <summary>
        /// Does this note extend the previous syllable?
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
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

        protected abstract string GetPhonemeOrFallback(string prevSymbol, string symbol, int tone, string color, string alt);

        void DistributeDuration(bool[] isVowel, Phoneme[] phonemes, int startIndex, int endIndex, int startTick, int endTick) {
            if (startIndex == endIndex) {
                return;
            }
            // First count number of vowels and consonants.
            int consonants = 0;
            int vowels = 0;
            int duration = endTick - startTick;
            for (int i = startIndex; i < endIndex; i++) {
                if (isVowel[i]) {
                    vowels++;
                } else {
                    consonants++;
                }
            }
            // If vowels exist, consonants are given fixed length, but combined no more than half duration.
            // However, if no vowel exists, consonants are evenly distributed within the total duration.
            int consonantDuration = vowels > 0
                ? (consonants > 0 ? Math.Min(ConsonantLength, duration / 2 / consonants) : 0)
                : duration / consonants;
            // Vowels are evenly distributed within (total duration - total consonant duration).
            int vowelDuration = vowels > 0 ? (duration - consonantDuration * consonants) / vowels : 0;
            int position = startTick;
            // Compute positions using previously computed durations.
            for (int i = startIndex; i < endIndex; i++) {
                if (isVowel[i]) {
                    phonemes[i].position = position;
                    position += vowelDuration;
                } else {
                    phonemes[i].position = position;
                    position += consonantDuration;
                }
            }
        }
    }
}
