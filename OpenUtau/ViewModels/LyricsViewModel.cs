using System;
using System.Linq;
using System.Reactive.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress;

namespace OpenUtau.App.ViewModels {
    public class LyricsViewModel : ViewModelBase {
        [Reactive] public string Text { get; set; } = string.Empty;
        [Reactive] public int CurrentCount { get; set; }
        [Reactive] public int TotalCount { get; set; }
        [Reactive] public bool SkipSymbols { get; set; } = true;

        public bool IsFocused { get; set; } = false;

        private NotesViewModel notesViewModel;
        private UNote[] notes = [];
        private UNote[] selection = [];

        public LyricsViewModel(NotesViewModel notesVm) {
            notesViewModel = notesVm;

            this.WhenAnyValue(x => x.SkipSymbols)
                .Subscribe(a => {
                    FilterNotes();
                });

            MessageBus.Current.Listen<NotesSelectionEvent>()
                .Subscribe(e => {
                    if (e.tempSelectedNotes.Length > 0) {
                        selection = [];
                    } else {
                        selection = e.selectedNotes.ToArray();
                    }
                    FilterNotes();
                });
            selection = notesViewModel.Selection.ToArray();
            FilterNotes();
        }

        private void FilterNotes() {
            if (IsFocused) {
                DocManager.Inst.EndUndoGroup();
                IsFocused = false;
            }
            if (notesViewModel == null || notesViewModel.Part == null) {
                notes = [];
                Text = string.Empty;
                TotalCount = 0;
                CurrentCount = 0;
            } else if (selection.Length == 0) {
                notes = [];
                CurrentCount = SplitLyrics.Split(Text).Count;
                if (TotalCount == 0) return;
                Text = string.Empty;
                TotalCount = 0;
                CurrentCount = 0;
            } else {
                if (SkipSymbols) {
                    notes = selection.Where(n => n.lyric != "R" && n.lyric != "-" && n.lyric != "+~").ToArray();
                } else {
                    notes = selection.ToArray();
                }
                Text = SplitLyrics.Join(notes.Select(n => n.lyric));
                TotalCount = notes.Length;
                CurrentCount = SplitLyrics.Split(Text).Count;
            }
        }

        public void ApplyLyrics() {
            var lyrics = SplitLyrics.Split(Text);
            CurrentCount = lyrics.Count;

            if (notesViewModel == null || notesViewModel.Part == null || !IsFocused || notes.Length == 0 || lyrics.Count == 0) {
                return;
            }

            int count = Math.Min(lyrics.Count, notes.Length);
            DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(notesViewModel.Part, notes.Take(count).ToArray(), lyrics.Take(count).ToArray()));
        }

        public string? GetFirstLyric() {
            var split = SplitLyrics.Split(Text);
            if (string.IsNullOrWhiteSpace(split.FirstOrDefault())) return null;
            var lyric = split[0];
            split.RemoveAt(0);
            Text = SplitLyrics.Join(split);
            return lyric;
        }
    }
}
