using System.Linq;
using OpenUtau.Classic;
using OpenUtau.Core.DiffSinger;
using Xunit;

namespace OpenUtau.Core.Util {
    public class YamlValidatorTest {
        const string CharacterYaml = @"name: Test Singer
localized_names:
  ja-JP: テスト
singer_type: utau
text_file_encoding: shift_jis
portrait: portrait.png
portrait_opacity: 0.67
portrait_height: 0
version: 1.0
default_phonemizer: OpenUtau.Plugin.Builtin.JapanesePresampPhonemizer
use_filename_as_alias: true
symbol_set:
  preset: hiragana
  head: '-'
  tail: R
subbanks:
- color: ''
  prefix: ''
  suffix: ''
  tone_ranges:
  - C1-B7
- color: Soft
  suffix: _S
  tone_ranges: [C3-B4]
";

        [Fact]
        public void ValidFileHasNoDiagnostics() {
            Assert.Empty(YamlValidator.Validate<VoicebankConfig>(CharacterYaml));
            Assert.Empty(YamlValidator.Validate<VoicebankConfig>(""));
        }

        [Fact]
        public void UnknownKeyPointsAtTheKey() {
            var text = CharacterYaml.Replace("default_phonemizer:", "defualt_phonemizer:");
            var d = Assert.Single(YamlValidator.Validate<VoicebankConfig>(text));
            Assert.Equal(YamlDiagnosticKind.UnknownKey, d.Kind);
            Assert.Equal("defualt_phonemizer", d.Detail);
            Assert.False(d.IsError);
            Assert.Equal((10, 1, 10, 19), (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn));
        }

        [Fact]
        public void UnknownKeyInListItem() {
            var text = CharacterYaml.Replace("  suffix: _S", "  suffix: _S\n  tone_range: C3");
            var d = Assert.Single(YamlValidator.Validate<VoicebankConfig>(text));
            Assert.Equal(YamlDiagnosticKind.UnknownKey, d.Kind);
            Assert.Equal("/subbanks/1/tone_range", d.Path);
            Assert.Equal((24, 3), (d.StartLine, d.StartColumn));
        }

        [Fact]
        public void WrongTypePointsAtTheValue() {
            var text = CharacterYaml.Replace("portrait_opacity: 0.67", "portrait_opacity: high");
            var d = Assert.Single(YamlValidator.Validate<VoicebankConfig>(text));
            Assert.Equal(YamlDiagnosticKind.WrongType, d.Kind);
            Assert.Equal("number", d.Detail);
            Assert.True(d.IsError);
            Assert.Equal((7, 19, 7, 23), (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn));
        }

        [Fact]
        public void ProblemsTheReaderAcceptsAreWarnings() {
            var text = CharacterYaml.Replace("use_filename_as_alias: true", "use_filename_as_alias: yes");
            var d = Assert.Single(YamlValidator.Validate<VoicebankConfig>(text));
            Assert.Equal(YamlDiagnosticKind.WrongType, d.Kind);
            // YAML 1.2 reads "yes" as a string, but the app's reader takes it as true.
            Assert.False(d.IsError);
        }

        [Fact]
        public void StringsAcceptAnyScalar() {
            // "nan" must stay a string, not a .NET NaN, and numbers read fine into string fields.
            var text = CharacterYaml.Replace("name: Test Singer", "name: nan").Replace("version: 1.0", "version: 2");
            Assert.Empty(YamlValidator.Validate<VoicebankConfig>(text));
        }

        [Fact]
        public void InvalidEnumValue() {
            var text = CharacterYaml.Replace("preset: hiragana", "preset: katakana");
            var d = Assert.Single(YamlValidator.Validate<VoicebankConfig>(text));
            Assert.Equal(YamlDiagnosticKind.Invalid, d.Kind);
            Assert.Equal("/symbol_set/preset", d.Path);
            Assert.True(d.IsError);
        }

