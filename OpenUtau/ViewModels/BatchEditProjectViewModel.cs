using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using MonoMac.Foundation;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public partial class BatchEditProjectViewModel: ViewModelBase {
        [Reactive] public partial bool DoResetAllExpressions { get; set; }
        [Reactive] public partial bool DoResetPitchBends { get; set; }
        [Reactive] public partial bool DoResetAliases { get; set; }
        [Reactive] public partial bool DoResetVibratos { get; set; }

        private UProject project;

        public BatchEditProjectViewModel(UProject project) {
            this.project = project;
        }

        public void RunSelected() {
            List<BatchEdit> edits = new List<BatchEdit>();

            if (DoResetAllExpressions) edits.Add(new ResetAllExpressions());
            if (DoResetPitchBends) edits.Add(new ResetPitchBends());
            if (DoResetAliases) edits.Add(new ResetAliases());
            if (DoResetVibratos) edits.Add(new ResetVibratos());

            foreach (var batchEdit in edits) {
                foreach (var uPart in project.parts.Where(part => part is UVoicePart)) {
                    var voicePart = (UVoicePart)uPart;
                    batchEdit.Run(project, voicePart, voicePart.notes.ToList(), DocManager.Inst);
                }
            }
        }
    }
}
