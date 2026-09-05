using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.Controls {
    public partial class LyricBox : UserControl {
        private LyricBoxViewModel viewModel;
        private TextBox box;
        private ListBox listBox;
        private DispatcherTimer? focusTimer;

        public LyricBox() {
            InitializeComponent();
            DataContext = viewModel = new LyricBoxViewModel();
            box = PART_Box;
            listBox = PART_Suggestions;
            IsVisible = false;
            viewModel.Suggestions.CollectionChanged += (_, _) => {
                Dispatcher.UIThread.Post(() => {
                    listBox.SelectedIndex = -1;
                    viewModel.SelectedSuggestion = null;
                }, DispatcherPriority.Input);
            };
        }

        private void Box_GotFocus(object? sender, FocusChangedEventArgs e) {
            box.SelectAll();
        }

        private void Box_LostFocus(object? sender, FocusChangedEventArgs e) {
            box.CaretIndex = 0;
        }

        private void ListBox_KeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    if (listBox.SelectedItem is LyricBoxViewModel.SuggestionItem item) {
                        box.Text = item.Alias;
                    }
                    EndEdit(true);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    EndEdit();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    if (listBox.SelectedItem is LyricBoxViewModel.SuggestionItem item1) {
                        box.Text = item1.Alias;
                    }
                    OnTab(e.KeyModifiers);
                    e.Handled = true;
                    break;
                case Key.Up:
                    ListBoxSelect(listBox.SelectedIndex - 1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    ListBoxSelect(listBox.SelectedIndex + 1);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    ListBoxSelect(listBox.SelectedIndex - 8);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    ListBoxSelect(listBox.SelectedIndex + 8);
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        private void ListBoxSelect(int index) {
            if (index < 0) {
                if (listBox.SelectedIndex == 0) {
                    index = listBox.ItemCount - 1;
                } else {
                    index = 0;
                }
            } else if (index >= listBox.ItemCount) {
                if (listBox.SelectedIndex == listBox.ItemCount - 1) {
                    index = 0;
                } else {
                    index = listBox.ItemCount - 1;
                }
            }
            listBox.SelectedIndex = index;
        }

        private void Box_KeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    EndEdit(true);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    EndEdit();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    OnTab(e.KeyModifiers);
                    e.Handled = true;
                    break;
                case Key.Up:
                case Key.Down:
                case Key.PageUp:
                case Key.PageDown:
                    listBox.Focus();
                    listBox.SelectedIndex = 0;
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (box.SelectionStart < box.SelectionEnd)
                        box.SelectionEnd = box.SelectionStart;
                    break;
                case Key.Right:
                    if (box.SelectionStart > box.SelectionEnd)
                        box.SelectionEnd = box.SelectionStart;
                    break;
                default:
                    break;
            }
        }

        private void OnTab(KeyModifiers keyModifiers) {
            Log.Error($"OnTab: tabFrom={viewModel.NoteOrPhoneme}, mods={keyModifiers}");
            UVoicePart? part = viewModel.Part;
            var tabFrom = viewModel.NoteOrPhoneme;
            LyricBoxNoteOrPhoneme? tabTo = null;
            UNote? focusNote = null;
            string? text = null;
            if (tabFrom is LyricBoxNote noteBox) {
                UNote? note = noteBox.Unwrap();
                note = keyModifiers == KeyModifiers.None ? note.Next
                    : keyModifiers == KeyModifiers.Shift ? note.Prev
                    : null;
                if (note != null) {
                    tabTo = new LyricBoxNote(note);
                    focusNote = note;
                    text = note.lyric;
                }
            } else if (tabFrom is LyricBoxPhoneme phonemeBox) {
                UPhoneme? phoneme = phonemeBox.Unwrap();
                phoneme = keyModifiers == KeyModifiers.None ? phoneme.Next
                    : keyModifiers == KeyModifiers.Shift ? phoneme.Prev
                    : null;
                if (phoneme != null) {
                    tabTo = new LyricBoxPhoneme(phoneme);
                    focusNote = phoneme.Parent;
                    text = phoneme.phoneme;
                }
            }
            EndEdit(true);
            if (tabTo != null && focusNote != null && text != null && part != null) {
                DocManager.Inst.ExecuteCmd(new FocusNoteNotification(part, focusNote));
                Show(part, tabTo, text);
            }
        }

        public void ListBox_PointerPressed(object sender, PointerPressedEventArgs args) {
            if (sender is DockPanel panel &&
                panel.DataContext is LyricBoxViewModel.SuggestionItem item) {
                box.Text = item.Alias;
            }
            EndEdit(true);
        }

        public void Show(UVoicePart part, LyricBoxNoteOrPhoneme noteOrPhoneme, string text) {
            viewModel.Part = part;
            viewModel.NoteOrPhoneme = noteOrPhoneme;
            viewModel.Text = text;
            viewModel.IsVisible = true;
            box.SelectAll();
            focusTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(15),
                DispatcherPriority.Normal,
                FocusTimer_Tick);
            focusTimer.Start();
        }

        private void FocusTimer_Tick(object? sender, System.EventArgs e) {
            box.Focus();
            box.SelectAll();
            if (focusTimer != null) {
                focusTimer.Tick -= FocusTimer_Tick;
                focusTimer.Stop();
                focusTimer = null;
            }
        }

        public void EndEdit(bool commit = false) {
            if (commit) {
                viewModel.Commit();
            }
            viewModel.Part = null;
            viewModel.NoteOrPhoneme = null;
            viewModel.IsVisible = false;
            viewModel.Text = string.Empty;
        }
    }
}
