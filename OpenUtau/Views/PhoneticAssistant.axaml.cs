using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class PhoneticAssistant : Window {
        PhoneticAssistantViewModel viewModel;
        public PhoneticAssistant() {
            InitializeComponent();
            DataContext = viewModel = new PhoneticAssistantViewModel();
        }

        public void OnCopy(object sender, RoutedEventArgs e) {
            Clipboard?.SetTextAsync(viewModel.Phonemes);
        }
    }
}
