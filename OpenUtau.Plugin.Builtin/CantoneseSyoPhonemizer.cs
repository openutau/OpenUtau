using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Pinyin;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Cantonese phonemizer for Syo-style banks.
    /// Supports both full jyutping syllables as well as syllable fallbacks without a final consonant or falling diphthong.
    /// Supports hanzi and jyutping input.
    /// </summary>
    [Phonemizer("Cantonese Syo-Style Phonemizer", "ZH-YUE SYO", "Lotte V", language: "ZH-YUE")]
    public class CantoneseSyoPhonemizer : Phonemizer {

        /// <summary>
        ///  The consonant table.
        /// </summary>
        static readonly string consonants = "b,p,m,f,d,t,n,l,g,k,ng,h,gw,kw,w,z,c,s,j";

        /// <summary>
        /// The vowel split table.
        /// </summary>
        static readonly string vowels = "aap=aa p,aat=aa t,aak=aa k,aam=aa m,aan=aa n,aang=aa ng,aai=aa i,aau=aa u,ap=a p,at=a t,ak=a k,am=a m,an=a n,ang=a ng,ai=a i,au=a u,op=o p,ot=o t,ok=o k,om=o m,on=o n,ong=o ng,oi=o i,ou=o u,oet=oe t,oek=oe k,oeng=oe ng,oei=oe i,eot=eo t,eon=eo n,eoi=eo i,ep=e p,et=e t,ek=e k,em=e m,en=e n,eng=e ng,ei=e i,eu=e u,up=u p,ut=u t,uk=uu k,um=um,un=u n,ung=uu ng,ui=u i,yut=yu t,yun=yu n,ip=i p,it=i t,ik=ii k,im=i m,in=i n,ing=ii ng,iu=i u";

        /// <summary>
        /// Check for vowel substitutes.
        /// </summary>
        static readonly string[] substitution = new string[] {
            "aap,aat,aak,aam,aan,aang,aai,aau=aa", "ap,at,ak,am,an,ang,ai,au=a", "op,ot,ok,om,on,ong,oi,ou=o", "oet,oek,oen,oeng,oei=oe", "eot,eon,eoi=eo","ep,et,ek,em,en,eng,ei,eu=e", "uk,ung=uu", "up,ut,um,un,ui=u", "yut,yun=yu","ik,ing=ii", "ip,it,im,in,iu=i"
        };

        /// <summary>
        /// Check for substitutes for finals.
        /// </summary>
        static readonly string[] finalSub = new string[] {
            "ii ng=i ng", "ii k=i k", "uu k=u k", "uu ng=u ng", "oe t=eo t", "oe i=eo i"
        };

        static HashSet<string> cSet;
        static Dictionary<string, string> vDict;
        static readonly Dictionary<string, string> substituteLookup;
        static readonly Dictionary<string, string> finalSubLookup;

        static CantoneseSyoPhonemizer() {
            cSet = new HashSet<string>(consonants.Split(','));
            vDict = vowels.Split(',')
                .Select(s => s.Split('='))
                .ToDictionary(a => a[0], a => a[1]);
            substituteLookup = substitution.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[0].Split(',').Select(orig => (orig, parts[1]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            finalSubLookup = finalSub.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[0].Split(',').Select(orig => (orig, parts[1]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        private USinger singer;

        // Simply stores the singer in a field.
        public override void SetSinger(USinger singer) => this.singer = singer;

        /// <summary>
        /// Converts hanzi notes to jyutping phonemes.
        /// </summary>
        /// <param name="groups"></param>
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            JyutpingConversion.RomanizeNotes(groups);
        }
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            // The overall logic is:
            // 1. Remove consonant: "jyut" -> "yut".
            // 2. Lookup the trailing sound in vowel table: "yut" -> "yu t".
            // 3. Split the total duration and returns "jyut"/"jyu" and "yu t".
            var note = notes[0];
            var lyric = note.lyric;
            string consonant = string.Empty;
            string vowel = string.Empty;

            if (lyric.Length > 2 && cSet.Contains(lyric.Substring(0, 2))) {
                // First try to find consonant "gw", "kw", "ng", and extract vowel.
                consonant = lyric.Substring(0, 2);
                vowel = lyric.Substring(2);
            } else if (lyric.Length > 1 && cSet.Contains(lyric.Substring(0, 1)) && lyric != "ng") {
                // Then try to find single character consonants, and extract vowel.
                consonant = lyric.Substring(0, 1);
                vowel = lyric.Substring(1);
            } else {
                // Otherwise the lyric is a vowel.
                vowel = lyric;
            }

            string phoneme0 = lyric;

            // Get color
            string color = string.Empty;
            int toneShift = 0;
            int? alt = 0;
            if (note.phonemeAttributes != null) {
                var attr = note.phonemeAttributes.FirstOrDefault(attr0 => attr0.index == 0);
                color = attr.voiceColor;
                toneShift = attr.toneShift;
                alt = attr.alternate;
            }

            string fin = $"{vowel} -";
            // We will need to split the total duration for phonemes, so we compute it here.
            int totalDuration = notes.Sum(n => n.duration);
            // Lookup the vowel split table. For example, "yut" will match "yu t".
            if (vDict.TryGetValue(vowel, out var phoneme1) && !string.IsNullOrEmpty(phoneme1)) {
                // Now phoneme0="jyu" and phoneme1="yu t",
                // try to give "yu t" 120 ticks, but no more than half of the total duration.
                int length1 = 120;

                if (length1 > totalDuration / 2) {
                    length1 = totalDuration / 2;
                }
                var lyrics = new List<string> { lyric };
                // find potential substitute symbol
                if (substituteLookup.TryGetValue(vowel ?? string.Empty, out var sub)) {
                    if (!string.IsNullOrEmpty(consonant)) {
                        lyrics.Add($"{consonant}{sub}");
                    } else {
                        lyrics.Add(sub);
                    }
                }

                // Try initial and then a plain lyric
                if (prevNeighbour == null || (prevNeighbour != null && (prevNeighbour.Value.lyric.EndsWith("p") || prevNeighbour.Value.lyric.EndsWith("t") || prevNeighbour.Value.lyric.EndsWith("k")))) {
                    var initial = $"- {lyric}";
                    var initial2 = $"- {lyrics[1]}";
                    var tests = new List<string> { initial, initial2, lyric, lyrics[1] };
                    if (checkOtoUntilHit(tests, note, out var otoInit)) {
                        phoneme0 = otoInit.Alias;
                    }
                } else { // nothing special necessary
                    if (checkOtoUntilHit(lyrics, note, out var otoLyric)) {
                        phoneme0 = otoLyric.Alias;
                    }
                }

                int length2 = 60;
                if (length2 > totalDuration / 2) {
                    length2 = totalDuration / 2;
                }
                if (nextNeighbour == null && singer.TryGetMappedOto(fin, note.tone, out _)) {
                    // Vowel ending is minimum 60 ticks, maximum half of note
                    var finals = new List<string> { fin };
                    if (checkOtoUntilHitFinal(finals, note, out var otoFin)) {
                        phoneme1 = otoFin.Alias;
                    }
                    return new Result {
                        phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = phoneme0,
                        },
                        new Phoneme() {
                            phoneme = phoneme1,
                            position = totalDuration - length2,
                        }
                    },
                    };
                } else {
                    var tails = new List<string> { phoneme1 };
                    // find potential substitute symbol
                    if (finalSubLookup.TryGetValue(phoneme1 ?? string.Empty, out var finSub)) {
                        tails.Add(finSub);
                    }
                    if (checkOtoUntilHitFinal(tails, note, out var otoTail)) {
                        phoneme1 = otoTail.Alias;
                    } else {
                        return MakeSimpleResult(phoneme0);
                    }
                }

                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = phoneme0,
                        },
                        new Phoneme() {
                            phoneme = phoneme1,
                            position = totalDuration - length1,
                        }
                    },
                };
            }

            // Check for vowel ending on open syllables.
            // If a vowel ending does not exist, it will not be inserted.
            if (nextNeighbour == null && string.IsNullOrEmpty(phoneme1) && !string.IsNullOrEmpty(fin)) {
                // Vowel ending is minimum 60 ticks, maximum half of note
                int length1 = 60;

                if (length1 > totalDuration / 2) {
                    length1 = totalDuration / 2;
                }
                // Try initial and then a plain lyric
                var lyrics = new List<string> { lyric };
                if (prevNeighbour == null || (prevNeighbour != null && (prevNeighbour.Value.lyric.EndsWith("p") || prevNeighbour.Value.lyric.EndsWith("t") || prevNeighbour.Value.lyric.EndsWith("k")))) {
                    var initial = $"- {lyric}";
                    var tests = new List<string> { initial, lyric };
                    if (checkOtoUntilHit(tests, note, out var otoInit)) {
                        phoneme0 = otoInit.Alias;
                    }
                } else { // nothing special necessary
                    if (checkOtoUntilHit(lyrics, note, out var otoLyric)) {
                        phoneme0 = otoLyric.Alias;
                    } else {
                        return MakeSimpleResult(phoneme0);
                    }
                }

                // Map vowel ending
                var tails = new List<string> { fin };
                if (checkOtoUntilHitFinal(tails, note, out var otoTail)) {
                    fin = otoTail.Alias;
                } else {
                    return MakeSimpleResult(phoneme0);
                }

                return new Result {
                    phonemes = new Phoneme[] {
                        new Phoneme() {
                            phoneme = phoneme0,
                        },
                        new Phoneme() {
                            phoneme = fin,
                            position = totalDuration - length1,
                        }
                    },
                };
            }

            // Try initial and then a plain lyric
            if (prevNeighbour == null || (prevNeighbour != null && (prevNeighbour.Value.lyric.EndsWith("p") || prevNeighbour.Value.lyric.EndsWith("t") || prevNeighbour.Value.lyric.EndsWith("k")))) {
                var simpleInitial = $"- {lyric}";
                var tests = new List<string> { simpleInitial, lyric };
                if (checkOtoUntilHit(tests, note, out var otoInit)) {
                    phoneme0 = otoInit.Alias;
                } else {
                    return MakeSimpleResult(phoneme0);
                }
            } else { // nothing special necessary
                var tests = new List<string> { lyric };
                if (checkOtoUntilHit(tests, note, out var otoLyric)) {
                    phoneme0 = otoLyric.Alias;
                } else {
                    return MakeSimpleResult(phoneme0);
                }
            }
            // Not spliting is needed. Return as is.
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = phoneme0,
                    }
                },
            };

        }

        /// <summary>
        /// Converts hanzi to jyutping, based on G2P.
        /// </summary>
        public class JyutpingConversion {
            public static Note[] ChangeLyric(Note[] group, string lyric) {
                var oldNote = group[0];
                group[0] = new Note {
                    lyric = lyric,
                    phoneticHint = oldNote.phoneticHint,
                    tone = oldNote.tone,
                    position = oldNote.position,
                    duration = oldNote.duration,
                    phonemeAttributes = oldNote.phonemeAttributes,
                };
                return group;
            }

            public static string[] Romanize(IEnumerable<string> lyrics) {
                var lyricsArray = lyrics.ToArray();
                var hanziLyrics = lyricsArray
                    .Where(Pinyin.Jyutping.Instance.IsHanzi)
                    .ToList();
                var jyutpingResult = Pinyin.Jyutping.Instance.HanziToPinyin(hanziLyrics, CanTone.Style.NORMAL, Pinyin.Error.Default).ToStrList();
                if (jyutpingResult == null) {
                    return lyricsArray;
                }
                var jyutpingIndex = 0;
                for (int i = 0; i < lyricsArray.Length; i++) {
                    if (lyricsArray[i].Length == 1 && Pinyin.Jyutping.Instance.IsHanzi(lyricsArray[i])) {
                        lyricsArray[i] = jyutpingResult[jyutpingIndex];
                        jyutpingIndex++;
                    }
                }
                return lyricsArray;
            }

            public static void RomanizeNotes(Note[][] groups) {
                var ResultLyrics = Romanize(groups.Select(group => group[0].lyric));
                Enumerable.Zip(groups, ResultLyrics, ChangeLyric).Last();
            }

            public void SetUp(Note[][] groups) {
                RomanizeNotes(groups);
            }
        }

        // make it quicker to check multiple oto occurrences at once rather than spamming if else if
        private bool checkOtoUntilHit(List<string> input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attrCheck => attrCheck.index == 0) ?? default;

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + attr.alternate, note.tone + attr.toneShift, attr.voiceColor, out var otoAlt)) {
                    otos.Add(otoAlt);
                } else if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            string color = attr.voiceColor ?? "";
            if (otos.Count > 0) {
                oto = otos.FirstOrDefault(oto => oto.IsColorMatch(color));
                if (oto == null) {
                    oto = otos.First();
                }
                return true;
            }
            return false;
        }

        // Check for final consonant or vowel ending
        private bool checkOtoUntilHitFinal(List<string> input, Note note, out UOto oto) {
            oto = default;
            var attr = note.phonemeAttributes?.FirstOrDefault(attrCheck => attrCheck.index == 1) ?? default;

            var otos = new List<UOto>();
            foreach (string test in input) {
                if (singer.TryGetMappedOto(test + attr.alternate, note.tone + attr.toneShift, attr.voiceColor, out var otoAlt)) {
                    otos.Add(otoAlt);
                } else if (singer.TryGetMappedOto(test, note.tone + attr.toneShift, attr.voiceColor, out var otoCandidacy)) {
                    otos.Add(otoCandidacy);
                }
            }

            string color = attr.voiceColor ?? "";
            if (otos.Count > 0) {
                oto = otos.FirstOrDefault(oto => oto.IsColorMatch(color));
                if (oto != null) {
                    return true;
                }
            }
            return false;
        }
    }
}
