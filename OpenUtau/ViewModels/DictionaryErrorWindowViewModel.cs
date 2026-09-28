using System;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
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
        public System.Text.Encoding FileEncoding { get; set; } = System.Text.Encoding.UTF8;

        // NEW: Store the entire file as a single string for AvaloniaEdit
        private string rawText = string.Empty;
        public string RawText {
            get => rawText;
            set => this.RaiseAndSetIfChanged(ref rawText, value);
        }

        public void SaveCorrections() {
            System.IO.File.WriteAllText(FilePath, RawText, FileEncoding);
        }
    }
}