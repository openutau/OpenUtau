using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Hts;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Util.nnmnkwii.frontend;
using OpenUtau.Core.Util.nnmnkwii.io.hts;
using Xunit;
using Xunit.Abstractions;

namespace OpenUtau.Plugins {
    // Minimal concrete HTSLabelPhonemizer for testing without external aligners.
    class DummyHtsLabelPhonemizer : HTSLabelPhonemizer {
        public string GeneratedFullScorePath => fullScorePath;
        public string GeneratedMonoTimingPath => monoTimingPath;
        public string GeneratedTempPath => htstmpPath;
        public bool DictionaryLoadedBeforeG2p { get; private set; }

        public DummyHtsLabelPhonemizer() {
            // Minimal language and symbol classes
            lang = "JPN";
            vowels = new List<string> { "a", "i", "u", "e", "o" };
            pauses = new List<string> { "pau" };
            silences = new List<string> { "sil" };
            breaks = new List<string> { "br" };
            tablePath = "oto.ini";
        }

        protected override IG2p LoadG2p(string rootPath) {
            DictionaryLoadedBeforeG2p = phoneDict.Count > 0;
            // Provide a tiny JP-like dictionary: simple CV mapping.
            var builder = G2pDictionary.NewBuilder();
            // vowels
            builder.AddSymbol("a", true);
            builder.AddSymbol("i", true);
            builder.AddSymbol("u", true);
            builder.AddSymbol("e", true);
            builder.AddSymbol("o", true);
            // consonants
            var cons = new[] { "k", "s", "t", "n", "h", "m", "y", "r", "w" };
            foreach (var c in cons) builder.AddSymbol(c, false);
            // pauses etc
            builder.AddSymbol("pau", false);
            builder.AddSymbol("sil", false);
            builder.AddSymbol("br", false);
            // single vowels
            builder.AddEntry("a", new[] { "a" });
            builder.AddEntry("i", new[] { "i" });
            builder.AddEntry("u", new[] { "u" });
            builder.AddEntry("e", new[] { "e" });
            builder.AddEntry("o", new[] { "o" });
            // CV (subset)
            builder.AddEntry("ka", new[] { "k", "a" });
            builder.AddEntry("ki", new[] { "k", "i" });
            builder.AddEntry("ku", new[] { "k", "u" });
            builder.AddEntry("ke", new[] { "k", "e" });
            builder.AddEntry("ko", new[] { "k", "o" });
            builder.AddEntry("ta", new[] { "t", "a" });
            builder.AddEntry("ti", new[] { "t", "i" });
            builder.AddEntry("to", new[] { "t", "o" });
            builder.AddEntry("na", new[] { "n", "a" });
            builder.AddEntry("ni", new[] { "n", "i" });
            builder.AddEntry("no", new[] { "n", "o" });
            builder.AddEntry("ma", new[] { "m", "a" });
            builder.AddEntry("mi", new[] { "m", "i" });
            builder.AddEntry("mo", new[] { "m", "o" });
            builder.AddEntry("ra", new[] { "r", "a" });
            builder.AddEntry("ri", new[] { "r", "i" });
            builder.AddEntry("ro", new[] { "r", "o" });
            return builder.Build();
        }

        protected override HTSNote CustomHTSNoteContext(HTSNote htsNote, Phonemizer.Note note) {
            return htsNote; // no-op
        }

        protected override HTSPhoneme[] CustomHTSPhonemeContext(HTSPhoneme[] htsPhonemes, Phonemizer.Note[] notes) {
            return htsPhonemes; // no-op
        }

        protected override Phonemizer.Note[][] PhraseAdjustments(Phonemizer.Note[][] phrese) {
            return phrese; // no-op
        }

        protected override void SendScore(Phonemizer.Note[][] phrase) {
            // Create a fake mono_timing.lab with uniform 100ms durations for each phoneme in full_score.lab
            if (!Directory.Exists(htstmpPath)) {
                Directory.CreateDirectory(htstmpPath);
            }
            int count = 0;
            if (File.Exists(fullScorePath)) {
                count = File.ReadLines(fullScorePath).Count();
            }
            long start = 0;
            var lines = new List<string>(count);
            for (int i = 0; i < count; i++) {
                long end = start + 1_000_000; // 100ms in 100ns units
                lines.Add($"{start} {end} a");
                start = end;
            }
            File.WriteAllLines(monoTimingPath, lines);
        }
    }

    public class HtsLabelPhonemizerTest : PhonemizerTestBase {
        public HtsLabelPhonemizerTest(ITestOutputHelper output) : base(output) { }

        protected override Phonemizer CreatePhonemizer() {
            return new DummyHtsLabelPhonemizer();
        }

        [Theory]
        [InlineData(new string[] { "a" }, new string[] { "a" })]
        [InlineData(new string[] { "a", "i" }, new string[] { "a", "i" })]
        [InlineData(new string[] { "a", "+~a", "i" }, new string[] { "a", "i" })] // extension note should not duplicate symbols
        // JP CV
        [InlineData(new string[] { "ka" }, new string[] { "k", "a" })]
        [InlineData(new string[] { "ka", "ki" }, new string[] { "k", "a", "k", "i" })]
        [InlineData(new string[] { "ka", "+~a", "ki" }, new string[] { "k", "a", "k", "i" })]
        public void BasicHtsPipelineTest(string[] lyrics, string[] aliases) {
            SameAltsTonesColorsTest("en_delta0", lyrics, aliases, "", "C4", "");
        }

