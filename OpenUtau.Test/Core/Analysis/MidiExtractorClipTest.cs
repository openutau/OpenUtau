using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.Analysis {
    public class MidiExtractorClipTest {
        sealed class Options {
            public Options() { }
        }

        sealed class RecordingExtractor : MidiExtractor<Options> {
            readonly bool batch;
            readonly bool extendLastNote;
            public List<float[]> Inputs { get; } = new();
            public int BatchCalls { get; private set; }
            protected override int ExpectedSampleRate => 44100;
            protected override bool SupportsBatch => batch;

            public RecordingExtractor(bool batch = false, bool extendLastNote = false) {
                this.batch = batch;
                this.extendLastNote = extendLastNote;
            }

            protected override List<TranscribedNote> TranscribeWaveform(float[] samples, Options options) {
                Inputs.Add(samples.ToArray());
                return new List<TranscribedNote> {
                    new((float)samples.Length / ExpectedSampleRate + (extendLastNote ? 1 : 0), 60, true),
                };
            }

            protected override List<List<TranscribedNote>> TranscribeWaveformBatch(List<float[]> samples, Options options) {
                BatchCalls++;
                return base.TranscribeWaveformBatch(samples, options);
            }

            protected override void DisposeManaged() { }
        }

        sealed class AudioFixture : IDisposable {
            readonly string directory = Path.Combine(Path.GetTempPath(), "OpenUtau-transcribe-clip-" + Guid.NewGuid());
            public UProject Project { get; } = new();
            public UWavePart Part { get; }

            public AudioFixture(int channels = 2, bool changingTempo = false, int seconds = 4) {
                Directory.CreateDirectory(directory);
                if (changingTempo) {
                    Project.tempos[0].bpm = 137;
                    Project.tempos.Add(new UTempo(700, 93));
                    Project.tempos.Add(new UTempo(1500, 171));
                    Project.timeAxis.BuildSegments(Project);
                }
                var samples = new float[44100 * seconds * channels];
                for (int frame = 0; frame < samples.Length / channels; frame++) {
                    for (int channel = 0; channel < channels; channel++) {
                        // Every section is audible and distinct. Silence slicing must
                        // not hide accidental inclusion of the original file's edges.
                        samples[frame * channels + channel] = 0.2f + frame % 997 * 0.0001f + channel * 0.1f;
                    }
                }
                Part = WritePart("source.wav", samples, channels);
                Part.position = 960;
                Part.skip = 960;
                Part.trim = 960;
                Part.Validate(new OpenUtau.Core.ValidateOptions(), Project, Project.tracks[0]);
            }

            public UWavePart WritePart(string name, float[] samples, int channels) {
                string path = Path.Combine(directory, name);
                using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, channels))) {
                    writer.WriteSamples(samples, 0, samples.Length);
                }
                var part = new UWavePart { FilePath = path };
                part.Load(Project);
                return part;
            }

            public void Dispose() => Directory.Delete(directory, recursive: true);
        }

        static float[] VisiblePcm(UProject project, UWavePart part) {
            int FrameAt(int tick) => (int)(project.timeAxis.TickPosToMsPos(tick) * part.sampleRate / 1000);
            int origin = FrameAt(part.position - part.skip);
            int start = FrameAt(part.position) - origin;
            int end = part.trim == 0 ? part.Samples.Length / part.channels : FrameAt(part.End) - origin;
            return part.Samples[(start * part.channels)..(end * part.channels)];
        }

        static float[] Mono(float[] interleaved, int channels) => Enumerable.Range(0, interleaved.Length / channels)
            .Select(frame => Enumerable.Range(0, channels).Select(channel => interleaved[frame * channels + channel]).Average())
            .ToArray();

        [Theory]
        [InlineData(1, false, false)]
        [InlineData(2, false, false)]
        [InlineData(1, true, false)]
        [InlineData(2, true, false)]
        [InlineData(1, false, true)]
        [InlineData(2, true, true)]
        public async Task ModelReceivesOnlyVisibleUnfadedAudio(int channels, bool changingTempo, bool batch) {
            using var fixture = new AudioFixture(channels, changingTempo);
            var project = fixture.Project;
            var wave = fixture.Part;
            await wave.Peaks;
            wave.fadein = wave.Duration;
            wave.fadeout = wave.Duration;
            var expected = Mono(VisiblePcm(project, wave), channels);
            using var extractor = new RecordingExtractor(batch);
            var progress = new List<(int processed, int total)>();

            var voice = extractor.Transcribe(project, wave, null,
                batch ? new MidiExtractor<Options>.BatchingStrategy { max_batch_size = 4 } : null,
                null, (processed, total) => progress.Add((processed, total)));

            Assert.NotNull(voice);
            Assert.Equal(expected, Assert.Single(extractor.Inputs));
            Assert.Equal(batch ? 1 : 0, extractor.BatchCalls);
            Assert.Equal(wave.position, voice.position);
            Assert.Equal(wave.Duration, voice.Duration);
            var note = Assert.Single(voice.notes);
            Assert.Equal(0, note.position);
            Assert.InRange(note.End, wave.Duration - 1, wave.Duration);
            Assert.Equal(((int)(expected.Length / 44100.0), (int)(expected.Length / 44100.0)), Assert.Single(progress));
        }

        [Fact]
        public async Task EachSplitSendsItsOwnSamplesAndPlacesNotesAtItsOwnStart() {
            using var fixture = new AudioFixture(changingTempo: true);
            var source = fixture.Part;
            var project = fixture.Project;
            await source.Peaks;
            var expected = Mono(VisiblePcm(project, source), source.channels);
            var (first, second) = source.Split(project, source.position + source.Duration / 2);
            await Task.WhenAll(first.Peaks, second.Peaks);
            var captured = new List<float>();

            foreach (var wave in new[] { first, second }) {
                using var extractor = new RecordingExtractor();
                var voice = extractor.Transcribe(project, wave, null, null, null, (_, _) => { });
                Assert.NotNull(voice);
                Assert.Equal(wave.position, voice.position);
                Assert.Equal(0, Assert.Single(voice.notes).position);
                Assert.All(voice.notes, note => Assert.True(note.End <= wave.Duration));
                captured.AddRange(Assert.Single(extractor.Inputs));
            }

            Assert.Equal(expected, captured);
        }

        [Fact]
        public async Task ModelTailPaddingCannotExtendTheCurrentClip() {
            using var fixture = new AudioFixture(changingTempo: true);
            await fixture.Part.Peaks;
            using var extractor = new RecordingExtractor(extendLastNote: true);

            var voice = extractor.Transcribe(fixture.Project, fixture.Part, null, null, null, (_, _) => { });

            Assert.NotNull(voice);
            Assert.Equal(fixture.Part.Duration, voice.Duration);
            var note = Assert.Single(voice.notes);
            Assert.Equal(0, note.position);
            Assert.Equal(voice.Duration, note.End);
        }

        [Fact]
        public async Task ShortClipFromLongFileDoesNotRequestLongAudioConfirmation() {
            using var fixture = new AudioFixture(channels: 1, seconds: 34);
            var wave = fixture.Part;
            var project = fixture.Project;
            await wave.Peaks;
            wave.trim += wave.Duration - 480;
            wave.Validate(new OpenUtau.Core.ValidateOptions(), project, project.tracks[0]);
            int confirmations = 0;
            using var extractor = new RecordingExtractor();

            var voice = extractor.Transcribe(project, wave, null, null,
                () => { confirmations++; return false; }, (_, _) => { });

            Assert.NotNull(voice);
            Assert.Equal(0, confirmations);
            Assert.Equal(22050, Assert.Single(extractor.Inputs).Length);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        public async Task PitchModelInputMatchesAFileContainingOnlyTheCurrentClip(int channels, bool changingTempo) {
            using var fixture = new AudioFixture(channels, changingTempo);
            var wave = fixture.Part;
            var project = fixture.Project;
            await wave.Peaks;
            wave.fadein = wave.Duration;
            wave.fadeout = wave.Duration;
            var standalone = fixture.WritePart("visible.wav", VisiblePcm(project, wave), channels);
            await standalone.Peaks;

            var actual = RmvpeTranscriber.PrepareInput(project, wave);
            var expected = RmvpeTranscriber.PrepareInput(project, standalone);

            Assert.NotEmpty(actual);
            Assert.Equal(expected, actual);
            Assert.True(actual.Length < (long)wave.Samples.Length / channels * 16000 / 44100);
        }
    }
}
