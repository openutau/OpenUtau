using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.Colors;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class ThemeEditorStateChangedEvent { }

    public partial class ThemeEditorViewModel : ViewModelBase {

        private readonly string customThemePath;

        private readonly string Name;
        [Reactive] public partial bool IsDarkMode { get; set; }
        [Reactive] public partial Color BackgroundColor { get; set; }
        [Reactive] public partial Color BackgroundColorPointerOver { get; set; }
        [Reactive] public partial Color BackgroundColorPressed { get; set; }
        [Reactive] public partial Color BackgroundColorDisabled { get; set; }

        [Reactive] public partial Color ForegroundColor { get; set; }
        [Reactive] public partial Color ForegroundColorPointerOver { get; set; }
        [Reactive] public partial Color ForegroundColorPressed { get; set; }
        [Reactive] public partial Color ForegroundColorDisabled { get; set; }

        [Reactive] public partial Color BorderColor { get; set; }
        [Reactive] public partial Color BorderColorPointerOver { get; set; }

        [Reactive] public partial Color SystemAccentColor { get; set; }
        [Reactive] public partial Color SystemAccentColorLight1 { get; set; }
        [Reactive] public partial Color SystemAccentColorDark1 { get; set; }

        [Reactive] public partial Color NeutralAccentColor { get; set; }
        [Reactive] public partial Color NeutralAccentColorPointerOver { get; set; }
        [Reactive] public partial Color AccentColor1 { get; set; }
        [Reactive] public partial Color AccentColor2 { get; set; }
        [Reactive] public partial Color AccentColor3 { get; set; }

        [Reactive] public partial Color TickLineColor { get; set; }
        [Reactive] public partial Color BarNumberColor { get; set; }
        [Reactive] public partial Color FinalPitchColor { get; set; }
        [Reactive] public partial Color TrackBackgroundAltColor { get; set; }

        [Reactive] public partial Color WhiteKeyColorLeft { get; set; }
        [Reactive] public partial Color WhiteKeyColorRight { get; set; }
        [Reactive] public partial Color WhiteKeyNameColor { get; set; }

        [Reactive] public partial Color CenterKeyColorLeft { get; set; }
        [Reactive] public partial Color CenterKeyColorRight { get; set; }
        [Reactive] public partial Color CenterKeyNameColor { get; set; }

        [Reactive] public partial Color BlackKeyColorLeft { get; set; }
        [Reactive] public partial Color BlackKeyColorRight { get; set; }
        [Reactive] public partial Color BlackKeyNameColor { get; set; }

        [Reactive] public Color MainColorA { get; set; }
        [Reactive] public Color MainColorB { get; set; }
        [Reactive] public bool IsDarkModeEasy { get; set; }

        [Reactive] public bool IsEasyMode { get; set; } = true;

        public ThemeEditorViewModel(string customThemePath) {
            this.customThemePath = customThemePath;

            var themeYaml = new CustomTheme.ThemeYaml();
            try {
                themeYaml = Yaml.DefaultDeserializer.Deserialize<CustomTheme.ThemeYaml>(File.ReadAllText(customThemePath, Encoding.UTF8));
            } catch (Exception e) {
                Log.Error(e, $"Failed to parse yaml in {customThemePath}");
            }

            Name = themeYaml.Name;
            IsDarkMode = themeYaml.IsDarkMode;
            BackgroundColor = Color.Parse(themeYaml.BackgroundColor);
            BackgroundColorPointerOver = Color.Parse(themeYaml.BackgroundColorPointerOver);
            BackgroundColorPressed = Color.Parse(themeYaml.BackgroundColorPressed);
            BackgroundColorDisabled = Color.Parse(themeYaml.BackgroundColorDisabled);

            ForegroundColor = Color.Parse(themeYaml.ForegroundColor);
            ForegroundColorPointerOver = Color.Parse(themeYaml.ForegroundColorPointerOver);
            ForegroundColorPressed = Color.Parse(themeYaml.ForegroundColorPressed);
            ForegroundColorDisabled = Color.Parse(themeYaml.ForegroundColorDisabled);

            BorderColor = Color.Parse(themeYaml.BorderColor);
            BorderColorPointerOver = Color.Parse(themeYaml.BorderColorPointerOver);
            SystemAccentColor = Color.Parse(themeYaml.SystemAccentColor);
            SystemAccentColorLight1 = Color.Parse(themeYaml.SystemAccentColorLight1);
            SystemAccentColorDark1 = Color.Parse(themeYaml.SystemAccentColorDark1);

            NeutralAccentColor = Color.Parse(themeYaml.NeutralAccentColor);
            NeutralAccentColorPointerOver = Color.Parse(themeYaml.NeutralAccentColorPointerOver);
            AccentColor1 = Color.Parse(themeYaml.AccentColor1);
            AccentColor2 = Color.Parse(themeYaml.AccentColor2);
            AccentColor3 = Color.Parse(themeYaml.AccentColor3);

            TickLineColor = Color.Parse(themeYaml.TickLineColor);
            BarNumberColor = Color.Parse(themeYaml.BarNumberColor);
            FinalPitchColor = Color.Parse(themeYaml.FinalPitchColor);
            TrackBackgroundAltColor = Color.Parse(themeYaml.TrackBackgroundAltColor);

            WhiteKeyColorLeft = Color.Parse(themeYaml.WhiteKeyColorLeft);
            WhiteKeyColorRight = Color.Parse(themeYaml.WhiteKeyColorRight);
            WhiteKeyNameColor = Color.Parse(themeYaml.WhiteKeyNameColor);

            CenterKeyColorLeft = Color.Parse(themeYaml.CenterKeyColorLeft);
            CenterKeyColorRight = Color.Parse(themeYaml.CenterKeyColorRight);
            CenterKeyNameColor = Color.Parse(themeYaml.CenterKeyNameColor);
            
            BlackKeyColorLeft = Color.Parse(themeYaml.BlackKeyColorLeft);
            BlackKeyColorRight = Color.Parse(themeYaml.BlackKeyColorRight);
            BlackKeyNameColor = Color.Parse(themeYaml.BlackKeyNameColor);

            MainColorA = Color.Parse(themeYaml.MainColorA);
            MainColorB = Color.Parse(themeYaml.MainColorB);
            IsDarkModeEasy = themeYaml.IsDarkModeEasy;

            this.WhenAnyValue(vm => vm.IsDarkMode)
                .Subscribe(v => {
                    Application.Current!.Resources["IsDarkMode"] = v; 
                    if (v) {
                        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                    } else {
                        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                    }
                    ThemeManager.LoadTheme();
                });


            this.WhenAnyValue(vm => vm.IsDarkModeEasy)
                .Subscribe(v => {
                    Application.Current!.Resources["IsDarkModeEasy"] = v;
                    UpdateColorsByMain();
                });

            this.WhenAnyValue(vm => vm.BackgroundColor)
                .Subscribe(v => Application.Current!.Resources["BackgroundColor"] = v);

            this.WhenAnyValue(vm => vm.BackgroundColorPointerOver)
                .Subscribe(v => Application.Current!.Resources["BackgroundColorPointerOver"] = v);

            this.WhenAnyValue(vm => vm.BackgroundColorPressed)
                .Subscribe(v => Application.Current!.Resources["BackgroundColorPressed"] = v);

            this.WhenAnyValue(vm => vm.BackgroundColorDisabled)
                .Subscribe(v => Application.Current!.Resources["BackgroundColorDisabled"] = v);


            this.WhenAnyValue(vm => vm.ForegroundColor)
                .Subscribe(v => Application.Current!.Resources["ForegroundColor"] = v);

            this.WhenAnyValue(vm => vm.ForegroundColorPointerOver)
                .Subscribe(v => Application.Current!.Resources["ForegroundColorPointerOver"] = v);

            this.WhenAnyValue(vm => vm.ForegroundColorPressed)
                .Subscribe(v => Application.Current!.Resources["ForegroundColorPressed"] = v);

            this.WhenAnyValue(vm => vm.ForegroundColorDisabled)
                .Subscribe(v => Application.Current!.Resources["ForegroundColorDisabled"] = v);


            this.WhenAnyValue(vm => vm.BorderColor)
                .Subscribe(v => Application.Current!.Resources["BorderColor"] = v);

            this.WhenAnyValue(vm => vm.BorderColorPointerOver)
                .Subscribe(v => Application.Current!.Resources["BorderColorPointerOver"] = v);


            this.WhenAnyValue(vm => vm.SystemAccentColor)
                .Subscribe(v => Application.Current!.Resources["SystemAccentColor"] = v);

            this.WhenAnyValue(vm => vm.SystemAccentColorLight1)
                .Subscribe(v => Application.Current!.Resources["SystemAccentColorLight1"] = v);

            this.WhenAnyValue(vm => vm.SystemAccentColorDark1)
                .Subscribe(v => Application.Current!.Resources["SystemAccentColorDark1"] = v);


            this.WhenAnyValue(vm => vm.NeutralAccentColor)
                .Subscribe(v => Application.Current!.Resources["NeutralAccentColor"] = v);

            this.WhenAnyValue(vm => vm.NeutralAccentColorPointerOver)
                .Subscribe(v => Application.Current!.Resources["NeutralAccentColorPointerOver"] = v);

            this.WhenAnyValue(vm => vm.AccentColor1)
                .Subscribe(v => Application.Current!.Resources["AccentColor1"] = v);

            this.WhenAnyValue(vm => vm.AccentColor2)
                .Subscribe(v => Application.Current!.Resources["AccentColor2"] = v);

            this.WhenAnyValue(vm => vm.AccentColor3)
                .Subscribe(v => Application.Current!.Resources["AccentColor3"] = v);


            this.WhenAnyValue(vm => vm.TickLineColor)
                .Subscribe(v => Application.Current!.Resources["TickLineColor"] = v);

            this.WhenAnyValue(vm => vm.BarNumberColor)
                .Subscribe(v => Application.Current!.Resources["BarNumberColor"] = v);

            this.WhenAnyValue(vm => vm.FinalPitchColor)
                .Subscribe(v => Application.Current!.Resources["FinalPitchColor"] = v);

            this.WhenAnyValue(vm => vm.TrackBackgroundAltColor)
                .Subscribe(v => Application.Current!.Resources["TrackBackgroundAltColor"] = v);


            this.WhenAnyValue(vm => vm.WhiteKeyColorLeft)
                .Subscribe(v => Application.Current!.Resources["WhiteKeyColorLeft"] = v);

            this.WhenAnyValue(vm => vm.WhiteKeyColorRight)
                .Subscribe(v => Application.Current!.Resources["WhiteKeyColorRight"] = v);

            this.WhenAnyValue(vm => vm.WhiteKeyNameColor)
                .Subscribe(v => Application.Current!.Resources["WhiteKeyNameColor"] = v);


            this.WhenAnyValue(vm => vm.CenterKeyColorLeft)
                .Subscribe(v => Application.Current!.Resources["CenterKeyColorLeft"] = v);

            this.WhenAnyValue(vm => vm.CenterKeyColorRight)
                .Subscribe(v => Application.Current!.Resources["CenterKeyColorRight"] = v);

            this.WhenAnyValue(vm => vm.CenterKeyNameColor)
                .Subscribe(v => Application.Current!.Resources["CenterKeyNameColor"] = v);


            this.WhenAnyValue(vm => vm.BlackKeyColorLeft)
                .Subscribe(v => Application.Current!.Resources["BlackKeyColorLeft"] = v);

            this.WhenAnyValue(vm => vm.BlackKeyColorRight)
                .Subscribe(v => Application.Current!.Resources["BlackKeyColorRight"] = v);

            this.WhenAnyValue(vm => vm.BlackKeyNameColor)
                .Subscribe(v => Application.Current!.Resources["BlackKeyNameColor"] = v);
                
            this.WhenAnyValue(vm => vm.MainColorA)
                .Subscribe(v => {
                    Application.Current!.Resources["MainColorA"] = v;
                    UpdateColorsByMain();
                });

            this.WhenAnyValue(vm => vm.MainColorB)
                .Subscribe(v => {
                    Application.Current!.Resources["MainColorB"] = v;
                    UpdateColorsByMain();
                });
        }

        public void Save() {
            var themeYaml = new CustomTheme.ThemeYaml {
                Name = Name,
                IsDarkMode = IsDarkMode,
                BackgroundColor = BackgroundColor.ToString(),
                BackgroundColorPointerOver = BackgroundColorPointerOver.ToString(),
                BackgroundColorPressed = BackgroundColorPressed.ToString(),
                BackgroundColorDisabled = BackgroundColorDisabled.ToString(),

                ForegroundColor = ForegroundColor.ToString(),
                ForegroundColorPointerOver = ForegroundColorPointerOver.ToString(),
                ForegroundColorPressed = ForegroundColorPressed.ToString(),
                ForegroundColorDisabled = ForegroundColorDisabled.ToString(),

                BorderColor = BorderColor.ToString(),
                BorderColorPointerOver = BorderColorPointerOver.ToString(),

                SystemAccentColor = SystemAccentColor.ToString(),
                SystemAccentColorLight1 = SystemAccentColorLight1.ToString(),
                SystemAccentColorDark1 = SystemAccentColorDark1.ToString(),

                NeutralAccentColor = NeutralAccentColor.ToString(),
                NeutralAccentColorPointerOver = NeutralAccentColorPointerOver.ToString(),
                AccentColor1 = AccentColor1.ToString(),
                AccentColor2 = AccentColor2.ToString(),
                AccentColor3 = AccentColor3.ToString(),

                TickLineColor = TickLineColor.ToString(),
                BarNumberColor = BarNumberColor.ToString(),
                FinalPitchColor = FinalPitchColor.ToString(),
                TrackBackgroundAltColor = TrackBackgroundAltColor.ToString(),

                WhiteKeyColorLeft = WhiteKeyColorLeft.ToString(),
                WhiteKeyColorRight = WhiteKeyColorRight.ToString(),
                WhiteKeyNameColor = WhiteKeyNameColor.ToString(),

                CenterKeyColorLeft = CenterKeyColorLeft.ToString(),
                CenterKeyColorRight = CenterKeyColorRight.ToString(),
                CenterKeyNameColor = CenterKeyNameColor.ToString(),

                BlackKeyColorLeft = BlackKeyColorLeft.ToString(),
                BlackKeyColorRight = BlackKeyColorRight.ToString(),
                BlackKeyNameColor = BlackKeyNameColor.ToString(),

                MainColorA = MainColorA.ToString(),
                MainColorB = MainColorB.ToString(),
                IsDarkModeEasy = IsDarkModeEasy
            };

            File.WriteAllText(customThemePath, Yaml.DefaultSerializer.Serialize(themeYaml), Encoding.UTF8);
        }

        private void UpdateColorsByMain() {
            if (IsDarkModeEasy) {
                BackgroundColor = Color.FromUInt32(0xFF303030);
                BackgroundColorPointerOver = Color.FromUInt32(0xFF505050);
                BackgroundColorPressed = Color.FromUInt32(0xFF707070);
                BackgroundColorDisabled = Color.FromUInt32(0xFF404040);

                ForegroundColor = Color.FromUInt32(0xFFE0E0E0);
                ForegroundColorPointerOver = Color.FromUInt32(0xFFFCFCFC);
                ForegroundColorPressed = Color.FromUInt32(0xFFFFFFFF);
                ForegroundColorDisabled = Color.FromUInt32(0xFFA0A0A0);

                BorderColor = Color.FromUInt32(0xFF707070);
                BorderColorPointerOver = Color.FromUInt32(0xFFB0B0B0);

                SystemAccentColor = MainColorA;
                SystemAccentColorLight1 = ColorShift(MainColorA, 32);
                SystemAccentColorDark1 = ColorMultiply(MainColorA, 0.8f);

                NeutralAccentColor = Color.FromUInt32(0xFF808080);
                NeutralAccentColorPointerOver = Color.FromUInt32(0xFFA0A0A0);
                AccentColor1 = MainColorA;
                AccentColor2 = ColorShift(MainColorB, 32);
                AccentColor3 = ColorMultiply(MainColorB, 0.8f);

                TickLineColor = Color.FromUInt32(0xFF707070);
                BarNumberColor = Color.FromUInt32(0xFFD0D0D0);
                FinalPitchColor = Color.FromUInt32(0xFFD0D0D0);
                TrackBackgroundAltColor = Color.FromUInt32(0xFF404040);

                WhiteKeyColorLeft = ColorMultiply(MainColorB, 0.9f);
                WhiteKeyColorRight = MainColorB; 
                WhiteKeyNameColor = Color.FromUInt32(0xFFFFFFFF);

                CenterKeyColorLeft = ColorMultiply(ColorShift(MainColorB, 152), 0.9f);
                CenterKeyColorRight = ColorShift(MainColorB, 152);
                CenterKeyNameColor = MainColorB;

                BlackKeyColorLeft = Color.FromUInt32(0x00000000);
                BlackKeyColorRight = Color.FromUInt32(0x00000000);
                BlackKeyNameColor = Color.FromUInt32(0xFFFFFFFF);
            } else {
                BackgroundColor = Color.FromUInt32(0xFFFFFFFF);
                BackgroundColorPointerOver = Color.FromUInt32(0xFFF0F0F0);
                BackgroundColorPressed = Color.FromUInt32(0xFFE0E0E0);
                BackgroundColorDisabled = Color.FromUInt32(0xFFD0D0D0);

                ForegroundColor = Color.FromUInt32(0xFF000000);
                ForegroundColorPointerOver = Color.FromUInt32(0xFF000000);
                ForegroundColorPressed = Color.FromUInt32(0xFF202020);
                ForegroundColorDisabled = Color.FromUInt32(0xFF808080);

                BorderColor = Color.FromUInt32(0xFF707070);
                BorderColorPointerOver = Color.FromUInt32(0xFFB0B0B0);

                SystemAccentColor = MainColorA;
                SystemAccentColorLight1 = ColorShift(MainColorA, 32);
                SystemAccentColorDark1 = ColorMultiply(MainColorA, 0.8f);

                NeutralAccentColor = Color.FromUInt32(0xFFA7A7A7);
                NeutralAccentColorPointerOver = Color.FromUInt32(0xFF909090);
                AccentColor1 = MainColorA;
                AccentColor2 = ColorShift(MainColorB, 32);
                AccentColor3 = ColorMultiply(MainColorB, 0.8f);

                TickLineColor = Color.FromUInt32(0xFFB0B0B0);
                BarNumberColor = Color.FromUInt32(0xFFB0B0B0);
                FinalPitchColor = Color.FromUInt32(0xFFC0C0C0);
                TrackBackgroundAltColor = Color.FromUInt32(0xFFF0F0F0);

                WhiteKeyColorLeft = Color.FromUInt32(0x00000000);
                WhiteKeyColorRight = Color.FromUInt32(0x00000000);
                WhiteKeyNameColor = MainColorB;

                CenterKeyColorLeft = ColorShift(MainColorB, 144);
                CenterKeyColorRight = ColorShift(MainColorB, 152);
                CenterKeyNameColor = MainColorB;

                BlackKeyColorLeft = ColorShift(MainColorB, 32);
                BlackKeyColorRight = MainColorB;
                BlackKeyNameColor = Color.FromUInt32(0xFFFFFFFF);
            }
            IsDarkMode = IsDarkModeEasy;
        }

        public static Color ColorMultiply(Color color, float ratio) {
            return Color.FromArgb(
                color.A,
                (byte)((color.R * ratio) > 255 ? 255 : color.R * ratio),
                (byte)((color.G * ratio) > 255 ? 255 : color.G * ratio),
                (byte)((color.B * ratio) > 255 ? 255 : color.B * ratio)
            );
        }
        public static Color ColorShift(Color color, int delta) {
            return Color.FromArgb(
                color.A,
                (byte)((color.R + delta) > 255 ? 255 : ((color.R + delta) < 0 ? 0 : color.R + delta)),
                (byte)((color.G + delta) > 255 ? 255 : ((color.G + delta) < 0 ? 0 : color.G + delta)),
                (byte)((color.B + delta) > 255 ? 255 : ((color.B + delta) < 0 ? 0 : color.B + delta))
            );
        }

    }
}
