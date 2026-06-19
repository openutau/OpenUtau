using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace OpenUtau.Core.Util {
    public enum EditTools {
        CursorTool = 0,
        PenTool = 10,
        PenPlusTool = 11,
        EraserTool = 20,
        DrawPitchTool = 30,
        OverwritePitchTool = 31,
        DrawLinePitchTool = 40,
        OverwriteLinePitchTool = 41,
        KnifeTool = 50
    }

    public class EditTool {
        public int BaseTool { get; set; } = 1;
        public int PenToolVariation { get; set; } = 0;
        public int DrawPitchToolVariation { get; set; } = 0;
        public int DrawLinePitchToolVariation { get; set; } = 0;

        [JsonIgnore]
        public EditTools CurrentTool {
            get {
                switch (BaseTool) {
                    case 1:
                        if (PenToolVariation == 1) {
                            return EditTools.PenPlusTool;
                        } else {
                            return EditTools.PenTool;
                        }
                    case 3:
                        if (DrawPitchToolVariation == 1) {
                            return EditTools.OverwritePitchTool;
                        } else {
                            return EditTools.DrawPitchTool;
                        }
                    case 4:
                        if (DrawLinePitchToolVariation == 1) {
                            return EditTools.OverwriteLinePitchTool;
                        } else {
                            return EditTools.DrawLinePitchTool;
                        }
                    default:
                        return (EditTools)(BaseTool * 10);
                }
            }
        }
        [JsonIgnore] public bool IsPitchTool => BaseTool == 3 || BaseTool == 4;
        public bool IsMatch(IEnumerable<EditTools> tools) => tools.Contains(CurrentTool);
    }
}
