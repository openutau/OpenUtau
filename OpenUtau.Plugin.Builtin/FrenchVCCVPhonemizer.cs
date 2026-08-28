using System;
using System.Collections.Generic;
using System.Text;
using OpenUtau.Api;
using System.Linq;


namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("French VCCV m2RUg Phonemizer", "FR VCCV", "Mim", language:"FR")]

    public class FrenchVCCVPhonemizer : SyllableBasedPhonemizer {

        private readonly string[] vowels = "A,E,e,2,9,i,o,O,u,y,a,U,0,A',E',e',2',9',i',o',O',u',y',a',U',0'".Split(",");
        private readonly string[] consonants = "b,d,f,g,Z,k,l,m,n,p,R,s,S,t,v,w,j,z,J,H,h,4,r,_hh".Split(",");
        private readonly Dictionary<string, string> dictionaryReplacements = (
            "aa=A;ai=E;ei=e;eu=2;ee=2;oe=9;ii=i;au=o;oo=O;ou=u;uu=y;an=a;in=U;un=U;on=0;uy=H;" +
            "bb=b;dd=d;ff=f;gg=g;jj=Z;kk=k;ll=l;mm=m;nn=n;pp=p;rr=R;ss=s;ch=S;tt=t;vv=v;ww=w;yy=j;zz=z;gn=J;4=4;hh=h;").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);


        private string[] shortConsonants = "R".Split(",");
        private string[] longConsonants = "t,k,g,p,s,S,Z".Split(",");
        private string[] hardConsonants = "t,k,g,p,d,b".Split(",");

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_fr.txt";
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;

        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;
            var lastC = cc.Length - 1;
            var firstC = 0;

            string basePhoneme;
            var phonemes = new List<string>();

            // add vocal fry support
            if (prevV.Contains("'")) {
                prevV = prevV.Replace("'", "");
            }

            // --------------------------- STARTING V ------------------------------- //
            if (syllable.IsStartingV) {
                basePhoneme = $"- {v}";

            } else if (syllable.IsVV) {  // if VV
                //if (!CanMakeAliasExtension(syllable)) {
                    basePhoneme = $"{prevV} {v}";
                //} else {
                //    // the previous alias will be extended
                //    basePhoneme = null;
                //}
                // --------------------------- STARTING CV ------------------------------- //
            } else if (syllable.IsStartingCVWithOneConsonant) {

                basePhoneme = $"- {cc[0]}{v}";
                if(!HasOto(basePhoneme,syllable.tone)) {
                    TryAddPhoneme(phonemes, syllable.tone, $"- {cc[0]}");
                    basePhoneme = $"{cc[0]}{v}";
                }

                // --------------------------- STARTING CCV ------------------------------- //
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {

                if (!hardConsonants.Contains(cc[0])) {
                    phonemes.Add($"- {cc[0]}");
                }

                basePhoneme = $"{cc.Last()}{v}";

                // CC + CCV support
                var ccv = $"{cc[cc.Length-2]}{cc.Last()}{v}";
                if (HasOto(ccv,syllable.tone)) {
                    basePhoneme = ccv;

                    for (int i = 0; i < cc.Length - 2; i++) {
                        var cci = $"{cc[i]} {cc[i + 1]}";

                        if (i == 0) {
                            cci = $"- {cc[i]}{cc[i + 1]}_";
                        }
                        if (!HasOto(cci, syllable.tone)) {
                            cci = $"{cc[i]}{cc[i + 1]}_";
                            if (i+1 == cc.Length-2 && HasOto($"_{ccv}",syllable.tone)) {
                                basePhoneme = $"_{ccv}";
                            }
                        }

                        TryAddPhoneme(phonemes, syllable.tone, cci);
                    }
                } else {
                    // CC + CV support
                    for (int i = 0; i < cc.Length - 1; i++) {
                        var cci = $"{cc[i]}{cc[i + 1]}_";

                        if (i == 0) {
                            cci = $"- {cc[i]}{cc[i + 1]}_";
                            if (!HasOto(cci,syllable.tone)) {
                                cci = $"{cc[i]}{cc[i + 1]}_";
                            }
                        }

                        if (HasOto(cci, syllable.tone)) {
                            phonemes.Add(cci);
                            if (i + 1 == cc.Length - 1 && HasOto($"_{cc.Last()}{v}", syllable.tone)) {
                                basePhoneme = $"_{cc.Last()}{v}";
                            }
                        } else {
                            cci = $"{cc[i]} {cc[i + 1]}";
                            TryAddPhoneme(phonemes, syllable.tone, cci);
                        }
                    }
                }


            }
                // --------------------------- IS VCV ------------------------------- //
                else if (syllable.IsVCVWithOneConsonant) {

                // try VCV
                var vc = $"{prevV} {cc[0]}";
                phonemes.Add(vc);

                basePhoneme = $"{cc[0]}{v}";

            } else {
                // ------------- IS VCV WITH MORE THAN ONE CONSONANT --------------- //
                var vc = $"{prevV} {cc[0]}";
                phonemes.Add(vc);

                basePhoneme = $"{cc.Last()}{v}";

                // CC + CCV support
                var ccv = $"{cc[cc.Length - 2]}{cc.Last()}{v}";
                if (HasOto(ccv, syllable.tone)) {
                    basePhoneme = ccv;

                    for (int i = 0; i < cc.Length - 2; i++) {
                        var cci = $"{cc[i]} {cc[i + 1]}";

                        if (!HasOto(cci, syllable.tone)) {
                            cci = $"{cc[i]}{cc[i + 1]}_";
                            if (i + 1 == cc.Length - 2 && HasOto($"_{ccv}", syllable.tone)) {
                                basePhoneme = $"_{ccv}";
                            }
                        }

                        TryAddPhoneme(phonemes, syllable.tone, cci);
                    }
                } else {
                    // CC + CV support
                    for (int i = 0; i < cc.Length - 1; i++) {
                        var cci = $"{cc[i]}{cc[i + 1]}_";

                        if (HasOto(cci, syllable.tone)) {
                            phonemes.Add(cci);
                            if (i + 1 == cc.Length - 1 && HasOto($"_{cc.Last()}{v}", syllable.tone)) {
                                basePhoneme = $"_{cc.Last()}{v}";
                            }
                        } else {
                            cci = $"{cc[i]} {cc[i + 1]}";
                            if (!HasOto(cci, syllable.tone)) {
                                cci = $"{cc[i]}{cc[i + 1]}";
                            }
                            TryAddPhoneme(phonemes, syllable.tone, cci);
                        }

                    }
                }

                // convert 'n + j' to 'J'
                if($"{cc.Last()}" == "j" && $"{cc[cc.Length-2]}" == "n") {
                    basePhoneme = $"J{v}";
                }

            }

            phonemes.Add(basePhoneme);
            return phonemes;
        }
        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc;
            string v = ending.prevV;

            var phonemes = new List<string>();

            // --------------------------- ENDING V ------------------------------- //
            if (ending.IsEndingV) {
                var vE = $"{v} -";
                phonemes.Add(vE);

            } else {
                // --------------------------- ENDING VC ------------------------------- //
                if (ending.IsEndingVCWithOneConsonant) {

                    // try 'VC -' else 'V C' + 'C -'
                    var vc = $"{v}{cc[0]} -";
                    if(HasOto(vc,ending.tone)) {
                        phonemes.Add(vc);
                    } else {
                        vc = $"{v} {cc[0]}";
                        phonemes.Add(vc);

                        var cE = $"{cc[0]} -";
                        phonemes.Add(cE);
                    }
                    


                } else {

                    // --------------------------- ENDING VCC ------------------------------- //
                    var vc = $"{v} {cc[0]}";
                    phonemes.Add(vc);
                    bool hasEnding = false;

                    for (int i = 0; i < cc.Length - 1; i++) {
                        var cci = $"{cc[i]} {cc[i + 1]}";

                        if(i == cc.Length - 2) {
                            cci = $"{cc[i]}{cc[i + 1]} -";
                            hasEnding = true;
                        }
                        if (!HasOto(cci,ending.tone)) {
                            cci = $"{cc[i]}{cc[i + 1]}_";
                            hasEnding = false;
                        }
                        if (!HasOto(cci, ending.tone)) {
                            cci = $"{cc[i]}{cc[i + 1]}";
                            hasEnding = false;
                        }

                        TryAddPhoneme(phonemes, ending.tone, cci);
                    }

                    if (!hasEnding) {
                        var cE = $"{cc.Last()} -";
                        TryAddPhoneme(phonemes, ending.tone, cE);
                    }
                }


            }

            // ---------------------------------------------------------------------------------- //

            return phonemes;
        }

        // Endings has 50 ticks gap
        protected override bool NoGap => true;

        protected override double GetTransitionBasicLengthMs(string alias, int tone, PhonemeAttributes attr) {
            double otoLength = GetTransitionBasicLengthMsByOto(alias, tone, attr);

            return otoLength;
        }

        protected override string[] GetSymbols(Note note) {
            string[] original = base.GetSymbols(note);
            if (original == null) {
                return null;
            }

            string[] arpabet = "aa,ai,ei,eu,ee,oe,ii,au,oo,ou,uu,an,in,un,on,uy,bb,dd,ff,gg,jj,kk,ll,mm,nn,pp,rr,ss,ch,tt,vv,ww,yy,zz,gn".Split(",");
            string[] m2rUg = "A,E,e,2,2,9,i,o,O,u,y,a,U,U,0,H,b,d,f,g,Z,k,l,m,n,p,R,s,S,t,v,w,j,z,J".Split(",");

            List<string> convert = new List<string>();
            foreach (string s in original) {
                string c = s;
                for (int i = 0; i < arpabet.Length; i++) {
                    if (s == arpabet[i]) {
                        c = m2rUg[i];
                    }
                }
                convert.Add(c);
            }

            if (convert == null) {
                return null;
            }

            return convert.ToArray();
        }


    }
}
