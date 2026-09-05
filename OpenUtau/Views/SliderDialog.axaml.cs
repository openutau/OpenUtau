using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Views {
    public partial class SliderDialog : Window {
        public Action<double>? onFinish;

        public SliderDialog() {
            InitializeComponent();
        }
        public SliderDialog(string title, double value, double min, double max, double tick) {
            InitializeComponent();
            Title = title;
            Slider.Value = value;
            Slider.Minimum = min;
            Slider.Maximum = max;
            Slider.TickFrequency = tick;

            this.WhenAnyValue(d => d.Slider.Value)
                    .Subscribe(value => {
                        TextBlock.Text = value.ToString();
                     });
        }

        private void OkButtonClick(object? sender, RoutedEventArgs e) {
            Finish();
        }

        private void Finish() {
            if (onFinish != null) {
                onFinish.Invoke(Slider.Value);
            }
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Close();
            } else if (e.Key == Key.Enter) {
                e.Handled = true;
                Finish();
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}
