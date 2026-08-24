using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace OpenUtau.App.Controls {

    public class ApplyToAllTracksButton : Button {
        public ApplyToAllTracksButton() {
            Padding = new Avalonia.Thickness(0);
            BorderThickness = new Avalonia.Thickness(0);
            Background = Brushes.Transparent;
            Focusable = false;
            Content = new Path {
                Stroke = ThemeManager.AccentBrush2,
                StrokeThickness = 1.75,
                Data = Geometry.Parse("M3,4 H11 M3,8 H11 M3,12 H11 M10,2 L13,4 L10,6 M10,6 L13,4 L10,2"),
            };
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            e.Handled = true;
        }
    }

}
