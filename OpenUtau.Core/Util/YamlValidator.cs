using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.TypeInspectors;
using YamlDotNet.Serialization.TypeResolvers;

namespace OpenUtau.Core {
    public enum YamlDiagnosticKind {
        /// <summary>The text is not valid YAML. Detail: the parser's message.</summary>
        Syntax,
        /// <summary>A key the app does not read, usually a typo. Detail: the key.</summary>
        UnknownKey,
        /// <summary>A value of the wrong kind. Detail: the expected kinds, e.g. "number".</summary>
        WrongType,
        /// <summary>Anything else the schema or the app's reader rejects. Detail: their message.</summary>
        Invalid,
    }

    /// <summary>A problem found in a YAML file. Lines and columns are 1-based; the end is exclusive.</summary>
    public record YamlDiagnostic(
        YamlDiagnosticKind Kind, string Path, string Detail,
        int StartLine, int StartColumn, int EndLine, int EndColumn) {
        /// <summary>
        /// The app could not load the file: broken YAML, or a problem its reader rejects.
        /// Otherwise a warning, e.g. an unknown key, which the app ignores.
        /// </summary>
        public bool IsError { get; init; }
    }

    /// <summary>
    /// A key: its description, value type and default, when known, and the <see cref="IYamlValueSource"/>
    /// of its known values, if it has one.
    /// </summary>
    public record YamlKeyInfo(string Key, string? Description, Type ValueType, object? DefaultValue, Type? ValueSource = null);

    /// <summary>
    /// Keys or values that fit where the caret is. They replace the line from <see cref="StartColumn"/>
    /// (1-based) to the caret. Keys come as <see cref="YamlKeyInfo"/>; values as plain text.
    /// </summary>
    public record YamlSuggestions(int StartColumn, IReadOnlyList<YamlKeyInfo> Keys, IReadOnlyList<YamlValue> Values);

    /// <summary>
    /// Validates YAML text against the C# type the app reads it into, e.g. character.yaml against
    /// <see cref="Classic.VoicebankConfig"/>. The JSON schema is built from YamlDotNet's view of the type,
    /// so its keys are exactly the ones <see cref="Yaml"/> reads, and the app's own reader runs too.
    /// </summary>
    public static class YamlValidator {
        static readonly ConcurrentDictionary<Type, JsonSchema> schemas = new ConcurrentDictionary<Type, JsonSchema>();

        public static List<YamlDiagnostic> Validate<T>(string text) => Validate(text, typeof(T));

        public static List<YamlDiagnostic> Validate(string text, Type type) {
            var diagnostics = new List<YamlDiagnostic>();
            var stream = new YamlStream();
            try {
                stream.Load(new StringReader(text));
            } catch (YamlException e) {
                diagnostics.Add(FromException(YamlDiagnosticKind.Syntax, e) with { IsError = true });
                return diagnostics;
            } catch (Exception e) {
                // YamlDotNet's scanner throws a bare Exception on some broken flow collections.
                var mark = LastParsedMark(text);
                diagnostics.Add(new YamlDiagnostic(YamlDiagnosticKind.Syntax, "", e.Message,
                    (int)mark.Line, (int)mark.Column, (int)mark.Line, (int)mark.Column + 1) { IsError = true });
                return diagnostics;
            }
            if (stream.Documents.Count == 0) {
                return diagnostics;
            }
            var positions = new Dictionary<string, NodePosition>();
            var json = ToJson(stream.Documents[0].RootNode, "", positions);
            var schema = schemas.GetOrAdd(type, t => SchemaFor(t, new HashSet<Type>()).Build());
            var results = schema.Evaluate(JsonSerializer.SerializeToElement(json), new EvaluationOptions {
                OutputFormat = OutputFormat.List,
            });
            foreach (var detail in results.Details) {
                if (detail.Errors == null) {
                    continue;
                }
                var path = detail.InstanceLocation.ToString();
                positions.TryGetValue(path, out var position);
                foreach (var (keyword, message) in detail.Errors) {
                    diagnostics.Add(FromSchemaError(path, keyword, message, position));
                }
            }
            // The app's reader is the ground truth. If it fails, the file does not load, so every problem
            // but unknown keys (which it ignores) is an error; report what it rejects that the schema missed.
            try {
                Yaml.DefaultDeserializer.Deserialize(text, type);
            } catch (Exception e) {
                diagnostics = diagnostics.Select(d => d with { IsError = d.Kind != YamlDiagnosticKind.UnknownKey }).ToList();
                var readerError = e is YamlException yamlException
                    ? FromException(YamlDiagnosticKind.Invalid, yamlException)
                    : new YamlDiagnostic(YamlDiagnosticKind.Invalid, "", e.Message, 1, 1, 1, 2);
                if (!diagnostics.Any(d => d.IsError && d.StartLine == readerError.StartLine)) {
                    diagnostics.Add(readerError with { IsError = true });
                }
            }
            return diagnostics
                .GroupBy(d => (d.Kind, d.Path, d.StartLine, d.StartColumn)).Select(g => g.First())
                .OrderBy(d => d.StartLine).ThenBy(d => d.StartColumn)
                .ToList();
        }

