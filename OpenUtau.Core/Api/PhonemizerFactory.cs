using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenUtau.Core;

namespace OpenUtau.Api {
    public class PhonemizerFactory {
        public Type type;
        public string name;
        public string tag;
        public string author;
        public string language;

        /// <summary>
        /// Identifies the phonemizer in projects, singer configs and preferences.
        /// Must equal <see cref="Phonemizer.Id"/> of the phonemizers it creates.
        /// </summary>
        public virtual string Id => type.FullName!;

        public virtual Phonemizer Create() {
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
        private static readonly ConcurrentDictionary<string, PhonemizerFactory> registeredFactories = new();
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

        public static PhonemizerFactory? Get(string id) {
            if (id == null) {
                return null;
            }
            foreach (var factory in factories.Values) {
                if (factory.Id == id) {
                    return factory;
                }
            }
            return registeredFactories.TryGetValue(id, out var registered) ? registered : null;
        }

        /// <summary>
        /// Adds a factory that is not a phonemizer type, such as a script phonemizer from a package.
        /// Call <see cref="BuildList"/> afterwards.
        /// </summary>
        public static void Register(PhonemizerFactory factory) {
            registeredFactories[factory.Id] = factory;
        }

        /// <summary>
        /// Removes a factory added with <see cref="Register"/>. Call <see cref="BuildList"/> afterwards.
        /// </summary>
        public static void Unregister(string id) {
            registeredFactories.TryRemove(id, out _);
        }

        public static void BuildList() {
            orderedFactories = factories.Values.Concat(registeredFactories.Values).OrderBy(f => f.tag).ToArray();
        }

        public static PhonemizerFactory[] GetAll() => orderedFactories;
    }

    /// <summary>The installed phonemizers, by the id a default_phonemizer key takes.</summary>
    public class PhonemizerTypeValues : IYamlValueSource {
        public IEnumerable<YamlValue> GetValues() => (PhonemizerFactory.GetAll() ?? Array.Empty<PhonemizerFactory>())
            .Select(factory => new YamlValue(factory.Id, factory.ToString()));
    }
}
