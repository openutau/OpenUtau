using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;

namespace OpenUtau.App.Controls {
    public partial class LyricsPanel : UserControl {
        private LyricsViewModel? viewModel;

        public LyricsPanel() {
            InitializeComponent();
            IsVisible = false;

            LyricsBox.AddHandler(GotFocusEvent, TextBoxGotFocus, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            LyricsBox.AddHandler(KeyDownEvent, TextBoxKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            LyricsBox.AddHandler(LostFocusEvent, TextBoxLostFocus, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }

        public void Show(LyricsViewModel viewModel) {
            DataContext = this.viewModel = viewModel;
            IsVisible = true;
        }

        private void TextBoxGotFocus(object? sender, RoutedEventArgs e) {
            if (viewModel == null) return;
            DocManager.Inst.StartUndoGroup("command.note.lyric");
            viewModel.IsFocused = true;
        }

        private void TextBoxKeyDown(object? sender, KeyEventArgs e) {
            if (viewModel == null || !LyricsBox.IsFocused) return;
            switch (e.Key) {
                case Key.Enter:
                    //If Shift+Enter, insert line break (default textbox behavior).
                    if (e.KeyModifiers == KeyModifiers.Shift) {
                        return;
                    }
                    this.Focus();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    this.Focus();
                    Close();
                    e.Handled = true;
                    break;
                default:
                    if (e.Key == Key.Z && e.KeyModifiers != KeyModifiers.None || e.Key == Key.Y && e.KeyModifiers != KeyModifiers.None) { // Todo: Supports shortcut remapping
                        // Finish lyrics editing and use the original shortcut
                        this.Focus();
                    }
                    break;
            }
        }

        private void TextBoxLostFocus(object? sender, RoutedEventArgs e) {
            if (viewModel == null) return;
            viewModel.IsFocused = false;
            DocManager.Inst.EndUndoGroup();
        }

        public void OnClose(object sender, RoutedEventArgs args) {
            Close();
        }
        private void Close() {
            IsVisible = false;
            viewModel = null;
        }
    }
}
