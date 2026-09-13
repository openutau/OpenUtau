using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace OpenUtau.Api {
    public class PhonemizerFactory {
        public Type type;
        public string name;
        public string tag;
        public string author;
        public string language;

        public Phonemizer Create() {
            var phonemizer = Activator.CreateInstance(type) as Phonemizer;
            phonemizer.Name = name;
            phonemizer.Tag = tag;
            phonemizer.Language = language;
            return phonemizer;
        }

        public override string ToString() => string.IsNullOrEmpty(author)
            ? $"[{tag}] {name}"
            : $"[{tag}] {name} (Contributed by {author})";

        private static readonly ConcurrentDictionary<Type, PhonemizerFactory> factories = new();
        private static PhonemizerFactory[] orderedFactories = [];
        public static PhonemizerFactory Get(Type type) {
            if (factories.TryGetValue(type, out var factory)) {
                return factory;
            }
            var attr = type.GetCustomAttribute<PhonemizerAttribute>();
            if (attr == null || string.IsNullOrEmpty(attr.Name) || string.IsNullOrEmpty(attr.Tag)) {
                return null;
            }
            factory = new PhonemizerFactory() {
                type = type,
                name = attr.Name,
                tag = attr.Tag,
                author = attr.Author,
                language = attr.Language,
            };
            return factories.GetOrAdd(type, factory);
        }

        public static PhonemizerFactory? Get(string typeFullName) {
            foreach (var factory in factories.Values) {
                if (factory.type.FullName == typeFullName) {
                    return factory;
                }
            }
            return null;
        }

        public static void BuildList() {
            orderedFactories = factories.Values.OrderBy(f => f.tag).ToArray();
        }

        public static PhonemizerFactory[] GetAll() => orderedFactories;
    }
}
