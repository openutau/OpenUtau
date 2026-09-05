using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    class SearchNoteViewModel : ViewModelBase, ICmdSubscriber {
        [Reactive] public bool NoteMode { get; set; }
        [Reactive] public string SearchWord { get; set; } = "";
        public string Watermark { get => ThemeManager.GetString(NoteMode ? "pianoroll.menu.searchnote" : "pianoroll.menu.searchnote.searchalias"); }
        [Reactive] public int Count { get; private set; }
        public ReactiveCommand<string, Unit> SelectCommand { get; }

        NotesViewModel notesViewModel { get; }
        List<UNote> notes = new List<UNote>();
        int selection = -1;
        [Reactive]public string ResultCount { get; private set; } = "";
        bool CaseSensitive{ get; set; } = true;
        bool WholeWord{ get; set; } = false;

        public SearchNoteViewModel(NotesViewModel notesViewModel) {
            this.notesViewModel = notesViewModel;

            this.WhenAnyValue(x => x.SearchWord)
                .Subscribe(s => {
                    Search();
                });
            this.WhenAnyValue(x => x.NoteMode)
                .Subscribe(s => {
                    this.RaisePropertyChanged(nameof(Watermark));
                    Search();
                });

            SelectCommand = ReactiveCommand.Create<string>(select => {
                switch (select) {
                    case "prev":
                        Prev();
                        break;
                    case "next":
                        Next();
                        break;
                    case "all":
                        SelectAll();
                        break;
                }
            });
            DocManager.Inst.AddSubscriber(this);
        }

        bool IsMatch(string lyric) {
            string noteStr = CaseSensitive ? lyric : lyric.ToLower();
            string matchStr = CaseSensitive ? SearchWord : SearchWord.ToLower();
            if (WholeWord) {
                return noteStr == matchStr;
            } else {
                return noteStr.Contains(matchStr);
            }
        }

        void Search() {
            if (!string.IsNullOrEmpty(SearchWord) && notesViewModel.Part != null) {
                if (NoteMode) {
                    notes = notesViewModel.Part.notes.Where(n => IsMatch(n.lyric)).ToList();
                } else {
                    notes = notesViewModel.Part.phonemes.Where(p => IsMatch(p.phoneme)).Select(p => p.Parent).Distinct().ToList();
                }
                Count = notes.Count();
            } else {
                notes.Clear();
                Count = 0;
            }
            selection = -1;
            UpdateResult();
        }

        public void Prev() {
            if (notes.Count() == 0) {
                selection = -1;
            } else if (notes.Count() == 1) {
                selection = 0;
            } else if (selection <= 0 || selection > notes.Count()) {
                selection = notes.Count() - 1;
            } else {
                selection--;
            }
            if (selection >= 0) {
                var note = notes[selection];
                notesViewModel.SelectNote(note);
                if (notesViewModel.Part != null) {
                    DocManager.Inst.ExecuteCmd(new FocusNoteNotification(notesViewModel.Part, note));
                }
            }
            UpdateResult();
        }

        public void Next() {
            if(notes.Count() == 0) {
                selection = -1;
            } else if (notes.Count() == 1) {
                selection = 0;
            } else if (selection + 1 >= notes.Count()) {
                selection = 0;
            } else {
                selection++;
            }
            if (selection >= 0) {
                var note = notes[selection];
                notesViewModel.SelectNote(note);
                if (notesViewModel.Part != null) {
                    DocManager.Inst.ExecuteCmd(new FocusNoteNotification(notesViewModel.Part, note));
                }
            }
            UpdateResult();
        }

        public void SelectAll() {
            notesViewModel.Selection.SelectNone();
            foreach (var note in notes) {
                notesViewModel.Selection.Add(note);
            }
            MessageBus.Current.SendMessage(new NotesSelectionEvent(notesViewModel.Selection));
        }

        public void UpdateResult(){
            if (selection >= 0) {
                ResultCount = $"{selection + 1}/{Count}";
            }else{
                ResultCount = $"{Count}";
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is UNotification notif) {
                if (cmd is LoadPartNotification) {
                    Search();
                    Next();
                }
            }
        }
    }
}
