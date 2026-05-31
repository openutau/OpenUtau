using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using ReactiveUI;

namespace OpenUtau.App.Views {
    public partial class ThemeEditorWindow : Window {

        private static ThemeEditorWindow? _instance;

        public static bool IsOpen => _instance != null;

        private ThemeEditorWindow(string customThemePath) {
            InitializeComponent();
            DataContext = new ThemeEditorViewModel(customThemePath);
        }

        void OnCancel(object? sender, RoutedEventArgs e) {
            Close();
        }

        void OnSave(object? sender, RoutedEventArgs e) {
            (DataContext as ThemeEditorViewModel)!.Save();
            Close();
        }

        void WindowClosing(object? sender, WindowClosingEventArgs e) {
            _instance = null;
            MessageBus.Current.SendMessage(new ThemeEditorStateChangedEvent());
            App.SetTheme();
        }

        public static void Show(string customThemePath) {
            if (_instance == null) {
                _instance = new ThemeEditorWindow(customThemePath);
                _instance.Show();
                MessageBus.Current.SendMessage(new ThemeEditorStateChangedEvent());
            } else {
                _instance.Activate();
            }
        }

    }
}
