using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    partial class VoiceColor : ReactiveObject {
        [Reactive] public partial string Name { get; set; }
        [Reactive] public partial ObservableCollectionExtended<VoiceColorRow> Rows { get; set; }
        public VoiceColor(string color, List<Subbank> subbanks) {
            Name = color;
            Rows = new ObservableCollectionExtended<VoiceColorRow>();
            for (int i = 107; i >= 24; --i) {
                Rows.Add(new VoiceColorRow(i, string.Empty, string.Empty));
            }
            foreach (var subbank in subbanks) {
                foreach (var range in subbank.ToneRanges) {
                    var parts = range.Split('-');
                    if (parts.Length == 1) {
                        int tone = MusicMath.NameToTone(parts[0]);
                        if (tone >= 24 && tone <= 107) {
                            Rows[107 - tone].Prefix = subbank.Prefix;
                            Rows[107 - tone].Suffix = subbank.Suffix;
                        }
                    } else if (parts.Length == 2) {
                        int start = MusicMath.NameToTone(parts[0]);
                        int end = MusicMath.NameToTone(parts[1]);
                        for (int tone = start; tone <= end; ++tone) {
                            if (tone >= 24 && tone <= 107) {
                                Rows[107 - tone].Prefix = subbank.Prefix;
                                Rows[107 - tone].Suffix = subbank.Suffix;
                            }
                        }
                    }
                }
            }
        }

        public override string ToString() =>
            string.IsNullOrEmpty(Name) ? "(main)" : Name;
    }

    partial class VoiceColorRow : ReactiveObject {
        public readonly int index;
        [Reactive] public partial string Tone { get; private set; }
        [Reactive] public partial string Prefix { get; set; }
        [Reactive] public partial string Suffix { get; set; }
        public VoiceColorRow(int index, string prefix, string suffix) {
            this.index = index;
            Tone = MusicMath.GetToneName(index);
            Prefix = prefix;
            Suffix = suffix;
        }
    }

    partial class EditSubbanksViewModel : ViewModelBase {
        [Reactive] public partial ObservableCollectionExtended<VoiceColor> Colors { get; set; }
        [Reactive] public partial VoiceColor? SelectedColor { get; set; }
        [Reactive] public partial bool IsEditableColor { get; set; }
        [Reactive] public partial ObservableCollectionExtended<VoiceColorRow>? Rows { get; set; }
        [Reactive] public partial ObservableCollectionExtended<VoiceColorRow>? SelectedRows { get; set; }
        [Reactive] public partial string Prefix { get; set; }
        [Reactive] public partial string Suffix { get; set; }

        public USinger? Singer { get; private set; }

        public EditSubbanksViewModel() {
            Colors = new ObservableCollectionExtended<VoiceColor>();
            this.WhenAnyValue(x => x.SelectedColor)
                .OfType<VoiceColor>()
                .Subscribe(color => {
                    Rows = color?.Rows;
                    IsEditableColor = color != null && !string.IsNullOrEmpty(color.Name);
                });
            Prefix = string.Empty;
            Suffix = string.Empty;
        }

        public void SetSinger(USinger singer) {
            this.Singer = singer;
            LoadSubbanks();
        }

        public void LoadSubbanks() {
            if (Singer == null) {
                return;
            }
            try {
                Colors.Clear();
                var colors = new Dictionary<string, List<Subbank>>();
                foreach (var subbank in Singer.Subbanks) {
                    if (!colors.TryGetValue(subbank.Color ?? string.Empty, out var subbanks)) {
                        subbanks = new List<Subbank>();
                        colors[subbank.Color ?? string.Empty] = subbanks;
                    }
                    subbanks.Add(subbank.subbank);
                }
                foreach (var key in colors.Keys.OrderBy(k => k)) {
                    Colors.Add(new VoiceColor(key, colors[key]));
                }
                if (Colors.Count == 0) {
                    Colors.Add(new VoiceColor(string.Empty, new List<Subbank>()));
                }
                SelectedColor = Colors[0];
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to load subbanks", "<translate:errors.failed.load>: subbanks", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void AddSubbank(string name) {
            if (string.IsNullOrEmpty(name)) {
                return;
            }
            if (Colors.Any(color => color.Name == name)) {
                return;
            }
            var color = new VoiceColor(name, new List<Subbank>());
            Colors.Add(color);
            SelectedColor = color;
        }

        public void RemoveSubbank() {
            if (SelectedColor == null || string.IsNullOrEmpty(SelectedColor.Name)) {
                return;
            }
            Colors.Remove(SelectedColor);
            SelectedColor = Colors[0];
        }

        public void RenameSubbank(string name) {
            if (string.IsNullOrEmpty(name) ||
                string.IsNullOrEmpty(SelectedColor?.Name) ||
                SelectedColor?.Name == name) {
                return;
            }
            SelectedColor!.Name = name;
        }

        public void Set(IList items) {
            foreach (var item in items) {
                if (item is VoiceColorRow row) {
                    row.Prefix = Prefix;
                    row.Suffix = Suffix;
                }
            }
        }

        public void Clear(IList items) {
            foreach (var item in items) {
                if (item is VoiceColorRow row) {
                    row.Prefix = string.Empty;
                    row.Suffix = string.Empty;
                }
            }
        }

        public void SaveSubbanks() {
            if (Singer == null) {
                return;
            }
            var yamlFile = Path.Combine(Singer.Location, "character.yaml");
            VoicebankConfig? bankConfig = null;
            try {
                // Load from character.yaml
                if (File.Exists(yamlFile)) {
                    using (var stream = File.OpenRead(yamlFile)) {
                        bankConfig = VoicebankConfig.Load(stream);
                    }
                }
            } catch {
            }
            if (bankConfig == null) {
                bankConfig = new VoicebankConfig();
            }
            bankConfig.Subbanks = ColorsToSubbanks();
            try {
                using (var stream = File.Open(yamlFile, FileMode.Create)) {
                    bankConfig.Save(stream);
                }
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to save subbanks", "<translate:errors.failed.save>: subbanks", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
            LoadSubbanks();
        }

        private Subbank[] ColorsToSubbanks() {
            var result = new List<Subbank>();
            foreach (var color in Colors) {
                var subbanks = new Dictionary<Tuple<string, string>, Subbank>();
                var toneSets = new Dictionary<Tuple<string, string>, SortedSet<int>>();
                foreach (var row in color.Rows) {
                    var key = Tuple.Create(row.Prefix, row.Suffix);
                    if (!subbanks.TryGetValue(key, out var subbank)) {
                        subbank = new Subbank() {
                            Color = color.Name,
                            Prefix = row.Prefix,
                            Suffix = row.Suffix,
                        };
                        subbanks[key] = subbank;
                    }
                    if (!toneSets.TryGetValue(key, out var toneSet)) {
                        toneSet = new SortedSet<int>();
                        toneSets[key] = toneSet;
                    }
                    toneSet.Add(row.index);
                }
                foreach (var pair in subbanks) {
                    pair.Value.ToneRanges = ToneSetToRanges(toneSets[pair.Key]);
                }
                result.AddRange(subbanks.Values);
            }
            return result.ToArray();
        }

        private string[] ToneSetToRanges(SortedSet<int> toneSet) {
            var ranges = new List<string>();
            int start = -1;
            for (int i = 24; i <= 107; ++i) {
                if (start < 0) {
                    if (toneSet.Contains(i)) {
                        start = i;
                    }
                } else {
                    if (!toneSet.Contains(i)) {
                        if (i - 1 == start) {
                            ranges.Add($"{MusicMath.GetToneName(start)}");
                        } else {
                            ranges.Add($"{MusicMath.GetToneName(start)}-{MusicMath.GetToneName(i - 1)}");
                        }
                        start = -1;
                    }
                }
            }
            if (start > 0) {
                ranges.Add($"{MusicMath.GetToneName(start)}-{MusicMath.GetToneName(107)}");
            }
            return ranges.ToArray();
        }
    }
}
