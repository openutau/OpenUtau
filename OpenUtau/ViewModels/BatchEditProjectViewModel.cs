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

            if (edits.Count == 0) return;

            try {
                DocManager.Inst.StartUndoGroup("dialogs.batchedit.project.caption", true, isDeep: true);
                foreach (var batchEdit in edits) {
                    foreach (var uPart in project.parts.Where(part => part is UVoicePart)) {
                        var voicePart = (UVoicePart)uPart;
                        batchEdit.Run(project, voicePart, voicePart.notes.ToList(), DocManager.Inst);
                    }
                }
            } finally {
                DocManager.Inst.EndUndoGroup();
            }

            // Trigger visual rerender
            foreach (UVoicePart voicePart in project.parts.OfType<UVoicePart>()) {
                DocManager.Inst.ExecuteCmd(new PhraseRenderStateNotification(
                    voicePart,
                    voicePart.position,
                    voicePart.End,
                    false
                    ));
            }
        }
    }
}
