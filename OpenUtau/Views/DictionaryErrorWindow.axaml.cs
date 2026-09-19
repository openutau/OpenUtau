using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using Avalonia.Threading;

namespace OpenUtau.App.Views {
    public partial class DictionaryErrorWindow : Window {
        public DictionaryErrorWindow() {
            InitializeComponent();
        }

        private async void Window_Loaded(object? sender, RoutedEventArgs e) {
            if (DataContext is DictionaryErrorWindowViewModel vm) {
                var errorLine = vm.ErrorContextLines.FirstOrDefault(l => l.IsErrorLine);
                
                if (errorLine != null) {
                    int index = vm.ErrorContextLines.IndexOf(errorLine);
                    await System.Threading.Tasks.Task.Delay(50);
                    var scrollViewer = this.FindControl<ScrollViewer>("CodeScrollViewer");
                    if (scrollViewer != null) {
                        double verticalOffset = System.Math.Max(0, (index * 24) - 150);
                        scrollViewer.Offset = new Vector(0, verticalOffset);
                    }
                }
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) {
            Close(false);
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e) {
            if (DataContext is DictionaryErrorWindowViewModel vm) {
                vm.SaveCorrections();
                Close(true);
            }
        }
    }
}