using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OpenUtau.Core.Ustx;
using WanaKanaNet;

namespace OpenUtau.Core.Editing {

    public abstract class SingleNoteLyricEdit : BatchEdit {
        public abstract string Name { get; }
        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes.ToArray() : part.notes.ToArray();
            if (notes.Length == 0) {
                return;
            }
            var lyrics = notes.Select(note => Transform(note.lyric)).ToArray();
            docManager.StartUndoGroup("command.batch.lyric", true);
            docManager.ExecuteCmd(new ChangeNoteLyricCommand(part, notes, lyrics));
            docManager.EndUndoGroup();
        }
        protected abstract string Transform(string lyric);
    }

    public class RomajiToHiragana : SingleNoteLyricEdit {
        static Dictionary<string, string> mapping = new Dictionary<string, string>() {
            {".", "."}, {",", ","}, {":", ":"}, {"/", "/"}, {"!", "!"}, {"?", "?"},
            {"~", "~"}, {"-", "-"}, {"‘", "‘"}, {"’", "’"}, {"“", "“"}, {"”", "”"},
            {"[", "["}, {"]", "]"}, {"(", "("}, {")", ")"}, {"{", "{"}, {"}", "}"},
        };
        private WanaKanaOptions option = new WanaKanaOptions() { CustomKanaMapping = mapping };
        public override string Name => "pianoroll.menu.lyrics.romajitohiragana";
        protected override string Transform(string lyric) {
            string hiragana = WanaKana.ToHiragana(lyric, option).Replace('ゔ','ヴ');
            if(Regex.IsMatch(hiragana, "[ぁ-んァ-ヴ]")) {
                return hiragana;
            } else {
                return lyric;
            }
        }
    }

    public class HiraganaToRomaji : SingleNoteLyricEdit {
        public override string Name => "pianoroll.menu.lyrics.hiraganatoromaji";
        protected override string Transform(string lyric) {
            return WanaKana.ToRomaji(lyric);
        }
    }

    public class JapaneseVCVtoCV : SingleNoteLyricEdit {
        public override string Name => "pianoroll.menu.lyrics.javcvtocv";
        protected override string Transform(string lyric) {
            if (lyric.Length > 2 && lyric[1] == ' ') {
                // When the lyric is like "a あ", "a R" or "- あ", cut off the first two characters.
                return lyric.Substring(2);
            } else {
                // Otherwise cannot recognize VCV, return as is.
                return lyric;
            }
        }
    }

    // Removes suffix like "C4", "C#4" or "Cb4"
    public class RemoveToneSuffix : SingleNoteLyricEdit {
        public override string Name => "pianoroll.menu.lyrics.removetonesuffix";
        protected override string Transform(string lyric) {
            if (Regex.IsMatch(lyric, ".+_?[A-G](#|b)?[1-7]")) {
                return Regex.Replace(lyric, "_?[A-G](#|b)?[1-7]", "");
            }
            return lyric;
        }
    }

    public class RemoveLetterSuffix : SingleNoteLyricEdit {
        public override string Name => "pianoroll.menu.lyrics.removelettersuffix";
        protected override string Transform(string lyric) {
            int pos = lyric.Length - 1;
            while (pos >= 0 && ShouldRemove(lyric[pos])) {
                pos--;
            }
            return lyric.Substring(0, pos + 1);
        }

        private bool ShouldRemove(char c) {
            return (c == '_' || c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') && c != 'R' && c != 'r';
        }
    }

    public class MoveSuffixToVoiceColor : BatchEdit {
        public virtual string Name => name;
        private string name;

        public MoveSuffixToVoiceColor() {
            name = "pianoroll.menu.lyrics.movesuffixtovoicecolor";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes.ToArray() : part.notes.ToArray();
            if (notes.Length == 0) {
                return;
            }
            // Determine the character that is the trigger
            UTrack track = project.tracks[part.trackNo];
            if (track.VoiceColorExp == null || track.VoiceColorExp.options.Length <= 0) {
                return;
            }
            Dictionary<int, string> colors = new Dictionary<int, string>(); // index, trigger

            foreach (var subbank in track.Singer.Subbanks) {
                int clrIndex = track.VoiceColorExp.options.ToList().IndexOf(subbank.Color);
                if (colors.ContainsKey(clrIndex)) {
                    string suffix = "";
                    string value = Regex.Replace(subbank.Suffix.Replace("_", ""), "[A-G](#|b)?[1-7]", "");

                    for (int i = 0; i < colors[clrIndex].Length && i < value.Length; i++) {
                        if(colors[clrIndex][i] == value[i]) {
                            suffix += value[i];
                        } else {
                            break;
                        }
                    }
                    colors[clrIndex] = suffix;
                } else {
                    colors.Add(clrIndex, Regex.Replace(subbank.Suffix.Replace("_", ""), "[A-G](#|b)?[1-7]", ""));
                }
            }

            // Order by the number of letters in the trigger
            var suffixes = colors.Values.ToList();
            suffixes.Remove("");
            suffixes.Sort((a, b) => b.Length - a.Length);

            // Set lyric and color
            docManager.StartUndoGroup("command.batch.lyric", true);
            foreach (var note in notes) {
                foreach (var suffix in suffixes) {
                    if (note.lyric.Contains(suffix)) {
                        string lyric = note.lyric.Split(suffix)[0];
                        docManager.ExecuteCmd(new ChangeNoteLyricCommand(part, note, lyric));

                        int index = colors.FirstOrDefault(c => c.Value == suffix).Key;
                        docManager.ExecuteCmd(new SetNoteExpressionCommand(project, track, part, note, Format.Ustx.CLR, new float?[] { index }));
                        break;
                    }
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class RemovePhoneticHint : SingleNoteLyricEdit {
        static readonly Regex phoneticHintPattern = new Regex(@"\[(.*)\]");
        public override string Name => "pianoroll.menu.lyrics.removephonetichint";
        protected override string Transform(string lyric) {
            var lrc = lyric;
            lrc = phoneticHintPattern.Replace(lrc, match => "");
            if (string.IsNullOrEmpty(lrc)) {
                return lyric;
            }
            return lrc;
        }
    }

    public class DashToPlus : SingleNoteLyricEdit {
        public override string Name => "pianoroll.menu.lyrics.dashtoplus";
        protected override string Transform(string lyric) {
            if (lyric == "-") {
                return lyric.Replace("-", "+");
            } else {
                return lyric;
            }
        }
    }

    public class DashToPlusTilda : SingleNoteLyricEdit {
        public override string Name => "pianoroll.menu.lyrics.dashtoplustilda";
        protected override string Transform(string lyric) {
            if (lyric == "-") {
                return lyric.Replace("-", "+~");
            } else {
                return lyric;
            }
        }
    }

    public class InsertSlur : BatchEdit{
        public virtual string Name => name;
        private string name;

        public InsertSlur() {
            name = "pianoroll.menu.lyrics.insertslur";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            if(selectedNotes.Count == 0){
                return;
            }
            var startPos = selectedNotes.First().position;
            Queue<string> lyricsQueue = new Queue<string>();
            docManager.StartUndoGroup("command.batch.lyric", true);
            foreach(var note in part.notes.Where(n => n.position >= startPos)){
                lyricsQueue.Enqueue(note.lyric);
                if(selectedNotes.Contains(note)){
                    docManager.ExecuteCmd(new ChangeNoteLyricCommand(part, note, "+~"));
                } else {
                    docManager.ExecuteCmd(new ChangeNoteLyricCommand(part, note, lyricsQueue.Dequeue()));
                }
            }
            docManager.EndUndoGroup();
        }
    }
}
