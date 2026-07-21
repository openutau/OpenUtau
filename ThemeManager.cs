using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.App.Controls;
using OpenUtau.Colors;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App {
    class ThemeChangedEvent { }

    /// <summary>Raised when timeline/tick grid line style (dashed vs solid) preference changes.</summary>
    class TickGridLinesStyleChangedEvent { }

    /// <summary>Raised when expression-panel curve line style (dashed vs solid) preference changes.</summary>
    class ExpressionCurveStyleChangedEvent { }

    /// <summary>Raised when classic vs overlay scrollbar preference changes.</summary>
    class ScrollbarsStyleChangedEvent { }

    class ThemeManager {
        public static bool IsDarkMode = false;
        public static IBrush ForegroundBrush = Brushes.Black;
        public static IBrush BackgroundBrush = Brushes.White;
        public static IBrush NeutralAccentBrush = Brushes.Gray;
        public static IBrush NeutralAccentBrushSemi = Brushes.Gray;
        public static IPen NeutralAccentPen = new Pen(Brushes.Black);
        public static IPen NeutralAccentPenSemi = new Pen(Brushes.Black);
        public static IBrush AccentBrush1 = Brushes.White;
        public static IPen AccentPen1 = new Pen(Brushes.White);
        public static IPen AccentPen1Thickness2 = new Pen(Brushes.White);
        public static IPen AccentPen1Thickness3 = new Pen(Brushes.White);
        public static IBrush AccentBrush1Semi = Brushes.Gray;
        public static IBrush AccentBrush1Note = Brushes.White;
        public static IBrush AccentBrush1NoteSemi = Brushes.Gray;
        public static IBrush AccentBrush2 = Brushes.Gray;
        public static IPen AccentPen2 = new Pen(Brushes.White);
        public static IPen AccentPen2Thickness2 = new Pen(Brushes.White);
        public static IPen AccentPen2Thickness3 = new Pen(Brushes.White);
        public static IBrush AccentBrush2Semi = Brushes.Gray;
        public static IBrush AccentBrush3 = Brushes.Gray;
        public static IPen AccentPen3 = new Pen(Brushes.White);
        public static IPen AccentPen3Thick = new Pen(Brushes.White);
        public static IBrush AccentBrush3Semi = Brushes.Gray;
        public static IPen NoteBorderPen = new Pen(Brushes.White, 1);
        public static IPen NoteBorderPenThickness3 = new Pen(Brushes.White, 3);
        public static IPen NoteBorderPenPressed = new Pen(Brushes.White, 1);
        public static IBrush NoteEmptyBrush = Brushes.White;
        public static IBrush NoteBrush = Brushes.White;
        public static IBrush NoteBrushPressed = Brushes.Gray;
        public static IBrush TickLineBrushLow = Brushes.Black;
        public static IBrush BarNumberBrush = Brushes.Black;
        public static IPen BarNumberPen = new Pen(Brushes.White);
        public static IBrush FinalPitchBrush = Brushes.Gray;
        public static IPen FinalPitchPen = new Pen(Brushes.Gray);
        public static IBrush RealCurveFillBrush = Brushes.Gray;
        public static IBrush RealCurveStrokeBrush = Brushes.Gray;
        public static IPen RealCurvePen = new Pen(Brushes.Gray, 1D, DashStyle.Dash);
        public static IBrush WhiteKeyBrush = Brushes.White;
        public static IBrush WhiteKeyNameBrush = Brushes.Black;
        public static IBrush CenterKeyBrush = Brushes.White;
        public static IBrush CenterKeyNameBrush = Brushes.Black;
        public static IBrush BlackKeyBrush = Brushes.Black;
        public static IBrush BlackKeyNameBrush = Brushes.White;
        public static IBrush ExpBrush = Brushes.White;
        public static IBrush ExpNameBrush = Brushes.Black;
        public static IBrush ExpShadowBrush = Brushes.Gray;
        public static IBrush ExpShadowNameBrush = Brushes.White;
        public static IBrush ExpActiveBrush = Brushes.Black;
        public static IBrush ExpActiveNameBrush = Brushes.White;
        public static IBrush TrackBackgroundAltBrush = Brushes.Gray;
        public static IBrush WorkspaceElevatedSurfaceBrush = Brushes.Gray;
        public static IBrush WorkspaceCardBrush = Brushes.Gray;
        public static IBrush TrackHeaderBorderBrush = Brushes.Gray;
        public static IBrush MutedIconBrush = Brushes.Gray;

        /// <summary>Theme values for CenterKey/BlackKey colors, restored when UseTrackColor is off.</summary>
        private static Color? s_defaultCenterKeyColorLeft;
        private static string lastPianorollTrackColor = "Blue";
        private static Color? s_defaultCenterKeyColorRight;
        private static Color? s_defaultCenterKeyNameColor;
        private static Color? s_defaultBlackKeyColorLeft;
        private static Color? s_defaultBlackKeyColorRight;
        private static IPen? s_defaultFinalPitchPen;

        static readonly TrackColor[] BuiltInTrackColors = {
                new TrackColor("Flamingo", "#D491AA", "#E06C96", "#EBB7CC", "#F4D4E3", "#66AC7288", "#C2708E", "#D491AA", "#EBCFDC", "#1AAC7288"),
                new TrackColor("Cherry", "#D93A3F", "#C02A2F", "#DB555A", "#F8CCCE", "#669E2E32", "#AF3136", "#C02A2F", "#F5B7B9", "#1A9E2E32"),
                new TrackColor("Peach", "#FF8A65", "#FF7043", "#FFB59E", "#FFE2D9", "#70F5683D", "#E07352", "#FFAB91", "#FFD5C8", "#1AF5683D"),
                new TrackColor("Banana", "#FBC13A", "#FBAB32", "#FCD569", "#FFF7D7", "#70FAC038", "#DFAD49", "#FFD97F", "#FFF4C0", "#1AFAC038"),
                new TrackColor("Olive", "#CDDC39", "#B0B931", "#E0EA85", "#F4F9D1", "#70CDD926", "#99A12B", "#E8F764", "#F2F7CE", "#1ACDD926"),
                new TrackColor("Mint", "#66BB8A", "#43A06A", "#B2E2C7", "#D6F2E2", "#7033CC73", "#45A16B", "#4DCB82", "#D2EBDD", "#1A33CC73"),
                new TrackColor("Sky", "#80D9FF", "#3DC7F5", "#ACE5F8", "#CBF2FF", "#5980D9FF", "#4D99B3", "#9EE3FA", "#C4EFFD", "#1A80D9FF"),
                new TrackColor("Blue", "#7266EE", "#4435E6", "#B9B4F9", "#DDDBFD", "#704C4C7A", "#50509B", "#7B79D9", "#E4E2FD", "#1A4C4C7A"),
                new TrackColor("Purple", "#BA68C8", "#AB47BC", "#D49FDD", "#EBCBF0", "#70BA68C8", "#AB47BC", "#CE93D8", "#E7C9EC", "#1ABA68C8"),
                new TrackColor("Barbie", "#E91E63", "#C2185B", "#EE89AB", "#FBBED3", "#70DB5781", "#DA3E7A", "#F28CAD", "#F8B1C9", "#1ADB5781"),
                new TrackColor("Wine", "#AE3442", "#96212F", "#D26F7A", "#FDB8C0", "#666A252E", "#96212F", "#AE3442", "#FAA8B1", "#1A6A252E"),
                new TrackColor("Orange", "#EE582B", "#C33C13", "#EF9D84", "#FFCEC2", "#70E65427", "#C33C13", "#F06B42", "#FFC2B3", "#1AE65427"),
                new TrackColor("Gold", "#FF8F00", "#FF7F00", "#F7C859", "#FFE9B8", "#70EC9A2F", "#C07326", "#FFAF4D", "#FFE097", "#1AEC9A2F"),
                new TrackColor("BRAT", "#C5E233", "#BAE61A", "#E1F292", "#F6FDD1", "#70BAE61A", "#8BAA0E", "#E1FF4D", "#F3FAD1", "#1AC4E61A"),
                new TrackColor("Forest", "#2E7D32", "#1B5E20", "#63B967", "#C1F2C3", "#701B5E20", "#2E7D32", "#43A047", "#A1D0A3", "#1A1B5E20"),
                new TrackColor("Teal", "#0AC2C2", "#008080", "#65CACA", "#CAF7F3", "#70008080", "#238B8B", "#14B8B8", "#90CBF9", "#1A008080"),
                new TrackColor("Violet", "#7B1FA2", "#4A148C", "#B45FC2", "#F6D4F7", "#704A148C", "#7B1FA2", "#AB47BC", "#D5A3DE", "#1A4A148C"),
                new TrackColor("Moon", "#707070", "#4A4A4A", "#A7A7A7", "#EAEAEA", "#6B4A4A47", "#707070", "#808080", "#C9C9C9", "#45454540"),
            };

        public static List<TrackColor> TrackColors = new List<TrackColor>();

        static ThemeManager() {
            ReloadTrackColors();
        }

        public static void ReloadTrackColors() {
            TrackColors.Clear();
            TrackColors.AddRange(BuiltInTrackColors);
            TrackColors.AddRange(CustomTrackColorStore.LoadAll());
        }

        public static bool IsBuiltInTrackColorName(string name) =>
            BuiltInTrackColors.Any(color => string.Equals(color.Name, name, StringComparison.OrdinalIgnoreCase));

        public static List<string> GetAvailableThemes() {
            Colors.CustomTheme.ListThemes();
            return [
                ..BuiltInThemeLoader.BaseThemeNames,
                ..BuiltInThemeLoader.BuiltInCustomThemeNames,
                ..Colors.CustomTheme.Themes.Select(v => v.Key),
            ];
        }

        public static void LoadTheme() {
            if (Application.Current == null) {
                return;
            }
            IResourceDictionary resDict = Application.Current.Resources;
            object? outVar;
            IsDarkMode = false;
            var themeVariant = ThemeVariant.Default;
            if (resDict.TryGetResource("IsDarkMode", themeVariant, out outVar)) {
                if (outVar is bool b) {
                    IsDarkMode = b;
                }
            }
            if (resDict.TryGetResource("SystemControlForegroundBaseHighBrush", themeVariant, out outVar)) {
                ForegroundBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("SystemControlBackgroundAltHighBrush", themeVariant, out outVar)) {
                BackgroundBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("NeutralAccentBrush", themeVariant, out outVar)) {
                NeutralAccentBrush = (IBrush)outVar!;
                NeutralAccentPen = new Pen(NeutralAccentBrush, 1);
            }
            if (resDict.TryGetResource("NeutralAccentBrushSemi", themeVariant, out outVar)) {
                NeutralAccentBrushSemi = (IBrush)outVar!;
                NeutralAccentPenSemi = new Pen(NeutralAccentBrushSemi, 1);
            }
            if (resDict.TryGetResource("AccentBrush1", themeVariant, out outVar)) {
                AccentBrush1 = (IBrush)outVar!;
                AccentPen1 = new Pen(AccentBrush1);
                AccentPen1Thickness2 = new Pen(AccentBrush1, 2);
                AccentPen1Thickness3 = new Pen(AccentBrush1, 3);
            }
            if (resDict.TryGetResource("AccentBrush1Semi", themeVariant, out outVar)) {
                AccentBrush1Semi = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("AccentBrush1Note", themeVariant, out outVar)) {
                AccentBrush1Note = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("AccentBrush1NoteSemi", themeVariant, out outVar)) {
                AccentBrush1NoteSemi = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("AccentBrush2", themeVariant, out outVar)) {
                AccentBrush2 = (IBrush)outVar!;
                AccentPen2 = new Pen(AccentBrush2, 1);
                AccentPen2Thickness2 = new Pen(AccentBrush2, 2);
                AccentPen2Thickness3 = new Pen(AccentBrush2, 3);
            }
            if (resDict.TryGetResource("AccentBrush2Semi", themeVariant, out outVar)) {
                AccentBrush2Semi = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("AccentBrush3", themeVariant, out outVar)) {
                AccentBrush3 = (IBrush)outVar!;
                AccentPen3 = new Pen(AccentBrush3, 1);
                AccentPen3Thick = new Pen(AccentBrush3, 3);
            }
            if (resDict.TryGetResource("AccentBrush3Semi", themeVariant, out outVar)) {
                AccentBrush3Semi = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("NoteBorderBrush", themeVariant, out outVar)) {
                NoteBorderPen = new Pen((IBrush)outVar!, 1);
                NoteBorderPenThickness3 = new Pen(NoteBorderPen.Brush, 3);
            }
            if (resDict.TryGetResource("NoteBorderBrushPressed", themeVariant, out outVar)) {
                NoteBorderPenPressed = new Pen((IBrush)outVar!, 1);
            }
            if (resDict.TryGetResource("TickLineBrushLow", themeVariant, out outVar)) {
                TickLineBrushLow = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("BarNumberBrush", themeVariant, out outVar)) {
                BarNumberBrush = (IBrush)outVar!;
                BarNumberPen = new Pen(BarNumberBrush, 1);
            }
            if (resDict.TryGetResource("FinalPitchBrush", themeVariant, out outVar)) {
                FinalPitchBrush = (IBrush)outVar!;
                FinalPitchPen = new Pen(FinalPitchBrush, 1);
                s_defaultFinalPitchPen = FinalPitchPen;
            }
            if (resDict.TryGetResource("RealCurveFillBrush", themeVariant, out outVar)) {
                RealCurveFillBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("RealCurveStrokeBrush", themeVariant, out outVar)) {
                RealCurveStrokeBrush = (IBrush)outVar!;
                RealCurvePen = new Pen(RealCurveStrokeBrush, 2, DashStyle.Dash);
            }
            if (resDict.TryGetResource("TrackBackgroundAltBrush", themeVariant, out outVar)) {
                TrackBackgroundAltBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("WorkspaceElevatedSurfaceBrush", themeVariant, out outVar)) {
                WorkspaceElevatedSurfaceBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("WorkspaceCardBrush", themeVariant, out outVar)) {
                WorkspaceCardBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("TrackHeaderBorderBrush", themeVariant, out outVar)) {
                TrackHeaderBorderBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("MutedIconBrush", themeVariant, out outVar)) {
                MutedIconBrush = (IBrush)outVar!;
            }
            if (resDict.TryGetResource("CenterKeyColorLeft", themeVariant, out outVar) && outVar is Color ckl) { s_defaultCenterKeyColorLeft = ckl; }
            if (resDict.TryGetResource("CenterKeyColorRight", themeVariant, out outVar) && outVar is Color ckr) { s_defaultCenterKeyColorRight = ckr; }
            if (resDict.TryGetResource("CenterKeyNameColor", themeVariant, out outVar) && outVar is Color ckn) { s_defaultCenterKeyNameColor = ckn; }
            if (resDict.TryGetResource("BlackKeyColorLeft", themeVariant, out outVar) && outVar is Color bkl) { s_defaultBlackKeyColorLeft = bkl; }
            if (resDict.TryGetResource("BlackKeyColorRight", themeVariant, out outVar) && outVar is Color bkr) { s_defaultBlackKeyColorRight = bkr; }
            SetKeyboardBrush();
            TextLayoutCache.Clear();
            ApplyPianorollColorCore(lastPianorollTrackColor);
            MessageBus.Current.SendMessage(new ThemeChangedEvent());
        }

        public static void ChangePianorollColor(string color) {
            lastPianorollTrackColor = color;
            ApplyPianorollColorCore(color);
            MessageBus.Current.SendMessage(new ThemeChangedEvent());
        }

        static void ApplyPianorollColorCore(string color) {
            if (Application.Current == null) {
                return;
            }
            try {
                IResourceDictionary resDict = Application.Current.Resources;
                TrackColor tcolor = GetTrackColor(color);
                
                resDict["SelectedTrackAccentBrush"] = tcolor.AccentColor;
                var accentSemi = new SolidColorBrush(tcolor.AccentColor.Color);
                accentSemi.Opacity = 0.5;
                resDict["SelectedTrackAccentBrushSemi"] = accentSemi;
                resDict["SelectedTrackAccentLightBrush"] = tcolor.AccentColorLight;
                resDict["SelectedTrackAccentLightBrushSemi"] = tcolor.AccentColorLightSemi;
                resDict["SelectedTrackAccentDarkBrush"] = tcolor.AccentColorDark;
                resDict["SelectedTrackCenterKeyBrush"] = tcolor.AccentColorCenterKey;
                if (Preferences.Default.UseTrackColor) {
                    if (IsDarkMode) {
                        resDict["CenterKeyNameColor"] = tcolor.AccentColorDark.Color;       // piano2; label darkening applied in SetKeyboardBrush
                        resDict["CenterKeyColorLeft"] = BlendColors(tcolor.AccentColorLight.Color, Color.Parse("#C0C0C0"));   // piano3 + gray 50/50
                        resDict["CenterKeyColorRight"] = BlendColors(tcolor.AccentColorCenterKey.Color, Color.Parse("#F0F0F0")); // piano4 + light gray 50/50
                        if (s_defaultBlackKeyColorLeft is Color bkl) { resDict["BlackKeyColorLeft"] = bkl; }
                        if (s_defaultBlackKeyColorRight is Color bkr) { resDict["BlackKeyColorRight"] = bkr; }
                        if (s_defaultFinalPitchPen != null) { FinalPitchPen = s_defaultFinalPitchPen; }
                    } else {
                        resDict["CenterKeyNameColor"] = tcolor.AccentColorDark.Color;       // piano2 (light theme)
                        resDict["CenterKeyColorLeft"] = tcolor.AccentColorLight.Color;       // piano3 (light theme)
                        resDict["CenterKeyColorRight"] = tcolor.AccentColorCenterKey.Color;  // piano4 (light theme)
                    }
                    if (!IsDarkMode) {
                        resDict["BlackKeyColorLeft"] = tcolor.AccentColorDark.Color;    // piano2 (light theme)
                        resDict["BlackKeyColorRight"] = tcolor.AccentColorLight.Color;  // piano3 (light theme)
                        FinalPitchPen = new Pen(tcolor.AccentColorDark, 1);             // pitch curve = piano2 (light theme)
                    }
                } else {
                    if (s_defaultCenterKeyColorLeft is Color ckl) { resDict["CenterKeyColorLeft"] = ckl; }
                    if (s_defaultCenterKeyColorRight is Color ckr) { resDict["CenterKeyColorRight"] = ckr; }
                    if (s_defaultCenterKeyNameColor is Color ckn) { resDict["CenterKeyNameColor"] = ckn; }
                    if (s_defaultBlackKeyColorLeft is Color bkl) { resDict["BlackKeyColorLeft"] = bkl; }
                    if (s_defaultBlackKeyColorRight is Color bkr) { resDict["BlackKeyColorRight"] = bkr; }
                    if (s_defaultFinalPitchPen != null) { FinalPitchPen = s_defaultFinalPitchPen; }
                }

                NoteBrush = tcolor.NoteColor;
                NoteBrushPressed = tcolor.NoteColorPressed;
                NoteBorderPen = new Pen(tcolor.NoteBorderColor);
                NoteBorderPenThickness3 = new Pen(NoteBorderPen.Brush, 3);
                NoteBorderPenPressed = new Pen(tcolor.NoteBorderColorPressed);
                NoteEmptyBrush = tcolor.NoteColorEmpty;

                SetKeyboardBrush();
            } catch { }
        }
        private static void SetKeyboardBrush() {
            if (Application.Current == null) {
                return;
            }
            IResourceDictionary resDict = Application.Current.Resources;
            object? outVar;
            var themeVariant = ThemeVariant.Default;

            if (Preferences.Default.UseTrackColor) {
                // Staff (piano roll keyboard): use theme colors for white/black keys and labels;
                // only C key rows use track color: fill = CenterKeyBrush, text = CenterKeyNameBrush (track accent).
                if (resDict.TryGetResource("WhiteKeyBrush", themeVariant, out outVar)) {
                    WhiteKeyBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("WhiteKeyNameBrush", themeVariant, out outVar)) {
                    WhiteKeyNameBrush = (IBrush)outVar!;
                }
                CenterKeyNameBrush = ResolveCenterKeyNameBrush(resDict, themeVariant);
                if (resDict.TryGetResource("BlackKeyBrush", themeVariant, out outVar)) {
                    BlackKeyBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("BlackKeyNameBrush", themeVariant, out outVar)) {
                    BlackKeyNameBrush = (IBrush)outVar!;
                }
                // Use gradient CenterKeyBrush (respects CenterKeyColorLeft/Right set in ChangePianorollColor)
                if (resDict.TryGetResource("CenterKeyBrush", themeVariant, out outVar)) {
                    CenterKeyBrush = (IBrush)outVar!;
                }
                if (!IsDarkMode) {
                    ExpBrush = WhiteKeyBrush;
                    ExpNameBrush = WhiteKeyNameBrush;
                    ExpActiveBrush = BlackKeyBrush;
                    ExpActiveNameBrush = BlackKeyNameBrush;
                } else {
                    ExpBrush = BlackKeyBrush;
                    ExpNameBrush = BlackKeyNameBrush;
                    ExpActiveBrush = WhiteKeyBrush;
                    ExpActiveNameBrush = WhiteKeyNameBrush;
                }
                ExpShadowBrush = DarkenBrush(ExpActiveBrush, 0.5);
                ExpShadowNameBrush = Brushes.White;
            } else { // DefColor
                if (resDict.TryGetResource("WhiteKeyBrush", themeVariant, out outVar)) {
                    WhiteKeyBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("WhiteKeyNameBrush", themeVariant, out outVar)) {
                    WhiteKeyNameBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("CenterKeyBrush", themeVariant, out outVar)) {
                    CenterKeyBrush = (IBrush)outVar!;
                }
                CenterKeyNameBrush = ResolveCenterKeyNameBrush(resDict, themeVariant);
                if (resDict.TryGetResource("BlackKeyBrush", themeVariant, out outVar)) {
                    BlackKeyBrush = (IBrush)outVar!;
                }
                if (resDict.TryGetResource("BlackKeyNameBrush", themeVariant, out outVar)) {
                    BlackKeyNameBrush = (IBrush)outVar!;
                }
                if (!IsDarkMode) {
                    ExpBrush = WhiteKeyBrush;
                    ExpNameBrush = WhiteKeyNameBrush;
                    ExpActiveBrush = BlackKeyBrush;
                    ExpActiveNameBrush = BlackKeyNameBrush;
                } else {
                    ExpBrush = BlackKeyBrush;
                    ExpNameBrush = BlackKeyNameBrush;
                    ExpActiveBrush = WhiteKeyBrush;
                    ExpActiveNameBrush = WhiteKeyNameBrush;
                }
                ExpShadowBrush = DarkenBrush(ExpActiveBrush, 0.5);
                ExpShadowNameBrush = Brushes.White;
            }
        }

        /// <summary>Dark-theme C key label: 50% accent blended with dark gray.</summary>
        private static Color DarkThemeAccentLabelColor(Color accent) =>
            BlendColorsWeighted(accent, Color.Parse("#2F2F2F"), 0.5);

        private static IBrush ResolveCenterKeyNameBrush(IResourceDictionary resDict, ThemeVariant themeVariant) {
            if (resDict.TryGetResource("CenterKeyNameColor", themeVariant, out object? outVar) && outVar is Color c) {
                if (IsDarkMode) {
                    c = DarkThemeAccentLabelColor(c);
                }
                return new SolidColorBrush(c);
            }
            if (resDict.TryGetResource("CenterKeyNameBrush", themeVariant, out outVar)) {
                return (IBrush)outVar!;
            }
            return Brushes.Black;
        }

        /// <summary>Blends two colors; <paramref name="weightA"/> is the fraction of <paramref name="a"/> (0–1).</summary>
        private static Color BlendColorsWeighted(Color a, Color b, double weightA) {
            weightA = System.Math.Clamp(weightA, 0, 1);
            double weightB = 1 - weightA;
            return Color.FromArgb(
                (byte)(a.A * weightA + b.A * weightB),
                (byte)(a.R * weightA + b.R * weightB),
                (byte)(a.G * weightA + b.G * weightB),
                (byte)(a.B * weightA + b.B * weightB));
        }

        /// <summary>Darkens a brush by blending its colors toward black; factor is the original color weight (e.g. 0.8 = 20% darker).</summary>
        private static IBrush DarkenBrush(IBrush brush, double factor) {
            if (brush is SolidColorBrush solid) {
                return new SolidColorBrush(DarkenColor(solid.Color, factor)) { Opacity = solid.Opacity };
            }
            if (brush is LinearGradientBrush gradient) {
                var stops = new GradientStops();
                foreach (GradientStop stop in gradient.GradientStops) {
                    stops.Add(new GradientStop(DarkenColor(stop.Color, factor), stop.Offset));
                }
                return new LinearGradientBrush {
                    StartPoint = gradient.StartPoint,
                    EndPoint = gradient.EndPoint,
                    GradientStops = stops,
                    Opacity = gradient.Opacity,
                };
            }
            return brush;
        }

        private static Color DarkenColor(Color c, double factor) => BlendColorsWeighted(c, Color.Parse("#000000"), factor);

        /// <summary>Blends two colors 50/50 by component.</summary>
        private static Color BlendColors(Color a, Color b) {
            return Color.FromArgb(
                (byte)((a.A + b.A) / 2),
                (byte)((a.R + b.R) / 2),
                (byte)((a.G + b.G) / 2),
                (byte)((a.B + b.B) / 2));
        }

        public static string GetString(string key) {
            TryGetString(key, out string value);
            return value;
        }

        public static bool TryGetString(string key, out string value) {
            if (Application.Current == null) {
                value = key;
                return false;
            }
            IResourceDictionary resDict = Application.Current.Resources;
            if (resDict.TryGetResource(key, ThemeVariant.Default, out var outVar) && outVar is string s) {
                value = s;
                return true;
            }
            value = key;
            return false;
        }

        public static TrackColor GetTrackColor(string name) {
            var match = TrackColors.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            return match ?? TrackColors.First(c => c.Name == "Blue");
        }
    }

    public class TrackColor {
        public string Name { get; set; } = "";
        public bool IsCustom { get; }
        public string? StoragePath { get; }
        public SolidColorBrush AccentColor { get; set; }
        public SolidColorBrush AccentColorDark { get; set; } // Pressed
        public SolidColorBrush AccentColorLight { get; set; } // PointerOver
        public SolidColorBrush AccentColorLightSemi { get; set; } // BackGround
        public SolidColorBrush AccentColorCenterKey { get; set; } // Keyboard
        public SolidColorBrush NoteColor { get; set; }
        public SolidColorBrush NoteColorPressed { get; set; }
        public SolidColorBrush NoteBorderColor { get; set; }
        public SolidColorBrush NoteBorderColorPressed { get; set; }
        public SolidColorBrush NoteColorEmpty { get; set; }

        public TrackColor(string name, string accentColor, string darkColor, string lightColor, string centerKey, string noteColor, string noteColorPressed, string noteBorderColor, string noteBorderColorPressed, string noteColorEmpty, bool isCustom = false, string? storagePath = null) {
            Name = name;
            IsCustom = isCustom;
            StoragePath = storagePath;
            AccentColor = SolidColorBrush.Parse(accentColor);
            AccentColorDark = SolidColorBrush.Parse(darkColor);
            AccentColorLight = SolidColorBrush.Parse(lightColor);
            AccentColorLightSemi = SolidColorBrush.Parse(lightColor);
            AccentColorLightSemi.Opacity = 0.5;
            AccentColorCenterKey = SolidColorBrush.Parse(centerKey);
            NoteColor = SolidColorBrush.Parse(noteColor);
            NoteColorPressed = SolidColorBrush.Parse(noteColorPressed);
            NoteBorderColor = SolidColorBrush.Parse(noteBorderColor);
            NoteBorderColorPressed = SolidColorBrush.Parse(noteBorderColorPressed);
            NoteColorEmpty = SolidColorBrush.Parse(noteColorEmpty);
        }

        public static TrackColor FromCustomYaml(TrackColorYaml yaml, string storagePath) {
            var normal = Color.Parse(yaml.BaseColor);
            var bright = Color.Parse(yaml.BrightColor);
            var palette = TrackColorPalette.Generate(normal, bright);
            return new TrackColor(
                yaml.Name,
                palette.AccentColor,
                palette.DarkColor,
                palette.LightColor,
                palette.CenterKeyColor,
                palette.NoteColor,
                palette.NoteColorPressed,
                palette.NoteBorderColor,
                palette.NoteBorderColorPressed,
                palette.NoteColorEmpty,
                isCustom: true,
                storagePath: storagePath);
        }
    }
}
