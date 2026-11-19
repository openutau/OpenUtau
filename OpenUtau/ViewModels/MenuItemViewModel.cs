using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using Avalonia.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.ViewModels {
    public class MenuItemViewModel {
        public string? Header { get; set; }
        public ICommand? Command { get; set; }
        public object? CommandParameter { get; set; }
        public IList<MenuItemViewModel>? Items { get; set; }
        public double Height { get; set; } = 24;
        public bool IsChecked { get; set; } = false;
        public KeyGesture? InputGesture { get; set; }
        public bool IsEnabled { get; set; } = true;
        public object? Icon { get; set; }

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
        private object? _icon;
        public new object? Icon {
            get {
                if(_icon == null) {
                    if (CommandParameter is USinger) {
                        var path = new CompiledBindingPathBuilder()
                            .Property(
                                new ClrPropertyInfo(
                                    nameof(IsFavourite), 
                                    obj => ((SingerMenuItemViewModel)obj).IsFavourite,
                                    (obj, val) => ((SingerMenuItemViewModel)obj).IsFavourite = (bool)val!, 
                                    typeof(bool)),
                                    (weakRef, iPropInfo) => PropertyInfoAccessorFactory.CreateInpcPropertyAccessor(weakRef, iPropInfo))
                            .Build();
                        _icon = new FavouriteToggleButton() {
                            [!FavouriteToggleButton.IsCheckedProperty] = new CompiledBindingExtension(path)
                        };
                    }
                }
                return _icon;
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
}
