using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// zhcvvplus.yaml class
    /// </summary> 
    [Serializable]
    public class ChineseCVVPlusConfigYaml {

        /// <summary>
        /// Prefix of affix vowel. Default value is "_"
        /// </summary>
        public string VowelTailPrefix = "_";

        /// <summary>
        /// Whether to use a long Nasal Vowel without a tail vowel
        /// </summary>
        public bool UseSingleNasalVowel = false;

        /// <summary>
        /// Whether to use a long Multiple Vowel without a tail vowel
        /// </summary>
        public bool UseSingleMultipleVowel = false;

        /// <summary>
        /// Whether to use retan. If set to True, "- " is added to the first note's lyrics.
        /// </summary>
        public bool UseRetan = false;

        /// <summary>
        /// Types of End Breath. Multiple types can be used.
        /// </summary>
        public string[] SupportedTailBreath = { "-" };

        /// <summary>
        /// Specify the Consonant. Separated into yaml for customization in case additional consonants are needed.
        /// </summary>
        public string[] ConsonantDict = { "zh", "ch", "sh", "b", "p", "m", "f", "d", "t", "n", "l", "z", "c", "s", "r", "j", "q", "x", "g", "k", "h" };

        /// <summary>
        /// Specify the Vowel. Separated into yaml for the same reason as above.
        /// </summary>
        public string[] SingleVowelDict = { "a", "o", "e", "i", "u", "v", "er" };

        /// <summary>
        /// Specify the Nasal Vowel. Separated into yaml for the same reason as above.
        /// </summary>
        public string[] NasalVowelDict = { "an", "en", "ang", "eng", "ong", "ian", "iang", "ing", "iong", "uan", "uen", "un", "uang", "ueng", "van", "vn" };

        /// <summary>
        /// Specify the Multiple Vowel. Separated into yaml for the same reason as above.
        /// </summary>
        public string[] MultipleVowelDict = { "ai", "ei", "ao", "ou", "ia", "iao", "ie", "iou", "ua", "uo", "uai", "uei", "ui", "ve" };

        /// <summary>
        /// Position of fast tail vowel (in ticks).
        /// </summary>
        public int FastTailVowelTimingTick = 100;
        /// <summary>
        /// The criterion for determining single usage when UseSingleNasalVowel or UseSingleMultipleVowel is set to True (in ticks).
        /// </summary>
        public int SingleVowelsReferenceTimimgTick = 480;
        /// <summary>
        /// Fast Multiple Vowel. If a fast Nasal Vowel is not needed, leave this empty and move everything to SlowTailVowelDict.
        /// </summary>
        public Dictionary<string, string> FastTailVowelDict = new Dictionary<string, string>() {
            {"ia", "ia"},
            {"ie", "ie"},
            {"ua", "ua"},
            {"uo", "uo"},
            {"ve", "ve"},
        };
        /// <summary>
        /// Slow Multiple Vowel. The position of the slow Multiple Vowel is calculated as 1/3 of the note.
        /// <br></br>
        /// {"Basic form of the vowel": "Representation excluding the prefix of the tail vowel"}
        /// </summary>
        public Dictionary<string, string> SlowTailVowelDict = new Dictionary<string, string>()
        {
            {"ai", "ai"},
            {"ei", "ei"},
            {"ao", "ao"},
            {"ou", "ou"},
            {"an", "an"},
            {"en", "en"},
            {"ang", "ang"},
            {"eng", "eng"},
            {"ong", "ong"},
            {"iao", "ao"},
            {"iu", "ou"},
            {"iou", "ou"},
            {"ian", "ian"},
            {"in", "in"},
            {"iang", "ang"},
            {"ing", "ing"},
            {"iong", "ong"},
            {"uai", "ai"},
            {"ui", "ei"},
            {"uei", "ei"},
            {"uan", "an"},
            {"un", "uen"},
            {"uang", "ang"},
            {"ueng", "eng"},
            {"van", "en"},
            {"vn", "vn"},
        };


        [YamlIgnore]
        public Dictionary<string, string> TailVowels {
            get {
                return FastTailVowelDict.Concat(SlowTailVowelDict).ToDictionary(g => g.Key, g => g.Value);
            }
        }


        [YamlIgnore]
        public string[] Consonants {
            get {
                return ConsonantDict.OrderByDescending(c => c.Length).ToArray();
            }
        }
    }

    /// <summary>
    /// Custom event to make arrays in inline style when serializing yaml
    /// </summary>
    class FlowStyleIntegerSequences : ChainedEventEmitter {
        public FlowStyleIntegerSequences(IEventEmitter nextEmitter)
            : base(nextEmitter) { }

        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter) {
            eventInfo = new SequenceStartEventInfo(eventInfo.Source) {
                Style = YamlDotNet.Core.Events.SequenceStyle.Flow
            };

            nextEmitter.Emit(eventInfo, emitter);
        }
    }


    /// <summary>
    /// Phonemizer
    /// </summary>
    [Phonemizer("Chinese CVV Plus Phonemizer", "ZH CVV+", "2xxbin", language: "ZH")]
    public class ChineseCVVPlusPhonemizer : BaseChinesePhonemizer {
        private USinger? singer;
        /// <summary>
        /// Variable containing zhcvvplus.yaml
        /// </summary>
        ChineseCVVPlusConfigYaml Config;
        public override void SetSinger(USinger singer) {

            if (singer == null) {
                return;
            }

            // Specify the path of zhcvvplus.yaml
            var configPath = Path.Join(singer.Location, "zhcvvplus.yaml");

            // If it doesn't exist, create and add it
            if (!File.Exists(configPath)) {
                CreateConfigChineseCVVPlus(configPath);
            }

            // Read zhcvvplus.yaml
            try {
                var configContent = File.ReadAllText(configPath);
                var deserializer = new DeserializerBuilder().Build();
                Config = deserializer.Deserialize<ChineseCVVPlusConfigYaml>(configContent);
            } catch (Exception e) {
                Log.Error(e, $"Failed to load zhcvvplus.yaml (configPath: '{configPath}')");
                try {
                    CreateConfigChineseCVVPlus(configPath);
                } catch (Exception e2) {
                    Log.Error(e2, "Failed to create zhcvvplus.yaml");
                }
            }

            // Specify the singer
            this.singer = singer;

            if (Config == null) {
                Log.Error("Failed to load zhcvvplus.yaml, using default settings.");
                Config = new ChineseCVVPlusConfigYaml();
            }
        }

        // Method that takes the lyrics of a note and returns the vowel.
        private string GetLyricVowel(string lyric) {
            string initialPrefix = string.Empty;

            // Handle the case that first character is not an alphabet(e.g "- qian") - remove it until the first alphabet apears, otherwise GetLyricVowel will return its lyric as it is.
            while (!char.IsLetter(lyric.First())) {
                initialPrefix += lyric.First();
                lyric = lyric.Remove(0, 1);
                if (lyric.Length == 0) {
                    return lyric;
                }
            }

            // Split the first two characters of the lyrics to prevent the issue of removing vowels, not just consonants (e.g., ian -> ia)
            string prefix = lyric.Substring(0, Math.Min(2, lyric.Length));
            string suffix = lyric.Length > 2 ? lyric.Substring(2) : string.Empty;

            // Iterate through the consonant list in order and replace them.
            foreach (var consonant in Config.Consonants) {
                if (prefix.StartsWith(consonant)) {
                    prefix = prefix.Replace(consonant, string.Empty);
                }
            }

            // Convert vowel notation to the standard form
            return $"{initialPrefix}{(prefix + suffix).Replace("yu", "v").Replace("y", "i").Replace("w", "u").Trim()}";
        }

        // Method to check if the alias exists in oto.ini.
        public static bool isExistPhonemeInOto(USinger singer, string phoneme, Note note) {

            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            string color = attr.voiceColor ?? string.Empty;

            var toneShift = 0;
            int? alt = null;
            if (phoneme.Equals(string.Empty)) {
                return false;
            }

            if (singer.TryGetMappedOto(phoneme + alt, note.tone + toneShift, color, out var otoAlt)) {
                return true;
            } else if (singer.TryGetMappedOto(phoneme, note.tone + toneShift, color, out var oto)) {
                return true;
            } else if (singer.TryGetMappedOto(phoneme, note.tone, color, out oto)) {
                return true;
            }

            return false;
        }

        static string GetOtoAlias(USinger singer, string phoneme, Note note) {
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            string color = attr.voiceColor ?? string.Empty;
            int? alt = attr.alternate;
            var toneShift = attr.toneShift;


            if (singer.TryGetMappedOto(phoneme + alt, note.tone + toneShift, color, out var otoAlt)) {
                return otoAlt.Alias;
            } else if (singer.TryGetMappedOto(phoneme, note.tone + toneShift, color, out var oto)) {
                return oto.Alias;
            } else if (singer.TryGetMappedOto(phoneme, note.tone, color, out oto)) {
                return oto.Alias;
            }
            return phoneme;
        }

        void CreateConfigChineseCVVPlus(string configPath) {
            Log.Information("Cannot Find zhcvvplus.yaml, creating a new one...");
            var serializer = new SerializerBuilder().WithEventEmitter(next => new FlowStyleIntegerSequences(next)).Build();
            var configContent = serializer.Serialize(new ChineseCVVPlusConfigYaml { });
            File.WriteAllText(configPath, configContent);
            Log.Information("New zhcvvplus.yaml created with default values.");
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            try {
                
                int totalDuration = notes.Sum(n => n.duration);
                string phoneme = notes[0].lyric;
                string? lryicVowel = GetLyricVowel(notes[0].lyric);

                // If a phonetic hint exists.
                if (notes[0].phoneticHint != null) {
                    // Phonetic hints are separated by commas.
                    var phoneticHints = notes[0].phoneticHint.Split(",");
                    var phonemes = new Phoneme[phoneticHints.Length];

                    foreach (var phoneticHint in phoneticHints.Select((hint, index) => (hint, index))) {
                        phonemes[phoneticHint.index] = new Phoneme {
                            phoneme = GetOtoAlias(singer, phoneticHint.hint.Trim(), notes[0]) ,
                            // The position is evenly divided into n parts.
                            position = totalDuration - ((totalDuration / phoneticHints.Length) * (phoneticHints.Length - phoneticHint.index)),
                        };
                    }

                    return new Result {
                        phonemes = phonemes,
                    };
                }

                // If the note is an End Breath note
                if (Config.SupportedTailBreath.Contains(phoneme) && prevNeighbour != null) {
                    phoneme = GetOtoAlias(singer, $"{GetLyricVowel(prevNeighbour?.lyric)} {phoneme}", notes[0]);
                    
                    return new Result {
                        // Output in the form "Basic vowel shape + End Breath written with lyrics"
                        phonemes = new Phoneme[] { new Phoneme { phoneme = phoneme } }
                    };
                }

                // If retan is set to True in zhcvvplus.yaml, there is no previous note, and the "- lyrics" alias exists in oto.ini
                if (Config.UseRetan && prevNeighbour == null && isExistPhonemeInOto(singer, $"- {phoneme}", notes[0])) {
                    // 가사를 "- 가사"로 변경
                    phoneme = $"- {phoneme}";
                    phoneme = GetOtoAlias(singer, phoneme, notes[0]);
                }

                // If the lyrics require a tail vowel
                if (Config.TailVowels.ContainsKey(lryicVowel)) {
                    // Declare the lyrics for the connecting note
                    var tailPhoneme = $"{Config.VowelTailPrefix}{Config.TailVowels[lryicVowel]}";

                    // 1. When the length of the note is less than or equal to the judgment tick in zhcvvplus.yaml
                    // 1-1. The lyrics are a Nasal Vowel, and the single use of Nasal Vowel in zhcvvplus.yaml is set to True, or
                    // 1-2. The lyrics are a Multiple Vowel, and the single use of Multiple Vowel in zhcvvplus.yaml is set to True
                    // 2. Or when the single use of Nasal Vowel in zhcvvplus.yaml is set to False, and the lyrics are a Nasal Vowel
                    // 3. Or when the single use of Multiple Vowel in zhcvvplus.yaml is set to False, and the lyrics are a Multiple Vowel
                    if ((totalDuration <= Config.SingleVowelsReferenceTimimgTick &&
                        (Config.UseSingleNasalVowel && Config.NasalVowelDict.Contains(lryicVowel)
                        || Config.UseSingleMultipleVowel && Config.MultipleVowelDict.Contains(lryicVowel)) ||
                        (!Config.UseSingleNasalVowel && Config.NasalVowelDict.Contains(lryicVowel)) ||
                        (!Config.UseSingleMultipleVowel && Config.MultipleVowelDict.Contains(lryicVowel)))) {

                        // To ensure naturalness, the position of the tail vowel is set to 1/3 of the note.
                        var tailVowelPosition = totalDuration - totalDuration / 3;

                        // If it is a fast tail vowel,
                        if (Config.FastTailVowelDict.ContainsKey(lryicVowel)) {
                            // Change to the position specified in zhcvvplus.yaml.
                            tailVowelPosition = Config.FastTailVowelTimingTick;
                        }
                        phoneme = GetOtoAlias(singer, phoneme, notes[0]);
                        tailPhoneme = GetOtoAlias(singer, tailPhoneme, notes[0]);
                        return new Result() {
                            phonemes = new Phoneme[] {
                                new Phoneme { phoneme = phoneme }, // Original note lyrics
                                new Phoneme { phoneme = tailPhoneme, position = tailVowelPosition}, // Tail vowel
                            }
                        };
                    }
                };

                // If it does not match any of the above if statements,
                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = GetOtoAlias(singer, phoneme, notes[0]), // 防止无尾音歌词在多音阶情况下丢失prefix map suffix
                        }
                    }
                };
            } catch (Exception e) { 
                Log.Error(e, "An error occurred during the phoneme processing in zh cvv+ module."); // Logging

                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = "ERROR", 
                        }
                    }
                };
            }
        }
    }
}