        // Where parsing stopped: the end of the last event read before the error.
        static Mark LastParsedMark(string text) {
            var mark = new Mark(0, 1, 1);
            try {
                var parser = new Parser(new StringReader(text));
                while (parser.MoveNext()) {
                    if (parser.Current != null) {
                        mark = parser.Current.End;
                    }
                }
            } catch (Exception) {
            }
            return mark;
        }

        static readonly Regex locationPrefix = new Regex(@"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\): ");

        static YamlDiagnostic FromException(YamlDiagnosticKind kind, YamlException e) {
            var message = locationPrefix.Replace(e.Message, "");
            if (e.InnerException != null && message.StartsWith("Exception during deserialization")) {
                message = e.InnerException.Message;
            }
            int endLine = (int)e.End.Line, endColumn = (int)e.End.Column;
            if (endLine < e.Start.Line || (endLine == e.Start.Line && endColumn <= e.Start.Column)) {
                (endLine, endColumn) = ((int)e.Start.Line, (int)e.Start.Column + 1);
            }
            return new YamlDiagnostic(kind, "", message, (int)e.Start.Line, (int)e.Start.Column, endLine, endColumn);
        }

        static readonly Regex typeMessage = new Regex("should be (.+)$");

        static YamlDiagnostic FromSchemaError(string path, string keyword, string message, NodePosition? position) {
            var kind = YamlDiagnosticKind.Invalid;
            var detail = message;
            (Mark start, Mark end) = position == null ? (Mark.Empty, Mark.Empty) : (position.ValueStart, position.ValueEnd);
            if (keyword == "" || keyword == "false") {
                // A false schema only comes from additionalProperties: false, on a key the type does not have.
                kind = YamlDiagnosticKind.UnknownKey;
                detail = UnescapePointer(path.Substring(path.LastIndexOf('/') + 1));
            } else if (keyword == "type") {
                kind = YamlDiagnosticKind.WrongType;
                // e.g. Value is "string" but should be ["number","null"]: every field allows null, so leave it out.
                var match = typeMessage.Match(message);
                if (match.Success) {
                    detail = string.Join(", ", Regex.Matches(match.Groups[1].Value, "[a-z]+")
                        .Select(m => m.Value).Where(t => t != "null"));
                }
            }
            // Point at the key for unknown keys and for values spanning lines, where users look.
            if (position != null && (kind == YamlDiagnosticKind.UnknownKey || start.Line != end.Line) && position.HasKey) {
                (start, end) = (position.KeyStart, position.KeyEnd);
            }
            if (start.Line == 0) {
                (start, end) = (new Mark(0, 1, 1), new Mark(0, 1, 2));
            }
            return new YamlDiagnostic(kind, path, detail, (int)start.Line, (int)start.Column, (int)end.Line, (int)end.Column);
        }

        class NodePosition {
            public bool HasKey;
            public Mark KeyStart, KeyEnd, ValueStart, ValueEnd;
        }

        static string EscapePointer(string s) => s.Replace("~", "~0").Replace("/", "~1");
        static string UnescapePointer(string s) => s.Replace("~1", "/").Replace("~0", "~");

