using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.Views {
    public partial class TimeSignatureDialog : Window, IReactiveObject {
        static readonly List<int> beatPerBars = Enumerable.Range(1, 32).ToList();
        static readonly List<int> beatUnits = new List<int> { 1, 2, 4, 8, 16, 32 };

        public List<int> BeatPerBars => beatPerBars;
        public List<int> BeatUnits => beatUnits;

        [Reactive] public partial int BeatPerBar { get; set; }
        [Reactive] public partial int BeatUnit { get; set; }

        public new event PropertyChangedEventHandler? PropertyChanged;
        public event PropertyChangingEventHandler? PropertyChanging;

        public void RaisePropertyChanging(PropertyChangingEventArgs args) => PropertyChanging?.Invoke(this, args);
        public void RaisePropertyChanged(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);

        public Action<int, int>? OnOk { get; set; }

        public TimeSignatureDialog() : this(4, 4) { }

        public TimeSignatureDialog(int beatPerBar, int beatUnit) {
            InitializeComponent();
            BeatPerBar = beatPerBar;
            BeatUnit = beatUnit;
            DataContext = this;
        }

        private void OnOkButtonClick(object sender, RoutedEventArgs args) {
            OnOk?.Invoke(BeatPerBar, BeatUnit);
            Close();
        }
    }
}
