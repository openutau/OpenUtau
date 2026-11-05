using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;

namespace OpenUtau.Plugin.KoreanCplusV
{
    [Phonemizer("Korean C+V Phonemizer", "KO C+V", "SODAsoo", language: "KO")]
    public class KoreanCVPhonemizer : BaseKoreanPhonemizer
    {
        private const int HangulSyllableStart = 0xAC00;
        private const int HangulSyllableEnd = 0xD7A3;
        private const string PronunciationDictionaryFileName = "kr-CpV-dict.yaml";

        private static readonly object DictionaryLock = new();
        private static string? cachedDictionaryPath;
        private static DateTime cachedDictionaryWriteTimeUtc;
        private static Dictionary<string, string[]> cachedPronunciationOverrides = new(StringComparer.Ordinal);

        private static readonly string DefaultPronunciationDictionaryText =
            "# Korean C+V pronunciation override dictionary\n" +
            "# This file is created automatically in the singer folder.\n" +
            "# Edit values when the automatic pronunciation rules sound unnatural.\n" +
            "# Format: KoreanText: phoneme tokens\n" +
            "# Initial consonants: g kk n d tt r m b pp s ss j jj ch k t p h y w\n" +
            "# Vowels: a eo o u eu i e ya yeo yo yu yae ye wa we wo wi ui\n" +
            "# Finals: K N T L M P NG\n" +
            "entries:\n" +
            "  꽃잎: kk o T n i P\n" +
            "  꽃잎이: kk o T n i p i\n" +
            "  꽃잎은: kk o T n i p eu N\n" +
            "  꽃잎을: kk o T n i p eu L\n" +
            "  나뭇잎: n a M n u T n i P\n" +
            "  나뭇잎이: n a M n u T n i p i\n" +
            "  깻잎: kk e T n i P\n" +
            "  깻잎이: kk e T n i p i\n" +
            "  앞일: a M n i L\n" +
            "  앞일이: a M n i r i\n";

        private static readonly Dictionary<string, DiphthongFallback> DiphthongFallbacks = new(StringComparer.Ordinal)
        {
            ["ya"] = new("y", "i", "a"),
            ["yeo"] = new("y", "i", "eo"),
            ["yo"] = new("y", "i", "o"),
            ["yu"] = new("y", "i", "u"),
            ["yae"] = new("y", "i", "e"),
            ["ye"] = new("y", "i", "e"),
            ["wa"] = new("w", "u", "a"),
            ["we"] = new("w", "u", "e"),
            ["wo"] = new("w", "u", "eo"),
            ["wi"] = new("w", "u", "i"),
            ["ui"] = new(null, "eu", "i"),
        };

        // Unicode order for complete Hangul syllable decomposition.
        private static readonly char[] InitialJamoOrder =
        {
            'ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ',
            'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ',
        };

        private static readonly char[] MedialJamoOrder =
        {
            'ㅏ', 'ㅐ', 'ㅑ', 'ㅒ', 'ㅓ', 'ㅔ', 'ㅕ', 'ㅖ', 'ㅗ', 'ㅘ',
            'ㅙ', 'ㅚ', 'ㅛ', 'ㅜ', 'ㅝ', 'ㅞ', 'ㅟ', 'ㅠ', 'ㅡ', 'ㅢ', 'ㅣ',
        };

        private static readonly string[] FinalJamoOrder =
        {
            "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ",
            "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ",
            "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ",
        };

        // 자음 매핑 (초성)
        private static readonly Dictionary<char, string> initialConsonants = new()
        {
            ['ㄱ'] = "- g", ['ㄴ'] = "- n", ['ㄷ'] = "- d", ['ㄹ'] = "- r", ['ㅁ'] = "- m",
            ['ㅂ'] = "- b", ['ㅅ'] = "- s", ['ㅇ'] = "-", ['ㅈ'] = "- j", ['ㅊ'] = "- ch",
            ['ㅋ'] = "- k", ['ㅌ'] = "- t", ['ㅍ'] = "- p", ['ㅎ'] = "- h",
            ['ㄲ'] = "- kk", ['ㄸ'] = "- tt", ['ㅃ'] = "- pp", ['ㅆ'] = "- ss", ['ㅉ'] = "- jj"
        };

        // 단모음 매핑
        private static readonly Dictionary<char, string> vowels = new()
        {
            ['ㅏ'] = "- a", ['ㅓ'] = "- eo", ['ㅗ'] = "- o", ['ㅜ'] = "- u",
            ['ㅡ'] = "- eu", ['ㅣ'] = "- i", ['ㅔ'] = "- e", ['ㅐ'] = "- e"
        };

