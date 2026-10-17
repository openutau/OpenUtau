using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;

namespace OpenUtau.App.Controls {
    static class TextLayoutCache {
        private static readonly Dictionary<Tuple<string, IBrush, double, bool, bool, FontFamily>, TextLayout> cache
            = new Dictionary<Tuple<string, IBrush, double, bool, bool, FontFamily>, TextLayout>();

        public static void Clear() {
            cache.Clear();
        }

        public static TextLayout Get(string text, IBrush brush, double fontSize, bool bold = false, bool useUiFont = true) {
            var fontFamily = GetUiFontFamily(useUiFont);
            var key = Tuple.Create(text, brush, fontSize, bold, useUiFont, fontFamily);
            if (!cache.TryGetValue(key, out var textLayout)) {
                var fontWeight = bold ? FontWeight.Bold : FontWeight.Normal;
                textLayout = new TextLayout(
                    text,
                    new Typeface(fontFamily, weight: fontWeight),
                    fontSize,
                    brush,
                    TextAlignment.Left,
                    TextWrapping.NoWrap);
                cache.Add(key, textLayout);
            }
            return textLayout;
        }

        private static FontFamily GetUiFontFamily(bool useUiFont) {
            if (useUiFont && Application.Current?.Resources.TryGetResource(
                              "ui.fontfamily", ThemeVariant.Default, out var resource) == true
                          && resource is FontFamily fontFamily) {
                return fontFamily;
            }
            return FontFamily.Default;
        }
    }
}
