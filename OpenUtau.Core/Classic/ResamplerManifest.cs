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
        public RendererManifest renderer;
        public AnalysisManifest analysis;

        public ResamplerManifest() { }

        public static ResamplerManifest Load(string path) {
            var manifest = Yaml.DefaultDeserializer.Deserialize<ResamplerManifest>(
                File.ReadAllText(path, encoding: Encoding.UTF8)
                );
            manifest.expressions ??= new Dictionary<string, UExpressionDescriptor>();
            manifest.expressions = manifest.expressions
                                .GroupBy(kvp => kvp.Key.ToLower())
                                .ToDictionary(
                                    group => group.Key,
                                    group => group.First().Value
                                );
            return manifest;
        }
    }

    public class RendererManifest {
        public bool enabled = false;
        public string id;
        public string name;
        public RendererBridgeManifest bridge;
        public RendererCapabilitiesManifest capabilities;
    }

    public class RendererBridgeManifest {
        public string assembly;
        public string type;
        public int apiVersion = 1;
    }

    public class RendererCapabilitiesManifest {
        // These must match the corresponding runtime IRenderer properties.
        public bool renderedPitch = false;
        public bool realCurves = false;
        // True means Render cooperatively observes its CancellationTokenSource.
        public bool cancellation = false;
        // Maximum simultaneous Render calls. Zero leaves scheduling to the host.
        public int parallelism = 0;
    }

    public class AnalysisManifest {
        public Dictionary<string, AnalysisFormatManifest> formats = new Dictionary<string, AnalysisFormatManifest>();
    }

    public class AnalysisFormatManifest {
        public string name;
        public string path;
        public bool required = false;
        public bool canGenerate = false;
        public bool shared = false;
    }
}
