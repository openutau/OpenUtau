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
        KnifeTool = 30,
        PitchPointTool = 40,
        DrawPitchTool = 50,
        PitchLineTool = 60,
        PitchSCurveTool = 70,
        PitchSineWaveTool = 80,
        PitchSmoothenTool = 90
    }

    public class EditTool {
        public int BaseTool { get; set; } = 1;
        public int PenToolVariation { get; set; } = 0;
        public bool OverwritePitch { get; set; } = false;

        [JsonIgnore]
        public EditTools CurrentTool {
            get {
                switch (BaseTool) {
                    case 1:
                        return PenToolVariation == 1 ? EditTools.PenPlusTool : EditTools.PenTool;
                    default:
                        return (EditTools)(BaseTool * 10);
                }
            }
        }
        [JsonIgnore] public bool IsPitchTool => BaseTool >= 5 && BaseTool <= 9;
        public bool IsMatch(IEnumerable<EditTools> tools) => tools.Contains(CurrentTool);
    }
}
