using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Vogen {
    public abstract class VogenBasePhonemizer : Phonemizer {
        private Dictionary<int, List<Tuple<string, int>>> partResult = new Dictionary<int, List<Tuple<string, int>>>();

        protected InferenceSession G2p { get; set; }
        protected InferenceSession Prosody { get; set; }

        public VogenBasePhonemizer() {
            G2p ??= new InferenceSession(Data.VogenRes.g2p_man);
            Prosody ??= new InferenceSession(Data.VogenRes.po_man);
        }

        protected abstract string LangPrefix { get; }
        protected virtual string[] Romanize(string[] lyric) => lyric;

        public override void SetSinger(USinger singer) { }

        //Merge slur notes into their previous lyrical note
        private void AddGroup(List<Note> phrase, Note[] group){
            if(group.Length==1){
                phrase.Add(group[0]);
                return;
            }
            phrase.Add(new Note{
                lyric = group[0].lyric,
                phoneticHint = group[0].phoneticHint,
                tone = group[0].tone,
                position = group[0].position,
                duration = group[^1].position + group[^1].duration - group[0].position
            });
        }
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            base.SetUp(groups, project, track);
            if (groups.Length == 0) {
                return;
            }
            var phrase = new List<Note>() {};
            AddGroup(phrase, groups[0]);
            for (int i = 1; i < groups.Length; ++i) {
                if (groups[i - 1][^1].position + groups[i - 1][^1].duration == groups[i][0].position) {
                    AddGroup(phrase, groups[i]);
                } else {
                    ProcessPart(phrase);
                    phrase.Clear();
                    AddGroup(phrase, groups[i]);
                }
            }
            if (phrase.Count > 0) {
                ProcessPart(phrase);
            }
        }

        void ProcessPart(IList<Note> notes) {
            float padding = 1000;
            int totalDur = notes.Sum(n => n.duration);
            var lyrics = Romanize(notes.Select(n => n.lyric).ToArray());
            var lyricsPadded = new string[notes.Count, 8];
            for (int i = 0; i < notes.Count; ++i) {
                string lyric = lyrics[i].PadRight(8, '\0');
                for (int j = 0; j < 8; j++) {
                    lyricsPadded[i, j] = lyric[j].ToString();
                }
            }
            var x = lyricsPadded.ToTensor();
            var inputs = new List<NamedOnnxValue>();
            inputs.Add(NamedOnnxValue.CreateFromTensor("letters", x));
            var outputs = G2p.Run(inputs);
            var phsTensor = outputs.First().AsTensor<string>();
            outputs.Dispose();

            var phs = new List<string>();
            var chPhCounts = new List<long>();
            var noteDursSec = new List<float>();
            phs.Insert(0, "");
            chPhCounts.Add(1);
            noteDursSec.Add(padding);
            for (int i = 0; i < notes.Count; ++i) {
                chPhCounts.Add(0);
                for (int j = 0; j < 4; ++j) {
                    if (phsTensor[i, j] != string.Empty) {
                        chPhCounts[chPhCounts.Count - 1]++;
                        phs.Add($"{LangPrefix}{phsTensor[i, j]}");
                    }
                }
                noteDursSec.Add((float)timeAxis.MsBetweenTickPos(
                    notes[i].position, notes[i].position + notes[i].duration) / 1000);
            }
            phs.Add("");
            chPhCounts.Add(1);
            noteDursSec.Add(padding);

            inputs.Clear();
            inputs.Add(NamedOnnxValue.CreateFromTensor("phs",
                new DenseTensor<string>(phs.ToArray(), new int[] { phs.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("chPhCounts",
                new DenseTensor<long>(chPhCounts.ToArray(), new int[] { chPhCounts.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("noteDursSec",
                new DenseTensor<float>(noteDursSec.ToArray(), new int[] { noteDursSec.Count })));
            outputs = Prosody.Run(inputs);
            var positions = outputs.First()
                .AsTensor<float>()
                .Select(t => t - padding)
                .ToArray();
            outputs.Dispose();

            int index = 1;
            double offsetMs = timeAxis.TickPosToMsPos(notes[0].position);
            double notePos = 0;
            for (int i = 1; i < chPhCounts.Count - 1; ++i) {
                var phonemes = new List<Tuple<string, int>>();
                for (int j = index; j < index + chPhCounts[i]; ++j) {
                    phonemes.Add(Tuple.Create(phs[j], timeAxis.TicksBetweenMsPos(
                       offsetMs + notePos * 1000, offsetMs + positions[j] * 1000)));
                }
                partResult[notes[i - 1].position] = phonemes;
                index += (int)chPhCounts[i];
                notePos += noteDursSec[i];
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (!partResult.TryGetValue(notes[0].position, out var phonemes)) {
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
        }
    }
}
