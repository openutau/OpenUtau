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

        public void RunSelected() {
            List<BatchEdit> edits = new List<BatchEdit>();

            if (DoResetAllExpressions) edits.Add(new ResetAllExpressions());
            if (DoResetPitchBends) edits.Add(new ResetPitchBends());
            if (DoResetAliases) edits.Add(new ResetAliases());
            if (DoResetVibratos) edits.Add(new ResetVibratos());

            foreach (var batchEdit in edits) {
                foreach (UVoicePart voicePart in DocManager.Inst.Project.parts.Where(part => part is UVoicePart)) {
                    batchEdit.Run(DocManager.Inst.Project, voicePart, voicePart.notes.ToList(), DocManager.Inst);
                }
            }
        }
    }
}
