using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtau.Core.DiffSinger
{
    [Phonemizer("DiffSinger Korean Phonemizer", "DIFFS KO","EX3", language:"KO")]
    public class DiffSingerKoreanPhonemizer : DiffSingerBasePhonemizer
    {
        protected override string GetDictionaryName()=>"dsdict-ko.yaml";
        public override string GetLangCode()=>"ko";

        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            base.SetUp(groups, project, track);
            if (groups.Length == 0) {
                return;
            }
            // variate lyrics 
            KoreanPhonemizerUtil.RomanizeNotes(groups, false);

            //Split song into sentences (phrases)
            var phrase = new List<Note[]> { groups[0] };
            for (int i = 1; i < groups.Length; ++i) {
                //If the previous and current notes are connected, do not split the sentence
                if (groups[i - 1][^1].position + groups[i - 1][^1].duration == groups[i][0].position) {
                    phrase.Add(groups[i]);
                } else {
                    //If the previous and current notes are not connected, process the current sentence and start the next sentence
                    ProcessPart(phrase.ToArray());
                    phrase.Clear();
                    phrase.Add(groups[i]);
                }
            }
            if (phrase.Count > 0) {
                ProcessPart(phrase.ToArray());
            }
        }
    }
}
