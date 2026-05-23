using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtau.App.Views {
    public partial class TypeInDialog : Window {
        public Action<string>? onFinish;

        public TypeInDialog() {
            InitializeComponent();
            OkButton.Click += OkButtonClick;
            TextBox.AttachedToVisualTree += (s, e) => { TextBox.SelectAll(); TextBox.Focus(); };
        }

        public void SetPrompt(string prompt) {
            Prompt.IsVisible = true;
            Prompt.Text = prompt;
        }

        public void SetText(string text) {
            TextBox.Text = text;
        }

        private void OkButtonClick(object? sender, RoutedEventArgs e) {
            Finish();
        }

        private void Finish() {
            if (onFinish != null) {
                onFinish.Invoke(TextBox.Text ?? string.Empty);
            }
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Close();
            } else if (e.Key == Key.Enter) {
                e.Handled = true;
                Finish();
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}
