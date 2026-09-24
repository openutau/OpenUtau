using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Enunu Onnx English Phonemizer", "ENUNU X EN", language:"EN", engine: "Enunu")]
    public class EnunuOnnxEnglishPhonemizer : EnunuOnnxPhonemizer {
        protected override IG2p LoadG2p(string rootPath) {
            var g2ps = new List<IG2p>();

            // Load dictionary from singer folder.
            string file = Path.Combine(rootPath, "enunux.yaml");
            if (File.Exists(file)) {
                try {
                    g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load {file}");
                }
            }
            g2ps.Add(new ArpabetG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }

    }
}
