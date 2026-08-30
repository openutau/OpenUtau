using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    /// <summary>
    /// NEUTRINO phonemizer: converts lyrics to model phonemes and uses t.bin
    /// to place default phoneme boundaries when the singer is available.
    /// </summary>
    [Phonemizer("NEUTRINO Phonemizer", "NEUTRINO", language: "JA")]
    public class NeutrinoPhonemizer : Phonemizer {
        const double defaultConsonantMs = 60;
        const int minPhonemeTicks = 10;
        const int sampleRate = 48000;
        const int hopSize = 480;

        NeutrinoSinger neutrinoSinger;
        readonly Dictionary<int, Phoneme[]> timedPhonemes = new Dictionary<int, Phoneme[]>();

        public override void SetSinger(USinger singer) {
            neutrinoSinger = singer as NeutrinoSinger;
        }

        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            timedPhonemes.Clear();
            if (neutrinoSinger == null || notes == null || notes.Length == 0 || timeAxis == null) {
                return;
            }
            try {
                neutrinoSinger.EnsureTimingSession();
                foreach (var phrase in SplitPhrases(notes)) {
                    BuildTimedPhonemes(phrase);
                }
            } catch (Exception e) {
                timedPhonemes.Clear();
                Log.Warning(e, "Failed to run NEUTRINO timing model for phoneme panel; using estimated phoneme positions.");
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next,
            Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {

            if (timedPhonemes.TryGetValue(notes[0].position, out var timed)) {
                return new Result {
                    phonemes = timed.ToArray(),
                };
            }

            var lyric = string.IsNullOrWhiteSpace(notes[0].phoneticHint)
                ? notes[0].lyric ?? "R"
                : notes[0].phoneticHint;
            var phonemes = LyricToPhonemes(lyric);
            var positions = DistributePhonemes(phonemes, notes);
            var resultPhonemes = phonemes
                .Select((phoneme, index) => new Phoneme {
                    index = index,
                    phoneme = phoneme,
                    position = positions[index],
                })
                .ToArray();
            return new Result {
                phonemes = PostProcessPhonemePositions(resultPhonemes, notes),
            };
        }

        List<TimedPhrase> SplitPhrases(Note[][] noteGroups) {
            var phrases = new List<TimedPhrase>();
            List<Note[]> phrase = null;
            int previousEnd = int.MinValue;
            foreach (var group in noteGroups.Where(group => group.Length > 0)) {
                int start = group[0].position;
                int end = group[^1].position + group[^1].duration;
                if (phrase == null || start > previousEnd) {
                    phrase = new List<Note[]>();
                    int contextStart = previousEnd == int.MinValue ? 0 : previousEnd;
                    phrases.Add(new TimedPhrase(phrase, Math.Min(start, contextStart)));
                }
                phrase.Add(group);
                previousEnd = Math.Max(previousEnd, end);
            }
            return phrases;
        }

        void BuildTimedPhonemes(TimedPhrase phrase) {
            var noteGroups = phrase.NoteGroups;
            var scoreNotes = new List<NeutrinoScoreNoteInput>();
            var phoneRefs = new List<TimedPhoneRef>();
            var groupsByPosition = noteGroups.ToDictionary(group => group[0].position);
            var groupedPhonemes = groupsByPosition.ToDictionary(pair => pair.Key, _ => new List<Phoneme>());

            foreach (var group in noteGroups) {
                var lyric = string.IsNullOrWhiteSpace(group[0].phoneticHint)
                    ? group[0].lyric ?? "R"
                    : group[0].phoneticHint;
                var phonemes = LyricToPhonemes(lyric);
                var modelPhones = new NeutrinoScorePhoneInput[phonemes.Length];
                for (int i = 0; i < phonemes.Length; i++) {
                    int id = NeutrinoPhoneme.GetPhonemeId(phonemes[i]);
                    int sourceIndex = phoneRefs.Count;
                    modelPhones[i] = new NeutrinoScorePhoneInput(id, sourceIndex);
                    phoneRefs.Add(new TimedPhoneRef {
                        groupPosition = group[0].position,
                        index = i,
                        phoneme = phonemes[i],
                    });
                }

                for (int noteIndex = 0; noteIndex < group.Length; noteIndex++) {
                    var note = group[noteIndex];
                    scoreNotes.Add(new NeutrinoScoreNoteInput(
                        (float)MusicMath.ToneToFreq(note.tone),
                        Math.Max(0.001f, (float)(GetNoteDurationMs(note) / 1000.0)),
                        noteIndex > 0 && NeutrinoInferenceUtil.IsExtensionLyric(note.lyric),
                        noteIndex == 0
                            ? modelPhones
                            : Array.Empty<NeutrinoScorePhoneInput>()));
                }
            }

            var sequence = NeutrinoInferenceUtil.BuildScoreSequence(scoreNotes);
            int numPhones = sequence.PhonemeIds.Length;
            if (numPhones == 0) {
                return;
            }

            int phraseStartTick = noteGroups[0][0].position;
            double leadingContextSeconds = GetLeadingContextSeconds(
                phraseStartTick,
                phrase.ContextStartTick);
            var boundaries = BuildChunkedTimingBoundaries(
                sequence.PhonemeIds,
                sequence.ScorePitchesHz,
                sequence.ScoreDurations,
                sequence.PhonePositions,
                leadingContextSeconds);
            double phraseStartMs = timeAxis.TickPosToMsPos(phraseStartTick);

            for (int modelPhone = 0; modelPhone < sequence.SourcePhoneIndices.Length; modelPhone++) {
                int sourceIndex = sequence.SourcePhoneIndices[modelPhone];
                if (sourceIndex < 0) {
                    continue;
                }
                var phoneRef = phoneRefs[sourceIndex];
                double positionMs = phraseStartMs + boundaries[modelPhone] * 1000.0;
                int position = timeAxis.MsPosToTickPos(positionMs) - phoneRef.groupPosition;
                groupedPhonemes[phoneRef.groupPosition].Add(new Phoneme {
                    index = phoneRef.index,
                    phoneme = phoneRef.phoneme,
                    position = position,
                });
            }

            foreach (var pair in groupedPhonemes) {
                if (pair.Value.Count > 0) {
                    timedPhonemes[pair.Key] = PostProcessTimedPhonemePositions(
                        pair.Value.ToArray(),
                        groupsByPosition[pair.Key]);
                }
            }
        }

        protected virtual string[] LyricToPhonemes(string lyric) {
            return NeutrinoPhoneme.KanaToPhonemes(lyric);
        }

        protected virtual Phoneme[] PostProcessPhonemePositions(Phoneme[] phonemes, Note[] notes) {
            return phonemes;
        }

        protected virtual Phoneme[] PostProcessTimedPhonemePositions(Phoneme[] phonemes, Note[] notes) {
            return PostProcessPhonemePositions(phonemes, notes);
        }

        double GetNoteDurationMs(Note note) {
            double startMs = timeAxis.TickPosToMsPos(note.position);
            double endMs = timeAxis.TickPosToMsPos(note.position + note.duration);
            return Math.Max(1, endMs - startMs);
        }

        double[] BuildChunkedTimingBoundaries(
            long[] phonemeIds,
            float[] scorePitchesHz,
            float[] scoreDurations,
            long[] phonePositions,
            double leadingContextSeconds) {

            var chunks = NeutrinoInferenceUtil.BuildPhoneChunks(phonemeIds);
            double frameSeconds = (double)hopSize / sampleRate;
            return NeutrinoInferenceUtil.BuildTimingBoundaries(
                scoreDurations,
                phonePositions,
                chunks,
                frameSeconds,
                chunk => {
                    var chunkPitches = NeutrinoInferenceUtil.Slice(
                        scorePitchesHz, chunk.PhoneStart, chunk.PhoneCount);
                    var chunkDurations = NeutrinoInferenceUtil.Slice(
                        scoreDurations, chunk.PhoneStart, chunk.PhoneCount);
                    var chunkPositions = NeutrinoInferenceUtil.Slice(
                        phonePositions, chunk.PhoneStart, chunk.PhoneCount);
                    var chunkIds = NeutrinoInferenceUtil.Slice(
                        phonemeIds, chunk.PhoneStart, chunk.PhoneCount);
                    var timingInputs = new List<NamedOnnxValue> {
                        NamedOnnxValue.CreateFromTensor("electron",
                            new DenseTensor<long>(chunkIds, new[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor("muon",
                            new DenseTensor<float>(chunkPitches, new[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor("tau",
                            new DenseTensor<float>(chunkDurations, new[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor("selectron",
                            new DenseTensor<long>(chunkPositions, new[] { 1, chunk.PhoneCount })),
                    };
                    return NeutrinoInferenceUtil.RequireTimingBoundaryLength(
                        neutrinoSinger.RunTiming(timingInputs),
                        chunk.PhoneCount,
                        "NEUTRINO v3 t.bin timing output");
                },
                leadingContextSeconds);
        }

        double GetLeadingContextSeconds(int phraseStartTick, int contextStartTick) {
            contextStartTick = Math.Max(
                phraseStartTick - NeutrinoRenderer.headTicks,
                Math.Min(phraseStartTick, contextStartTick));
            double phraseStartMs = timeAxis.TickPosToMsPos(phraseStartTick);
            double contextStartMs = timeAxis.TickPosToMsPos(contextStartTick);
            return Math.Max(0, (phraseStartMs - contextStartMs) / 1000.0);
        }

        readonly struct TimedPhrase {
            public List<Note[]> NoteGroups { get; }
            public int ContextStartTick { get; }

            public TimedPhrase(List<Note[]> noteGroups, int contextStartTick) {
                NoteGroups = noteGroups;
                ContextStartTick = contextStartTick;
            }
        }

        struct TimedPhoneRef {
            public int groupPosition;
            public int index;
            public string phoneme;
        }

        int[] DistributePhonemes(string[] phonemes, Note[] notes) {
            if (phonemes.Length == 0) {
                return Array.Empty<int>();
            }
            if (phonemes.Length == 1) {
                return new[] { 0 };
            }

            int noteStart = notes[0].position;
            int noteEnd = notes.Last().position + notes.Last().duration;
            int totalDuration = Math.Max(minPhonemeTicks, noteEnd - noteStart);
            int lastStart = Math.Max(0, totalDuration - minPhonemeTicks);
            int consonantTicks = Math.Clamp(DefaultConsonantTicks(noteStart), minPhonemeTicks, lastStart);
            int firstVowel = Array.FindIndex(phonemes, NeutrinoPhoneme.IsVowelPhoneme);

            var positions = new int[phonemes.Length];
            if (firstVowel > 0) {
                for (int i = 0; i < phonemes.Length; i++) {
                    if (i <= firstVowel) {
                        positions[i] = (int)Math.Round((double)consonantTicks * i / firstVowel);
                    } else {
                        int restCount = phonemes.Length - firstVowel;
                        positions[i] = consonantTicks
                            + (int)Math.Round((double)(lastStart - consonantTicks) * (i - firstVowel) / restCount);
                    }
                }
            } else {
                for (int i = 0; i < phonemes.Length; i++) {
                    positions[i] = (int)Math.Round((double)lastStart * i / (phonemes.Length - 1));
                }
            }

            positions[0] = 0;
            for (int i = 1; i < positions.Length; i++) {
                int maxPosition = Math.Max(positions[i - 1] + minPhonemeTicks,
                    totalDuration - minPhonemeTicks * (positions.Length - i));
                positions[i] = Math.Min(Math.Max(positions[i], positions[i - 1] + minPhonemeTicks), maxPosition);
            }
            return positions;
        }

        int DefaultConsonantTicks(int notePosition) {
            if (timeAxis == null) {
                return 60;
            }
            double noteMs = timeAxis.TickPosToMsPos(notePosition);
            return Math.Max(minPhonemeTicks,
                timeAxis.TicksBetweenMsPos(noteMs, noteMs + defaultConsonantMs));
        }
    }
}
