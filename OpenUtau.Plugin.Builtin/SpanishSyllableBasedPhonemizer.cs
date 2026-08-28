using System;
using System.Collections.Generic;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using System.Linq;
using Serilog;
using System.IO;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Spanish syllable-based phonemizer.
    /// I tried to make this phonemizer as compatible with many different methods as possible.
    /// Supports both CVVC and VCV if the voicebank has it.
    /// Supports seseo ("s" instead of "z" if the voicebank doesn't have the latter).
    /// It also substitutes "nh" for "ny" if the voicebank doesn't have the first.
    /// Ít now also uses "i" instead of "y" and "u" instead of "w" depending on what the voicebank supports.
    /// Now with full VCV support, including "consonant VCV" if the voicebank has either of them (ex. "l ba", "n da" but also "m bra" etc.).
    /// </summary>
    [Phonemizer("Spanish Syllable-Based Phonemizer", "ES SYL", "Lotte V", language: "ES")]
    public class SpanishSyllableBasedPhonemizer : SyllableBasedPhonemizer {

        private readonly string[] vowels = "a,e,i,o,u".Split(',');
        private readonly string[] consonants = "b,ch,d,dz,f,g,h,hh,j,k,l,ll,m,n,nh,p,r,rr,s,sh,t,ts,w,y,z,zz,zh,I,U".Split(',');
        private readonly Dictionary<string, string> dictionaryReplacements = ("a=a;e=e;i=i;o=o;u=u;" + "b=b;ch=ch;d=d;f=f;g=g;gn=nh;k=k;l=l;ll=j;m=m;n=n;p=p;r=r;rr=rr;s=s;t=t;w=w;x=h;y=y;z=z;I=I;U=U;B=b;D=d;G=g;Y=y").Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private readonly string[] longConsonants = "ch,dz,h,s,sh,k,p,rr,t,ts,z".Split(',');
        private readonly string[] initialCC = "bl,bly,blw,br,bry,brw,by,bw,dr,dry,drw,dy,dw,fl,fly,flw,fr,fry,frw,fy,fw,gl,gly,glw,gr,gry,grw,gy,gw,kl,kly,klw,kr,kry,krw,ky,kw,pl,ply,plw,pr,pry,prw,py,pw,tl,tly,tlw,tr,try,trw,ty,tw,chy,chw,hy,hw,jy,jw,ly,lw,my,mw,ny,nw,ry,rw,sy,sw,vy,vw,zy,zw,nhy,nhw,rry,rrw,ts".Split(',');
        private readonly string[] notClusters = "dz,hh,ll,nh,sh,zz,zh,th,ng".Split(',');
        private readonly string[] burstConsonants = "ch,dz,j,k,p,t,ts,r".Split(',');

        // For banks with a seseo accent
        private readonly Dictionary<string, string> seseo = "z=s".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isSeseo = false;

        // For banks with alternate semivowel aliases
        private readonly Dictionary<string, string> semiVowelFallback = "jya=ja;jye=je;jyo=jo;jyu=ju;chya=cha;chye=che;chyo=cho;chyu=chu;nhya=nha;nhye=nhe;nhyo=nho;nhyu=nhu;nhwa=nua;nhwe=nue;nhwi=nui;nhwo=nuo;ya=ia;ye=ie;yo=io;yu=iu;wa=ua;we=ue;wi=ui;wo=uo".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isSemiVowelFallback = false;

        // For banks with alternate alias for "ñ"
        private readonly Dictionary<string, string> eñeFallback = "nhya=nya;nhye=nye;nhyo=nyo;nhyu=nyu;nhwa=nwa;nhwe=nwe;nhwi=nwi;nhwo=nwo;nha=nya;nhe=nye;nyi=ni;nhi=nyi;nho=nyo;nhu=nyu;a nh=a n;e nh=e n;i nh=i n;o nh=o n;u nh=u n;n nh=n n;l nh=l n;m nh=m n".Split(';')
                .Select(entry => entry.Split('='))
                .Where(parts => parts.Length == 2)
                .Where(parts => parts[0] != parts[1])
                .ToDictionary(parts => parts[0], parts => parts[1]);

        private bool isEñeFallback = false;

        protected override string[] GetVowels() => vowels;
        protected override string[] GetConsonants() => consonants;
        protected override string GetDictionaryName() => "cmudict_es.txt";

        protected override IG2p LoadBaseDictionary() {
            var g2ps = new List<IG2p>();

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "es-syl.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }
            g2ps.Add(new SpanishG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }
        protected override Dictionary<string, string> GetDictionaryPhonemesReplacement() => dictionaryReplacements;

        protected override List<string> ProcessSyllable(Syllable syllable) {
            string prevV = syllable.prevV;
            string[] cc = syllable.cc;
            string v = syllable.v;

            string basePhoneme;
            var phonemes = new List<string>();

            var lastC = cc.Length - 1;
            var firstC = 0;

            var rv = $"- {v}";
            var vv1 = $"{prevV} {v}";
            var vv2 = $"{prevV}{v}";

            // Switch between phonetic systems, depending on certain aliases in the bank
            if (!HasOto($"z{v}", syllable.tone) && !HasOto($"- z{v}", syllable.tone)) {
                isSeseo = true;
            }
            if (!HasOto($"{cc.Length}y{v}", syllable.vowelTone) && !HasOto($"- {cc.Length}y{v}", syllable.vowelTone)) {
                isSemiVowelFallback = true;
            }
            if (!HasOto($"nh{v}", syllable.vowelTone) && !HasOto($"- nh{v}", syllable.vowelTone)) {
                isEñeFallback = true;
            }

            if (syllable.IsStartingV) {
                basePhoneme = rv;
                if (!HasOto(rv, syllable.vowelTone)) {
                    basePhoneme = $"{v}";
                }
            } else if (syllable.IsVV) {
                if (!CanMakeAliasExtension(syllable)) {
                    if (HasOto(vv1, syllable.vowelTone)) {
                        basePhoneme = vv1;
                    } else if (!HasOto(vv1, syllable.vowelTone) && HasOto(vv2, syllable.vowelTone)) {
                        basePhoneme = vv2;
                    } else {
                        basePhoneme = v;
                    }
                } else {
                    // the previous alias will be extended
                    basePhoneme = null;
                }
            } else if (syllable.IsStartingCVWithOneConsonant) {
                // TODO: move to config -CV or -C CV
                var rcv = $"- {cc[0]}{v}";
                if (HasOto(rcv, syllable.vowelTone) || HasOto(ValidateAlias(rcv), syllable.vowelTone)) {
                    basePhoneme = rcv;
                } else {
                    basePhoneme = $"{cc[0]}{v}";
                }
            } else if (syllable.IsStartingCVWithMoreThanOneConsonant) {
                // try RCCV
                var rccv = $"- {string.Join("", cc)}{v}";
                var ccv = string.Join("", cc) + v;
                var crv = $"{cc.Last()} {v}";
                if ((HasOto(rccv, syllable.vowelTone) || HasOto(ValidateAlias(rccv), syllable.vowelTone)) && (initialCC.Contains(string.Join("", cc)) || initialCC.Contains(ValidateAlias(string.Join("", cc))))) {
                    basePhoneme = rccv;
                    lastC = 0;
                } else if (!HasOto(rccv, syllable.vowelTone) && !HasOto(ValidateAlias(rccv), syllable.vowelTone) && (HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) && (initialCC.Contains(string.Join("", cc)) || initialCC.Contains(ValidateAlias(string.Join("", cc))))) {
                    basePhoneme = ccv;
                    lastC = 0;
                } else {
                    basePhoneme = ValidateAlias(crv);
                    if (!HasOto(ValidateAlias(crv), syllable.vowelTone)) {
                        basePhoneme = $"{cc.Last()}{v}";
                    }
                    // try RCC
                    for (var i = cc.Length; i > 1; i--) {
                        var rcc = $"- {string.Join("", cc.Take(i))}";
                        var cc1 = string.Join("", cc.Take(i));
                        if (initialCC.Contains(string.Join("", cc.Take(i))) || initialCC.Contains(ValidateAlias(string.Join("", cc.Take(i))))) {
                            if (TryAddPhoneme(phonemes, syllable.tone, rcc, ValidateAlias(rcc), cc1, ValidateAlias(cc1))) {
                                firstC = i - 1;
                                break;
                            }
                        }
                    }
                    if (phonemes.Count == 0) {
                        TryAddPhoneme(phonemes, syllable.tone, $"- {cc[0]}", ValidateAlias($"- {cc[0]}"));
                    }
                }
            } else { // VCV
                var vcv = $"{prevV} {cc[0]}{v}";
                var vccv = $"{prevV} {string.Join("", cc)}{v}";
                if ((HasOto(vcv, syllable.vowelTone) || HasOto(ValidateAlias(vcv), syllable.vowelTone))
                    && (syllable.IsVCVWithOneConsonant)) {
                    basePhoneme = vcv;
                } else if ((HasOto(vccv, syllable.vowelTone) || HasOto(ValidateAlias(vccv), syllable.vowelTone))
                    && syllable.IsVCVWithMoreThanOneConsonant
                    && (initialCC.Contains(string.Join("", cc)) || initialCC.Contains(ValidateAlias(string.Join("", cc))))) {
                    basePhoneme = vccv;
                    lastC = 0;
                } else {
                    var cv = cc.Last() + v;
                    var crv = $"{cc.Last()} {v}";
                    basePhoneme = cv;
                    if (!HasOto(cv, syllable.vowelTone) && !HasOto(ValidateAlias(cv), syllable.vowelTone)) {
                        basePhoneme = crv;
                    }

                    // try CCV
                    if (cc.Length - firstC > 1) {
                        for (var i = firstC; i < lastC; i++) {
                            var ccv = $"{string.Join("", cc.Skip(i))}{v}";
                            var ccv2 = $"{string.Join(" ", cc.Skip(i))}{v}";
                            var ccv3 = $"{cc[0]} {string.Join("", cc.Skip(i))}{v}";
                            if ((HasOto(ccv, syllable.vowelTone) || HasOto(ValidateAlias(ccv), syllable.vowelTone)) && (initialCC.Contains(string.Join("", cc.Skip(i))) || initialCC.Contains(ValidateAlias(string.Join("", cc.Skip(i)))))) {
                                lastC = i;
                                basePhoneme = ccv;
                                break;
                            } else if (HasOto(ccv2, syllable.vowelTone) || HasOto(ValidateAlias(ccv2), syllable.vowelTone)) {
                                lastC = i;
                                basePhoneme = ccv2;
                                break;
                            } else if ((HasOto(ccv3, syllable.vowelTone) || HasOto(ValidateAlias(ccv3), syllable.vowelTone)) && (initialCC.Contains(string.Join("", cc.Skip(i))) || initialCC.Contains(ValidateAlias(string.Join("", cc.Skip(i)))))) {
                                lastC = i;
                                basePhoneme = ccv3;
                                break;
                            } else {
                                continue;
                            }
                        }
                    }
                    // try vcc or vc
                    for (var i = lastC + 1; i >= 0; i--) {
                        var vc1 = $"{prevV} {cc[0]}";
                        var vc2 = $"{prevV}{cc[0]}";
                        var vcc = $"{prevV} {string.Join("", cc.Take(2))}";
                        var vcc2 = $"{prevV}{string.Join("", cc.Take(2))}";
                        if ((HasOto(ValidateAlias(vcc), syllable.tone) || HasOto(ValidateAlias(vcc), syllable.tone)) && (!notClusters.Contains(string.Join("", cc.Take(2))) && !notClusters.Contains(ValidateAlias(string.Join("", cc.Take(2)))))) {
                            phonemes.Add(vcc);
                            firstC = 1;
                            break;
                        } else if ((HasOto(vcc2, syllable.tone) || HasOto(ValidateAlias(vcc2), syllable.tone)) && (!notClusters.Contains(string.Join("", cc.Take(2))) && !notClusters.Contains(ValidateAlias(string.Join("", cc.Take(2)))))) {
                            phonemes.Add(vcc2);
                            firstC = 1;
                            break;
                        } else if ((HasOto(vc1, syllable.tone) || HasOto(ValidateAlias(vc1), syllable.tone))) {
                            phonemes.Add(vc1);
                            break;
                        } else if (HasOto(vc2, syllable.tone) || HasOto(ValidateAlias(vc2), syllable.tone)) {
                            phonemes.Add(vc2);
                            break;
                        }
                    }
                }
            }
            for (var i = firstC; i < lastC; i++) {
                // we could use some CCV, so lastC is used
                // we could use -CC so firstC is used
                var cc1 = $"{cc[i]}{cc[i + 1]}";

                // Use [C1C2]
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                // Use [C1 C2]
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = $"{cc[i]} {cc[i + 1]}";
                }
                if (!HasOto(cc1, syllable.tone)) {
                    cc1 = ValidateAlias(cc1);
                }
                if (i + 1 < lastC) {
                    var cc2 = $"{cc[i + 1]}{cc[i + 2]}";

                    // Use [C2C3]
                    if (!HasOto(cc2, syllable.tone)) {
                        cc2 = ValidateAlias(cc2);
                    }
                    // Use [C2 C3]
                    if (!HasOto(cc2, syllable.tone)) {
                        cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                    }
                    if (!HasOto(cc2, syllable.tone)) {
                        cc2 = ValidateAlias(cc2);
                    }
                    if ((HasOto(cc1, syllable.tone) || HasOto(ValidateAlias(cc1), syllable.tone)) && (HasOto(cc2, syllable.tone) || HasOto(ValidateAlias(cc2), syllable.tone))) {
                        // like [V C1] [C1 C2] [C2 C3] [C3 ..]
                        phonemes.Add(cc1);
                    } else if (TryAddPhoneme(phonemes, syllable.tone, cc1, ValidateAlias(cc1))) {
                        // like [V C1] [C1 C2] [C2 ..]
                    } else {
                        // like[V C1][C1][C2..]
                        TryAddPhoneme(phonemes, syllable.tone, cc[i], ValidateAlias(cc[i]));
                    }
                } else {
                    // like [V C1] [C1 C2]  [C2 ..] or like [V C1] [C1 -] [C3 ..]
                    TryAddPhoneme(phonemes, syllable.tone, cc1, cc[i], ValidateAlias(cc[i]));
                }
            }

            phonemes.Add(basePhoneme);
            return phonemes;
        }

        protected override List<string> ProcessEnding(Ending ending) {
            string[] cc = ending.cc;
            string v = ending.prevV;

            var phonemes = new List<string>();

            var lastC = cc.Length - 1;
            var firstC = 0;

            if (ending.IsEndingV) { // ending V
                TryAddPhoneme(phonemes, ending.tone, $"{v} -", $"{v}-", $"{v} R");
            } else if (ending.IsEndingVCWithOneConsonant) { // ending VC
                var vcr = $"{v} {cc[0]}-";
                var vcr2 = $"{v}{cc[0]}-";
                var vc = $"{v} {cc[0]}";
                var vc2 = $"{v}{cc[0]}";
                if (HasOto(vcr, ending.tone) || HasOto(ValidateAlias(vcr), ending.tone)) {
                    phonemes.Add(vcr);
                } else if (HasOto(vcr2, ending.tone) || HasOto(ValidateAlias(vcr2), ending.tone)) {
                    phonemes.Add(vcr2);
                } else {
                    if (HasOto(vc, ending.tone) || HasOto(ValidateAlias(vc), ending.tone)) {
                        phonemes.Add(vc);
                    } else if (HasOto(vc2, ending.tone) || HasOto(ValidateAlias(vc2), ending.tone)) {
                        phonemes.Add(vc2);
                    }
                    if (burstConsonants.Contains(cc[0]) || burstConsonants.Contains(ValidateAlias(cc[0]))) {
                        TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", $"{ValidateAlias(cc[0])} -", cc[0], ValidateAlias(cc[0]));
                    } else {
                        TryAddPhoneme(phonemes, ending.tone, $"{cc[0]} -", $"{ValidateAlias(cc[0])} -", $"{cc[0]}-", $"{ValidateAlias(cc[0])}-", $"{cc[0]} R", $"{ValidateAlias(cc[0])} R");
                    }
                }
            } else { // ending VCC (very rare, usually only occurs in words ending with "x")
                for (var i = lastC; i >= 0; i--) {
                    var vcc = $"{v} {string.Join("", cc.Take(2))}-";
                    var vcc3 = $"{v}{string.Join("", cc.Take(2))}";
                    var vcc2 = $"{v} {string.Join("", cc.Take(2))}";
                    var vcc4 = $"{v}{string.Join(" ", cc.Take(2))}-";
                    var vc = $"{v} {cc[0]}";
                    var vc2 = $"{v}{cc[0]}";
                    if ((HasOto(vcc, ending.tone) || HasOto(ValidateAlias(vcc), ending.tone)) && lastC == 1 && !notClusters.Contains(string.Join("", cc.Take(2))) && !notClusters.Contains(ValidateAlias(string.Join("", cc.Take(2))))) {
                        phonemes.Add(vcc);
                        firstC = 1;
                        break;
                    } else if ((HasOto(vcc2, ending.tone) || HasOto(ValidateAlias(vcc2), ending.tone)) && !notClusters.Contains(string.Join("", cc.Take(2))) && !notClusters.Contains(ValidateAlias(string.Join("", cc.Take(2))))) {
                        phonemes.Add(vcc2);
                        firstC = 1;
                        break;
                    } else if ((HasOto(vcc3, ending.tone) || HasOto(ValidateAlias(vcc3), ending.tone)) && !notClusters.Contains(string.Join("", cc.Take(2))) && !notClusters.Contains(ValidateAlias(string.Join("", cc.Take(2))))) {
                        phonemes.Add(vcc3);
                        firstC = 1;
                        break;
                    } else if ((HasOto(vcc4, ending.tone) || HasOto(ValidateAlias(vcc4), ending.tone)) && lastC == 1 && !notClusters.Contains(string.Join("", cc.Take(2))) && !notClusters.Contains(ValidateAlias(string.Join("", cc.Take(2))))) {
                        phonemes.Add(vcc4);
                        firstC = 1;
                        break;
                    } else
                    if (HasOto(vc, ending.tone) || HasOto(ValidateAlias(vc), ending.tone)) {
                        phonemes.Add(vc);
                        break;
                    } else {
                        TryAddPhoneme(phonemes, ending.tone, vc2, ValidateAlias(vc2));
                        break;
                    }
                }

                for (var i = firstC; i < lastC; i++) {
                    // all CCs except the first one are /C1C2/, the last one is /C1 C2-/
                    // but if there is no /C1C2/, we try /C1 C2-/, vise versa for the last one
                    var cc1 = $"{cc[i]} {cc[i + 1]}";
                    if (i < cc.Length - 2) {
                        var cc2 = $"{cc[i + 1]} {cc[i + 2]}";
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, ending.tone) && !notClusters.Contains($"{cc[i]}{cc[i + 1]}") && !notClusters.Contains(ValidateAlias($"{cc[i]}{cc[i + 1]}"))) {
                            cc1 = $"{cc[i]}{cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = $"{cc[i + 1]}{cc[i + 2]}";
                        }
                        if (!HasOto(cc2, ending.tone)) {
                            cc2 = ValidateAlias(cc2);
                        }
                        if (HasOto(cc1, ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}-", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}-"), ending.tone)) || HasOto($"{cc[i + 1]}{cc[i + 2]}-", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]}{cc[i + 2]}-"), ending.tone)) {
                            // like [C1 C2][C2 ...]
                            phonemes.Add(cc1);
                        } else if ((HasOto(cc[i], ending.tone) || HasOto(ValidateAlias(cc[i]), ending.tone) && (HasOto(cc2, ending.tone) || HasOto($"{cc[i + 1]} {cc[i + 2]}-", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]} {cc[i + 2]}-"), ending.tone) || HasOto($"{cc[i + 1]}{cc[i + 2]}-", ending.tone) || HasOto(ValidateAlias($"{cc[i + 1]}{cc[i + 2]}-"), ending.tone)))) {
                            // like [C1 C2-][C3 ...]
                            phonemes.Add(cc[i]);
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} {cc[i + 2]}-", ValidateAlias($"{cc[i + 1]} {cc[i + 2]}-"), $"{cc[i + 1]}{cc[i + 2]}-", ValidateAlias($"{cc[i + 1]}{cc[i + 2]}-"))) {
                            // like [C1 C2-][C3 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]}{cc[i + 2]}", ValidateAlias($"{cc[i + 1]}{cc[i + 2]}"))) {
                            // like [C1C2][C2 ...]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            i++;
                        } else {
                            // like [C1][C2 ...]
                            TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i]), $"{cc[i]} -", ValidateAlias($"{cc[i]} -"));
                            TryAddPhoneme(phonemes, ending.tone, cc[i + 1], ValidateAlias(cc[i + 1]), $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"));
                            i++;
                        }
                    } else {
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = $"{cc[i]}{cc[i + 1]}";
                        }
                        if (!HasOto(cc1, ending.tone)) {
                            cc1 = ValidateAlias(cc1);
                        }
                        if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]} {cc[i + 1]}-", ValidateAlias($"{cc[i]} {cc[i + 1]}-"), $"{cc[i]}{cc[i + 1]}-", ValidateAlias($"{cc[i]}{cc[i + 1]}-"))) {
                            // like [C1 C2-]
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, cc1, ValidateAlias(cc1))) {
                            // like [C1 C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        } else if (TryAddPhoneme(phonemes, ending.tone, $"{cc[i]}{cc[i + 1]}", ValidateAlias($"{cc[i]}{cc[i + 1]}"))) {
                            // like [C1C2][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        } else {
                            // like [C1][C2 -]
                            TryAddPhoneme(phonemes, ending.tone, cc[i], ValidateAlias(cc[i]), $"{cc[i]} -", ValidateAlias($"{cc[i]} -"));
                            TryAddPhoneme(phonemes, ending.tone, $"{cc[i + 1]} -", ValidateAlias($"{cc[i + 1]} -"), cc[i + 1], ValidateAlias(cc[i + 1]));
                            i++;
                        }
                    }
                }
            }
            return phonemes;
        }

        protected override string ValidateAlias(string alias) {
            // Validate alias depending on method
            if (isSeseo) {
                foreach (var syllable in seseo) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }
            if (isSemiVowelFallback) {
                foreach (var syllable in semiVowelFallback) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }
            if (isEñeFallback) {
                foreach (var syllable in eñeFallback) {
                    alias = alias.Replace(syllable.Key, syllable.Value);
                }
            }

            // Other validations
            if (alias.Contains("I")) {
                alias = alias.Replace("I", "i");
            }
            if (alias.Contains("U")) {
                alias = alias.Replace("U", "u");
            }
            foreach (var cc in new[] { "ks" }) {
                alias = alias.Replace("ks", "x");
            }
            if (alias == "r") {
                alias = alias.Replace("r", "rr");
            }
            if (alias == "ch") {
                alias = alias.Replace("ch", "tch");
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
