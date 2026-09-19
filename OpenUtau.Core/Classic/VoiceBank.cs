using System.Collections.Generic;
using System.Text;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {
    public class Voicebank {
        public string BasePath;
        public string File;
        public string Name;
        public Dictionary<string, string> LocalizedNames = new Dictionary<string, string>();
        public string Image;
        public string Portrait;
        public float PortraitOpacity;
        public int PortraitHeight;
        public int PortraitHeightCap;
        public int PortraitVertPosition;
        public int PortraitHoriPosition;
        public string Author;
        public string Voice;
        public string Web;
        public string Version;
        public string Sample;
        public string OtherInfo;
        public string DefaultPhonemizer;
        public Encoding TextFileEncoding;
        public USingerType SingerType = USingerType.Classic;
        public List<OtoSet> OtoSets = new List<OtoSet>();
        public List<Subbank> Subbanks = new List<Subbank>();
        public string Id;
        public bool? UseFilenameAsAlias = null;

        public void Reload() {
            Name = null;
            LocalizedNames.Clear();
            Image = null;
            Portrait = null;
            PortraitOpacity = 0;
            PortraitHeight = 0;
            Author = null;
            Voice = null;
            Web = null;
            Version = null;
            Sample = null;
            OtherInfo = null;
            TextFileEncoding = null;
            SingerType = USingerType.Classic;
            OtoSets.Clear();
            Subbanks.Clear();
            Id = null;
            UseFilenameAsAlias = null;
            VoicebankLoader.LoadVoicebank(this);
        }

        public override string ToString() {
            return Name;
        }
    }

    public class OtoSet {
        public string File;
        public string Name;
        public List<Oto> Otos = new List<Oto>();

        public override string ToString() {
            return Name;
        }
    }

    public class Oto {
        public string Alias;
        public string Phonetic;
        public string Wav;

        // Wav layout:
        // |-offset-|-consonant-(fixed)-|-stretched-|-cutoff-|
        // |        |-preutter-----|
        // |        |-overlap-|
        // Note position:
        // ... ----------prev-note-|-this-note-- ...
        // Phoneme overlap:
        // ... --prev-phoneme-\
        //          /-this-phoneme-------------- ...

        // Length of left offset.
        public double Offset;
        // Length of unstretched consonant in wav, AKA fixed.
        public double Consonant;
        // Length of right cutoff, AKA end blank. If negative, length of (consonant + stretched). 
        public double Cutoff;
        // Length before note start, usually within consonant range.
        public double Preutter;
        // Length overlap with previous note, usually within consonant range.
        public double Overlap;

        public bool IsValid;
        public string Error = string.Empty;
        public FileTrace? FileTrace;

        public override string ToString() {
            return Alias;
        }
    }
}
