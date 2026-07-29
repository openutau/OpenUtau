using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;


namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Base Phonemizer for Korean Phonemizers.
    /// <para>1. Can process Phoneme variation(음운 변동), through Hangeul.Variate().</para>
    /// <para>2. Can find Alias in oto, including Voice color etc, through FindInOto().</para>
    /// <para>3. Can manage .ini configuring, through implementing IniParser at child class. (Usage is in KoreanCVPhonemizer.cs)</para>
    /// <para>4. Can generate phonemes according to Phoneme hints.</para>
    /// </summary>
    public abstract class BaseKoreanPhonemizer : Phonemizer {
        
        protected USinger singer;

        protected int vcLengthShort = 90;

        protected static readonly string[] PLAIN_VOWELS = new string[]{"ㅏ", "ㅣ", "ㅜ", "ㅔ", "ㅗ", "ㅡ", "ㅓ", "ㅢ"};
        protected static readonly string[] SOFT_BATCHIMS = new string[]{"ㄴ", "ㄹ", "ㅇ"};
        protected static readonly string[] HARD_BATCHIMS = new string[]{"ㄱ", "ㄷ", "ㅂ", "ㅁ"};

        // this phonemizer will call ConvertPhonemes() when lyric is hanguel or additionalTest is true . (override to use)
        protected virtual bool additionalTest(string lyric) {
            return false;
        } 
        public override void SetSinger(USinger singer) => this.singer = singer;
        public string? FindInOto(USinger singer, string phoneme, Note note, bool nullIfNotFound = false) {
            // 음소와 노트를 입력받고, 다음계 및 보이스컬러 에일리어스를 적용한다. 
            // nullIfNotFound가 true이면 음소가 찾아지지 않을 때 음소가 아닌 null을 리턴한다.
            // nullIfNotFound가 false면 음소가 찾아지지 않을 때 그대로 음소를 반환
            string phonemeToReturn;
            var attr = note.phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
            string color = attr.voiceColor ?? GetParentVoiceColor();
            int toneShift = attr.toneShift ?? GetParentToneShift();
            int? alt = attr.alternate ?? GetParentAlternate();
            if (phoneme.Equals("")) {return phoneme;}

            if (singer.TryGetMappedOto(phoneme + alt, note.tone + toneShift, color, out var oto)) {
                phonemeToReturn = oto.Alias;
            } 
            else if (singer.TryGetMappedOto(phoneme, note.tone, color, out oto)) {
                phonemeToReturn = oto.Alias;
            } 
            else if (nullIfNotFound) {
                phonemeToReturn = null;
            } 
            else {
                phonemeToReturn = phoneme;
            }

            return phonemeToReturn;
        }
        
        /// <summary>
        /// <para>All child Korean Phonemizer have to do is implementing this (1). </para>
        /// <para> This Function manages phoneme conversion at Notes that are not in last position. </para>
        /// </summary>
        /// <param name="notes"></param>
        /// <param name="prev"></param>
        /// <param name="next"></param>
        /// <param name="prevNeighbour"></param>
        /// <param name="nextNeighbour"></param>
        /// <param name="prevNeighbours"></param>
        /// <returns>Same as BasePhonemizer.Process(), but just manages Notes that are not in last position.</returns>
        public virtual Result ConvertPhonemes(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            // below return is Dummy
            return new Result() {
                phonemes = new Phoneme[] {
                        new Phoneme { phoneme = $""},
                    }
            };
        }

        /// <summary>
        /// <para>All child Korean Phonemizer have to do is implementing this (2). </para>
        /// <para> This Function manages phoneme conversion at Note in last position. </para>
        /// </summary>
        /// <param name="notes"></param>
        /// <param name="prev"></param>
        /// <param name="next"></param>
        /// <param name="prevNeighbour"></param>
        /// <param name="nextNeighbour"></param>
        /// <param name="prevNeighbours"></param>
        /// <returns>Same as BasePhonemizer.Process(), but just manages Note that in last position.</returns>
        public virtual Result GenerateEndSound(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            // below return is Dummy
            return new Result() {
                phonemes = new Phoneme[] {
                        new Phoneme { phoneme = $""},
                    }
            };
        }

        /// <summary>
        /// Returns Result with two input Phonemes. 
        /// <param name="firstPhoneme"></param>
        /// <param name="secondPhoneme"></param>
        /// <param name="totalDuration"></param>
        /// <param name="totalDurationDivider"></param>
        /// </summary>
        public Result GenerateResult(String firstPhoneme, String secondPhoneme, int totalDuration, int secondPhonemePosition, int totalDurationDivider=3){
            return new Result() {
                phonemes = new Phoneme[] {
                    new Phoneme { phoneme = firstPhoneme },
                    new Phoneme { phoneme = secondPhoneme,
                    position = totalDuration - Math.Min(totalDuration / totalDurationDivider, secondPhonemePosition)},
                }
            };
        }

        /// <summary>
        /// Returns Result with two input Phonemes. 
        /// <param name="firstPhoneme"></param>
        /// <param name="secondPhoneme"></param>
        /// <param name="totalDuration"></param>
        /// <param name="totalDurationDivider"></param>
        /// </summary>
        public Result GenerateResult(String firstPhoneme, String secondPhoneme, int totalDuration, int totalDurationDivider=3){
            return new Result() {
                phonemes = new Phoneme[] {
                    new Phoneme { phoneme = firstPhoneme },
                    new Phoneme { phoneme = secondPhoneme,
                    position = totalDuration - totalDuration / totalDurationDivider},
                }
            };
            
        }

        /// <summary>
        /// Returns Result with one input Phonemes. 
        /// </summary>
        public Result GenerateResult(String firstPhoneme){
            return new Result() {
                phonemes = new Phoneme[] {
                    new Phoneme { phoneme = firstPhoneme },
                }
            };
        }

        /// <summary>
        /// Returns Result with three input Phonemes. 
        /// 
        /// </summary>
        /// <param name="firstPhoneme"></param>
        /// <param name="secondPhoneme"></param>
        /// <param name="thirdPhoneme"></param>
        /// <param name="totalDuration"></param>
        /// <param name="secondPhonemePosition"></param>
        /// <param name="secondTotalDurationDivider"></param>
        /// <param name="thirdTotalDurationDivider"></param>
        /// <returns> Result  </returns>
        public Result GenerateResult(String firstPhoneme, String secondPhoneme, String thirdPhoneme, int totalDuration, int secondPhonemePosition, int secondTotalDurationDivider=3, int thirdTotalDurationDivider=8){
            return new Result() {
                phonemes = new Phoneme[] {
                    new Phoneme { phoneme = firstPhoneme},
                    new Phoneme { phoneme = secondPhoneme,
                    position = totalDuration - Math.Min(totalDuration / secondTotalDurationDivider, secondPhonemePosition)},
                    new Phoneme { phoneme = thirdPhoneme,
                    position = totalDuration - totalDuration / thirdTotalDurationDivider},
                }// -음소 있이 이어줌
            };
        }

        public class ProcessResult {
            public Note[] KoreanLyricNotes { get; set; }
            public Note? KoreanLyricPrevNote { get; set; }
            public Note? KoreanLyricNextNote { get; set; }
        }

        // <summary>
        /// The Romanized lyrics will be completely converted to Hangul.
        /// 로마자 가사들을 전부 한글로 바꿔줍니다.
        /// </summary>
        /// <param name="notes"></param>
        /// <param name="prevNote"></param>
        /// <param name="nextNote"></param>
        /// <returns> ProcessResult? </returns>
        public static ProcessResult? ConvertRomajiNoteToHangeul(Note[] notes, Note? prevNote, Note? nextNote) {
            var note = notes[0];
            var lyric = note.lyric;

            if (KoreanPhonemizerUtil.IsKoreanRomaji(lyric)) { // TODO
                notes[0] = new Note() {
                    lyric = KoreanPhonemizerUtil.TryParseKoreanRomaji(lyric),
                    phoneticHint = note.phoneticHint,
                    position = note.position,
                    duration = note.duration,
                    tone = note.tone,
                    phonemeAttributes = note.phonemeAttributes
                };
                Note? prevNoteNew = prevNote;
                Note? nextNoteNew = nextNote;
                if (prevNote != null) {
                    if (KoreanPhonemizerUtil.IsHangeul(prevNote.Value.lyric) != null) {
                        if (prevNote != null) {
                            prevNoteNew = new Note() {
                                lyric = KoreanPhonemizerUtil.TryParseKoreanRomaji(prevNote.Value.lyric),
                                phoneticHint = prevNote.Value.phoneticHint,
                                position = prevNote.Value.position,
                                duration = prevNote.Value.duration,
                                tone = prevNote.Value.tone,
                                phonemeAttributes = prevNote.Value.phonemeAttributes
                            };
                        }
                        if (nextNote != null) {
                            nextNoteNew = new Note() {
                                lyric = KoreanPhonemizerUtil.TryParseKoreanRomaji(nextNote.Value.lyric),
                                phoneticHint = nextNote.Value.phoneticHint,
                                position = nextNote.Value.position,
                                duration = nextNote.Value.duration,
                                tone = nextNote.Value.tone,
                                phonemeAttributes = nextNote.Value.phonemeAttributes
                            };

                        }
                        
                    }
                }
                return new ProcessResult {
                    KoreanLyricNotes = notes,
                    KoreanLyricPrevNote = prevNoteNew,
                    KoreanLyricNextNote = nextNoteNew,
                };
            }
            return null;
        }

        // <summary>
        /// It analyzes the phonetic hint and returns the result.
        /// 발음 힌트를 분석하여 Result를 반환합니다.
        /// </summary>
        /// <param name="singer"></param>
        /// <param name="note"></param>
        /// <param name="totalDuration"></param>
        /// <returns> Result? </returns>
        public  Result? RenderPhoneticHint(USinger singer, Note note, int totalDuration) {
            var phoneticHint = note.phoneticHint;

            if (phoneticHint != null) {
                // if there are phonetic hint
                // 발음 힌트가 있음 
                // 냥[nya2, ang]
                string[] phoneticHints = phoneticHint.Split(','); // phonemes are seperated by ','.
                int phoneticHintsLength = phoneticHints.Length;

                Phoneme[] phonemes = new Phoneme[phoneticHintsLength];

                Dictionary<string, string> VVdictionary = new Dictionary<string, string>() { };

                string[] VVsource = new string[] { "a", "i", "u", "e", "o", "eo", "eu" };

                for (int i = 0; i < 7; i++) {
                    // VV 딕셔너리를 채운다
                    // 나중에 발음기호에 ["a a"]를 입력하고 만일 음원에게 "a a"가 없을 경우, 자동으로 VVDictionary에서 "a a"에 해당하는 값인 "a"를 호출해 사용
                    // (반대도 똑같이 적용)

                    // VVDictionary 예시: {"a a", "a"} ...
                    for (int j = 6; j >= 0; j--) {
                        VVdictionary[$"{VVsource[i]} {VVsource[j]}"] = $"{VVsource[j]}"; // CV/CVC >> CBNN 호환용
                        VVdictionary[$"{VVsource[j]}"] = $"{VVsource[i]} {VVsource[j]}"; // CBNN >> CV/CVC 호환용
                    }
                }

                for (int i = 0; i < phoneticHintsLength; i++) {
                    string? alias = FindInOto(singer, phoneticHints[i].Trim(), note, true); // alias if exists, otherwise null

                    if (alias != null) {
                        // 발음기호에 입력된 phoneme이 음원에 존재함

                        if (i == 0) {
                            // first syllable
                            phonemes[i] = new Phoneme { phoneme = alias };
                        } 
                        else if ((i == phoneticHintsLength - 1) && ((phoneticHints[i].Trim().EndsWith('-')) || phoneticHints[i].Trim().EndsWith('R'))) {
                            // 마지막 음소이고 끝음소(ex: a -, a R)일 경우, VCLengthShort에 맞춰 음소를 배치
                            phonemes[i] = new Phoneme {
                                phoneme = alias,
                                position = totalDuration - Math.Min(vcLengthShort, totalDuration / 8)
                                // 8등분한 길이로 끝에 숨소리 음소 배치, n등분했을 때의 음소 길이가 이보다 작다면 n등분했을 때의 길이로 간다
                            };
                        } 
                        else if (phoneticHintsLength == 2) {
                            // 입력되는 발음힌트가 2개일 경우, 2등분되어 음소가 배치된다.
                            // 이 경우 부자연스러우므로 3등분해서 음소 배치하게 조정
                            phonemes[i] = new Phoneme {
                                phoneme = alias,
                                position = totalDuration - totalDuration / 3
                                // 3등분해서 음소가 배치됨
                            };
                        } 
                        else {
                            phonemes[i] = new Phoneme {
                                phoneme = alias,
                                position = totalDuration - ((totalDuration / phoneticHintsLength) * (phoneticHintsLength - i))
                                // 균등하게 n등분해서 음소가 배치됨
                            };
                        }
                    } else if (VVdictionary.ContainsKey(phoneticHints[i].Trim())) {
                        // 입력 실패한 음소가 VV 혹은 V일 때
                        if (phoneticHintsLength == 2) {
                            // 입력되는 발음힌트가 2개일 경우, 2등분되어 음소가 배치된다.
                            // 이 경우 부자연스러우므로 3등분해서 음소 배치하게 조정
                            phonemes[i] = new Phoneme {
                                phoneme = FindInOto(singer, VVdictionary[phoneticHints[i].Trim()], note),
                                position = totalDuration - totalDuration / 3
                                // 3등분해서 음소가 배치됨
                            };
                        } 
                        else {
                            phonemes[i] = new Phoneme {
                                phoneme = FindInOto(singer, VVdictionary[phoneticHints[i].Trim()], note),
                                position = totalDuration - ((totalDuration / phoneticHintsLength) * (phoneticHintsLength - i))
                                // 균등하게 n등분해서 음소가 배치됨
                            };
                        }
                    } else {
                        // 그냥 음원에 음소가 없음
                        phonemes[i] = new Phoneme {
                            phoneme = phoneticHints[i].Trim(),
                            position = totalDuration - ((totalDuration / phoneticHintsLength) * (phoneticHintsLength - i))
                            // 균등하게 n등분해서 음소가 배치됨
                        };
                    }
                }

                return new Result() {
                    phonemes = phonemes
                };
            }
            return null;
        }

        /// <summary>
        /// Returns Result with three input Phonemes. 
        /// 
        /// </summary>
        /// <param name="firstPhoneme"></param>
        /// <param name="secondPhoneme"></param>
        /// <param name="thirdPhoneme"></param>
        /// <param name="totalDuration"></param>
        /// <param name="secondPhonemePosition"></param>
        /// <param name="secondTotalDurationDivider"></param>
        /// <param name="thirdTotalDurationDivider"></param>
        /// <returns> Result  </returns>
        public Result GenerateResult(String firstPhoneme, String secondPhoneme, String thirdPhoneme, int totalDuration, int secondTotalDurationDivider=3, int thirdTotalDurationDivider=8){
            return new Result() {
                phonemes = new Phoneme[] {
                    new Phoneme { phoneme = firstPhoneme},
                    new Phoneme { phoneme = secondPhoneme,
                    position = totalDuration - totalDuration / secondTotalDurationDivider},
                    new Phoneme { phoneme = thirdPhoneme,
                    position = totalDuration - totalDuration / thirdTotalDurationDivider},
                }// -음소 있이 이어줌
            };
        }
        /// <summary>
        /// <para> It AUTOMATICALLY generates phonemes based on phoneme hints (each phonemes should be separated by ",". (Example: [a, a i, ya])) </para>
        /// <para> But it can't generate phonemes automatically, so should implement ConvertPhonemes() Method in child class. </para>
        /// <para> Also it can't generate Endsounds automatically, so should implement GenerateEndSound() Method in child class.</para>
        /// </summary>
        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            Note note = notes[0];
            string lyric = note.lyric;

            Note? prevNote = prevNeighbour; // null or Note
            Note thisNote = note;
            Note? nextNote = nextNeighbour; // null or Note

            int totalDuration = notes.Sum(n => n.duration);

            var phoneticHint = RenderPhoneticHint(singer, note, totalDuration);
            if (phoneticHint != null) {
                return (Result) phoneticHint;
            }

            var romaji2Korean = ConvertRomajiNoteToHangeul(notes, prevNeighbour, nextNeighbour);
            if (romaji2Korean != null) {
                return ConvertPhonemes(romaji2Korean.KoreanLyricNotes, prev, next, romaji2Korean.KoreanLyricPrevNote, romaji2Korean.KoreanLyricNextNote, prevNeighbours);
            }

            if (KoreanPhonemizerUtil.IsHangeul(lyric) || !KoreanPhonemizerUtil.IsHangeul(lyric) && additionalTest(lyric)) {
                return ConvertPhonemes(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours);
            } 
            else {
                return GenerateEndSound(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours);
            }
        }

        
        /// <summary>
        /// abstract class for Ini Management
        /// To use, child phonemizer should implement this class(BaseIniManager) with its own setting values!
        /// </summary>
        public abstract class BaseIniManager : KoreanPhonemizerUtil.BaseIniManager{}
    }
}
