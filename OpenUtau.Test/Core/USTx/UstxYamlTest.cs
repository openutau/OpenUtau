using Xunit;
using Xunit.Abstractions;

namespace OpenUtau.Core.Ustx {
    public class UstxYamlTest {
        readonly ITestOutputHelper output;
        readonly UExpressionDescriptor descriptor;
        readonly UNote note;

        public UstxYamlTest(ITestOutputHelper output) {
            this.output = output;
            descriptor = new UExpressionDescriptor("velocity", Format.Ustx.VEL, 0, 200, 100);

            note = UNote.Create();
            note.position = 120;
            note.duration = 60;
            note.tone = 42;
            note.lyric = "あ";
            note.pitch.AddPoint(new PitchPoint(-5, 0));
            note.pitch.AddPoint(new PitchPoint(5, 0));
            note.phonemeExpressions.Add(new UExpression(descriptor) {
                index = 0,
                value = 123,
            });
        }

        [Fact]
        public void UNoteSerializationTest() {
            var actual = Yaml.DefaultSerializer.Serialize(note);
            output.WriteLine(actual);

            string expected = @"position: 120
duration: 60
tone: 42
lyric: あ
pitch:
  data:
  - {x: -5, y: 0, shape: io}
  - {x: 5, y: 0, shape: io}
  snap_first: true
vibrato: {length: 0, period: 175, depth: 25, in: 10, out: 10, shift: 0, drift: 0, vol_link: 0}
tuning: 0
phoneme_expressions:
- {index: 0, abbr: vel, value: 123}
phoneme_overrides: []
";

            Assert.Equal(expected.Replace("\r\n", "\n"), actual.Replace("\r\n", "\n"));
        }

        [Fact]
        public void UNoteDeserializationTest() {
            var yaml = Yaml.DefaultSerializer.Serialize(note);
            var actual = Yaml.DefaultDeserializer.Deserialize<UNote>(yaml);

            Assert.Equal(120, actual.position);
            Assert.Equal(60, actual.duration);
            Assert.Equal(42, actual.tone);
            Assert.Equal("あ", actual.lyric);
            Assert.Single(actual.phonemeExpressions);
            var vel = actual.phonemeExpressions[0];
            Assert.NotNull(vel);
            Assert.Null(vel.descriptor);
            Assert.Equal(123, vel.value);
        }

        [Fact]
        public void SpecialLyric() {
            var yaml = Yaml.DefaultSerializer.Serialize(new UNote() { lyric = "-@" });
            var actual = Yaml.DefaultDeserializer.Deserialize<UNote>(yaml);
            Assert.Equal("-@", actual.lyric);

            yaml = Yaml.DefaultSerializer.Serialize(new UNote() { lyric = "-&" });
            actual = Yaml.DefaultDeserializer.Deserialize<UNote>(yaml);
            Assert.Equal("-&", actual.lyric);

            yaml = Yaml.DefaultSerializer.Serialize(new UNote() { lyric = "null" });
            actual = Yaml.DefaultDeserializer.Deserialize<UNote>(yaml);
            Assert.Equal("null", actual.lyric);

            yaml = Yaml.DefaultSerializer.Serialize(new UNote() { lyric = "true" });
            actual = Yaml.DefaultDeserializer.Deserialize<UNote>(yaml);
            Assert.Equal("true", actual.lyric);

            yaml = Yaml.DefaultSerializer.Serialize(new UNote() { lyric = "-," });
            actual = Yaml.DefaultDeserializer.Deserialize<UNote>(yaml);
            Assert.Equal("-,", actual.lyric);

            yaml = Yaml.DefaultSerializer.Serialize(new UNote() { lyric = "\t- asdf" });
            actual = Yaml.DefaultDeserializer.Deserialize<UNote>(yaml);
            Assert.Equal("\t- asdf", actual.lyric);
        }
    }
}
