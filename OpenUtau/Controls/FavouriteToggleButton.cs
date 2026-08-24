using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using OpenUtau.App;

namespace OpenUtau.App.Controls {

    public class FavouriteToggleButton : ToggleButton {
        private readonly Path _iconPath;

        public FavouriteToggleButton() {
            Padding = new Avalonia.Thickness(0);
            //this.Height = 20;
            //this.Width = 20;
            // Create icon Path.
            _iconPath = new Path {
                Fill = SolidColorBrush.Parse("#00000000"),
                Stroke = ThemeManager.AccentBrush3,
                StrokeThickness = 2,
                Data = Geometry.Parse("M12,21.35L10.55,20.03C5.4,15.36,2,12.28,2,8.5C2,5.42,4.42,3,7.5,3C9.24,3,10.91,3.81,12,5.09C13.09,3.81,14.76,3,16.5,3C19.58,3,22,5.42,22,8.5C22,12.28,18.6,15.36,13.45,20.04L12,21.35Z"),
                RenderTransform = new ScaleTransform { ScaleX = 0.5, ScaleY = 0.5 }
            };

            this.Content = _iconPath;

            // Change icon on click.
            this.PropertyChanged += (sender, e) => {
                if (e.Property == IsCheckedProperty) {
                    UpdateIcon(IsChecked ?? false);
                }
            };
        }

        private void UpdateIcon(bool isChecked) {
            if (isChecked) {
                _iconPath.Fill = ThemeManager.AccentBrush3;
            } else {
                _iconPath.Fill = SolidColorBrush.Parse("#00000000");
            }
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
