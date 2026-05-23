using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// The English Arpasing Phonemizer.
    /// <para>
    /// Arpasing is a system that uses CMUdict as dictionary to convert English words to phoneme symbols.
    /// See http://www.speech.cs.cmu.edu/cgi-bin/cmudict and https://arpasing.neocities.org/en/faq.html.
    /// </para>
    /// </summary>
    [Phonemizer("English Arpasing Phonemizer", "EN ARPA", language: "UTAU")]
    public class ArpasingPhonemizer : LatinDiphonePhonemizer {
        public ArpasingPhonemizer() {
            try {
                Initialize();
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize.");
            }
        }

        protected override IG2p LoadG2p() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, "arpasing.yaml");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllBytes(path, Data.Resources.arpasing_template);
            }
            g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "arpasing.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }

            // Load base g2p.
            g2ps.Add(new ArpabetG2p());

            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override Dictionary<string, string[]> LoadVowelFallbacks() {
            // Vowel fallback: เมื่อ voicebank ไม่มี phoneme ที่ต้องการ ให้ลองใช้ phoneme ที่ใกล้เคียง
            return (
                // Stressed vowels
                "aa=ah,ae,ao;" +
                "ae=ah,aa,eh;" +
                "ah=aa,ae,uh;" +
                "ao=ow,aa;" +
                "aw=ao,ah;" +
                "ax=ah,uh;" +   // reduced schwa
                "ay=ah,ey;" +
                // Mid vowels
                "eh=ae,ey,ah;" +
                "er=ah,uh;" +   // r-colored schwa
                "ey=eh,ay;" +
                // High vowels
                "ih=iy,ah;" +
                "iy=ih,ey;" +
                "uh=uw,ah;" +
                "uw=uh,ow;" +
                // Diphthongs
                "ow=ao,uw;" +
                "oy=ow,aw"
            ).Split(';')
             .Select(entry => entry.Split('='))
             .ToDictionary(parts => parts[0], parts => parts[1].Split(','));
        }
    }
}
