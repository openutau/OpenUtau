using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Brazilian Portuguese CVC Phonemizer", "PT-BR CVC", "HAI-D", language:"PT")]
    public class BrazilianPortugueseCVCPhonemizer : SyllableBasedPhonemizer {

        /// <summary>
        /// Brazilian Portuguese CVC Phonemizer by HAI-D
        /// Connotation: BRAPA
        /// Main Accent: Neutral
        /// Supported Alias: -C, -V, C-, V-, -CV, VC-, CV, VC, V, VV, _V
        /// </summary>

        private readonly string[] vowels = "a,oa,ah,ahn,ax,an,e,en,eh,ehn,ae,aen,i,in,i0,o,on,oh,ohn,u,un,u0".Split(",");
        private readonly string[] consonants = "b,bv,ch,d,dj,f,g,gv,h,hr,k,l,l0,lh,m,n,ng,nh,p,r,rr,rw,s,sh,t,v,w,x,y,z,rh".Split(",");
        private readonly string[] burstConsonants = "b,ch,d,dj,g,k,p,t".Split(",");
        private readonly Dictionary<string, string> dictionaryReplacements = ("a=a;e=e;i=i;o=o;u=u;E=eh;O=oh;a~=an;e~=en;i~=in;o~=on;u~=un;" +
                "b=b;tS=ch;d=d;dZ=dj;f=f;g=g;h=h;X=h;R=h;Z=j;k=k;l=l;L=lh;m=m;n=n;J=nh;p=p;r=r;s=s;S=sh;t=t;v=v;w=w;w~=w;j=y;j~=y;z=z").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private string[] shortConsonants = "r".Split(",");
        private string[] longConsonants = "s,sh".Split(",");

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_ptbr.txt";
        protected override IG2p LoadBaseDictionary() => new PortugueseG2p();

        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;

        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;

            string basePhoneme;
            var phonemes = new List<string>();
            if (syllable.IsStartingV) {
                basePhoneme = $"- {v}";
            }
            else if (syllable.IsVV) {  // if VV
                if (!CanMakeAliasExtension(syllable)) {
                    //try VV
                    basePhoneme = $"{prevV} {v}";

                    //if no VV -> _V
                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                        basePhoneme = $"_{v}";

                        //if no _V -> V
                        if (!HasOto(basePhoneme, syllable.vowelTone)) {
                            basePhoneme = v;
                        }
                    }
                } else {
                    // the previous alias will be extended
                    basePhoneme = null;
                }
            }
            else if (syllable.IsStartingCV) {
                basePhoneme = $"-{cc.Last()} {v}";
                for (var i = 0; i < cc.Length - 1; i++) {
                    phonemes.Add($"- {cc[i]}");
                }
            }
            else { // VCV
                if (cc.Length == 1 || IsShort(syllable) || cc.Last() == "`") {
                    basePhoneme = $"{cc.Last()} {v}";
                } else {
                    basePhoneme = $"-{cc.Last()} {v}";
                }
                phonemes.Add($"{prevV} {cc[0]}"); ;
                var offset = burstConsonants.Contains(cc[0]) ? 0 : 1;
                for (var i = offset; i < cc.Length - 1; i++) {
                    var cr = $"{cc[i]} -";
                    phonemes.Add(HasOto(cr, syllable.tone) ? cr : cc[i]);
                }
            }
            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc;
            string v = ending.prevV;

            var phonemes = new List<string>();
            if (ending.IsEndingV) {
                phonemes.Add($"{v} -");
            }
            else {
                phonemes.Add($"{v} {cc[0]}-");
                for (var i = 1; i < cc.Length; i++) {
                    var cr = $"{cc[i]} -";
                    phonemes.Add(HasOto(cr, ending.tone) ? cr : cc[i]);
                }
            }

            return phonemes;
        }

        // change rh V -> r V
        // since rh is a VC only alias, r is used as their natural approximant to make CV connections, if it happens
        protected override string ValidateAlias(string alias, int tone = 0) {
            string baseResolved = base.ValidateAlias(alias, tone);
            if (!string.IsNullOrEmpty(baseResolved) && baseResolved != alias) {
                if (HasOto(baseResolved, tone)) {
                    return baseResolved;
                }
                alias = baseResolved;
            }
            foreach (var vowel in vowels) {
                alias = alias.Replace("rh" + " " + vowel, "r" + " " + vowel);
            }
            return alias;
        }

        protected override double GetTransitionBasicLengthMs(string alias = "") {
            foreach (var c in shortConsonants) {
                if (alias.EndsWith(c)) {
                    return base.GetTransitionBasicLengthMs() * 0.75;
                }
            }
            foreach (var c in longConsonants) {
                if (alias.EndsWith(c)) {
                    return base.GetTransitionBasicLengthMs() * 1.5;
                }
            }
            return base.GetTransitionBasicLengthMs();
        }

    }
}
