using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Util {
    public static class NotePresets {
        public static SerializableNotePresets Default;

        static NotePresets() {
            Load();
        }

        public static void Save() {
            try {
                File.WriteAllText(PathManager.Inst.NotePresetsFilePath,
                    JsonConvert.SerializeObject(Default, Formatting.Indented),
                    Encoding.UTF8);
            } catch (Exception e) {
                Log.Error(e, "Failed to save note presets.");
            }
        }

        private static void Load() {
            try {
                if (File.Exists(PathManager.Inst.NotePresetsFilePath)) {
                    Default = JsonConvert.DeserializeObject<SerializableNotePresets>(
                        File.ReadAllText(PathManager.Inst.NotePresetsFilePath, Encoding.UTF8));
                } else {
                    Reset();
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to load prefs.");
                Default = new SerializableNotePresets();
            }
        }

        public static void Reset() {
            Default = new SerializableNotePresets();
            Default.PortamentoPresets.AddRange(new List<PortamentoPreset> {
                new PortamentoPreset("Standard", 80, -40),
                new PortamentoPreset("Fast", 50, -25),
                new PortamentoPreset("Slow", 120, -60),
                new PortamentoPreset("Snap", 2, -1),
            });
            Default.VibratoPresets.AddRange(new List<VibratoPreset> {
                new VibratoPreset("Standard", 75, 175, 25, 10, 10, 0, 0, 0),
                new VibratoPreset("UTAU Default", 65, 180, 35, 20, 20, 0, 0, 0),
                new VibratoPreset("UTAU Strong", 65, 210, 55, 25, 25, 0, 0, 0),
                new VibratoPreset("UTAU Weak", 65, 165, 20, 25, 25, 0, 0, 0)
            });

            Save();
        }

        [Serializable]
        public class SerializableNotePresets {
            public string DefaultLyric = "a";
            public string SplittedLyric = "+";
            public PortamentoPreset DefaultPortamento = new PortamentoPreset("Standard", 80, -40);
            public List<PortamentoPreset> PortamentoPresets = new List<PortamentoPreset> { };
            public PitchPointShape DefaultPitchShape = PitchPointShape.io;
            public VibratoPreset DefaultVibrato = new VibratoPreset("Standard", 75, 175, 25, 10, 10, 0, 0, 0);
            public List<VibratoPreset> VibratoPresets = new List<VibratoPreset> { };
            public bool AutoVibratoToggle = false;
            public int AutoVibratoNoteDuration = 481;
        }

        public class PortamentoPreset {
            public string Name = "Default";
            public int PortamentoLength = 80;
            public int PortamentoStart = -40;

            public PortamentoPreset (string name, int length, int start) {
                Name = name;
                PortamentoLength = length;
                PortamentoStart = start;
            }

            public override string ToString() => Name;
        }

        public class VibratoPreset {
            public string Name = "Default";
            public float VibratoLength = 75;
            public float VibratoPeriod = 175;
            public float VibratoDepth = 25;
            public float VibratoIn = 10;
            public float VibratoOut = 10;
            public float VibratoShift = 0;
            public float VibratoDrift = 0;
            public float VibratoVolLink = 0;

            public VibratoPreset(string name, float length, float period, float depth, float fadein, float fadeout, float shift, float drift, float volLink) {
                Name = name;
                VibratoLength = length;
                VibratoPeriod = period;
                VibratoDepth = depth;
                VibratoIn = fadein;
                VibratoOut = fadeout;
                VibratoShift = shift;
                VibratoDrift = drift;
                VibratoVolLink = volLink;
            }

            public override string ToString() => Name;
        }

    }
}
