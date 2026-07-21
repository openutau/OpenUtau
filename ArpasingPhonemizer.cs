#pragma warning disable CS0618, CS0649, CS8632, CS0108
// ==========================================
// Made And Checked By DELTA SYNTH & Gemini AI
// Original by OpenUtau (stakira)
// Version: v.1.1
// History/Summary:
// v.1.1 (Stability/Balance fix - แก้บัค ปรับสมดุลการทำงาน):
//   ลดพื้นที่เวลา (timing) ที่กลุ่มพยัญชนะ CC, CCV, R-C และ C_C ใช้ไปในแต่ละพยางค์
//   ให้แคบลงจนเหลือพื้นที่รวมไม่เกินประมาณ 3% ของความยาวโน้ต แทนที่จะกินพื้นที่มากเกินไปจนเสียง
//   ล่วงหน้า (transition) ยืดเยื้อ ทำให้จังหวะร้องดูอืดและไม่เป็นธรรมชาติ
//   Narrowed the timing window reserved for consonant-cluster groups (CC, CCV, R-C, C_C) down to
//   roughly 3% of note length overall, so consonant transitions no longer eat too much of the note
//   and singing timing feels tighter and more natural.
//   หมายเหตุ: ค่านี้ override เมธอดฐาน GetTransitionBasicLengthMs ของ LatinDiphonePhonemizer
//   ซึ่งเป็นตัวควบคุมความยาวพยัญชนะทรานสิชันของทุกกลุ่ม (CC/CCV/R-C/C_C) หากคอมไพล์ไม่ผ่าน
//   กรุณาตรวจสอบ signature ของเมธอดนี้ใน LatinDiphonePhonemizer.cs ของ OpenUtau เวอร์ชันที่ใช้งานอยู่
//   แล้วปรับชื่อ/พารามิเตอร์ให้ตรงกัน
//   NOTE: this overrides the LatinDiphonePhonemizer base-class hook GetTransitionBasicLengthMs,
//   which controls consonant-transition length for all cluster groups (CC/CCV/R-C/C_C). If this
//   does not compile, please check the exact method signature in your installed OpenUtau's
//   LatinDiphonePhonemizer.cs and adjust the override to match.
// ==========================================
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
                ConsonantLength = 21; // v.1.1: Scaled down to ~35% to narrow consonant transitions.
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
            return "aa=ah,ae;ae=ah,aa;ah=aa,ae;ao=ow;ow=ao;eh=ae;ih=iy;iy=ih;uh=uw;uw=uh;aw=ao".Split(';')
                .Select(entry => entry.Split('='))
                .ToDictionary(parts => parts[0], parts => parts[1].Split(','));
        }

        protected override string GetPhonemeOrFallback(string prevSymbol, string symbol, int tone, string color, string alt) {
            if (g2p != null && g2p.IsVowel(prevSymbol) && g2p.IsVowel(symbol)) {
                prevSymbol = "-";
            }
            if (!string.IsNullOrEmpty(alt) && singer.TryGetMappedOto($"{prevSymbol} {symbol}{alt}", tone, color, out var oto)) {
                return oto.Alias;
            }
            if (singer.TryGetMappedOto($"{prevSymbol} {symbol}", tone, color, out var oto1)) {
                return oto1.Alias;
            }
            if (vowelFallback.TryGetValue(symbol, out string[] fallbacks)) {
                foreach (var fallback in fallbacks) {
                    if (singer.TryGetMappedOto($"{prevSymbol} {fallback}", tone, color, out var oto2)) {
                        return oto2.Alias;
                    }
                }
            }
            // Only use the "- symbol" fallback if we are actually at the start of a phrase (prevSymbol == "-")
            if (prevSymbol == "-" && singer.TryGetMappedOto($"- {symbol}", tone, color, out var oto3)) {
                return oto3.Alias;
            }
            return $"{prevSymbol} {symbol}{alt}";
        }

    }
}
