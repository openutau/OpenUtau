using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    public class NeutrinoSinger : USinger {
        public override string Id => voicebank.Id;
        public override string Name => voicebank.Name;
        public override Dictionary<string, string> LocalizedNames => voicebank.LocalizedNames;
        public override USingerType SingerType => voicebank.SingerType;
        public override string BasePath => voicebank.BasePath;
        public override string Author => voicebank.Author;
        public override string Voice => voicebank.Voice;
        public override string Location => Path.GetDirectoryName(voicebank.File);
        public override string Web => voicebank.Web;
        public override string Version => singerVersion;
        public override string OtherInfo => voicebank.OtherInfo;
        public override IList<string> Errors => errors;
        public override string Avatar => voicebank.Image == null ? null : Path.Combine(Location, voicebank.Image);
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null ? null : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string Sample => voicebank.Sample == null ? null : Path.Combine(Location, voicebank.Sample);
        public override string DefaultPhonemizer => voicebank.DefaultPhonemizer;
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => subbanks;
        public override IList<UOto> Otos => otos;

        Voicebank voicebank;
        List<string> errors = new List<string>();
        List<USubbank> subbanks = new List<USubbank>();
        List<UOto> otos = new List<UOto>();
        Dictionary<string, UOto> otoMap = new Dictionary<string, UOto>();

        HashSet<string> phonemes = new HashSet<string>();
        Dictionary<string, string[]> table = new Dictionary<string, string[]>();

        public byte[] avatarData;
        public ulong voicebankNameHash;
        public string singerVersion = string.Empty;

        public NeutrinoSinger(Voicebank voicebank) {
            this.voicebank = voicebank;
            found = true;
        }

        public override void EnsureLoaded() {
            if (Loaded) {
                return;
            }
            Reload();
        }

        public override void Reload() {
            if (!Found) {
                return;
            }
            errors.Clear();
            try {
                voicebank.Reload();
                Load();
                loaded = true;
            } catch (Exception e) {
                errors.Add($"Failed to load {voicebank.File}: {e.Message}");
                Log.Error(e, $"Failed to load {voicebank.File}");
            }
        }

        void Load() {
            voicebankNameHash = Hash();
            phonemes.Clear();
            table.Clear();
            otos.Clear();
            otoMap.Clear();
            subbanks.Clear();

            LoadInfo();
            LoadSubbanks();
            LoadPhonemes();
            BuildOtos();
            LoadAvatar();
        }

        void LoadInfo() {
            singerVersion = string.Empty;
            string infoPath = Path.Join(Location, "info.toml");
            if (!File.Exists(infoPath)) {
                errors.Add($"info.toml not found at {infoPath}");
                return;
            }
            try {
                var info = TomlData.Load(infoPath);
                info.TryGetValue("acoustic", "version", out object? version);
                if (version != null) {
                    singerVersion = version.ToString() ?? string.Empty;
                }
                info.TryGetValue("", "version", out object? version_);
                if (version_ != null) {
                    singerVersion = version_.ToString() ?? string.Empty;
                }
            } catch (Exception e) {
                errors.Add($"Failed to load info.toml: {e.Message}");
                Log.Error(e, $"Failed to load info.toml for {Name}");
            }
        }

        void LoadSubbanks() {
            if (voicebank.Subbanks == null || voicebank.Subbanks.Count == 0 ||
                voicebank.Subbanks.Count == 1 && string.IsNullOrEmpty(voicebank.Subbanks[0].Color)) {
                subbanks.Add(new USubbank(new Subbank() {
                    Prefix = string.Empty,
                    Suffix = string.Empty,
                    ToneRanges = new[] { "C1-B7" },
                }));
            } else {
                subbanks.AddRange(voicebank.Subbanks
                    .Select(subbank => new USubbank(subbank)));
            }
        }

        void LoadPhonemes() {
            if (!NeutrinoUtils.TryResolveBasePath(singerVersion, out var basePath, out var error)) {
                errors.Add(error);
                Log.Error($"{error} for {Name}");
                return;
            }
            var tablePath = NeutrinoUtils.DicPath(basePath, NeutrinoUtils.TableFile);
            try {
                foreach (var line in File.ReadAllLines(tablePath)) {
                    if (line.Contains("#")) {
                        continue;
                    }
                    var parts = line.Trim().Split();
                    table[parts[0]] = parts.Skip(1).ToArray();
                    foreach (var phoneme in table[parts[0]]) {
                        phonemes.Add(phoneme);
                    }
                }
            } catch (Exception e) {
                errors.Add($"Failed to load {NeutrinoUtils.TableFile}: {e.Message}");
                Log.Error(e, $"Failed to load {tablePath} for {Name}");
            }
            var confPath = NeutrinoUtils.DicPath(basePath, NeutrinoUtils.ConfFile);
            try {
                foreach (var line in File.ReadAllLines(confPath)) {
                    if (line.Contains('=')) {
                        var lineSplit = line.Split("=");
                        var value = lineSplit[1];
                        var phonemes_ = value.Trim(new char[] { '\"' }).Split(",");
                        foreach (var phoneme in phonemes_) {
                            phonemes.Add(phoneme);
                        }
                    }
                }
            } catch (Exception e) {
                errors.Add($"Failed to load {NeutrinoUtils.ConfFile}: {e.Message}");
                Log.Error(e, $"Failed to load {confPath} for {Name}");
            }
            phonemes.Add("pau");
            phonemes.Add("br");
        }

        void BuildOtos() {
            foreach (var phone in phonemes) {
                var uOto = UOto.OfDummy(phone);
                if (otoMap.ContainsKey(uOto.Alias)) {
                    continue;
                }
                otos.Add(uOto);
                otoMap.Add(uOto.Alias, uOto);
            }
        }

        void LoadAvatar() {
            if (Avatar != null && File.Exists(Avatar)) {
                try {
                    using (var stream = new FileStream(Avatar, FileMode.Open, FileAccess.Read)) {
                        using (var memoryStream = new MemoryStream()) {
                            stream.CopyTo(memoryStream);
                            avatarData = memoryStream.ToArray();
                        }
                    }
                } catch (Exception e) {
                    avatarData = null;
                    Log.Error(e, "Failed to load avatar data.");
                }
            } else {
                avatarData = null;
                Log.Error("Avatar can't be found");
            }
        }

        public override bool TryGetOto(string phoneme, out UOto oto) {
            var parts = phoneme.Split();
            if (parts.All(p => phonemes.Contains(p))) {
                oto = UOto.OfDummy(phoneme);
                return true;
            }
            oto = null;
            return false;
        }

        public override IEnumerable<UOto> GetSuggestions(string text) {
            if (text != null) {
                text = text.ToLowerInvariant().Replace(" ", "");
            }
            bool all = string.IsNullOrEmpty(text);
            return table.Keys
                .Where(key => all || key.Contains(text))
                .Select(key => UOto.OfDummy(key));
        }

        public override byte[] LoadPortrait() {
            return string.IsNullOrEmpty(Portrait)
                ? null
                : File.ReadAllBytes(Portrait);
        }

        public override byte[] LoadSample() {
            return string.IsNullOrEmpty(Sample)
                ? null
                : File.ReadAllBytes(Sample);
        }

        private ulong Hash() {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(Name);
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }
    }
}
