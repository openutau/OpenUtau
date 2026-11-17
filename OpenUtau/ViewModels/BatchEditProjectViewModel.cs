using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.ViewModels {
    public partial class BatchEditProjectViewModel(UProject project) : ViewModelBase {
        public List<SelectableBatchEdit> ResetBatchEdits { get; set; } = [
            new(new ResetAllExpressions(), false),
            new(new ResetPitchBends(), false),
            new(new ResetAliases(), false),
            new(new ResetVibratos(), false)
        ];


        public void RunSelected() {
            if (ResetBatchEdits.TrueForAll(edit => !edit.Active)) return;

            try {
                DocManager.Inst.StartUndoGroup("dialogs.batchedit.project.caption", true, isDeep: true);
                foreach (var batchEdit in ResetBatchEdits) {
                    foreach (var uPart in project.parts.Where(part => part is UVoicePart)) {
                        var voicePart = (UVoicePart)uPart;
                        batchEdit.Edit.Run(project, voicePart, voicePart.notes.ToList(), DocManager.Inst);
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
            DocManager.Inst.ExecuteCmd(new ToastNotification("MainWindow", "Batch reset done", "command.batch.project.toast"));
        }
    }

    public class SelectableBatchEdit {
        public BatchEdit Edit { get; set; }
        public bool Active { get; set; }

        public string Name {
            get => ThemeManager.GetString(Edit.Name);
        }

        public SelectableBatchEdit(BatchEdit edit, bool active) {
            Edit = edit;
            Active = active;
        }

        public override string ToString() {
            return $"{nameof(Edit)}: {Edit}, {nameof(Active)}: {Active}, {nameof(Name)}: {Name}";
        }
    }
}
