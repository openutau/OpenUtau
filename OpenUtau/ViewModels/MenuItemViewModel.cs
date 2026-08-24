using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.ViewModels {
    public class MenuItemViewModel {
        public string? Header { get; set; }
        public ICommand? Command { get; set; }
        public ICommand? SecondaryCommand { get; set; }
        public object? CommandParameter { get; set; }
        public IList<MenuItemViewModel>? Items { get; set; }
        public double Height { get; set; } = 24;
        public bool IsChecked { get; set; } = false;
        public KeyGesture? InputGesture { get; set; }
        public bool IsEnabled { get; set; } = true;
        public object? Icon { get; set; }
        public virtual object HeaderViewModel => this;

        public MenuItemViewModel() { }
        public MenuItemViewModel(bool isChecked) {
            IsChecked = isChecked;
            Dispatcher.UIThread.Post(() => {
                Icon = new Path {
                    IsVisible = isChecked,
                    Classes = { "checkmenu" },
                };
            });
        }
    }

    public class SingerMenuItemViewModel : MenuItemViewModel {
        public bool IsFavourite {
            get {
                if(CommandParameter is USinger singer) {
                    return singer.IsFavourite;
                }
                return false;
            }
            set {
                if (CommandParameter is USinger singer) {
                    singer.IsFavourite = value;
                }
            }
        }

        public string? Location {
            get {
                if (CommandParameter is USinger singer) {
                    return singer.Location;
                }
                return null;
            }
        }
    }

    public class PhonemizerMenuItemViewModel : MenuItemViewModel {
    }
}
