using System.Collections.Generic;
using System.Collections.ObjectModel;
using MonoMac.Foundation;
using OpenUtau.Core.Editing;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class BatchEditProjectViewModel: ViewModelBase {
        [Reactive] public partial ObservableCollection<BatchEdit> ApplyingEdits { get; set; }

        // Available Batch Edits
        public ResetPitchBends ResetPitchBends;
        public ResetAllExpressions resetAllExpressions;
        public ClearVibratos clearVibratos;
        public ResetVibratos resetVibratos;
        public ClearTimings clearTimings;
        public ResetAliases resetAliases;

        public BatchEditProjectViewModel(ObservableCollection<BatchEdit> applyingEdits) {
            ApplyingEdits = applyingEdits;

            ResetPitchBends = new ResetPitchBends();
            resetAllExpressions = new ResetAllExpressions();
            clearVibratos = new ClearVibratos();
            resetVibratos = new ResetVibratos();
            clearTimings = new ClearTimings();
            resetAliases = new ResetAliases();
        }
    }
}
