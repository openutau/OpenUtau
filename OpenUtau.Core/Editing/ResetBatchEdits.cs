using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Editing {
    class TickRange{
        public int start;
        public int end;

        public TickRange(int start, int end){
            this.start = start;
            this.end = end;
        }

        public TickRange copy(){
            return new TickRange(start, end);
        }
    }

    static class SelectionUtils{
        /// <summary>
        /// Simplify the time selection by merging overlapping ranges.
        /// </summary>
        /// <param name="ranges">Time selection to be simplified</param>
        /// <returns></returns>
        public static List<TickRange> SimplifyTimeSelection(List<TickRange> ranges){
            var result = new List<TickRange>();
            if(ranges.Count == 0){
                return result;
            }
            ranges.Sort((a, b) => a.start - b.start);
            var current = ranges[0].copy();
            for(int i = 1; i < ranges.Count; i++){
                var next = ranges[i];
                if(next.start <= current.end){
                    current.end = Math.Max(current.end, next.end);
                }else{
                    result.Add(current);
                    current = next;
                }
            }
            result.Add(current);
            return result;
        }
        
        public static Dictionary<UNote, List<UPhoneme>> NotePhonemes(UVoicePart part){
            var result = new Dictionary<UNote, List<UPhoneme>>();
            foreach(var phoneme in part.phonemes){
                var note = phoneme.Parent;
                if(result.ContainsKey(note)){
                    result[note].Add(phoneme);
                }else{
                    result[note] = new List<UPhoneme>(){phoneme};
                }
            }
            return result;
        }
        
        /// <summary>
        /// Get the tick ranges of the selected notes, relative to the beginning of the part.
        /// </summary>
        /// <param name="part"></param>
        /// <param name="selectedNotes"></param>
        /// <returns></returns>
        public static List<TickRange> SelectedTickRanges(UVoicePart part, List<UNote> selectedNotes) {
            var notePhonemes = NotePhonemes(part);
            var result = selectedNotes.Select(note => {
                int start = note.position;
                if(note.Prev.End < note.position 
                    && notePhonemes.TryGetValue(note, out var phonemes) 
                    && phonemes.Count > 0
                    ){
                    start = Math.Min(start, phonemes[0].position);
                }
                int end = note.End;
                return new TickRange(start - 1, end + 1);
            }).ToList();
            return SimplifyTimeSelection(result);
        }
    }

    public class ResetPitchBends : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ResetPitchBends() {
            name = "pianoroll.menu.notes.reset.pitchbends";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.reset", true);
            foreach (var note in notes) {
                docManager.ExecuteCmd(new ResetPitchPointsCommand(part, note));
            }
            docManager.EndUndoGroup();
        }
    }

    public class ResetAllExpressions : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ResetAllExpressions() {
            name = "pianoroll.menu.notes.reset.exps";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.reset", true);
            //reset numerical and options expressions
            foreach (var note in notes) {
                if (note.phonemeExpressions.Count > 0) {
                    docManager.ExecuteCmd(new ResetExpressionsCommand(part, note));
                }
            }
            //reset curve expressions
            var curveAbbrs = part.curves.Select(c => c.abbr).ToArray();
            foreach (var abbr in curveAbbrs) {    
                if(notes.Count == part.notes.Count){
                    //All notes are selected
                    docManager.ExecuteCmd(new ClearCurveCommand(part, abbr));
                }
                else{
                    var selectedTickRanges = SelectionUtils.SelectedTickRanges(part, notes);
                    int defaultValue = (int)part.curves.First(c => c.abbr == abbr).descriptor.defaultValue;
                    foreach(var range in selectedTickRanges){
                        docManager.ExecuteCmd(new PasteCurveCommand(project, part, Format.Ustx.PITD,
                            range.start, defaultValue,
                            range.end, defaultValue));
                    }
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ClearVibratos : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ClearVibratos() {
            name = "pianoroll.menu.notes.clear.vibratos";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.reset", true);
            foreach (var note in notes) {
                if (note.vibrato.length > 0) {
                    docManager.ExecuteCmd(new VibratoLengthCommand(part, note, 0));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ResetVibratos : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ResetVibratos() {
            name = "pianoroll.menu.notes.reset.vibratos";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.reset", true);
            var vibrato = new UVibrato();
            foreach (var note in notes) {
                docManager.ExecuteCmd(new SetVibratoCommand(part, note, vibrato));
                if (NotePresets.Default.AutoVibratoToggle && note.duration >= NotePresets.Default.AutoVibratoNoteDuration) {
                    docManager.ExecuteCmd(new VibratoLengthCommand(part, note, NotePresets.Default.DefaultVibrato.VibratoLength));
                } else {
                    docManager.ExecuteCmd(new VibratoLengthCommand(part, note, 0));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ClearTimings : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ClearTimings() {
            name = "pianoroll.menu.notes.reset.phonemetimings";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.reset", true);
            foreach (var note in notes) {
                bool shouldClear = false;
                foreach (var o in note.phonemeOverrides) {
                    if (o.offset != null || o.preutterDelta != null || o.overlapDelta != null) {
                        shouldClear = true;
                        break;
                    }
                }
                if (shouldClear) {
                    docManager.ExecuteCmd(new ClearPhonemeTimingCommand(part, note));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ResetAliases : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ResetAliases() {
            name = "pianoroll.menu.notes.reset.aliases";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.reset", true);
            foreach (var note in notes) {
                foreach (var o in note.phonemeOverrides) {
                    if (o.phoneme != null) {
                        docManager.ExecuteCmd(new ChangePhonemeAliasCommand(part, note, o.index, null));
                    }
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class ResetAll : BatchEdit {
        public virtual string Name => name;

        private string name;

        public ResetAll() {
            name = "pianoroll.menu.notes.reset.all";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.reset", true);
            foreach (var note in notes) {
                // pitch points
                docManager.ExecuteCmd(new ResetPitchPointsCommand(part, note));
                // expressions
                if (note.phonemeExpressions.Count > 0) {
                    docManager.ExecuteCmd(new ResetExpressionsCommand(part, note));
                }
                // vibrato
                if (note.vibrato.length > 0) {
                    docManager.ExecuteCmd(new VibratoLengthCommand(part, note, 0));
                }
                // timings
                bool shouldClear = false;
                foreach (var o in note.phonemeOverrides) {
                    if (o.offset != null || o.preutterDelta != null || o.overlapDelta != null) {
                        shouldClear = true;
                        break;
                    }
                }
                if (shouldClear) {
                    docManager.ExecuteCmd(new ClearPhonemeTimingCommand(part, note));
                }
                // aliases
                foreach (var o in note.phonemeOverrides) {
                    if (o.phoneme != null) {
                        docManager.ExecuteCmd(new ChangePhonemeAliasCommand(part, note, o.index, null));
                    }
                }
            }
            //curve expressions
            var curveAbbrs = part.curves.Select(c => c.abbr).ToArray();
            foreach (var abbr in curveAbbrs) {    
                if(notes.Count == part.notes.Count){
                    //All notes are selected
                    docManager.ExecuteCmd(new ClearCurveCommand(part, abbr));
                }
                else{
                    var selectedTickRanges = SelectionUtils.SelectedTickRanges(part, notes);
                    int defaultValue = (int)part.curves.First(c => c.abbr == abbr).descriptor.defaultValue;
                    foreach(var range in selectedTickRanges){
                        docManager.ExecuteCmd(new PasteCurveCommand(project, part, abbr,
                            range.start, defaultValue,
                            range.end, defaultValue));
                    }
                }
            }
            docManager.EndUndoGroup();
        }
    }
}
