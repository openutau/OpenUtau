using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Core.Util.nnmnkwii.io.hts;
using Serilog;

namespace OpenUtau.Core.Hts {
    public abstract class HTSLabelPhonemizer : MachineLearningPhonemizer {
        protected USinger singer;
        //information used by HTS writer
        protected Dictionary<string, string[]> phoneDict = new Dictionary<string, string[]>();
        protected List<string> vowels = new List<string>();
        protected List<string> consonants = new List<string>();
        protected List<string> breaks = new List<string>();
        protected List<string> pauses = new List<string>();
        protected List<string> silences = new List<string>();
        protected List<string> unvoiced = new List<string>();
        protected string lang = "";
        int key = 0;
        int resolution = 480;

        //information used by openutau phonemizer
        protected IG2p g2p;
        //result caching
        private Dictionary<int, List<Tuple<string, int>>> partResult = new Dictionary<int, List<Tuple<string, int>>>();

        protected string tmpPath = string.Empty;
        protected string tablePath = string.Empty;
        protected string questionPath = string.Empty;
        protected string htstmpPath = string.Empty;
        protected string monoScorePath = string.Empty;
        protected string fullScorePath = string.Empty;
        protected string monoTimingPath = string.Empty;
        protected string fullTimingPath = string.Empty;

        public HTSLabelPhonemizer() {

        }

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            if (singer == null) {
                return;
            }
            phoneDict.Clear();
            //Load enuconfig
            string rootPath;
            if (File.Exists(Path.Join(singer.Location, "enunux", "enuconfig.yaml"))) {
                rootPath = Path.Combine(singer.Location, "enunux");
            }else if (File.Exists(Path.Join(singer.Location, "enuconfig.yaml"))) {
                rootPath = Path.Combine(singer.Location, "enunux");
            } else {
                rootPath = singer.Location;
            }
            //Load Dictionary
            var enunuDictPath = Path.Join(rootPath, tablePath);
            try {
                LoadDict(Path.Join(rootPath, tablePath), singer.TextFileEncoding);
            } catch (Exception e) {
                Log.Error(e, $"failed to load dictionary from {enunuDictPath}");
                return;
            }
            //Load g2p from enunux.yaml
            //g2p dict should be load after enunu dict
            try {
                g2p = LoadG2p(rootPath);
            } catch (Exception e) {
                Log.Error(e, "failed to load g2p dictionary");
                return;
            }
        }

        protected virtual IG2p LoadG2p(string rootPath) {
            var g2ps = new List<IG2p>();

            var enunuxPath = Path.Combine(rootPath, "enunux.yaml");
            var builder = G2pDictionary.NewBuilder();
            // Load dictionary from enunux.yaml and nnsvs dict
            if (File.Exists(enunuxPath)) {
                try {
                    var input = File.ReadAllText(enunuxPath, singer.TextFileEncoding);
                    var data = Yaml.DefaultDeserializer.Deserialize<G2pDictionaryData>(input);
                    if (data.symbols != null) {
                        foreach (var symbolData in data.symbols) {
                            builder.AddSymbol(symbolData.symbol, symbolData.type);
                        }
                    }
                    foreach (var grapheme in phoneDict.Keys) {
                        builder.AddEntry(grapheme, phoneDict[grapheme]);
                    }
                    if (data.entries != null) {
                        foreach (var entry in data.entries) {
                            builder.AddEntry(entry.grapheme, entry.phonemes);
                        }
                    }
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load Dictionary");
                }
            }
            foreach (var entry in phoneDict.Keys) {
                builder.AddEntry(entry, phoneDict[entry]);
            }
            g2ps.Add(builder.Build());
            return new G2pFallbacks(g2ps.ToArray());
        }

        public void LoadDict(string path, Encoding encoding) {
            if (path.EndsWith(".conf")) {
                LoadConf(path, encoding);
            } else {
                LoadTable(path, encoding);
            }
        }

        public void LoadTable(string path, Encoding encoding) {
            var lines = File.ReadLines(path, encoding);
            foreach (var line in lines) {
                var lineSplit = line.Split();
                phoneDict[lineSplit[0]] = lineSplit[1..];
            }
        }

