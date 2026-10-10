using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core {
    /// <summary>A registry package (see <see cref="PackageManager"/>) is needed but not installed.</summary>
    public class MissingPackageException : MessageCustomizableException {
        const string Key = "<translate:packages.errors.missing>";

        public MissingPackageException(params string[] packageIds)
            : base($"Package \"{string.Join(", ", packageIds)}\" not found", Key,
                new Exception($"Package \"{string.Join(", ", packageIds)}\" not found"), false,
                new object[] { string.Join(", ", packageIds) }) { }

        /// <summary>
        /// The missing packages anywhere in an exception tree, without duplicates. Matched by
        /// message, since a MessageCustomizableException built from one copies its message, not its type.
        /// </summary>
        public static IReadOnlyList<string> Collect(Exception? e) => Walk(e).Distinct().ToList();

        static IEnumerable<string> Walk(Exception? e) => e switch {
            null => [],
            MessageCustomizableException { TranslatableMessage: Key, Replaces: [string ids, ..] } => ids.Split(", "),
            MessageCustomizableException mce => Walk(mce.SubstanceException),
            AggregateException ae => ae.InnerExceptions.SelectMany(Walk),
            _ => Walk(e.InnerException),
        };
    }
}
