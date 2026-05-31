using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

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
        }

        private void Box_GotFocus(object? sender, GotFocusEventArgs e) {
            box.SelectAll();
        }

        private void Box_LostFocus(object? sender, RoutedEventArgs e) {
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
                    if (!viewModel.IsAliasBox) {
                        if (listBox.SelectedItem is LyricBoxViewModel.SuggestionItem item1) {
                            box.Text = item1.Alias;
                        }
                        OnTab(e.KeyModifiers);
                    }
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
                    if (!viewModel.IsAliasBox) {
                        OnTab(e.KeyModifiers);
                    }
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
            UVoicePart? part = viewModel.Part;
            UNote? tabTo = null;
            var tabFrom = viewModel.NoteOrPhoneme as LyricBoxNote;
            if (keyModifiers == KeyModifiers.None) {
                tabTo = tabFrom?.Unwrap().Next;
            } else if (keyModifiers == KeyModifiers.Shift) {
                tabTo = tabFrom?.Unwrap().Prev;
            }
            EndEdit(true);
            if (tabTo != null && part != null) {
                DocManager.Inst.ExecuteCmd(new FocusNoteNotification(part, tabTo));
                Show(part, new LyricBoxNote(tabTo), tabTo.lyric);
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
