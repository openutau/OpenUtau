using System;
using System.Collections.Generic;
using System.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Editing {
    public interface BatchEdit {
        string Name { get; }
        bool IsAsync => false;
        void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager);

        void RunAsync(
            UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager,
            Action<int, int> setProgressCallback, CancellationToken cancellationToken) {
            Run(project, part, selectedNotes, docManager);
        }
    }
}
