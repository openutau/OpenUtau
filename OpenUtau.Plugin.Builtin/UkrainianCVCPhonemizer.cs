using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.G2p;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Ukrainian CVC Phonemizer", "UK CVC", "phi＿pea", language:"UK")]
    // contributed by phi_pea. special thanks to FRANKENRECORDS (who also trained the G2P this phonemizer is based on) and Layt_Desu!
    
    public class UkrainianCVCPhonemizer : SyllableBasedPhonemizer {
        private readonly string[] vowels = "a,i,u,e,o,y".Split(",");
        private readonly string[] consonants = "b,bq,d,dq,dz,dzh,dzhq,dzq,f,fq,g,gq,h,hq,j,k,kq,l,lq,m,mq,n,nq,p,pq,r,rq,s,sh,shq,sq,t,tq,ts,tsh,tshq,tsq,v,vq,x,xq,z,zh,zhq,zq".Split(",");

        private readonly string[] shortConsonants = "b,b\',d,d\',dz,dz\',dZ,dZ\',g,g\',k,k\',p,p\',t,t\',ts,ts\',tS,tS\'".Split(",");
        private readonly string[] longConsonants = "f,f\',h,h\',l,l\',m,m\',n,n\',r,r\',s,s\',S,S\',v,v\',x,x\',z,z\',Z,Z\',j".Split(",");
        
        private readonly Dictionary<string, string> dictionaryReplacements = ("a=a;i=i;u=u;e=e;o=o;y=y" +
        "b=b;bq=b\';d=d;dq=d\';dz=dz;dzh=dZ;dzhq=dZ\';dzq=dz\';f=f;fq=f\';g=g;gq=g\';h=h;hq=h\';j=j;k=k;kq=k\';l=l;lq=l\';m=m;mq=m\';n=n;nq=n\';p=p;pq=p\';r=r;rq=r\';s=s;sh=S;shq=S\';sq=s\';t=t;tq=t\';ts=ts;tsh=tS;tshq=tS\';tsq=ts\';v=v;vq=v\';x=x;xq=x\';z=z;zh=Z;zhq=Z\';zq=z\'").Split(';')
            .Select(entry => entry.Split('='))
            .Where(parts => parts.Length == 2)
            .Where(parts => parts[0] != parts[1])
            .ToDictionary(parts => parts[0], parts => parts[1]);
        
        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "dict_uk.txt";
        protected override IG2p LoadBaseDictionary() => new UkrainianG2p();
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;
        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;

            string? basePhoneme = null;
            var phonemes = new List<string>();
            // ----- Starting V ----- //
            // if no [- V] try [-V], if still not it - [V]
            if (syllable.IsStartingV) {
                basePhoneme = $"- {v}";
                if (!HasOto(basePhoneme, syllable.vowelTone)) {
                    basePhoneme = $"-{v}";
                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                        basePhoneme = v;
                    }
                }
            }
            // ----- VV transitions ----- //
            // if no [V V], try in order: [VV], [_V], [* V], [*V], [V], otherwise extend the previous alias
            else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    basePhoneme = $"{prevV} {v}";
                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                        basePhoneme = $"{prevV}{v}";
                        if (!HasOto(basePhoneme, syllable.vowelTone)) {
                            basePhoneme = $"_{v}";
                            if (!HasOto(basePhoneme, syllable.vowelTone)) {
                                basePhoneme = $"* {v}";
                                if (!HasOto(basePhoneme, syllable.vowelTone)) {
                                    basePhoneme = $"*{v}";
                                    if (!HasOto(basePhoneme, syllable.vowelTone)) {
                                        basePhoneme = v;
                                    }
                                }
                            }
                        }
                    }
                } else {
                    basePhoneme = null;
                }
            }
            // ----- starting CVs ----- //
            else if (syllable.IsStartingCV) {
                // ----- one-letter CVs ----- //
                // first try [- CV], then [-CV]. if neither work - [CV] & try adding [- C] or [-C] before it
                if (syllable.IsStartingCVWithOneConsonant) {
                    basePhoneme = $"- {cc.Last()}{v}";
                    if (!HasOto(basePhoneme, syllable.tone)) {
                        basePhoneme = $"-{cc.Last()}{v}";
                        if (!HasOto(basePhoneme, syllable.tone)) {
                            basePhoneme = $"{cc.Last()}{v}";
                            var startingC = cc[0];
                            if (v == "i" && cc.Last() != "j") {
                                startingC = $"{startingC}'";
                            }
                            TryAddPhoneme(phonemes, syllable.tone, $"- {startingC}", $"-{startingC}");
                        }
                    }
                // ----- oh boy CCVs ----- //
                // (bro i straight up have no idea what i'm doing here)
                // but tldr for now it adds each C separately, first adds [- C]/[-C]/[C] fot the first consonant of the cluster, then [C -]/[C-]/[C] fot the following ones.
                } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                    basePhoneme = $"{cc.Last()}{v}";
                    
                    TryAddPhoneme(phonemes, syllable.tone, $"- {cc[0]}", $"-{cc[0]}", $"{cc[0]}");
                    
                    for (var i = longConsonants.Contains(cc[0]) ? 1 : 0; i < cc.Length - 1; i++) {
                        string? startingC = $"{cc[i]} -";
                        if (!HasOto(startingC, syllable.tone)) {
                            startingC = $"{cc[i]}-";
                            if (!HasOto(startingC, syllable.tone)) {
                                startingC = $"{cc[i]}";
                            }
                        }
                        phonemes.Add(startingC);
                    }
                }
            }
            // ----- VCV, as it's called here ----- //
            // 
            else {
                // ----- one consonant ----- //
                // first add [CV], then [V C]. if doesn't work, try [VC], if still not, no transition
                if (syllable.IsVCVWithOneConsonant) {
                    basePhoneme = $"{cc.Last()}{v}";
                    if (cc.Last().Contains('\'') && v == "i" && !HasOto(basePhoneme, syllable.tone)) {
                        basePhoneme = basePhoneme.Replace("'", "");
                    }

                    var vc = $"{prevV} {cc.Last()}";
                    if ((v == "i" && !cc.Last().Contains('\'')) && cc.Last() != "j") {
                        vc = $"{prevV} {cc.Last()}'";
                    }
                    if (!HasOto(vc, syllable.tone)) {
                        vc = $"{prevV}{cc.Last()}";
                        if (v == "i" && cc.Last() != "j") {
                            vc = $"{prevV}{cc.Last()}'";
                        }
                        if (!HasOto(vc, syllable.tone)) {
                            vc = null;
                        }
                    }
                    phonemes.Add(vc);
                // ----- CCs (oh boy) ----- //
                // first checks whether a [V C] or a [VC] transition is available for the first consonant of the cluster
                // if none are present, don't add one at all, and if it's a short consonant, add the [C -]/[C-]/[C] for it
                // if the first consonant was long, skip adding it as the previous step already handled it, then just add
                // the rest of the consonants as [C -]s/[C-]s/[C]s, regardless of type
                } else {
                    basePhoneme = $"{cc.Last()}{v}";
                    if (cc.Last().Contains('\'') && v == "i" && !HasOto(basePhoneme, syllable.tone)) {
                        basePhoneme = basePhoneme.Replace("'", "");
                    }
                    
                    string? vc = $"{prevV} {cc[0]}";
                    if (!HasOto(vc, syllable.tone)) {
                        vc = $"{prevV}{cc[0]}";
                        if (!HasOto(vc, syllable.tone) && longConsonants.Contains(cc[0])) {
                            vc = null;
                            TryAddPhoneme(phonemes, syllable.tone, $"{cc[0]} -", $"{cc[0]}-", $"{cc[0]}");
                        } else if (!HasOto(vc, syllable.tone) && shortConsonants.Contains(cc[0])) {
                            vc = null;
                        }
                    }
                    phonemes.Add(vc);
                    for (var i = longConsonants.Contains(cc[0]) ? 1 : 0; i < cc.Length - 1; i++) {
                        TryAddPhoneme(phonemes, syllable.tone, $"{cc[i]} -", $"{cc[i]}-", $"{cc[i]}");
                    }
                }
                
            }
            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc;
            string v = ending.prevV;

            string? endPhoneme = null;
            var phonemes = new List<string>();

            // ----- Ending Vs ----- //
            // try [V -], then [V-]. if none work, don't add an ending V
            if (ending.IsEndingV) {
                endPhoneme = $"{v} -";
                if (!HasOto(endPhoneme, ending.tone)) {
                    endPhoneme = $"{v}-";
                    if (!HasOto(endPhoneme, ending.tone)) {
                        endPhoneme = null;
                    }
                }
            // ----- ending VCs ----- //    
            } else {
                // ----- one consonant ----- //
                // first try [VC -], then [C V]/[VC] + [C -]/[C-]/[C]
                if (ending.IsEndingVCWithOneConsonant) {
                    endPhoneme = $"{v}{cc[0]} -";
                    if (!HasOto(endPhoneme, ending.tone)) {
                        endPhoneme = $"{cc[0]} -";
                        if (!HasOto(endPhoneme, ending.tone)) {
                            endPhoneme = $"{cc[0]}-";
                            if (!HasOto(endPhoneme, ending.tone)) {
                                endPhoneme = $"{cc[0]}";
                            }
                        }
                        //phonemes.Add($"{v} {cc[0]}");
                        TryAddPhoneme(phonemes, ending.tone, $"{v} {cc[0]}", $"{v}{cc[0]}");
                    }
                // ----- VCCs (oh boy...) ----- //
                // similar logic to VCVs with multiple consonants
                } else  {
                    endPhoneme = $"{cc.Last()} -";
                    string? vc = $"{v} {cc[0]}";
                    if (!HasOto(vc, ending.tone) && longConsonants.Contains(cc[0])) {
                        vc = null;
                        TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", $"{cc[0]}-", $"{cc[0]}");
                    }
                    phonemes.Add(vc);
                    for (var i = longConsonants.Contains(cc.Last()) ? 0 : 1; i < cc.Length - 1; i++) {
                        TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} -", $"{cc[i]}-", $"{cc[i]}");
                    }
                }
            }
            
            phonemes.Add(endPhoneme);
            return phonemes;
        }
        
        protected override double GetTransitionBasicLengthMs(string alias = "") {
            foreach (var c in shortConsonants) {
                if (alias.EndsWith(c)) {
                    return base.GetTransitionBasicLengthMs();
                }
            }
            foreach (var c in longConsonants) {
                if (alias.EndsWith(c)) {
                    return base.GetTransitionBasicLengthMs() * 2;
                }
            }
            return base.GetTransitionBasicLengthMs();
        }
    }
    
    
}
