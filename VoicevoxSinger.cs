using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Voicevox {
    public class VoicevoxSinger : USinger {
        public override string Id => voicebank.Id;
        public override string Name => voicebank.Name;
        public override Dictionary<string, string> LocalizedNames => voicebank.LocalizedNames;
        public override USingerType SingerType => voicebank.SingerType;
        public override string BasePath => voicebank.BasePath;
        public override string Author => voicebank.Author;
        public override string Voice => voicebank.Voice;
        public override string Location => Path.GetDirectoryName(voicebank.File);
        public override string Web => voicebank.Web;
        public override string Version => voicebank.Version;
        public override string OtherInfo => voicebank.OtherInfo;
        public override IList<string> Errors => errors;
        public override string Avatar => voicebank.Image == null ? voicevoxConfig == null ? null : voicevoxConfig.style_infos[0].icon == null ? null : voicevoxConfig.style_infos[0].icon : Path.Combine(Location, voicebank.Image);
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null ? voicevoxConfig == null ? null : voicevoxConfig.portraitPath == null ? null : voicevoxConfig.style_infos[0].portrait : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string Sample => voicebank.Sample == null ? null : Path.Combine(Location, voicebank.Sample);
        public override string DefaultPhonemizer => voicebank.DefaultPhonemizer;
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => subbanks;
        public override IList<UOto> Otos => otos;

        Voicebank voicebank;
        public VoicevoxConfig voicevoxConfig;
        List<string> errors = new List<string>();
        List<USubbank> subbanks = new List<USubbank>();
        List<UOto> otos = new List<UOto>();
        Dictionary<string, UOto> otoMap = new Dictionary<string, UOto>();

        HashSet<string> phonemes = new HashSet<string>();
        Dictionary<string, string[]> table = new Dictionary<string, string[]>();

        public byte[] avatarData;

        public VoicevoxSinger(Voicebank voicebank) {
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
            try {
                voicevoxConfig = VoicevoxConfig.Load(this);
                voicebank.Reload();
                Load();
                loaded = true;
            } catch (Exception e) {
                Log.Error(e, $"Failed to load {voicebank.File}");
            }
        }

        void Load() {
            if (voicevoxConfig.version.Equals("1.15.0")) {
                Log.Error("It differs from the supported version.");
            }
            if(voicevoxConfig.style_infos == null) {
                voicevoxConfig.LoadInfo(voicevoxConfig,this.Location);
            }
            phonemes.Clear();
            table.Clear();
            otos.Clear();
            try {
                //Prepared for planned changes or additions to phonemizers.
                foreach (var str in VoicevoxUtils.phoneme_List.vowels) {
                    phonemes.Add(str);
                }
                foreach (var str in VoicevoxUtils.phoneme_List.consonants) {
                    phonemes.Add(str);
                }
                foreach (var str in VoicevoxUtils.phoneme_List.kanas) {
                    phonemes.Add(str.Key);
                }
                foreach (var str in VoicevoxUtils.phoneme_List.paus) {
                    phonemes.Add(str.Key);
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemes.yaml for {Name}");
            }

            subbanks.Clear();
            subbanks.Add(new USubbank(new Subbank() {
                Prefix = string.Empty,
                Suffix = string.Empty,
                ToneRanges = new[] { "C1-B7" },
                Color = ""
            })); ;
            voicevoxConfig.styles.ForEach(style => {
                if (style.type.Equals("frame_decode")) {
                    subbanks.Add(new USubbank(new Subbank() {
                        Prefix = string.Empty,
                        Suffix = style.name,
                        ToneRanges = new[] { "C1-B7" },
                        Color = style.name
                    }));
                } else {
                    subbanks.Add(new USubbank(new Subbank() {
                        Prefix = string.Empty,
                        Suffix = style.name + "_" + style.type,
                        ToneRanges = new[] { "C1-B7" },
                        Color = style.name + "_" + style.type
                    }));
                }
            });

            var dummyOtoSet = new UOtoSet(new OtoSet(), Location);
            foreach (var phone in phonemes) {
                foreach (var subbank in subbanks) {
                    var uOto = UOto.OfDummy(phone);
                    if (!otoMap.ContainsKey(uOto.Alias)) {
                        otos.Add(uOto);
                        otoMap.Add(uOto.Alias, uOto);
                    } else {
                        //Errors.Add($"oto conflict {Otos[oto.Alias].Set}/{oto.Alias} and {otoSet.Name}/{oto.Alias}");
                    }
                }
            }

            if (Avatar != null && File.Exists(Avatar)) {
                try {
                    using (var stream = new FileStream(Avatar, FileMode.Open)) {
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
            if(phoneme != null) {
                var parts = phoneme.Split();
                if (parts.All(p => phonemes.Contains(p))) {
                    oto = UOto.OfDummy(phoneme);
                    return true;
                }
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
    }
}