        static JsonNode? ToJson(YamlNode node, string path, Dictionary<string, NodePosition> positions) {
            if (!positions.TryGetValue(path, out var position)) {
                positions[path] = position = new NodePosition();
            }
            (position.ValueStart, position.ValueEnd) = (node.Start, node.End);
            switch (node) {
                case YamlMappingNode map: {
                    var obj = new JsonObject();
                    foreach (var (key, value) in map.Children) {
                        var name = key is YamlScalarNode scalar ? scalar.Value ?? "" : key.ToString();
                        var childPath = path + "/" + EscapePointer(name);
                        positions[childPath] = new NodePosition { HasKey = true, KeyStart = key.Start, KeyEnd = key.End };
                        obj[name] = ToJson(value, childPath, positions);
                    }
                    return obj;
                }
                case YamlSequenceNode sequence: {
                    var array = new JsonArray();
                    int i = 0;
                    foreach (var item in sequence.Children) {
                        array.Add(ToJson(item, $"{path}/{i++}", positions));
                    }
                    return array;
                }
                case YamlScalarNode scalar:
                    return ScalarToJson(scalar);
            }
            return null;
        }

        static readonly Regex integerPattern = new Regex(@"^[-+]?[0-9]+$");
        static readonly Regex floatPattern = new Regex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$");

