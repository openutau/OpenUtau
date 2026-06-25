using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using WanaKanaNet;
using static OpenUtau.Core.KoreanPhonemizerUtil;

namespace OpenUtau.Plugin.Builtin {
    [Phonemizer("KO to JA Phonemizer", "KO to JA", "Lotte V", language: "KO")]
    public class KOtoJAPhonemizer : BaseKoreanPhonemizer {
        /// <summary>
        /// Phonemizer for making Japanese banks sing in Korean.
        /// Supports Hangul and phonetic hint (based on Japanese romaji).
        /// Works with VCV, CVVC and CV banks. Crossfade vowels are also supported.
        /// CVVC and VCV+CVVC banks give the best results.
        /// </summary>

        /// <summary>
        /// Initial jamo as ordered in Unicode
        /// </summary>
        static readonly string[] initials = { "g", "k", "n", "d", "t", "r", "m", "b", "p", "s", "s", string.Empty, "j", "ch", "ch", "k", "t", "p", "h" };
        // ㄱ ㄲ ㄴ ㄷ ㄸ ㄹ ㅁ ㅂ ㅃ ㅅ ㅆ ㅇ ㅈ ㅉ ㅊ ㅋ ㅌ ㅍ ㅎ

        /// <summary>
        /// Medial jamo as ordered in Unicode
        /// </summary>
        static readonly string[] medials = { "a", "e", "ya", "ye", "o", "e", "yo", "ye", "o", "wa", "we", "we", "yo", "u", "wo", "we", "wi", "yu", "u", "wi", "i" };
        // ㅏ ㅐ ㅑ ㅒ ㅓ ㅔ ㅕ ㅖ ㅗ ㅘ ㅙ ㅚ ㅛ ㅜ ㅝ ㅞ ㅟ ㅠ ㅡ ㅢ ㅣ

        /// <summary>
        /// Final jamo as ordered in Unicode + vowel end breath sounds (inhale and exhale)
        /// </summary>
        static readonly string[] finals = { string.Empty, "k", "k", "k", "n", "n", "n", "t", "r", "r", "r", "r", "r", "r", "r", "r", "m", "p", "p", "t", "t", "n", "t", "t", "k", "t", "p", "t", string.Empty, string.Empty, string.Empty, };
        // - ㄱ ㄲ ㄳ ㄴ ㄵ ㄶ ㄷ ㄹ ㄺ ㄻ ㄼ ㄽ ㄾ ㄿ ㅀ ㅁ ㅂ ㅄ ㅅ ㅆ ㅇ ㅈ ㅊ ㅋ ㅌ ㅍ ㅎ H B bre

        /// <summary>
        /// Sonorant batchim (i.e., extendable batchim sounds)
        /// </summary>
        static readonly string[] sonorants = { "n" };

        /// <summary>
        /// Extra English-based sounds for phonetic hint input + alternate romanizations for tense plosives (ㄲ, ㄸ, ㅃ)
        /// </summary>
        static readonly string[] extras = { "f", "v", "z", "ts", "sh" };

        static readonly string[] consonants = new string[] {
            "ch=ち,ちぇ,ちゃ,ちゅ,ちょ",
            "gy=ぎ,ぎぇ,ぎゃ,ぎゅ,ぎょ",
            "ts=つ,つぁ,つぃ,つぇ,つぉ",
            "ty=てぃ,てぇ,てゃ,てゅ,てょ",
            "py=ぴ,ぴぇ,ぴゃ,ぴゅ,ぴょ",
            "ry=り,りぇ,りゃ,りゅ,りょ",
            "ny=に,にぇ,にゃ,にゅ,にょ",
            "r=ら,る,るぁ,るぃ,るぇ,るぉ,れ,ろ",
            "hy=ひ,ひぇ,ひゃ,ひゅ,ひょ",
            "dy=でぃ,でぇ,でゃ,でゅ,でょ",
            "by=び,びぇ,びゃ,びゅ,びょ",
            "b=ば,ぶ,ぶぁ,ぶぃ,ぶぇ,ぶぉ,べ,ぼ",
            "d=だ,で,ど,どぅ",
            "g=が,ぐ,ぐぁ,ぐぃ,ぐぇ,ぐぉ,げ,ご",
            "f=ふ,ふぁ,ふぃ,ふぇ,ふぉ,ふぃぇ,ふゃ,ふゅ,ふょ",
            "h=は,へ,ほ",
            "k=か,く,くぁ,くぃ,くぇ,くぉ,け,こ",
            "j=じ,じぇ,じゃ,じゅ,じょ",
            "m=ま,む,むぁ,むぃ,むぇ,むぉ,め,も",
            "n=な,ぬ,ぬぁ,ぬぃ,ぬぇ,ぬぉ,ね,の",
            "p=ぱ,ぷ,ぷぁ,ぷぃ,ぷぇ,ぷぉ,ぺ,ぽ",
            "s=さ,す,すぁ,すぃ,すぇ,すぉ,せ,そ",
            "sh=し,しぇ,しゃ,しゅ,しょ",
            "t=た,て,と,とぅ",
            "v=ヴ,ヴぁ,ヴぃ,ヴぇ,ヴぉ,ヴぃぇ,ヴゃ,ヴゅ,ヴょ",
            "ky=き,きぇ,きゃ,きゅ,きょ",
            "w=うぃ,うぇ,うぉ,わ,を",
            "y=いぇ,や,ゆ,よ",
            "z=ざ,ず,ずぁ,ずぃ,ずぇ,ずぉ,ぜ,ぞ",
            "my=み,みぇ,みゃ,みゅ,みょ",
        };

