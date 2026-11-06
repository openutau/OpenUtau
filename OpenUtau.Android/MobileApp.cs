using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace OpenUtau.Mobile;

public sealed class MobileApp : Application {
    public override void Initialize() {
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime) {
            activityLifetime.MainViewFactory = () => new MobileHomeView();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class MobileHomeView : UserControl {
    public MobileHomeView() {
        Background = Brush.Parse("#10131C");
        Content = new ScrollViewer {
            Content = new StackPanel {
                Spacing = 20,
                Margin = new Thickness(24, 32),
                Children = {
                    new TextBlock {
                        Text = "OpenUtau",
                        FontSize = 30,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brush.Parse("#F1F5F9")
                    },
                    new TextBlock {
                        Text = "Mobile workspace · Android preview",
                        FontSize = 15,
                        Foreground = Brush.Parse("#AAB4C5")
                    },
                    CreateCard(
                        "Project",
                        "Start with a clean workspace. Project loading and saving will be added in a later milestone."),
                    CreateCard(
                        "Piano roll",
                        "The mobile editor is not connected yet. This preview establishes a touch-friendly layout."),
                    CreateCard(
                        "Playback",
                        "Android audio output is not implemented in this starter."),
                    new Button {
                        Content = "New project · coming soon",
                        MinHeight = 56,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        IsEnabled = false
                    }
                }
            }
        };
    }

    private static Border CreateCard(string title, string detail) => new() {
        Background = Brush.Parse("#1B2030"),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(18),
        Child = new StackPanel {
            Spacing = 8,
            Children = {
                new TextBlock {
                    Text = title,
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush.Parse("#F1F5F9")
                },
                new TextBlock {
                    Text = detail,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush.Parse("#AAB4C5")
                }
            }
        }
    };
}