        // Plain scalars resolve by the YAML 1.2 core schema; quoted ones are always strings.
        // Only YAML's own number syntax counts: .NET parsing would also take "nan" (a pinyin syllable) or "1,000".
        static JsonNode? ScalarToJson(YamlScalarNode scalar) {
            var text = scalar.Value ?? "";
            if (scalar.Style != YamlDotNet.Core.ScalarStyle.Plain) {
                return JsonValue.Create(text);
            }
            if (text is "" or "~" or "null" or "Null" or "NULL") {
                return null;
            }
            if (text is "true" or "True" or "TRUE") {
                return JsonValue.Create(true);
            }
            if (text is "false" or "False" or "FALSE") {
                return JsonValue.Create(false);
            }
            if (integerPattern.IsMatch(text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) {
                return JsonValue.Create(l);
            }
            if (floatPattern.IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) {
                return JsonValue.Create(d);
            }
            return JsonValue.Create(text);
        }

        // The members YamlDotNet's deserializer sets: writable properties and fields.
        static readonly ITypeInspector typeInspector = new CompositeTypeInspector(
            new WritablePropertiesTypeInspector(new StaticTypeResolver()),
            new ReadableFieldsTypeInspector(new StaticTypeResolver()));

        static readonly SchemaValueType[] anyScalar = {
            SchemaValueType.String, SchemaValueType.Number, SchemaValueType.Boolean, SchemaValueType.Null,
        };

        // The keys of a type as the app reads them: YamlMember aliases, else underscored member names.
        // In declaration order, which files usually follow; the inspector lists properties before fields.
        internal static IEnumerable<(string name, IPropertyDescriptor property)> KeysOf(Type type) {
            int DeclarationOrder(IPropertyDescriptor property) =>
                type.GetMember(property.Name).Select(member => member.MetadataToken).DefaultIfEmpty(int.MaxValue).Min();
            foreach (var property in typeInspector.GetProperties(type, null).OrderBy(DeclarationOrder)) {
                if (property.GetCustomAttribute<YamlIgnoreAttribute>() != null) {
                    continue;
                }
                var alias = property.GetCustomAttribute<YamlMemberAttribute>()?.Alias;
                yield return (string.IsNullOrEmpty(alias) ? UnderscoredNamingConvention.Instance.Apply(property.Name) : alias, property);
            }
        }

        public static Type? DictionaryValueType(Type type) => type.GetInterfaces().Append(type)
            .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            ?.GetGenericArguments()[1];

        public static Type? ItemType(Type type) => type.IsArray ? type.GetElementType() : type == typeof(string) ? null
            : type.GetInterfaces().Append(type)
                .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

        /// <summary>
        /// The key at a position (1-based line and column), for help on hover: its description from a
        /// [Description] attribute, the type of its value, and its default. Null if there is no known key there.
        /// </summary>
        public static YamlKeyInfo? DescribeKeyAt(string text, Type type, int line, int column) {
            var stream = new YamlStream();
            try {
                stream.Load(new StringReader(text));
            } catch (Exception) {
                return null;
            }
            if (stream.Documents.Count == 0) {
                return null;
            }
            return FindKey(stream.Documents[0].RootNode, type, (line, column));
        }

        const string Placeholder = "__openutau_suggestion__";
        static readonly Regex keyLine = new Regex(@"^(?<indent>\s*(?:-\s+)*)(?<partial>[A-Za-z0-9_]*)$");
        static readonly Regex valueLine = new Regex(@"^(?<key>\s*(?:-\s+)*[A-Za-z0-9_]+):\s+(?<partial>[A-Za-z0-9_.\-]*)$");

        /// <summary>
        /// Suggestions for the caret (1-based line and column) at the end of a line: keys the section there
        /// takes and doesn't have yet, or the values of an enum, true/false or a key with [YamlValues]. Null if there are none,
        /// or if the rest of the file isn't valid YAML.
        /// </summary>
        public static YamlSuggestions? SuggestAt(string text, Type type, int line, int column) {
            var lines = text.Split('\n');
            if (line < 1 || line > lines.Length) {
                return null;
            }
            var lineText = lines[line - 1].TrimEnd('\r');
            if (column < 1 || column - 1 > lineText.Length || lineText.Substring(column - 1).Trim().Length > 0) {
                return null;
            }
            var before = lineText.Substring(0, column - 1);
            // Parses the file with the line being typed replaced by a placeholder, which is valid YAML.
            YamlNode? Parse(string replacement) {
                lines[line - 1] = replacement;
                var stream = new YamlStream();
                try {
                    stream.Load(new StringReader(string.Join("\n", lines)));
                } catch (Exception) {
                    return null;
                }
                return stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
            }
            var match = keyLine.Match(before);
            if (match.Success) {
                var indent = match.Groups["indent"].Value;
                var root = Parse(indent + Placeholder + ": ~");
                var section = root == null ? null : FindSection(root, type);
                if (section == null) {
                    return null;
                }
                var (sectionType, present) = section.Value;
                var keys = KeysOf(sectionType)
                    .Where(k => !present.Contains(k.name))
                    .Select(k => InfoOf(k.name, sectionType, k.property))
                    .ToList();
                return keys.Count == 0 ? null : new YamlSuggestions(indent.Length + 1, keys, Array.Empty<YamlValue>());
            }
            match = valueLine.Match(before);
            if (match.Success) {
                var key = match.Groups["key"].Value;
                var root = Parse(key + ": " + Placeholder);
                if (root == null) {
                    return null;
                }
                var info = FindKey(root, type, (line, key.Length - key.TrimStart(' ', '-').Length + 1));
                if (info == null) {
                    return null;
                }
                var values = ValuesOf(info);
                return values.Count == 0 ? null
                    : new YamlSuggestions(column - match.Groups["partial"].Length, Array.Empty<YamlKeyInfo>(), values);
            }
            return null;
        }

        static YamlKeyInfo InfoOf(string name, Type owner, IPropertyDescriptor member) =>
            new YamlKeyInfo(name, member.GetCustomAttribute<DescriptionAttribute>()?.Description,
                member.Type, DefaultOf(owner, member), member.GetCustomAttribute<YamlValuesAttribute>()?.Source);

        // Known values from a [YamlValues] source, else the choices of true/false and enum keys.
        static IReadOnlyList<YamlValue> ValuesOf(YamlKeyInfo key) {
            if (key.ValueSource != null) {
                try {
                    return Activator.CreateInstance(key.ValueSource) is IYamlValueSource source
                        ? source.GetValues().ToList() : Array.Empty<YamlValue>();
                } catch (Exception) {
                    return Array.Empty<YamlValue>();
                }
            }
            var type = Nullable.GetUnderlyingType(key.ValueType) ?? key.ValueType;
            return type == typeof(bool) ? new[] { new YamlValue("true"), new YamlValue("false") }
                : type.IsEnum ? Enum.GetNames(type).Select(name => new YamlValue(name)).ToList()
                : Array.Empty<YamlValue>();
        }

        // The type of the section holding the placeholder key, and the keys it already has.
        static (Type type, HashSet<string> keys)? FindSection(YamlNode node, Type type) {
            type = Nullable.GetUnderlyingType(type) ?? type;
            switch (node) {
                case YamlMappingNode map:
                    var names = map.Children.Keys.Select(k => k is YamlScalarNode scalar ? scalar.Value ?? "" : k.ToString()).ToList();
                    if (names.Contains(Placeholder)) {
                        return DictionaryValueType(type) == null ? (type, names.ToHashSet()) : null;
                    }
                    var members = DictionaryValueType(type) == null ? KeysOf(type).ToDictionary(k => k.name, k => k.property) : null;
                    foreach (var (key, value) in map.Children) {
                        var name = key is YamlScalarNode scalar ? scalar.Value ?? "" : key.ToString();
                        IPropertyDescriptor? member = null;
                        members?.TryGetValue(name, out member);
                        var valueType = member?.Type ?? DictionaryValueType(type);
                        var found = valueType == null ? null : FindSection(value, valueType);
                        if (found != null) {
                            return found;
                        }
                    }
                    return null;
                case YamlSequenceNode sequence:
                    var itemType = ItemType(type);
                    return itemType == null ? null : sequence.Children
                        .Select(item => FindSection(item, itemType)).FirstOrDefault(found => found != null);
            }
            return null;
        }

        // Only scalars have a full range: YamlDotNet ends a mapping or sequence at its first token.
        static bool Contains(YamlNode node, (int line, int column) at) =>
            (node.Start.Line, node.Start.Column).CompareTo(at) <= 0 && at.CompareTo((node.End.Line, node.End.Column)) < 0;

        // Checks every key, since sections can't be ruled out by their range; the files are small.
        static YamlKeyInfo? FindKey(YamlNode node, Type type, (int line, int column) at) {
            type = Nullable.GetUnderlyingType(type) ?? type;
            switch (node) {
                case YamlMappingNode map:
                    var members = DictionaryValueType(type) == null ? KeysOf(type).ToDictionary(k => k.name, k => k.property) : null;
                    foreach (var (key, value) in map.Children) {
                        var name = key is YamlScalarNode scalar ? scalar.Value ?? "" : key.ToString();
                        IPropertyDescriptor? member = null;
                        members?.TryGetValue(name, out member);
                        var valueType = member?.Type ?? DictionaryValueType(type);
                        if (valueType == null) {
                            continue;
                        }
                        if (Contains(key, at)) {
                            return member == null ? new YamlKeyInfo(name, null, valueType, null) : InfoOf(name, type, member);
                        }
                        var found = FindKey(value, valueType, at);
                        if (found != null) {
                            return found;
                        }
                    }
                    return null;
                case YamlSequenceNode sequence:
                    var itemType = ItemType(type);
                    return itemType == null ? null : sequence.Children
                        .Select(item => FindKey(item, itemType, at)).FirstOrDefault(found => found != null);
            }
            return null;
        }

        // A member's value in a new instance, e.g. 0.67 for portrait_opacity.
        static object? DefaultOf(Type type, IPropertyDescriptor member) {
            try {
                var instance = Activator.CreateInstance(type);
                return instance == null ? null : member.Read(instance).Value;
            } catch (Exception) {
                return null;
            }
        }

        static JsonSchemaBuilder SchemaFor(Type type, HashSet<Type> visiting) {
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) {
                return SchemaFor(nullable, visiting);
            }
            // Any scalar reads as a string, e.g. "version: 1.0".
            if (type == typeof(string)) {
                return new JsonSchemaBuilder().Type(anyScalar);
            }
            if (type == typeof(bool)) {
                return new JsonSchemaBuilder().Type(SchemaValueType.Boolean, SchemaValueType.Null);
            }
            if (type.IsEnum) {
                return new JsonSchemaBuilder().Enum(Enum.GetNames(type).Select(n => (JsonNode?)JsonValue.Create(n)));
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) {
                return new JsonSchemaBuilder().Type(SchemaValueType.Number, SchemaValueType.Null);
            }
            if (type.IsPrimitive) {
                return new JsonSchemaBuilder().Type(SchemaValueType.Integer, SchemaValueType.Null);
            }
            var valueType = DictionaryValueType(type);
            if (valueType != null) {
                return new JsonSchemaBuilder()
                    .Type(SchemaValueType.Object, SchemaValueType.Null)
                    .AdditionalProperties(SchemaFor(valueType, visiting));
            }
            var itemType = ItemType(type);
            if (itemType != null) {
                return new JsonSchemaBuilder()
                    .Type(SchemaValueType.Array, SchemaValueType.Null)
                    .Items(SchemaFor(itemType, visiting));
            }
            if (type == typeof(object) || typeof(IEnumerable).IsAssignableFrom(type) || !visiting.Add(type)) {
                return new JsonSchemaBuilder();
            }
            var properties = new Dictionary<string, JsonSchemaBuilder>();
            foreach (var (name, property) in KeysOf(type)) {
                properties[name] = SchemaFor(property.Type, visiting);
            }
            visiting.Remove(type);
            return new JsonSchemaBuilder()
                .Type(SchemaValueType.Object, SchemaValueType.Null)
                .Properties(properties)
                .AdditionalProperties(false);
        }
    }
}
