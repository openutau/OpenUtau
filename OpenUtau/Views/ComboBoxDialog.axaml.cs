using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtau.App.Views {
    public partial class ComboBoxDialog : Window {
        public Action<int>? onFinish;

        public ComboBoxDialog() {
            InitializeComponent();
            OkButton.Click += OkButtonClick;
        }

        public void Initialize(string title, string[] items, int selectedIndex) {
            Title = title;
            ComboBox.ItemsSource = items;
            ComboBox.SelectedIndex = selectedIndex;
        }
        public void SetPrompt(string prompt) {
            Prompt.IsVisible = true;
            Prompt.Text = prompt;
        }

        private void OkButtonClick(object? sender, RoutedEventArgs e) {
            Finish();
        }

        private void Finish() {
            if (onFinish != null) {
                onFinish.Invoke(ComboBox.SelectedIndex);
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
