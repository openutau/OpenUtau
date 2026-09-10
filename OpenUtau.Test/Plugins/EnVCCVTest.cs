using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class EnVCCVTest : PhonemizerTestBase {
        public EnVCCVTest(ITestOutputHelper output) : base(output) { }
        protected override Phonemizer CreatePhonemizer() {
            return new EnglishVCCVPhonemizer();
        }
        
        /// <summary>
        /// Runs the phonemizer with a real UVoicePart so unotes is populated and
        /// convel is active. Returns vel values in phoneme order.
        /// </summary>
        float[] RunConvelTest(string singerName, string[] lyrics, int[] durations) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var file = Path.Join(dir, "Files", singerName, "character.txt");

            VoicebankLoader.IsTest = true;
            var voicebank = new Voicebank() { File = file, BasePath = dir };
            VoicebankLoader.LoadVoicebank(voicebank);
            var singer = new ClassicSinger(voicebank);
            singer.EnsureLoaded();

            var project = new UProject();
            Ustx.AddDefaultExpressions(project);
            var track = project.tracks[0];
            project.expressions.TryGetValue(Ustx.CLR, out var colorDescriptor);
            track.VoiceColorExp = colorDescriptor.Clone();
            var voiceColors = singer.Subbanks.Select(subbank => subbank.Color).ToHashSet();
            track.VoiceColorExp.options = voiceColors.OrderBy(c => c).ToArray();
            track.VoiceColorExp.max = track.VoiceColorExp.options.Length - 1;

            var part = new UVoicePart { trackNo = 0 };
            int pos = 240;
            foreach (var dur in durations) {
                part.notes.Add(new UNote {
                    position = pos, duration = dur,
                    pitch = new UPitch(), vibrato = new UVibrato(),
                });
                pos += dur;
            }
            project.parts.Add(part);

            var timeAxis = new TimeAxis();
            timeAxis.BuildSegments(project);

            var groups = lyrics.Select((lyric, i) => new Phonemizer.Note[] {
                new Phonemizer.Note {
                    lyric = lyric, duration = durations[i], position = 240 + durations.Take(i).Sum(),
                    tone = MusicMath.NameToTone("C4"),
                    phonemeAttributes = new[] {
                        new Phonemizer.PhonemeAttributes { index = 0, consonantStretchRatio = 1 }
                    },
                }
            }).ToList();

            var phonemizer = new EnglishVCCVPhonemizer();
            phonemizer.Testing = true;
            phonemizer.SetSinger(singer);
            phonemizer.SetTiming(timeAxis);
            phonemizer.SetUp(groups.ToArray(), project, track);

            var results = groups.Select((g, i) => phonemizer.Process(
                g,
                i > 0 ? groups[i - 1][0] : null,
                i < groups.Count - 1 ? groups[i + 1][0] : null,
                i > 0 ? groups[i - 1][0] : null,
                i < groups.Count - 1 ? groups[i + 1][0] : null,
                i > 0 ? groups[i - 1] : null)).ToList();

            return results
                .SelectMany(r => r.phonemes)
                .Select(p => p.expressions?.FirstOrDefault(e => e.abbr == "vel").value ?? -1f)
                .ToArray();
        }

        [Theory]
        [InlineData("en_vccv",
            new string[] { "test", "words" },
            new string[] { "-te", "es-", "st", "w3", "3d-", "dz-" })]
        public void BasicPhonemizingTest(string singerName, string[] lyrics, string[] aliases) {
            SameAltsTonesColorsTest(singerName, lyrics, aliases, "", "C4", "");
        }

        [Fact]
        public void ToneShiftTest() {
            RunPhonemizeTest("en_vccv", new NoteParams[] {
                new NoteParams {
                    lyric = "hi",
                    hint = "",
                    tone = "C4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            alt = 0,
                            shift = 0,
                            color = "",
                        },
                        new PhonemeParams {
                            alt = 0,
                            shift = 12,
                            color = "",
                        },
                    }
                }
            }, new string[] { "-hI", "I-_H" });
        }

        [Theory]
        [InlineData("read", "", new string[] { "-re", "ed-" })]
        [InlineData("read", "r E d", new string[] { "-rE", "Ed-" })]

        [InlineData("asdfjkl", "r E d", new string[] { "-rE", "Ed-" })]
        [InlineData("", "r E d", new string[] { "-rE", "Ed-" })]
        public void HintTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_vccv", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C4", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }
        


        // CV onset phonemes use the current note's vel; coda (VC-) phonemes use prevVel.
        // "sea" (dur=240 → vel=150) then "bird" (dur=960 → vel=50):
        //   -sE  → CV,  vel = 150 (sea's own vel)
        //   Ed-  → VC-, vel = 150 (bird's coda inherits sea's vel as prevVel)
        //   -b3  → CV,  vel =  50 (bird's own vel)
        [Fact]
        public void ConvelCodaInheritsPrevNoteVel() {
            var vels = RunConvelTest("en_vccv",
                new[] { "sea",  "bird" },
                new[] { 240,     960  });

            // All phonemes should have a valid vel (not the -1 sentinel)
            Assert.All(vels, v => Assert.True(v >= 0, $"Missing vel expression (got {v})"));

            // sea's CV onset: vel = 150
            Assert.Equal(150f, vels[0], precision: 1);
            // bird's VC- coda: inherits prevVel = 150
            Assert.Equal(150f, vels[1], precision: 1);
            // bird's CV onset: vel = 50
            Assert.Equal(50f,  vels[2], precision: 1);
        }

        // A lone starting-V note has no prev, so vel = noteVel.
        // duration 240 → 150,  480 → 100,  960 → 50
        [Theory]
        [InlineData(240, 150f)]
        [InlineData(480, 100f)]
        [InlineData(960,  50f)]
        public void ConvelNoteVelScalesWithDuration(int duration, float expectedVel) {
            var vels = RunConvelTest("en_vccv", new[] { "a" }, new[] { duration });
            Assert.All(vels, v => Assert.Equal(expectedVel, v, precision: 1));
        }
        
    }
}
