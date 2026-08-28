using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DynamicData;

namespace OpenUtau.App.ViewModels {
    public class VoiceColorMappingViewModel : ViewModelBase {

        public string TrackName { get; set; }
        public ObservableCollection<ColorMapping> ColorMappings { get; set; } = new ObservableCollection<ColorMapping>();
        public ObservableCollection<EngineMapping> EngineMappings { get; set; } = new ObservableCollection<EngineMapping>();
        public ObservableCollection<VoiceColorMappingRow> MappingRows { get; set; } = new ObservableCollection<VoiceColorMappingRow>();

        public VoiceColorMappingRow? DefaultMappingRow => MappingRows.FirstOrDefault();
        public IEnumerable<VoiceColorMappingRow> NonDefaultMappingRows => MappingRows.Skip(1);

        public VoiceColorMappingViewModel(string[] oldColors, string[] newColors, string trackName, string[] resamplers) {
            var NewColors = new ObservableCollection<string>(newColors);
            NewColors[0] = "(Default)";
            TrackName = trackName;

            var engines = new ObservableCollection<string> { "(Default)" };
            foreach (var r in resamplers) {
                engines.Add(r);
            }

            for (int i = 0; i < oldColors.Length; i++) {
                int clrSelectedIndex;
                if (i == 0) {
                    clrSelectedIndex = 0;
                } else if (newColors.Contains(oldColors[i])) {
                    clrSelectedIndex = newColors.IndexOf(oldColors[i]);
                } else if (i < newColors.Length) {
                    clrSelectedIndex = i;
                } else {
                    clrSelectedIndex = 0;
                }

                var colorMapping = new ColorMapping(
                    i == 0 ? "(Default)" : oldColors[i],
                    i,
                    clrSelectedIndex,
                    NewColors);
                ColorMappings.Add(colorMapping);

                var engineMapping = new EngineMapping(
                    i == 0 ? "(Default)" : oldColors[i],
                    i,
                    0,
                    engines);
                EngineMappings.Add(engineMapping);

                MappingRows.Add(new VoiceColorMappingRow {
                    Name = i == 0 ? "(Default)" : oldColors[i],
                    OldIndex = i,
                    ColorSelectedIndex = clrSelectedIndex,
                    NewColors = NewColors,
                    EngineSelectedIndex = 0,
                    Engines = engines,
                });
            }
        }
    }

    public class VoiceColorMappingRow {
        public string Name { get; set; } = string.Empty;
        public int OldIndex { get; set; }
        public int ColorSelectedIndex { get; set; }
        public ObservableCollection<string> NewColors { get; set; } = new ObservableCollection<string>();
        public int EngineSelectedIndex { get; set; }
        public ObservableCollection<string> Engines { get; set; } = new ObservableCollection<string>();
    }

    public class ColorMapping {
        public string Name { get; set; }
        public int OldIndex { get; set; }
        public int SelectedIndex { get; set; }
        public ObservableCollection<string> NewColors { get; set; }

        public ColorMapping(string name,int oldIndex, int selectedIndex, ObservableCollection<string> newColors) {
            Name = name;
            OldIndex = oldIndex;
            SelectedIndex = selectedIndex;
            NewColors = newColors;
        }
    }

    public class EngineMapping {
        public string Name { get; set; }
        public int OldColorIndex { get; set; }
        public int SelectedIndex { get; set; }
        public ObservableCollection<string> Engines { get; set; }

        public EngineMapping(string name, int oldColorIndex, int selectedIndex, ObservableCollection<string> engines) {
            Name = name;
            OldColorIndex = oldColorIndex;
            SelectedIndex = selectedIndex;
            Engines = engines;
        }
    }
}