        // 이중모음 매핑
        private static readonly Dictionary<char, string> diphthongs = new()
        {
            ['ㅑ'] = "- ya", ['ㅕ'] = "- yeo", ['ㅛ'] = "- yo", ['ㅠ'] = "- yu",
            ['ㅒ'] = "- yae", ['ㅖ'] = "- ye", ['ㅘ'] = "- wa", ['ㅙ'] = "- we",
            ['ㅚ'] = "- we", ['ㅝ'] = "- wo", ['ㅞ'] = "- we", ['ㅟ'] = "- wi", ['ㅢ'] = "- ui"
        };

        // 받침 매핑 (겹받침 포함)
        private static readonly Dictionary<string, string> finalConsonantsOnly = new(StringComparer.Ordinal)
        {
            ["ㄱ"] = "K", ["ㄲ"] = "K", ["ㄳ"] = "K", ["ㅋ"] = "K", ["ㄺ"] = "K",
            ["ㄴ"] = "N", ["ㄵ"] = "N", ["ㄶ"] = "N",
            ["ㄷ"] = "T", ["ㅅ"] = "T", ["ㅆ"] = "T", ["ㅈ"] = "T", ["ㅊ"] = "T", ["ㅌ"] = "T", ["ㅎ"] = "T",
            ["ㄹ"] = "L", ["ㄼ"] = "L", ["ㄽ"] = "L", ["ㄾ"] = "L", ["ㅀ"] = "L",
            ["ㅁ"] = "M", ["ㄻ"] = "M",
            ["ㅂ"] = "P", ["ㅄ"] = "P", ["ㅍ"] = "P", ["ㄿ"] = "P",
            ["ㅇ"] = "NG",
        };

        public override Result ConvertPhonemes(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours)
        {
            if (notes.Length == 0)
            {
                return new Result { phonemes = Array.Empty<Phoneme>() };
            }

            var note = notes[0];
            var lyric = note.lyric?.Trim() ?? string.Empty;
            int totalDuration = notes.Sum(n => n.duration);

            // 확장자 노트 처리
            if (lyric.StartsWith("+"))
            {
                return new Result { phonemes = Array.Empty<Phoneme>() };
            }

            if (TryGetPronunciationOverride(lyric, prevNeighbour, nextNeighbour, out var overrideSyllables))
            {
                return BuildPronouncedResult(overrideSyllables, note, totalDuration);
            }

            // 한글 분해
            var decomposed = DecomposeHangul(lyric);

            // 파싱 실패(비한글/미지원 문자) 시 원문 alias를 그대로 찾는다.
            if (decomposed.Count == 0)
            {
                return new Result
                {
                    phonemes = new[]
                    {
                        new Phoneme { index = 0, phoneme = ResolveMappedAlias(lyric, note, 0), position = 0 },
                    },
                };
            }

            var prevContext = GetLastHangul(prevNeighbour, prev);
            var nextContext = GetFirstHangul(nextNeighbour, next);
            var pronounced = ApplyPronunciationRules(decomposed, prevContext, nextContext);

            return BuildPronouncedResult(pronounced, note, totalDuration);
        }

        public override Result GenerateEndSound(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours)
        {
            return ConvertPhonemes(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours);
        }

        private Result BuildPronouncedResult(IReadOnlyList<PronouncedSyllable> pronounced, Note note, int totalDuration)
        {
            var rawPhonemes = new List<Phoneme>();
            int syllableCount = pronounced.Count;
            for (int i = 0; i < syllableCount; i++)
            {
                var syllable = pronounced[i];
                int syllableStart = totalDuration * i / syllableCount;
                int syllableEnd = totalDuration * (i + 1) / syllableCount;
                int syllableDuration = Math.Max(1, syllableEnd - syllableStart);
                rawPhonemes.AddRange(ProcessSyllable(syllable, syllableDuration, syllableStart, note, rawPhonemes.Count));
            }

            var mappedPhonemes = new List<Phoneme>(rawPhonemes.Count);
            for (int i = 0; i < rawPhonemes.Count; i++)
            {
                var phoneme = rawPhonemes[i];
                mappedPhonemes.Add(new Phoneme
                {
                    index = i,
                    phoneme = ResolveMappedAlias(phoneme.phoneme, note, i),
                    position = phoneme.position,
                });
            }

            return new Result { phonemes = mappedPhonemes.ToArray() };
        }

        private List<HangulSyllable> DecomposeHangul(string text)
        {
            var result = new List<HangulSyllable>();

            foreach (char c in text)
            {
                if (c >= HangulSyllableStart && c <= HangulSyllableEnd)
                {
                    int unicode = c - HangulSyllableStart;
                    int initial = unicode / (MedialJamoOrder.Length * 28);
                    int medial = (unicode % (MedialJamoOrder.Length * 28)) / 28;
                    int final = unicode % 28;

                    result.Add(new HangulSyllable
                    {
                        InitialJamo = GetInitialJamo(initial),
                        MedialJamo = GetMedialJamo(medial),
                        Initial = GetInitialConsonant(initial),
                        Medial = GetMedialVowel(medial),
                        Final = final > 0 ? GetFinalConsonant(final - 1) : null
                    });
                }
            }

            return result;
        }

