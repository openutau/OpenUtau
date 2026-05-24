using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.IO;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using System.Text;
using System.Linq;
using Serilog;

namespace OpenUtau.Core.DiffSinger {

    //Config file for rhythmizer
    [Serializable]
    public class DsRhythmizerConfig {
        public string name = "rhythmizer";
        public string model = "model.onnx";
        public string phonemes = "phonemes.txt";
    }

    //Declaration file in the voicebank for which rhythmizer to use
    [Serializable]
    public class DsRhythmizerYaml {
        public string rhythmizer = DsRhythmizer.DefaultRhythmizer;
    }

    class DsRhythmizer {
        public string Location;
        public DsRhythmizerConfig config;
        public InferenceSession session;
        public Dictionary<string, string[]> phoneDict;
        public List<string> phonemes = new List<string>();
        //Default rhythmizer package name
        public static string DefaultRhythmizer = "rhythmizer_zh_opencpop_strict";

        public static Dictionary<string, string[]> LoadPhoneDict(string path, Encoding TextFileEncoding) {
            var phoneDict = new Dictionary<string, string[]>();
            if (File.Exists(path)) {
                foreach (string line in File.ReadLines(path, TextFileEncoding)) {
                    string[] elements = line.Split("\t");
                    phoneDict.Add(elements[0].Trim(), elements[1].Trim().Split(" "));
                }
            }
            return phoneDict;
        }

        //Get rhythmizer using package name
        public static DsRhythmizer FromName(string name) {
            var path = Path.Combine(PathManager.Inst.DependencyPath, name);
            return new DsRhythmizer(path);
        }

        public DsRhythmizer(string path) {
            this.Location = path;
            config = Core.Yaml.DefaultDeserializer.Deserialize<DsRhythmizerConfig>(
                File.ReadAllText(Path.Combine(Location, "vocoder.yaml"),
                    System.Text.Encoding.UTF8));
            phoneDict = LoadPhoneDict(Path.Combine(Location, "dsdict.txt"), Encoding.UTF8);
            //Load phoneme set
            string phonemesPath = Path.Combine(Location, config.phonemes);
            phonemes = File.ReadLines(phonemesPath, Encoding.UTF8).ToList();
            session = new InferenceSession(Path.Combine(Location, config.model));
        }
    }

    [Phonemizer("DiffSinger Rhythmizer Phonemizer", "DIFFS RHY", language: "ZH")]
    public class DiffSingerRhythmizerPhonemizer : MachineLearningPhonemizer {
        USinger singer;
        DsRhythmizer rhythmizer;
        Dictionary<string, string[]> phoneDict;
        
        public override void SetSinger(USinger singer) {
            if (this.singer == singer) {
                return;
            }
            this.singer = singer;
            if (this.singer == null) {
                return;
            }
            //Load rhythmizer model
            try {
                //if rhythmizer is packed within the voicebank
                var packedRhythmizerPath = Path.Combine(singer.Location, "rhythmizer");
                if(Directory.Exists(packedRhythmizerPath)) {
                    rhythmizer = new DsRhythmizer(packedRhythmizerPath);
                } else { 
                    string rhythmizerName;
                    string rhythmizerYamlPath = Path.Combine(singer.Location, "dsrhythmizer.yaml");
                    if (File.Exists(rhythmizerYamlPath)) {
                        rhythmizerName = Core.Yaml.DefaultDeserializer.Deserialize<DsRhythmizerYaml>(
                            File.ReadAllText(rhythmizerYamlPath, singer.TextFileEncoding)).rhythmizer;
                    } else {
                        rhythmizerName = DsRhythmizer.DefaultRhythmizer;
                    }
                    rhythmizer = DsRhythmizer.FromName(rhythmizerName);
                }
                //Load pinyin to phoneme dictionary from rhythmizer package
                phoneDict = rhythmizer.phoneDict;
            } catch (Exception ex) {
                Log.Error(ex, "Failed to load rhythmizer");
                throw;
            }
        }

