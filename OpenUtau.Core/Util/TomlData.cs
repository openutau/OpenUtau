using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenUtau.Core.Util {
    public sealed class TomlData {
        static readonly IReadOnlyDictionary<string, object?> emptySection =
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

        readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> sections;

        private TomlData(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> sections) {
            this.sections = sections;
        }

        public IReadOnlyCollection<string> SectionNames => sections.Keys.ToArray();

        /// <summary>
        /// Loads a TOML file by reading it line by line.
        /// Root-level keys are stored under the empty section name.
        /// </summary>
        public static TomlData Load(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            var sections = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase) {
                [string.Empty] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            };
            var currentSection = string.Empty;
            var lineNumber = 0;

            foreach (var rawLine in File.ReadLines(filePath, Encoding.UTF8)) {
                lineNumber++;
                var line = StripComment(rawLine).Trim();
                if (string.IsNullOrEmpty(line)) {
                    continue;
                }
                if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal)) {
                    throw new InvalidDataException($"Array of tables is not supported: {Path.GetFileName(filePath)}:{lineNumber}");
                }
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) {
                    currentSection = line[1..^1].Trim();
                    if (string.IsNullOrEmpty(currentSection)) {
                        throw new InvalidDataException($"Section name is empty: {Path.GetFileName(filePath)}:{lineNumber}");
                    }
                    if (!sections.ContainsKey(currentSection)) {
                        sections[currentSection] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    }
                    continue;
                }

                var separatorIndex = FindKeyValueSeparator(line);
                if (separatorIndex < 0) {
                    throw new InvalidDataException($"Invalid TOML key/value line: {Path.GetFileName(filePath)}:{lineNumber}");
                }

                var key = line[..separatorIndex].Trim();
                var valueText = line[(separatorIndex + 1)..].Trim();
                if (string.IsNullOrEmpty(key)) {
                    throw new InvalidDataException($"Key name is empty: {Path.GetFileName(filePath)}:{lineNumber}");
                }

                sections[currentSection][key] = ParseValue(valueText);
            }

            return new TomlData(sections.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, object?>)new ReadOnlyDictionary<string, object?>(pair.Value),
                StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all key/value pairs for a section.
        /// Root-level keys use an empty section name.
        /// </summary>
        public bool TryGetSection(string section, out IReadOnlyDictionary<string, object?> values) {
            ArgumentNullException.ThrowIfNull(section);
            if (sections.TryGetValue(section, out var sectionValues)) {
                values = sectionValues;
                return true;
            }
            values = emptySection;
            return false;
        }

        /// <summary>
        /// Gets a value from a section by key.
        /// Root-level keys use an empty section name.
        /// </summary>
        public bool TryGetValue(string section, string key, out object? value) {
            ArgumentNullException.ThrowIfNull(section);
            ArgumentNullException.ThrowIfNull(key);
            if (sections.TryGetValue(section, out var sectionValues) && sectionValues.TryGetValue(key, out value)) {
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Enumerates all TOML entries as section/key/value tuples.
        /// Root-level keys use an empty section name.
        /// </summary>
        public IEnumerable<(string Section, string Key, object? Value)> EnumerateEntries() {
            foreach (var section in sections) {
                foreach (var item in section.Value) {
                    yield return (section.Key, item.Key, item.Value);
                }
            }
        }

        private static int FindKeyValueSeparator(string line) {
            var inDoubleQuote = false;
            var inSingleQuote = false;
            var escape = false;
            for (var i = 0; i < line.Length; i++) {
                var c = line[i];
                if (escape) {
                    escape = false;
                    continue;
                }
                if (c == '\\' && inDoubleQuote) {
                    escape = true;
                    continue;
                }
                if (c == '"' && !inSingleQuote) {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }
                if (c == '\'' && !inDoubleQuote) {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }
                if (c == '=' && !inDoubleQuote && !inSingleQuote) {
                    return i;
                }
            }
            return -1;
        }

        private static string StripComment(string line) {
            var inDoubleQuote = false;
            var inSingleQuote = false;
            var escape = false;
            for (var i = 0; i < line.Length; i++) {
                var c = line[i];
                if (escape) {
                    escape = false;
                    continue;
                }
                if (c == '\\' && inDoubleQuote) {
                    escape = true;
                    continue;
                }
                if (c == '"' && !inSingleQuote) {
                    inDoubleQuote = !inDoubleQuote;
                    continue;
                }
                if (c == '\'' && !inDoubleQuote) {
                    inSingleQuote = !inSingleQuote;
                    continue;
                }
                if (c == '#' && !inDoubleQuote && !inSingleQuote) {
                    return line[..i];
                }
            }
            return line;
        }

        private static object? ParseValue(string valueText) {
            if (string.IsNullOrWhiteSpace(valueText)) {
                return string.Empty;
            }
            if ((valueText.StartsWith('"') && valueText.EndsWith('"')) ||
                (valueText.StartsWith('\'') && valueText.EndsWith('\''))) {
                return valueText[1..^1];
            }
            if (valueText.StartsWith("[", StringComparison.Ordinal) && valueText.EndsWith("]", StringComparison.Ordinal)) {
                return ParseArray(valueText[1..^1]);
            }
            if (bool.TryParse(valueText, out var boolValue)) {
                return boolValue;
            }
            if (long.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue)) {
                return longValue;
            }
            if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)) {
                return doubleValue;
            }
            if (DateTimeOffset.TryParse(valueText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset)) {
                return dateTimeOffset;
            }
            return valueText;
        }

        private static object?[] ParseArray(string valueText) {
            if (string.IsNullOrWhiteSpace(valueText)) {
                return Array.Empty<object?>();
            }
            var values = new List<object?>();
            var builder = new StringBuilder();
            var inDoubleQuote = false;
            var inSingleQuote = false;
            var escape = false;
            for (var i = 0; i < valueText.Length; i++) {
                var c = valueText[i];
                if (escape) {
                    builder.Append(c);
                    escape = false;
                    continue;
                }
                if (c == '\\' && inDoubleQuote) {
                    builder.Append(c);
                    escape = true;
                    continue;
                }
                if (c == '"' && !inSingleQuote) {
                    inDoubleQuote = !inDoubleQuote;
                    builder.Append(c);
                    continue;
                }
                if (c == '\'' && !inDoubleQuote) {
                    inSingleQuote = !inSingleQuote;
                    builder.Append(c);
                    continue;
                }
                if (c == ',' && !inDoubleQuote && !inSingleQuote) {
                    values.Add(ParseValue(builder.ToString().Trim()));
                    builder.Clear();
                    continue;
                }
                builder.Append(c);
            }
            if (builder.Length > 0) {
                values.Add(ParseValue(builder.ToString().Trim()));
            }
            return values.ToArray();
        }
    }
}
