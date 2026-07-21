#pragma warning disable CS0618, CS0649, CS8632, CS0108
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Simple German diphone phonemizer. Works similar to Arpasing.
    /// You could make a combined English-German diphonic bank, and it will work.
    /// Based on the German CMUSphinx dictionary, with some adjustments: https://sourceforge.net/projects/cmusphinx/files/Acoustic%20and%20Language%20Models/German/
    /// </summary>
    [Phonemizer("German Diphone Phonemizer", "DE DIPHONE", "Lotte V", language: "UTAU")]
    // Version: v
    public class GermanDiphonePhonemizer : LatinDiphonePhonemizer {

        public GermanDiphonePhonemizer() {
            try {
                Initialize();
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize.");
            }
        }

        protected override IG2p LoadG2p() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, "german.yaml");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.german_template);
            }
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "german.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }

            // Load base g2p.
            g2ps.Add(new GermanG2p());

            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override Dictionary<string, string[]> LoadVowelFallbacks() {
            return "aa=ex;ex=aa;ah=ax,aa;ae=eh;eh=ae,ee;ee=eh;ao=ooh;ooh=ao;er=ex,ax;ih=iy;iy=ih;uh=uw;uw=uh;yy=ue;ue=yy;ohh=oe;oe=ohh;ax=eh;cc=x;x=cc;dh=z;jh=ch;r=rr;th=s;w=v;zh=sh".Split(';')
                .Select(entry => entry.Split('='))
                .ToDictionary(parts => parts[0], parts => parts[1].Split(','));
        }
    }
}