        private List<PronouncedSyllable> ApplyPronunciationRules(
            IReadOnlyList<HangulSyllable> syllables,
            HangulSyllable? prevContext,
            HangulSyllable? nextContext)
        {
            var result = new List<PronouncedSyllable>(syllables.Count);

            for (int i = 0; i < syllables.Count; i++)
            {
                var current = syllables[i];
                var prev = i > 0 ? syllables[i - 1] : prevContext;
                var next = i + 1 < syllables.Count ? syllables[i + 1] : nextContext;

                result.Add(new PronouncedSyllable
                {
                    Initial = GetPronouncedInitial(current, prev),
                    Medial = current.Medial,
                    Final = GetPronouncedFinal(current, next),
                });
            }

            return result;
        }

        private List<Phoneme> ProcessSyllable(
            PronouncedSyllable syllable,
            int syllableDuration,
            int syllableOffset,
            Note note,
            int phonemeIndexOffset)
        {
            var phonemes = new List<Phoneme>();
            
            // 무성음 자음 기본 길이
            int consonantDuration = 25; 
            
            // 유성 자음(n, r, m)일 경우 타이밍
            if (syllable.Initial == "- n" || syllable.Initial == "- r" || syllable.Initial == "- m")
            {
                consonantDuration = 20;
            }

            if (!string.IsNullOrEmpty(syllable.Initial) && syllable.Initial != "-")
            {
                phonemes.Add(new Phoneme { phoneme = syllable.Initial, position = syllableOffset });
                if (!string.IsNullOrEmpty(syllable.Medial))
                {
                    int vowelPosition = syllableOffset + Math.Min(consonantDuration, Math.Max(0, syllableDuration - 1));
                    AddMedialPhonemes(phonemes, syllable.Medial, vowelPosition, syllableDuration, note, phonemeIndexOffset, startingAlias: false);
                }
            }
            else if (!string.IsNullOrEmpty(syllable.Medial))
            {
                AddMedialPhonemes(phonemes, syllable.Medial, syllableOffset, syllableDuration, note, phonemeIndexOffset, startingAlias: true);
            }

            if (!string.IsNullOrEmpty(syllable.Final))
            {
                string finalPhoneme = finalConsonantsOnly.TryGetValue(syllable.Final, out var mappedFinal)
                    ? mappedFinal
                    : syllable.Final;
                
                // 받침 기본 위치 설정
                int finalDuration = 40;
                
                // M, L, NG의 경우 타이밍을 짧게 설정
                if (finalPhoneme == "M" || finalPhoneme == "L" || finalPhoneme == "NG")
                {
                    finalDuration = 20; 
                }
                else if (finalPhoneme == "N")
                {
                    finalDuration = 13;
                }

                // 받침 위치를 노트 길이에 따라 동적으로 조정
                int finalPosition = syllableOffset + Math.Max(0, syllableDuration - finalDuration);
                phonemes.Add(new Phoneme { phoneme = finalPhoneme, position = finalPosition });
            }

            return phonemes;
        }

        private void AddMedialPhonemes(
            List<Phoneme> phonemes,
            string medial,
            int position,
            int syllableDuration,
            Note note,
            int phonemeIndexOffset,
            bool startingAlias)
        {
            string vowel = medial.StartsWith("- ", StringComparison.Ordinal) ? medial[2..] : medial;
            if (!DiphthongFallbacks.TryGetValue(vowel, out var fallback))
            {
                phonemes.Add(new Phoneme
                {
                    phoneme = startingAlias ? medial : vowel,
                    position = position,
                });
                return;
            }

            int phonemeIndex = phonemeIndexOffset + phonemes.Count;
            var fullCandidates = BuildAliasCandidates(vowel, startingAlias);
            if (!CanCheckMappedAliases())
            {
                phonemes.Add(new Phoneme { phoneme = fullCandidates[0], position = position });
                return;
            }
            if (TryPickMappedAlias(fullCandidates, note, phonemeIndex, out var fullAlias))
            {
                phonemes.Add(new Phoneme { phoneme = fullAlias, position = position });
                return;
            }

            int secondPosition = position + Math.Min(GetDiphthongLeadDuration(syllableDuration), Math.Max(0, syllableDuration - 1));
            if (!string.IsNullOrEmpty(fallback.Semivowel))
            {
                var semivowelCandidates = BuildAliasCandidates(fallback.Semivowel, startingAlias);
                var mainVowelCandidates = BuildAliasCandidates(fallback.MainVowel, startingAlias: false);
                if (TryPickMappedAlias(semivowelCandidates, note, phonemeIndex, out var semivowelAlias)
                    && TryPickMappedAlias(mainVowelCandidates, note, phonemeIndex + 1, out var mainAlias))
                {
                    phonemes.Add(new Phoneme { phoneme = semivowelAlias, position = position });
                    phonemes.Add(new Phoneme { phoneme = mainAlias, position = secondPosition });
                    return;
                }
            }

            var shortVowelCandidates = BuildAliasCandidates(fallback.ShortVowel, startingAlias);
            var fallbackMainVowelCandidates = BuildAliasCandidates(fallback.MainVowel, startingAlias: false);
            if (TryPickMappedAlias(shortVowelCandidates, note, phonemeIndex, out var shortVowelAlias)
                && TryPickMappedAlias(fallbackMainVowelCandidates, note, phonemeIndex + 1, out var fallbackMainAlias))
            {
                phonemes.Add(new Phoneme { phoneme = shortVowelAlias, position = position });
                phonemes.Add(new Phoneme { phoneme = fallbackMainAlias, position = secondPosition });
                return;
            }

            phonemes.Add(new Phoneme { phoneme = fullCandidates[0], position = position });
        }

