using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls {
    public partial class SearchBar : UserControl {
        SearchNoteViewModel? viewModel;
        private DispatcherTimer? focusTimer;
        private bool noteMode = true;

        public SearchBar() {
            InitializeComponent();
            IsVisible = false;
        }

        public void Show(NotesViewModel notesViewModel) {
            viewModel = new SearchNoteViewModel(notesViewModel);
            DataContext = viewModel;
            viewModel.NoteMode = noteMode;
            //If there is a note selected, use its lyric as the search word
            if (notesViewModel.Part != null && notesViewModel.Part.notes.Count > 0) {
                if (notesViewModel.Selection.Count > 0) {
                    if (notesViewModel.Selection.FirstOrDefault() is UNote note) {
                        if (!string.IsNullOrEmpty(note.lyric)) {
                            viewModel.SearchWord = note.lyric;
                        }
                    }
                }
            }
            IsVisible = true;
            box.SelectAll();
            focusTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(15),
                DispatcherPriority.Normal,
                FocusTimer_Tick);
            focusTimer.Start();
        }

        private void FocusTimer_Tick(object? sender, EventArgs e) {
            box.Focus();
            if (focusTimer != null) {
                focusTimer.Tick -= FocusTimer_Tick;
                focusTimer.Stop();
                focusTimer = null;
            }
        }

        public void OnClose(object sender, RoutedEventArgs args) {
            IsVisible = false;
            if (viewModel != null) {
                noteMode = viewModel.NoteMode;
            }
        }

        private void Box_GotFocus(object? sender, FocusChangedEventArgs e) {
            box.SelectAll();
        }

        private void Box_KeyDown(object? sender, KeyEventArgs e){
            if(!IsVisible){
                return;
            }
            bool isShift = e.KeyModifiers == KeyModifiers.Shift;
            switch (e.Key){
                case Key.Enter:
                    if (DataContext is SearchNoteViewModel viewModel){
                        if(isShift){
                            viewModel.Prev();
                        }else{
                            viewModel.Next();
                        }
                    }
                    e.Handled = true;
                    break;
                case Key.Escape:
                    IsVisible = false;
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }
    }
}
