using System;
using System.Collections.Generic;

namespace OpenUtau.Core {
    /// <summary>
    /// Known values of a free-text YAML key, suggested while editing, e.g. the installed phonemizers.
    /// <paramref name="source"/> is an <see cref="IYamlValueSource"/> with a parameterless constructor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class YamlValuesAttribute : Attribute {
        public YamlValuesAttribute(Type source) {
            Source = source;
        }

        public Type Source { get; }
    }

    public interface IYamlValueSource {
        IEnumerable<YamlValue> GetValues();
    }

    /// <summary>A suggested value, with an optional description shown beside it.</summary>
    public record YamlValue(string Text, string? Description = null);
}
