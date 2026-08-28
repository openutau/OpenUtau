using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Russian VCCV Phonemizer", "RU VCCV", "Heiden.BZR", language: "RU")]
    public class RussianVCCVPhonemizer : SyllableBasedPhonemizer {

        private readonly string[] vowels = "a,e,o,u,y,i,M,N,ex,ax,x".Split(",");
        private readonly string[] consonants = "sh',sh,zh,j,ts,ch,b',b,v',v,g',g,d',d,z',z,k',k,l',l,m',m,n',n,p',p,r',r,s',s,t',t,f',f,h',h".Split(",");
        private readonly string[] burstConsonants = "t,t',k,k',p,p',ch,ts,b,b',g,g',d,d'".Split(",");
        private readonly Dictionary<string, string> dictionaryReplacements = ("a=ax;aa=a;ay=a;b=b;bb=b';c=ts;ch=ch;d=d;dd=d';ee=e;" +
            "f=f;ff=f';g=g;gg=g';h=h;hh=h';i=x;ii=i;j=j;ja=a;je=e;jo=o;ju=u;k=k;kk=k';l=l;ll=l';m=m;mm=m';n=n;nn=n';oo=o;ae=e;" +
            "p=p;pp=p';r=r;rr=r';s=s;sch=sh';sh=sh;ss=s';t=t;tt=t';u=u;uj=u;uu=u;v=v;vv=v';y=ex;yy=y;z=z;zh=zh;zz=z'").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);
        private readonly string[] noStartConsonants = "t,t',k,k',p,p',ch,ts".Split(",");

        private string[] hardConsonants = "b,v,g,d,z,k,l,m,n,p,r,s,t,f,h,sh,ts".Split(",");
        private string[] shortConsonants = "r,r'".Split(",");
        private string[] longConsonants = "sh,sh',ch,ts,ts".Split(",");

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_ru.txt";
        protected override IG2p LoadBaseDictionary() => new RussianG2p();
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;

        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;

            string basePhoneme;
            var phonemes = new List<string>();
            if (syllable.IsStartingV) {
                basePhoneme = $"- {v}";
            } else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    basePhoneme = $"{prevV} {v}";
                } else {
                    // the previous alias will be extended
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                // TODO: move to config -CV or -C CV
                var rcv = $"- {cc[0]}{v}";
                if (HasOto(rcv, syllable.tone)) {
                    basePhoneme = rcv;
                } else {
                    basePhoneme = $"{cc[0]}{v}";
                    if (!noStartConsonants.Contains(cc[0])) {
                        phonemes.Add($"- {cc[0]}");
                    }
                }
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                basePhoneme = $"{cc.Last()}{v}";
                if (!noStartConsonants.Contains(cc[0])) {
                    phonemes.Add($"- {cc[0]}");
                }
            } else { // VCV
                basePhoneme = $"{cc.Last()}{v}";
                phonemes.Add($"{prevV} {cc[0]}");
            }
            for (var i = 0; i < cc.Length - 1; i++) {
                // same for any transition
                var currentCc = $"{cc[i]} {cc[i + 1]}";
                if (!HasOto(currentCc, syllable.tone)) {
                    continue;
                }
                phonemes.Add(currentCc);
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
            } else if (ending.IsEndingVCWithOneConsonant) {
                // TODO: move to config VC- or VC C-
                var vcr = $"{v}{cc[0]} -";
                if (HasOto(vcr, ending.tone)) {
                    phonemes.Add(vcr);
                } else {
                    phonemes.Add($"{v} {cc[0]}");
                    phonemes.Add($"{cc[0]} -");
                }
            } else { // VCmR
                phonemes.Add($"{v} {cc[0]}");
                for (var i = 0; i < cc.Length - 1; i++) {
                    var currentCc = $"{cc[i]} {cc[i + 1]}";
                    if (!HasOto(currentCc, ending.tone)) {
                        continue;
                    }
                    phonemes.Add(currentCc);
                }
                if (burstConsonants.Contains(cc.Last())) {
                    phonemes.Add($"{cc.Last()} -");
                }
            }
            return phonemes;
        }

        protected override string ValidateAlias(string alias) {
            foreach (var consonant in new[] { "'", "ch", "j" }) {
                foreach (var vowel in new[] { "ax", "ex" }) {
                    alias = alias.Replace(consonant + vowel, consonant + "x");
                }
                alias = alias.Replace(consonant + "y", consonant + "i");
            }
            foreach (var consonant in hardConsonants) {
                alias = alias.Replace(consonant + "i", consonant + "y");

            }
            return alias;
        }

        // Endings has 50 ticks gap
        protected override bool NoGap => true;

        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            double otoLength = GetTransitionBasicLengthMsByOto(alias, tone, attr);

            return otoLength;
        }
    }
}