        // in case voicebank is missing certain symbols
        static readonly string[] substitution = new string[] {
            "ty,ch,ts=t", "j,dy=d", "gy=g", "ky=k", "py=p", "ny=n", "ry=r", "my=m", "hy,f=h", "by,v=b",
        };

        static readonly Dictionary<string, string> consonantLookup;
        static readonly Dictionary<string, string> substituteLookup;

        static KOtoJAPhonemizer() {
            consonantLookup = consonants.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[1].Split(',').Select(cv => (cv, parts[0]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
            substituteLookup = substitution.ToList()
                .SelectMany(line => {
                    var parts = line.Split('=');
                    return parts[0].Split(',').Select(orig => (orig, parts[1]));
                })
                .ToDictionary(t => t.Item1, t => t.Item2);
        }

        private Dictionary<string, string> AltCv => altCv;
        private static readonly Dictionary<string, string> altCv = new Dictionary<string, string> {
            {"wo", "ulo" },
            {"kwi", "kuli" },
            {"kwe", "kule" },
            {"kwo", "kulo" },
            {"twa", "ta" },
            {"twi", "teli" },
            {"twe", "te" },
            {"two", "to" },
            {"nwa", "nula" },
            {"nwi", "nuli" },
            {"nwe", "nule" },
            {"nwo", "nulo" },
            {"hwa", "fa" },
            {"hwi", "fi" },
            {"hwe", "fe" },
            {"hwo", "fo" },
            {"mwa", "mula" },
            {"mwi", "muli" },
            {"mwe", "mule" },
            {"mwo", "mulo" },
            {"rwa", "rula" },
            {"rwi", "ruli" },
            {"rwe", "rule" },
            {"rwo", "rulo" },
            {"zwa", "zula" },
            {"zwi", "zuli" },
            {"zwe", "zule" },
            {"zwo", "zulo" },
            {"dwa", "da" },
            {"dwi", "deli" },
            {"dwe", "de" },
            {"dwo", "do" },
            {"bwa", "bula" },
            {"bwi", "buli" },
            {"bwe", "bule" },
            {"bwo", "bulo" },
            {"pwa", "pula" },
            {"pwi", "puli" },
            {"pwe", "pule" },
            {"pwo", "pulo" },
            {"chwa", "tsa" },
            {"chwi", "tsi" },
            {"chwe", "tse" },
            {"chwo", "tso" },
            {"jwa", "zula" },
            {"jwi", "zuli" },
            {"jwe", "zule" },
            {"jwo", "zulo" },
            {"ti", "teli" },
            {"tye", "tele" },
            {"tya", "telya" },
            {"tyu", "telyu" },
            {"tyo", "telyo" },
            {"tu", "tolu" },
            {"di", "deli" },
            {"dye", "dele" },
            {"dya", "delya" },
            {"dyu", "delyu" },
            {"dyo", "delyo" },
            {"du", "dolu" },
            {"zi", "zuli" },
            {"zye", "zulile" },
            {"zya", "zulya" },
            {"zyu", "zulyu" },
            {"zyo", "zulyo" },
            {"chye", "che" },
            {"chya", "cha" },
            {"chyu", "chu" },
            {"chyo", "cho" },
            {"fye", "file" },
            {"vye", "vile" },
            {"shye", "she" },
            {"shya", "sha" },
            {"shyu", "shu" },
            {"shyo", "sho" },
            {"shwa", "sha" },
            {"shwi", "shi" },
            {"shwe", "she" },
            {"shwo", "sho" },
            {"fwa", "fa" },
            {"fwi", "fi" },
            {"fwe", "fe" },
            {"fwo", "fo" },
            {"vwa", "va" },
            {"vwi", "vi" },
            {"vwe", "ve" },
            {"vwo", "vo" },
            {"tswa", "tsa" },
            {"tswi", "tsi" },
            {"tswe", "tse" },
            {"tswo", "tso" },
            {"tsye", "tse" },
            {"tsya", "tsa" },
            {"tsyu", "tsu" },
            {"tsyo", "tso" },
        };

        private Dictionary<string, string> ConditionalAlt => conditionalAlt;
        private static readonly Dictionary<string, string> conditionalAlt = new Dictionary<string, string> {
            {"ulo", "wo"},
            {"kwa", "ka"},
            {"kwi", "ki"},
            {"kwe", "ke"},
            {"kwo", "ko"},
            {"swa", "sa"},
            {"swi", "si"},
            {"swe", "se"},
            {"swo", "so"},
            {"nwa", "na"},
            {"nwi", "ni"},
            {"nwe", "ne"},
            {"nwo", "no"},
            {"fa", "ha"},
            {"fi", "hi"},
            {"fe", "he"},
            {"fo", "ho"},
            {"mwa", "ma"},
            {"mwi", "mi"},
            {"mwe", "me"},
            {"mwo", "mo"},
            {"rwa", "ra"},
            {"rwi", "ri"},
            {"rwe", "re"},
            {"rwo", "ro"},
            {"gwa", "ga"},
            {"gwi", "gi"},
            {"gwe", "ge"},
            {"gwo", "go"},
            {"zwa", "za"},
            {"zwi", "ji"},
            {"zwe", "ze"},
            {"zwo", "zo"},
            {"jwa", "ja"},
            {"jwi", "ji"},
            {"jwe", "je"},
            {"jwo", "jo"},
            {"chwa", "cha"},
            {"chwi", "chi"},
            {"chwe", "che"},
            {"chwo", "cho"},
            {"bwa", "ba"},
            {"bwi", "bi"},
            {"bwe", "be"},
            {"bwo", "bo"},
            {"pwa", "pa"},
            {"pwi", "pi"},
            {"pwe", "pe"},
            {"pwo", "po"},
            {"ye", "e"},
            {"kye", "ke"},
            {"sye", "se"},
            {"che", "te"},
            {"nye", "ne"},
            {"hye", "he"},
            {"mye", "me"},
            {"rye", "re"},
            {"gye", "ge"},
            {"je", "ze"},
            {"bye", "be"},
            {"pye", "pe"},
            {"tye", "te"},
            {"ti", "chi"},
            {"tya", "cha"},
            {"tyu", "chu"},
            {"tyo", "cho"},
            {"tu", "tsu"},
            {"tsa", "cha"},
            {"tsi", "chi"},
            {"tse", "che"},
            {"tso", "cho"},
            {"di", "ji"},
            {"dye", "de"},
            {"dya", "ja"},
            {"dyu", "ju"},
            {"dyo", "jo"},
            {"du", "zu"},
            {"zye", "je"},
            {"zya", "ja"},
            {"zyu", "ju"},
            {"zyo", "jo"},
            {"fye", "pye"},
            {"fya", "pya"},
            {"fyu", "pyu"},
            {"fyo", "pyo"},
            {"va", "ba"},
            {"vi", "bi"},
            {"vu", "bu"},
            {"ヴ", "ぶ"},
            {"ve", "be"},
            {"vo", "bo"},
            {"vye", "bye"},
            {"vya", "bya"},
            {"vyu", "byu"},
            {"vyo", "byo"},
            {"chye", "se"},
            {"jye", "ze"},
            {"we", "e"},
            {"wi", "i"},
            {"k", "ku" },
            {"t", "to" },
            {"p", "pu" },
            {"r", "ru" },
            {"m", "mu" },
        };

        /// <summary>
        /// Apply Korean sandhi rules to Hangeul lyrics.
        /// </summary>
        public override void SetUp(Note[][] groups, UProject project, UTrack track) {
            base.SetUp(groups, project, track);
            // variate lyrics 
            RomanizeNotes(groups, false);
        }

        /// <summary>
        /// Gets the romanized initial, medial, and final components of the passed Hangul syllable.
        /// </summary>
        /// <param name="syllable">A Hangul syllable.</param>
        /// <returns>An array containing the initial, medial, and final sounds of the syllable.</returns>
        public string[] GetIMF(string syllable) {
            byte[] bytes = Encoding.Unicode.GetBytes(syllable);
            int numval = Convert.ToInt32(bytes[0]) + Convert.ToInt32(bytes[1]) * (16 * 16);
            numval -= 44032;
            int i = numval / 588;
            numval -= i * 588;
            int m = numval / 28;
            numval -= m * 28;
            int f = numval;

            string[] ret = { initials[i], medials[m], finals[f] };

            return ret;
        }

        /// <summary>
        /// Separates the initial, medial, and final components of the passed phonetic hint.
        /// </summary>
        /// <param name="hint">A phonetic hint.</param>
        /// <returns>An array containing the initial, medial, and final sounds of the phonetic hint.</returns>
        public string[] GetIMFFromHint(string hint) {
            string[] hintSplit = hint.Split(' ');

            string i = Array.IndexOf(initials.Concat(extras).ToArray(), hintSplit[0]) > -1 ? hintSplit[0] : string.Empty;
            string m = string.IsNullOrEmpty(i) ? hintSplit[0] : hintSplit[1];
            string f = (hintSplit.Length > 2 || (hintSplit.Length == 2 && string.IsNullOrEmpty(i))) ? hintSplit[^1] : string.Empty;
            string[] ret = { i, m, f };

            return ret;
        }

        /// <summary>
        /// Gets the last sound of an alias.
        /// </summary>
        /// <param name="lyric">The alias to get the last sound of.</param>
        /// <returns>The last sound of the alias</returns>
        public string GetLastSoundOfAlias(string lyric) {
            string lastSound = lyric.Split(' ')[^1];
            Regex symbolRemove = new Regex(@"\W");
            MatchCollection symbolMatches = symbolRemove.Matches(lastSound);
            foreach (Match symbolMatch in symbolMatches) {
                lastSound = lastSound.Replace(symbolMatch.Value, string.Empty);
            }
            if (Array.IndexOf(finals, lastSound) == -1) {
                foreach (string i in initials) {
                    if (!string.IsNullOrEmpty(i)) lastSound = lastSound.Replace(i, string.Empty);
                }
            }
            return lastSound;
        }

        private USinger singer;

        // Store singer
        public override void SetSinger(USinger singer) => this.singer = singer;

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            Note note = notes[0];
            string color = string.Empty;
            int shift = 0;
            int? alt;

            string color1 = string.Empty;
            int shift1 = 0;
            int? alt1;

            string color2 = string.Empty;
            int shift2 = 0;
            int? alt2;

            PhonemeAttributes attr = note.phonemeAttributes.FirstOrDefault(a => a.index == 0);
            color = attr.voiceColor ?? GetParentVoiceColor();
            shift = attr.toneShift ?? GetParentToneShift();
            alt = attr.alternate ?? GetParentAlternate();

            PhonemeAttributes attr1 = note.phonemeAttributes.FirstOrDefault(a => a.index == 1);
            color1 = attr1.voiceColor ?? GetParentVoiceColor();
            shift1 = attr1.toneShift ?? GetParentToneShift();
            alt1 = attr1.alternate ?? GetParentAlternate();

            PhonemeAttributes attr2 = note.phonemeAttributes.FirstOrDefault(a => a.index == 2);
            color2 = attr2.voiceColor ?? GetParentVoiceColor();
            shift2 = attr2.toneShift ?? GetParentToneShift();
            alt2 = attr2.alternate ?? GetParentAlternate();

            string[] currIMF;
            string currPhoneme;
            string[] prevIMF;

            var romaji2Korean = ConvertRomajiNoteToHangeul(notes, prevNeighbour, nextNeighbour);
            if (romaji2Korean != null) {
                notes = romaji2Korean.KoreanLyricNotes;

                note = notes[0];
            }

            // Check if notes are romaji
            var currRomaji = IsKoreanRomaji(note.lyric);
            var prevRomaji = IsKoreanRomaji(prevNeighbour?.lyric);

            // Improve romaji lyric parsing, to mix with hangeul, vowel endings, and phonetic hint
            var currRomLyric = TryParseKoreanRomaji(note.lyric);
            var prevRomLyric = TryParseKoreanRomaji(prevNeighbour?.lyric);

            // Check if lyric is R, - or an end breath and return appropriate Result; otherwise, move to next steps
            if (note.lyric == "R" || note.lyric == "-" || note.lyric == "H" || note.lyric == "B" || note.lyric == "bre" || note.lyric == "息" || note.lyric == "吸") {
                currPhoneme = note.lyric;
                if (prevNeighbour == null) {
                    return new Result {
                        phonemes = new Phoneme[] {
                    new Phoneme() { phoneme = currPhoneme }
                }
                    };
                } else {
                    if (singer.TryGetMappedOto(prevNeighbour.Value.lyric, note.tone + shift, color, out _)) {
                        string lastSound = GetLastSoundOfAlias(prevNeighbour.Value.lyric);
                        if (singer.TryGetMappedOto($"{lastSound} {currPhoneme}", note.tone + shift, color, out _)) {
                            currPhoneme = $"{lastSound} {currPhoneme}";
                        } else {
                            currPhoneme = $"{currPhoneme}";
                        }
                    } else {
                        if (string.IsNullOrEmpty(prevNeighbour?.phoneticHint)) {
                            byte[] bytes = Encoding.Unicode.GetBytes($"{prevNeighbour?.lyric[0]}");
                            int numval = Convert.ToInt32(bytes[0]) + Convert.ToInt32(bytes[1]) * (16 * 16);
                            if (prevNeighbour?.lyric.Length == 1 && numval >= 44032 && numval <= 55215)
                                prevIMF = GetIMF(prevNeighbour.Value.lyric);
                            else if (prevRomaji && prevRomLyric != null) {
                                prevIMF = GetIMF(prevRomLyric);
                            } else
                                return new Result {
                                    phonemes = new Phoneme[] {
                                        new Phoneme() {
                                        phoneme = currPhoneme
                                    }
                                }
                            };
                        } else prevIMF = GetIMFFromHint(prevNeighbour.Value.phoneticHint);

                        if (string.IsNullOrEmpty(prevIMF[2])) {
                            if (singer.TryGetMappedOto(currPhoneme = $"{((prevIMF[1][0] == 'w' || prevIMF[1][0] == 'y') ? prevIMF[1].Remove(0, 1) : ((prevIMF[1] == "wi") ? "i" : prevIMF[1]))} {currPhoneme}", note.tone + shift, color, out _)) {
                                // remove semivowel from ending note
                            } else {
                                currPhoneme = $"{currPhoneme}";
                            }
                        } else if (prevIMF[2] == "n") {
                            if (singer.TryGetMappedOto($"{prevIMF[2]} {currPhoneme}", note.tone + shift, color, out _)) {
                                currPhoneme = $"{prevIMF[2]} {currPhoneme}";
                            } else {
                                currPhoneme = $"{currPhoneme}";
                            }
                        }
                    }
                    // Map alias (apply shift + color)
                    if (singer.TryGetMappedOto(currPhoneme + alt, note.tone + shift, color, out var oto)) {
                        currPhoneme = oto.Alias;
                    }

                    return new Result {
                        phonemes = new Phoneme[] {
                    new Phoneme() { phoneme = currPhoneme }
                    }
                    };
                }
            }
            // Get IMF of current note if valid, otherwise return the lyric as is
            if (string.IsNullOrEmpty(note.phoneticHint)) {
                byte[] bytes = Encoding.Unicode.GetBytes($"{note.lyric[0]}");
                int numval = Convert.ToInt32(bytes[0]) + Convert.ToInt32(bytes[1]) * (16 * 16);
                if (note.lyric.Length == 1 && numval >= 44032 && numval <= 55215) currIMF = GetIMF(note.lyric);
                else if (currRomaji && currRomLyric != null) {
                    currIMF = GetIMF(currRomLyric);
                } else return new Result {
                    phonemes = new Phoneme[] {
                new Phoneme() { phoneme = note.lyric }
            }
                };
            } else currIMF = GetIMFFromHint(note.phoneticHint);
            // Convert current note to phoneme
            currPhoneme = $"{currIMF[0]}{currIMF[1]}";
            // Adjust current phoneme based on previous neighbor
            if (prevNeighbour != null && prevNeighbour?.lyric != "bre" && singer.TryGetMappedOto(prevNeighbour.Value.lyric, note.tone + shift, color, out _)) {
                // Apply alt CV
                if (singer.TryGetMappedOto($"{GetLastSoundOfAlias(prevNeighbour.Value.lyric)} {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                    currPhoneme = $"{GetLastSoundOfAlias(prevNeighbour.Value.lyric)} {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                } else if (singer.TryGetMappedOto($"* {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                    currPhoneme = $"* {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                } else if (singer.TryGetMappedOto($"- {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                    currPhoneme = $"- {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                    currPhoneme = $"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                    // Apply conditional alt CV
                } else if (singer.TryGetMappedOto($"{GetLastSoundOfAlias(prevNeighbour.Value.lyric)} {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                    currPhoneme = $"{GetLastSoundOfAlias(prevNeighbour.Value.lyric)} {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                } else if (singer.TryGetMappedOto($"* {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                    currPhoneme = $"* {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                } else if (singer.TryGetMappedOto($"- {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                    currPhoneme = $"- {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                } else {
                    currPhoneme = $"{ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                }
            } else {
                if (prevNeighbour == null || prevNeighbour?.lyric == "R" || prevNeighbour?.lyric == "-" || prevNeighbour?.lyric == "H" || prevNeighbour?.lyric == "B" || prevNeighbour?.lyric == "bre" || prevNeighbour?.lyric == "息" || prevNeighbour?.lyric == "吸") {
                    // Apply alt CV
                    if (singer.TryGetMappedOto($"- {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"- {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                    } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                        // Apply conditional alt CV
                    } else if (singer.TryGetMappedOto($"- {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"- {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                    } else {
                        currPhoneme = $"{ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                    }
                } else {
                    if (string.IsNullOrEmpty(prevNeighbour?.phoneticHint)) {
                        byte[] bytes = Encoding.Unicode.GetBytes($"{prevNeighbour?.lyric[0]}");
                        int numval = Convert.ToInt32(bytes[0]) + Convert.ToInt32(bytes[1]) * (16 * 16);
                        if (prevNeighbour?.lyric.Length == 1 && numval >= 44032 && numval <= 55215) prevIMF = GetIMF(prevNeighbour.Value.lyric);
                        else if (prevRomaji && prevRomLyric != null) {
                            prevIMF = GetIMF(prevRomLyric);
                        } else return new Result {
                            phonemes = new Phoneme[] {
                        new Phoneme() { phoneme = note.lyric }
                    }
                        };
                    } else prevIMF = GetIMFFromHint(prevNeighbour.Value.phoneticHint);
                    string prevConnect;
                    string prevBatchim = (prevIMF[1][0] == 'w' || prevIMF[1][0] == 'y') ? prevIMF[1].Remove(0, 1) : prevIMF[1];
                    // Adjust Result if note has batchim
                    if (!string.IsNullOrEmpty(prevIMF[2])) {
                        if (prevIMF[2] == "n") {
                            if (singer.TryGetMappedOto($"{prevBatchim} {ToHiragana(prevIMF[2])}", prevNeighbour.Value.tone + shift, color, out _)) {
                                prevBatchim += $" {ToHiragana(prevIMF[2])}";
                            } else if (singer.TryGetMappedOto($"* {ToHiragana(prevIMF[2])}", prevNeighbour.Value.tone + shift, color, out _)) {
                                prevBatchim = $"* {ToHiragana(prevIMF[2])}";
                            } else {
                                prevBatchim = $"{ToHiragana(prevIMF[2])}";
                            }
                        } else {
                            if (singer.TryGetMappedOto($"{prevBatchim} {prevIMF[2]}", prevNeighbour.Value.tone + shift, color, out _)) {
                                prevBatchim += $" {prevIMF[2]}";
                                // Apply alt CV
                            } else if (singer.TryGetMappedOto($"{prevBatchim} {ToHiragana(AltCv.ContainsKey(prevIMF[2]) ? AltCv[prevIMF[2]] : prevIMF[2])}", prevNeighbour.Value.tone + shift, color, out _)) {
                                prevBatchim += $" {ToHiragana(AltCv.ContainsKey(prevIMF[2]) ? AltCv[prevIMF[2]] : prevIMF[2])}";
                            } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(prevIMF[2]) ? AltCv[prevIMF[2]] : prevIMF[2])}", prevNeighbour.Value.tone + shift, color, out _)) {
                                prevBatchim = $"{ToHiragana(AltCv.ContainsKey(prevIMF[2]) ? AltCv[prevIMF[2]] : prevIMF[2])}";
                                // Apply conditional alt CV
                            } else if (singer.TryGetMappedOto($"{prevBatchim} {ToHiragana(ConditionalAlt.ContainsKey(prevIMF[2]) ? ConditionalAlt[prevIMF[2]] : prevIMF[2])}", prevNeighbour.Value.tone + shift, color, out _)) {
                                prevBatchim += $" {ToHiragana(ConditionalAlt.ContainsKey(prevIMF[2]) ? ConditionalAlt[prevIMF[2]] : prevIMF[2])}";
                            } else {
                                prevBatchim = $"{ToHiragana(ConditionalAlt.ContainsKey(prevIMF[2]) ? ConditionalAlt[prevIMF[2]] : prevIMF[2])}";
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(prevIMF[2])) {
                        if (Array.IndexOf(sonorants, prevIMF[2]) > -1) {
                            // Apply (conditional) alt CV
                            if (singer.TryGetMappedOto($"{prevIMF[2]} {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _) ||
                                singer.TryGetMappedOto($"{prevIMF[2]} {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                                prevConnect = prevIMF[2];
                            } else {
                                prevConnect = null;
                            }
                        } else if (prevBatchim.Contains("く") || prevBatchim.Contains("る") || prevBatchim.Contains("む") || prevBatchim.Contains("ぷ")) {
                            prevConnect = "u";
                        } else if (prevBatchim.Contains("と")) {
                            prevConnect = "o";
                        } else {
                            // Apply alt CV
                            if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _) ||
                                singer.TryGetMappedOto($"{ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                                prevConnect = "";
                                // Apply conditional alt CV
                            } else if (singer.TryGetMappedOto($"- {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _) ||
                                singer.TryGetMappedOto($"- {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                                prevConnect = "-";
                            } else {
                                prevConnect = null;
                            }
                        }
                    } else {
                        if (prevIMF[1][0] == 'w' || prevIMF[1][0] == 'y') {
                            prevConnect = prevIMF[1].Remove(0, 1);
                        } else {
                            prevConnect = prevIMF[1];
                        }
                    }
                    // Apply alt CV
                    if (singer.TryGetMappedOto($"{prevConnect} {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"{prevConnect} {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                    } else if (singer.TryGetMappedOto($"* {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"* {ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                    } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}";
                        // Apply conditional alt CV
                    } else if (singer.TryGetMappedOto($"{prevConnect} {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"{prevConnect} {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                    } else if (singer.TryGetMappedOto($"* {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift, color, out _)) {
                        currPhoneme = $"* {ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                    } else {
                        currPhoneme = $"{ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}";
                    }
                    if (prevNeighbour != null && (string.IsNullOrEmpty(prevIMF[2]) || !string.IsNullOrEmpty(prevIMF[2]) && prevIMF[2] == "n")
                        && (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", note.tone + shift1, color1, out _)
                        || singer.TryGetMappedOto($"{ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", note.tone + shift1, color1, out _))) {
                        int vcLength = 60;
                        int endTick = notes[^1].position + notes[^1].duration;
                        // totalDuration calculated on basis of previous note length
                        int totalDuration = prevNeighbour.Value.duration;
                        if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme)}", notes[0].tone + shift1, color1, out var oto)) {
                            if (oto.Overlap < 0) {
                                vcLength = -timeAxis.MsToTickAt(-(oto.Preutter - oto.Overlap), endTick);
                            } else {
                                vcLength = -timeAxis.MsToTickAt(-oto.Preutter, endTick);
                            }
                        } else if (singer.TryGetMappedOto($"{ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme)}", notes[0].tone + shift1, color1, out var otoCon)) {
                            if (otoCon.Overlap < 0) {
                                vcLength = -timeAxis.MsToTickAt(-(otoCon.Preutter - otoCon.Overlap), endTick);
                            } else {
                                vcLength = -timeAxis.MsToTickAt(-otoCon.Preutter, endTick);
                            }
                        }
                        // vcLength depends on the Vel of the current base note
                        vcLength = Convert.ToInt32(Math.Min(totalDuration / 2, vcLength * (attr1.consonantStretchRatio ?? GetParentConsonantStretchRatio())));

                        if (string.IsNullOrEmpty(prevIMF[2])) {
                            if (prevIMF[1][0] == 'w' || prevIMF[1][0] == 'y') {
                                prevConnect = prevIMF[1].Remove(0, 1);
                            } else {
                                prevConnect = prevIMF[1];
                            }
                        } else if (prevIMF[2] == "n") {
                            prevConnect = prevIMF[2];
                        }
                        string consonant = "";
                        // look for alt CV
                        if (consonantLookup.TryGetValue(ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme).FirstOrDefault().ToString() ?? string.Empty, out var con)
                            || (ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme).Length >= 2
                            && consonantLookup.TryGetValue(ToHiragana(AltCv.ContainsKey(currPhoneme) ? AltCv[currPhoneme] : currPhoneme).Substring(0, 2), out con))
                            ) {
                            consonant = con;
                            // look for conditional alt CV
                        } else if (consonantLookup.TryGetValue(ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme).FirstOrDefault().ToString() ?? string.Empty, out var con2)
                            || (ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme).Length >= 2
                            && consonantLookup.TryGetValue(ToHiragana(ConditionalAlt.ContainsKey(currPhoneme) ? ConditionalAlt[currPhoneme] : currPhoneme).Substring(0, 2), out con))) {
                            consonant = con2;
                        }
                        var vcPhoneme = $"{prevConnect} {consonant}";
                        var vcPhonemes = new string[] { vcPhoneme, "" };
                        // find potential substitute symbol
                        if (substituteLookup.TryGetValue(consonant ?? string.Empty, out con)) {
                            vcPhonemes[1] = $"{prevConnect} {con}";
                        }
                        if (singer.TryGetMappedOto(currPhoneme + alt1, note.tone + shift1, color1, out var oto0)) {
                            currPhoneme = oto0.Alias;
                        }
                        string secondPhoneme = (currIMF[1][0] == 'w' || currIMF[1][0] == 'y') ? currIMF[1].Remove(0, 1) : currIMF[1];
                        // Adjust Result if note has batchim
                        if (!string.IsNullOrEmpty(currIMF[2])) {
                            if (nextNeighbour == null) {
                                if (string.IsNullOrEmpty(currIMF[2])) {
                                    secondPhoneme += " R";
                                } else {
                                    if (currIMF[2] == "n") {
                                        if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                                            secondPhoneme += $" {ToHiragana(currIMF[2])}";
                                        } else if (singer.TryGetMappedOto($"* {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                                            secondPhoneme = $"* {ToHiragana(currIMF[2])}";
                                        } else {
                                            secondPhoneme = $"{ToHiragana(currIMF[2])}";
                                        }
                                    } else {
                                        if (singer.TryGetMappedOto($"{secondPhoneme} {currIMF[2]}", note.tone + shift, color, out _)) {
                                            secondPhoneme += $" {currIMF[2]}";
                                            // alt cv
                                        } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                            secondPhoneme += $" {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                                        } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                            secondPhoneme = $"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                                            // conditional alt
                                        } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                            secondPhoneme += $" {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                                        } else {
                                            secondPhoneme = $"{ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                                        }
                                    }
                                }
                            } else if (!string.IsNullOrEmpty(currIMF[2])) {
                                if (currIMF[2] == "n") {
                                    if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                                        secondPhoneme += $" {ToHiragana(currIMF[2])}";
                                    } else if (singer.TryGetMappedOto($"* {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                                        secondPhoneme = $"* {ToHiragana(currIMF[2])}";
                                    } else {
                                        secondPhoneme = $"{ToHiragana(currIMF[2])}";
                                    }
                                } else {
                                    if (singer.TryGetMappedOto($"{secondPhoneme} {currIMF[2]}", note.tone + shift, color, out _)) {
                                        secondPhoneme += $" {currIMF[2]}";
                                        // alt cv
                                    } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                        secondPhoneme += $" {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                                    } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                        secondPhoneme = $"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                                        // conditional alt
                                    } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                        secondPhoneme += $" {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                                    } else {
                                        secondPhoneme = $"{ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                                    }
                                }
                            }
                            int noteLength = 0;
                            for (int i = 0; i < notes.Length; i++) noteLength += notes[i].duration;
                            int secondPosition = Math.Max(noteLength - (nextNeighbour == null ? 120 : 180), noteLength / 2);
                            if (singer.TryGetMappedOto(currPhoneme + alt1, note.tone + shift1, color1, out oto0)) {
                                currPhoneme = oto0.Alias;
                            }
                            if (singer.TryGetMappedOto(vcPhoneme ?? vcPhonemes[1] ?? string.Empty + alt, prevNeighbour.Value.tone + shift, color, out var otoVc)) {
                                vcPhoneme = otoVc.Alias;
                            }
                            if (singer.TryGetMappedOto(secondPhoneme + alt2, note.tone + shift2, color2, out var oto3)) {
                                secondPhoneme = oto3.Alias;
                            }
                            if (singer.TryGetMappedOto(vcPhoneme, note.tone + shift, color, out _)
                                && singer.TryGetMappedOto(currPhoneme, note.tone + shift, color, out _)
                                && singer.TryGetMappedOto(secondPhoneme, note.tone + shift, color, out _)) {
                                return new Result {
                                    phonemes = new Phoneme[] {
                                    new Phoneme() {
                                        phoneme = vcPhoneme,
                                        position = -vcLength,
                                    },
                                    new Phoneme() {
                                        phoneme = currPhoneme
                                    },
                                    new Phoneme() {
                                        phoneme = secondPhoneme,
                                        position = secondPosition
                                    }
                                }
                                };
                            }
                        }
                        if (singer.TryGetMappedOto(vcPhoneme ?? vcPhonemes[1] ?? string.Empty + alt, prevNeighbour.Value.tone + shift, color, out var otoVcAlt1)) {
                            vcPhoneme = otoVcAlt1.Alias;
                        } else if (singer.TryGetMappedOto(vcPhoneme ?? vcPhonemes[1] ?? string.Empty, prevNeighbour.Value.tone + shift, color, out var otoVc)) {
                            vcPhoneme = otoVc.Alias;
                        }
                        if (singer.TryGetMappedOto(vcPhoneme, note.tone + shift, color, out _)
                            && singer.TryGetMappedOto(currPhoneme, note.tone + shift, color, out _)) {
                            return new Result {
                                phonemes = new Phoneme[] {
                                new Phoneme() {
                                    phoneme = vcPhoneme,
                                    position = -vcLength,
                                },
                                new Phoneme() {
                                    phoneme = currPhoneme
                                }
                                }
                            };
                        }
                    }
                }
            }
            // Return Result now if note has no batchim
            if (string.IsNullOrEmpty(currIMF[2])) {

                // Map alias (apply shift + color)
                if (singer.TryGetMappedOto(currPhoneme + alt, note.tone + shift, color, out var oto)) {
                    currPhoneme = oto.Alias;
                }
                return new Result {
                    phonemes = new Phoneme[] {
                new Phoneme() { phoneme = currPhoneme }
                }
                };
            }
            // Adjust Result if note has batchim
            else {
                string secondPhoneme = (currIMF[1][0] == 'w' || currIMF[1][0] == 'y') ? currIMF[1].Remove(0, 1) : currIMF[1];
                if (nextNeighbour == null) {
                    if (string.IsNullOrEmpty(currIMF[2])) {
                        secondPhoneme += " R";
                    } else {
                        if (currIMF[2] == "n") {
                            if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                                secondPhoneme += $" {ToHiragana(currIMF[2])}";
                            } else if (singer.TryGetMappedOto($"* {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                                secondPhoneme = $"* {ToHiragana(currIMF[2])}";
                            } else {
                                secondPhoneme = $"{ToHiragana(currIMF[2])}";
                            }
                        } else {
                            if (singer.TryGetMappedOto($"{secondPhoneme} {currIMF[2]}", note.tone + shift, color, out _)) {
                                secondPhoneme += $" {currIMF[2]}";
                                // alt cv
                            } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                secondPhoneme += $" {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                            } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                secondPhoneme = $"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                                // conditional alt
                            } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                                secondPhoneme += $" {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                            } else {
                                secondPhoneme = $"{ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                            }
                        }
                    }
                } else if (!string.IsNullOrEmpty(currIMF[2])) {
                    if (currIMF[2] == "n") {
                        if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                            secondPhoneme += $" {ToHiragana(currIMF[2])}";
                        } else if (singer.TryGetMappedOto($"* {ToHiragana(currIMF[2])}", note.tone + shift, color, out _)) {
                            secondPhoneme = $"* {ToHiragana(currIMF[2])}";
                        } else {
                            secondPhoneme = $"{ToHiragana(currIMF[2])}";
                        }
                    } else {
                        if (singer.TryGetMappedOto($"{secondPhoneme} {currIMF[2]}", note.tone + shift, color, out _)) {
                            secondPhoneme += $" {currIMF[2]}";
                            // alt cv
                        } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                            secondPhoneme += $" {ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                        } else if (singer.TryGetMappedOto($"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                            secondPhoneme = $"{ToHiragana(AltCv.ContainsKey(currIMF[2]) ? AltCv[currIMF[2]] : currIMF[2])}";
                            // conditional alt
                        } else if (singer.TryGetMappedOto($"{secondPhoneme} {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}", note.tone + shift, color, out _)) {
                            secondPhoneme += $" {ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                        } else {
                            secondPhoneme = $"{ToHiragana(ConditionalAlt.ContainsKey(currIMF[2]) ? ConditionalAlt[currIMF[2]] : currIMF[2])}";
                        }
                    }
                }
                int noteLength = 0;
                for (int i = 0; i < notes.Length; i++) noteLength += notes[i].duration;
                int secondPosition = Math.Max(noteLength - (nextNeighbour == null ? 120 : 180), noteLength / 2);
                // Map alias (apply shift + color)
                if (singer.TryGetMappedOto(currPhoneme + alt, note.tone + shift, color, out var oto)) {
                    currPhoneme = oto.Alias;
                }
                if (singer.TryGetMappedOto(secondPhoneme + alt1, note.tone + shift1, color1, out oto)) {
                    secondPhoneme = oto.Alias;
                }
                // Return Result
                return new Result {
                    phonemes = new Phoneme[] {
                new Phoneme() { phoneme = currPhoneme },
                new Phoneme() { phoneme = secondPhoneme, position = secondPosition }
                }
                };
            }
        }
        private string ToHiragana(string romaji) {
            var hiragana = WanaKana.ToHiragana(romaji);
            hiragana = hiragana.Replace("ゔ", "ヴ");
            return hiragana;
        }
    }
}
