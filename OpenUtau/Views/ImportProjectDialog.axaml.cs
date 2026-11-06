using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtau.App.Views {
    public partial class ImportProjectDialog : Window {
        public enum Result {
            AsNewProject,
            AsTracks,
            Cancelled,
        }

        public Action<Result>? onFinish;
        private Result result = Result.Cancelled;

        public ImportProjectDialog(string[] files) {
            InitializeComponent();
            FilesList.ItemsSource = files;
        }

        private void ImportAsTracks(object? sender, RoutedEventArgs e) {
            result = Result.AsTracks;
            Close();
        }

        private void ImportAsNewProject(object? sender, RoutedEventArgs e) {
            result = Result.AsNewProject;
            Close();
        }

        public void WindowClosing(object? sender, WindowClosingEventArgs e) {
            onFinish?.Invoke(result);
        }
    }
}
