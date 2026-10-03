using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.ExpressionGraph {
    public class ExpressionGraphTest {
        const string Renderer = "TEST";

        [Fact]
        public void PhraseIndexIsLazyAndShared() {
            var (project, track, part) = Fixture(null);
            var source = PhraseSource.FromPart(project, track, part, 0);
            var contexts = source.PhraseGroups.Select(g => new GraphContext(source, phraseStart: g.Start)).ToArray();
            Assert.False(source.PhraseNoteIndex.IsValueCreated);
            Parallel.ForEach(contexts, context => context.PhraseNoteAt(0));
            Assert.True(source.PhraseNoteIndex.IsValueCreated);
            var index = source.PhraseNoteIndex.Value;
            foreach (var context in contexts) {
                context.PhraseNoteAt(1200);
                Assert.Same(index, source.PhraseNoteIndex.Value);
            }
            var other = PhraseSource.FromPart(project, track, part, 0);
            Assert.False(other.PhraseNoteIndex.IsValueCreated);
            Assert.NotSame(index, other.PhraseNoteIndex.Value);
        }

        [Theory]
        [InlineData("10")]
        [InlineData("-10")]
        public void CubicFollowsMovingTargets(string slope) {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Time),
                Node(2, GraphNodeTypes.Multiply, ("b", slope)),
                Node(3, GraphNodeTypes.Slew, ("speed", "2"), ("easing", "cubic")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2, "a"), Link(2, 3), Link(3, 4));
            var (project, track, part) = Fixture(graph);
            part.position = 0;
            var context = new GraphContext(PhraseSource.FromPart(project, track, part, 0));
            float[] Run(int step) {
                var ticks = Enumerable.Range(0, 960 / step + 1).Select(i => i * step).ToArray();
                var values = Evaluate(graph, project, track, part, ticks)["tenc"];
                for (int i = 1; i < values.Length; i++) {
                    double dt = (context.Axis.TickPosToMsPos(ticks[i]) - context.Axis.TickPosToMsPos(ticks[i - 1])) / 1000;
                    Assert.InRange(Math.Abs(values[i] - values[i - 1]), 0, 2 * dt + 0.000001);
                }
                return values;
            }
            var coarse = Run(10);
            var fine = Run(1);
            double seconds = context.Axis.TickPosToMsPos(960) / 1000;
            Assert.InRange(Math.Abs(fine.Last()), seconds, 2 * seconds + 0.00001);
            // Retargeting is sampled, so trajectories need not match exactly, but a
            // tenfold denser grid must stay within 10% of the maximum travel, not freeze.
            Assert.InRange(Math.Abs(coarse.Last() - fine.Last()), 0, 0.1 * 2 * seconds);
            Assert.Equal(fine, Run(1));
        }

        [Fact]
        public void PhraseCounterResetsAndCountsExtensionNotes() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.PhraseNotes),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "cstm")),
                Node(4, GraphNodeTypes.PhonemeOutput, ("abbr", "gen")),
            }, Link(1, 2), new UGraphLink { from = 1, fromPort = "count", to = 3, toPort = "value" }, Link(1, 4));
            var (project, track, part) = Fixture(graph);
            var source = PhraseSource.FromPart(project, track, part, 0);
            Assert.Equal(2, source.PhraseGroups.Length);
            var program = ExpressionGraphProgram.Compile(graph, out var error);
            Assert.NotNull(program);
            var context = new GraphContext(source);
            var result = program.Evaluate(context, new[] { 0, 480, 1000, 1200, 1680 });
            Assert.Equal(new float[] { 0, 1, -1, 0, 1 }, result["tenc"]);
            Assert.Equal(new float[] { 2, 2, 0, 2, 2 }, result["cstm"]);
            Assert.Equal(new float[] { 0, 1, 0, 1 }, program.EvaluatePhonemes(context)["gen"]);
            // The active render phrase keeps its count through leading/trailing padding.
            var active = new GraphContext(source, phraseStart: source.PhraseGroups[1].Start);
            Assert.Equal((-1, 2), active.PhraseNoteAt(1000));
            Assert.Equal((0, 2), active.PhraseNoteAt(1200));
            // A phoneme before its note still uses the owning note's index.
            Assert.Equal((0, 2), active.PhraseNoteAt(1100, source.PhraseGroups[1].Start));
            Assert.Equal((1, 2), active.PhraseNoteAt(1680));
            Assert.NotEmpty(source.BuildPhrases());
        }

        [Fact]
        public void RateSpeedCanBeConnected() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Lfo, ("rate", "1"), ("unit", "beat"), ("shape", "square")),
                Node(2, GraphNodeTypes.Slew, ("speed", "100")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(4, GraphNodeTypes.Constant, ("value", "0")),
            }, Link(1, 2), Link(2, 3), Link(4, 2, "speed"));
            var (project, track, part) = Fixture(graph);
            part.position = 0;
            var ticks = new[] { 0, project.resolution / 2 };
            Assert.Equal(new float[] { 1, 1 }, Evaluate(graph, project, track, part, ticks)["tenc"]);
            graph.nodes[3].Set("value", "10000");
            Assert.Equal(new float[] { 1, -1 }, Evaluate(graph, project, track, part, ticks)["tenc"]);
        }

        [Fact]
        public void RandomRangeIsStableAndBounded() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.RandomRange, ("min", "10"), ("max", "-5"),
                    ("seed", "42"), ("rate", "1"), ("unit", "beat")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2));
            var (project, track, part) = Fixture(graph);
            part.position = 0;
            var ticks = new[] { -project.resolution, 0, 1, project.resolution, project.resolution * 2 };
            var result = Evaluate(graph, project, track, part, ticks)["tenc"];
            Assert.All(result, x => Assert.InRange(x, -5, 10));
            Assert.Equal(result[1], result[2]);
            Assert.NotEqual(result[0], result[1]);
            Assert.Equal(result, Evaluate(graph, project, track, part, ticks)["tenc"]);
            Assert.Equal(result.Skip(3), Evaluate(graph, project, track, part, ticks.Skip(3).ToArray())["tenc"]);
            graph.nodes[0].Set("seed", "43");
            Assert.NotEqual(result[1], Evaluate(graph, project, track, part, ticks)["tenc"][1]);
            graph.nodes[0].Set("rate", "0");
            Assert.Single(Evaluate(graph, project, track, part, ticks)["tenc"].Distinct());
            graph.nodes[0].Set("min", "10");
            graph.nodes[0].Set("max", "10");
            Assert.All(Evaluate(graph, project, track, part, ticks)["tenc"], x => Assert.Equal(10, x));
        }

        [Theory]
        [InlineData("linear")]
        [InlineData("cubic")]
        public void RateLimiterRespectsSpeed(string easing) {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Lfo, ("rate", "0.25"), ("unit", "beat"), ("shape", "square")),
                Node(2, GraphNodeTypes.Slew, ("speed", "4"), ("easing", easing)),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2), Link(2, 3));
            var (project, track, part) = Fixture(graph);
            part.position = 0;
            var ticks = Enumerable.Range(0, 401).Select(i => i * 10).ToArray();
            var result = Evaluate(graph, project, track, part, ticks)["tenc"];
            var context = new GraphContext(PhraseSource.FromPart(project, track, part, 0));
            for (int i = 1; i < ticks.Length; i++) {
                double dt = (context.Axis.TickPosToMsPos(ticks[i]) - context.Axis.TickPosToMsPos(ticks[i - 1])) / 1000;
                Assert.InRange(Math.Abs(result[i] - result[i - 1]), 0, 4 * dt + 0.000001);
                Assert.InRange(result[i], -1, 1);
            }
            Assert.Contains(-1f, result);
            Assert.Contains(result, x => x > -1 && x < 1);
            Assert.Equal(result, Evaluate(graph, project, track, part, ticks)["tenc"]);
            Assert.Empty(Evaluate(graph, project, track, part, Array.Empty<int>())["tenc"]);
            graph.nodes[1].Set("speed", "0");
            Assert.All(Evaluate(graph, project, track, part, ticks)["tenc"], x => Assert.Equal(1, x));
        }

        [Theory]
        [InlineData("and", "-2", "3", 1)]
        [InlineData("and", "0", "3", 0)]
        [InlineData("or", "0", "-3", 1)]
        [InlineData("or", "0", "0", 0)]
        public void LogicUsesNonzeroAsTrue(string type, string a, string b, float expected) {
            var graph = Graph(new[] {
                Node(1, type, ("a", a), ("b", b)),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2));
            var (project, track, part) = Fixture(graph);
            Assert.Equal(new[] { expected, expected }, Evaluate(graph, project, track, part, new[] { 0, 10 })["tenc"]);
        }

        [Theory]
        [InlineData("less", 0)]
        [InlineData("less_equal", 1)]
        [InlineData("greater", 0)]
        [InlineData("greater_equal", 1)]
        [InlineData("equal", 1)]
        [InlineData("not_equal", 0)]
        public void CompareSelectsBranches(string op, int expected) {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Compare, ("a", "2"), ("b", "2"), ("operator", op)),
                Node(2, GraphNodeTypes.IfElse, ("then", "60"), ("else", "-20")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(4, GraphNodeTypes.Not),
                Node(5, GraphNodeTypes.CurveOutput, ("abbr", "other")),
            }, Link(1, 2, "condition"), Link(2, 3), Link(1, 4), Link(4, 5));
            var (project, track, part) = Fixture(graph);
            var result = Evaluate(graph, project, track, part, new[] { 0 });
            Assert.Equal(expected == 1 ? 60 : -20, result["tenc"][0]);
            Assert.Equal(1 - expected, result["other"][0]);
        }

        [Theory]
        [InlineData("sine", 0, 1, 0, -1)]
        [InlineData("triangle", -1, 0, 1, 0)]
        [InlineData("square", 1, 1, -1, -1)]
        [InlineData("saw", -1, -0.5f, 0, 0.5f)]
        public void LfoShapesFollowProjectBeats(string shape, float p0, float p1, float p2, float p3) {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Lfo, ("unit", "beat"), ("rate", "1"), ("shape", shape)),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2));
            var (project, track, part) = Fixture(graph);
            part.position = 0;
            var ticks = new[] { 0, project.resolution / 4, project.resolution / 2, project.resolution * 3 / 4 };
            var result = Evaluate(graph, project, track, part, ticks)["tenc"];
            var expected = new[] { p0, p1, p2, p3 };
            for (int i = 0; i < result.Length; i++) Assert.Equal(expected[i], result[i], 5);
            Assert.Equal(result.Skip(2), Evaluate(graph, project, track, part, ticks.Skip(2).ToArray())["tenc"]);
        }

        [Fact]
        public void SmoothingUsesElapsedTimeAndHasNoSharedState() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Lfo, ("unit", "beat"), ("rate", "1"), ("shape", "square")),
                Node(2, GraphNodeTypes.Smooth, ("attack_ms", "100"), ("release_ms", "200")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2), Link(2, 3));
            var (project, track, part) = Fixture(graph);
            part.position = 0;
            var ticks = new[] { 0, project.resolution / 2, project.resolution };
            var program = ExpressionGraphProgram.Compile(graph, out var error);
            Assert.NotNull(program);
            var context = new GraphContext(PhraseSource.FromPart(project, track, part, 0));
            var result = program.Evaluate(context, ticks)["tenc"];
            double dt = context.Axis.TickPosToMsPos(ticks[1]) - context.Axis.TickPosToMsPos(0);
            Assert.Equal(1, result[0]);
            Assert.Equal((float)(-1 + 2 * Math.Exp(-dt / 200)), result[1], 5);
            Assert.Equal((float)(1 + (result[1] - 1) * Math.Exp(-dt / 100)), result[2], 5);
            Assert.Equal(result, program.Evaluate(context, ticks)["tenc"]);
            Assert.Empty(program.Evaluate(context, Array.Empty<int>())["tenc"]);
            graph.nodes[1].Set("attack_ms", "0");
            graph.nodes[1].Set("release_ms", "0");
            Assert.Equal(new float[] { 1, -1, 1 }, Evaluate(graph, project, track, part, ticks)["tenc"]);
        }

        [Fact]
        public void TimeIncludesPartPosition() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Time, ("unit", "beat")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2));
            var (project, track, part) = Fixture(graph);
            part.position = project.resolution;
            Assert.Equal(new float[] { 1, 2 }, Evaluate(graph, project, track, part, new[] { 0, project.resolution })["tenc"]);
        }

        // Reads every expression of the phrase fixture.
        class CurveRenderer : IRenderer {
            public USingerType SingerType => USingerType.Classic;
            public bool SupportsRenderPitch => false;
            public bool SupportsExpression(UExpressionDescriptor descriptor) => true;
            public RenderResult Layout(RenderPhrase phrase) => new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
            public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
                    System.Threading.CancellationTokenSource cancellation, bool isPreRender = false,
                    RenderPhraseEvents? renderEvents = null) => throw new NotImplementedException();
            public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;
            public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) =>
                Array.Empty<UExpressionDescriptor>();
        }

        static UGraphNode Node(int id, string type, params (string key, string value)[] parameters) {
            var node = new UGraphNode { id = id, type = type };
            foreach (var (key, value) in parameters) {
                node.Set(key, value);
            }
            return node;
        }

        static UGraphLink Link(int from, int to, string port = GraphNodeTypes.Value) =>
            new UGraphLink { from = from, to = to, toPort = port };

        static UExpressionGraph Graph(IEnumerable<UGraphNode> nodes, params UGraphLink[] links) =>
            new UExpressionGraph { id = "g", name = "G", renderer = Renderer, nodes = nodes.ToList(), links = links.ToList() };

        /// <summary>
        /// The phrase fixture, plus a numeric flag and an options flag, and per-phoneme values:
        /// volume 80 on the first phoneme, gender 20 on the second and the "Y" option on the third and the fourth,
        /// which extends the third's note.
        /// </summary>
        static (UProject project, UTrack track, UVoicePart part) Fixture(UExpressionGraph graph) {
            var (project, track, part) = PhraseSourceHashTest.BuildFixture(new CurveRenderer());
            track.RendererSettings.renderer = Renderer;
            project.RegisterExpression(new UExpressionDescriptor("gender", "gen", -100, 100, 0, "g"));
            project.RegisterExpression(new UExpressionDescriptor("growl", "grw", true, new[] { "", "Y" }));
            project.RegisterExpression(new UExpressionDescriptor("pitch override", "pito", 2400, 10800, 6000) {
                type = UExpressionType.MaskedCurve,
            });
            project.RegisterExpression(new UExpressionDescriptor("rendered pitch", "rpit", 2400, 10800, 6000) {
                type = UExpressionType.MaskedCurve,
            });
            var notes = part.notes.ToArray();
            notes[0].phonemeExpressions.Add(new UExpression(project.expressions["vol"]) { index = 0, value = 80 });
            notes[1].phonemeExpressions.Add(new UExpression(project.expressions["gen"]) { index = 0, value = 20 });
            notes[2].phonemeExpressions.Add(new UExpression(project.expressions["grw"]) { index = 0, value = 1 });
            foreach (var phoneme in part.phonemes) {
                phoneme.Validate(new ValidateOptions(), project, track, part, phoneme.Parent);
            }
            if (graph != null) {
                project.expressionGraphs = new List<UExpressionGraph> { graph };
                project.defaultExpressionGraphs = new Dictionary<string, string> { [Renderer] = graph.id };
            }
            return (project, track, part);
        }

        static Dictionary<string, float[]> Evaluate(UExpressionGraph graph, UProject project, UTrack track, UVoicePart part, int[] ticks) {
            var program = ExpressionGraphProgram.Compile(graph, out var error);
            Assert.True(program != null, error);
            var source = PhraseSource.FromPart(project, track, part, 0);
            return program!.Evaluate(new GraphContext(source), ticks);
        }

        [Fact]
        public void NodesAreStoredAsFlatMappings() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "pwr")),
                Node(2, GraphNodeTypes.MapRange, ("in_min", "0"), ("in_max", "100"), ("out_min", "-20"), ("out_max", "20"), ("clamp", "true")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                // A node from a newer version, with a parameter that needs quoting.
                Node(4, "future_node", ("label", "a: b"), ("empty", "")),
            }, Link(1, 2), Link(2, 3));
            graph.nodes[0].x = 40;
            graph.nodes[0].y = 80.5f;

            var yaml = Yaml.DefaultSerializer.Serialize(graph);
            Assert.Contains("{id: 1, type: curve_input, abbr: pwr, x: 40, y: 80.5}", yaml);
            Assert.Contains("{id: 2, type: map_range, in_min: 0, in_max: 100, out_min: -20, out_max: 20, clamp: true, x: 0, y: 0}", yaml);
            Assert.Contains("{from: 1, to: 2, to_port: value}", yaml);

            var read = Yaml.DefaultDeserializer.Deserialize<UExpressionGraph>(yaml);
            Assert.Equal(yaml, Yaml.DefaultSerializer.Serialize(read));
            Assert.Equal("a: b", read.nodes[3].GetString("label"));
            Assert.Equal("", read.nodes[3].GetString("empty"));
            Assert.Equal(80.5f, read.nodes[0].y);
        }

        [Fact]
        public void ProjectsKeepTheirGraphs() {
            var (project, track, _) = Fixture(Graph(new[] { Node(1, GraphNodeTypes.Constant, ("value", "1")) }));
            track.ExpressionGraph = "g";
            var yaml = Yaml.DefaultSerializer.Serialize(project);
            var read = Yaml.DefaultDeserializer.Deserialize<UProject>(yaml);
            Assert.Equal("g", Assert.Single(read.expressionGraphs).id);
            Assert.Equal("g", read.defaultExpressionGraphs[Renderer]);
            Assert.Equal("g", read.tracks[0].ExpressionGraph);

            // A project without graphs doesn't mention them.
            yaml = Yaml.DefaultSerializer.Serialize(new UProject());
            Assert.DoesNotContain("expression_graph", yaml);
        }

        [Fact]
        public void BrokenGraphsDoNotCompile() {
            string Error(UExpressionGraph graph) {
                Assert.Null(ExpressionGraphProgram.Compile(graph, out var error));
                return error;
            }
            var input = Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn"));
            Assert.Contains("unknown type", Error(Graph(new[] { input, Node(2, "future_node") })));
            Assert.Contains("no input", Error(Graph(new[] { input, Node(2, GraphNodeTypes.Abs) }, Link(1, 2, "x"))));
            Assert.Contains("missing node", Error(Graph(new[] { input }, Link(1, 9))));
            Assert.Contains("two links", Error(Graph(
                new[] { input, Node(2, GraphNodeTypes.Constant), Node(3, GraphNodeTypes.Abs) },
                Link(1, 3), Link(2, 3))));
            Assert.Contains("Two outputs", Error(Graph(new[] {
                input,
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2), Link(1, 3))));
            Assert.Contains("cycle", Error(Graph(
                new[] { Node(1, GraphNodeTypes.Add), Node(2, GraphNodeTypes.Abs) },
                Link(1, 2), Link(2, 1, "a"))));
        }

        [Fact]
        public void RefusesLinksThatCloseACycle() {
            var graph = Graph(
                new[] { Node(1, GraphNodeTypes.Abs), Node(2, GraphNodeTypes.Abs), Node(3, GraphNodeTypes.Abs) },
                Link(1, 2), Link(2, 3));
            Assert.True(ExpressionGraphProgram.WouldCreateCycle(graph, 3, 1));
            Assert.True(ExpressionGraphProgram.WouldCreateCycle(graph, 2, 2));
            Assert.False(ExpressionGraphProgram.WouldCreateCycle(graph, 1, 3));
        }

        [Fact]
        public void MapsACurveToAnother() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn")),
                Node(2, GraphNodeTypes.MapRange, ("in_min", "-12"), ("in_max", "0"), ("out_min", "0"), ("out_max", "100"), ("clamp", "true")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2), Link(2, 3));
            var (project, track, part) = Fixture(graph);
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 480, 960, 1920, 3000 });
            Assert.Equal(new[] { "tenc" }, outputs.Keys);
            Assert.Equal(new[] { 50f, 75f, 100f, 0f, 100f }, outputs["tenc"]);
        }

        [Fact]
        public void MixBlendsByTheFactor() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn")),
                Node(2, GraphNodeTypes.MapRange, ("in_min", "-12"), ("in_max", "-6")),
                Node(3, GraphNodeTypes.Mix, ("a", "10"), ("b", "20")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(5, GraphNodeTypes.Mix, ("a", "10"), ("b", "20")),
                Node(6, GraphNodeTypes.CurveOutput, ("abbr", "genc")),
            }, Link(1, 2), Link(2, 3, "factor"), Link(3, 4), Link(5, 6));
            var (project, track, part) = Fixture(graph);
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 960, 1920 });
            Assert.Equal(new[] { 20f, 20f, 10f }, outputs["tenc"]);
            Assert.Equal(new[] { 15f, 15f, 15f }, outputs["genc"]);
        }

        [Fact]
        public void UnconnectedPortsTakeTheirParameter() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Constant, ("value", "10")),
                Node(2, GraphNodeTypes.Multiply, ("b", "0.5")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "genc")),
            }, Link(1, 2, "a"), Link(2, 3));
            var (project, track, part) = Fixture(graph);
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 5 });
            Assert.Equal(new[] { 5f, 5f }, outputs["tenc"]);
            Assert.False(outputs.ContainsKey("genc"));
        }

        [Fact]
        public void TimeNodes() {
            var (project, track, part) = Fixture(null);
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Lfo, ("rate", "1")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "a")),
                Node(3, GraphNodeTypes.Lfo, ("rate", "0.25"), ("unit", "beat"), ("amplitude", "2")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "b")),
                Node(5, GraphNodeTypes.NotePosition),
                Node(6, GraphNodeTypes.CurveOutput, ("abbr", "c")),
                Node(7, GraphNodeTypes.NoteEnvelope, ("attack_ms", "100"), ("release_ms", "50")),
                Node(8, GraphNodeTypes.CurveOutput, ("abbr", "d")),
            }, Link(1, 2), Link(3, 4), Link(5, 6), Link(7, 8));
            var outputs = Evaluate(graph, project, track, part, new[] { 0, 240, 360, 1100 });
            Assert.Equal(new[] { 0f, 1f, (float)Math.Sin(2 * Math.PI * 1.375), (float)Math.Sin(2 * Math.PI * 2.14583333) },
                outputs["a"], new ToleranceComparer(1e-4f));
            Assert.Equal(new[] { 0f, 2 * (float)Math.Sin(2 * Math.PI * 0.625), 2 * (float)Math.Sin(2 * Math.PI * 0.6875) },
                outputs["b"].Take(3), new ToleranceComparer(1e-4f));
            Assert.Equal(new[] { 0f, 0.5f, 0.75f, 0f }, outputs["c"]);
            Assert.Equal(new[] { 0f, 1f, 1f, 0f }, outputs["d"]);
        }

        class ToleranceComparer : IEqualityComparer<float> {
            readonly float tolerance;
            public ToleranceComparer(float tolerance) => this.tolerance = tolerance;
            public bool Equals(float x, float y) => Math.Abs(x - y) <= tolerance;
            public int GetHashCode(float obj) => 0;
        }

        [Fact]
        public void TracksUseTheirRenderersDefaultOrTheirOverride() {
            var (project, track, _) = Fixture(Graph(new[] { Node(1, GraphNodeTypes.Constant) }));
            var other = Graph(new[] { Node(1, GraphNodeTypes.Constant) });
            other.id = "other";
            project.expressionGraphs!.Add(other);
            Assert.Equal("g", ExpressionGraphProgram.GetEffectiveGraph(project, track)!.id);
            track.ExpressionGraph = "other";
            Assert.Equal("other", ExpressionGraphProgram.GetEffectiveGraph(project, track)!.id);
            other.renderer = "OTHER";
            Assert.Null(ExpressionGraphProgram.GetEffectiveGraph(project, track));
            track.ExpressionGraph = null;
            track.RendererSettings.renderer = "OTHER";
            Assert.Null(ExpressionGraphProgram.GetEffectiveGraph(project, track));
        }

        [Fact]
        public void WorldlineVariantsUseWorldlineRGraphs() {
            var graph = Graph(new[] { Node(1, GraphNodeTypes.Constant) });
            var (project, track, _) = Fixture(graph);
            graph.renderer = Renderers.WORLDLINE_R;
            project.defaultExpressionGraphs = new Dictionary<string, string> { [Renderers.WORLDLINE_R] = graph.id };
            foreach (var renderer in new[] { Renderers.WORLDLINE_R, Renderers.WORLDLINE_R11, Renderers.WORLDLINE_R2 }) {
                track.RendererSettings.renderer = renderer;
                Assert.Equal(graph.id, ExpressionGraphProgram.GetEffectiveGraph(project, track)?.id);
            }
            track.RendererSettings.renderer = Renderers.CLASSIC;
            Assert.Null(ExpressionGraphProgram.GetEffectiveGraph(project, track));
        }

        static string Snapshot(IEnumerable<RenderPhrase> phrases) {
            var lines = new List<string>();
            foreach (var phrase in phrases) {
                lines.Add($"p {phrase.hash:x16} {phrase.preEffectHash:x16}");
                // Removed deleted phrase.xsy property and added null safety checks
                foreach (var array in new[] { phrase.pitches, phrase.dynamics, phrase.tension }) {
                    if (array != null) {
                        lines.Add(string.Join(",", array.Select(v => BitConverter.SingleToInt32Bits(v))));
                    }
                }
                foreach (var curve in phrase.curves) {
                    lines.Add(curve.Item1 + " " + string.Join(",", curve.Item2.Select(v => BitConverter.SingleToInt32Bits(v))));
                }
                foreach (var phone in phrase.phones) {
                    lines.Add($"h {phone.hash:x16} {Flags(phone)} {phone.volume} {phone.velocity} {phone.modulation} "
                        + string.Join(" ", phone.envelope.Select(p => $"{p.X},{p.Y}")));
                }
            }
            return string.Join("\n", lines);
        }

        static string Flags(RenderPhone phone) => string.Join(" ", phone.flags.Select(f => f.Item1 + f.Item2));

        static void AddIdentity(List<UGraphNode> nodes, List<UGraphLink> links, IEnumerable<string> abbrs, string input, string output) {
            foreach (var abbr in abbrs) {
                int id = nodes.Count + 1;
                nodes.Add(Node(id, input, ("abbr", abbr)));
                nodes.Add(Node(id + 1, output, ("abbr", abbr)));
                links.Add(Link(id, id + 1));
            }
        }

        [Fact]
        public void IdentityGraphChangesNothing() {
            var (project, track, part) = Fixture(null);
            var withoutGraph = Snapshot(RenderPhrase.FromPart(project, track, part));

            var nodes = new List<UGraphNode>();
            var links = new List<UGraphLink>();
            AddIdentity(nodes, links,
                project.expressions.Values.Where(d => d.type == UExpressionType.Curve).Select(d => d.abbr),
                GraphNodeTypes.CurveInput, GraphNodeTypes.CurveOutput);
            AddIdentity(nodes, links,
                project.expressions.Values.Where(d => d.type == UExpressionType.Numerical).Select(d => d.abbr),
                GraphNodeTypes.PhonemeInput, GraphNodeTypes.PhonemeOutput);
            var graph = Graph(nodes, links.ToArray());
            (project, track, part) = Fixture(graph);
            Assert.NotNull(ExpressionGraphProgram.ForTrack(project, track));
            Assert.Equal(withoutGraph, Snapshot(RenderPhrase.FromPart(project, track, part)));
        }

        [Fact]
        public void DrivenCurvesReachThePhrase() {
            var (project, track, part) = Fixture(null);
            var before = RenderPhrase.FromPart(project, track, part);

            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "cstm")),
                Node(2, GraphNodeTypes.Multiply, ("b", "3")),
                Node(3, GraphNodeTypes.Add, ("b", "10")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2, "a"), Link(2, 3, "a"), Link(3, 4));
            (project, track, part) = Fixture(graph);
            var after = RenderPhrase.FromPart(project, track, part);

            Assert.Contains(31f, after[0].tension);
            Assert.All(after[1].tension, v => Assert.Equal(10f, v));
            Assert.Null(before[0].drivenCurves);
            Assert.Equal(after[1].tension, after[1].drivenCurves!["tenc"]);
            Assert.NotEqual(before[0].hash, after[0].hash);
            Assert.Equal(before[0].dynamics, after[0].dynamics);
            Assert.Equal(before[0].curves.Single(c => c.Item1 == "cstm").Item2, after[0].curves.Single(c => c.Item1 == "cstm").Item2);
        }

        [Fact]
        public void DrivenCurvesStayInTheirRange() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Constant, ("value", "500")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2));
            var (project, track, part) = Fixture(graph);
            Assert.All(RenderPhrase.FromPart(project, track, part)[0].tension, v => Assert.Equal(100f, v));
        }

        [Fact]
        public void DrivenPhonemeValuesReachThePhones() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.CurveInput, ("abbr", "dyn")),
                Node(2, GraphNodeTypes.Add, ("b", "100")),
                Node(3, GraphNodeTypes.PhonemeOutput, ("abbr", "vol")),
                Node(4, GraphNodeTypes.Constant, ("value", "30.7")),
                Node(5, GraphNodeTypes.PhonemeOutput, ("abbr", "gen")),
            }, Link(1, 2, "a"), Link(2, 3), Link(4, 5));
            var (project, track, part) = Fixture(graph);
            var phones = RenderPhrase.FromPart(project, track, part).SelectMany(p => p.phones).ToArray();

            Assert.Equal(new[] { 94f, 97f, 97f, 91f }.Select(v => v * 0.01f), phones.Select(p => p.volume));
            Assert.Equal(new[] { 94f, 97f, 97f, 91f }, phones.Select(p => p.drivenExpressions!["vol"]));
            Assert.Equal(new[] { 94f, 94f, 0f }, phones[0].envelope.Skip(1).Take(3).Select(p => p.Y));
            Assert.Equal(new[] { "g30 ", "g30 ", "g30 Y", "g30 Y" }, phones.Select(Flags));
        }

        [Fact]
        public void TimingExpressionsCannotBeDriven() {
            var (project, track, part) = Fixture(null);
            var before = Snapshot(RenderPhrase.FromPart(project, track, part));
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.Constant, ("value", "50")),
                Node(2, GraphNodeTypes.PhonemeOutput, ("abbr", "vel")),
                Node(3, GraphNodeTypes.PhonemeOutput, ("abbr", "shft")),
                Node(4, GraphNodeTypes.PhonemeOutput, ("abbr", "grw")),
            }, Link(1, 2), Link(1, 3), Link(1, 4));
            (project, track, part) = Fixture(graph);
            Assert.Equal(before, Snapshot(RenderPhrase.FromPart(project, track, part)));
        }

        [Fact]
        public void PhonemeValuesDriveCurves() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.PhonemeInput, ("abbr", "vol")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "a")),
                Node(3, GraphNodeTypes.PhonemeInput, ("abbr", "vol"), ("interpolation", "linear")),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "b")),
                Node(5, GraphNodeTypes.PhonemeInput, ("abbr", "grw")),
                Node(6, GraphNodeTypes.CurveOutput, ("abbr", "c")),
            }, Link(1, 2), Link(3, 4), Link(5, 6));
            var (project, track, part) = Fixture(graph);
            var outputs = Evaluate(graph, project, track, part, new[] { -100, 0, 240, 480, 840, 2000 });
            Assert.Equal(new[] { 80f, 80f, 80f, 100f, 100f, 100f }, outputs["a"]);
            Assert.Equal(new[] { 80f, 80f, 90f, 100f, 100f, 100f }, outputs["b"]);
            Assert.All(outputs["c"], v => Assert.Equal(0f, v));
        }

        [Fact]
        public void InterpolatesBetweenAnchors() {
            var anchors = new PhonemeAnchors(new[] { 0, 100, 100, 300, 400 },
                new Dictionary<string, float[]> { ["x"] = new[] { 0f, 50f, 60f, 100f, 100f } });
            Assert.Equal(50f, anchors.At("x", 1));
            Assert.Equal(60f, anchors.At("x", 2));
            foreach (var mode in new[] { AnchorInterpolation.Step, AnchorInterpolation.Linear, AnchorInterpolation.Cubic }) {
                Assert.Equal(new[] { 0f, 0f, 60f, 100f, 100f, 100f },
                    new[] { -50, 0, 100, 300, 400, 500 }.Select(t => anchors.Sample("x", t, mode)));
            }
            Assert.Equal(0f, anchors.Sample("x", 50, AnchorInterpolation.Step));
            Assert.Equal(30f, anchors.Sample("x", 50, AnchorInterpolation.Linear));
            Assert.Equal(80f, anchors.Sample("x", 200, AnchorInterpolation.Linear));
            var cubic = Enumerable.Range(0, 401).Select(t => anchors.Sample("x", t, AnchorInterpolation.Cubic)).ToArray();
            Assert.All(cubic, v => Assert.InRange(v, 0f, 100f));
            Assert.All(cubic.Zip(cubic.Skip(1)), p => Assert.True(p.Second >= p.First - 1e-4f, $"{p.First} > {p.Second}"));
            Assert.Equal(0f, anchors.Sample("y", 50, AnchorInterpolation.Linear));
        }

        static float[] PitchWith(string source) {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.PitchInput, ("source", source)),
                Node(2, GraphNodeTypes.PitchOutput),
            }, Link(1, 2));
            var (project, track, part) = Fixture(graph);
            return RenderPhrase.FromPart(project, track, part)[0].pitches;
        }

        [Fact]
        public void PitchSources() {
            var (project, track, part) = Fixture(null);
            var phrase = RenderPhrase.FromPart(project, track, part)[0];
            int start = phrase.position - part.position - phrase.leading;

            var bend = PitchWith("pitch_bend");
            var vibrato = PitchWith("vibrato");
            Assert.Equal(phrase.pitchesBeforeDeviation, bend.Zip(vibrato, (b, v) => b + v), new ToleranceComparer(1e-3f));
            for (int i = 0; i < vibrato.Length; ++i) {
                int tick = start + i * 5;
                if (tick < 190 || tick >= 480) {
                    Assert.Equal(0f, vibrato[i]);
                }
            }
            Assert.Contains(vibrato, v => Math.Abs(v) > 10);
            Assert.All(PitchWith("mod_plus"), v => Assert.Equal(0f, v));
            var notes = part.notes.ToArray();
            Assert.Equal(Enumerable.Range(0, phrase.pitches.Length)
                    .Select(i => (start + i * 5 < notes[0].End ? notes[0] : notes[1]).AdjustedTone * 100),
                PitchWith("notes"));
        }

        [Fact]
        public void PitchCanDriveCurves() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.PitchInput, ("source", "pitch_bend")),
                Node(2, GraphNodeTypes.MapRange, ("in_min", "6000"), ("in_max", "6100"), ("out_min", "0"), ("out_max", "1")),
                Node(3, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            }, Link(1, 2), Link(2, 3));
            var (project, track, part) = Fixture(graph);
            var phrase = RenderPhrase.FromPart(project, track, part)[0];
            Assert.Equal(PitchWith("pitch_bend").Select(p => Math.Clamp((p - 6000) / 100, 0, 100)), phrase.tension);
        }

        [Fact]
        public void PerPhonemeOutputsCannotReadPitch() {
            Assert.Null(ExpressionGraphProgram.Compile(Graph(new[] {
                Node(1, GraphNodeTypes.PitchInput),
                Node(2, GraphNodeTypes.PhonemeOutput, ("abbr", "vol")),
            }, Link(1, 2)), out var error));
            Assert.Contains("can't read pitch", error);
            Assert.Null(ExpressionGraphProgram.Compile(Graph(new[] {
                Node(1, GraphNodeTypes.PitchInput, ("source", "nowhere")),
            }), out error));
            Assert.Contains("unknown pitch source", error);
        }

        [Fact]
        public void MaskedCurveNodeFallsBackAndMasks() {
            var graph = Graph(new[] {
                Node(1, GraphNodeTypes.PitchInput, ("source", "notes")),
                Node(2, GraphNodeTypes.MaskedCurveInput, ("abbr", "pito")),
                Node(3, GraphNodeTypes.PitchOutput),
                Node(4, GraphNodeTypes.CurveOutput, ("abbr", "cstm")),
            }, Link(1, 2, "fallback"), new UGraphLink { from = 2, fromPort = "value", to = 3, toPort = "value" },
                new UGraphLink { from = 2, fromPort = "mask", to = 4, toPort = "value" });
            var (project, track, part) = Fixture(graph);
            part.maskedCurves.Add(new UMaskedCurve("pito"));
            part.maskedCurves[0].Set(0, 5000, 20, 5100);
            var notes = part.notes.ToArray();
            var phrase = RenderPhrase.FromPart(project, track, part)[0];
            int start = phrase.position - part.position - phrase.leading;
            var mask = phrase.curves.Single(c => c.Item1 == "cstm").Item2;
            for (int i = 0; i < phrase.pitches.Length; ++i) {
                int tick = start + i * 5;
                bool drawn = tick >= 0 && tick <= 20;
                Assert.Equal(drawn ? 5000 + tick * 5 : (tick < notes[0].End ? notes[0] : notes[1]).AdjustedTone * 100,
                    phrase.pitches[i]);
                Assert.Equal(drawn ? 1f : 0f, mask[i]);
            }
        }

        [Fact]
        public void LinksNameTheOutputTheyLeave() {
            var nodes = new[] {
                Node(1, GraphNodeTypes.MaskedCurveInput, ("abbr", "pito")),
                Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")),
            };
            Assert.Null(ExpressionGraphProgram.Compile(Graph(nodes,
                new UGraphLink { from = 1, fromPort = "nowhere", to = 2, toPort = "value" }), out var error));
            Assert.Contains("no output", error);
            Assert.NotNull(ExpressionGraphProgram.Compile(Graph(nodes, Link(1, 2)), out _));

            var graph = Graph(nodes);
            Assert.True(ExpressionGraphEdits.TryLink(graph, 1, 2, "value", "mask"));
            Assert.Equal("mask", graph.links.Single().fromPort);
            Assert.False(ExpressionGraphEdits.TryLink(graph, 1, 2, "value", "nowhere"));
            graph = Graph(new[] { Node(1, GraphNodeTypes.Constant), Node(2, GraphNodeTypes.CurveOutput, ("abbr", "tenc")) });
            Assert.True(ExpressionGraphEdits.TryLink(graph, 1, 2, "value"));
            Assert.Null(graph.links.Single().fromPort);
        }

        [Fact]
        public void RenderedPitchIsResampledOntoTheGrid() {
            var result = new RenderPitchResult {
                ticks = new float[] { 0, 11, 22, 33, 44, 55 },
                tones = new[] { 60f, 61.1f, 62f, 63f, 64f, 65f },
                voiced = new[] { true, true, false, true, true, true },
            };
            var cleared = new List<(int, int)>();
            var values = new List<(int x, float y)>();
            OpenUtau.Core.Editing.LoadRenderedPitch.CollectRenderedPitch(result, 100, 50, cleared, values);
            Assert.Equal(new[] { (100, 155) }, cleared);
            Assert.Equal(new[] { 100, 105, 110, 135, 140 }, values.Select(v => v.x));
            Assert.Equal(new[] { 6000f, 6050f, 6100f, 6318.18f, 6363.64f }, values.Select(v => v.y), new ToleranceComparer(0.01f));
        }

        [Fact]
        public void GraphsCanPreferThePitchOverride() {
            var graph = Graph(new[] { Node(1, GraphNodeTypes.Constant) });
            var (project, track, _) = Fixture(graph);
            Assert.False(ExpressionGraphProgram.PrefersPitchOverride(project, track));
            graph.preferredPitchCurve = "pito";
            Assert.True(ExpressionGraphProgram.PrefersPitchOverride(project, track));
            Assert.Contains("preferred_pitch_curve: pito", Yaml.DefaultSerializer.Serialize(graph));
            graph.nodes.Add(Node(1, GraphNodeTypes.Abs));
            Assert.False(ExpressionGraphProgram.PrefersPitchOverride(project, track));
        }

        [Fact]
        public void DefaultGraphsKeepTodaysPitchUntilOverridden() {
            var (project, track, part) = Fixture(null);
            var before = RenderPhrase.FromPart(project, track, part)[0].pitches;

            var graph = ExpressionGraphEdits.CreateDefault("g", "G", Renderer, rendersPitch: false);
            Assert.Equal("pito", graph.preferredPitchCurve);
            (project, track, part) = Fixture(graph);
            Assert.Equal(before, RenderPhrase.FromPart(project, track, part)[0].pitches, new ToleranceComparer(0.01f));

            part.maskedCurves.Add(new UMaskedCurve("pito"));
            part.maskedCurves[0].Set(0, 5000, 20, 5000);
            var phrase = RenderPhrase.FromPart(project, track, part)[0];
            int start = phrase.position - part.position - phrase.leading;
            Assert.Equal(5000f, phrase.pitches[(0 - start) / 5]);
        }

        [Fact]
        public void DefaultGraphsForPitchRenderersUseTheRenderedPitch() {
            var graph = ExpressionGraphEdits.CreateDefault("g", "G", Renderer, rendersPitch: true);
            var (project, track, part) = Fixture(graph);
            var notes = part.notes.ToArray();
            var pitd = part.curves.Single(c => c.abbr == "pitd");
            var phrase = RenderPhrase.FromPart(project, track, part)[0];
            int start = phrase.position - part.position - phrase.leading;
            for (int i = 0; i < phrase.pitches.Length; ++i) {
                int tick = start + i * 5;
                Assert.Equal((tick < notes[0].End ? notes[0] : notes[1]).AdjustedTone * 100 + pitd.Sample(tick),
                    phrase.pitches[i], 3);
            }
            part.maskedCurves.Add(new UMaskedCurve("rpit"));
            part.maskedCurves[0].Set(0, 6500, 20, 6500);
            phrase = RenderPhrase.FromPart(project, track, part)[0];
            Assert.Equal(6500 + pitd.Sample(0), phrase.pitches[(0 - start) / 5], 3);
        }

        [Fact]
        public void LibraryEditsAreUndoable() {
            var (project, track, _) = Fixture(Graph(new[] { Node(1, GraphNodeTypes.Constant) }));
            track.ExpressionGraph = "g";
            var draft = new ExpressionGraphEdits.Draft(project);
            draft.Find("g")!.nodes[0].x = 50;
            Assert.Equal(Pipeline.ImpactKind.None, new SetExpressionGraphsCommand(project, draft.ToState()).Impact.Kind);

            draft.Remove("g");
            var remove = new SetExpressionGraphsCommand(project, draft.ToState());
            Assert.Equal(Pipeline.ImpactKind.Project, remove.Impact.Kind);
            remove.Execute();
            Assert.Null(project.expressionGraphs);
            Assert.Null(project.defaultExpressionGraphs);
            Assert.Null(track.ExpressionGraph);
            remove.Unexecute();
            Assert.Equal("g", project.expressionGraphs!.Single().id);
            Assert.Equal("g", project.defaultExpressionGraphs![Renderer]);
            Assert.Equal("g", track.ExpressionGraph);
            Assert.Equal(0f, project.expressionGraphs!.Single().nodes[0].x);

            var ids = new ExpressionGraphEdits.Draft(project);
            ids.Graphs.Add(new UExpressionGraph { id = "my_graph" });
            Assert.Equal("my_graph_2", ids.NewId("My graph!"));
        }
    }
}