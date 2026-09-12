using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Core.Headless {
    public class HeadlessRenderCommandTest {
        [Theory]
        [InlineData("render", true)]
        [InlineData("RENDER", true)]
        [InlineData("open", false)]
        public void IsCommandDetectsRenderCommand(string command, bool expected) {
            Assert.Equal(expected, HeadlessRenderCommand.IsCommand(new[] { command }));
        }

        [Fact]
        public void IsCommandReturnsFalseForEmptyArgs() {
            Assert.False(HeadlessRenderCommand.IsCommand(Array.Empty<string>()));
        }

        [Fact]
        public void ParseRenderArgsParsesJobAndOptions() {
            var (job, options) = HeadlessRenderCommand.ParseRenderArgs(new[] {
                "-i", "song.ust",
                "--output=out.wav",
                "--singer", "Singer",
                "--renderer", "WORLDLINE-R",
                "--phonemizer", "JA VCV",
                "--resampler", "resampler.exe",
                "--wavtool", "wavtool.exe",
                "--singers-path", "Singers",
                "--onnx-runner", "cpu",
                "--onnx-gpu", "1",
                "--diffsinger-depth", "0.7",
                "--diffsinger-steps", "5",
                "--diffsinger-variance-steps", "6",
                "--diffsinger-pitch-steps", "7",
                "--diffsinger-tensor-cache", "false",
            });

            Assert.Equal("song.ust", job.InputPath);
            Assert.Equal("out.wav", job.OutputPath);
            Assert.Equal("Singer", job.Singer);
            Assert.Equal("WORLDLINE-R", job.Renderer);
            Assert.Equal("JA VCV", job.Phonemizer);
            Assert.Equal("resampler.exe", job.Resampler);
            Assert.Equal("wavtool.exe", job.Wavtool);
            Assert.Equal("Singers", options.SingersPath);
            Assert.Equal("CPU", options.OnnxRunner);
            Assert.Equal(1, options.OnnxGpu);
            Assert.Equal(0.7, options.DiffSingerDepth);
            Assert.Equal(5, options.DiffSingerSteps);
            Assert.Equal(6, options.DiffSingerVarianceSteps);
            Assert.Equal(7, options.DiffSingerPitchSteps);
            Assert.False(options.DiffSingerTensorCache);
        }

        public static IEnumerable<object[]> InvalidArguments => new[] {
            new object[] { new[] { "--output", "out.wav" }, "Missing required option: --input" },
            new object[] { new[] { "--input", "song.ust" }, "Missing required option: --output" },
            new object[] { new[] { "--unknown", "value" }, "Unknown option: --unknown" },
            new object[] { new[] { "song.ust" }, "Unexpected argument: song.ust" },
            new object[] { new[] { "--input", "--output", "out.wav" }, "Missing value for option: --input" },
            new object[] { new[] { "--input", "song.ust", "--output", "out.wav", "--onnx-runner", "missing" }, "Invalid value for --onnx-runner" },
            new object[] { new[] { "--input", "song.ust", "--output", "out.wav", "--onnx-gpu", "-1" }, "Missing value for option: --onnx-gpu" },
            new object[] { new[] { "--input", "song.ust", "--output", "out.wav", "--diffsinger-steps", "0" }, "Invalid value for --diffsinger-steps" },
            new object[] { new[] { "--input", "song.ust", "--output", "out.wav", "--diffsinger-depth", "-0.1" }, "Missing value for option: --diffsinger-depth" },
            new object[] { new[] { "--input", "song.ust", "--output", "out.wav", "--diffsinger-tensor-cache", "maybe" }, "Invalid value for --diffsinger-tensor-cache" },
        };

        [Theory]
        [MemberData(nameof(InvalidArguments))]
        public void ParseRenderArgsRejectsInvalidArguments(string[] args, string expectedMessage) {
            var ex = Assert.Throws<HeadlessRenderCommand.CommandLineException>(
                () => HeadlessRenderCommand.ParseRenderArgs(args));
            Assert.Contains(expectedMessage, ex.Message);
        }

        [Fact]
        public void ExpandRenderJobsCreatesSingleFilePlan() {
            WithTempDirectory(dir => {
                var input = Path.Join(dir, "song.ust");
                var output = Path.Join(dir, "song.wav");
                File.WriteAllText(input, string.Empty);

                var plan = HeadlessRenderCommand.ExpandRenderJobs(new RenderJob {
                    InputPath = input,
                    OutputPath = output,
                    Singer = "Singer",
                    Renderer = "WORLDLINE-R",
                    Phonemizer = "JA VCV",
                    Resampler = "resampler.exe",
                    Wavtool = "wavtool.exe",
                });

                Assert.False(plan.IsBatch);
                var job = Assert.Single(plan.Jobs);
                Assert.Equal(Path.GetFullPath(input), job.InputPath);
                Assert.Equal(Path.GetFullPath(output), job.OutputPath);
                Assert.Equal("Singer", job.Singer);
                Assert.Equal("WORLDLINE-R", job.Renderer);
                Assert.Equal("JA VCV", job.Phonemizer);
                Assert.Equal("resampler.exe", job.Resampler);
                Assert.Equal("wavtool.exe", job.Wavtool);
            });
        }

        [Fact]
        public void ExpandRenderJobsCreatesBatchPlanForKnownProjectFiles() {
            WithTempDirectory(dir => {
                var inputDir = Path.Join(dir, "input");
                var outputDir = Path.Join(dir, "output");
                Directory.CreateDirectory(inputDir);
                Directory.CreateDirectory(Path.Join(inputDir, "nested"));
                File.WriteAllText(Path.Join(inputDir, "b.ust"), string.Empty);
                File.WriteAllText(Path.Join(inputDir, "A.USTX"), string.Empty);
                File.WriteAllText(Path.Join(inputDir, "ignore.txt"), string.Empty);
                File.WriteAllText(Path.Join(inputDir, "nested", "nested.ust"), string.Empty);

                var plan = HeadlessRenderCommand.ExpandRenderJobs(new RenderJob {
                    InputPath = inputDir,
                    OutputPath = outputDir,
                    Singer = "Singer",
                });

                Assert.True(plan.IsBatch);
                Assert.Equal(new[] { "A.USTX", "b.ust" }, plan.Jobs.Select(job => Path.GetFileName(job.InputPath)));
                Assert.Equal(new[] { "A.wav", "b.wav" }, plan.Jobs.Select(job => Path.GetFileName(job.OutputPath)));
                Assert.All(plan.Jobs, job => Assert.Equal("Singer", job.Singer));
            });
        }

        [Fact]
        public void ExpandRenderJobsRejectsMissingInput() {
            WithTempDirectory(dir => {
                var ex = Assert.Throws<HeadlessRenderCommand.CommandLineException>(
                    () => HeadlessRenderCommand.ExpandRenderJobs(new RenderJob {
                        InputPath = Path.Join(dir, "missing"),
                        OutputPath = Path.Join(dir, "out.wav"),
                    }));
                Assert.Contains("Input project or directory not found", ex.Message);
            });
        }

        [Fact]
        public void ExpandRenderJobsRejectsEmptyBatchInputDirectory() {
            WithTempDirectory(dir => {
                var inputDir = Path.Join(dir, "input");
                Directory.CreateDirectory(inputDir);

                var ex = Assert.Throws<HeadlessRenderCommand.CommandLineException>(
                    () => HeadlessRenderCommand.ExpandRenderJobs(new RenderJob {
                        InputPath = inputDir,
                        OutputPath = Path.Join(dir, "output"),
                    }));
                Assert.Contains("No project files found", ex.Message);
            });
        }

        [Fact]
        public void ExpandRenderJobsRejectsOutputFileForBatch() {
            WithTempDirectory(dir => {
                var inputDir = Path.Join(dir, "input");
                var outputFile = Path.Join(dir, "output.wav");
                Directory.CreateDirectory(inputDir);
                File.WriteAllText(Path.Join(inputDir, "song.ust"), string.Empty);
                File.WriteAllText(outputFile, string.Empty);

                var ex = Assert.Throws<HeadlessRenderCommand.CommandLineException>(
                    () => HeadlessRenderCommand.ExpandRenderJobs(new RenderJob {
                        InputPath = inputDir,
                        OutputPath = outputFile,
                    }));
                Assert.Contains("Batch output must be a directory", ex.Message);
            });
        }

        [Fact]
        public void ExpandRenderJobsRejectsDuplicateBatchOutputs() {
            WithTempDirectory(dir => {
                var inputDir = Path.Join(dir, "input");
                Directory.CreateDirectory(inputDir);
                File.WriteAllText(Path.Join(inputDir, "Song.ust"), string.Empty);
                File.WriteAllText(Path.Join(inputDir, "song.ustx"), string.Empty);

                var ex = Assert.Throws<HeadlessRenderCommand.CommandLineException>(
                    () => HeadlessRenderCommand.ExpandRenderJobs(new RenderJob {
                        InputPath = inputDir,
                        OutputPath = Path.Join(dir, "output"),
                    }));
                Assert.Contains("Multiple input projects map to output path", ex.Message);
            });
        }

        [Theory]
        [InlineData("OpenUtau.Plugin.Builtin.JapaneseVCVPhonemizer")]
        [InlineData("Japanese VCV Phonemizer (legacy)")]
        [InlineData("JA VCV")]
        [InlineData("ja vcv")]
        [InlineData("JapaneseVCVPhonemizer")]
        public void ResolvePhonemizerMatchesRegisteredFactory(string value) {
            RegisterJapaneseVcvPhonemizer();

            var factory = HeadlessRenderer.ResolvePhonemizer(value);

            Assert.Equal(typeof(JapaneseVCVPhonemizer), factory.type);
        }

        [Fact]
        public void ResolvePhonemizerRejectsUnknownValue() {
            RegisterJapaneseVcvPhonemizer();

            var ex = Assert.Throws<HeadlessRenderException>(
                () => HeadlessRenderer.ResolvePhonemizer("Missing Phonemizer"));
            Assert.Contains("Phonemizer not found", ex.Message);
        }

        [Theory]
        [InlineData("worldline-r", "WORLDLINE-R")]
        [InlineData("DIFFSINGER", "DIFFSINGER")]
        public void ResolveRendererAcceptsSupportedRendererNames(string value, string expected) {
            Assert.Equal(expected, HeadlessRenderer.ResolveRenderer(value));
        }

        [Fact]
        public void ResolveRendererRejectsUnknownValue() {
            var ex = Assert.Throws<HeadlessRenderException>(
                () => HeadlessRenderer.ResolveRenderer("Missing Renderer"));
            Assert.Contains("Renderer not found", ex.Message);
        }

        [Fact]
        public void EnsureProjectReadyRejectsMissingSinger() {
            var project = CreateProject(USinger.CreateMissing("missing-singer"), new TestRenderer(USingerType.Classic, "WORLDLINE-R"));

            var ex = Assert.Throws<HeadlessRenderException>(
                () => HeadlessRenderer.EnsureProjectReady(project));

            Assert.Contains("Singer not found for track 1", ex.Message);
        }

        [Fact]
        public void EnsureProjectReadyRejectsMissingRenderer() {
            var project = CreateProject(new TestSinger(USingerType.Classic, "Classic Singer"), null);
            project.tracks[0].RendererSettings.renderer = "Missing Renderer";

            var ex = Assert.Throws<HeadlessRenderException>(
                () => HeadlessRenderer.EnsureProjectReady(project));

            Assert.Contains("Renderer not found for track 1: Missing Renderer", ex.Message);
        }

        [Fact]
        public void EnsureProjectReadyRejectsRendererSingerMismatch() {
            var project = CreateProject(
                new TestSinger(USingerType.Classic, "Classic Singer"),
                new TestRenderer(USingerType.DiffSinger, "DIFFSINGER"));

            var ex = Assert.Throws<HeadlessRenderException>(
                () => HeadlessRenderer.EnsureProjectReady(project));

            Assert.Contains("Renderer DIFFSINGER is not supported for singer Classic Singer.", ex.Message);
        }

        [Fact]
        public void EnsureProjectReadyRejectsRenderablePartWithoutRenderPhrases() {
            var project = CreateProject(
                new TestSinger(USingerType.Classic, "Classic Singer"),
                new TestRenderer(USingerType.Classic, "WORLDLINE-R"));
            var part = (UVoicePart)project.parts[0];
            var note = UNote.Create();
            note.lyric = "a";
            note.duration = 120;
            part.notes.Add(note);

            var ex = Assert.Throws<HeadlessRenderException>(
                () => HeadlessRenderer.EnsureProjectReady(project));

            Assert.Contains("No render phrases were generated", ex.Message);
        }

        private static UProject CreateProject(USinger singer, IRenderer renderer) {
            var project = new UProject();
            project.tracks[0].Singer = singer;
            project.tracks[0].RendererSettings = new URenderSettings {
                renderer = renderer?.ToString(),
                Renderer = renderer,
            };
            project.parts.Add(new UVoicePart {
                trackNo = 0,
                name = "Part",
            });
            return project;
        }

        private static void RegisterJapaneseVcvPhonemizer() {
            PhonemizerFactory.Get(typeof(JapaneseVCVPhonemizer));
            PhonemizerFactory.BuildList();
        }

        private static void WithTempDirectory(Action<string> action) {
            var dir = Path.Join(Path.GetTempPath(), "OpenUtauTest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                action(dir);
            } finally {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        private sealed class TestSinger : USinger {
            private readonly USingerType singerType;
            private readonly string name;

            public TestSinger(USingerType singerType, string name) {
                this.singerType = singerType;
                this.name = name;
                found = true;
                loaded = true;
            }

            public override string Id => name;
            public override string Name => name;
            public override USingerType SingerType => singerType;
        }

        private sealed class TestRenderer : IRenderer {
            private readonly string name;

            public TestRenderer(USingerType singerType, string name) {
                SingerType = singerType;
                this.name = name;
            }

            public USingerType SingerType { get; }
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => false;
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult { samples = Array.Empty<float>() };
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender = false) {
                return Task.FromResult(Layout(phrase));
            }
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
                return new RenderPitchResult {
                    ticks = Array.Empty<float>(),
                    tones = Array.Empty<float>(),
                };
            }
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
                return Array.Empty<UExpressionDescriptor>();
            }
            public override string ToString() => name;
        }
    }
}
