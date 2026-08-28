using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Hts {
    public abstract class HTSLabelRenderer : IRenderer {

        static readonly object lockObj = new object();

        public virtual bool SupportsRenderPitch => true;

        public abstract USingerType SingerType { get; }

        public abstract bool SupportsExpression(UExpressionDescriptor descriptor);

        protected TimeAxis timeAxis;

        //information used by HTS writer
        protected Dictionary<string, string[]> phoneDict = new Dictionary<string, string[]>();
        protected List<string> vowels = new List<string>();
        protected List<string> consonants = new List<string>();
        protected List<string> breaks = new List<string>();
        protected List<string> pauses = new List<string>();
        protected List<string> silences = new List<string>();
        protected List<string> unvoiced = new List<string>();
        protected List<string> macronLyrics = new List<string>();
        protected int startTick;
        protected int endTick;
        protected UTimeSignature sigStart;
        protected double bpmStart;
        protected double headMs;
        protected int barLenTicksStart;
        protected UTimeSignature sigEnd;
        protected double bpmEnd;
        protected double tailMs;
        protected int barLenTicksEnd;
        protected string lang = "";
        protected int key = 0;
        protected int resolution = 480;
        protected int framePeriod = 5;

        //information used by openutau phonemizer
        protected IG2p g2p;
        //result caching
        private Dictionary<int, List<Tuple<string, int>>> partResult = new Dictionary<int, List<Tuple<string, int>>>();
        protected string tablePath = string.Empty;
        protected string monoScorePath = string.Empty;
        protected string fullScorePath = string.Empty;
        protected string monoTimingPath = string.Empty;
        protected string fullTimingPath = string.Empty;

        public abstract void SetUp();

        protected virtual void LoadDict(string path, Encoding encoding) {
            if (path.EndsWith(".conf")) {
                LoadConf(path, encoding);
            } else {
                LoadTable(path, encoding);
            }
        }

        private void LoadTable(string path, Encoding encoding) {
            var lines = File.ReadLines(path, encoding);
            foreach (var line in lines) {
                var lineSplit = line.Split();
                phoneDict[lineSplit[0]] = lineSplit[1..];
            }
        }

        private void LoadConf(string path, Encoding encoding) {
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
        protected IG2p LoadG2p() {
            var g2ps = new List<IG2p>();
            var builder = G2pDictionary.NewBuilder();
            vowels.AddRange(phoneDict["VOWELS"]);
            breaks.AddRange(phoneDict["BREAK"]);
            pauses.AddRange(phoneDict["PAUSES"]);
            silences.AddRange(phoneDict["SILENCES"]);
            consonants.AddRange(phoneDict["PHONEME_CL"]);
            macronLyrics.AddRange(phoneDict["MACRON"]);
            foreach (var dict in phoneDict.Values) {
                foreach (var phoneme in dict) {
                    if (!consonants.Contains(phoneme) && !vowels.Contains(phoneme) &&
                        !breaks.Contains(phoneme) && !pauses.Contains(phoneme) &&
                        !silences.Contains(phoneme)) {
                        consonants.Add(phoneme);
                    }
                    if (!consonants.Contains(phoneme)) {
                        builder.AddSymbol(phoneme, true);
                    } else {
                        builder.AddSymbol(phoneme, false);
                    }
                }
            }
            foreach (var entry in phoneDict.Keys) {
                builder.AddEntry(entry, phoneDict[entry]);
                foreach (var reduction in phoneDict["VOWEL_REDUCTION"]) {
                    var phonemes = phoneDict[entry].Except(vowels).ToList();
                    if (phonemes.Count == 0) continue;
                    builder.AddEntry(entry + reduction, phonemes);
                }
                foreach (var macron in phoneDict["MACRON"]) {
                    var addPhonemes = phoneDict[entry].Where(x => vowels.Contains(x)).ToList();
                    if (addPhonemes.Count == 0) continue;
                    var phonemes = phoneDict[entry].ToList();
                    phonemes.AddRange(addPhonemes);
                    builder.AddEntry(entry + macron, phonemes);
                    macronLyrics.Add(entry + macron);
                }
            }
            g2ps.Add(builder.Build());
            return new G2pFallbacks(g2ps.ToArray());
        }



        protected (string prefix, string suffix) GetPrefixAndSuffix(RenderNote note) {
            string prefix = string.Empty;
            string suffix = string.Empty;

            var textList = note.lyric.Split().ToList();
            bool splitFlag = true;
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

        private RenderPhone FindLastVowelOrLastPhoneme(RenderPhone[] phonemes) {
            for (int i = phonemes.Length - 1; i >= 0; --i) {
                if (g2p.IsVowel(phonemes[i].phoneme)) {
                    return phonemes[i];
                }
            }
            return phonemes[^1];
        }

        protected virtual HTSNote CustomHTSNoteContext(HTSNote htsNote, RenderNote note) {
            var fixs = GetPrefixAndSuffix(note);
            if (!htsNote.isRest && !htsNote.isSlur) {
                htsNote.langDependent = "0"; // no macron
                if (macronLyrics.Contains(note.lyric)) {
                    htsNote.langDependent = "1"; // macron
                }
            }
            return htsNote;
        }

        //make a HTS Note from given symbols and UNotes
        private HTSNote makeHtsNote(string[] symbols, RenderNote note, int startTick, double leadingMs) {
            var positiontick = startTick + note.position;
            var endTick = positiontick + note.duration;
            UTimeSignature sig = timeAxis.TimeSignatureAtTick(positiontick);
            timeAxis.TickPosToBarBeat(positiontick, out int bar, out int beat, out int remainingTicks);
            var isRest = symbols.Select(x => x.ToLowerInvariant()).Any(x => pauses.Contains(x) || silences.Contains(x) || breaks.Contains(x));
            var htsNote = new HTSNote(
                            symbols: symbols,
                            tone: note.tone,
                            isSlur: IsSyllableVowelExtensionNote(note),
                            isRest: isRest,
                            lang: isRest ? string.Empty : lang,
                            accent: string.Empty,
                            beatPerBar: sig.beatPerBar,
                            beatUnit: sig.beatUnit,
                            positionBar: bar,
                            positionBeat: beat,
                            key: key,
                            bpm: timeAxis.GetBpmAtTick(positiontick),
                            startms: timeAxis.MsBetweenTickPos(startTick, positiontick) + leadingMs,
                            endms: timeAxis.MsBetweenTickPos(startTick, endTick) + leadingMs,
                            positionTicks: positiontick,
                            durationTicks: note.duration
                            );
            return CustomHTSNoteContext(htsNote, note) ?? htsNote;
        }
        private HTSNote makeHtsNote(string symbol, RenderNote note, int startTick, double leadingMs) {
            return makeHtsNote(new string[] { symbol }, note, startTick, leadingMs);
        }

        protected virtual bool IsSyllableVowelExtensionNote(RenderNote note) {
            return note.lyric.StartsWith("+") || note.lyric.StartsWith("-") || note.lyric.StartsWith("*") || note.lyric.StartsWith("~");
        }

        private string GetPhonemeType(string phoneme) {
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

        private HTSPhoneme[] HTSNoteToPhonemes(HTSNote htsNote) {
            var htsPhonemes = htsNote.symbols.Select(x => new HTSPhoneme(x, htsNote)).ToArray();
            foreach (int i in Enumerable.Range(0, htsPhonemes.Length)) {
                htsPhonemes[i].type = GetPhonemeType(htsPhonemes[i].symbol);
                htsPhonemes[i].position = i + 1;
                htsPhonemes[i].position_backward = htsPhonemes.Length - i;
                if (htsPhonemes[i].type.Equals("c")) {
                    int prev = i - 1;
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
            for (int i = htsPhonemes.Length - 1; i >= 0; --i) {
                if (htsPhonemes[i].type.Equals("c")) {
                    int next = i + 1;
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

        protected abstract HTSPhoneme[] CustomHTSPhonemeContext(HTSPhoneme[] htsPhonemes, RenderNote notes);

        public struct monoLabel {
            public string symbol;
            public double startMs;
            public double endMs;
            public override string ToString() {
                return $"{(long)Math.Round(startMs * 10000.0)} {(long)Math.Round(endMs * 10000.0)} {symbol}";
            }
        }

        protected virtual List<monoLabel> CustomMonoLabel(List<monoLabel> monoLabels) {
            return monoLabels;
        }

        public void ProcessPart(RenderPhrase phrase) {
            if (timeAxis == null) {
                timeAxis = phrase.timeAxis;
            }

            int startTick = phrase.position;
            int endTick = phrase.position + phrase.duration;

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
            double sentenceDurMs = headMs + phrase.endMs - phrase.positionMs + tailMs;
            int sentenceDurTicks = barLenTicksStart + (endTick - startTick) + barLenTicksEnd;

            // 先頭パディング pau
            timeAxis.TickPosToBarBeat(startTick - barLenTicksStart, out int barStart, out int beatStart, out int _);
            var sigForPadStart = timeAxis.TimeSignatureAtTick(startTick - barLenTicksStart);


            List<monoLabel> monoLabels_ = new List<monoLabel>();
            //double phonemeDuration = 0;

            HTSNote PaddingNoteStart = new HTSNote(
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
                endms: headMs,
                positionTicks: startTick - barLenTicksStart,
                durationTicks: barLenTicksStart
            );
            var htsNotes = new List<HTSNote> { PaddingNoteStart };
            var htsPhonemes = new List<HTSPhoneme>();
            htsPhonemes.AddRange(HTSNoteToPhonemes(PaddingNoteStart));

            monoLabels_.Add(new monoLabel() {
                symbol = htsPhonemes[0].symbol,
                startMs = 0,
                endMs = headMs
            });
            //phonemeDuration += headMs;

            //Alignment
            var phonemesByNoteIndex = phrase.phones
                .GroupBy(phone => phone.noteIndex)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(phone => phone).ToArray());
            var lastBasePhonemes = Array.Empty<RenderPhone>();
            var tuples = new List<Tuple<HTSNote, int>>();
            for (int noteIndex = 0; noteIndex < phrase.notes.Length; noteIndex++) {
                var note = phrase.notes[noteIndex];
                if (phonemesByNoteIndex.TryGetValue(noteIndex, out var phonemes)) {
                    foreach (var phone in phonemes) {
                        var phoneStartMs = headMs + (phone.positionMs - phrase.positionMs);
                        var phoneEndMs = headMs + (phone.endMs - phrase.positionMs);
                        var lastMonoLabel = monoLabels_[^1];
                        if (phoneStartMs < lastMonoLabel.endMs) {
                            lastMonoLabel.endMs = phoneStartMs;
                            monoLabels_[^1] = lastMonoLabel;
                        }
                        monoLabels_.Add(new monoLabel() {
                            symbol = phone.phoneme,
                            startMs = phoneStartMs,
                            endMs = phoneEndMs
                        });
                    }

                    lastBasePhonemes = phonemes;
                    HTSNote htsNote = makeHtsNote(phonemes.Select(phone => phone.phoneme).ToArray(), note, startTick, headMs);
                    tuples.Add(Tuple.Create(htsNote, noteIndex));
                } else if (IsSyllableVowelExtensionNote(note)) {
                    // 拍点延長ノートは、直前の通常ノートの最後の母音を引き延ばす
                    var extensionPhoneme = FindLastVowelOrLastPhoneme(lastBasePhonemes);
                    if (!string.IsNullOrEmpty(extensionPhoneme.phoneme)) {
                        var htsNote = makeHtsNote(extensionPhoneme.phoneme, note, startTick, headMs);
                        var extensionStartMs = htsNote.startMs;
                        var extensionEndMs = htsNote.endMs;

                        var lastMonoLabel = monoLabels_[^1];
                        if (lastMonoLabel.symbol == extensionPhoneme.phoneme &&
                            lastMonoLabel.startMs < extensionStartMs &&
                            extensionStartMs < lastMonoLabel.endMs) {
                            lastMonoLabel.endMs = extensionStartMs;
                            monoLabels_[^1] = lastMonoLabel;
                        }

                        monoLabels_.Add(new monoLabel() {
                            symbol = extensionPhoneme.phoneme,
                            startMs = extensionStartMs,
                            endMs = extensionEndMs
                        });

                        tuples.Add(Tuple.Create(htsNote, noteIndex));
                    }
                } else {
                    continue;
                }
            }
            int noteCount = tuples.Count;
            for (int i = 0; i < tuples.Count; i++) {
                var htsNote = tuples[i].Item1;
                htsNotes.Add(htsNote);
                htsNote.index = i + 1;
                htsNote.indexBackwards = noteCount - i;
                htsNote.sentenceDurMs = sentenceDurMs;
                htsNote.sentenceDurTicks = sentenceDurTicks;
                var tmpPhonemes = HTSNoteToPhonemes(htsNote);
                var notePhonemes = CustomHTSPhonemeContext(tmpPhonemes, phrase.notes[tuples[i].Item2]) ?? tmpPhonemes;
                htsPhonemes.AddRange(notePhonemes);
            }
            // 終端パディング pau（位置は「本当の曲末」tick）
            timeAxis.TickPosToBarBeat(endTick, out int barEnd, out int beatEnd, out int _);
            HTSNote PaddingNoteEnd = new HTSNote(
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
                startms: sentenceDurMs - tailMs,
                endms: sentenceDurMs,
                positionTicks: endTick,
                durationTicks: barLenTicksEnd
            );
            htsNotes.Add(PaddingNoteEnd);
            htsPhonemes.AddRange(HTSNoteToPhonemes(PaddingNoteEnd));

            monoLabels_.Add(new monoLabel() {
                symbol = htsPhonemes[^1].symbol,
                startMs = sentenceDurMs - tailMs,
                endMs = sentenceDurMs
            });

            var htsPhrase = new HTSPhrase(htsNotes.ToArray());
            htsPhrase.UpdateResolution(resolution);
            htsPhrase.totalNotes = htsNotes.Count - 1;
            htsPhrase.totalPhonemes = htsPhonemes.Count - 1;
            htsPhrase.totalPhrases = 1;
            //make neighborhood links between htsNotes and between htsPhonemes
            foreach (int i in Enumerable.Range(0, htsNotes.Count)) {
                htsNotes[i].parent = htsPhrase;
                if (i > 0) {
                    htsNotes[i].prev = htsNotes[i - 1];
                    htsNotes[i - 1].next = htsNotes[i];
                }
            }
            for (int i = 1; i < htsPhonemes.Count; ++i) {
                htsPhonemes[i].prev = htsPhonemes[i - 1];
                htsPhonemes[i - 1].next = htsPhonemes[i];
            }

            monoLabels_ = CustomMonoLabel(monoLabels_);

            try {
                File.WriteAllLines(fullScorePath, htsPhonemes.Select(x => x.dump()));
                File.WriteAllLines(monoTimingPath, monoLabels_.Select(x => x.ToString()));
            } catch (Exception e) {
                Log.Error(e.ToString());
                throw;
            }
        }

        public virtual RenderResult Layout(RenderPhrase phrase) {
            if (timeAxis == null) {
                timeAxis = phrase.timeAxis;
            }
            startTick = phrase.position;
            endTick = phrase.position + phrase.duration;

            // パディングを小節長で設定（開始・終了ともに1小節）
            sigStart = timeAxis.TimeSignatureAtTick(startTick);
            bpmStart = timeAxis.GetBpmAtTick(startTick);
            headMs = (int)Math.Round((60000.0 / bpmStart) * sigStart.beatPerBar);

            sigEnd = timeAxis.TimeSignatureAtTick(endTick);
            bpmEnd = timeAxis.GetBpmAtTick(endTick);
            tailMs = (int)Math.Round((60000.0 / bpmEnd) * sigEnd.beatPerBar);
            return new RenderResult() {
                leadingMs = headMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = headMs + phrase.durationMs + tailMs,
            };
        }

        public abstract Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender);

        public abstract UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings);

        public abstract override string ToString();

        public abstract RenderPitchResult LoadRenderedPitch(RenderPhrase phrase);
    }
}
