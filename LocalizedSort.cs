using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OpenUtau.Core.Util {

    public class LocalizedComparer : IComparer<string> {
        CultureInfo cultureInfo;
        public LocalizedComparer(CultureInfo cultureInfo) {
            this.cultureInfo = cultureInfo;
        }

        public int Compare(string x, string y) {
            return cultureInfo.CompareInfo.Compare(x, y);
        }
    }
    public static class LocalizedSort {
        public static IEnumerable<T> LocalizedOrderBy<T>(this IEnumerable<T> source, Func<T, string> selector) {
            var sortingOrder = Preferences.Default.SortingOrder;
            CultureInfo culture;
            if(sortingOrder == null) {
                //Follow the display language
                culture = CultureInfo.GetCultureInfo(Preferences.Default.Language);
            } else if(sortingOrder == String.Empty){
                //Don't translate
                culture = CultureInfo.InvariantCulture;
            } else {
                culture = CultureInfo.GetCultureInfo(sortingOrder);
            }
            var comparer = new LocalizedComparer(culture);
            return source.OrderBy(selector, comparer);
        }
    }
}
