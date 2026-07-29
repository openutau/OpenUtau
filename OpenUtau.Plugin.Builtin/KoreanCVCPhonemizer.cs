using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("KoreanCVCPhonemizer", "KO CVC", "NANA", language:"KO")]

    public class KoreanCVCPhonemizer : BaseKoreanPhonemizer {

        static readonly string[] naPlainVowels = new string[] { "a", "e", "a", "e", "eo", "e", "eo", "e", "o", "a", "e", "e", "o", "u", "eo", "e", "i", "u", "eu", "i", "i" };

        static readonly string[] naConsonants = new string[] {
            "ㄱ:g","ㄲ:gg","ㄴ:n","ㄷ:d","ㄸ:dd","ㄹ:r","ㅁ:m","ㅂ:b","ㅃ:bb","ㅅ:s","ㅆ:ss","ㅇ:","ㅈ:j","ㅉ:jj","ㅊ:ch","ㅋ:k","ㅌ:t","ㅍ:p","ㅎ:h"
        };
        static readonly string[] naVowels = new string[] {
            "ㅏ:a","ㅐ:e","ㅑ:ya","ㅒ:ye","ㅓ:eo","ㅔ:e","ㅕ:yeo","ㅖ:ye","ㅗ:o","ㅘ:wa","ㅙ:we","ㅚ:we","ㅛ:yo","ㅜ:u","ㅝ:weo","ㅞ:we","ㅟ:wi","ㅠ:yu","ㅡ:eu","ㅢ:eui","ㅣ:i"
        };
        static readonly string[] naFinals = new string[] {
            ":","ㄱ:k","ㄲ:k","ㄳ:k","ㄴ:n","ㄵ:n","ㄶ:n","ㄷ:t","ㄹ:l","ㄺ:k","ㄻ:m","ㄼ:l","ㄽ:l","ㄾ:l","ㄿ:p","ㅀ:l","ㅁ:m","ㅂ:p","ㅄ:p","ㅅ:t","ㅆ:t","ㅇ:ng","ㅈ:t","ㅊ:t","ㅋ:k","ㅌ:t","ㅍ:p","ㅎ:t"
        };
        private const int hangeulStartIndex = 0xAC00;
        private const int hangeulEndIndex = 0xD7A3;

        // ======================================================================================


        static readonly string[] plainVowels = new string[] { "eu", "eo", "a", "i", "u", "e", "o", "er" };

        static readonly string[] plainDiphthongs = new string[] { "ya", "yeo", "yo", "yu", "ye", "wa", "weo", "wi", "we", "eui" };

        static readonly string[] vowels = new string[] {
            "eu=geu,neu,deu,reu,leu,meu,beu,seu,eu,jeu,cheu,keu,teu,peu,heu,ggeu,ddeu,bbeu,sseu,jjeu,feu,veu,zeu,theu,rreu",
            "eo=geo,neo,deo,reo,leo,meo,beo,seo,eo,jeo,cheo,keo,teo,peo,heo,ggeo,ddeo,bbeo,sseo,jjeo,feo,veo,zeo,theo,rreo,gyeo,nyeo,dyeo,ryeo,lyeo,myeo,byeo,syeo,yeo,jyeo,chyeo,kyeo,tyeo,pyeo,hyeo,ggyeo,ddyeo,bbyeo,ssyeo,jjyeo,fyeo,vyeo,zyeo,thyeo,gweo,nweo,dweo,rweo,lweo,mweo,bweo,sweo,weo,jweo,chweo,kweo,tweo,pweo,hweo,ggweo,ddweo,bbweo,ssweo,jjweo,fweo,vweo,zweo,thweo",
            "a=ga,na,da,ra,la,ma,ba,sa,a,ja,cha,ka,ta,pa,ha,gga,dda,bba,ssa,jja,fa,va,za,tha,rra,gya,nya,dya,rya,lya,mya,bya,sya,ya,jya,chya,kya,tya,pya,hya,ggya,ddya,bbya,ssya,jjya,fya,vya,zya,thya,gwa,nwa,dwa,rwa,lwa,mwa,bwa,swa,wa,jwa,chwa,kwa,twa,pwa,hwa,ggwa,ddwa,bbwa,sswa,jjwa,fwa,vwa,zwa,thwa",
            "e=ge,ne,de,re,le,me,be,se,e,je,che,ke,te,pe,he,gge,dde,bbe,sse,jje,fe,ve,ze,the,rre,gye,nye,dye,rye,lye,mye,bye,sye,ye,jye,chye,kye,tye,pye,hye,ggye,ddye,bbye,ssye,jjye,fye,vye,zye,thye,gwe,nwe,dwe,rwe,lwe,mwe,bwe,swe,we,jwe,chwe,kwe,twe,pwe,hwe,ggwe,ddwe,bbwe,sswe,jjwe,fwe,vwe,zwe,thwe",
            "i=gi,ni,di,ri,li,mi,bi,si,i,ji,chi,ki,ti,pi,hi,ggi,ddi,bbi,ssi,jji,fi,vi,zi,thi,rri,gwi,nwi,dwi,rwi,lwi,mwi,bwi,swi,wi,jwi,chwi,kwi,twi,pwi,hwi,ggwi,ddwi,bbwi,sswi,jjwi,fwi,vwi,zwi,thwi",
            "o=go,no,do,ro,lo,mo,bo,so,o,jo,cho,ko,to,po,ho,ggo,ddo,bbo,sso,jjo,fo,vo,zo,tho,rro,gyo,nyo,dyo,ryo,lyo,myo,byo,syo,yo,jyo,chyo,kyo,tyo,pyo,hyo,ggyo,ddyo,bbyo,ssyo,jjyo,fyo,vyo,zyo,thyo",
            "u=gu,nu,du,ru,lu,mu,bu,su,u,ju,chu,ku,tu,pu,hu,ggu,ddu,bbu,ssu,jju,fu,vu,zu,thu,rru,gyu,nyu,dyu,ryu,lyu,myu,byu,syu,yu,jyu,chyu,kyu,tyu,pyu,hyu,ggyu,ddyu,bbyu,ssyu,jjyu,fyu,vyu,zyu,thyu",
            "ng=ang,ing,ung,eng,ong,eung,eong",
            "n=an,in,un,en,on,eun,eon",
            "m=am,im,um,em,om,eum,eom",
            "l=al,il,ul,el,ol,eul,eol",
            "p=ap,ip,up,ep,op,eup,eop",
            "t=at,it,ut,et,ot,eut,eot",
            "k=ak,ik,uk,ek,ok,euk,eok",
            "er=er"
        };

        static readonly string[] consonants = new string[] {
            "gg=gg,gga,ggi,ggu,gge,ggo,ggeu,ggeo,ggya,ggyu,ggye,ggyo,ggyeo,ggwa,ggwi,ggwe,ggweo",
            "dd=dd,dda,ddi,ddu,dde,ddo,ddeu,ddeo,ddya,ddyu,ddye,ddyo,ddyeo,ddwa,ddwi,ddwe,ddweo",
            "bb=bb,bba,bbi,bbu,bbe,bbo,bbeu,bbeo,bbya,bbyu,bbye,bbyo,bbyeo,bbwa,bbwi,bbwe,bbweo",
            "ss=ss,ssa,ssi,ssu,sse,sso,sseu,sseo,ssya,ssyu,ssye,ssyo,ssyeo,sswa,sswi,sswe,ssweo",

    "f=f,fa,fi,fu,fe,fo,feu,feo,fya,fyu,fye,fyo,fyeo,fwa,fwi,fwe,fweo",
            "v=v,va,vi,vu,ve,vo,veu,veo,vya,vyu,vye,vyo,vyeo,vwa,vwi,vwe,vweo",
            "z=z,za,zi,zu,ze,zo,zeu,zeo,zya,zyu,zye,zyo,zyeo,zwa,zwi,zwe,zweo",
            "th=th,tha,thi,thu,the,tho,theu,theo,thya,thyu,thye,thyo,thyeo,thwa,thwi,thwe,thweo",
            "rr=rr,rra,rri,rru,rre,rro,rreu,rreo",

            "g=g,ga,gi,gu,ge,go,geu,geo,gya,gyu,gye,gyo,gyeo,gwa,gwi,gwe,gweo",
            "n=n,na,ni,nu,ne,no,neu,neo,nya,nyu,nye,nyo,nyeo,nwa,nwi,nwe,nweo",
            "d=d,da,di,du,de,do,deu,deo,dya,dyu,dye,dyo,dyeo,dwa,dwi,dwe,dweo",
            "r=r,ra,ri,ru,re,ro,reu,reo,rya,ryu,rye,ryo,ryeo,rwa,rwi,rwe,rweo",
            "m=m,ma,mi,mu,me,mo,meu,meo,mya,myu,mye,myo,myeo,mwa,mwi,mwe,mweo",
            "b=b,ba,bi,bu,be,bo,beu,beo,bya,byu,bye,byo,byeo,bwa,bwi,bwe,bweo",
            "s=s,sa,si,su,se,so,seu,seo,sya,syu,sye,syo,syeo,swa,swi,swe,sweo",
            "j=j,ja,ji,ju,je,jo,jeu,jeo,jya,jyu,jye,jyo,jyeo,jwa,jwi,jwe,jweo",
            "ch=ch,cha,chi,chu,che,cho,cheu,cheo,chya,chyu,chye,chyo,chyeo,chwa,chwi,chwe,chweo",
            "k=k,ka,ki,ku,ke,ko,keu,keo,kya,kyu,kye,kyo,kyeo,kwa,kwi,kwe,kweo",
            "t=t,ta,ti,tu,te,to,teu,teo,tya,tyu,tye,tyo,tyeo,twa,twi,twe,tweo",
            "p=p,pa,pi,pu,pe,po,peu,peo,pya,pyu,pye,pyo,pyeo,pwa,pwi,pwe,pweo",
            "h=h,ha,hi,hu,he,ho,heu,heo,hya,hyu,hye,hyo,hyeo,hwa,hwi,hwe,hweo"
            };

        static readonly Dictionary<string, string> vowelLookup;
        static readonly Dictionary<string, string> consonantLookup;

        string getConsonant(string str) {
            str = str.Replace('a', ' ');
            str = str.Replace('i', ' ');
            str = str.Replace('u', ' ');
            str = str.Replace('e', ' ');
            str = str.Replace('o', ' ');
            str = str.Replace('w', ' ');
            str = str.Replace('y', ' ');
            str = str.Trim();

            return str;
        }

        /// <summary>
        /// Apply Korean sandhi rules to Hangeul lyrics.
        /// </summary>
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            base.SetUp(groups, project, track);
            // variate lyrics 
            KoreanPhonemizerUtil.RomanizeNotes(groups, false);
        }

        bool isAlphaCon(string str) {
            if (str == "gg") { return true; }
            else if (str == "dd") { return true; }
            else if (str == "bb") { return true; }
            else if (str == "ss") { return true; }
            else if (str == "f") { return true; }
            else if (str == "v") { return true; }
            else if (str == "z") { return true; }
            else if (str == "th") { return true; }
            else if (str == "rr") { return true; }
            else if (str == "g") { return true; }
            else if (str == "n") { return true; }
            else if (str == "d") { return true; }
            else if (str == "r") { return true; }
            else if (str == "m") { return true; }
            else if (str == "b") { return true; }
            else if (str == "s") { return true; }
            else if (str == "j") { return true; }
            else if (str == "ch") { return true; }
            else if (str == "k") { return true; }
            else if (str == "t") { return true; }
            else if (str == "p") { return true; }
            else if (str == "h") { return true; }else { return false; }
        }

        static KoreanCVCPhonemizer() {
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


        // ======================================================================================


        private USinger singer;
        public override void SetSinger(USinger singer) => this.singer = singer;

        // make it quicker to check multiple oto occurrences at once rather than spamming if else if
            private bool checkOtoUntilHit(string[] input, Note note, out UOto oto){
                oto = default;

                var attr0 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                var attr1 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;

                foreach (string test in input){
                    if (singer.TryGetMappedOto(test, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out oto)){
                        return true;
                    }
                }

                return false;
            }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var note = notes[0];
            var currentUnicode = ToUnicodeElements(note.lyric); // 현재 가사의 유니코드
            string currentLyric = note.lyric; // 현재 가사
            var attr0 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            var attr1 = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 1) ?? default;
            int totalDuration = notes.Sum(n => n.duration);

            // 가사의 초성, 중성, 종성 분리
            // P(re)Lconsonant, PLvowel, PLfinal / C(urrent)Lconsonant, CLvowel, CLfinal / N(ext)Lconsonant, NLvowel, NLfinal

            int CLconsonant = 0;
            int CLvowel = 0;
            int CLfinal = 0; // 현재 노트의 consonant, vowel, final 인덱스
            string[] TCLtemp;
            string TCLconsonant = "";
            string TCLvowel = "";
            string TCLfinal = "";
            string TCLplainvowel = ""; // 현재 노트의 consonant, vowel, fina, 모음의 단순화

            int NLconsonant = 0;
            int NLvowel = 0;
            int NLfinal = 0;
            string[] TNLtemp;
            string TNLconsonant = "";
            string TNLvowel = "";
            string TNLfinal = "";
            string TNLplainvowel = "";

            int PLconsonant = 0;
            int PLvowel = 0;
            int PLfinal = 0;
            string[] TPLtemp;
            string TPLconsonant = "";
            string TPLvowel = "";
            string TPLfinal = "";
            string TPLplainvowel = "";
            string TPLplainfinal = "";

            bool currentHangeul = false;
            bool prevHangeul = false;
            bool nextHangeul = false;

            bool prevExist = false;
            bool nextExist = false;

            char firstCL, firstPL, firstNL;
            int lCL, lPL, lNL;
            int uCL, uPL, uNL;
            lCL = 0; lPL = 0; lNL = 0;

            var phoneticHint = RenderPhoneticHint(singer, notes[0], totalDuration);
            if (phoneticHint != null) {
                return (Result) phoneticHint;
            }


            // 현재 노트 첫번째 글자 확인
            firstCL = currentLyric[0];
            if (firstCL == 'ㄹ') { lCL = 1; firstCL = currentLyric[1]; }
            uCL = (int)firstCL;
            if ((uCL >= hangeulStartIndex) && (uCL <= hangeulEndIndex)) {
                currentHangeul = true;
                CLconsonant = (uCL - hangeulStartIndex) / (21 * 28);
                CLvowel = (uCL - hangeulStartIndex) % (21 * 28) / 28;
                CLfinal = (uCL - hangeulStartIndex) % 28;


                TCLtemp = naConsonants[CLconsonant].Split(":");
                TCLconsonant = TCLtemp[1];

                TCLtemp = naVowels[CLvowel].Split(":");
                TCLvowel = TCLtemp[1];
                TCLplainvowel = naPlainVowels[CLvowel];

                TCLtemp = naFinals[CLfinal].Split(":");
                TCLfinal = TCLtemp[1];

                // TCLconsonant : 현노트 초성    TCLvowel : 현노트 중성    TCLfinal : 현노트 종성

            }

            // 이전 노트 존재 여부 확인 + 이전 노트 첫번째 글자 확인
            if (prevNeighbour != null) {
                firstPL = (prevNeighbour?.lyric)[0]; // 가사 받아오기
                prevExist = true; // 이전 노트 존재한다 반짝
                if (firstPL == 'ㄹ') { lPL = 1; firstPL = (prevNeighbour?.lyric)[1]; } // ㄹㄹ 발음이다 반짝
                uPL = (int)firstPL; // 가사를 int로 변환

                if ((uPL >= hangeulStartIndex) && (uPL <= hangeulEndIndex)) {
                    prevHangeul = true;

                    PLconsonant = (uPL - hangeulStartIndex) / (21 * 28);
                    PLvowel = (uPL - hangeulStartIndex) % (21 * 28) / 28;
                    PLfinal = (uPL - hangeulStartIndex) % 28;


                    TPLtemp = naConsonants[PLconsonant].Split(":");
                    TPLconsonant = TPLtemp[1];

                    TPLtemp = naVowels[PLvowel].Split(":");
                    TPLvowel = TPLtemp[1];
                    TPLplainvowel = naPlainVowels[PLvowel];

                    TPLtemp = naFinals[PLfinal].Split(":");
                    TPLfinal = TPLtemp[1];
                    TPLplainfinal = TPLfinal;
                }
            }

            // 다음 노트 존재 여부 확인 + 다음 노트 첫번째 글자 확인
            if (nextNeighbour != null) {
                firstNL = (nextNeighbour?.lyric)[0];
                nextExist = true;
                if (firstNL == 'ㄹ') { lNL = 1; firstNL = (nextNeighbour?.lyric)[1]; }
                uNL = (int)firstNL;

                if ((uNL >= hangeulStartIndex) && (uNL <= hangeulEndIndex)) {
                    nextHangeul = true;

                    NLconsonant = (uNL - hangeulStartIndex) / (21 * 28);
                    NLvowel = (uNL - hangeulStartIndex) % (21 * 28) / 28;
                    NLfinal = (uNL - hangeulStartIndex) % 28;


                    TNLtemp = naConsonants[NLconsonant].Split(":");
                    TNLconsonant = TNLtemp[1];

                    TNLtemp = naVowels[NLvowel].Split(":");
                    TNLvowel = TNLtemp[1];
                    TNLplainvowel = naPlainVowels[NLvowel];

                    TNLtemp = naFinals[NLfinal].Split(":");
                    TNLfinal = TNLtemp[1];
                }
            }

            if (currentHangeul) {
                // 음운규칙 적용
                if (currentHangeul) {

                    // 1. 연음법칙 
                    string tempTCLconsonant = "";
                    string tempTCLfinal = "";
                    bool yeoneum = false;
                    bool yeoneum2 = false;

                    if (prevExist && prevHangeul && (CLconsonant == 11) && (TPLfinal != "")) {
                        int temp = PLfinal;
                        if (temp == 1) { TCLtemp = naConsonants[0].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 2) { TCLtemp = naConsonants[1].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 3) { TCLtemp = naConsonants[10].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 4) { TCLtemp = naConsonants[2].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 5) { TCLtemp = naConsonants[12].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 6) { TCLtemp = naConsonants[18].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 7) { TCLtemp = naConsonants[3].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 8) { TCLtemp = naConsonants[5].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 9) { TCLtemp = naConsonants[0].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 10) { TCLtemp = naConsonants[6].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 11) { TCLtemp = naConsonants[7].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 12) { TCLtemp = naConsonants[9].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 13) { TCLtemp = naConsonants[16].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 14) { TCLtemp = naConsonants[17].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 15) { TCLtemp = naConsonants[18].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 16) { TCLtemp = naConsonants[6].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 17) { TCLtemp = naConsonants[7].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 18) { TCLtemp = naConsonants[9].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 19) { TCLtemp = naConsonants[9].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 20) { TCLtemp = naConsonants[10].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 21) { tempTCLconsonant = ""; yeoneum = true; }
                        else if (temp == 22) { TCLtemp = naConsonants[12].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 23) { TCLtemp = naConsonants[14].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 24) { TCLtemp = naConsonants[15].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 25) { TCLtemp = naConsonants[16].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 26) { TCLtemp = naConsonants[17].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                        else if (temp == 27) { TCLtemp = naConsonants[18].Split(":"); tempTCLconsonant = TCLtemp[1]; yeoneum = true; }
                    }

                    if (nextExist && nextHangeul && (TCLfinal != "") && (TNLconsonant == "")) {
                        int temp = CLfinal;

                        if (temp == 1) { TCLtemp = naConsonants[0].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 2) { TCLtemp = naConsonants[1].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 3) { TCLfinal = "k"; yeoneum2 = true; }
                        else if (temp == 4) { TCLtemp = naConsonants[2].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 5) { TCLfinal = "n"; yeoneum2 = true; }
                        else if (temp == 6) { TCLfinal = "n"; yeoneum2 = true; }
                        else if (temp == 7) { TCLtemp = naConsonants[3].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 8) { TCLtemp = naConsonants[5].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 9) { TCLfinal = "l"; yeoneum2 = true; }
                        else if (temp == 10) { TCLfinal = "l"; yeoneum2 = true; }
                        else if (temp == 11) { TCLfinal = "l"; yeoneum2 = true; }
                        else if (temp == 12) { TCLfinal = "l"; yeoneum2 = true; }
                        else if (temp == 13) { TCLfinal = "l"; yeoneum2 = true; }
                        else if (temp == 14) { TCLfinal = "l"; yeoneum2 = true; }
                        else if (temp == 15) { TCLfinal = "l"; yeoneum2 = true; }
                        else if (temp == 16) { TCLtemp = naConsonants[6].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 17) { TCLtemp = naConsonants[7].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 18) { TCLfinal = "p"; yeoneum2 = true; }
                        else if (temp == 19) { TCLtemp = naConsonants[9].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                        else if (temp == 20) { TCLtemp = naConsonants[10].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             //else if (temp == 21) { TCLtemp = naConsonants[11].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             else if (temp == 22) { TCLtemp = naConsonants[12].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; } else if (temp == 23) { TCLtemp = naConsonants[14].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; } else if (temp == 24) { TCLtemp = naConsonants[15].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; } else if (temp == 25) { TCLtemp = naConsonants[16].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; } else if (temp == 26) { TCLtemp = naConsonants[17].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; } else if (temp == 27) { TCLtemp = naConsonants[18].Split(":"); tempTCLfinal = TCLtemp[1]; TCLfinal = ""; yeoneum2 = true; }

                    }
                    if (yeoneum) { TCLconsonant = tempTCLconsonant; }
                    if (yeoneum2) { TNLconsonant = tempTCLfinal; }


                    // 2. 격음화/유기음화/거센소리되기
                    if (prevExist && prevHangeul && (TPLfinal != "")) {
                        if (((PLfinal == 27) && (CLconsonant == 0)) || ((PLfinal == 6) && (CLconsonant == 0)) || ((PLfinal == 15) && (CLconsonant == 0))) { TCLconsonant = "k"; } else if (((PLfinal == 27) && (CLconsonant == 3)) || ((PLfinal == 6) && (CLconsonant == 3)) || ((PLfinal == 15) && (CLconsonant == 3))) { TCLconsonant = "t"; } else if (((PLfinal == 27) && (CLconsonant == 12)) || ((PLfinal == 6) && (CLconsonant == 12)) || ((PLfinal == 15) && (CLconsonant == 12))) { TCLconsonant = "ch"; } else if (((PLfinal == 27) && (CLconsonant == 9)) || ((PLfinal == 6) && (CLconsonant == 9)) || ((PLfinal == 15) && (CLconsonant == 9))) { TCLconsonant = "ss"; }

                        if ((PLfinal == 1) && (CLconsonant == 18)) { TCLconsonant = "k"; } else if ((PLfinal == 7) && (CLconsonant == 18)) { TCLconsonant = "t"; } else if ((PLfinal == 17) && (CLconsonant == 18)) { TCLconsonant = "p"; } else if ((PLfinal == 22) && (CLconsonant == 18)) { TCLconsonant = "ch"; }
                    }
                    if (nextExist && nextHangeul && (TCLfinal != "")) {
                        if ((NLconsonant == 0) && (CLfinal == 27)) { TCLfinal = ""; TNLconsonant = "k"; } else if ((NLconsonant == 0) && (CLfinal == 6)) { TCLfinal = "n"; TNLconsonant = "k"; } else if ((NLconsonant == 0) && (CLfinal == 15)) { TCLfinal = "l"; TNLconsonant = "k"; } else if ((NLconsonant == 3) && (CLfinal == 27)) { TCLfinal = ""; TNLconsonant = "t"; } else if ((NLconsonant == 3) && (CLfinal == 6)) { TCLfinal = "n"; TNLconsonant = "t"; } else if ((NLconsonant == 3) && (CLfinal == 15)) { TCLfinal = "l"; TNLconsonant = "t"; } else if ((NLconsonant == 12) && (CLfinal == 27)) { TCLfinal = ""; TNLconsonant = "ch"; } else if ((NLconsonant == 12) && (CLfinal == 6)) { TCLfinal = "n"; TNLconsonant = "ch"; } else if ((NLconsonant == 12) && (CLfinal == 15)) { TCLfinal = "l"; TNLconsonant = "ch"; } else if ((NLconsonant == 9) && (CLfinal == 27)) { TCLfinal = ""; TNLconsonant = "ss"; } else if ((NLconsonant == 9) && (CLfinal == 6)) { TCLfinal = "n"; TNLconsonant = "ss"; } else if ((NLconsonant == 9) && (CLfinal == 15)) { TCLfinal = "l"; TNLconsonant = "ss"; }

                        if ((NLconsonant == 2) && (CLfinal == 27)) { TCLfinal = "n"; }

                        if ((NLconsonant == 18) && (CLfinal == 1)) { TCLfinal = ""; TNLconsonant = "k"; } else if ((NLconsonant == 18) && (CLfinal == 7)) { TCLfinal = ""; TNLconsonant = "t"; } else if ((NLconsonant == 18) && (CLfinal == 17)) { TCLfinal = ""; TNLconsonant = "p"; } else if ((NLconsonant == 18) && (CLfinal == 22)) { TCLfinal = ""; TNLconsonant = "ch"; }
                    }


                    // 3. 음절의 끝소리 규칙 예외
                    if (nextExist && nextHangeul) {
                        /*
                        // ㄼ + 자음이 있을 때 => ㄼ : p
                        if ((CLfinal == 11) && (TCLconsonant != "")) { TCLfinal = "p"; }
                        */
                        // ㄺ + ㄱ => ㄺ : ㄹ
                        if ((CLfinal == 9) && (NLconsonant == 0)) { TCLfinal = "l"; }
                    }


                    // 4. 경음화/된소리되기
                    if (prevExist && prevHangeul && TPLfinal != "") {
                        // ㄱㄷㅂ + ㄱㄷㅂㅅㅈ = ㄲㄸㅃㅆㅉ
                        if (((TPLfinal == "k") && (CLconsonant == 0)) || ((TPLfinal == "t") && (CLconsonant == 0)) || ((TPLfinal == "p") && (CLconsonant == 0))) { TCLconsonant = "gg"; } else if (((TPLfinal == "k") && (CLconsonant == 3)) || ((TPLfinal == "t") && (CLconsonant == 3)) || ((TPLfinal == "p") && (CLconsonant == 3))) { TCLconsonant = "dd"; } else if (((TPLfinal == "k") && (CLconsonant == 7)) || ((TPLfinal == "t") && (CLconsonant == 7)) || ((TPLfinal == "p") && (CLconsonant == 7))) { TCLconsonant = "bb"; } else if (((TPLfinal == "k") && (CLconsonant == 9)) || ((TPLfinal == "t") && (CLconsonant == 9)) || ((TPLfinal == "p") && (CLconsonant == 9))) { TCLconsonant = "ss"; } else if (((TPLfinal == "k") && (CLconsonant == 12)) || ((TPLfinal == "t") && (CLconsonant == 12)) || ((TPLfinal == "p") && (CLconsonant == 12))) { TCLconsonant = "jj"; }

                        /* 
                        // 용언 어간 받침 ㄴㅁ + ㄱㄷㅅㅈ = ㄲㄸㅆㅉ
                        if(((TPLfinal=="n")&&(CLconsonant==0))|| ((TPLfinal == "m") && (CLconsonant == 0))) { TCLconsonant = "gg"; }
                        else if (((TPLfinal == "n") && (CLconsonant == 3)) || ((TPLfinal == "m") && (CLconsonant == 3))) { TCLconsonant = "dd"; }
                        else if (((TPLfinal == "n") && (CLconsonant == 9)) || ((TPLfinal == "m") && (CLconsonant == 9))) { TCLconsonant = "ss"; }
                        else if (((TPLfinal == "n") && (CLconsonant == 12)) || ((TPLfinal == "m") && (CLconsonant == 12))) { TCLconsonant = "jj"; }
                        */

                        // 관형사형 어미ㄹ / 한자어 ㄹ + ㄷㅅㅈ = ㄸㅆㅉ
                        if ((PLfinal == 8) && (CLconsonant == 3)) { TCLconsonant = "dd"; } else if ((PLfinal == 8) && (CLconsonant == 9)) { TCLconsonant = "ss"; } else if ((PLfinal == 8) && (CLconsonant == 12)) { TCLconsonant = "jj"; }

                        // 어간 받침 ㄼㄾ + ㄱㄷㅅㅈ = ㄲㄸㅆㅉ
                        if (((PLfinal == 11) && (CLconsonant == 0)) || ((PLfinal == 13) && (CLconsonant == 0))) { TCLconsonant = "gg"; } else if (((PLfinal == 11) && (CLconsonant == 3)) || ((PLfinal == 13) && (CLconsonant == 3))) { TCLconsonant = "dd"; } else if (((PLfinal == 11) && (CLconsonant == 9)) || ((PLfinal == 13) && (CLconsonant == 9))) { TCLconsonant = "ss"; } else if (((PLfinal == 11) && (CLconsonant == 12)) || ((PLfinal == 13) && (CLconsonant == 12))) { TCLconsonant = "jj"; }
                    }


                    // 5. 구개음화
                    if (prevExist && prevHangeul && (TPLfinal != "")) {
                        if ((PLfinal == 7) && (CLconsonant == 11) && (CLvowel == 20)) { TCLconsonant = "j"; } else if ((PLfinal == 25) && (CLconsonant == 11) && (CLvowel == 20)) { TCLconsonant = "ch"; } else if ((PLfinal == 13) && (CLconsonant == 11) && (CLvowel == 20)) { TCLconsonant = "ch"; } else if ((PLfinal == 7) && (CLconsonant == 18) && (CLvowel == 20)) { TCLconsonant = "ch"; }
                    }
                    if (nextExist && nextHangeul && (TCLfinal != "")) {
                        if ((CLfinal == 7) && (NLconsonant == 11) && (NLvowel == 20)) { TCLfinal = ""; } else if ((CLfinal == 25) && (NLconsonant == 11) && (NLvowel == 20)) { TCLfinal = ""; } else if ((CLfinal == 13) && (NLconsonant == 11) && (NLvowel == 20)) { TCLfinal = ""; } else if ((CLfinal == 7) && (NLconsonant == 18) && (NLvowel == 20)) { TCLfinal = ""; }

                    }


                    // 6. 비음화
                    if (prevExist && prevHangeul && (TPLfinal != "")) {
                        // 한자어 받침 ㅁㅇ + ㄹ = ㄴ
                        if (((TPLfinal == "m") && (CLconsonant == 5)) || ((TPLfinal == "ng") && (CLconsonant == 5))) { TCLconsonant = "n"; }

                        // 한자어 받침 ㄱㄷㅂ + ㄹ = ㅇㄴㅁ + ㄴ(1)
                        if (((TPLfinal == "k") && (CLconsonant == 5)) || ((TPLfinal == "t") && (CLconsonant == 5)) || ((TPLfinal == "p") && (CLconsonant == 5))) { TCLconsonant = "n"; }
                    }
                    if (nextExist && nextHangeul && (TCLfinal != "")) {
                        //받침 ㄱㄷㅂ + ㄴㅁ = ㅇㄴㅁ
                        if (((TCLfinal == "k") && (TNLconsonant == "n")) || ((TCLfinal == "k") && (TNLconsonant == "m"))) { TCLfinal = "ng"; } else if (((TCLfinal == "t") && (TNLconsonant == "n")) || ((TCLfinal == "t") && (TNLconsonant == "m"))) { TCLfinal = "n"; } else if (((TCLfinal == "p") && (TNLconsonant == "n")) || ((TCLfinal == "p") && (TNLconsonant == "m"))) { TCLfinal = "m"; }

                        // 한자어 받침 ㄱㄷㅂ + ㄹ = ㅇㄴㅁ + ㄴ(2)
                        if ((TCLfinal == "k") && (NLconsonant == 5)) { TCLfinal = "ng"; } else if ((TCLfinal == "t") && (NLconsonant == 5)) { TCLfinal = "n"; } else if ((TCLfinal == "p") && (NLconsonant == 5)) { TCLfinal = "m"; }
                    }


                    // 7. 유음화
                    if (prevExist && prevHangeul && (TPLfinal != "")) {
                        if (((PLfinal == 8) && (TCLconsonant == "n")) || ((PLfinal == 13) && (TCLconsonant == "n")) || ((PLfinal == 15) && (TCLconsonant == "n"))) { TCLconsonant = "r"; }
                    }
                    if (nextExist && nextHangeul && (TCLfinal != "")) {
                        if ((TCLfinal == "n") && (TNLconsonant == "r")) { TCLfinal = "l"; }
                    }



                    // 8. 받침 + ㄹ = ㄹㄹ
                    if (prevExist && prevHangeul && (TPLfinal != "")) { lCL = 1; }



                    // consonant에 변경 사항이 있을 때
                    if (prevExist && prevHangeul) {


                        // 비음화
                        // (1) ㄱ(ㄲㅋㄳㄺ)
                        //     ㄷ(ㅅ,ㅆ,ㅈ,ㅊ,ㅌ,ㅎ)
                        //     ㅂ(ㅍ,ㄼ,ㄿ,ㅄ)


                    }
                    // final에 변경 사항이 있을 때


                }

                string CV = (TCLconsonant + TCLvowel);
                string VC = "";

                if (nextExist && (TCLfinal == "") && nextHangeul && TNLconsonant == "gg") {
                    VC = TCLplainvowel + "k";
                } else if (nextExist && (TCLfinal == "") && nextHangeul && (TNLconsonant == "dd" || TNLconsonant == "jj")) {
                    VC = TCLplainvowel + "t";
                } else if (nextExist && (TCLfinal == "") && nextHangeul && TNLconsonant == "bb") {
                    VC = TCLplainvowel + "p";
                } else if (nextExist && (TCLfinal == "") && nextHangeul && (TNLconsonant != "gg" || TNLconsonant != "dd" || TNLconsonant != "bb")) {
                    VC = TCLplainvowel + " " + TNLconsonant;
                }    

                string FC = "";
                if (TCLfinal != "") { FC = TCLplainvowel + TCLfinal; }

                if (lCL == 1) { CV = CV.Replace("r", "l"); }

                // 만약 앞에 노트가 없다면
                if (!prevExist && TCLconsonant == "r") {
                    string[] tests = new string[] { $"- {CV}", $"l{TCLvowel}", CV, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        CV = oto.Alias;
                    }
                }
                else if (!prevExist && TCLconsonant != "r") {
                    string[] tests = new string[] { $"- {CV}", CV, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        CV = oto.Alias;
                    }
                }

                if (prevExist && TCLconsonant == "" && TPLfinal == "") {
                    string[] tests = new string[] { $"{TPLplainvowel} {CV}", CV, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        CV = oto.Alias;
                    }
                }
                
                if (prevExist && TCLconsonant == "" && TPLfinal == "ng") {
                    string[] tests = new string[] { $"ng{CV}", CV, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        CV = oto.Alias;
                    }
                }

                if (prevNeighbour != null && prevExist && !prevHangeul && TCLconsonant == "") {
                    var prevUnicode = ToUnicodeElements(prevNeighbour?.lyric);
                    var vowel = "";

                    var prevLyric = string.Join("", prevUnicode);

                    // Current note is VV
                    if (vowelLookup.TryGetValue(prevUnicode.LastOrDefault() ?? string.Empty, out var vow)) {
                        vowel = vow;

                        var mixVV = $"{vow} {CV}";

                        if (prevLyric.EndsWith("eo")) {
                            mixVV = $"eo {CV}";
                        } else if (prevLyric.EndsWith("eu")) {
                            mixVV = $"eu {CV}";
                        } else if (prevLyric.EndsWith("er")) {
                            mixVV = $"er {CV}";
                        }

                        // try vowlyric then currentlyric
                        string[] tests = new string[] { mixVV, CV, currentLyric };
                        if (checkOtoUntilHit(tests, note, out var oto)) {
                            CV = oto.Alias;
                        }
                    }

                }

                if (nextNeighbour != null) { // 다음에 노트가 있으면
                    var nextUnicode = ToUnicodeElements(nextNeighbour?.lyric);
                    var nextLyric = string.Join("", nextUnicode);

                    // Insert VC before next neighbor
                    // Get vowel from current note
                    var vowel = TCLplainvowel;

                    // Get consonant from next note
                    var consonant = "";
                    if (consonantLookup.TryGetValue(nextUnicode.FirstOrDefault() ?? string.Empty, out var con)) {
                        consonant = getConsonant(nextNeighbour?.lyric); //로마자만 가능
                        if (nextLyric.StartsWith("gg")) {
                            consonant = "k";
                        } else if (nextLyric.StartsWith("dd")) {
                            consonant = "t";
                        } else if (nextLyric.StartsWith("bb")) {
                            consonant = "p";
                        } else if (nextLyric.StartsWith("jj")) {
                            consonant = "t";
                        } else if ((!isAlphaCon(consonant))) { consonant = con; }
                    } else if (nextExist && nextHangeul) {
                        consonant = TNLconsonant;
                    } else if (nextLyric.StartsWith("ch")) { consonant = "ch"; }
                    else if (nextLyric.StartsWith("l")) {
                        consonant = "l";
                    }

                    if (!nextHangeul && (nextLyric.StartsWith("gg") || nextLyric.StartsWith("dd") || nextLyric.StartsWith("bb") || nextLyric.StartsWith("jj") || nextLyric.StartsWith("l"))) {
                        VC = TCLplainvowel + consonant;
                    } else if (!nextHangeul && (!nextLyric.StartsWith("gg") || !nextLyric.StartsWith("dd") || !nextLyric.StartsWith("bb") || !nextLyric.StartsWith("jj") || !nextLyric.StartsWith("l"))) {
                        VC = TCLplainvowel + " " + consonant;
                    }
                }

                // 만약 받침이 있다면
                if (FC != "") {
                    int fcLength = totalDuration / 3;
                    if ((TCLfinal == "k") || (TCLfinal == "p") || (TCLfinal == "t")) { fcLength = totalDuration / 2; }

                    if (singer.TryGetMappedOto(CV, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto1) && singer.TryGetMappedOto(FC, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto2)) {
                        CV = oto1.Alias;
                        FC = oto2.Alias;
                        return new Result {
                            phonemes = new Phoneme[] {
                                new Phoneme() {
                                    phoneme = CV,
                                },
                                new Phoneme() {
                                    phoneme = FC,
                                    position = totalDuration - fcLength,
                                }
                            },
                        };
                    }
                }


                // 만약 받침이 없다면
                if (TCLfinal == "") {
                    // 뒤에 노트가 있다면
                    if (nextExist) { if ((nextNeighbour?.lyric)[0] == 'ㄹ') { VC = TCLplainvowel + "l"; } }
                    if ((VC != "") && (TNLconsonant != "")) {
                        int vcLength = 60;
                        if ((TNLconsonant == "r") || (TNLconsonant == "h")) { vcLength = 30; }
                        else if (TNLconsonant == "s") { vcLength = totalDuration/3; }
                        else if ((TNLconsonant == "k") || (TNLconsonant == "t") || (TNLconsonant == "p") || (TNLconsonant == "ch")) { vcLength = totalDuration / 2; }
                        else if ((TNLconsonant == "gg") || (TNLconsonant == "dd") || (TNLconsonant == "bb") || (TNLconsonant == "ss") || (TNLconsonant == "jj")) { vcLength = totalDuration / 2; }
                        vcLength = Math.Min(totalDuration / 2, vcLength);

                        if (singer.TryGetMappedOto(CV, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto1) && singer.TryGetMappedOto(VC, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto2)) {
                            CV = oto1.Alias;
                            VC = oto2.Alias;
                            return new Result {
                                phonemes = new Phoneme[] {
                                    new Phoneme() {
                                        phoneme = CV,
                                    },
                                    new Phoneme() {
                                        phoneme = VC,
                                        position = totalDuration - vcLength,
                                    }
                                },
                            };
                        }
                    } else if (VC != "" && TNLconsonant == "") {
                        int vcLength = 60;
                        var nextAttr = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                        var nextUnicode = ToUnicodeElements(nextNeighbour?.lyric);
                        var nextLyric = string.Join("", nextUnicode);
                        if (singer.TryGetMappedOto(nextLyric, nextNeighbour.Value.tone + (nextAttr.toneShift ?? GetParentToneShift()), nextAttr.voiceColor ?? GetParentVoiceColor(), out var oto0)) {
                            vcLength = MsToTick(oto0.Preutter);
                            
                        }
                        vcLength = Math.Min(totalDuration / 2, vcLength);

                        if (singer.TryGetMappedOto(CV, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto1) && singer.TryGetMappedOto(VC, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto2)) {
                            CV = oto1.Alias;
                            VC = oto2.Alias;
                            return new Result {
                                phonemes = new Phoneme[] {
                                    new Phoneme() {
                                        phoneme = CV,
                                    },
                                    new Phoneme() {
                                        phoneme = VC,
                                        position = totalDuration - vcLength,
                                    }
                                },
                            };
                        }
                    }
                    // 그 외(받침 없는 마지막 노트)
                    if (singer.TryGetMappedOto(CV, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto)) {
                        CV = oto.Alias;
                        return new Result {
                            phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = CV,
                                }
                            },
                        };
                    }
                }
            }

            if (prevHangeul) {
                string endBreath = "R";

                if (prevExist && TPLfinal == "" && endBreath.Contains(currentLyric)) {
                    endBreath = $"{TPLplainvowel} R";
                } else if (prevExist && TPLfinal != "" && endBreath.Contains(currentLyric)) {
                    endBreath = $"{TPLplainfinal} R";
                }

                if (singer.TryGetMappedOto(endBreath, note.tone + (attr0.toneShift ?? GetParentToneShift()), attr0.voiceColor ?? GetParentVoiceColor(), out var oto)){
                    endBreath = oto.Alias;
                    return new Result {
                        phonemes = new Phoneme[] {
                            new Phoneme() {
                                phoneme = endBreath,
                            }
                        },
                    };
                }
            }



            // ======================================================================================


            if (prevNeighbour == null) {
                // Use "- V" or "- CV" if present in voicebank
                var initial = $"- {currentLyric}";
                string[] tests = new string[] {initial, currentLyric};
                // try [- XX] before trying plain lyric
                if (checkOtoUntilHit(tests, note, out var oto)){
                    currentLyric = oto.Alias;
                }
            } else if (plainVowels.Contains(currentLyric) || plainDiphthongs.Contains(currentLyric)) {
                var prevUnicode = ToUnicodeElements(prevNeighbour?.lyric);
                var vowel = "";

                var prevLyric = string.Join("", prevUnicode);
                // Current note is VV
                if (vowelLookup.TryGetValue(prevUnicode.LastOrDefault() ?? string.Empty, out var vow)) {
                    vowel = vow;

                    var vowLyric = $"{vow} {currentLyric}";

                    if (prevLyric.EndsWith("eo")) {
                        vowLyric = $"eo {currentLyric}";
                    } else if (prevLyric.EndsWith("eu")) {
                        vowLyric = $"eu {currentLyric}";
                    } else if (prevLyric.EndsWith("er")) {
                        vowLyric = $"er {currentLyric}";
                    }

                    // try vowlyric then currentlyric
                    string[] tests = new string[] { vowLyric, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        currentLyric = oto.Alias;
                    }
                } else if (prevExist && prevHangeul && TPLfinal == "") {
                    var vowLyric = $"{TPLplainvowel} {currentLyric}";

                    string[] tests = new string[] { vowLyric, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        currentLyric = oto.Alias;
                    }
                } else if (prevExist && prevHangeul && TPLfinal == "ng") {
                    string[] tests = new string[] { $"ng{currentLyric}", currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        currentLyric = oto.Alias;
                    }
                } else {
                    string[] tests = new string[] { currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        currentLyric = oto.Alias;
                    }
                }
            } else if ("R".Contains(currentLyric)) {
                var prevUnicode = ToUnicodeElements(prevNeighbour?.lyric);
                var prevLyric = string.Join("", prevUnicode);
                // end breath note
                if (vowelLookup.TryGetValue(prevUnicode.LastOrDefault() ?? string.Empty, out var vow)) {
                    var vowel = "";
                    vowel = vow;

                    var endBreath = $"{vow} R";
                    if (prevLyric.EndsWith("eo")) {
                        endBreath = $"eo R";
                    } else if (prevLyric.EndsWith("eu")) {
                        endBreath = $"eu R";
                    } else if (prevLyric.EndsWith("er")) {
                        endBreath = $"er R";
                    }

                    // try end breath
                    string[] tests = new string[] { endBreath, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        currentLyric = oto.Alias;
                    }
                } else {
                    var endBreath = $"{prevUnicode.LastOrDefault()} R";
                    if (prevLyric.EndsWith("ng")) {
                        endBreath = $"ng R";
                    }

                    // try end breath
                    string[] tests = new string[] { endBreath, currentLyric };
                    if (checkOtoUntilHit(tests, note, out var oto)) {
                        currentLyric = oto.Alias;
                    }
                }
            } else {
                string[] tests = new string[] {currentLyric};
                if (checkOtoUntilHit(tests, note, out var oto)){
                    currentLyric = oto.Alias;
                }
            }

            if (nextNeighbour != null) { // 다음에 노트가 있으면
                var nextUnicode = ToUnicodeElements(nextNeighbour?.lyric);
                var nextLyric = string.Join("", nextUnicode);

                // Check if next note is a vowel and does not require VC
                if (plainVowels.Contains(nextUnicode.FirstOrDefault() ?? string.Empty)) {
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
                    if (currentLyric.Contains(TCLplainvowel)) {
                        vow = TCLplainvowel;
                    }

                    if (currentLyric.Contains("e") && !currentLyric.Contains("eui")) {
                        vowel = "e" + vowel;
                        vowel = vowel.Replace("ee", "e");
                    }
                }

                // Get consonant from next note
                var consonant = "";
                if (consonantLookup.TryGetValue(nextUnicode.FirstOrDefault() ?? string.Empty, out var con)) {
                    consonant = getConsonant(nextNeighbour?.lyric); //로마자만 가능
                    if (nextLyric.StartsWith("jj")) {
                        consonant = "t";
                    }
                    if ((!isAlphaCon(consonant))) { consonant = con; }
                    } else if (nextLyric.StartsWith("gg")) {
                    consonant = "k";
                    } else if (nextLyric.StartsWith("dd")) {
                    consonant = "t";
                    } else if (nextLyric.StartsWith("bb")) {
                    consonant = "p";
                    } else if (nextLyric.StartsWith("l")) {
                    consonant = "l";
                    } else if (nextExist && nextHangeul) {
                    consonant = TNLconsonant;
                    if (TNLconsonant == "gg") {
                        consonant = "k";
                    } else if (TNLconsonant == "dd" || TNLconsonant == "jj") {
                        consonant = "t";
                    } else if (TNLconsonant == "bb") {
                        consonant = "p";
                    } else if (nextLyric.StartsWith('ㄹ')) {
                        consonant = "l";
                    } else {
                        consonant = TNLconsonant;
                    }
                } else if (nextLyric.StartsWith("ch")) {
                    consonant = "ch";
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

                var vcPhoneme = "";
                if (TNLconsonant == "gg" || TNLconsonant == "dd" || TNLconsonant == "bb" || TNLconsonant == "jj" || nextLyric.StartsWith('ㄹ')) {
                    vcPhoneme = $"{vowel}{consonant}";
                } else if (nextLyric.StartsWith("gg") || nextLyric.StartsWith("dd") || nextLyric.StartsWith("bb") || nextLyric.StartsWith("jj") || nextLyric.StartsWith("l")) {
                    vcPhoneme = $"{vowel}{consonant}";
                } else {
                    vcPhoneme = $"{vowel} {consonant}";
                }
                var vcPhonemes = new string[] {vcPhoneme, ""};
                if (checkOtoUntilHit(vcPhonemes, note, out var oto1)) {
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
                
                int vcLength = 60;
                var nextAttr = nextNeighbour.Value.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                if (singer.TryGetMappedOto(nextLyric, nextNeighbour.Value.tone + (nextAttr.toneShift ?? GetParentToneShift()), nextAttr.voiceColor ?? GetParentVoiceColor(), out var oto)) {
                    vcLength = MsToTick(oto.Preutter);
                } else if ((TNLconsonant == "r") || (TNLconsonant == "h")) { vcLength = 30; }
                else if (TNLconsonant == "s") { vcLength = totalDuration / 3; }
                else if ((TNLconsonant == "k") || (TNLconsonant == "t") || (TNLconsonant == "p") || (TNLconsonant == "ch")) { vcLength = totalDuration / 2; }
                else if ((TNLconsonant == "gg") || (TNLconsonant == "dd") || (TNLconsonant == "bb") || (TNLconsonant == "ss") || (TNLconsonant == "jj")) { vcLength = totalDuration / 2; }
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