        [Fact]
        public void DictionaryIsLoadedBeforeG2p() {
            var phonemizer = CreateConfiguredPhonemizer(new[] { "a" });

            Assert.True(phonemizer.DictionaryLoadedBeforeG2p);
        }

        [Fact]
        public void GeneratedLabelsCanDriveFrontendAndSimpleSynthesis() {
            var phonemizer = CreateConfiguredPhonemizer(new[] { "ka", "ki", "ro" });

            Assert.True(File.Exists(phonemizer.GeneratedFullScorePath));
            Assert.True(File.Exists(phonemizer.GeneratedMonoTimingPath));

            var questionPath = WriteMinimalQuestionSet(phonemizer.GeneratedTempPath);
            var questionSet = hts.load_question_set(questionPath, encoding: Encoding.UTF8);
            var fullLabels = hts.load(phonemizer.GeneratedFullScorePath, Encoding.UTF8);
            var monoLabels = hts.load(phonemizer.GeneratedMonoTimingPath, Encoding.UTF8);
            var features = merlin.linguistic_features(fullLabels, questionSet.Item1, questionSet.Item2);

            Assert.Equal(fullLabels.Count, monoLabels.Count);
            Assert.Equal(fullLabels.Count, features.Count);
            Assert.All(features, feature => {
                Assert.Single(feature);
                Assert.Equal(1f, feature[0]);
            });

            var waveform = SynthesizeFromLabels(monoLabels, features, 16000);

            Assert.NotEmpty(waveform);
            Assert.Contains(waveform, sample => Math.Abs(sample) > 0.0001f);
        }

        DummyHtsLabelPhonemizer CreateConfiguredPhonemizer(string[] lyrics) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var basePath = Path.Join(dir, "Files");
            var file = Path.Join(basePath, "en_delta0", "character.txt");

            VoicebankLoader.IsTest = true;
            var voicebank = new Voicebank() { File = file, BasePath = dir };
            VoicebankLoader.LoadVoicebank(voicebank);
            var singer = new ClassicSinger(voicebank);
            singer.EnsureLoaded();

            var project = new UProject();
            Ustx.AddDefaultExpressions(project);
            var track = project.tracks[0];
            project.expressions.TryGetValue(Ustx.CLR, out var descriptor);
            track.VoiceColorExp = descriptor.Clone();
            var colors = singer.Subbanks.Select(subbank => subbank.Color).ToHashSet();
            track.VoiceColorExp.options = colors.OrderBy(color => color).ToArray();
            track.VoiceColorExp.max = track.VoiceColorExp.options.Length - 1;

            var timeAxis = new TimeAxis();
            timeAxis.BuildSegments(project);

            var phonemizer = new DummyHtsLabelPhonemizer();
            phonemizer.Testing = true;
            phonemizer.SetSinger(singer);
            phonemizer.SetTiming(timeAxis);
            phonemizer.SetUp(BuildGroups(lyrics), project, track);
            return phonemizer;
        }

        Phonemizer.Note[][] BuildGroups(string[] lyrics) {
            var groups = new List<Phonemizer.Note[]>();
            int position = 240;
            foreach (var lyric in lyrics) {
                groups.Add(new[] {
                    new Phonemizer.Note {
                        lyric = lyric,
                        duration = 240,
                        position = position,
                        tone = Core.MusicMath.NameToTone("C4"),
                        phonemeAttributes = new[] {
                            new Phonemizer.PhonemeAttributes {
                                index = 0,
                                consonantStretchRatio = 1,
                                voiceColor = string.Empty,
                            }
                        },
                    }
                });
                position += 240;
            }
            return groups.ToArray();
        }

        string WriteMinimalQuestionSet(string directory) {
            var questionPath = Path.Combine(directory, "test-minimal.qst");
            File.WriteAllLines(questionPath, new[] {
                "QS \"ALL\" {*}",
            });
            return questionPath;
        }

        float[] SynthesizeFromLabels(HTSLabelFile monoLabels, List<List<float>> features, int sampleRate) {
            Assert.True(monoLabels.Count > 0);
            long totalDuration = monoLabels[^1].end_time;
            int totalSamples = (int)Math.Ceiling(totalDuration / 10_000_000.0 * sampleRate);
            var waveform = new float[totalSamples];
            for (int index = 0; index < monoLabels.Count; index++) {
                var label = monoLabels[index];
                Assert.True(label.end_time > label.start_time);
                if (index > 0) {
                    Assert.Equal(monoLabels[index - 1].end_time, label.start_time);
                }
                int startSample = (int)Math.Round(label.start_time / 10_000_000.0 * sampleRate);
                int endSample = Math.Min(totalSamples, (int)Math.Round(label.end_time / 10_000_000.0 * sampleRate));
                float amplitude = 0.05f + 0.05f * features[index].Sum();
                float frequency = 220f + 30f * index;
                for (int sample = startSample; sample < endSample; sample++) {
                    float time = sample / (float)sampleRate;
                    waveform[sample] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * time);
                }
            }
            return waveform;
        }
    }
}
