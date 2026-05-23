using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using OpenUtau.Colors;

namespace OpenUtau.App.ViewModels {
    public class ThemeEditorStateChangedEvent { }

    public class ThemeEditorViewModel : ViewModelBase {

        private readonly string customThemePath;

        private readonly string Name;
        [Reactive] public bool IsDarkMode { get; set; }
        [Reactive] public Color BackgroundColor { get; set; }
        [Reactive] public Color BackgroundColorPointerOver { get; set; }
        [Reactive] public Color BackgroundColorPressed { get; set; }
        [Reactive] public Color BackgroundColorDisabled { get; set; }

        [Reactive] public Color ForegroundColor { get; set; }
        [Reactive] public Color ForegroundColorPointerOver { get; set; }
        [Reactive] public Color ForegroundColorPressed { get; set; }
        [Reactive] public Color ForegroundColorDisabled { get; set; }

        [Reactive] public Color BorderColor { get; set; }
        [Reactive] public Color BorderColorPointerOver { get; set; }

        [Reactive] public Color SystemAccentColor { get; set; }
        [Reactive] public Color SystemAccentColorLight1 { get; set; }
        [Reactive] public Color SystemAccentColorDark1 { get; set; }

        [Reactive] public Color NeutralAccentColor { get; set; }
        [Reactive] public Color NeutralAccentColorPointerOver { get; set; }
        [Reactive] public Color AccentColor1 { get; set; }
        [Reactive] public Color AccentColor2 { get; set; }
        [Reactive] public Color AccentColor3 { get; set; }

        [Reactive] public Color TickLineColor { get; set; }
        [Reactive] public Color BarNumberColor { get; set; }
        [Reactive] public Color FinalPitchColor { get; set; }
        [Reactive] public Color TrackBackgroundAltColor { get; set; }

        [Reactive] public Color WhiteKeyColorLeft { get; set; }
        [Reactive] public Color WhiteKeyColorRight { get; set; }
        [Reactive] public Color WhiteKeyNameColor { get; set; }

        [Reactive] public Color CenterKeyColorLeft { get; set; }
        [Reactive] public Color CenterKeyColorRight { get; set; }
        [Reactive] public Color CenterKeyNameColor { get; set; }

        [Reactive] public Color BlackKeyColorLeft { get; set; }
        [Reactive] public Color BlackKeyColorRight { get; set; }
        [Reactive] public Color BlackKeyNameColor { get; set; }

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
                BlackKeyNameColor = BlackKeyNameColor.ToString()
            };

            File.WriteAllText(customThemePath, Yaml.DefaultSerializer.Serialize(themeYaml), Encoding.UTF8);
        }

    }
}
