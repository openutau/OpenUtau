using System;
using System.Collections.Generic;
using System.IO;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// A French diphone phonemizer that uses CMUSphinx dictionary.
    /// </summary>
    [Phonemizer("French CMUSphinx Phonemizer", "FR SPHINX", language: "UTAU")]
    public class FrenchCMUSphinxPhonemizer : LatinDiphonePhonemizer {
        public FrenchCMUSphinxPhonemizer() {
            try {
                Initialize();
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize.");
            }
        }

        protected override IG2p LoadG2p() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, "french.yaml");
            if (File.Exists(path)) {
                try {
                    g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load {path}");
                }
            }

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "french.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }

            // Load base g2p.
            g2ps.Add(new FrenchG2p());

            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override Dictionary<string, string[]> LoadVowelFallbacks() {
            return new Dictionary<string, string[]>();
        }
    }
}
