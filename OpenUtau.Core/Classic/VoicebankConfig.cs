using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenUtau.Core;

namespace OpenUtau.Classic {
    public enum SymbolSetPreset { unknown, hiragana, arpabet }

    public class SymbolSet {
        public SymbolSetPreset Preset { get; set; }
        public string Head { get; set; } = "-";
        public string Tail { get; set; } = "R";
    }


    public class Subbank {
        /// <summary>
        /// Voice color, e.g., "power", "whisper". Leave unspecified for the main bank.
        /// </summary>
        public string Color { get; set; } = string.Empty;

        /// <summary>
        /// Subbank prefix. Leave unspecified if none.
        /// </summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>
        /// Subbank suffix. Leave unspecified if none.
        /// </summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>
        /// Tone ranges. Each range specified as "C1-C4" or "C4".
        /// </summary>
        public string[] ToneRanges { get; set; }
    }

public class TrackColor {
    public string ColorName { get; set; } = string.Empty;
    public string AccentColor { get; set; } = string.Empty;
    public string AccentColorDark { get; set; } = string.Empty; // Pressed
    public string AccentColorLight { get; set; } = string.Empty; // PointerOver
    public string AccentColorCenterKey { get; set; } = string.Empty; // Keyboard
}

    public class VoicebankConfig {
        public string Name;
        public Dictionary<string, string> LocalizedNames;
        public string SingerType;
        public string TextFileEncoding;
        public string Image;
        public string Portrait;
        public float PortraitOpacity = 0.67f;
        public int PortraitHeight = 0;
        public string Author;
        public string Voice;
        public string Web;
        public string Version;
        public string Sample;
        public string DefaultPhonemizer;
        public SymbolSet SymbolSet { get; set; }
        public Subbank[] Subbanks { get; set; }
        public bool? UseFilenameAsAlias = null;
        public TrackColor[] TrackColor;
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