        public void LoadConf(string path, Encoding encoding) {
            phoneDict["SILENCES"] = new string[] { "sil" };
            phoneDict["PAUSES"] = new string[] { "pau" };
            phoneDict["BREAK"] = new string[] { "br" };
            var lines = File.ReadLines(path, encoding);
            foreach (var line in lines) {
                if (line.Contains('=')) {
                    var lineSplit = line.Split("=");
                    var key = lineSplit[0];
                    var value = lineSplit[1];
                    var phonemes = value.Trim(new char[] { '\"' }).Split(",");
                    phoneDict[key] = phonemes;
                }
            }
        }

        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            key = project.key;
            resolution = project.resolution;
            //将全曲拆分为句子
            var phrase = new List<Note[]> { notes[0] };
            for (var i = 1; i < notes.Length; ++i) {
                //如果上下音符相互衔接，则不分句
                if (notes[i - 1][^1].position + notes[i - 1][^1].duration == notes[i][0].position) {
                    phrase.Add(notes[i]);
                } else {
                    //如果断开了，则处理当前句子，并开启下一句
                    ProcessPart(phrase.ToArray());
                    phrase.Clear();
                    phrase.Add(notes[i]);
                }
            }
            if (phrase.Count > 0) {
                ProcessPart(phrase.ToArray());
            }
        }

        protected (string prefix, string suffix) GetPrefixAndSuffix(Note note) {
            var prefix = string.Empty;
            var suffix = string.Empty;

            var textList = note.lyric.Split().ToList();
            var splitFlag = true;
            foreach (var text in textList) {
                var existSymbol = g2p.IsValidSymbol(text);
                if (existSymbol) {
                    splitFlag = false;
                    continue;
                } else if (!existSymbol && !splitFlag) {
                    splitFlag = true;
                    continue;
                }
                if (splitFlag) {
                    prefix += text;
                } else {
                    suffix += text;
                }
            }

            return (prefix, suffix);
        }

        protected abstract HTSNote CustomHTSNoteContext(HTSNote htsNote, Note note);

        //make a HTS Note from given symbols and UNotes
        //TODO:Fix the processing for rests
        protected HTSNote makeHtsNote(string[] symbols, IList<Note> group, int startTick) {
            var htsNote = HTSContextBuilder.BuildNote(
                symbols,
                group[0].tone,
                IsSyllableVowelExtensionNote(group[0]),
                lang,
                key,
                timeAxis,
                group[0].position,
                group[^1].position + group[^1].duration,
                startTick,
                0,
                symbol => pauses.Contains(symbol) || silences.Contains(symbol) || breaks.Contains(symbol));
            return CustomHTSNoteContext(htsNote, group[0]) ?? htsNote;
        }

        protected HTSNote makeHtsNote(string symbol, Note[] group, int startTick) {
            return makeHtsNote(new string[] { symbol }, group, startTick);
        }

        protected bool IsSyllableVowelExtensionNote(Note note) {
            return note.lyric.StartsWith("+~") || note.lyric.StartsWith("+*");
        }

        private string[] ApplyExtensions(string[] symbols, Note[] notes) {
            var newSymbols = new List<string>();
            var vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            var lastVowelI = 0;
            newSymbols.AddRange(symbols.Take(vowelIds[lastVowelI] + 1));
            for (var i = 1; i < notes.Length && lastVowelI + 1 < vowelIds.Count; i++) {
                if (!IsSyllableVowelExtensionNote(notes[i])) {
                    var prevVowel = vowelIds[lastVowelI];
                    lastVowelI++;
                    var vowel = vowelIds[lastVowelI];
                    newSymbols.AddRange(symbols.Skip(prevVowel + 1).Take(vowel - prevVowel));
                } else {
                    newSymbols.Add(symbols[vowelIds[lastVowelI]]);
                }
            }
            newSymbols.AddRange(symbols.Skip(vowelIds[lastVowelI] + 1));
            return newSymbols.ToArray();
        }

        private List<int> ExtractVowels(string[] symbols) {
            var vowelIds = new List<int>();
            for (var i = 0; i < symbols.Length; i++) {
                if (g2p.IsVowel(symbols[i])) {
                    vowelIds.Add(i);
                }
            }
            return vowelIds;
        }

        protected virtual Note[] HandleNotEnoughNotes(Note[] notes, List<int> vowelIds) {
            var newNotes = new List<Note>();
            newNotes.AddRange(notes.SkipLast(1));
            var lastNote = notes.Last();
            var position = lastNote.position;
            var notesToSplit = vowelIds.Count - newNotes.Count;
            var duration = lastNote.duration / notesToSplit / 15 * 15;
            for (var i = 0; i < notesToSplit; i++) {
                var durationFinal = i != notesToSplit - 1 ? duration : lastNote.duration - duration * (notesToSplit - 1);
                newNotes.Add(new Note() {
                    position = position,
                    duration = durationFinal,
                    tone = lastNote.tone,
                    phonemeAttributes = lastNote.phonemeAttributes
                });
                position += durationFinal;
            }

            return newNotes.ToArray();
        }

        protected virtual Note[] HandleExcessNotes(Note[] notes, List<int> vowelIds) {
            var newNotes = new List<Note>();
            var SyllableCount = vowelIds.Count;
            newNotes.AddRange(notes.Take(SyllableCount - 1));
            var lastNote = notes[SyllableCount - 1];
            newNotes.Add(new Note() {
                lyric = lastNote.lyric,
                phoneticHint = lastNote.phoneticHint,
                position = lastNote.position,
                duration = notes[(SyllableCount - 1)..].Select(note => note.duration).Sum(),
                tone = lastNote.tone,
                phonemeAttributes = lastNote.phonemeAttributes
            });
            return newNotes.ToArray();
        }

        public string GetPhonemeType(string phoneme) {
            if (phoneme == "xx") {
                return "xx";
            }
            if (vowels.Contains(phoneme)) {
                return "v";
            }
            if (pauses.Contains(phoneme)) {
                return "p";
            }
            if (silences.Contains(phoneme)) {
                return "s";
            }
            if (breaks.Contains(phoneme)) {
                return "b";
            }
            //if (unvoiced.Contains(phoneme)) {
            //    return "c";
            //}
            return "c";
        }

        string[] GetSymbols(Note note) {
            //priority:
            //1. phonetic hint
            //2. query from g2p dictionary
            //3. treat lyric as phonetic hint, including single phoneme
            //4. default pause
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                // Split space-separated symbols into an array.
                return note.phoneticHint.Split()
                    .Where(s => g2p.IsValidSymbol(s)) // skip the invalid symbols.
                    .ToArray();
            }
            // User has not provided hint, query g2p dictionary.
            var g2presult = g2p.Query(note.lyric);
            if (g2presult != null) {
                return g2presult;
            }
            //not founded in g2p dictionary, treat lyric as phonetic hint
            var lyricSplited = note.lyric.Split()
                    .Where(s => g2p.IsValidSymbol(s)) // skip the invalid symbols.
                    .ToArray();
            if (lyricSplited.Length > 0) {
                return lyricSplited;
            }
            return new string[] { "pau" };
        }

        private (string[], int[], Note[]) GetSymbolsAndVowels(Note[] notes) {
            var mainNote = notes[0];
            var symbols = GetSymbols(mainNote);
            if (symbols == null) {
                return (null, null, null);
            }
            if (symbols.Length == 0) {
                symbols = new string[] { "" };
            }
            symbols = ApplyExtensions(symbols, notes);
            var vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            if (notes.Length < vowelIds.Count) {
                notes = HandleNotEnoughNotes(notes, vowelIds);
            } else if (notes.Length > vowelIds.Count) {
                notes = HandleExcessNotes(notes, vowelIds);
            }
            return (symbols, vowelIds.ToArray(), notes);
        }

        protected struct Syllable {
            public List<string> symbols;
            public List<Note> notes;
        }

        protected virtual HTSNote[] MakeSyllables(Note[] inputNotes, int startTick) {
            (var symbols, var vowelIds, var notes) = GetSymbolsAndVowels(inputNotes);
            if (symbols == null || vowelIds == null || notes == null) {
                return null;
            }
            var firstVowelId = vowelIds[0];
            if (notes.Length < vowelIds.Length) {
                //error = $"Not enough extension notes, {vowelIds.Length - notes.Length} more expected";
                return null;
            }

            var syllables = new Syllable[vowelIds.Length];

            // Making the first syllable

            // there is only empty space before us
            syllables[0] = new Syllable() {
                symbols = symbols.Take(firstVowelId + 1).ToList(),
                notes = notes[0..1].ToList()
            };

            // normal syllables after the first one
            var noteI = 1;
            var ccs = new List<string>();
            var position = 0;
            var lastSymbolI = firstVowelId + 1;
            for (; lastSymbolI < symbols.Length; lastSymbolI++) {
                if (!vowelIds.Contains(lastSymbolI)) {
                    ccs.Add(symbols[lastSymbolI]);
                } else {
                    position += notes[noteI - 1].duration;
                    syllables[noteI] = new Syllable() {
                        symbols = ccs.Append(symbols[lastSymbolI]).ToList(),
                        notes = new List<Note>() { notes[noteI] }
                    };
                    ccs = new List<string>();
                    noteI++;
                }
            }
            syllables[^1].symbols.AddRange(ccs);
            return syllables.Select(x => makeHtsNote(x.symbols.ToArray(), x.notes, startTick)).ToArray();
        }

        HTSPhoneme[] HTSNoteToPhonemes(HTSNote htsNote) {
            var htsPhonemes = htsNote.symbols.Select(x => new HTSPhoneme(x, htsNote)).ToArray();
            // 音節内の音素に対して、タイプ（母音/子音/休符など）や位置情報を付与
            foreach (var i in Enumerable.Range(0, htsPhonemes.Length)) {
                htsPhonemes[i].type = GetPhonemeType(htsPhonemes[i].symbol);
                htsPhonemes[i].position = i + 1;
                htsPhonemes[i].position_backward = htsPhonemes.Length - i;
            }
            foreach (var i in Enumerable.Range(0, htsPhonemes.Length)) {
                if (htsPhonemes[i].type.Equals("c")) {
                    var prev = i - 1;
                    if (prev >= 0) {
                        if (htsPhonemes[prev].type.Equals("v")) {
                            htsPhonemes[i].prev_vowel_distance = 1;
                        } else if (htsPhonemes[prev].prev_vowel_distance > 0) {
                            htsPhonemes[i].prev_vowel_distance = htsPhonemes[prev].prev_vowel_distance + 1;
                        } else {
                            htsPhonemes[i].prev_vowel_distance = 0;
                        }
                    }
                }
            }
            for (var i = htsPhonemes.Length - 1; i >= 0; --i) {
                if (htsPhonemes[i].type.Equals("c")) {
                    var next = i + 1;
                    if (next < htsPhonemes.Length) {
                        if (htsPhonemes[next].type.Equals("v")) {
                            htsPhonemes[i].next_vowel_distance = 1;
                        } else if (htsPhonemes[next].next_vowel_distance > 0) {
                            htsPhonemes[i].next_vowel_distance = htsPhonemes[next].next_vowel_distance + 1;
                        } else {
                            htsPhonemes[i].next_vowel_distance = 0;
                        }
                    }
                }
            }
            return htsPhonemes;
        }

        protected abstract void SendScore(Note[][] phrase);

        ulong HashPhraseGroups(Note[][] phrase) {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write(phrase.ToString());
                    foreach (var phone in phrase) {
                        writer.Write(phone[0].lyric);
                        if (phone[0].phoneticHint != null) {
                            writer.Write("[" + phone[0].phoneticHint + "]");
                        }
                        var attr = phone[0].phonemeAttributes?.FirstOrDefault(attr => attr.index == 0) ?? default;
                        writer.Write(attr.toneShift ?? GetParentToneShift());
                        writer.Write(phone[0].position);
                        writer.Write(phone[0].duration);
                    }
                    return XXH64.DigestOf(stream.ToArray());
                }
            }
        }

        protected abstract Note[][] PhraseAdjustments(Note[][] phrese);

        protected abstract HTSPhoneme[] CustomHTSPhonemeContext(HTSPhoneme[] htsPhonemes, Note[] notes);


        protected override void ProcessPart(Note[][] phrase) {
            tmpPath = Path.Join(PathManager.Inst.CachePath, $"lab-{HashPhraseGroups(phrase):x16}");
            htstmpPath = tmpPath + "_htstemp";
            fullScorePath = Path.Join(htstmpPath, $"full_score.lab");
            fullTimingPath = Path.Join(htstmpPath, $"full_timing.lab");
            monoScorePath = Path.Join(htstmpPath, $"mono_score.lab");
            monoTimingPath = Path.Join(htstmpPath, $"mono_timing.lab");

            phrase = PhraseAdjustments(phrase) ?? phrase;

            var startTick = phrase[0][0].position;
            var endTick = phrase[^1][^1].position + phrase[^1][^1].duration;

            // パディングを小節長で設定（開始・終了ともに1小節）
            var sigStart = timeAxis.TimeSignatureAtTick(startTick);
            var bpmStart = timeAxis.GetBpmAtTick(startTick);
            var barLenMsStart = (int)Math.Round(60000.0 / bpmStart * sigStart.beatPerBar);
            var barLenTicksStart = timeAxis.MsPosToTickPos(barLenMsStart);

            var sigEnd = timeAxis.TimeSignatureAtTick(endTick);
            var bpmEnd = timeAxis.GetBpmAtTick(endTick);
            var barLenMsEnd = (int)Math.Round(60000.0 / bpmEnd * sigEnd.beatPerBar);
            var barLenTicksEnd = timeAxis.MsPosToTickPos(barLenMsEnd);

            // 文全体の長さ（開始1小節 + 本体 + 終了1小節）
            var sentenceDurMs = barLenMsStart + (int)timeAxis.MsBetweenTickPos(startTick, endTick) + barLenMsEnd;
            var sentenceDurTicks = barLenTicksStart + (endTick - startTick) + barLenTicksEnd;

            var notePhIndex = new List<int> { 1 }; // 先頭パディング分
            var phAlignPoints = new List<Tuple<int, double>>();

            // 先頭パディング pau
            timeAxis.TickPosToBarBeat(startTick - barLenTicksStart, out var barStart, out var beatStart, out var _);
            var sigForPadStart = timeAxis.TimeSignatureAtTick(startTick - barLenTicksStart);
            var PaddingNoteStart = new HTSNote(
                symbols: new string[] { "pau" },
                beatPerBar: sigForPadStart.beatPerBar,
                beatUnit: sigForPadStart.beatUnit,
                positionBar: barStart,
                positionBeat: beatStart,
                key: key,
                bpm: timeAxis.GetBpmAtTick(startTick - barLenTicksStart),
                tone: 0,
                isSlur: false,
                isRest: true,
                lang: string.Empty,
                accent: string.Empty,
                startms: 0,
                endms: barLenMsStart,
                positionTicks: startTick - barLenTicksStart,
                durationTicks: barLenTicksStart
            );
            var htsNotes = new List<HTSNote> { PaddingNoteStart };
            var htsPhonemes = new List<HTSPhoneme>();
            htsPhonemes.AddRange(CustomHTSPhonemeContext(HTSNoteToPhonemes(PaddingNoteStart), phrase[0]));

            // 楽譜ノート → HTSノート
            for (var n = 0; n < phrase.Length; ++n) {
                var Syllables = MakeSyllables(phrase[n], startTick);
                // 各ノートの start/end を「開始パディング加算」ベースに
                foreach (var note in Syllables) {
                    note.startMs += barLenMsStart;
                    note.endMs += barLenMsStart;
                }
                htsNotes.AddRange(Syllables);

                for (var noteIndex = 0; noteIndex < Syllables.Length; noteIndex++) {
                    var htsNote = Syllables[noteIndex];
                    var tmpPhonemes = HTSNoteToPhonemes(htsNote);
                    var notePhonemes = CustomHTSPhonemeContext(tmpPhonemes, phrase[n]) ?? tmpPhonemes;

                    // 第1母音位置をアンカーに（絶対ms）
                    var firstVowelIndex = 0;
                    for (var phIndex = 0; phIndex < htsNote.symbols.Length; phIndex++) {
                        if (g2p.IsVowel(htsNote.symbols[phIndex])) {
                            firstVowelIndex = phIndex;
                            break;
                        }
                    }
                    phAlignPoints.Add(Tuple.Create(
                        htsPhonemes.Count + firstVowelIndex,
                        timeAxis.TickPosToMsPos(htsNote.positionTicks) + barLenMsStart
                    ));
                    htsPhonemes.AddRange(notePhonemes);
                }
                notePhIndex.Add(htsPhonemes.Count);
            }

            // 終端パディング pau（位置は「本当の曲末」tick）
            timeAxis.TickPosToBarBeat(endTick, out var barEnd, out var beatEnd, out var _);
            var PaddingNoteEnd = new HTSNote(
                symbols: new string[] { "pau" },
                beatPerBar: sigEnd.beatPerBar,
                beatUnit: sigEnd.beatUnit,
                positionBar: barEnd,
                positionBeat: beatEnd,
                key: key,
                bpm: bpmEnd,
                tone: 0,
                isSlur: false,
                isRest: true,
                lang: string.Empty,
                accent: string.Empty,
                // 絶対msで末尾に配置
                startms: sentenceDurMs - barLenMsEnd,
                endms: sentenceDurMs,
                positionTicks: endTick,
                durationTicks: barLenTicksEnd
            );
            htsNotes.Add(PaddingNoteEnd);
            htsPhonemes.AddRange(CustomHTSPhonemeContext(HTSNoteToPhonemes(PaddingNoteEnd), phrase[^1]));

            // 末尾アンカーは「曲末＋終端パディング」位置
            var lastNote = htsNotes[^1];
            phAlignPoints.Add(Tuple.Create(
                htsPhonemes.Count,
                timeAxis.TickPosToMsPos(lastNote.positionTicks + lastNote.durationTicks) + barLenMsStart // = sentenceDurMs
            ));
            var htsPhrase = new HTSPhrase(htsNotes.ToArray());
            htsPhrase.UpdateResolution(resolution);
            htsPhrase.totalNotes = htsNotes.Count - 2;
            htsPhrase.totalPhonemes = htsPhonemes.Count - 3;
            htsPhrase.totalPhrases = 1;
            //make neighborhood links between htsNotes and between htsPhonemes
            foreach (var i in Enumerable.Range(0, htsNotes.Count)) {
                htsNotes[i].parent = htsPhrase;
                htsNotes[i].index = i;
                htsNotes[i].indexBackwards = htsNotes.Count - i - 1;
                htsNotes[i].sentenceDurMs = sentenceDurMs;
                htsNotes[i].sentenceDurTicks = sentenceDurTicks;
                if (i > 0) {
                    htsNotes[i].prev = htsNotes[i - 1];
                    htsNotes[i - 1].next = htsNotes[i];
                }
            }
            for (var i = 1; i < htsPhonemes.Count; ++i) {
                htsPhonemes[i].prev = htsPhonemes[i - 1];
                htsPhonemes[i - 1].next = htsPhonemes[i];
            }

            try {
                if (!Directory.Exists(htstmpPath)) {
                    Directory.CreateDirectory(htstmpPath);
                }
                File.WriteAllLines(fullScorePath, htsPhonemes.Select(x => x.dump()));
            } catch (Exception e) {
                Log.Error(e.ToString());
                throw;
            }

            SendScore(phrase);
            if (!File.Exists(monoTimingPath)) {
                Log.Error($"File not found.:{monoTimingPath}");
                return;
            }

            var hTSLabels = hts.load(monoTimingPath, Encoding.UTF8);

            // 100ns -> ms は 10000 で割る
            var labPositions =
                hTSLabels.Skip(1).SkipLast(1).Select(label => (label.end_time - label.start_time) / 10000.0).ToList();
            labPositions.Insert(0, labPositions[0]);
            labPositions.Add(labPositions[^1]);

            var positions = HTSContextBuilder.AlignTimingPositions(labPositions, phAlignPoints);

            // 出力（略）
            var phonemesRedirected = htsPhonemes.Select(x => x.symbol).ToArray();
            for (var groupIndex = 0; groupIndex < phrase.Length; groupIndex++) {
                var group = phrase[groupIndex];
                if (group[0].lyric.StartsWith("+")) {
                    continue;
                }
                var notePos = timeAxis.TickPosToMsPos(group[0].position) + barLenMsStart; // ms
                var noteResult = HTSContextBuilder.BuildAlignedNoteTimingResult(
                    phonemesRedirected,
                    notePhIndex[groupIndex],
                    notePhIndex[groupIndex + 1],
                    positions,
                    notePos,
                    timeAxis.TicksBetweenMsPos);
                partResult[group[0].position] = noteResult;
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {
            if (partResult.TryGetValue(notes[0].position, out var phonemes)) {
                return new Result {
                    phonemes = phonemes
                        .Select((tu) => new Phoneme() {
                            phoneme = tu.Item1,
                            position = tu.Item2,
                        })
                        .ToArray(),
                };
            }
            if (SetUpException != null) {
                throw new Exception("Phonemizer failed to process.", SetUpException);
            }
            throw new Exception("Part result not found");
        }
    }
}
