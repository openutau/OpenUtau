using System;
using System.Collections.ObjectModel;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    public class ParseErrorLineContext : ReactiveObject {
        public int LineNumber { get; set; }
        public int ActualLineIndex { get; set; }
        private string text = string.Empty;
        public string Text {
            get => text;
            set => this.RaiseAndSetIfChanged(ref text, value);
        }
        public bool IsErrorLine { get; set; }
    }

    public class DictionaryErrorWindowViewModel : ReactiveObject {
        private string errorTitle = ThemeManager.GetString("dict.error.syntax");
        public string ErrorTitle {
            get => errorTitle;
            set => this.RaiseAndSetIfChanged(ref errorTitle, value);
        }

        private string errorMessage = string.Empty;
        public string ErrorMessage {
            get => errorMessage;
            set => this.RaiseAndSetIfChanged(ref errorMessage, value);
        }
        
        public string FilePath { get; set; } = string.Empty;
        public string[] FullFileLines { get; set; } = Array.Empty<string>();
        public System.Text.Encoding FileEncoding { get; set; } = System.Text.Encoding.UTF8;

        public ObservableCollection<ParseErrorLineContext> ErrorContextLines { get; } = new();

        public void SaveCorrections() {
            foreach (var lineCtx in ErrorContextLines) {
                if (lineCtx.ActualLineIndex >= 0 && lineCtx.ActualLineIndex < FullFileLines.Length) {
                    FullFileLines[lineCtx.ActualLineIndex] = lineCtx.Text;
                }
            }
            System.IO.File.WriteAllLines(FilePath, FullFileLines, FileEncoding);
        }
    }
}