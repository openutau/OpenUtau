using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core.Enunu;
using Xunit;

namespace OpenUtau.Core {
    public class EnunuConnectionTest {
        [Fact]
        public void F0ToTonesInterpolatesFramesWithoutPitch() {
            double a = MusicMath.ToneToFreq(60);
            double b = MusicMath.ToneToFreq(64);
            var tones = EnunuRenderer.F0ToTones(new double[] { 0, a, 0, 0, 0, b, 0 })!;
            Assert.Equal(new float[] { 60, 60, 61, 62, 63, 64, 64 }, tones.Select(t => (float)Math.Round(t, 3)));
        }

        [Fact]
        public void F0ToTonesReturnsNullWithoutPitch() {
            Assert.Null(EnunuRenderer.F0ToTones(new double[] { 0, 0, 0 }));
        }

        [Fact]
        public void CommandRequestsMatchTheServerFormat() {
            // ENUNUServer 2: style shift at [5] is required; acoustic_f0 carries the f0 at [6] as numbers.
            Assert.Equal("[\"acoustic\",\"a.tmp\",\"\",\"hash\",\"600\",0]",
                Json.Serialize(EnunuConnection.CommandRequest(EnunuCommand.Acoustic, "a.tmp", "", "hash")));
            Assert.Equal("[\"acoustic_f0\",\"a.tmp\",\"\",\"hash\",\"600\",0,[0,220.5]]",
                Json.Serialize(EnunuConnection.CommandRequest(EnunuCommand.AcousticF0, "a.tmp", "", "hash", new double[] { 0, 220.5 })));
        }

        [Fact]
        public void ReadsFeaturesOnlyFromNewServers() {
            var legacy = Json.Deserialize<VersionResponse>(
                "{\"result\": {\"name\": \"SimpleENUNUServer\", \"version\": \"0.5.0\", \"author\": \"x\"}}");
            Assert.Null(legacy.result.features);

            var current = Json.Deserialize<VersionResponse>(
                "{\"result\": {\"name\": \"SimpleENUNUServer\", \"version\": \"2.0.0\", \"author\": \"roku10shi\", " +
                "\"features\": {\"commands\": [\"timing\", \"acoustic\", \"pitch\", \"acoustic_f0\", \"synthe\", \"config\"], " +
                "\"style_shift\": true, \"pitch_n_frames\": true, " +
                "\"diffusion\": {\"mgc\": {\"method\": \"ddim\", \"steps\": 25}}}}}");
            Assert.True(current.result.features!.SupportsPitch);
            Assert.True(current.result.features.Has(EnunuCommand.Config));
            Assert.Equal(25, current.result.features.diffusion!["mgc"].steps);
        }

        [Fact]
        public void ReadsResponses() {
            var pitch = Json.Deserialize<PitchResponse>(
                "{\"result\": {\"path_f0\": \"p.npy\", \"lf0_conditioning\": true, \"n_frames\": 12}}");
            Assert.Null(pitch.Error);
            Assert.True(pitch.result.lf0_conditioning);
            Assert.Equal(12, pitch.result.n_frames);

            // acoustic has no lf0_conditioning; acoustic_f0 has it.
            var acoustic = Json.Deserialize<AcousticResponse>("{\"result\": {\"path_f0\": \"f.npy\", \"path_mel\": null}}");
            Assert.Null(acoustic.result.lf0_conditioning);
            var error = Json.Deserialize<AcousticResponse>("{\"error\": \"boom\"}");
            Assert.Equal("boom", error.Error);
        }

        /// <summary>
        /// Sends pitch → acoustic_f0 → synthe to a running ENUNUServer on 15556, the way EnunuRenderer does.
        /// Set ENUNU_TEST_VOICE to a voicebank folder to run it; skipped otherwise.
        /// </summary>
        [Fact]
        public void TalksToARunningServer() {
            string? voice = Environment.GetEnvironmentVariable("ENUNU_TEST_VOICE");
            if (string.IsNullOrEmpty(voice) || !Directory.Exists(voice)) {
                Assert.Skip("Set ENUNU_TEST_VOICE to a voicebank folder and start ENUNUServer on 15556.");
            }
            var dir = Path.Combine(Path.GetTempPath(), $"enunu-conn-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try {
                var ust = Path.Combine(dir, "enu-test.tmp");
                WriteTmp(ust, voice!, dir);
                var connection = EnunuConnection.Inst;
                Assert.True(connection.GetFeatures()?.SupportsPitch, "the server on 15556 has no pitch / acoustic_f0");

                var pitch = connection.Pitch(ust, "test-voice");
                Assert.True(pitch.result.n_frames > 0);
                Assert.True(File.Exists(pitch.result.path_f0));
                Assert.Equal(pitch.result.lf0_conditioning, connection.Lf0Conditioning("test-voice"));

                // +200 cent over the model's own pitch, as a drawn curve would be.
                var f0 = NumSharp.np.Load<double[]>(pitch.result.path_f0).Select(f => f * Math.Pow(2, 2.0 / 12)).ToArray();
                var acoustic = connection.AcousticF0(ust, "test-voice", f0);
                Assert.True(File.Exists(acoustic.result.path_f0));

                var wav = Path.Combine(dir, "out.wav");
                connection.Synthe(ust, wav, "test-voice");
                Assert.True(new FileInfo(wav).Length > 1000);
            } finally {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // Same format as EnunuUtils.WriteUst: head rest, one note per phoneme, tail rest.
        static void WriteTmp(string path, string voice, string cacheDir) {
            var notes = new (string lyric, int length, int noteNum)[] {
                ("R", 240, 60), ("a", 480, 60), ("i", 480, 62), ("u", 480, 64), ("e", 480, 65), ("o", 960, 67), ("R", 240, 67),
            };
            var sb = new StringBuilder();
            sb.AppendLine("[#SETTING]").AppendLine("Tempo=120").AppendLine("Tracks=1")
                .AppendLine($"Project={path}").AppendLine($"VoiceDir={voice}").AppendLine($"CacheDir={cacheDir}").AppendLine("Mode2=True");
            for (int i = 0; i < notes.Length; i++) {
                sb.AppendLine($"[#{i}]").AppendLine($"Lyric={notes[i].lyric}").AppendLine($"Length={notes[i].length}")
                    .AppendLine($"NoteNum={notes[i].noteNum}").AppendLine("Velocity=100");
            }
            sb.AppendLine("[#TRACKEND]");
            // ASCII only, so it reads the same as Shift_JIS.
            File.WriteAllText(path, sb.ToString());
        }
    }
}
