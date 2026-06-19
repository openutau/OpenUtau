using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;
using SharpCompress;

namespace OpenUtau.Classic {
    public class FileTrace {
        public string file = string.Empty;
        public int lineNumber;
        public string line = string.Empty;
        public FileTrace() { }
        public FileTrace(FileTrace other) {
            file = other.file;
            lineNumber = other.lineNumber;
            line = other.line;
        }
        public override string ToString() {
            return $"\"{file}\"\nat line {lineNumber + 1}:\n\"{line}\"";
        }
    }

    public class VoicebankLoader {
        public const string kCharTxt = "character.txt";
        public const string kCharYaml = "character.yaml";
        public const string kEnuconfigYaml = "enuconfig.yaml";
        public const string kDsconfigYaml = "dsconfig.yaml";
        public const string kConfigYaml = "config.yaml";
        public const string kOtoIni = "oto.ini";

        readonly string basePath;

        public static bool IsTest = false;

        public VoicebankLoader(string basePath) {
            this.basePath = basePath;
        }

        public IEnumerable<Voicebank> SearchAll() {
            List<Voicebank> result = new List<Voicebank>();
            if (!Directory.Exists(basePath)) {
                return result;
            }
            IEnumerable<string> files;
            if (Preferences.Default.LoadDeepFolderSinger) {
                files = Directory.EnumerateFiles(basePath, kCharTxt, SearchOption.AllDirectories);
            } else {
                // TopDirectoryOnly
                files = Directory.GetDirectories(basePath)
                    .SelectMany(path => Directory.EnumerateFiles(path, kCharTxt));
            }
            result.AddRange(files
                .Select(filePath => {
                    try {
                        var voicebank = new Voicebank();
                        LoadInfo(voicebank, filePath, basePath);
                        return voicebank;
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {filePath} info.");
                        return null;
                    }
                })
                .OfType<Voicebank>());
            return result;
        }

        public static void LoadVoicebank(Voicebank voicebank) {
            LoadInfo(voicebank, voicebank.File, voicebank.BasePath);
            LoadSubbanks(voicebank);
            LoadOtoSets(voicebank, Path.GetDirectoryName(voicebank.File));
        }

        public static void LoadInfo(Voicebank voicebank, string filePath, string basePath) {
            var dir = Path.GetDirectoryName(filePath);
            var yamlFile = Path.Combine(dir, kCharYaml);
            VoicebankConfig? bankConfig = null;
            if (File.Exists(yamlFile)) {
                try {
                    using (var stream = File.OpenRead(yamlFile)) {
                        bankConfig = VoicebankConfig.Load(stream);
                    }
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load yaml {yamlFile}");
                }
            }
            string singerType = bankConfig?.SingerType ?? string.Empty;
            if (SingerTypeUtils.SingerTypeFromName.ContainsKey(singerType)) {
                voicebank.SingerType = SingerTypeUtils.SingerTypeFromName[singerType];
            } else {
                // Legacy detection code. Do not add more here.
                var enuconfigFile = Path.Combine(dir, kEnuconfigYaml);
                var dsconfigFile = Path.Combine(dir, kDsconfigYaml);
                if (File.Exists(enuconfigFile)) {
                    voicebank.SingerType = USingerType.Enunu;
                } else if (File.Exists(dsconfigFile)) {
                    voicebank.SingerType = USingerType.DiffSinger;
                } else if (voicebank.SingerType != USingerType.Enunu) {
                    voicebank.SingerType = USingerType.Classic;
                }
            }
            Encoding encoding = Encoding.GetEncoding("shift_jis");
            if (!string.IsNullOrEmpty(bankConfig?.TextFileEncoding)) {
                encoding = Encoding.GetEncoding(bankConfig.TextFileEncoding);
            }
            using (var stream = File.OpenRead(filePath)) {
                ParseCharacterTxt(voicebank, stream, filePath, basePath, encoding);
            }
            if (bankConfig != null) {
                ApplyConfig(voicebank, bankConfig);
            }
        }

