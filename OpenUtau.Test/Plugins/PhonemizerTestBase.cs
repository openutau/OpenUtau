using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.Format;
using Xunit;
using Xunit.Abstractions;

namespace OpenUtau.Plugins {
    public abstract class PhonemizerTestBase {
        public struct NoteParams {
            public string lyric;
            public string hint;
            public string tone;
            public PhonemeParams[] phonemes;
        }
        public struct PhonemeParams {
            public int shift;
            public int alt;
            public string color;
        }

        protected readonly ITestOutputHelper output;

        protected abstract Phonemizer CreatePhonemizer();

        public PhonemizerTestBase(ITestOutputHelper output) {
            this.output = output;
        }

        public void RunPhonemizeTest(string singerName, string[] lyrics, string[] alts, string[] tones, string[] colors, string[] aliases) {
            var groups = GetSinglePhonemeNotes(lyrics, alts, tones, colors);
            RunPhonemizeTest(singerName, groups, aliases);
        }

        public void RunPhonemizeTest(string singerName, NoteParams[] inputs, string[] aliases) {
            var groups = GetMultiPhonemeNotes(inputs);
            RunPhonemizeTest(singerName,groups, aliases);
        }

        void RunPhonemizeTest(string singerName, List<Phonemizer.Note[]> groups, string[] aliases) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var basePath = Path.Join(dir, "Files");
            var file = Path.Join(basePath, singerName, "character.txt");

            VoicebankLoader.IsTest = true;
            var voicebank = new Voicebank() { File = file, BasePath = dir };
            VoicebankLoader.LoadVoicebank(voicebank);
            var singer = new ClassicSinger(voicebank);
            singer.EnsureLoaded();

            var project = new Core.Ustx.UProject();
            Ustx.AddDefaultExpressions(project);
            var track = project.tracks[0];
            project.expressions.TryGetValue(Ustx.CLR, out var descriptor);
            track.VoiceColorExp = descriptor.Clone();
            var colors = singer.Subbanks.Select(subbank => subbank.Color).ToHashSet();
            track.VoiceColorExp.options = colors.OrderBy(c => c).ToArray();
            track.VoiceColorExp.max = track.VoiceColorExp.options.Length - 1;

            var timeAxis = new Core.TimeAxis();
            timeAxis.BuildSegments(project);

            var phonemizer = CreatePhonemizer();
            phonemizer.Testing = true;
            phonemizer.SetSinger(singer);
            phonemizer.SetTiming(timeAxis);
            phonemizer.SetUp(groups.ToArray(), project, track);

            var results = new List<Phonemizer.Result>();
            for (var i = 0; i < groups.Count; i++) {
                results.Add(phonemizer.Process(
                    groups[i],
                    i > 0 ? groups[i - 1].First() : null,
                    i < groups.Count - 1 ? groups[i + 1].First() : null,
                    i > 0 ? groups[i - 1].First() : null,
                    i < groups.Count - 1 ? groups[i + 1].First() : null,
                    i > 0 ? groups[i - 1] : null));
            }
            var resultAliases = results.SelectMany(r => r.phonemes).Select(p => p.phoneme).ToArray();

            var builder = new StringBuilder();
            foreach (var resultAlias in resultAliases) {
                builder.Append("\"");
                builder.Append(resultAlias);
                builder.Append("\", ");
            }
            output.WriteLine(builder.ToString());

            Assert.Equal(aliases, resultAliases);
        }

        List<Phonemizer.Note[]> GetSinglePhonemeNotes(string[] lyrics, string[] alts, string[] tones, string[] colors) {
            Assert.Equal(lyrics.Length, alts.Length);
            Assert.Equal(lyrics.Length, tones.Length);
            Assert.Equal(lyrics.Length, colors.Length);
            var result = new List<Phonemizer.Note[]>();
            var group = new List<Phonemizer.Note>();
            int position = 240;
            for (var i = 0; i < lyrics.Length; i++) {
                var lyric = lyrics[i];
                if (!lyric.StartsWith("+") && group.Count > 0) {
                    result.Add(group.ToArray());
                    group.Clear();
                }
                group.Add(new Phonemizer.Note {
                    lyric = lyric,
                    duration = 240,
                    position = position,
                    tone = Core.MusicMath.NameToTone(tones[i]),
                    phonemeAttributes = new Phonemizer.PhonemeAttributes[] {
                        new Phonemizer.PhonemeAttributes{
                            index = 0,
                            consonantStretchRatio = 1,
                            voiceColor = colors[i],
                        }
                    },
                });
                position += 240;
            }
            if (group.Count > 0) {
                result.Add(group.ToArray());
                group.Clear();
            }
            return result;
        }

        List<Phonemizer.Note[]> GetMultiPhonemeNotes(NoteParams[] inputs) {
            var result = new List<Phonemizer.Note[]>();
            var group = new List<Phonemizer.Note>();
            int position = 240;
            for (var i = 0; i < inputs.Length; i++) {
                var noteParams = inputs[i];
                var lyric = noteParams.lyric;
                if (!lyric.StartsWith("+") && group.Count > 0) {
                    result.Add(group.ToArray());
                    group.Clear();
                }
                var attr = new Phonemizer.PhonemeAttributes[noteParams.phonemes.Length];
                for (var j = 0;j < noteParams.phonemes.Length; j++) {
                    var phonemeParams = noteParams.phonemes[j];
                    attr[j] = new Phonemizer.PhonemeAttributes {
                        index = j,
                        consonantStretchRatio = 1,
                        toneShift = phonemeParams.shift,
                        alternate = phonemeParams.alt == 0 ? null : phonemeParams.alt, // 0 means no alt (nothing added)
                        voiceColor = phonemeParams.color
                    };
                }
                group.Add(new Phonemizer.Note {
                    lyric = lyric,
                    duration = 240,
                    position = position,
                    tone = Core.MusicMath.NameToTone(noteParams.tone),
                    phoneticHint = noteParams.hint,
                    phonemeAttributes = attr
                });
                position += 240;
            }
            if (group.Count > 0) {
                result.Add(group.ToArray());
                group.Clear();
            }
            return result;
        }

        protected void SameAltsTonesColorsTest(string singerName, string[] lyrics, string[] aliases, string alt, string tone, string color) {
            RunPhonemizeTest(singerName, lyrics,
                RepeatString(lyrics.Length, alt),
                RepeatString(lyrics.Length, tone),
                RepeatString(lyrics.Length, color), aliases);
        }

        protected PhonemeParams[] SamePhonemeParams(int count, int shift, int alt, string color) {
            PhonemeParams[] array = new PhonemeParams[count];
            Array.Fill(array, new PhonemeParams { shift = shift, alt = alt, color = color });
            return array;
        }

        protected string[] RepeatString(int count, string s) {
            string[] array = new string[count];
            Array.Fill(array, s);
            return array;
        }
    }
}
