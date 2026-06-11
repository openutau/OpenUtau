using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    public partial class MixFxDialog : Window {
        readonly MixFxViewModel viewModel;

        public MixFxDialog() : this(null) { }

        public MixFxDialog(UTrack? track) {
            InitializeComponent();
            DataContext = viewModel = new MixFxViewModel(track);
            viewModel.AskForName = PromptForNameAsync;
        }

        Task<string?> PromptForNameAsync() {
            var tcs = new TaskCompletionSource<string?>();
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("mixfx.library.save");
            dialog.SetText(string.Empty);
            string? captured = null;
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name)) captured = name;
            };
            dialog.Closed += (_, __) => tcs.TrySetResult(captured);
            dialog.ShowDialog(this);
            return tcs.Task;
        }

        void OnOkClicked(object sender, RoutedEventArgs e) {
            viewModel.Apply();
            Close();
        }

        void OnCancelClicked(object sender, RoutedEventArgs e) {
            Close();
        }
    }
}
