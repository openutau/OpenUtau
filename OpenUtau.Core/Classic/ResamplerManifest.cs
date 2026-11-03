using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    public class ResamplerManifest {
        public Dictionary<string, UExpressionDescriptor> expressions = new Dictionary<string, UExpressionDescriptor> { };
        public bool expressionFilter = false;
        public string[] files = Array.Empty<string>();

        public ResamplerManifest() { }

        public static ResamplerManifest Load(string path) {
            var manifest = Yaml.DefaultDeserializer.Deserialize<ResamplerManifest>(
                File.ReadAllText(path, encoding: Encoding.UTF8)
                );
            manifest.expressions = manifest.expressions
                                .GroupBy(kvp => kvp.Key.ToLower())
                                .ToDictionary(
                                    group => group.Key,
                                    group => group.First().Value
                                );
            return manifest;
        }
    }
}