        [Fact]
        public void SyntaxError() {
            var d = Assert.Single(YamlValidator.Validate<VoicebankConfig>("name: [unclosed\nauthor: x\n"));
            Assert.Equal(YamlDiagnosticKind.Syntax, d.Kind);
            Assert.True(d.IsError);
            Assert.True(d.StartLine >= 1);
        }

        [Fact]
        public void DescribesTheKeyAtAPosition() {
            var key = YamlValidator.DescribeKeyAt(CharacterYaml, typeof(VoicebankConfig), 10, 3)!;
            Assert.Equal("default_phonemizer", key.Key);
            Assert.Contains("Phonemizer", key.Description);
            Assert.Equal(typeof(string), key.ValueType);
            Assert.Null(key.DefaultValue);

            key = YamlValidator.DescribeKeyAt(CharacterYaml, typeof(VoicebankConfig), 7, 1)!;
            Assert.Equal("portrait_opacity", key.Key);
            Assert.Equal(0.67f, key.DefaultValue);

            // Nested keys, in a list item and in a section.
            key = YamlValidator.DescribeKeyAt(CharacterYaml, typeof(VoicebankConfig), 20, 5)!;
            Assert.Equal("tone_ranges", key.Key);
            Assert.Equal(typeof(string[]), key.ValueType);
            key = YamlValidator.DescribeKeyAt(CharacterYaml, typeof(VoicebankConfig), 13, 3)!;
            Assert.Equal(typeof(SymbolSetPreset), key.ValueType);

            // A dictionary entry has the dictionary's value type but no description of its own.
            key = YamlValidator.DescribeKeyAt(CharacterYaml, typeof(VoicebankConfig), 3, 3)!;
            Assert.Equal(("ja-JP", null, typeof(string)), (key.Key, key.Description, key.ValueType));

            // Values, unknown keys and broken YAML have no key help.
            Assert.Null(YamlValidator.DescribeKeyAt(CharacterYaml, typeof(VoicebankConfig), 10, 25));
            Assert.Null(YamlValidator.DescribeKeyAt("defualt_phonemizer: x\n", typeof(VoicebankConfig), 1, 1));
            Assert.Null(YamlValidator.DescribeKeyAt("name: [\n", typeof(VoicebankConfig), 1, 1));

            // Keys under YamlMember aliases.
            key = YamlValidator.DescribeKeyAt("use_shallow_diffusion: true\n", typeof(DsConfig), 1, 1)!;
            Assert.Contains("use_variable_depth", key.Description);
        }

        // Suggestions at the caret, marked with "|".
        static YamlSuggestions? SuggestAt<T>(string textWithCaret) {
            int index = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(index, 1);
            int line = text.Substring(0, index).Count(c => c == '\n') + 1;
            int lineStart = index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
            int column = index - lineStart + 1;
            return YamlValidator.SuggestAt(text, typeof(T), line, column);
        }

        [Fact]
        public void SuggestsKeysTheSectionDoesNotHaveYet() {
            var suggestions = SuggestAt<VoicebankConfig>("name: X\nde|\n")!;
            Assert.Equal(1, suggestions.StartColumn);
            var keys = suggestions.Keys.Select(k => k.Key).ToList();
            Assert.Contains("default_phonemizer", keys);
            Assert.DoesNotContain("name", keys);
            Assert.Contains("Phonemizer", suggestions.Keys.First(k => k.Key == "default_phonemizer").Description);

            // In a list item, after its first key, and in a new list item.
            suggestions = SuggestAt<VoicebankConfig>("subbanks:\n- color: a\n  suf|\n")!;
            Assert.Equal(3, suggestions.StartColumn);
            Assert.Equal(new[] { "prefix", "suffix", "tone_ranges" }, suggestions.Keys.Select(k => k.Key));
            suggestions = SuggestAt<VoicebankConfig>("subbanks:\n- color: a\n- co|\n")!;
            Assert.Equal(3, suggestions.StartColumn);
            Assert.Contains("color", suggestions.Keys.Select(k => k.Key));

            // In a section, on an empty line.
            suggestions = SuggestAt<VoicebankConfig>("symbol_set:\n  |\n")!;
            Assert.Equal(new[] { "preset", "head", "tail" }, suggestions.Keys.Select(k => k.Key));
        }

