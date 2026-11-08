using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public partial class BatchEditProjectViewModel(UProject project) : ViewModelBase {
        [Reactive] public partial bool DoResetAllExpressions { get; set; }
        [Reactive] public partial bool DoResetPitchBends { get; set; }
        [Reactive] public partial bool DoResetAliases { get; set; }
        [Reactive] public partial bool DoResetVibratos { get; set; }

        public void RunSelected() {
            List<BatchEdit> edits = new List<BatchEdit>();

            if (DoResetAllExpressions) edits.Add(new ResetAllExpressions());
            if (DoResetPitchBends) edits.Add(new ResetPitchBends());
            if (DoResetAliases) edits.Add(new ResetAliases());
            if (DoResetVibratos) edits.Add(new ResetVibratos());

            try {
                DocManager.Inst.StartUndoGroup("dialogs.batchedit.project.caption", true);
                foreach (var batchEdit in edits) {
                    foreach (var uPart in project.parts.Where(part => part is UVoicePart)) {
                        var voicePart = (UVoicePart)uPart;
                        batchEdit.Run(project, voicePart, voicePart.notes.ToList(), DocManager.Inst, false);
                    }
                }
            } finally {
                DocManager.Inst.EndUndoGroup();
            }
        }
    }
}
