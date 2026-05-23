using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Colors;
public class CustomTheme {
    public static Dictionary<string, string> Themes = [];
    public static ThemeYaml Default;

    static CustomTheme() {
        Default = new ThemeYaml();
        ListThemes();
    }

    public static void Load(string themeName) {
        if (!string.IsNullOrEmpty(themeName) && Themes.TryGetValue(themeName, out var themePath) && File.Exists(themePath)) {
            try {
                Default = Yaml.DefaultDeserializer.Deserialize<ThemeYaml>(File.ReadAllText(themePath, Encoding.UTF8));
                return;
            } catch (Exception e) {
                Log.Error(e, $"Failed to parse yaml in {themePath}");
            }
        }

        Preferences.Default.ThemeName = "Light";
        Default = new ThemeYaml();
    }

    public static void ListThemes() {
        Themes.Clear();
        Directory.CreateDirectory(PathManager.Inst.ThemesPath);
        foreach (var item in Directory.EnumerateFiles(PathManager.Inst.ThemesPath, "*.yaml")) {
            try {
                string baseName = Yaml.DefaultDeserializer.Deserialize<ThemeYaml>(File.ReadAllText(item, Encoding.UTF8)).Name;
                string themeName = baseName;
                int dupIter = 1;
                while (Themes.ContainsKey(themeName)) {
                    themeName = $"{baseName} ({dupIter})";
                    dupIter++;
                }
                Themes.Add(themeName, item);
            } catch (Exception e) {
                Log.Error(e, $"Failed to parse yaml in {item}");
            }
        }
    }

    public static void ApplyTheme(string themeName) {
        Load(themeName);

        if (Application.Current != null) {
            Application.Current.Resources["IsDarkMode"] = Default.IsDarkMode; 
            SetResourceColor("BackgroundColor", Default.BackgroundColor);
            SetResourceColor("BackgroundColorPointerOver", Default.BackgroundColorPointerOver);
            SetResourceColor("BackgroundColorPressed", Default.BackgroundColorPressed);
            SetResourceColor("BackgroundColorDisabled", Default.BackgroundColorDisabled);

            SetResourceColor("ForegroundColor", Default.ForegroundColor);
            SetResourceColor("ForegroundColorPointerOver", Default.ForegroundColorPointerOver);
            SetResourceColor("ForegroundColorPressed", Default.ForegroundColorPressed);
            SetResourceColor("ForegroundColorDisabled", Default.ForegroundColorDisabled);

            SetResourceColor("BorderColor", Default.BorderColor);
            SetResourceColor("BorderColorPointerOver", Default.BorderColorPointerOver);

            SetResourceColor("SystemAccentColor", Default.SystemAccentColor);
            SetResourceColor("SystemAccentColorLight1", Default.SystemAccentColorLight1);
            SetResourceColor("SystemAccentColorDark1", Default.SystemAccentColorDark1);

            SetResourceColor("NeutralAccentColor", Default.NeutralAccentColor);
            SetResourceColor("NeutralAccentColorPointerOver", Default.NeutralAccentColorPointerOver);
            SetResourceColor("AccentColor1", Default.AccentColor1);
            SetResourceColor("AccentColor2", Default.AccentColor2);
            SetResourceColor("AccentColor3", Default.AccentColor3);

            SetResourceColor("TickLineColor", Default.TickLineColor);
            SetResourceColor("BarNumberColor", Default.BarNumberColor);
            SetResourceColor("FinalPitchColor", Default.FinalPitchColor);
            SetResourceColor("TrackBackgroundAltColor", Default.TrackBackgroundAltColor);

            SetResourceColor("WhiteKeyColorLeft", Default.WhiteKeyColorLeft);
            SetResourceColor("WhiteKeyColorRight", Default.WhiteKeyColorRight);
            SetResourceColor("WhiteKeyNameColor", Default.WhiteKeyNameColor);

            SetResourceColor("CenterKeyColorLeft", Default.CenterKeyColorLeft);
            SetResourceColor("CenterKeyColorRight", Default.CenterKeyColorRight);
            SetResourceColor("CenterKeyNameColor", Default.CenterKeyNameColor);

            SetResourceColor("BlackKeyColorLeft", Default.BlackKeyColorLeft);
            SetResourceColor("BlackKeyColorRight", Default.BlackKeyColorRight);
            SetResourceColor("BlackKeyNameColor", Default.BlackKeyNameColor);
        }
    }

    private static void SetResourceColor(string res, string colorStr) {
        if (Color.TryParse(colorStr, out var color)) {
            Application.Current!.Resources[res] = color;
        } else {
            Log.Error($"Failed to parse color \"{colorStr}\" in {Default.Name} custom theme");
        }
    }
    
    [Serializable]
    public class ThemeYaml {
        public string Name = "Custom YAML";
            
        public bool IsDarkMode = false;
        public string BackgroundColor = "#FFFFFF";
        public string BackgroundColorPointerOver = "#F0F0F0";
        public string BackgroundColorPressed = "#E0E0E0";
        public string BackgroundColorDisabled = "#D0D0D0";

        public string ForegroundColor = "#000000";
        public string ForegroundColorPointerOver = "#000000";
        public string ForegroundColorPressed = "#202020";
        public string ForegroundColorDisabled = "#808080";
        
        public string BorderColor = "#707070";
        public string BorderColorPointerOver = "#B0B0B0";

        public string SystemAccentColor = "#4EA6EA";
        public string SystemAccentColorLight1 = "#90CAF9";
        public string SystemAccentColorDark1 = "#1E88E5";

        public string NeutralAccentColor = "#ADA1B3";
        public string NeutralAccentColorPointerOver = "#948A99";
        public string AccentColor1 = "#4EA6EA";
        public string AccentColor2 = "#FF679D";
        public string AccentColor3 = "#E62E6E";

        public string TickLineColor = "#AFA3B5";
        public string BarNumberColor = "#AFA3B5";
        public string FinalPitchColor = "#C0C0C0";
        public string TrackBackgroundAltColor = "#F0F0F0";

        public string WhiteKeyColorLeft = "Transparent";
        public string WhiteKeyColorRight = "Transparent";
        public string WhiteKeyNameColor = "#FF347c";
            
        public string CenterKeyColorLeft = "#FFDDE6";
        public string CenterKeyColorRight = "#FFCEDC";
        public string CenterKeyNameColor = "#FF347C";
            
        public string BlackKeyColorLeft = "#FF71A3";
        public string BlackKeyColorRight = "#FF347C";
        public string BlackKeyNameColor = "#FFFFFF";
    }
}