        [Fact]
        public void SuggestsChoicesForEnumAndBooleanValues() {
            var suggestions = SuggestAt<VoicebankConfig>("symbol_set:\n  preset: hi|\n")!;
            Assert.Equal(11, suggestions.StartColumn);
            Assert.Equal(new[] { "unknown", "hiragana", "arpabet" }, suggestions.Values.Select(v => v.Text));
            Assert.Equal(new[] { "true", "false" }, SuggestAt<DsConfig>("use_lang_id: |\n")!.Values.Select(v => v.Text));
        }

        [Fact]
        public void SuggestsKnownValuesOfFreeTextKeys() {
            var suggestions = SuggestAt<VoicebankConfig>("singer_type: di|\n")!;
            Assert.Equal(14, suggestions.StartColumn);
            Assert.Equal(new[] { "utau", "enunu", "diffsinger", "voicevox" }, suggestions.Values.Select(v => v.Text));
            // The installed phonemizers come from the same kind of source.
            var key = YamlValidator.DescribeKeyAt("default_phonemizer: x\n", typeof(VoicebankConfig), 1, 1)!;
            Assert.Equal(typeof(OpenUtau.Api.PhonemizerTypeValues), key.ValueSource);
        }

        [Fact]
        public void NoSuggestionsWhereNothingFits() {
            Assert.Null(SuggestAt<VoicebankConfig>("name: |\n"));                    // free text
            Assert.Null(SuggestAt<VoicebankConfig>("localized_names:\n  en|\n"));  // user-defined keys
            Assert.Null(SuggestAt<VoicebankConfig>("na|me: X\n"));                  // not at the end of the line
            Assert.Null(SuggestAt<VoicebankConfig>("name: [\nde|\n"));             // broken elsewhere
        }

        [Theory]
        [InlineData(typeof(VoicebankConfig))]
        [InlineData(typeof(SymbolSet))]
        [InlineData(typeof(Subbank))]
        [InlineData(typeof(DsConfig))]
        [InlineData(typeof(AugmentationArgs))]
        [InlineData(typeof(RandomPitchShifting))]
        public void EveryKeyHasADescription(System.Type type) {
            Assert.Empty(YamlValidator.KeysOf(type)
                .Where(k => k.property.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>() == null)
                .Select(k => k.name));
        }

        [Fact]
        public void DsConfigKeysMatchTheReader() {
            // camelCase fields, snake_case fields and YamlMember aliases, as the app reads them.
            const string dsconfig = @"phonemes: phonemes.txt
acoustic: acoustic.onnx
vocoder: nsf_hifigan
speakers: [a, b]
hidden_size: 256
use_key_shift_embed: true
use_lang_id: false
use_shallow_diffusion: true
max_depth: 1000
augmentation_args:
  random_pitch_shifting:
    range: [-5.0, 5.0]
sample_rate: 44100
mel_base: e
";
            Assert.Empty(YamlValidator.Validate<DsConfig>(dsconfig));
            var diagnostics = YamlValidator.Validate<DsConfig>(dsconfig + "_use_shallow_diffusion: true\nmax_depth_ms: 1\n");
            Assert.Equal(new[] { "_use_shallow_diffusion", "max_depth_ms" },
                diagnostics.Where(d => d.Kind == YamlDiagnosticKind.UnknownKey).Select(d => d.Detail));
        }

        [Fact]
        public void ReaderErrorsTheSchemaMisses() {
            // Too large for the int field: valid for the schema, rejected by the app's reader.
            var d = Assert.Single(YamlValidator.Validate<DsConfig>("hidden_size: 99999999999\n"));
            Assert.Equal(YamlDiagnosticKind.Invalid, d.Kind);
            Assert.Equal(1, d.StartLine);
            Assert.True(d.IsError);
        }
    }
}
