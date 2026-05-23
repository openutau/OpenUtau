using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Ustx {
    public class UOto : INotifyPropertyChanged {
        public string Alias { get; private set; }
        public string Phonetic { get; private set; }
        public string Set { get; private set; }
        public USubbank[] Subbanks { get; private set; }
        public string Color { get => string.Join(", ", Subbanks.Select(x => string.IsNullOrWhiteSpace(x.Color) ? "(main)" : x.Color)); }
        public string Prefix { get => Subbanks.First().Prefix; }
        public string Suffix { get => Subbanks.First().Suffix; }
        public string File { get; private set; }
        public string DisplayFile { get; private set; }
        public double Offset {
            get => offset;
            set {
                offset = Math.Max(0, Math.Round(value, 3));
                NotifyPropertyChanged(nameof(Offset));
            }
        }
        public double Consonant {
            get => consonant;
            set {
                consonant = Math.Max(0, Math.Round(value, 3));
                NotifyPropertyChanged(nameof(Consonant));
            }
        }
        public double Cutoff {
            get => cutoff;
            set {
                cutoff = Math.Round(value, 3);
                NotifyPropertyChanged(nameof(Cutoff));
            }
        }
        public double Preutter {
            get => preutter;
            set {
                preutter = Math.Max(0, Math.Round(value, 3));
                NotifyPropertyChanged(nameof(Preutter));
            }
        }
        public double Overlap {
            get => overlap;
            set {
                overlap = Math.Round(value, 3);
                NotifyPropertyChanged(nameof(Overlap));
            }
        }
        public OtoFrq? Frq { get; set; }
        public List<string> SearchTerms { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private Oto oto;
        private double offset;
        private double consonant;
        private double cutoff;
        private double preutter;
        private double overlap;

        public UOto() { }

        public UOto(Oto oto, UOtoSet set, USubbank[] subbanks) {
            this.oto = oto;
            Alias = oto.Alias;
            Phonetic = oto.Phonetic;
            Set = set.Name;
            Subbanks = subbanks;
            if (!string.IsNullOrEmpty(oto.Wav)) {
                File = Path.Combine(set.Location, oto.Wav);
            } else {
                File = string.Empty;
            }
            DisplayFile = oto?.Wav;
            Offset = oto.Offset;
            Consonant = oto.Consonant;
            Cutoff = oto.Cutoff;
            Preutter = oto.Preutter;
            Overlap = oto.Overlap;

            SearchTerms = new List<string>();
        }

        public static UOto OfDummy(string alias) => new UOto() {
            Alias = alias,
            Phonetic = alias,
        };

        public bool IsColorMatch(string color) {
            if (Subbanks.Any(s => s.Color == color)) return true;
            return false;
        }

        public void WriteBack() {
            oto.Offset = offset;
            oto.Consonant = consonant;
            oto.Cutoff = cutoff;
            oto.Preutter = preutter;
            oto.Overlap = overlap;
        }

        private void NotifyPropertyChanged(string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => Alias;
    }

    public class UOtoSet {
        public string Name => otoSet.Name;
        public readonly string Location;

        private readonly OtoSet otoSet;

        public UOtoSet(OtoSet otoSet, string singersPath) {
            this.otoSet = otoSet;
            if (!string.IsNullOrEmpty(otoSet.File)) {
                Location = Path.Combine(singersPath, Path.GetDirectoryName(otoSet.File));
            } else {
                Location = string.Empty;
            }
        }

        public override string ToString() => Name;
    }

    public class USubbank {
        public string Color {
            get => subbank.Color;
            set => subbank.Color = value;
        }
        public string Prefix {
            get => subbank.Prefix;
            set => subbank.Prefix = value;
        }
        public string Suffix {
            get => subbank.Suffix;
            set => subbank.Suffix = value;
        }
        public string ToneRangesString {
            get => toneRangesString;
            set {
                subbank.ToneRanges = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
                toneRangesString = value;
            }
        }

        public readonly SortedSet<int> toneSet;
        public readonly Subbank subbank;

        private string toneRangesString;

        public USubbank(Subbank subbank) {
            this.subbank = subbank;
            toneSet = new SortedSet<int>();
            if (subbank.ToneRanges != null) {
                toneRangesString = string.Join(',', subbank.ToneRanges);
                foreach (var range in subbank.ToneRanges) {
                    AddToneRange(range, toneSet);
                }
            } else {
                toneRangesString = string.Empty;
            }
        }

        private static void AddToneRange(string range, SortedSet<int> set) {
            var parts = range.Split('-');
            if (parts.Length == 1) {
                int tone = MusicMath.NameToTone(parts[0]);
                if (tone > 0) {
                    set.Add(tone);
                }
            } else if (parts.Length == 2) {
                int start = MusicMath.NameToTone(parts[0]);
                int end = MusicMath.NameToTone(parts[1]);
                if (start > 0 && end > 0) {
                    for (int i = start; i <= end; ++i) {
                        set.Add(i);
                    }
                }
            }
        }

        public override string ToString() {
            return $"color:{Color}, suffix:{Suffix}";
        }
    }

    [Flags] public enum USingerType { Classic = 0x1, Enunu = 0x2, Vogen = 0x4, DiffSinger = 0x5, Voicevox = 0x6 }

    public static class SingerTypeUtils {
        public static Dictionary<USingerType?, string> SingerTypeNames = new Dictionary<USingerType?, string>(){
            {USingerType.Classic, "utau"},
            {USingerType.Enunu, "enunu"},
            {USingerType.DiffSinger, "diffsinger"},
            {USingerType.Voicevox, "voicevox"},
        };

        public static Dictionary<string, USingerType> SingerTypeFromName = new Dictionary<string, USingerType>(){
            {"utau", USingerType.Classic},
            {"enunu", USingerType.Enunu},
            {"diffsinger", USingerType.DiffSinger},
            {"voicevox", USingerType.Voicevox},
        };

    }

    public class USinger : INotifyPropertyChanged, IEquatable<USinger> {
        protected static readonly List<UOto> emptyOtos = new List<UOto>();

        public virtual string Id { get; }
        public virtual string Name => name;
        public virtual Dictionary<string, string> LocalizedNames { get; }
        public virtual USingerType SingerType { get; }
        public virtual string BasePath { get; }
        public virtual string Author { get; }
        public virtual string Voice { get; }
        public virtual string Location { get; }
        public virtual string Web { get; }
        public virtual string Version { get; }
        public virtual string OtherInfo { get; }
        public virtual IList<string> Errors { get; }
        public virtual string Avatar { get; }
        public virtual byte[] AvatarData { get; }
        public virtual string Portrait { get; }
        public virtual float PortraitOpacity { get; }
        public virtual int PortraitHeight { get; }
        public virtual string Sample { get; }
        public virtual string DefaultPhonemizer { get; }
        public virtual Encoding TextFileEncoding => Encoding.UTF8;
        public virtual IList<USubbank> Subbanks { get; }
        public virtual IList<UOto> Otos => emptyOtos;

        public bool Found => found;
        public bool Loaded => found && loaded;
        public bool OtoDirty {
            get => otoDirty;
            set {
                otoDirty = value;
                NotifyPropertyChanged(nameof(OtoDirty));
            }
        }
        public bool IsFavourite {
            get => Preferences.Default.FavoriteSingers.Contains(Id);
            set {
                if (value) {
                    if (!Preferences.Default.FavoriteSingers.Contains(Id)) {
                        Preferences.Default.FavoriteSingers.Add(Id);
                    }
                } else {
                    Preferences.Default.FavoriteSingers.Remove(Id);
                }
                Preferences.Save();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected bool found;
        protected bool loaded;
        protected bool otoDirty;

        private string name;

        public string LocalizedName {
            get {
                if (LocalizedNames == null) {
                    return Found ? Name : $"[Missing] {Name}";
                }
                string language = Preferences.Default.SortingOrder;
                if (language == null) {
                    language = Preferences.Default.Language;
                }
                if (string.IsNullOrEmpty(language)) { // InvariantCulture
                    return Found ? Name : $"[Missing] {Name}";
                }
                if (LocalizedNames.TryGetValue(language, out var localizedName)) {
                    return Found ? localizedName : $"[Missing] {localizedName}";
                } else {
                    return Found ? Name : $"[Missing] {Name}";
                }
            }
        }

        public virtual void EnsureLoaded() { }
        public virtual void Reload() { }
        public virtual void Save() { }
        public virtual bool TryGetOto(string phoneme, out UOto oto) {
            oto = default;
            return false;
        }
        public virtual bool TryGetMappedOto(string phoneme, int tone, out UOto oto) {
            return TryGetOto(phoneme, out oto);
        }
        public virtual bool TryGetMappedOto(string phoneme, int tone, string color, out UOto oto) {
            return TryGetOto(phoneme, out oto);
        }

        public virtual IEnumerable<UOto> GetSuggestions(string text) { return emptyOtos; }
        public virtual byte[] LoadPortrait() => null;
        public virtual byte[] LoadSample() => null;
        public override string ToString() => LocalizedName;
        public bool Equals(USinger other) {
            // Tentative: Since only the singer's Id is recorded in ustx and preferences, singers with the same Id are considered identical.
            // Singer with the same directory name in different locations may be identical.
            if (other != null && other.Id == this.Id) {
                return true;
            } else {
                return false;
            }
        }
        public override int GetHashCode() => Id.GetHashCode();

        public static USinger CreateMissing(string name) {
            return new USinger() {
                found = false,
                loaded = false,
                name = name,
            };
        }

        private void NotifyPropertyChanged(string propertyName = "") {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Some types of singers store their data in memory when rendering.
        /// This method is called when the singer is no longer used.
        /// Note:
        /// - the voicebank may be used again even after this method is called.
        /// - this method may be called even when the singer has not been used
        /// </summary>
        public virtual void FreeMemory() { }
    }
}