        private string[] BuildAliasCandidates(string alias, bool startingAlias)
        {
            return startingAlias
                ? new[] { "- " + alias, alias }
                : new[] { alias, "- " + alias };
        }

        private int GetDiphthongLeadDuration(int syllableDuration)
        {
            return Math.Clamp(syllableDuration / 6, 12, 35);
        }

        private bool CanCheckMappedAliases()
        {
            return singer != null;
        }

        private bool TryPickMappedAlias(IEnumerable<string> candidates, Note note, int phonemeIndex, out string alias)
        {
            foreach (var candidate in candidates)
            {
                if (HasMappedAlias(candidate, note, phonemeIndex))
                {
                    alias = candidate;
                    return true;
                }
            }

            alias = string.Empty;
            return false;
        }

        private bool HasMappedAlias(string phoneme, Note note, int phonemeIndex)
        {
            if (singer == null)
            {
                return false;
            }

            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == phonemeIndex) ?? default;
            string alt = attr.alternate?.ToString() ?? string.Empty;
            string color = attr.voiceColor ?? string.Empty;
            int shiftedTone = note.tone + attr.toneShift;

            if (singer.TryGetMappedOto(phoneme + alt, shiftedTone, color, out _)
                || singer.TryGetMappedOto(phoneme, shiftedTone, color, out _))
            {
                return true;
            }
            if (attr.toneShift != 0
                && (singer.TryGetMappedOto(phoneme + alt, note.tone, color, out _)
                    || singer.TryGetMappedOto(phoneme, note.tone, color, out _)))
            {
                return true;
            }

            return singer.TryGetMappedOto(phoneme + alt, shiftedTone, out _)
                || singer.TryGetMappedOto(phoneme, shiftedTone, out _);
        }

        private bool TryGetPronunciationOverride(
            string lyric,
            Note? prevNeighbour,
            Note? nextNeighbour,
            out List<PronouncedSyllable> syllables)
        {
            syllables = new List<PronouncedSyllable>();
            if (string.IsNullOrWhiteSpace(lyric))
            {
                return false;
            }

            var overrides = LoadPronunciationOverrides();
            if (!overrides.TryGetValue(lyric, out var tokens))
            {
                return TryGetContextualPronunciationOverride(lyric, prevNeighbour, nextNeighbour, overrides, out syllables);
            }

            syllables = BuildSyllablesFromDictionaryTokens(tokens);
            return syllables.Count > 0;
        }

        private bool TryGetContextualPronunciationOverride(
            string lyric,
            Note? prevNeighbour,
            Note? nextNeighbour,
            Dictionary<string, string[]> overrides,
            out List<PronouncedSyllable> syllables)
        {
            syllables = new List<PronouncedSyllable>();
            if (DecomposeHangul(lyric).Count != 1)
            {
                return false;
            }

            string prevText = prevNeighbour == null ? string.Empty : GetNoteText(prevNeighbour.Value);
            string nextText = nextNeighbour == null ? string.Empty : GetNoteText(nextNeighbour.Value);

            if (DecomposeHangul(prevText).Count == 1 && DecomposeHangul(nextText).Count == 1
                && TryGetDictionarySyllable(prevText + lyric + nextText, 1, overrides, out var tripleCurrent))
            {
                syllables.Add(tripleCurrent);
                return true;
            }

            if (DecomposeHangul(prevText).Count == 1
                && TryGetDictionarySyllable(prevText + lyric, 1, overrides, out var pairCurrentAfterPrev))
            {
                syllables.Add(pairCurrentAfterPrev);
                return true;
            }

            if (DecomposeHangul(nextText).Count == 1
                && TryGetDictionarySyllable(lyric + nextText, 0, overrides, out var pairCurrentBeforeNext))
            {
                syllables.Add(pairCurrentBeforeNext);
                return true;
            }

            return false;
        }