        public static void ParseCharacterTxt(Voicebank voicebank, Stream stream, string filePath, string basePath, Encoding encoding) {
            using (var reader = new StreamReader(stream, encoding)) {
                voicebank.BasePath = basePath;
                voicebank.File = filePath;
                voicebank.TextFileEncoding = encoding;
                var otherLines = new List<string>();
                while (!reader.EndOfStream) {
                    string line = reader.ReadLine().Trim();
                    var s = line.Split('=', 2);
                    if (s.Length < 2) {
                        s = line.Split(':', 2);
                    }
                    if (s.Length < 2) {
                        s = line.Split('：', 2);
                    }
                    Array.ForEach(s, temp => temp.Trim());
                    if (s.Length == 2) {
                        s[0] = s[0].ToLowerInvariant();
                        if (s[0] == "name") {
                            voicebank.Name = s[1];
                        } else if (s[0] == "名前" && string.IsNullOrEmpty(voicebank.Name)) {
                            voicebank.Name = s[1];
                        } else if (s[0] == "image") {
                            voicebank.Image = s[1];
                        } else if (s[0] == "author" || s[0] == "created by") {
                            voicebank.Author = s[1];
                        } else if (s[0].StartsWith("voice") || s[0] == "cv") {
                            voicebank.Voice = s[1];
                        } else if (s[0] == "sample") {
                            voicebank.Sample = s[1];
                        } else if (s[0] == "web") {
                            voicebank.Web = s[1];
                        } else if (s[0] == "version") {
                            voicebank.Version = s[1];
                        } else {
                            otherLines.Add(line);
                        }
                    } else {
                        otherLines.Add(line);
                    }
                }
                voicebank.OtherInfo = string.Join("\n", otherLines);
                voicebank.Id = Path.GetRelativePath(basePath, Path.GetDirectoryName(voicebank.File));
                if (string.IsNullOrEmpty(voicebank.Name)) {
                    voicebank.Name = $"No Name ({voicebank.Id})";
                }
            }
        }

