using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App.Controls {
    public partial class NotePropertyExpression : UserControl {

        public NotePropertyExpression() {
            InitializeComponent();
            
            slider.AddHandler(PointerPressedEvent, SliderPointerPressed, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, SliderPointerReleased, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerMovedEvent, SliderPointerMoved, RoutingStrategies.Tunnel);
            comboBox.AddHandler(PointerPressedEvent, OnComboBoxPointerPressed, RoutingStrategies.Tunnel);
        }

        // textbox
        private string textBoxValue = string.Empty;
        void OnTextBoxGotFocus(object? sender, GotFocusEventArgs args) {
            Log.Debug("Note property textbox got focus");
            if (sender is TextBox textBox) {
                textBoxValue = textBox.Text ?? string.Empty;
            }
        }
        void OnTextBoxLostFocus(object? sender, RoutedEventArgs args) {
            Log.Debug("Note property textbox lost focus");
            if (sender is TextBox textBox && textBoxValue != textBox.Text) {
                SetNumericalExpressions(textBox.Text);
            }
        }

        // slider
        void SliderPointerPressed(object? sender, PointerPressedEventArgs args) {
            Log.Debug("Note property slider pressed");
            if (sender is Control control) {
                var point = args.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed) {
                    DocManager.Inst.StartUndoGroup("command.property.edit");
                    NotePropertiesViewModel.PanelControlPressed = true;
                } else if (point.Properties.IsRightButtonPressed) {
                    SetNumericalExpressions(null);
                }
            }
        }
        void SliderPointerReleased(object? sender, PointerReleasedEventArgs args) {
            Log.Debug("Note property slider released");
            if (NotePropertiesViewModel.PanelControlPressed) {
                if (sender is Slider slider && DataContext is NotePropertyExpViewModel ViewModel) {
                    ViewModel.SetNumericalExpressions((float)slider.Value);
                }
                NotePropertiesViewModel.PanelControlPressed = false;
                DocManager.Inst.EndUndoGroup();
            }
        }
        void SliderPointerMoved(object? sender, PointerEventArgs args) {
            if (sender is Slider slider && DataContext is NotePropertyExpViewModel ViewModel) {
                ViewModel.SetNumericalExpressions((float)slider.Value);
            }
        }

        void OnComboBoxPointerPressed(object? sender, PointerPressedEventArgs args) {
            Log.Debug("Note property textbox pressed");
            if (sender is ComboBox comboBox) {
                var point = args.GetCurrentPoint(comboBox);
                if (point.Properties.IsRightButtonPressed) {
                    SetNumericalExpressions(null);
                    args.Handled = true;
                }
            }
        }

        private void SetNumericalExpressions(string? expression) {
            if (DataContext is NotePropertyExpViewModel viewModel) {
                DocManager.Inst.StartUndoGroup("command.property.edit");
                NotePropertiesViewModel.PanelControlPressed = true;
                viewModel.SetNumericalExpressions(expression);
                NotePropertiesViewModel.PanelControlPressed = false;
                DocManager.Inst.EndUndoGroup();
            }
        }
    }
}