        //Run timing model for a sentence
        //Slur notes are merged into the lyrical note before it to prevent shortening of consonants due to short slur
        protected override void ProcessPart(Note[][] phrase) {
            float padding = 0.5f;//Padding time for consonants at the beginning of a sentence
            var phonemes = new List<string> { "SP" };
            var midi = new List<long> { 0 };//Phoneme pitch
            var midi_dur = new List<float> { padding };//List of parent note duration for each phoneme
            var is_slur = new List<bool> { false };//Whether the phoneme is slur
            List<double> ph_dur;//Phoneme durations output by the model
            var notePhIndex = new List<int>{ 1 };//The position of the first phoneme of each note in the phoneme list
            var phAlignPoints = new List<Tuple<int, double>>();//Phoneme alignment position, s, absolute time
            double offsetMs = timeAxis.TickPosToMsPos(phrase[0][0].position);

            //Convert note list to phoneme list
            foreach (int groupIndex in Enumerable.Range(0,phrase.Length)) {
                string[] notePhonemes;
                Note[] group = phrase[groupIndex];
                if (group[0].phoneticHint is null) {
                    var lyric = group[0].lyric;

                    if (phoneDict.ContainsKey(lyric)) {
                        notePhonemes = phoneDict[lyric];
                    } else if (rhythmizer.phonemes.Contains(lyric)) {
                        notePhonemes = new string[] { lyric };
                    } else {
                        notePhonemes = new string[] { "SP" };
                        unrecognizedLyrics[group[0].position] = lyric;
                    }
                } else {
                    notePhonemes = group[0].phoneticHint.Split(" ");
                }
                is_slur.AddRange(Enumerable.Repeat(false, notePhonemes.Length));
                phAlignPoints.Add(new Tuple<int,double>(
                    phonemes.Count + (notePhonemes.Length > 1 ? 1 : 0),//TODO
                    timeAxis.TickPosToMsPos(group[0].position) / 1000
                    ));
                phonemes.AddRange(notePhonemes);
                midi.AddRange(Enumerable.Repeat((long)group[0].tone, notePhonemes.Length));
                notePhIndex.Add(phonemes.Count);

                midi_dur.AddRange(Enumerable.Repeat((float)timeAxis.MsBetweenTickPos(
                    group[0].position, group[^1].position + group[^1].duration) / 1000, notePhonemes.Length));
            }
            var lastNote = phrase[^1][^1];
            phAlignPoints.Add(new Tuple<int, double>(
                phonemes.Count,
                timeAxis.TickPosToMsPos(lastNote.position + lastNote.duration) / 1000));

            //Call Diffsinger phoneme timing model
            //ph_dur = session.run(['ph_dur'], {'tokens': tokens, 'midi': midi, 'midi_dur': midi_dur, 'is_slur': is_slur})[0]
            //error phonemes are replaced with SP
            long defaultToken = rhythmizer.phonemes.IndexOf("SP");
            var tokens = phonemes
                .Select(x => {
                    var idx = rhythmizer.phonemes.IndexOf(x);
                    if (idx < 0) {
                        Log.Warning($"Phoneme \"{x}\" not found in rhythmizer, replacing with SP");
                    }
                    return (long)(idx < 0 ? defaultToken : idx);
                })
                .ToList();
            var inputs = new List<NamedOnnxValue>();
            inputs.Add(NamedOnnxValue.CreateFromTensor("tokens",
                new DenseTensor<long>(tokens.ToArray(), new int[] { tokens.Count }, false)
                .Reshape(new int[] { 1, tokens.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("midi",
                new DenseTensor<long>(midi.ToArray(), new int[] { midi.Count }, false)
                .Reshape(new int[] { 1, midi.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("midi_dur",
                new DenseTensor<float>(midi_dur.ToArray(), new int[] { midi_dur.Count }, false)
                .Reshape(new int[] { 1, midi_dur.Count })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("is_slur",
                new DenseTensor<bool>(is_slur.ToArray(), new int[] { is_slur.Count }, false)
                .Reshape(new int[] { 1, is_slur.Count })));
            var outputs = rhythmizer.session.Run(inputs);
            ph_dur = outputs.First().AsTensor<float>().Select(x => (double)x).ToList();
            //Align the starting time of vowels to the position of each note, unit: s
            var positions = new List<double>();
            List<double> alignGroup = ph_dur.GetRange(0, phAlignPoints[0].Item1);
            //Starting consonants are not scaled
            positions.AddRange(stretch(alignGroup, 1, phAlignPoints[0].Item2));
            //The other phonemes are scaled according to the ratio of the time difference 
            //between the two alignment points and the duration of the phoneme
            //pairwise(alignGroups)
            foreach(var pair in phAlignPoints.Zip(phAlignPoints.Skip(1), (a, b) => Tuple.Create(a, b))){
                var currAlignPoint = pair.Item1;
                var nextAlignPoint = pair.Item2;
                alignGroup = ph_dur.GetRange(currAlignPoint.Item1, nextAlignPoint.Item1 - currAlignPoint.Item1);
                double ratio = (nextAlignPoint.Item2 - currAlignPoint.Item2)/alignGroup.Sum();
                positions.AddRange(stretch(alignGroup, ratio, nextAlignPoint.Item2));
            }
            //Convert the position sequence to tick and fill into the result list
            int index = 1;
            foreach(int groupIndex in Enumerable.Range(0, phrase.Length)) {
                Note[] group = phrase[groupIndex];
                var noteResult = new List<Tuple<string, int>>();
                if (group[0].lyric.StartsWith("+")) {
                    continue;
                }
                double notePos = timeAxis.TickPosToMsPos(group[0].position);//音符起点位置，单位ms
                for(int phIndex = notePhIndex[groupIndex]; phIndex < notePhIndex[groupIndex + 1]; ++phIndex) {
                    noteResult.Add(Tuple.Create(phonemes[phIndex], timeAxis.TicksBetweenMsPos(
                       notePos, positions[phIndex] * 1000)));
                }
                partResult[group[0].position] = noteResult;
            }
        }

        public static IEnumerable<double> CumulativeSum(IEnumerable<double> sequence, double start = 0) {
            double sum = start;
            foreach (var item in sequence) {
                sum += item;
                yield return sum;
            }
        }
        
        //Stretch phoneme duration sequence with a certain ratio
        public List<double> stretch(IList<double> source, double ratio, double endPos) {
            //source: phoneme duration sequence, unit: s
            //ratio：scaling ratio
            //endPos: target end time, unit: s
            //output: scaled phoneme position, unit: s
            double startPos = endPos - source.Sum() * ratio;
            var result = CumulativeSum(source.Select(x => x * ratio).Prepend(0),startPos).ToList();
            result.RemoveAt(result.Count - 1);
            return result;
        }

        protected override string[] Romanize(IEnumerable<string> lyrics) {
            return BaseChinesePhonemizer.Romanize(lyrics);
        }
    }
}
