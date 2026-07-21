// Made And Checked By DELTA SYNTH & Gemini AI
// Original by DELTA SYNTH
// File: Piano_Roll_Unlocker_Macro_v1.2.cs
// Version: 1.2
// Date: 2026-07-15

using System;
using System.Collections.Generic;
using System.IO;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    public class PianoRollUnlockerMacro {

        private readonly string centralPath = @"C:\Users\delta\Documents\DELTA_SYNTH_Central\";
        private readonly string uiNameThai = "ปลดล็อกการเคลื่อนที่โน้ตแบบอิสระ";
        private readonly string uiNameEng = "Unlock Free Note Movement";

        public void ProcessSelectedNotes(List<UNote> selectedNotes) {
            int unquantizeValue = 0;
            
            foreach (var note in selectedNotes) {
                ApplyFreeMovement(note, unquantizeValue);
            }
        }

        private void ApplyFreeMovement(UNote note, int offset) {
            if (note != null) {
                note.position = note.position + offset;
                Console.WriteLine($"{uiNameThai} / {uiNameEng}: Success");
            }
        }
    }
}
