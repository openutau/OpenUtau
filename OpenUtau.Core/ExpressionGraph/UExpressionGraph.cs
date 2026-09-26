using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.ExpressionGraph {
    /// <summary>
    /// A node graph that derives renderer expressions from the curves the user edits.
    /// Stored in the project library; a track uses the default graph of its renderer or its own override.
    /// </summary>
    public class UExpressionGraph {
        public string id = string.Empty;
        public string? name;
        /// <summary>The renderer this graph targets, as in <see cref="Ustx.URenderSettings.renderer"/>.</summary>
        public string? renderer;
        /// <summary>
        /// Where pitch is drawn in the piano roll and where Load rendered pitch writes: PITD (the default) or the
        /// pitch override, PITO.
        /// </summary>
        public string? preferredPitchCurve;
        public List<UGraphNode> nodes = new List<UGraphNode>();
        public List<UGraphLink> links = new List<UGraphLink>();

        public UExpressionGraph Clone() {
            return new UExpressionGraph {
                id = id,
                name = name,
                renderer = renderer,
                preferredPitchCurve = preferredPitchCurve,
                nodes = nodes.Select(n => n.Clone()).ToList(),
                links = links.Select(l => l.Clone()).ToList(),
            };
        }
    }

    /// <summary>
    /// A node, stored as one flat mapping: id, type, the type's parameters, then its canvas position.
    /// Parameters are kept as text, so nodes of types this version doesn't know survive a save.
    /// </summary>
    public class UGraphNode {
        public int id;
        public string? type;
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
        public float x;
        public float y;

        public UGraphNode Clone() {
            return new UGraphNode {
                id = id,
                type = type,
                parameters = new Dictionary<string, string>(parameters),
                x = x,
                y = y,
            };
        }

        public string? GetString(string key) => parameters.TryGetValue(key, out var value) ? value : null;

        public bool TryGetFloat(string key, out float value) {
            value = 0;
            return parameters.TryGetValue(key, out var text)
                && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public void Set(string key, string value) => parameters[key] = value;
        public void Set(string key, float value) => parameters[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A link from a node's output to a named input port of another node.</summary>
    public class UGraphLink {
        public int from;
        /// <summary>The output port of <see cref="from"/>; null for its only output.</summary>
        public string? fromPort;
        public int to;
        public string? toPort;

        public UGraphLink Clone() => new UGraphLink { from = from, fromPort = fromPort, to = to, toPort = toPort };
    }

    /// <summary>Reads and writes <see cref="UGraphNode"/> as one flat flow mapping.</summary>
    public class UGraphNodeYamlConverter : IYamlTypeConverter {
        const string Id = "id";
        const string Type = "type";
        const string X = "x";
        const string Y = "y";

        public bool Accepts(Type type) => type == typeof(UGraphNode);

        public object ReadYaml(IParser parser, Type type) {
            var node = new UGraphNode();
            parser.Consume<MappingStart>();
            while (!parser.TryConsume<MappingEnd>(out _)) {
                var key = parser.Consume<Scalar>().Value;
                if (!parser.TryConsume<Scalar>(out var scalar)) {
                    // Not a scalar value: skip it rather than fail the whole document.
                    parser.SkipThisAndNestedEvents();
                    continue;
                }
                var value = scalar.Value;
                switch (key) {
                    case Id:
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out node.id);
                        break;
                    case Type:
                        node.type = value;
                        break;
                    case X:
                        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out node.x);
                        break;
                    case Y:
                        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out node.y);
                        break;
                    default:
                        node.parameters[key] = value;
                        break;
                }
            }
            return node;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type) {
            var node = (UGraphNode)value!;
            emitter.Emit(new MappingStart(null, null, true, MappingStyle.Flow));
            Emit(emitter, Id, node.id.ToString(CultureInfo.InvariantCulture));
            Emit(emitter, Type, node.type ?? string.Empty);
            foreach (var kv in node.parameters) {
                if (kv.Key is Id or Type or X or Y) {
                    continue;
                }
                Emit(emitter, kv.Key, kv.Value ?? string.Empty);
            }
            Emit(emitter, X, node.x.ToString(CultureInfo.InvariantCulture));
            Emit(emitter, Y, node.y.ToString(CultureInfo.InvariantCulture));
            emitter.Emit(new MappingEnd());
        }

        static void Emit(IEmitter emitter, string key, string value) {
            emitter.Emit(new Scalar(null, null, key, PlainOrQuoted(key), true, false));
            emitter.Emit(new Scalar(null, null, value, PlainOrQuoted(value), true, false));
        }

        // This converter reads every value back as text, so any plain scalar round-trips.
        // Plain only when it can't start a YAML indicator: numbers and identifiers.
        static ScalarStyle PlainOrQuoted(string text) {
            bool plain = text.Length > 0
                && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '+' or '-')
                && (char.IsAsciiLetterOrDigit(text[0]) || text.Length > 1 && char.IsAsciiLetterOrDigit(text[1]));
            return plain ? ScalarStyle.Plain : ScalarStyle.DoubleQuoted;
        }
    }
}
