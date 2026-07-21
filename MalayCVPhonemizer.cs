#pragma warning disable CS0618, CS0649, CS8632, CS0108
using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Malay Syllable Phonemizer", "MS CV", "DELTA SYNTH", language: "MS")]
    // Version: v
    public class MalayCVPhonemizer : SyllableBasedPhonemizer {

        // Vowels for Malay / Indonesian
        private readonly string[] vowels = "a,e,i,o,u".Split(',');
        private readonly string[] consonants = "b,c,d,f,g,h,j,k,l,m,n,p,q,r,s,t,v,w,x,y,z,ng,ny,sy,kh".Split(',');
        private readonly Dictionary<string, string> dictionaryReplacements = ("a=a;e=e;i=i;o=o;u=u").Split(';').ToDictionary(entry => entry.Split('=')[0], entry => entry.Split('=')[1]);

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_ms.txt";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;

        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;

            var phonemes = new List<string>();
            var lastC = cc.Length - 1;
            var firstC = 0;

            if (syllable.IsStartingV) {
                phonemes.Add($"- {v}");
            } else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    phonemes.Add($"{prevV} {v}");
                } else {
                    phonemes.Add(v);
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                phonemes.Add($"- {cc[0]}{v}");
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                phonemes.Add($"- {cc[0]}");
                for (int i = 0; i < cc.Length - 1; i++) {
                    phonemes.Add($"{cc[i]} {cc[i + 1]}");
                }
                phonemes.Add($"{cc[lastC]}{v}");
            } else {
                phonemes.Add($"{cc[lastC]}{v}");
            }
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc;
            string v = ending.prevV;
            var phonemes = new List<string>();

            if (ending.IsEndingV) {
                phonemes.Add($"{v} -");
            } else if (ending.IsEndingVCWithOneConsonant) {
                phonemes.Add($"{v} {cc[0]}");
                phonemes.Add($"{cc[0]} -");
            } else {
                phonemes.Add($"{v} {cc[0]}");
                for (int i = 0; i < cc.Length - 1; i++) {
                    phonemes.Add($"{cc[i]} {cc[i + 1]}");
                }
                phonemes.Add($"{cc[cc.Length - 1]} -");
            }
            return phonemes;
        }
    }
}
