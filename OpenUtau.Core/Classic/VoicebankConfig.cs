using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Text;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    public enum SymbolSetPreset { unknown, hiragana, arpabet }

    /// <summary>The singer types character.yaml can name.</summary>
    public class SingerTypeValues : IYamlValueSource {
        public IEnumerable<YamlValue> GetValues() => SingerTypeUtils.SingerTypeFromName.Keys.Select(name => new YamlValue(name));
    }

    public class SymbolSet {
        [Description("The symbol set: unknown, hiragana or arpabet.")]
        public SymbolSetPreset Preset { get; set; }
        [Description("Symbol marking the start of a phrase.")]
        public string Head { get; set; } = "-";
        [Description("Symbol marking the end of a phrase.")]
        public string Tail { get; set; } = "R";
    }


    public class Subbank {
        [Description("Voice color, e.g. \"power\" or \"whisper\". Leave unspecified for the main bank.")]
        public string Color { get; set; } = string.Empty;
        [Description("Prefix of this subbank's aliases. Leave unspecified if none.")]
        public string Prefix { get; set; } = string.Empty;
        [Description("Suffix of this subbank's aliases, e.g. a pitch like \"C4\". Leave unspecified if none.")]
        public string Suffix { get; set; } = string.Empty;
        [Description("Pitches this subbank is used for. Each range is written as \"C1-C4\" or \"C4\".")]
        public string[] ToneRanges { get; set; }
    }

    public class VoicebankConfig {
        [Description("Name shown for this singer. Overrides the name in character.txt.")]
        public string Name;
        [Description("The singer's name in other languages, by language code, e.g. \"ja-JP: 名前\". Shown when OpenUtau uses that language.")]
        public Dictionary<string, string> LocalizedNames;
        [Description("Extra words that find this singer in singer search, such as nicknames or romanized names.")]
        public string[] SearchTerms;
        [Description("Kind of voicebank: utau, enunu, diffsinger or voicevox. If unspecified, OpenUtau guesses from the files in the folder.")]
        [YamlValues(typeof(SingerTypeValues))]
        public string SingerType;
        [Description("Encoding of the voicebank's text files, such as character.txt and oto.ini, e.g. shift_jis or utf-8. If unspecified, shift_jis. An oto.ini can declare its own with a #Charset: line.")]
        public string TextFileEncoding;
        [Description("Icon image, relative to the voicebank folder.")]
        public string Image;
        [Description("Portrait image shown in the piano roll, relative to the voicebank folder.")]
        public string Portrait;
        [Description("Opacity of the portrait, from 0 (transparent) to 1 (opaque).")]
        public float PortraitOpacity = 0.67f;
        [Description("Height in pixels the portrait is scaled to. 0 scales portraits taller than 800 pixels down to 800.")]
        public int PortraitHeight = 0;
        [Description("Author of the voicebank.")]
        public string Author;
        [Description("Voice provider.")]
        public string Voice;
        [Description("Website of the voicebank.")]
        public string Web;
        [Description("Version of the voicebank.")]
        public string Version;
        [Description("Audio file played by Play Sample, relative to the voicebank folder.")]
        public string Sample;
        [Description("Phonemizer selected when this singer is picked for a track, by its full type name, e.g. OpenUtau.Plugin.Builtin.JapanesePresampPhonemizer.")]
        [YamlValues(typeof(Api.PhonemizerTypeValues))]
        public string DefaultPhonemizer;
        [Description("Describes the symbols used in aliases. Not used by OpenUtau at the moment.")]
        public SymbolSet SymbolSet { get; set; }
        [Description("Voice colors and pitch ranges, each picked by the prefix and suffix of its aliases.")]
        public Subbank[] Subbanks { get; set; }
        [Description("UTAU voicebanks only: also use each sample's file name, without extension, as an alias.")]
        public bool? UseFilenameAsAlias = null;

        public void Save(Stream stream) {
            using (var writer = new StreamWriter(stream, Encoding.UTF8)) {
                Yaml.DefaultSerializer.Serialize(writer, this);
            }
        }

        public static VoicebankConfig Load(Stream stream) {
            using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                var bankConfig = Yaml.DefaultDeserializer.Deserialize<VoicebankConfig>(reader);
                return bankConfig;
            }
        }
    }
}
