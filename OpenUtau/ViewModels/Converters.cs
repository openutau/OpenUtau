using System;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

namespace OpenUtau.App.ViewModels {
    public class CultureNameConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is CultureInfo cultureInfo) {

            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class AutosaveModeConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is string mode) {
                return ThemeManager.GetString($"prefs.advanced.autosave.mode.{mode.ToLower()}");
            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class AutosaveIntervalConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is string seconds && int.TryParse(seconds, out int secs)) {
                return secs < 60 ? $"{secs} s" : $"{secs / 60} min";
            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class EncodingNameConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as Encoding)?.EncodingName ?? string.Empty;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
