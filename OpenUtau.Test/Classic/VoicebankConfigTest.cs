using OpenUtau.Core;
using Xunit;

namespace OpenUtau.Classic {
    public class VoicebankConfigTest {
        readonly ITestOutputHelper output;

        public VoicebankConfigTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void OptionalVoiceColorPortraitsRoundTripWithExactColorNames() {
            using var yaml = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                "portrait: portraits/normal.png\nvoice_color_portraits:\n  Soft Voice: portraits/soft.png\n"));
            var config = VoicebankConfig.Load(yaml);
            using var saved = new System.IO.MemoryStream();
            config.Save(saved);
            using var roundTrip = new System.IO.MemoryStream(saved.ToArray());
            var bank = new Voicebank();
            VoicebankLoader.ApplyConfig(bank, VoicebankConfig.Load(roundTrip));
            Assert.Equal("portraits/normal.png", bank.Portrait);
            Assert.Equal("portraits/soft.png", bank.VoiceColorPortraits!["Soft Voice"]);
            Assert.False(bank.VoiceColorPortraits.ContainsKey("soft voice"));
            VoicebankLoader.ApplyConfig(bank, new VoicebankConfig { Portrait = "legacy.png" });
            Assert.Null(bank.VoiceColorPortraits);
            Assert.Equal("legacy.png", bank.Portrait);
        }

        static VoicebankConfig CreateConfig() {
            return new VoicebankConfig() {
                PortraitOpacity = 0.75f,
                PortraitHeight = 675,
                Sample = "sample.wav",
                SymbolSet = new SymbolSet() {
                    Preset = SymbolSetPreset.hiragana,
                },
                Subbanks = new Subbank[] {
                    new Subbank() {
                        ToneRanges = new [] { "C1-C4" },
                    },
                    new Subbank() {
                        Suffix = "D4",
                        ToneRanges = new [] { "C#4-F4" },
                    },
                    new Subbank() {
                        Suffix = "G4",
                        ToneRanges = new [] { "F#4-A#4" },
                    },
                    new Subbank() {
                        Suffix = "C5",
                        ToneRanges = new [] { "B4-B7" },
                    },
                    new Subbank() {
                        Suffix = "C5P",
                        Color = "power" ,
                        ToneRanges = new [] { "B4-B7" },
                    },
                    new Subbank() {
                        Suffix = "C5S",
                        Color = "shout" ,
                        ToneRanges = new [] { "B4-B7" },
                    },
                }
            };
        }

        [Fact]
        public void SerializationTest() {
            var yaml = Yaml.DefaultSerializer.Serialize(CreateConfig());
            output.WriteLine(yaml);

            //"" evaluates to " in verbatim string literals
            Assert.Equal(@"portrait_opacity: 0.75
portrait_height: 675
sample: sample.wav
symbol_set:
  preset: hiragana
  head: '-'
  tail: R
subbanks:
- color: """"
  prefix: """"
  suffix: """"
  tone_ranges:
  - C1-C4
- color: """"
  prefix: """"
  suffix: D4
  tone_ranges:
  - C#4-F4
- color: """"
  prefix: """"
  suffix: G4
  tone_ranges:
  - F#4-A#4
- color: """"
  prefix: """"
  suffix: C5
  tone_ranges:
  - B4-B7
- color: power
  prefix: """"
  suffix: C5P
  tone_ranges:
  - B4-B7
- color: shout
  prefix: """"
  suffix: C5S
  tone_ranges:
  - B4-B7
".Replace("\r\n", "\n"), yaml.Replace("\r\n", "\n"));
        }

        [Fact]
        public void RoundTripTest() {
            var yaml = Yaml.DefaultSerializer.Serialize(CreateConfig());
            var config = Yaml.DefaultDeserializer.Deserialize<VoicebankConfig>(yaml);
            var yaml2 = Yaml.DefaultSerializer.Serialize(config);

            Assert.Equal(yaml, yaml2);
        }

        [Fact]
        public void SearchTermsTest() {
            var config = Yaml.DefaultDeserializer.Deserialize<VoicebankConfig>(@"
name: 重音テト
search_terms:
- kasane
- teto
- ' '
");
            var bank = new Voicebank();
            VoicebankLoader.ApplyConfig(bank, config);
            Assert.Equal(new[] { "kasane", "teto" }, bank.SearchTerms);
        }
    }
}
