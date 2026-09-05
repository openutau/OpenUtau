using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// 中文 VCV 音素器
    /// <para>支持直接输入汉字，自动转为无声调拼音后进行VCV拼接</para>
    /// <para>尾音规则严格匹配拼音-尾韵母对照表</para>
    /// </summary>
    [Phonemizer("Chinese VCV Phonemizer", "ZH VCV", "樗儿", language: "ZH")]
    public class ChineseVCVPhonemizer : Phonemizer {

        // 拼音 → 尾韵母 映射表
        // 格式：尾韵母=拼音1,拼音2,拼音3,...
        static readonly string[] tailMap = new string[] {
            "a=a,ba,pa,ma,fa,da,ta,na,la,ga,ka,ha,zha,cha,sha,za,ca,sa,ya,lia,jia,qia,xia,wa,gua,kua,hua,zhua,shua,dia",
            "ang=ang,bang,pang,mang,fang,dang,tang,nang,lang,gang,kang,hang,zhang,chang,shang,rang,zang,cang,sang,yang,liang,jiang,qiang,xiang,wang,guang,kuang,huang,zhuang,chuang,shuang,niang",
            "ao=ao,bao,pao,mao,dao,tao,nao,lao,gao,kao,hao,zhao,chao,shao,rao,zao,cao,sao,yao,biao,piao,miao,diao,tiao,niao,liao,jiao,qiao,xiao",
            "ai=ai,bai,pai,mai,dai,tai,nai,lai,gai,kai,hai,zhai,chai,shai,zai,cai,sai,wai,guai,kuai,huai,zhuai,chuai,shuai",
            "an=an,ban,pan,man,fan,dan,tan,nan,lan,gan,kan,han,zhan,chan,shan,ran,zan,can,san,wan,duan,tuan,nuan,luan,guan,kuan,huan,zhuan,chuan,shuan,ruan,zuan,cuan,suan",
            "o=o,bo,po,mo,fo,wo,duo,tuo,nuo,luo,guo,kuo,huo,zhuo,chuo,shuo,ruo,zuo,cuo,suo",
            "ong=ong,dong,tong,nong,long,gong,kong,hong,zhong,chong,rong,zong,cong,song,yong,jiong,qiong,xiong",
            "ou=ou,pou,mou,fou,dou,tou,lou,gou,kou,hou,zhou,chou,shou,rou,zou,cou,sou,you,miu,diu,niu,liu,jiu,qiu,xiu",
            "e=e,me,de,te,ne,le,ge,ke,he,zhe,che,she,re,ze,ce,se",
            "en=en,ben,pen,men,fen,nen,gen,ken,hen,zhen,chen,shen,ren,zen,cen,sen,wen,dun,tun,lun,gun,kun,hun,zhun,chun,shun,run,zun,cun,sun",
            "eng=eng,beng,peng,meng,feng,deng,teng,neng,leng,geng,keng,heng,weng,zheng,cheng,sheng,reng,zeng,ceng,seng",
            "ei=ei,bei,pei,mei,fei,dei,tei,nei,lei,gei,kei,hei,zhei,shei,zei,wei,dui,tui,gui,kui,hui,zhui,chui,shui,rui,zui,cui,sui",
            "ie=ye,bie,pie,mie,die,tie,nie,lie,jie,qie,xie",
            "ue=yue,nue,lue,jue,que,xue",
            "u=u,bu,pu,mu,fu,du,tu,nu,lu,gu,ku,hu,zhu,chu,shu,ru,zu,cu,su,wu",
            "v=yu,nv,lv,ju,qu,xu",
            "vn=yun,jun,qun,xun",
            "i=i,bi,pi,mi,di,ti,ni,li,ji,qi,xi,yi",
            "in=yin,bin,pin,min,nin,lin,jin,qin,xin",
            "ing=ying,bing,ping,ming,ding,ting,ning,ling,jing,qing,xing",
            "ir=zhi,chi,shi,ri",
            "iz=zi,ci,si",
            "er=er",
            "ian=yan,bian,pian,mian,dian,tian,nian,lian,jian,qian,xian,yuan,juan,quan,xuan",
        };

        static readonly Dictionary<string, string> tailLookup;

        private USinger? singer;

        /// <summary>
        /// 静态构造：将字符串映射表转为字典，提升查询性能
        /// </summary>
        static ChineseVCVPhonemizer() {
            tailLookup = tailMap
                .SelectMany(line => {
                    var parts = line.Split('=');
                    string tail = parts[0];
                    return parts[1].Split(',').Select(pinyin => (pinyin, tail));
                })
                .ToDictionary(t => t.pinyin, t => t.tail);
        }

        /// <summary>
        /// 设置歌手实例
        /// </summary>
        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        /// <summary>
        /// 初始化阶段：批量将汉字转为无声调拼音
        /// 复用 BaseChinesePhonemizer 的罗马化能力，与官方中文音素器行为一致
        /// </summary>
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            BaseChinesePhonemizer.RomanizeNotes(groups);
        }

        /// <summary>
        /// 核心处理：将音符转换为 VCV 格式的音素
        /// </summary>
        public override Result Process(
            Note[] notes,
            Note? prev,
            Note? next,
            Note? prevNeighbour,
            Note? nextNeighbour,
            Note[] prevs) {

            var note = notes[0];
            string currentLyric = note.lyric.Normalize();

            // 1. 音素提示优先（强制覆盖）
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                string hint = note.phoneticHint.Normalize();
                if (CheckOtoUntilHit(new string[] { hint }, note, out var ph)) {
                    return MakeSimpleResult(ph.Alias);
                }
                return MakeSimpleResult(hint);
            }

            // 2. 提取纯拼音
            string currentPure = ExtractPurePinyin(currentLyric);

            // 3. 连音符原样透传
            if (currentPure == "+") {
                return MakeSimpleResult("+");
            }

            // 4. 生成候选匹配列表（按优先级）
            var candidates = new List<string>();

            if (prevNeighbour.HasValue) {
                // 提取前一个音的纯拼音
                string prevLyric = prevNeighbour.Value.lyric.Normalize();
                if (!string.IsNullOrEmpty(prevNeighbour.Value.phoneticHint)) {
                    prevLyric = prevNeighbour.Value.phoneticHint.Normalize();
                }
                string prevPure = ExtractPurePinyin(prevLyric);

                // 查表获取前音尾韵母，生成 VCV 格式
                if (tailLookup.TryGetValue(prevPure, out string? tail) && !string.IsNullOrEmpty(tail)) {
                    candidates.Add($"{tail} {currentPure}");  // 优先级1：精确VCV
                    candidates.Add($"* {currentPure}");       // 优先级2：通配符
                }
            }

            candidates.Add($"- {currentPure}");  // 优先级3：开头格式
            candidates.Add(currentPure);          // 优先级4：纯拼音兜底

            // 5. 按优先级匹配 OTO
            if (CheckOtoUntilHit(candidates.ToArray(), note, out var oto)) {
                return MakeSimpleResult(oto.Alias);
            }

            // 6. 全部失败：返回原拼音保底
            return MakeSimpleResult(currentPure);
        }

        // 辅助方法

        /// <summary>
        /// 从歌词中提取纯拼音
        /// <list type="bullet">
        /// <item>去掉开头的 "-" 前缀（如 "-tian" → "tian"）</item>
        /// <item>含空格时取最后一段（如 "ian bu" → "bu"）</item>
        /// <item>连音符 "+" 原样保留</item>
        /// </list>
        /// </summary>
        private string ExtractPurePinyin(string lyric) {
            if (string.IsNullOrWhiteSpace(lyric)) {
                return string.Empty;
            }
            string result = lyric.Trim();

            // 去掉 "-" 前缀
            if (result.StartsWith("-")) {
                result = result.Substring(1).Trim();
            }

            // 含空格时取最后一段（兼容VCV格式歌词）
            if (result.Contains(' ')) {
                result = result.Split(' ').Last().Trim();
            }

            return result;
        }

        /// <summary>
        /// 按优先级依次匹配 OTO，返回第一个命中项
        /// 处理：备用索引、音高偏移、音色、多音阶映射
        /// </summary>
        private bool CheckOtoUntilHit(string[] inputs, Note note, out UOto matchedOto) {
            matchedOto = default;

            if (singer == null) {
                return false;
            }

            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == 0) ?? default;
            string color = attr.voiceColor ?? string.Empty;
            int toneShift = attr.toneShift ?? 0;
            int? alt = attr.alternate;

            var results = new List<UOto>();

            foreach (string input in inputs) {
                // 先尝试带备用索引的别名
                if (alt.HasValue) {
                    string altAlias = input + alt.Value;
                    if (singer.TryGetMappedOto(altAlias, note.tone + toneShift, color, out var otoAlt)) {
                        results.Add(otoAlt);
                    }
                }
                // 再尝试普通别名
                if (singer.TryGetMappedOto(input, note.tone + toneShift, color, out var oto)) {
                    results.Add(oto);
                }
            }

            if (results.Count == 0) {
                return false;
            }

            // 优先选择音色完全匹配的
            matchedOto = results.FirstOrDefault(o => o.IsColorMatch(color));
            if (matchedOto == null) {
                matchedOto = results[0];
            }
            return true;
        }
    }
}