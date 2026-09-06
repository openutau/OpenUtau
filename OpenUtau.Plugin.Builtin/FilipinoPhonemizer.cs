using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using Classic;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;
using YamlDotNet.Core.Tokens;
using System.Text.RegularExpressions;
using OpenUtau.Core;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Filipino Phonemizer", "FIL VCV & CVVC", "Cadlaxa", language: "FIL")]
    public class FilipinoPhonemizer : ArpasingPlusPhonemizer {
        protected override string YamlFileName => "filipino.yaml";
        protected override byte[] YamlTemplate => Data.Resources.filipino_template;
        public FilipinoPhonemizer() {
            this.vowels = new string[] {
                "a", "e", "i", "o", "u", "ay", "ey", "oy", "uy", "aw", "ew", "ow", "iw"
            };
            this.consonants = Array.Empty<string>();
            this.diphthongTails = new Dictionary<string, string>() {
                { "ay", "y" },
                { "ey", "y" },
                { "oy", "y" },
                { "uy", "y" },
                { "aw", "w" },
                { "ew", "w" },
                { "ow", "w" },
                { "iw", "w" },
            };
        }
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "";
        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                return note.phoneticHint.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);
            }

            if (original == null || original.Length == 0) {
                string lyric = note.lyric.ToLowerInvariant();
                List<string> fallbackSplit = new List<string>();
                string[] vowels = GetVowels();
                string[] consonants = GetConsonants();

                // Handle apostrophes at the start or end
                bool hasLeadingApostrophe = lyric.StartsWith("'");
                bool hasTrailingApostrophe = lyric.EndsWith("'");

                if (hasLeadingApostrophe) {
                    lyric = lyric.Substring(1);
                }
                if (hasTrailingApostrophe && lyric.Length > 0) {
                    lyric = lyric.Substring(0, lyric.Length - 1);
                }

                int ii = 0;
                while (ii < lyric.Length) {
                    string match = null;
                    foreach (var cons in consonants.OrderByDescending(c => c.Length)) {
                        if (lyric.Substring(ii).StartsWith(cons)) {
                            match = cons;
                            break;
                        }
                    }
                    if (match == null) {
                        foreach (var vow in vowels.OrderByDescending(v => v.Length)) {
                            if (lyric.Substring(ii).StartsWith(vow)) {
                                match = vow;
                                break;
                            }
                        }
                    }
                    if (match != null) {
                        fallbackSplit.Add(match);
                        ii += match.Length;
                    } else {
                        fallbackSplit.Add(lyric[ii].ToString());
                        ii++;
                    }
                }
                // Add "q" at the beginning or end if needed
                if (hasLeadingApostrophe) {
                    fallbackSplit.Insert(0, "q");
                }
                if (hasTrailingApostrophe) {
                    fallbackSplit.Add("q");
                    
                }
                original = fallbackSplit.ToArray();
            }
            
            List<string> finalProcessedPhonemes = new List<string>();
            
            foreach (string s in original) {
                switch (s) {
                    default:
                        finalProcessedPhonemes.Add(s);
                        break;
                }
            }
            return finalProcessedPhonemes.ToArray();
        }
        protected override IG2p[] GetBaseG2ps() => Array.Empty<IG2p>();

        // Endings has 50 ticks gap
        protected override bool NoGap => true;
        protected override string ValidateAlias(string alias, int tone = 0) {

            // VALIDATE ALIAS DEPENDING ON METHOD
            if (HasOto(alias, tone)) return alias;

            string baseResolved = base.ValidateAlias(alias, tone);
            if (!string.IsNullOrEmpty(baseResolved) && baseResolved != alias) {
                if (HasOto(baseResolved, tone)) {
                    return baseResolved;
                }
                alias = baseResolved;
            }
            return alias;
        }
    }
}