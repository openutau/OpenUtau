using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Chinese 十月式整音扩张 CVV Phonemizer.
    /// <para>It works by spliting "duang" to "duang" + "_ang", to produce the proper tail sound.</para>
    /// </summary>
    [Phonemizer("Chinese CVV (十月式整音扩张) Phonemizer", "ZH CVV", language: "ZH")]
    public class ChineseCVVMonophonePhonemizer : MonophonePhonemizer
    {
        static readonly string pinyins = "a,ai,an,ang,ao,ba,bai,ban,bang,bao,bei,ben,beng,bi,bian,biao,bie,bin,bing,bo,bu,ca,cai,can,cang,cao,ce,cei,cen,ceng,cha,chai,chan,chang,chao,che,chen,cheng,chi,chong,chou,chu,chua,chuai,chuan,chuang,chui,chun,chuo,ci,cong,cou,cu,cuan,cui,cun,cuo,da,dai,dan,dang,dao,de,dei,den,deng,di,dia,dian,diao,die,ding,diu,dong,dou,du,duan,dui,dun,duo,e,ei,en,eng,er,fa,fan,fang,fei,fen,feng,fo,fou,fu,ga,gai,gan,gang,gao,ge,gei,gen,geng,gong,gou,gu,gua,guai,guan,guang,gui,gun,guo,ha,hai,han,hang,hao,he,hei,hen,heng,hong,hou,hu,hua,huai,huan,huang,hui,hun,huo,ji,jia,jian,jiang,jiao,jie,jin,jing,jiong,jiu,ju,jv,juan,jvan,jue,jve,jun,jvn,ka,kai,kan,kang,kao,ke,kei,ken,keng,kong,kou,ku,kua,kuai,kuan,kuang,kui,kun,kuo,la,lai,lan,lang,lao,le,lei,leng,li,lia,lian,liang,liao,lie,lin,ling,liu,lo,long,lou,lu,luan,lun,luo,lv,lve,ma,mai,man,mang,mao,me,mei,men,meng,mi,mian,miao,mie,min,ming,miu,mo,mou,mu,na,nai,nan,nang,nao,ne,nei,nen,neng,ni,nian,niang,niao,nie,nin,ning,niu,nong,nou,nu,nuan,nun,nuo,nv,nve,o,ou,pa,pai,pan,pang,pao,pei,pen,peng,pi,pian,piao,pie,pin,ping,po,pou,pu,qi,qia,qian,qiang,qiao,qie,qin,qing,qiong,qiu,qu,qv,quan,qvan,que,qve,qun,qvn,ran,rang,rao,re,ren,reng,ri,rong,rou,ru,rua,ruan,rui,run,ruo,sa,sai,san,sang,sao,se,sen,seng,sha,shai,shan,shang,shao,she,shei,shen,sheng,shi,shou,shu,shua,shuai,shuan,shuang,shui,shun,shuo,si,song,sou,su,suan,sui,sun,suo,ta,tai,tan,tang,tao,te,tei,teng,ti,tian,tiao,tie,ting,tong,tou,tu,tuan,tui,tun,tuo,wa,wai,wan,wang,wei,wen,weng,wo,wu,xi,xia,xian,xiang,xiao,xie,xin,xing,xiong,xiu,xu,xv,xuan,xvan,xue,xve,xun,xvn,ya,yan,yang,yao,ye,yi,yin,ying,yo,yong,you,yu,yv,yuan,yvan,yue,yve,yun,yvn,za,zai,zan,zang,zao,ze,zei,zen,zeng,zha,zhai,zhan,zhang,zhao,zhe,zhei,zhen,zheng,zhi,zhong,zhou,zhu,zhua,zhuai,zhuan,zhuang,zhui,zhun,zhuo,zi,zong,zou,zu,zuan,zui,zun";
        static readonly string tails = "_vn,_ing,_ong,_an,_ou,_er,_ao,_eng,_ang,_en,_en2,_ai,_iong,_in,_ei";
        
        static readonly string[] pinyinList = pinyins.Split(',');
        static readonly string[] tailList = tails.Split(',');

        public ChineseCVVMonophonePhonemizer() {
            ConsonantLength = 120;    
        }

        protected override IG2p LoadG2p() {
            var g2ps = new List<IG2p>();

            // Load dictionary from plugin folder.
            string path = Path.Combine(PluginDir, "zhcvv.yaml");
            if (File.Exists(path)) {
                g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(path)).Build());
            }

            // Load dictionary from singer folder.
            if (singer != null && singer.Found && singer.Loaded) {
                string file = Path.Combine(singer.Location, "zhcvv.yaml");
                if (File.Exists(file)) {
                    try {
                        g2ps.Add(G2pDictionary.NewBuilder().Load(File.ReadAllText(file)).Build());
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to load {file}");
                    }
                }
            }
            g2ps.Add(new ChineseCVVG2p());
            return new G2pFallbacks(g2ps.ToArray());
        }

        protected override Dictionary<string, string[]> LoadVowelFallbacks() {
            return "_un=_en;_uai=_ai".Split(';')
                .Select(entry => entry.Split('='))
                .ToDictionary(parts => parts[0], parts => parts[1].Split(','));
        }

        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            base.SetUp(groups, project, track);
            BaseChinesePhonemizer.RomanizeNotes(groups);
        }
    }
    
    class ChineseCVVG2p : IG2p{
        /// <summary>
        ///  The consonant table.
        /// </summary>
        static readonly string consonants = "b,p,m,f,d,t,n,l,g,k,h,j,q,x,z,c,s,zh,ch,sh,r,y,w";
        /// <summary>
        /// The vowel split table.
        /// </summary>
        static readonly string vowels = "ai=_ai,uai=_uai,an=_an,ian=_en2,uan=_an,van=_en2,ang=_ang,iang=_ang,uang=_ang,ao=_ao,iao=_ao,ou=_ou,iu=_ou,ong=_ong,iong=_ong,ei=_ei,ui=_ei,uei=_ei,en=_en,un=_un,uen=_un,eng=_eng,in=_in,ing=_ing,vn=_vn";

        static HashSet<string> cSet;
        static Dictionary<string, string> vDict;
        
        static ChineseCVVG2p() {
            cSet = new HashSet<string>(consonants.Split(','));
            vDict = vowels.Split(',')
                .Select(s => s.Split('='))
                .ToDictionary(a => a[0], a => a[1]);
        }

        public bool IsVowel(string phoneme){
            return !phoneme.StartsWith("_");
        }

        public bool IsGlide(string phoneme){
            return false;
        }

        public string[] Query(string lyric){
            // The overall logic is:
            // 1. Remove consonant: "duang" -> "uang".
            // 2. Lookup the trailing sound in vowel table: "uang" -> "_ang".
            string consonant = string.Empty;
            string vowel = string.Empty;
            if (lyric.Length > 2 && cSet.Contains(lyric.Substring(0, 2))) {
                // First try to find consonant "zh", "ch" or "sh", and extract vowel.
                consonant = lyric.Substring(0, 2);
                vowel = lyric.Substring(2);
            } else if (lyric.Length > 1 && cSet.Contains(lyric.Substring(0, 1))) {
                // Then try to find single character consonants, and extract vowel.
                consonant = lyric.Substring(0, 1);
                vowel = lyric.Substring(1);
            } else {
                // Otherwise the lyric is a vowel.
                vowel = lyric;
            }
            if ((vowel == "un" || vowel == "uan") && (consonant == "j" || consonant == "q" || consonant == "x" || consonant == "y")) {
                vowel = "v" + vowel.Substring(1);
            }

            if ((vowel == "an") && (consonant == "y")) {
                vowel = "ian";
            }
            if(vDict.TryGetValue(vowel, out var tail)){
                return new string[] { lyric, tail };
            }else{
                return new string[] { lyric };
            }
                        
        }
        public bool IsValidSymbol(string symbol){
            return true;
        }

        public string[] UnpackHint(string hint, char separator = ' ') {
            return hint.Split(separator)
                .ToArray();
        }
    }
}
