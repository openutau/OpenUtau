using System;
using System.Collections.Generic;
using System.Linq;
using Classic;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

#if DEBUG
namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("Presamp Sample Phonemizer", "ZH CVVC", language: "ZH")]
    public class PresampSamplePhonemizer : Phonemizer {
        // Supporting: [VOWEL][CONSONANT][PRIORITY][REPLACE][ALIAS(VCPAD,VCVPAD)]

        private USinger singer;
        private Presamp presamp;
        public override void SetSinger(USinger singer) {
            if (this.singer == singer) {
                return;
            }
            this.singer = singer;
            if (this.singer == null) {
                return;
            }

            presamp = new Presamp();
            presamp.SetVowels(defVowels);
            presamp.SetConsonants(defConsonants);
            presamp.Replace.Clear();
            presamp.Priorities = new List<string> { "k", "g", "t", "d", "b", "p" };
            presamp.AliasRules.ENDING1 = "_%v%"; // not supported yet
            presamp.AddEnding = 1; // not supported yet

            // Read ini after preparing default values for the phonemizer
            presamp.ReadPresampIni(singer.Location, singer.TextFileEncoding);
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var lyric = notes[0].lyric;
            foreach (var pair in presamp.Replace) { // replace (exact match)
                if (pair.Key == lyric) {
                    lyric = pair.Value;
                }
            }

            string consonant = lyric;
            if (presamp.PhonemeList.TryGetValue(lyric, out PresampPhoneme currentPhoneme)) {
                consonant = currentPhoneme.Consonant;
            }
            string prevVowel = "-";
            if (prevNeighbour != null) {
                var prevLyric = prevNeighbour.Value.lyric;
                if (presamp.PhonemeList.TryGetValue(prevLyric, out PresampPhoneme prevPhoneme)) {
                    prevVowel = prevPhoneme.Vowel;
                }
            };
            string vcpad = presamp.AliasRules.VCPAD;

            var attr0 = notes[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            var attr1 = notes[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;
            if (lyric == "-" || lyric.ToLowerInvariant() == "r") {
                if (singer.TryGetMappedOto($"{prevVowel}{vcpad}R", notes[0].tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto1)) {
                    return MakeSimpleResult(oto1.Alias);
                }
                return MakeSimpleResult($"{prevVowel}{vcpad}R");
            }
            int totalDuration = notes.Sum(n => n.duration);
            if (singer.TryGetMappedOto($"{prevVowel}{vcpad}{lyric}", notes[0].tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto)) {
                return MakeSimpleResult(oto.Alias);
            }
            int vcLen = 120;
            if (singer.TryGetMappedOto(lyric, notes[0].tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var cvOto)) {
                if (cvOto.Overlap < 0) {
                    vcLen = MsToTick(cvOto.Preutter - cvOto.Overlap);
                } else {
                    vcLen = MsToTick(cvOto.Preutter);
                }
                vcLen = Convert.ToInt32(Math.Min(totalDuration / 2, vcLen * (attr0.consonantStretchRatio ?? GetParentConsonantStretchRatio())));
            }
            if (singer.TryGetMappedOto($"{prevVowel}{vcpad}{consonant}", notes[0].tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out oto)) {
                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = oto.Alias,
                            position = - vcLen,
                        },
                        new Phoneme() {
                            phoneme = cvOto?.Alias ?? lyric,
                        },
                    },
                };
            }
            return MakeSimpleResult(cvOto?.Alias ?? lyric);
        }

        // Citation: https://delta-kimigatame.hatenablog.jp/entry/ar591802 CVVChinese用
        private readonly static List<string> defVowels = new List<string> {
            { "ang=ang=ang,bang,pang,mang,fang,dang,tang,nang,lang,gang,kang,hang,zhang,chang,shang,rang,zang,cang,sang,yang,liang,jiang,qiang,xiang,wang,guang,kuang,huang,zhuang,chuang,shuang,niang=100" },
            { "ei=ei=ei,bei,pei,mei,fei,dei,tei,nei,lei,gei,kei,hei,zhei,shei,zei,wei,dui,tui,gui,kui,hui,zhui,chui,shui,rui,zui,cui,sui=100" },
            { "ai=ai=ai,bai,pai,mai,dai,tai,nai,lai,gai,kai,hai,zhai,chai,shai,zai,cai,sai,wai,guai,kuai,huai,zhuai,chuai,shuai=100" },
            { "ir=ir=zhi,chi,shi,ri=100" },
            { "ong=ong=ong,dong,tong,nong,long,gong,kong,hong,zhong,chong,rong,zong,cong,song,yong,jiong,qiong,xiong=100" },
            { "ao=ao=ao,bao,pao,mao,dao,tao,nao,lao,gao,kao,hao,zhao,chao,shao,rao,zao,cao,sao,yao,biao,piao,miao,diao,tiao,niao,liao,jiao,qiao,xiao=100" },
            { "an=an=an,ban,pan,man,fan,dan,tan,nan,lan,gan,kan,han,zhan,chan,shan,ran,zan,can,san,wan,duan,tuan,nuan,luan,guan,kuan,huan,zhuan,chuan,shuan,ruan,zuan,cuan,suan=100" },
            { "en=en=en,ben,pen,men,fen,nen,gen,ken,hen,zhen,chen,shen,ren,zen,cen,sen,wen,dun,tun,lun,gun,kun,hun,zhun,chun,shun,run,zun,cun,sun=100" },
            { "in=in=yin,bin,pin,min,nin,lin,jin,qin,xin=100" },
            { "ing=ing=ying,bing,ping,ming,ding,ting,ning,ling,jing,qing,xing=100" },
            { "er=er=er=100" },
            { "eng=eng=eng,beng,peng,meng,feng,deng,teng,neng,leng,geng,keng,heng,weng,zheng,cheng,sheng,reng,zeng,ceng,seng=100" },
            { "i0=i0=zi,ci,si=100" },
            { "vn=vn=yun,jun,qun,xun=100" },
            { "e0=e0=ye,bie,pie,mie,die,tie,nie,lie,jie,qie,xie=100" },
            { "a=a=a,ba,pa,ma,fa,da,ta,na,la,ga,ka,ha,zha,cha,sha,za,ca,sa,ya,lia,jia,qia,xia,wa,gua,kua,hua,zhua,shua,dia=100" },
            { "e=e=e,me,de,te,ne,le,ge,ke,he,zhe,che,she,re,ze,ce,se=100" },
            { "i=i=i,bi,pi,mi,di,ti,ni,li,ji,qi,xi,yi=100" },
            { "o=o=o,bo,po,mo,fo,wo,duo,tuo,nuo,luo,guo,kuo,huo,zhuo,chuo,shuo,ruo,zuo,cuo,suo=100" },
            { "en0=en0=yan,bian,pian,mian,dian,tian,nian,lian,jian,qian,xian,yuan,juan,quan,xuan=100" },
            { "u=u=u,bu,pu,mu,fu,du,tu,nu,lu,gu,ku,hu,zhu,chu,shu,ru,zu,cu,su,wu=100" },
            { "v=v=yu,nv,lv,ju,qu,xu=100" },
            { "ue=ue=yue,nue,lue,jue,que,xue=100" },
            { "ou=ou=ou,pou,mou,fou,dou,tou,lou,gou,kou,hou,zhou,chou,shou,rou,zou,cou,sou,you,miu,diu,niu,liu,jiu,qiu,xiu=100" }
        };
        private readonly static List<string> defConsonants = new List<string> {
            { "ch=cha,chang,chao,chai,chan,chong,chou,che,chen,cheng,chi=1" },
            { "zh=zha,zhang,zhao,zhai,zhan,zhong,zhou,zhe,zhen,zheng,zhei,zhi=1" },
            { "sw=suan,suo,sun,sui,su=0" },
            { "xy=xia,xiang,xiao,xiong,xiu,xie,xi,xin,xing,xian=0" },
            { "zw=zuan,zuo,zun,zui,zu=1" },
            { "cw=cuan,cuo,cun,cui,cu=1" },
            { "xw=xue,xu,xun,xuan=0" },
            { "ny=niang,niao,niu,nie,ni,nin,ning,nian=0" },
            { "r=rang,rao,ran,ruan,ruo,rong,rou,re,ren,run,reng,rui,ru,ri=0" },
            { "zhw=zhua,zhuang,zhuai,zhuan,zhuo,zhun,zhui,zhu=1" },
            { "chw=chuang,chuai,chuan,chuo,chun,chui,chu=1" },
            { "ly=lia,liang,liao,liu,lie,li,lin,ling,lian=0" },
            { "jy=jia,jiang,jiao,jiong,jiu,jie,ji,jin,jing,jian=1" },
            { "jw=jue,ju,jun,juan=1" },
            { "hw=hua,huang,huai,huan,huo,hun,hui,hu=0" },
            { "qw=que,qu,qun,quan=1" },
            { "c=ca,cang,cao,cai,can,cong,cou,ce,cen,ceng,ci=1" },
            { "b=ba,bang,bao,biao,bai,ban,bo,ben,beng,bei,bie,bu,bi,bin,bing,bian=1" },
            { "d=da,dia,dang,dao,diao,dai,dan,duan,duo,dong,dou,diu,de,dun,deng,dei,dui,die,du,di,ding,dian=1" },
            { "g=ga,gua,gang,guang,gao,gai,guai,gan,guan,guo,gong,gou,ge,gen,gun,geng,gei,gui,gu=1" },
            { "f=fa,fang,fan,fo,fou,fen,feng,fei,fu=0" },
            { "qy=qia,qiang,qiao,qiong,qiu,qie,qi,qin,qing,qian=1" },
            { "h=ha,hang,hao,hai,han,hong,hou,he,hen,heng,hei=0" },
            { "k=ka,kua,kang,kuang,kao,kai,kuai,kan,kuan,kuo,kong,kou,ke,ken,kun,keng,kei,kui,ku=1" },
            { "shw=shua,shuang,shuai,shuan,shuo,shun,shui,shu=0" },
            { "m=ma,mang,mao,mai,man,mo,mou,me,men,meng,mei,mu=0" },
            { "l=la,lang,lao,lai,lan,luan,luo,long,lou,le,lun,leng,lei,lue,lu,lv=0" },
            { "n=na,nang,nao,nai,nan,nuan,nuo,nong,ne,nen,neng,nei,nue,nu,nv=0" },
            { "p=pa,pang,pao,piao,pai,pan,po,pou,pen,peng,pei,pie,pu,pi,pin,ping,pian=1" },
            { "s=sa,sang,sao,sai,san,song,sou,se,sen,seng,si=1" },
            { "sh=sha,shang,shao,shai,shan,shou,she,shen,sheng,shei,shi=1" },
            { "t=ta,tang,tao,tiao,tai,tan,tuan,tuo,tong,tou,te,tun,teng,tei,tui,tie,tu,ti,ting,tian=1" },
            { "w=wa,wang,wai,wan,wo,wen,weng,wei,wu=0" },
            { "v=yue,yu,yun,yuan=0" },
            { "y=ya,yang,yao,yong,you,ye,i,yi,yin,ying,yan=0" },
            { "z=za,zang,zao,zai,zan,zong,zou,ze,zen,zeng,zei,zi=1" },
            { "my=miao,miu,mie,mi,min,ming,mian=0" }
        };
        //private readonly static Dictionary<string, string> defReplace = new Dictionary<string, string>();
    }
}
#endif
