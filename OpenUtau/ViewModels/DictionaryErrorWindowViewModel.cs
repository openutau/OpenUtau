using System;
using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class ParseErrorLineContext : ReactiveObject {
        public int LineNumber { get; set; }
        public int ActualLineIndex { get; set; }
        [Reactive] public string Text { get; set; } = string.Empty;
        public bool IsErrorLine { get; set; }
    }

    public class DictionaryErrorWindowViewModel : ReactiveObject {
        [Reactive] public string ErrorTitle { get; set; } = ThemeManager.GetString("dict.error.syntax");
        [Reactive] public string ErrorMessage { get; set; } = string.Empty;
        
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