        public static void ApplyConfig(Voicebank bank, VoicebankConfig bankConfig) {
            if (!string.IsNullOrWhiteSpace(bankConfig.Name)) {
                bank.Name = bankConfig.Name;
            }
            if (bankConfig.LocalizedNames != null) {
                foreach (var kv in bankConfig.LocalizedNames) {
                    bank.LocalizedNames[kv.Key] = kv.Value;
                }
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.Image)) {
                bank.Image = bankConfig.Image;
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.Portrait)) {
                bank.Portrait = bankConfig.Portrait;
                bank.PortraitOpacity = bankConfig.PortraitOpacity;
                bank.PortraitHeight = bankConfig.PortraitHeight;
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.Author)) {
                bank.Author = bankConfig.Author;
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.Voice)) {
                bank.Voice = bankConfig.Voice;
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.Web)) {
                bank.Web = bankConfig.Web;
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.Version)) {
                bank.Version = bankConfig.Version;
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.Sample)) {
                bank.Sample = bankConfig.Sample;
            }
            if (!string.IsNullOrWhiteSpace(bankConfig.DefaultPhonemizer)) {
                bank.DefaultPhonemizer = bankConfig.DefaultPhonemizer;
            }
            if (bankConfig.Subbanks != null && bankConfig.Subbanks.Length > 0) {
                foreach (var subbank in bankConfig.Subbanks) {
                    subbank.Color ??= string.Empty;
                    subbank.Prefix ??= string.Empty;
                    subbank.Suffix ??= string.Empty;
                }
                bank.Subbanks.AddRange(bankConfig.Subbanks);
            }
            if (bank.SingerType is USingerType.Classic && bankConfig.UseFilenameAsAlias != null) {
                bank.UseFilenameAsAlias = bankConfig.UseFilenameAsAlias;
            }
        }

        public static void LoadSubbanks(Voicebank voicebank) {
            if (voicebank.Subbanks.Count == 0) {
                LoadPrefixMap(voicebank);
            }
            if (voicebank.Subbanks.Count == 0) {
                voicebank.Subbanks.Add(new Subbank() {
                    ToneRanges = new string[0],
                });
            }
        }

        public static void LoadPrefixMap(Voicebank voicebank) {
            var dir = Path.GetDirectoryName(voicebank.File);
            var filePath = Path.Combine(dir, "prefix.map");
            if (File.Exists(filePath)) {
                LoadMap(voicebank, filePath, string.Empty);
            }

            // Append.map for presamp
            var mapDir = Path.Combine(dir, "prefix");
            if (Directory.Exists(mapDir)) {
                var maps = Directory.EnumerateFiles(mapDir, "*.map");
                foreach (string mapPath in maps) {
                    LoadMap(voicebank, mapPath, Path.GetFileNameWithoutExtension(mapPath));
                }
            }
        }
        public static void LoadMap(Voicebank voicebank, string filePath, string color) {
            try {
                using (var stream = File.OpenRead(filePath)) {
                    var map = ParsePrefixMap(stream, voicebank.TextFileEncoding);
                    foreach (var kv in map) {
                        var subbank = new Subbank() {
                            Color = color,
                            Prefix = kv.Key.Item1,
                            Suffix = color + kv.Key.Item2,
                        };
                        var toneRanges = new List<string>();
                        int? rangeStart = null;
                        int? rangeEnd = null;
                        for (int i = 24; i <= 108; i++) {
                            if (kv.Value.Contains(i) && i < 108) {
                                if (rangeStart == null) {
                                    rangeStart = i;
                                } else {
                                    rangeEnd = i;
                                }
                            } else if (rangeStart != null) {
                                if (rangeEnd != null) {
                                    toneRanges.Add($"{MusicMath.GetToneName(rangeStart.Value)}-{MusicMath.GetToneName(rangeEnd.Value)}");
                                } else {
                                    toneRanges.Add($"{MusicMath.GetToneName(rangeStart.Value)}");
                                }
                                rangeStart = null;
                                rangeEnd = null;
                            }
                        }
                        subbank.ToneRanges = toneRanges.ToArray();
                        voicebank.Subbanks.Add(subbank);
                    }
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load {filePath}");
            }

        }
        public static Dictionary<Tuple<string, string>, SortedSet<int>> ParsePrefixMap(Stream stream, Encoding encoding) {
            using (var reader = new StreamReader(stream, encoding)) {
                var result = new Dictionary<Tuple<string, string>, SortedSet<int>>();
                while (!reader.EndOfStream) {
                    var s = reader.ReadLine().Split('\t');
                    if (s.Length == 3) {
                        int tone = MusicMath.NameToTone(s[0]);
                        var key = Tuple.Create(s[1], s[2]);
                        if (!result.TryGetValue(key, out var tones)) {
                            tones = new SortedSet<int>();
                            result[key] = tones;
                        }
                        tones.Add(tone);
                    }
                }
                return result;
            }
        }

        public static void LoadOtoSets(Voicebank voicebank, string dirPath) {
            var otoFile = Path.Combine(dirPath, kOtoIni);
            if (File.Exists(otoFile)) {
                var otoSet = ParseOtoSet(otoFile, voicebank.TextFileEncoding, voicebank.UseFilenameAsAlias);
                var voicebankDir = Path.GetDirectoryName(voicebank.File);
                otoSet.Name = Path.GetRelativePath(voicebankDir, dirPath);
                if (otoSet.Name == ".") {
                    otoSet.Name = string.Empty;
                }
                voicebank.OtoSets.Add(otoSet);
            }
            var dirs = Directory.GetDirectories(dirPath);
            foreach (var dir in dirs) {
                LoadOtoSets(voicebank, dir);
            }
        }

        public static OtoSet ParseOtoSet(string filePath, Encoding encoding, bool? useFilenameAsAlias) {
            try {
                var otoDeclaredEncoding = GetOtoDeclaredEncoding(filePath);
                if (otoDeclaredEncoding != null) {
                    encoding = otoDeclaredEncoding;
                }
                using (var stream = File.OpenRead(filePath)) {
                    var otoSet = ParseOtoSet(stream, filePath, encoding);
                    if (!IsTest) {
                        CheckWavExist(otoSet);
                    }
                    AddAliasForMissingFiles(otoSet);
                    if (useFilenameAsAlias == true) {
                        AddFilenameAlias(otoSet);
                    }
                    return otoSet;
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load {filePath}");
            }
            return null;
        }

        // Oto.ini can declare its own encoding at the beginning of the file with #Charset:
        static Encoding? GetOtoDeclaredEncoding(string filePath) {
            using (var reader = new StreamReader(filePath, Encoding.GetEncoding("shift_jis"))) {
                for (var i = 0; i < 10; i++) {
                    var line = reader.ReadLine();
                    if (line == null) {
                        break;
                    }
                    line = line.Trim();
                    if (line.StartsWith("#Charset:")) {
                        try {
                            return Encoding.GetEncoding(line.Replace("#Charset:", ""));
                        } catch (ArgumentException) {
                            return null;
                        }
                    }
                }
            }
            return null;
        }

        public static OtoSet ParseOtoSet(Stream stream, string filePath, Encoding encoding) {
            OtoSet otoSet;
            using (var reader = new StreamReader(stream, encoding)) {
                var trace = new FileTrace { file = filePath, lineNumber = 0 };
                otoSet = new OtoSet() {
                    File = filePath,
                };
                while (!reader.EndOfStream) {
                    var line = reader.ReadLine().Trim();
                    trace.line = line;
                    try {
                        Oto oto = ParseOto(line, trace);
                        if (oto != null) {
                            otoSet.Otos.Add(oto);
                        }
                        if (!string.IsNullOrEmpty(oto.Error)) {
                            Log.Error($"Failed to parse\n{oto.Error}");
                        }
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to parse\n{trace}");
                    }
                    trace.line = null;
                    trace.lineNumber++;
                }
            }
            return otoSet;
        }

        static void AddAliasForMissingFiles(OtoSet otoSet) {
            // Use filename as alias if not in oto.
            var knownFiles = otoSet.Otos.Where(oto => oto.IsValid).Select(oto => oto.Wav).ToHashSet();
            var dir = Path.GetDirectoryName(otoSet.File);
            foreach (var wav in Directory.EnumerateFiles(dir, "*.wav", SearchOption.TopDirectoryOnly)) {
                var file = Path.GetFileName(wav);
                if (!knownFiles.Contains(file)) {
                    var oto = new Oto {
                        Alias = Path.GetFileNameWithoutExtension(file),
                        Wav = file,
                        FileTrace = null,
                    };
                    oto.Phonetic = oto.Alias;
                    otoSet.Otos.Add(oto);
                }
            }
        }

        static void CheckWavExist(OtoSet otoSet) {
            var wavGroups = otoSet.Otos.Where(oto => oto.IsValid).GroupBy(oto => oto.Wav);
            var dir = Path.GetDirectoryName(otoSet.File);
            var NFDFiles = Directory.GetFiles(dir, "*.wav")
                .Select(file => Path.GetFileName(file))
                .Where(file => !file.IsNormalized())
                .ToDictionary(file => file.Normalize());

            foreach (var group in wavGroups) {
                string path = Path.Combine(dir, group.Key);
                if (!File.Exists(path)) {
                    if (NFDFiles.TryGetValue(group.Key.Normalize(), out string NFDFile)) {
                        foreach (var oto in group) {
                            oto.Wav = NFDFile;
                        }
                    } else {
                        Log.Error($"Sound file missing. {path}");
                        foreach (Oto oto in group) {
                            if (string.IsNullOrEmpty(oto.Error)) {
                                oto.Error = $"Sound file missing. {path}";
                            }
                            oto.IsValid = false;
                        }
                    }
                }
            }
        }

        static void AddFilenameAlias(OtoSet otoSet) {
            // Use filename as alias.
            var files = otoSet.Otos.Where(oto => oto.IsValid).Select(oto => oto.Wav).Distinct().ToList();
            foreach (var wav in files) {
                string filename = Path.GetFileNameWithoutExtension(wav);
                if (!otoSet.Otos.Any(oto => oto.Alias == filename)) {
                    var reference = otoSet.Otos.OrderBy(oto => oto.Offset).First(oto => oto.Wav == wav);
                    var oto = new Oto {
                        Alias = filename,
                        Phonetic = filename,
                        Wav = wav,
                        Offset = reference.Offset,
                        Consonant = reference.Consonant,
                        Cutoff = reference.Cutoff,
                        Preutter = reference.Preutter,
                        Overlap = reference.Overlap,
                        IsValid = true,
                        Error = reference.Error,
                        FileTrace = reference.FileTrace
                    };
                    otoSet.Otos.Add(oto);
                }
            }
        }

        static Oto ParseOto(string line, FileTrace trace) {
            const string format = "<wav>=<alias>,<offset>,<consonant>,<cutoff>,<preutter>,<overlap>";
            var oto = new Oto {
                FileTrace = new FileTrace(trace),
            };
            if (string.IsNullOrWhiteSpace(line)) {
                return oto;
            }
            var parts = line.Split('=');
            if (parts.Length < 2) {
                oto.Error = $"Line does not match format {format}.";
                return oto;
            }
            oto.Wav = parts[0].Trim();
            parts = parts[1].Split(',');
            oto.Alias = parts.ElementAtOrDefault(0);
            if (string.IsNullOrEmpty(oto.Alias)) {
                oto.Alias = RemoveExtension(oto.Wav);
            }
            oto.Phonetic = oto.Alias;
            if (!ParseDouble(parts.ElementAtOrDefault(1), out oto.Offset)) {
                oto.Error = $"{trace}\nFailed to parse offset. Format is {format}.";
                return oto;
            }
            if (!ParseDouble(parts.ElementAtOrDefault(2), out oto.Consonant)) {
                oto.Error = $"{trace}\nFailed to parse consonant. Format is {format}.";
                return oto;
            }
            if (!ParseDouble(parts.ElementAtOrDefault(3), out oto.Cutoff)) {
                oto.Error = $"{trace}\nFailed to parse cutoff. Format is {format}.";
                return oto;
            }
            if (!ParseDouble(parts.ElementAtOrDefault(4), out oto.Preutter)) {
                oto.Error = $"{trace}\nFailed to parse preutter. Format is {format}.";
                return oto;
            }
            if (!ParseDouble(parts.ElementAtOrDefault(5), out oto.Overlap)) {
                oto.Error = $"{trace}\nFailed to parse overlap. Format is {format}.";
                return oto;
            }
            oto.IsValid = true;
            return oto;
        }

        static bool ParseDouble(string s, out double value) {
            if (string.IsNullOrEmpty(s)) {
                value = 0;
                return true;
            }
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static void WriteOtoSets(Voicebank voicebank) {
            foreach (var otoSet in voicebank.OtoSets) {
                using (var stream = File.Open(otoSet.File, FileMode.Create, FileAccess.Write)) {
                    WriteOtoSet(otoSet, stream, voicebank.TextFileEncoding);
                }
                Log.Information($"Write oto set {otoSet.Name}");
            }
        }

        public static void WriteOtoSet(OtoSet otoSet, Stream stream, Encoding encoding) {
            using (var writer = new StreamWriter(stream, encoding)) {
                foreach (var oto in otoSet.Otos) {
                    if (!oto.IsValid && (oto.FileTrace != null)) {
                        writer.Write(oto.FileTrace.line);
                        writer.Write('\n');
                        continue;
                    }
                    writer.Write(oto.Wav);
                    writer.Write('=');
                    writer.Write(oto.Alias);
                    writer.Write(',');
                    if (oto.Offset != 0) {
                        writer.Write(oto.Offset);
                    }
                    writer.Write(',');
                    if (oto.Consonant != 0) {
                        writer.Write(oto.Consonant);
                    }
                    writer.Write(',');
                    if (oto.Cutoff != 0) {
                        writer.Write(oto.Cutoff);
                    }
                    writer.Write(',');
                    if (oto.Preutter != 0) {
                        writer.Write(oto.Preutter);
                    }
                    writer.Write(',');
                    if (oto.Overlap != 0) {
                        writer.Write(oto.Overlap);
                    }
                    writer.Write('\n');
                }
                writer.Flush();
            }
        }

        static string RemoveExtension(string filePath) {
            var ext = Path.GetExtension(filePath);
            if (!string.IsNullOrEmpty(ext)) {
                return filePath.Substring(0, filePath.Length - ext.Length);
            } else {
                return filePath;
            }
        }
    }
}
