using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Italian CVVC Phonemizer", "IT CVVC", "DJ-001", language: "IT")]
    public class ItalianCVVCPhonemizer : Phonemizer {

        /// <summary>
        /// Italian CVVC Phonemizer by DJ-001
        /// Utilizing Makku's reclist connotation, based on TUBS's Japanese CVVC Phonemizer
        /// </summary>

        static readonly string[] plainVowels = new string[] { "a", "e", "i", "o", "u", "3", "0" };

        static readonly string[] vowels = new string[] {
            "a=a,pa,ba,ta,da,ka,ga,tsa,dza,tSa,dZa,fa,va,sa,za,Sa,ra,la,ja,ya,wa,na,ma,gna,sta,ska,spa,sfa,zda,zba,zga,zva,zra,zla,zma,zna,pya,bya,tya,dya,kya,gya,tsya,dzya,fya,vya,sya,zya,rya,lya,nya,mya,pwa,bwa,twa,dwa,kwa,gwa,tswa,dzwa,fwa,vwa,swa,zwa,rwa,lwa,jwa,ywa,nwa,mwa,Na,Nya,Nwa,Ma,Mya,Mwa",
            "e=e,pe,be,te,de,ke,ge,tse,dze,tSe,dZe,fe,ve,se,ze,Se,re,le,je,ye,we,ne,me,gne,ste,ske,spe,sfe,zde,zbe,zge,zve,zre,zle,zme,zne,pye,bye,tye,dye,kye,gye,tsye,dzye,fye,vye,sye,zye,rye,lye,nye,mye,pwe,bwe,twe,dwe,kwe,gwe,tswe,dzwe,fwe,vwe,swe,zwe,rwe,lwe,jwe,ywe,nwe,mwe,Ne,Nye,Nwe,Me,Mye,Mwe",
            "i=i,pi,bi,ti,di,ki,gi,tsi,dzi,tSi,dZi,fi,vi,si,zi,Si,ri,li,ji,yi,wi,ni,mi,gni,sti,ski,spi,sfi,zdi,zbi,zgi,zvi,zri,zli,zmi,zni,pyi,byi,tyi,dyi,kyi,gyi,tsyi,dzyi,fyi,vyi,syi,zyi,ryi,lyi,nyi,myi,pwi,bwi,twi,dwi,kwi,gwi,tswi,dzwi,fwi,vwi,swi,zwi,rwi,lwi,jwi,ywi,nwi,mwi,Ni,Nyi,Nwi,Mi,Myi,Mwi",
            "o=o,po,bo,to,do,ko,go,tso,dzo,tSo,dZo,fo,vo,so,zo,So,ro,lo,jo,yo,wo,no,mo,gno,sto,sko,spo,sfo,zdo,zbo,zgo,zvo,zro,zlo,zmo,zno,pyo,byo,tyo,dyo,kyo,gyo,tsyo,dzyo,fyo,vyo,syo,zyo,ryo,lyo,nyo,myo,pwo,bwo,two,dwo,kwo,gwo,tswo,dzwo,fwo,vwo,swo,zwo,rwo,lwo,jwo,ywo,nwo,mwo,No,Nyo,Nwo,Mo,Myo,Mwo",
            "u=u,pu,bu,tu,du,ku,gu,tsu,dzu,tSu,dZu,fu,vu,su,zu,Su,ru,lu,ju,yu,wu,nu,mu,gnu,stu,sku,spu,sfu,zdu,zbu,zgu,zvu,zru,zlu,zmu,znu,pyu,byu,tyu,dyu,kyu,gyu,tsyu,dzyu,fyu,vyu,syu,zyu,ryu,lyu,nyu,myu,pwu,bwu,twu,dwu,kwu,gwu,tswu,dzwu,fwu,vwu,swu,zwu,rwu,lwu,jwu,ywu,nwu,mwu,Nu,Nyu,Nwu,Mu,Myu,Mwu",
            "3=3,p3,b3,t3,d3,k3,g3,ts3,dz3,tS3,dZ3,f3,v3,s3,z3,S3,r3,l3,j3,y3,w3,n3,m3,gn3,st3,sk3,sp3,sf3,zd3,zb3,zg3,zv3,zr3,zl3,zm3,zn3,py3,by3,ty3,dy3,ky3,gy3,tsy3,dzy3,fy3,vy3,sy3,zy3,ry3,ly3,ny3,my3,pw3,bw3,tw3,dw3,kw3,gw3,tsw3,dzw3,fw3,vw3,sw3,zw3,rw3,lw3,jw3,yw3,nw3,mw3,N3,Ny3,Nw3,M3,My3,Mw3",
            "0=0,p0,b0,t0,d0,k0,g0,ts0,dz0,tS0,dZ0,f0,v0,s0,z0,S0,r0,l0,j0,y0,w0,n0,m0,gn0,st0,sk0,sp0,sf0,zd0,zb0,zg0,zv0,zr0,zl0,zm0,zn0,py0,by0,ty0,dy0,ky0,gy0,tsy0,dzy0,fy0,vy0,sy0,zy0,ry0,ly0,ny0,my0,pw0,bw0,tw0,dw0,kw0,gw0,tsw0,dzw0,fw0,vw0,sw0,zw0,rw0,lw0,jw0,yw0,nw0,mw0,N0,Ny0,Nw0,M0,My0,Mw0"
        };

        static readonly string[] consonants = new string[] {
            "p=p,pa,pe,pi,po,pu,p3,p0,pya,pye,pyi,pyo,pyu,py3,py0,pwa,pwe,pwi,pwo,pwu,pw3,pw0",
            "b=b,ba,be,bi,bo,bu,b3,b0,bya,bye,byi,byo,byu,by3,by0,bwa,bwe,bwi,bwo,bwu,bw3,bw0",
            "t=t,ta,te,ti,to,tu,t3,t0,tya,tye,tyi,tyo,tyu,ty3,ty0,twa,twe,twi,two,twu,tw3,tw0",
            "d=d,da,de,di,do,du,d3,d0,dya,dye,dyi,dyo,dyu,dy3,dy0,dwa,dwe,dwi,dwo,dwu,dw3,dw0",
            "k=k,ka,ke,ki,ko,ku,k3,k0,kya,kye,kyi,kyo,kyu,ky3,ky0,kwa,kwe,kwi,kwo,kwu,kw3,kw0",
            "g=g,ga,ge,gi,go,gu,g3,g0,gya,gye,gyi,gyo,gyu,gy3,gy0,gwa,gwe,gwi,gwo,gwu,gw3,gw0",
            "ts=ts,tsa,tse,tsi,tso,tsu,ts3,ts0,tsya,tsye,tsyi,tsyo,tsyu,tsy3,tsy0,tswa,tswe,tswi,tswo,tswu,tsw3,tsw0",
            "dz=dz,dza,dze,dzi,dzo,dzu,dz3,dz0,dzya,dzye,dzyi,dzyo,dzyu,dzy3,dzy0,dzwa,dzwe,dzwi,dzwo,dzwu,dzw3,dzw0",
            "tS=tS,tSa,tSe,tSi,tSo,tSu,tS3,tS0,tSya,tSye,tSyi,tSyo,tSyu,tSy3,tSy0,tSwa,tSwe,tSwi,tSwo,tSwu,tSw3,tSw0",
            "dZ=dZ,dZa,dZe,dZi,dZo,dZu,dZ3,dZ0,dZya,dZye,dZyi,dZyo,dZyu,dZy3,dZy0,dZwa,dZwe,dZwi,dZwo,dZwu,dZw3,dZw0",
            "f=f,fa,fe,fi,fo,fu,f3,f0,fya,fye,fyi,fyo,fyu,fy3,fy0,fwa,fwe,fwi,fwo,fwu,fw3,fw0",
            "v=v,va,ve,vi,vo,vu,v3,v0,vya,vye,vyi,vyo,vyu,vy3,vy0,vwa,vwe,vwi,vwo,vwu,vw3,vw0",
            "s=s,sa,se,si,so,su,s3,s0,sya,sye,syi,syo,syu,sy3,sy0,swa,swe,swi,swo,swu,sw3,sw0",
            "z=z,za,ze,zi,zo,zu,z3,z0,zya,zye,zyi,zyo,zyu,zy3,zy0,zwa,zwe,zwi,zwo,zwu,zw3,zw0",
            "S=S,Sa,Se,Si,So,Su,S3,S0,Sya,Sye,Syi,Syo,Syu,Sy3,Sy0,Swa,Swe,Swi,Swo,Swu,Sw3,Sw0",
            "r=r,ra,re,ri,ro,ru,r3,r0,rya,rye,ryi,ryo,ryu,ry3,ry0,rwa,rwe,rwi,rwo,rwu,rw3,rw0",
            "l=l,la,le,li,lo,lu,l3,l0,lya,lye,lyi,lyo,lyu,ly3,ly0,lwa,lwe,lwi,lwo,lwu,lw3,lw0",
            "j=j,ja,je,ji,jo,ju,j3,j0,jya,jye,jyi,jyo,jyu,jy3,jy0,jwa,jwe,jwi,jwo,jwu,jw3,jw0",
            "y=y,ya,ye,yi,yo,yu,y3,y0,ywa,ywe,ywi,ywo,ywu,yw3,yw0",
            "w=w,wa,we,wi,wo,wu,w3,w0",
            "n=n,na,ne,ni,no,nu,n3,n0,nya,nye,nyi,nyo,nyu,ny3,ny0,nwa,nwe,nwi,nwo,nwu,nw3,nw0",
            "m=m,ma,me,mi,mo,mu,m3,m0,mya,mye,myi,myo,myu,my3,my0,mwa,mwe,mwi,mwo,mwu,mw3,mw0",
            "gn=gn,gna,gne,gni,gno,gnu,gn3,gn0",
            "N=N,Na,Ne,Ni,No,Nu,N3,N0,Nya,Nye,Nyi,Nyo,Nyu,Ny3,Ny0,Nwa,Nwe,Nwi,Nwo,Nwu,Nw3,Nw0",
            "M=M,Ma,Me,Mi,Mo,Mu,M3,M0,Mya,Mye,Myi,Myo,Myu,My3,My0,Mwa,Mwe,Mwi,Mwo,Mwu,Mw3,Mw0",
            "R=R",
            "-=-"
        };

        static readonly Dictionary<string, string> vowelLookup;
        static readonly Dictionary<string, string> consonantLookup;

        static ItalianCVVCPhonemizer() {
            vowelLookup = vowels.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            consonantLookup = consonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        // Store singer in field, will try reading presamp.ini later
        private USinger singer;
        public override void SetSinger(USinger singer) => this.singer = singer;

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentUnicode = ToUnicodeElements(note.lyric);
            var currentLyric = note.lyric;
            var attr0 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            var attr1 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;

            if (prevNeighbour == null) {
                // Use "- V" or "- CV" if present in voicebank
                var initial = $"-{currentLyric}";
                if (singer.TryGetMappedOto(initial, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto)) {
                    currentLyric = oto.Alias;
                }
            } else if (plainVowels.Contains(currentLyric)) {
                var prevUnicode = ToUnicodeElements(prevNeighbour?.lyric);
                // Current note is VV
                if (vowelLookup.TryGetValue(prevUnicode.LastOrDefault() ?? string.Empty, out var vow)) {
                    currentLyric = $"{vow} {currentLyric}";
                    if (singer.TryGetMappedOto(currentLyric, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto)) {
                        currentLyric = oto.Alias;
                    }
                }
            } else if (singer.TryGetMappedOto(currentLyric, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto)) {
                currentLyric = oto.Alias;
            }

            if (nextNeighbour != null) {

                var nextUnicode = ToUnicodeElements(nextNeighbour?.lyric);
                var nextLyric = string.Join("", nextUnicode);

                // Check if next note is a vowel and does not require VC
                if (nextUnicode.Count < 2 && plainVowels.Contains(nextUnicode.FirstOrDefault() ?? string.Empty)) {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = currentLyric,
                            }
                        },
                    };
                }

                // Insert VC before next neighbor
                // Get vowel from current note

                var vowel = "";
                if (vowelLookup.TryGetValue(currentUnicode.LastOrDefault() ?? string.Empty, out var vow)) {
                    vowel = vow;
                }

                // Get consonant from next note
                var consonant = "";
                if (consonantLookup.TryGetValue(nextUnicode.FirstOrDefault() ?? string.Empty, out var con)
                    || nextUnicode.Count >= 2 && consonantLookup.TryGetValue(string.Join("", nextUnicode.Take(2)), out con)) {
                    consonant = con;
                }


                if (consonant == "") {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = currentLyric,
                            }
                        },
                    };
                }

                var vcPhoneme = $"{vowel} {consonant}";
                if (singer.TryGetMappedOto(vcPhoneme, note.tone + (attr1.toneShift ?? GetParentToneShift()), attr1.voiceColor ?? GetParentVoiceColor(), out var oto1)) {
                    vcPhoneme = oto1.Alias;
                } else {
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = currentLyric,
                            }
                        },
                    };
                }

                int totalDuration = notes.Sum(n => n.duration);
                int vcLength = 120;
                var nextAttr = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                if (singer.TryGetMappedOto(nextLyric, nextNeighbour.Value.tone + (nextAttr.toneShift ?? GetParentToneShift()), nextAttr.voiceColor ?? GetParentVoiceColor(), out var oto)) {
                    vcLength = MsToTick(oto.Preutter);
                }
                vcLength = Math.Min(totalDuration / 2, vcLength);

                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = currentLyric,
                        },
                        new Phoneme() {
                            phoneme = vcPhoneme,
                            position = totalDuration - vcLength,
                        }
                    },
                };
            }

            // No next neighbor
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = currentLyric,
                    }
                },
            };
        }
    }
}
