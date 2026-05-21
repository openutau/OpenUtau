using System;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    class LyricsViewModel : ViewModelBase {
        [Reactive] public string? Text { get; set; } = string.Empty;
        [Reactive] public int CurrentCount { get; set; }
        [Reactive] public int TotalCount { get; set; }
        [Reactive] public bool LivePreview { get; set; } = true;
        [Reactive] public bool ApplySelection { get; set; } = true;

        private UVoicePart? part;
        private UNote[]? notes;
        private UNote[]? selection;
        private string[]? startLyrics;

        public LyricsViewModel() {
            this.WhenAnyValue(x => x.LivePreview,
                x => x.Text)
                .Subscribe(t => {
                    Preview(t.Item1);
                });
            this.WhenAnyValue(x => x.ApplySelection)
                .Subscribe(a => {
                    UpdateTotalCount();
                    Preview(LivePreview);
                });
        }

        public void Start(UVoicePart part, UNote[] notes, UNote[] selection) {
            this.part = part;
            this.notes = notes;
            this.selection = selection;
            if (selection.Length < 1) {
                ApplySelection = false;
            }

            UpdateTotalCount();
            CurrentCount = TotalCount;
            Text = SplitLyrics.Join(startLyrics!);
            DocManager.Inst.StartUndoGroup("command.note.lyric");
        }

        private void Preview(bool update) {
            var notes = ApplySelection ? selection : this.notes;
            if (startLyrics == null || notes == null || part == null) {
                return;
            }
            DocManager.Inst.RollBackUndoGroup();
            var lyrics = SplitLyrics.Split(Text);
            CurrentCount = lyrics.Count;
            if (update) {
                for (int i = 0; i < lyrics.Count && i < notes.Length; ++i) {
                    if (notes[i].lyric != lyrics[i]) {
                        DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(part, notes[i], lyrics[i]));
                    }
                }
            }
        }

        private void UpdateTotalCount() {
            if (ApplySelection) {
                TotalCount = selection?.Length ?? 0;
                startLyrics = selection?.Select(n => n.lyric).ToArray();
            } else {
                TotalCount = notes?.Length ?? 0;
                startLyrics = notes?.Select(n => n.lyric).ToArray();
            }
        }

        public void Reset() {
            if (startLyrics == null) {
                return;
            }
            DocManager.Inst.RollBackUndoGroup();
            Text = SplitLyrics.Join(startLyrics);
        }

        public void Cancel() {
            DocManager.Inst.RollBackUndoGroup();
            DocManager.Inst.EndUndoGroup();
        }

        public void Finish() {
            Preview(true);
            DocManager.Inst.EndUndoGroup();
        }
    }
}