        private bool TryGetDictionarySyllable(
            string key,
            int syllableIndex,
            Dictionary<string, string[]> overrides,
            out PronouncedSyllable syllable)
        {
            syllable = new PronouncedSyllable();
            if (!overrides.TryGetValue(key, out var tokens))
            {
                return false;
            }

            var parsed = BuildSyllablesFromDictionaryTokens(tokens);
            if (syllableIndex < 0 || syllableIndex >= parsed.Count)
            {
                return false;
            }

            syllable = parsed[syllableIndex];
            return true;
        }

        private Dictionary<string, string[]> LoadPronunciationOverrides()
        {
            string? dictionaryPath = GetPronunciationDictionaryPath();
            if (string.IsNullOrEmpty(dictionaryPath))
            {
                return new Dictionary<string, string[]>(StringComparer.Ordinal);
            }

            lock (DictionaryLock)
            {
                EnsurePronunciationDictionary(dictionaryPath);
                var writeTime = File.Exists(dictionaryPath)
                    ? File.GetLastWriteTimeUtc(dictionaryPath)
                    : DateTime.MinValue;

                if (cachedDictionaryPath == dictionaryPath && cachedDictionaryWriteTimeUtc == writeTime)
                {
                    return cachedPronunciationOverrides;
                }

                cachedDictionaryPath = dictionaryPath;
                cachedDictionaryWriteTimeUtc = writeTime;
                cachedPronunciationOverrides = ParsePronunciationDictionary(dictionaryPath);
                return cachedPronunciationOverrides;
            }
        }

        private string? GetPronunciationDictionaryPath()
        {
            if (singer == null || !singer.Found || !singer.Loaded || string.IsNullOrWhiteSpace(singer.Location))
            {
                return null;
            }
            return Path.Combine(singer.Location, PronunciationDictionaryFileName);
        }

