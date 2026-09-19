using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Core.Ustx;
using Serilog;
using WanaKanaNet;

namespace OpenUtau.Classic {
    public class ClassicSinger : USinger, IDisposable {
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
        public override IList<string> Errors => data.errors;
        public override string Avatar => voicebank.Image == null ? null : Path.Combine(Location, voicebank.Image);
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null ? null : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string DefaultPhonemizer => voicebank.DefaultPhonemizer;
        public override string Sample => voicebank.Sample == null ? null : Path.Combine(Location, voicebank.Sample);
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => data.subbanks;
        public override IList<UOto> Otos => data.otos;
        public object SessionLock { get; } = new object();

        /// <summary>
        /// Everything Load() produces, published as one immutable unit so readers on
        /// other threads (phonemizer, renderers, UI) never see a half-built oto map
        /// during a reload. Readers copy the reference once per call.
        /// </summary>
        sealed class OtoData {
            public static readonly OtoData Empty = new OtoData();
            public readonly List<UOtoSet> otoSets = new List<UOtoSet>();
            public readonly List<USubbank> subbanks = new List<USubbank>();
            public readonly List<UOto> otos = new List<UOto>();
            public readonly Dictionary<string, UOto> otoMap = new Dictionary<string, UOto>();
            public readonly List<string> errors = new List<string>();
        }

        Voicebank voicebank;
        byte[] avatarData;
        volatile OtoData data = OtoData.Empty;
        OtoWatcher otoWatcher;

        public bool? UseFilenameAsAlias { get => voicebank.UseFilenameAsAlias; set => voicebank.UseFilenameAsAlias = value; }
        public Dictionary<string, IFrqFiles> Frqs { get; set; } = new Dictionary<string, IFrqFiles>();

        public ClassicSinger(Voicebank voicebank) {
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
                voicebank.Reload();
                Load();
                loaded = true;
                if (otoWatcher == null) {
                    otoWatcher = new OtoWatcher(this, Location);
                }
                OtoDirty = false;
            } catch (Exception e) {
                Log.Error(e, $"Failed to load {voicebank.File}");
            }
        }

        void Load() {
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

            var d = new OtoData();
            d.subbanks.AddRange(voicebank.Subbanks
                .OrderByDescending(subbank => subbank.Prefix.Length + subbank.Suffix.Length)
                .Select(subbank => new USubbank(subbank)));
            var groups = d.subbanks.GroupBy(subbank => $"^{Regex.Escape(subbank.Prefix)}(.*){Regex.Escape(subbank.Suffix)}$")
                .Select(group => new KeyValuePair<Regex, USubbank[]>(new Regex(group.Key), group.ToArray()));

            var dummy = new USubbank[] { new USubbank(new Subbank()) };
            foreach (var otoSet in voicebank.OtoSets) {
                var uSet = new UOtoSet(otoSet, voicebank.BasePath);
                d.otoSets.Add(uSet);
                foreach (var oto in otoSet.Otos) {
                    if (!oto.IsValid) {
                        if (!string.IsNullOrEmpty(oto.Error)) {
                            d.errors.Add(oto.Error);
                        }
                        continue;
                    }
                    UOto? uOto = null;
                    foreach (var group in groups) {
                        var m = group.Key.Match(oto.Alias);
                        if (m.Success) {
                            oto.Phonetic = m.Groups[1].Value;
                            uOto = new UOto(oto, uSet, group.Value);
                            break;
                        }
                    }
                    if (uOto == null) {
                        uOto = new UOto(oto, uSet, dummy);
                    }
                    d.otos.Add(uOto);
                    if (!d.otoMap.ContainsKey(oto.Alias)) {
                        d.otoMap.Add(oto.Alias, uOto);
                    } else {
                        //Errors.Add($"oto conflict {Otos[oto.Alias].Set}/{oto.Alias} and {otoSet.Name}/{oto.Alias}");
                    }
                }
            }

            foreach (var oto in d.otoMap.Values) {
                oto.SearchTerms.Add(oto.Alias.ToLowerInvariant().Replace(" ", ""));
                try {
                    oto.SearchTerms.Add(WanaKana.ToRomaji(oto.Alias).ToLowerInvariant().Replace(" ", ""));
                } catch { }
            }

            // Single atomic publish: readers see either the old snapshot or this one.
            data = d;
        }

        public override void Save() {
            try {
                otoWatcher.Paused = true;
                foreach (var oto in Otos) {
                    oto.WriteBack();
                }
                VoicebankLoader.WriteOtoSets(voicebank);
            } finally {
                otoWatcher.Paused = false;
            }
        }

        public void Dispose() {
            otoWatcher?.Dispose();
            otoWatcher = null;
        }

        public override void FreeMemory() {
            Log.Information($"Freeing memory for singer {Id}");
            lock (SessionLock) {
                Dispose();
                data = OtoData.Empty;
                loaded = false;
            }
        }

        public override bool TryGetOto(string phoneme, out UOto oto) {
            return data.otoMap.TryGetValue(phoneme, out oto);
        }

        public override bool TryGetMappedOto(string phoneme, int tone, out UOto oto) {
            return TryGetMappedOto(data, phoneme, tone, out oto);
        }

        public override bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
            var d = data;
            var subbank = d.subbanks.Find(subbank => subbank.Color == color && subbank.toneSet.Contains(tone));
            if (subbank != null && d.otoMap.TryGetValue($"{subbank.Prefix}{phoneme}{subbank.Suffix}", out oto)) {
                return true;
            }
            return TryGetMappedOto(d, phoneme, tone, out oto);
        }

        static bool TryGetMappedOto(OtoData d, string phoneme, int tone, out UOto oto) {
            var subbank = d.subbanks.Find(subbank => string.IsNullOrEmpty(subbank.Color) && subbank.toneSet.Contains(tone));
            if (subbank != null && d.otoMap.TryGetValue($"{subbank.Prefix}{phoneme}{subbank.Suffix}", out oto)) {
                return true;
            }
            return d.otoMap.TryGetValue(phoneme, out oto);
        }

        public override Dictionary<string, UOto> GetSuggestions(string text, bool isAlias) {
            if (text != null) {
                text = text.ToLowerInvariant().Replace(" ", "");
            }
            bool all = string.IsNullOrEmpty(text);
            var filtered = data.otoMap.Values
                .Where(oto => all || oto.SearchTerms.Exists(term => term.Contains(text)))
                .ToList();

            var result = new Dictionary<string, UOto>();
            if (!isAlias) {
                foreach (var oto in filtered) {
                    if (!string.IsNullOrEmpty(oto.Phonetic)) {
                        result.TryAdd(oto.Phonetic, oto);
                    }
                }
                result = result
                    .OrderBy(pair => pair.Key.Length)
                    .ThenBy(pair => pair.Key)
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            foreach (var oto in filtered) {
                if (!string.IsNullOrEmpty(oto.Alias)) {
                    result.TryAdd(oto.Alias, oto);
                }
            }
            return result;
        }

        public override byte[] LoadPortrait() {
            return string.IsNullOrEmpty(Portrait)
                ? null
                : File.ReadAllBytes(Portrait);
        }
    }
}
