using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.Core.USTx {
    public class UWavePartSplitTest {
        sealed class AudioFixture : IDisposable {
            readonly string directory = Path.Combine(Path.GetTempPath(), "OpenUtau-wave-split-" + Guid.NewGuid());
            public UProject Project { get; }
            public UWavePart Part { get; }

            public AudioFixture(int channels = 2, bool changingTempo = false) {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "source.wav");
                // Neither the source length nor the cut positions land on whole ticks/frames.
                var samples = new float[176543 * channels];
                for (int i = 0; i < samples.Length; i++) {
                    samples[i] = (float)(0.7 * Math.Sin(i * 0.031) + 0.2 * Math.Cos(i * 0.017));
                }
                using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, channels))) {
                    writer.WriteSamples(samples, 0, samples.Length);
                }
                Project = new UProject { FilePath = Path.Combine(directory, "project.ustx") };
                Project.tempos[0].bpm = 137;
                if (changingTempo) {
                    Project.tempos.Add(new UTempo(700, 93));
                    Project.tempos.Add(new UTempo(1500, 171));
                }
                Project.timeAxis.BuildSegments(Project);
                Part = new UWavePart {
                    FilePath = path,
                    position = 321,
                    skip = 123,
                    trim = 87,
                    comment = "Imported audio",
                    trackNo = 0,
                };
                Part.Load(Project);
            }

            public void Dispose() => Directory.Delete(directory, recursive: true);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        public async Task PreservesPlacementMetadataAndSharesLoadedAudio(int channels, bool changingTempo) {
            using var fixture = new AudioFixture(channels, changingTempo);
            var project = fixture.Project;
            var source = fixture.Part;
            var peaks = await source.Peaks;
            source.BeforeSave(project, project.tracks[0]);
            int cut = source.position + source.Duration / 3;
            int originalEnd = source.End;

            var (first, second) = source.Split(project, cut);

            Assert.Equal(source.position, first.position);
            Assert.Equal(cut, first.End);
            Assert.Equal(cut, second.position);
            Assert.Equal(originalEnd, second.End);
            Assert.Equal(source.Duration, first.Duration + second.Duration);
            Assert.Equal(source.skip, first.skip);
            Assert.Equal(source.trim + originalEnd - cut, first.trim);
            Assert.Equal(source.skip + cut - source.position, second.skip);
            Assert.Equal(source.trim, second.trim);
            foreach (var part in new[] { first, second }) {
                Assert.Equal(source.name, part.name);
                Assert.Equal(source.comment, part.comment);
                Assert.Equal(source.trackNo, part.trackNo);
                Assert.Equal(source.FilePath, part.FilePath);
                Assert.Equal(source.relativePath, part.relativePath);
                Assert.Equal(source.position - source.skip, part.position - part.skip);
                Assert.Equal(source.channels, part.channels);
                Assert.Equal(source.sampleRate, part.sampleRate);
                Assert.Same(source.Samples, part.Samples);
                Assert.Same(peaks, await part.Peaks);
                Assert.NotEqual(source.Id, part.Id);
            }
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(originalEnd, source.End);
        }

        [Theory]
        [InlineData(1, false, false)]
        [InlineData(2, false, false)]
        [InlineData(1, true, false)]
        [InlineData(2, true, false)]
        [InlineData(1, false, true)]
        [InlineData(2, true, true)]
        public async Task RepeatedSplitsPreserveEverySample(int channels, bool changingTempo, bool fades) {
            using var fixture = new AudioFixture(channels, changingTempo);
            var project = fixture.Project;
            var source = fixture.Part;
            await source.Peaks;
            if (fades) {
                // Cuts inside both fades, including an overlap between their envelopes.
                source.fadein = source.Duration * 2 / 3;
                source.fadeout = source.Duration * 2 / 3;
            }
            var raw = source.Samples.ToArray();
            var expected = source.GetTrimmedSamples(project).pcm;
            int firstCut = source.position + source.Duration / 3;
            int secondCut = source.position + source.Duration * 2 / 3;

            var (first, remainder) = source.Split(project, firstCut);
            var (middle, last) = remainder.Split(project, secondCut);
            await Task.WhenAll(first.Peaks, middle.Peaks, last.Peaks);
            var actual = new[] { first, middle, last }
                .SelectMany(part => part.GetTrimmedSamples(project).pcm).ToArray();

            Assert.Equal(expected, actual);
            Assert.Equal(raw, source.Samples);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task UntrimmedSourceKeepsItsFinalPartialTick(int channels) {
            using var fixture = new AudioFixture(channels);
            var project = fixture.Project;
            var source = fixture.Part;
            await source.Peaks;
            source.skip = 0;
            source.trim = 0;
            source.Validate(new ValidateOptions(), project, project.tracks[0]);
            Assert.Equal(source.Samples, source.GetTrimmedSamples(project).pcm);

            var (first, second) = source.Split(project, source.End - 1);
            await Task.WhenAll(first.Peaks, second.Peaks);

            Assert.Equal(source.Samples,
                first.GetTrimmedSamples(project).pcm.Concat(second.GetTrimmedSamples(project).pcm).ToArray());
        }

        [Theory]
        [InlineData(1, false, false)]
        [InlineData(2, false, true)]
        [InlineData(1, true, true)]
        [InlineData(2, true, true)]
        public async Task PlaybackSlotsMixIdenticallyBeforeAndAfterSplit(int channels, bool changingTempo, bool fades) {
            using var fixture = new AudioFixture(channels, changingTempo);
            var project = fixture.Project;
            var source = fixture.Part;
            await source.Peaks;
            if (fades) {
                source.fadein = source.Duration * 2 / 3;
                source.fadeout = source.Duration * 2 / 3;
            }
            SampleSlot Slot(UWavePart part) {
                var audio = part.GetTrimmedSamples(project);
                return new SampleSlot(audio.offsetMs, audio.estimatedLengthMs, audio.channels,
                    audio.pcm.Freeze(), SlotState.Ready);
            }
            var original = Slot(source);
            // The file origin and this cut have fractional sample positions; with
            // changing tempo the cut is also exactly on the first tempo boundary.
            var (first, second) = source.Split(project, 700);
            await Task.WhenAll(first.Peaks, second.Peaks);
            int stereoCount = (original.Offset + original.Data.Length) * (2 / channels) + 128;
            var before = new float[stereoCount];
            var after = new float[stereoCount];

            original.Mix(0, before, 0, before.Length);
            Slot(first).Mix(0, after, 0, after.Length);
            Slot(second).Mix(0, after, 0, after.Length);

            Assert.Equal(before, after);
        }

        [Fact]
        public async Task SplitWhileLoadingPublishesSamplesBeforePeaksComplete() {
            using var fixture = new AudioFixture();
            var source = fixture.Part;
            var loading = source.Peaks;
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.Peaks = HoldLoading(loading, release.Task);
            var (first, second) = source.Split(fixture.Project, source.position + source.Duration / 2);
            try {
                Assert.False(first.Peaks.IsCompleted);
                Assert.False(second.Peaks.IsCompleted);
            } finally {
                release.TrySetResult(true);
                await Task.WhenAll(source.Peaks, first.Peaks, second.Peaks);
            }

            Assert.NotNull(first.Samples);
            Assert.NotNull(second.Samples);
            Assert.Same(source.Samples, first.Samples);
            Assert.Same(source.Samples, second.Samples);
            Assert.Same(await source.Peaks, await first.Peaks);
            Assert.Same(await source.Peaks, await second.Peaks);
            Assert.Equal(source.GetTrimmedSamples(fixture.Project).pcm,
                first.GetTrimmedSamples(fixture.Project).pcm.Concat(second.GetTrimmedSamples(fixture.Project).pcm).ToArray());
        }

        static async Task<WavePeaks> HoldLoading(Task<WavePeaks> loading, Task release) {
            await release;
            return await loading;
        }

        [Fact]
        public async Task SerializedSplitsKeepTheirTrimmingAndFadeEnvelopes() {
            using var fixture = new AudioFixture(changingTempo: true);
            var project = fixture.Project;
            var source = fixture.Part;
            await source.Peaks;
            source.fadein = source.Duration * 2 / 3;
            source.fadeout = source.Duration * 2 / 3;
            var expected = source.GetTrimmedSamples(project).pcm;
            var (first, second) = source.Split(project, source.position + source.Duration / 2);
            var reloaded = new UWavePart[2];
            var originals = new[] { first, second };
            for (int i = 0; i < originals.Length; i++) {
                var part = originals[i];
                part.BeforeSave(project, project.tracks[0]);
                reloaded[i] = Yaml.DefaultDeserializer.Deserialize<UWavePart>(Yaml.DefaultSerializer.Serialize(part));
                reloaded[i].AfterLoad(project, project.tracks[0]);
                await reloaded[i].Peaks;
                Assert.Equal(part.position, reloaded[i].position);
                Assert.Equal(part.End, reloaded[i].End);
                Assert.Equal(part.skip, reloaded[i].skip);
                Assert.Equal(part.trim, reloaded[i].trim);
                Assert.Equal(part.fadeinOffset, reloaded[i].fadeinOffset);
                Assert.Equal(part.fadeoutOffset, reloaded[i].fadeoutOffset);
            }

            Assert.Equal(expected, reloaded.SelectMany(part => part.GetTrimmedSamples(project).pcm).ToArray());
        }

        [Fact]
        public async Task CloningSplitPartPreservesIdentityMetadataAndFadeEnvelopes() {
            using var fixture = new AudioFixture(changingTempo: true);
            var project = fixture.Project;
            var source = fixture.Part;
            await source.Peaks;
            source.fadein = source.Duration * 2 / 3;
            source.fadeout = source.Duration * 2 / 3;
            var (_, remainder) = source.Split(project, source.position + source.Duration / 3);
            var (middle, _) = remainder.Split(project, source.position + source.Duration * 2 / 3);
            await middle.Peaks;
            middle.name = "Middle clip";
            middle.trackNo = 3;
            middle.BeforeSave(project, project.tracks[0]);

            var clone = Assert.IsType<UWavePart>(middle.Clone());
            await clone.Peaks;

            Assert.Equal(middle.Id, clone.Id);
            Assert.Equal(middle.name, clone.name);
            Assert.Equal(middle.comment, clone.comment);
            Assert.Equal(middle.trackNo, clone.trackNo);
            Assert.Equal(middle.FilePath, clone.FilePath);
            Assert.Equal(middle.relativePath, clone.relativePath);
            Assert.Equal(middle.fileDurationMs, clone.fileDurationMs);
            Assert.Equal(middle.position, clone.position);
            Assert.Equal(middle.Duration, clone.Duration);
            Assert.Equal(middle.skip, clone.skip);
            Assert.Equal(middle.trim, clone.trim);
            Assert.Equal(middle.fadein, clone.fadein);
            Assert.Equal(middle.fadeout, clone.fadeout);
            Assert.True(clone.fadeinOffset > 0 && clone.fadeoutOffset > 0);
            Assert.Equal(middle.fadeinOffset, clone.fadeinOffset);
            Assert.Equal(middle.fadeoutOffset, clone.fadeoutOffset);
            Assert.Equal(middle.channels, clone.channels);
            Assert.Equal(middle.sampleRate, clone.sampleRate);
            Assert.Same(middle.Samples, clone.Samples);
            Assert.Same(await middle.Peaks, await clone.Peaks);
            Assert.Equal(middle.GetTrimmedSamples(project).pcm, clone.GetTrimmedSamples(project).pcm);
        }

        [Fact]
        public async Task EditingSplitPartFadesResetsOffsetsAndUndoRestoresAudio() {
            using var fixture = new AudioFixture(changingTempo: true);
            var project = fixture.Project;
            var source = fixture.Part;
            await source.Peaks;
            source.fadein = source.Duration * 2 / 3;
            source.fadeout = source.Duration * 2 / 3;
            var (_, remainder) = source.Split(project, source.position + source.Duration / 3);
            var (middle, _) = remainder.Split(project, source.position + source.Duration * 2 / 3);
            await middle.Peaks;
            int originalInOffset = middle.fadeinOffset;
            int originalOutOffset = middle.fadeoutOffset;
            Assert.True(originalInOffset > 0 && originalOutOffset > 0);
            var originalPcm = middle.GetTrimmedSamples(project).pcm;
            int newFade = middle.Duration / 4;
            var fadeIn = new PartFadeInCommand(project, middle, newFade);
            var fadeOut = new PartFadeOutCommand(project, middle, newFade);

            fadeIn.Execute();
            Assert.Equal(newFade, middle.fadein);
            Assert.Equal(0, middle.fadeinOffset);
            Assert.Equal(originalOutOffset, middle.fadeoutOffset);
            var afterFadeIn = middle.GetTrimmedSamples(project).pcm;
            fadeOut.Execute();
            Assert.Equal(newFade, middle.fadeout);
            Assert.Equal(0, middle.fadeoutOffset);

            fadeOut.Unexecute();
            Assert.Equal(source.fadeout, middle.fadeout);
            Assert.Equal(originalOutOffset, middle.fadeoutOffset);
            Assert.Equal(afterFadeIn, middle.GetTrimmedSamples(project).pcm);
            fadeIn.Unexecute();
            Assert.Equal(source.fadein, middle.fadein);
            Assert.Equal(originalInOffset, middle.fadeinOffset);
            Assert.Equal(originalPcm, middle.GetTrimmedSamples(project).pcm);
        }

        [Fact]
        public async Task RejectsCutsOutsideTheInteriorWithoutChangingSource() {
            using var fixture = new AudioFixture();
            var source = fixture.Part;
            await source.Peaks;
            int originalPosition = source.position;
            int originalEnd = source.End;
            foreach (int cut in new[] { source.position - 1, source.position, source.End, source.End + 1 }) {
                Assert.Throws<ArgumentOutOfRangeException>(() => source.Split(fixture.Project, cut));
            }
            Assert.Equal(originalPosition, source.position);
            Assert.Equal(originalEnd, source.End);
        }
    }
}