        private void EnsurePronunciationDictionary(string dictionaryPath)
        {
            if (File.Exists(dictionaryPath))
            {
                return;
            }

            string? directory = Path.GetDirectoryName(dictionaryPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(dictionaryPath, DefaultPronunciationDictionaryText, new UTF8Encoding(false));
        }

        private Dictionary<string, string[]> ParsePronunciationDictionary(string dictionaryPath)
        {
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (!File.Exists(dictionaryPath))
            {
                return result;
            }

            foreach (var rawLine in File.ReadAllLines(dictionaryPath, Encoding.UTF8))
            {
                string line = StripInlineComment(rawLine).Trim();
                if (line.Length == 0 || line == "entries:" || line.EndsWith(":", StringComparison.Ordinal))
                {
                    continue;
                }

                int colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                {
                    continue;
                }

                string key = Unquote(line[..colonIndex].Trim());
                string value = Unquote(line[(colonIndex + 1)..].Trim());
                var tokens = TokenizeDictionaryValue(value);
                if (key.Length > 0 && tokens.Length > 0)
                {
                    result[key] = tokens;
                }
            }

            return result;
        }

        private string StripInlineComment(string line)
        {
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                }
                else if (c == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                }
                else if (c == '#' && !inSingleQuote && !inDoubleQuote)
                {
                    return line[..i];
                }
            }
            return line;
        }

        private string Unquote(string text)
        {
            if (text.Length >= 2)
            {
                char first = text[0];
                char last = text[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    return text[1..^1].Trim();
                }
            }
            return text;
        }

        private string[] TokenizeDictionaryValue(string value)
        {
            return value
                .Replace("[", " ", StringComparison.Ordinal)
                .Replace("]", " ", StringComparison.Ordinal)
                .Replace(",", " ", StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private List<PronouncedSyllable> BuildSyllablesFromDictionaryTokens(IReadOnlyList<string> tokens)
        {
            var result = new List<PronouncedSyllable>();
            int i = 0;
            while (i < tokens.Count)
            {
                string initial = string.Empty;
                string medial = string.Empty;
                string final = string.Empty;

                if (IsDictionaryInitial(tokens[i]))
                {
                    initial = ToInitialAlias(tokens[i]);
                    i++;
                }

                if (i < tokens.Count && IsDictionaryVowel(tokens[i]))
                {
                    medial = ToMedialAlias(tokens[i]);
                    i++;
                }
                else if (initial.Length > 0)
                {
                    result.Add(new PronouncedSyllable { Initial = initial });
                    continue;
                }
                else
                {
                    i++;
                    continue;
                }

                if (i < tokens.Count && IsDictionaryFinal(tokens[i]))
                {
                    final = tokens[i].ToUpperInvariant();
                    i++;
                }

                result.Add(new PronouncedSyllable
                {
                    Initial = initial,
                    Medial = medial,
                    Final = final,
                });
            }

            return result;
        }

        private bool IsDictionaryInitial(string token)
        {
            return token is "g" or "kk" or "n" or "d" or "tt" or "r" or "m" or "b" or "pp" or "s" or "ss"
                or "j" or "jj" or "ch" or "k" or "t" or "p" or "h" or "y" or "w";
        }

        private bool IsDictionaryVowel(string token)
        {
            return token is "a" or "eo" or "o" or "u" or "eu" or "i" or "e"
                or "ya" or "yeo" or "yo" or "yu" or "yae" or "ye" or "wa" or "we" or "wo" or "wi" or "ui";
        }

        private bool IsDictionaryFinal(string token)
        {
            return token is "K" or "N" or "T" or "L" or "M" or "P" or "NG";
        }

        private string ToInitialAlias(string token)
        {
            return "- " + token;
        }

        private string ToMedialAlias(string token)
        {
            return "- " + token;
        }

        private string GetPronouncedInitial(HangulSyllable current, HangulSyllable? prev)
        {
            if (prev == null || string.IsNullOrEmpty(prev.Final))
            {
                return current.Initial;
            }

            string prevFinal = prev.Final;
            char currentInitial = current.InitialJamo;

            if (currentInitial == 'ㅇ')
            {
                return TryGetLiaisonInitial(prevFinal, current.MedialJamo, out var liaisonInitial)
                    ? liaisonInitial
                    : current.Initial;
            }

            if (currentInitial == 'ㅎ')
            {
                return TryGetAspiratedInitialFromH(prevFinal, out var aspiratedInitial)
                    ? aspiratedInitial
                    : current.Initial;
            }

            if (IsHFinal(prevFinal))
            {
                if (TryGetAspiratedInitialByFinalH(prevFinal, currentInitial, out var hInitial))
                {
                    return hInitial;
                }
            }

            if (IsLiquidFinal(prevFinal) && currentInitial == 'ㄴ')
            {
                return "- r";
            }

            if (currentInitial == 'ㄹ' && IsRieulToNieunEnvironment(prevFinal))
            {
                return "- n";
            }

            if (IsTensingFinal(prevFinal) && TryGetTensedInitial(currentInitial, out var tensedInitial))
            {
                return tensedInitial;
            }

            return current.Initial;
        }

        private string GetPronouncedFinal(HangulSyllable current, HangulSyllable? next)
        {
            if (string.IsNullOrEmpty(current.Final))
            {
                return string.Empty;
            }

            string final = current.Final;
            if (next == null)
            {
                return GetFinalAlias(final);
            }

            char nextInitial = next.InitialJamo;
            if (nextInitial == 'ㅇ')
            {
                return GetFinalBeforeSilentInitial(final);
            }

            if (nextInitial == 'ㅎ' && TryGetFinalBeforeFollowingH(final, out var finalBeforeH))
            {
                return finalBeforeH;
            }

            if (IsHFinal(final))
            {
                if (nextInitial == 'ㄱ' || nextInitial == 'ㄷ' || nextInitial == 'ㅈ' || nextInitial == 'ㅅ')
                {
                    return GetFinalAliasWithoutH(final);
                }
                if (nextInitial == 'ㄴ')
                {
                    return final == "ㅀ" ? "L" : "N";
                }
            }

            if (nextInitial == 'ㄴ' || nextInitial == 'ㅁ')
            {
                if (IsVelarStopFinal(final)) return "NG";
                if (IsCoronalStopFinal(final)) return "N";
                if (IsBilabialStopFinal(final)) return "M";
            }

            if (nextInitial == 'ㄹ')
            {
                if (IsVelarStopFinal(final)) return "NG";
                if (IsBilabialStopFinal(final)) return "M";
                if (IsNFinal(final)) return "L";
            }

            return GetFinalAlias(final);
        }

        private bool TryGetLiaisonInitial(string final, char medial, out string initial)
        {
            initial = string.Empty;

            if (medial == 'ㅣ')
            {
                if (final == "ㄷ")
                {
                    initial = "- j";
                    return true;
                }
                if (final == "ㅌ" || final == "ㄾ")
                {
                    initial = "- ch";
                    return true;
                }
            }

            switch (final)
            {
                case "ㄱ": initial = "- g"; return true;
                case "ㄲ": initial = "- kk"; return true;
                case "ㄳ": initial = "- ss"; return true;
                case "ㄴ": initial = "- n"; return true;
                case "ㄵ": initial = "- j"; return true;
                case "ㄶ": initial = "- n"; return true;
                case "ㄷ": initial = "- d"; return true;
                case "ㄹ": initial = "- r"; return true;
                case "ㄺ": initial = "- g"; return true;
                case "ㄻ": initial = "- m"; return true;
                case "ㄼ": initial = "- b"; return true;
                case "ㄽ": initial = "- ss"; return true;
                case "ㄾ": initial = "- t"; return true;
                case "ㄿ": initial = "- p"; return true;
                case "ㅀ": initial = "- r"; return true;
                case "ㅁ": initial = "- m"; return true;
                case "ㅂ": initial = "- b"; return true;
                case "ㅄ": initial = "- ss"; return true;
                case "ㅅ": initial = "- s"; return true;
                case "ㅆ": initial = "- ss"; return true;
                case "ㅈ": initial = "- j"; return true;
                case "ㅊ": initial = "- ch"; return true;
                case "ㅋ": initial = "- k"; return true;
                case "ㅌ": initial = "- t"; return true;
                case "ㅍ": initial = "- p"; return true;
                case "ㅎ": return true;
                default: return false;
            }
        }

        private string GetFinalBeforeSilentInitial(string final)
        {
            return final switch
            {
                "ㄳ" => "K",
                "ㄵ" => "N",
                "ㄺ" => "L",
                "ㄻ" => "L",
                "ㄼ" => "L",
                "ㄽ" => "L",
                "ㄾ" => "L",
                "ㄿ" => "L",
                "ㅄ" => "P",
                "ㅇ" => "NG",
                _ => string.Empty,
            };
        }

        private bool TryGetAspiratedInitialFromH(string final, out string initial)
        {
            initial = final switch
            {
                "ㄱ" or "ㄲ" or "ㅋ" or "ㄳ" or "ㄺ" => "- k",
                "ㄷ" or "ㅅ" or "ㅆ" or "ㅈ" or "ㅊ" or "ㅌ" => "- t",
                "ㅂ" or "ㅍ" or "ㅄ" or "ㄼ" or "ㄿ" => "- p",
                "ㄵ" => "- ch",
                _ => string.Empty,
            };
            return initial.Length > 0;
        }

        private bool TryGetAspiratedInitialByFinalH(string final, char nextInitial, out string initial)
        {
            initial = string.Empty;
            if (nextInitial == 'ㄱ')
            {
                initial = "- k";
            }
            else if (nextInitial == 'ㄷ')
            {
                initial = "- t";
            }
            else if (nextInitial == 'ㅈ')
            {
                initial = "- ch";
            }
            else if (nextInitial == 'ㅅ')
            {
                initial = "- ss";
            }
            return initial.Length > 0;
        }

        private bool TryGetFinalBeforeFollowingH(string final, out string finalAlias)
        {
            finalAlias = final switch
            {
                "ㄺ" or "ㄼ" or "ㄿ" => "L",
                "ㄵ" => "N",
                _ when IsVelarStopFinal(final) || IsCoronalStopFinal(final) || IsBilabialStopFinal(final) => string.Empty,
                _ => GetFinalAlias(final),
            };
            return true;
        }

        private bool TryGetTensedInitial(char initial, out string tensedInitial)
        {
            tensedInitial = initial switch
            {
                'ㄱ' => "- kk",
                'ㄷ' => "- tt",
                'ㅂ' => "- pp",
                'ㅅ' => "- ss",
                'ㅈ' => "- jj",
                _ => string.Empty,
            };
            return tensedInitial.Length > 0;
        }

        private string GetFinalAlias(string final)
        {
            return finalConsonantsOnly.TryGetValue(final, out var mappedFinal)
                ? mappedFinal
                : final;
        }

        private string GetFinalAliasWithoutH(string final)
        {
            return final switch
            {
                "ㄶ" => "N",
                "ㅀ" => "L",
                "ㅎ" => string.Empty,
                _ => GetFinalAlias(final),
            };
        }

        private bool IsStopFinal(string final)
        {
            return IsVelarStopFinal(final) || IsCoronalStopFinal(final) || IsBilabialStopFinal(final);
        }

        private bool IsTensingFinal(string final)
        {
            return IsStopFinal(final) || final is "ㄵ" or "ㄻ";
        }

        private bool IsVelarStopFinal(string final)
        {
            return final is "ㄱ" or "ㄲ" or "ㅋ" or "ㄳ" or "ㄺ";
        }

        private bool IsCoronalStopFinal(string final)
        {
            return final is "ㄷ" or "ㅅ" or "ㅆ" or "ㅈ" or "ㅊ" or "ㅌ" or "ㅎ";
        }

        private bool IsBilabialStopFinal(string final)
        {
            return final is "ㅂ" or "ㅍ" or "ㅄ" or "ㄼ" or "ㄿ";
        }

        private bool IsNFinal(string final)
        {
            return final is "ㄴ" or "ㄵ" or "ㄶ";
        }

        private bool IsLiquidFinal(string final)
        {
            return final is "ㄹ" or "ㄼ" or "ㄽ" or "ㄾ" or "ㄿ" or "ㅀ";
        }

        private bool IsHFinal(string final)
        {
            return final is "ㅎ" or "ㄶ" or "ㅀ";
        }

        private bool IsRieulToNieunEnvironment(string final)
        {
            return IsVelarStopFinal(final) || IsBilabialStopFinal(final) || final is "ㅁ" or "ㄻ" or "ㅇ";
        }

        private HangulSyllable? GetLastHangul(params Note?[] notes)
        {
            foreach (var note in notes)
            {
                if (note == null)
                {
                    continue;
                }

                var decomposed = DecomposeHangul(GetNoteText(note.Value));
                if (decomposed.Count > 0)
                {
                    return decomposed[^1];
                }
            }
            return null;
        }

        private HangulSyllable? GetFirstHangul(params Note?[] notes)
        {
            foreach (var note in notes)
            {
                if (note == null)
                {
                    continue;
                }

                var decomposed = DecomposeHangul(GetNoteText(note.Value));
                if (decomposed.Count > 0)
                {
                    return decomposed[0];
                }
            }
            return null;
        }

        private string GetNoteText(Note note)
        {
            var text = string.IsNullOrWhiteSpace(note.phoneticHint)
                ? note.lyric
                : note.phoneticHint;
            text = text?.Trim() ?? string.Empty;
            return text.StartsWith("+", StringComparison.Ordinal) ? string.Empty : text;
        }

        private char GetInitialJamo(int index)
        {
            return index < InitialJamoOrder.Length ? InitialJamoOrder[index] : '\0';
        }

        private char GetMedialJamo(int index)
        {
            return index < MedialJamoOrder.Length ? MedialJamoOrder[index] : '\0';
        }

        private string GetInitialConsonant(int index)
        {
            if (index < InitialJamoOrder.Length)
            {
                char initial = InitialJamoOrder[index];
                if (initial == 'ㅇ') return "";
                return initialConsonants.ContainsKey(initial) ? initialConsonants[initial] : "";
            }
            return "";
        }

        private string GetMedialVowel(int index)
        {
            if (index < MedialJamoOrder.Length)
            {
                char vowel = MedialJamoOrder[index];
                if (diphthongs.ContainsKey(vowel)) return diphthongs[vowel];
                if (vowels.ContainsKey(vowel)) return vowels[vowel];
            }
            return "";
        }

        private string? GetFinalConsonant(int index)
        {
            return index < FinalJamoOrder.Length ? FinalJamoOrder[index] : null;
        }

        private sealed class DiphthongFallback
        {
            public DiphthongFallback(string? semivowel, string shortVowel, string mainVowel)
            {
                Semivowel = semivowel;
                ShortVowel = shortVowel;
                MainVowel = mainVowel;
            }

            public string? Semivowel { get; }
            public string ShortVowel { get; }
            public string MainVowel { get; }
        }

        private string ResolveMappedAlias(string phoneme, Note note, int phonemeIndex)
        {
            if (string.IsNullOrWhiteSpace(phoneme))
            {
                return phoneme;
            }
            if (singer == null)
            {
                return phoneme;
            }

            var attr = note.phonemeAttributes?.FirstOrDefault(a => a.index == phonemeIndex) ?? default;
            string alt = attr.alternate?.ToString() ?? string.Empty;
            string color = attr.voiceColor ?? string.Empty;
            int shiftedTone = note.tone + attr.toneShift;

            if (singer.TryGetMappedOto(phoneme + alt, shiftedTone, color, out var mappedWithAlt))
            {
                return mappedWithAlt.Alias;
            }
            if (singer.TryGetMappedOto(phoneme, shiftedTone, color, out var mapped))
            {
                return mapped.Alias;
            }
            if (attr.toneShift != 0)
            {
                if (singer.TryGetMappedOto(phoneme + alt, note.tone, color, out mappedWithAlt))
                {
                    return mappedWithAlt.Alias;
                }
                if (singer.TryGetMappedOto(phoneme, note.tone, color, out mapped))
                {
                    return mapped.Alias;
                }
            }
            if (singer.TryGetMappedOto(phoneme + alt, shiftedTone, out mappedWithAlt))
            {
                return mappedWithAlt.Alias;
            }
            if (singer.TryGetMappedOto(phoneme, shiftedTone, out mapped))
            {
                return mapped.Alias;
            }

            return phoneme;
        }
    }

    public class HangulSyllable
    {
        public char InitialJamo { get; set; }
        public char MedialJamo { get; set; }
        public string Initial { get; set; } = string.Empty;
        public string Medial { get; set; } = string.Empty;
        public string? Final { get; set; }
    }

    public class PronouncedSyllable
    {
        public string Initial { get; set; } = string.Empty;
        public string Medial { get; set; } = string.Empty;
        public string Final { get; set; } = string.Empty;
    }
}
