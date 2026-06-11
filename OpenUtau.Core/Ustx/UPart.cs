using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NWaves.Operations;
using NWaves.Signals;
using OpenUtau.Api;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using Serilog;
using SharpCompress;
using YamlDotNet.Serialization;

namespace OpenUtau.Core.Ustx {
    public abstract class UPart {
        public string name = "New Part";
        public string comment = string.Empty;
        public int trackNo;
        public int position = 0;

        [YamlIgnore] public virtual string DisplayName { get; }
        [YamlIgnore] public virtual int Duration { set; get; }
        [YamlIgnore] public int End { get { return position + Duration; } }

        public UPart() { }

        public abstract int GetMinDurTick(UProject project);
        public abstract int GetMaxPosiTick(UProject project);

        public virtual void BeforeSave(UProject project, UTrack track) { }
        public virtual void AfterLoad(UProject project, UTrack track) { }

        public virtual void Validate(ValidateOptions options, UProject project, UTrack track) { }

        public abstract UPart Clone();
    }

    public class UVoicePart : UPart {
        public int duration;

        [YamlMember(Order = 100)]
        public SortedSet<UNote> notes = new SortedSet<UNote>();
        [YamlMember(Order = 101)]
        public List<UCurve> curves = new List<UCurve>();

        [YamlIgnore] public List<UPhoneme> phonemes = new List<UPhoneme>();
        [YamlIgnore] public int phonemesRevision = 0;
        [YamlIgnore] public List<RenderPhrase> renderPhrases = new List<RenderPhrase>();

        [YamlIgnore] private PhonemizerResponse phonemizerResponse;
        [YamlIgnore] private Dictionary<string, Phonemizer> overridePhonemizers = new Dictionary<string, Phonemizer>();
        [YamlIgnore] private long notesTimestamp;
        [YamlIgnore] private long phonemesTimestamp;

        [YamlIgnore] private ISignalSource mix;

        [YamlIgnore] public bool PhonemesUpToDate => notesTimestamp == phonemesTimestamp;
        [YamlIgnore] public ISignalSource Mix => mix;

        public override string DisplayName => name;
        public override int Duration { get => duration; set => duration = value; }

        public override int GetMinDurTick(UProject project) {
            int endTicks = position + (notes.LastOrDefault()?.End ?? 1);
            project.timeAxis.TickPosToBarBeat(endTicks, out int bar, out int beat, out int remainingTicks);
            return project.timeAxis.BarBeatToTickPos(bar, beat + 1) - position;
        }
        
        public override int GetMaxPosiTick(UProject project) {
            int maxStartTick = position + (notes.FirstOrDefault()?.position ?? Duration);
            project.timeAxis.TickPosToBarBeat(maxStartTick, out int bar, out int beat, out int remainingTicks);
            return project.timeAxis.BarBeatToTickPos(bar, beat - 1);
        }

        public override void BeforeSave(UProject project, UTrack track) {
            foreach (var note in notes) {
                note.BeforeSave(project, track, this);
            }
        }

        public override void AfterLoad(UProject project, UTrack track) {
            foreach (var note in notes) {
                note.AfterLoad(project, track, this);
            }
            Duration = Math.Max(Duration, GetMinDurTick(project));
            foreach (var curve in curves) {
                if (project.expressions.TryGetValue(curve.abbr, out var descriptor)) {
                    curve.descriptor = descriptor;
                }
            }
        }

        public override void Validate(ValidateOptions options, UProject project, UTrack track) {
            UNote lastNote = null;
            foreach (UNote note in notes) {
                note.Prev = lastNote;
                note.Next = null;
                if (lastNote != null) {
                    lastNote.Next = note;
                }
                lastNote = note;
            }
            foreach (UNote note in notes) {
                note.ExtendedDuration = note.duration;
                if (note.Prev != null && note.Prev.End == note.position && note.lyric.StartsWith("+")) {
                    note.Extends = note.Prev.Extends ?? note.Prev;
                    note.Extends.ExtendedDuration = note.End - note.Extends.position;
                } else {
                    note.Extends = null;
                }
            }
            foreach (UNote note in notes) {
                note.Validate(options, project, track, this);
            }
            if (!options.SkipPhonemizer) {
                var noteIndexes = new List<int>();
                var groups = new List<Phonemizer.Note[]>();
                
                var trackPhonemizers = new List<Phonemizer> { track.Phonemizer };
                var notePhonemizerIndices = new List<int>();

                int noteIndex = 0;
                foreach (var note in notes) {
                    if (note.OverlapError || note.Extends != null) {
                        noteIndex++;
                        continue;
                    }
                    var group = new List<UNote>() { note };
                    var next = note.Next;
                    while (next != null && next.Extends == note) {
                        group.Add(next);
                        next = next.Next;
                    }
                    groups.Add(group.Select(e => e.ToPhonemizerNote(track, this)).ToArray());
                    noteIndexes.Add(noteIndex);

                    int pIndex = 0;
                    if (!string.IsNullOrEmpty(note.PhonemizerOverride)) {
                        pIndex = trackPhonemizers.FindIndex(p => p != null && p.Name == note.PhonemizerOverride);
                        if (pIndex == -1) {
                            if (!overridePhonemizers.TryGetValue(note.PhonemizerOverride, out var newPhonemizer)) {
                                var factory = PhonemizerFactory.GetAll().FirstOrDefault(f => f.name == note.PhonemizerOverride);
                                newPhonemizer = factory?.Create();
                                if (newPhonemizer != null) {
                                    overridePhonemizers[note.PhonemizerOverride] = newPhonemizer;
                                }
                            }
                            
                            if (newPhonemizer != null) {
                                trackPhonemizers.Add(newPhonemizer);
                                pIndex = trackPhonemizers.Count - 1;
                            } else {
                                pIndex = 0;
                            }
                        }
                    }
                    notePhonemizerIndices.Add(pIndex);
                    noteIndex++;
                }

                var request = new PhonemizerRequest() {
                    singer = track.Singer,
                    part = this,
                    timestamp = DateTime.Now.ToFileTimeUtc(),
                    noteIndexes = noteIndexes.ToArray(),
                    notes = groups.ToArray(),
                    
                    // NEW: Feed the runner our multi-phonemizer data instead of a single phonemizer
                    phonemizers = trackPhonemizers.ToArray(),
                    notePhonemizerIndices = notePhonemizerIndices.ToArray(),
                    
                    timeAxis = project.timeAxis.Clone(),
                };
                notesTimestamp = request.timestamp;
                DocManager.Inst.PhonemizerRunner?.Push(request);
            }
            lock (this) {
                if (phonemizerResponse != null) {
                    var resp = phonemizerResponse;
                    if (resp.timestamp == notesTimestamp) {
                        phonemes.Clear();
                        foreach (var note in notes) {
                            note.phonemizerExpressions.Clear();
                        }

                        for (int i = 0; i < resp.phonemes.Length; ++i) {
                            var indexes = new List<int>();
                            var note = notes.ElementAtOrDefault(resp.noteIndexes[i]);
                            for (int j = 0; j < resp.phonemes[i].Length; ++j) {
                                var phoneme = new UPhoneme() {
                                    rawPosition = resp.phonemes[i][j].position - position,
                                    rawPhoneme = resp.phonemes[i][j].phoneme,
                                    index = resp.phonemes[i][j].index ?? j,
                                    Parent = note,
                                    ErrorException = resp.phonemes[i][j].error
                                };
                                // Check for duplicate indexes
                                if (phonemes.Any(p => p.Parent == phoneme.Parent && p.index == phoneme.index)) {
                                    try {
                                        throw new ArgumentException("Duplicate phoneme index.");
                                    } catch (Exception e) {
                                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                                        continue;
                                    }
                                }
                                phonemes.Add(phoneme);
                                indexes.Add(phoneme.index);
                                if (resp.phonemes[i][j].expressions != null && resp.phonemes[i][j].expressions.Any()) {
                                    resp.phonemes[i][j].expressions.ForEach(exp => {
                                        if (track.TryGetExpDescriptor(project, exp.abbr, out var descriptor)) {
                                            if (descriptor.type != UExpressionType.Curve && descriptor.min <= exp.value && exp.value <= descriptor.max) {
                                                note.phonemizerExpressions.Add(new UExpression(descriptor) {
                                                    index = phoneme.index,
                                                    value = exp.value
                                                });
                                            }
                                        }
                                    });
                                }
                            }
                            indexes.Sort();
                            note.phonemeIndexes = indexes.ToArray();
                        }
                        phonemesTimestamp = resp.timestamp;
                    }
                    phonemizerResponse = null;
                }
            }
            if (!options.SkipPhoneme) {
                UPhoneme lastPhoneme = null;
                foreach (var phoneme in phonemes) {
                    phoneme.Prev = lastPhoneme;
                    phoneme.Next = null;
                    if (lastPhoneme != null) {
                        lastPhoneme.Next = phoneme;
                    }
                    lastPhoneme = phoneme;
                }
                foreach (var note in notes) {
                    for (int i = note.phonemeOverrides.Count - 1; i >= 0; --i) {
                        if (note.phonemeOverrides[i].IsEmpty) {
                            note.phonemeOverrides.RemoveAt(i);
                        }
                    }
                }
                foreach (var phoneme in phonemes) {
                    phoneme.position = phoneme.rawPosition;
                    phoneme.phoneme = phoneme.rawPhoneme;
                    phoneme.preutterDelta = null;
                    phoneme.overlapDelta = null;
                    var note = phoneme.Parent;
                    if (note == null) {
                        continue;
                    }
                    var o = note.phonemeOverrides.FirstOrDefault(o => o.index == phoneme.index);
                    if (o != null) {
                        phoneme.position += o.offset ?? 0;
                        phoneme.phoneme = !string.IsNullOrWhiteSpace(o.phoneme) ? o.phoneme : phoneme.rawPhoneme;
                        phoneme.preutterDelta = o.preutterDelta;
                        phoneme.overlapDelta = o.overlapDelta;
                    }
                }
                // Safety treatment after phonemizer output and phoneme overrides.
                for (int i = phonemes.Count - 2; i >= 0; --i) {
                    phonemes[i].position = Math.Min(phonemes[i].position, phonemes[i + 1].position - 10);
                }
                foreach (var phoneme in phonemes) {
                    var note = phoneme.Parent;
                    if (note == null) {
                        continue;
                    }
                    phoneme.Validate(options, project, track, this, note);
                }
            }
            renderPhrases.Clear();
            if (PhonemesUpToDate) {
                renderPhrases.AddRange(RenderPhrase.FromPart(project, track, this));
            }
        }

        internal void SetPhonemizerResponse(PhonemizerResponse response) {
            lock (this) {
                phonemizerResponse = response;
            }
        }

        internal RenderPartRequest GetRenderRequest() {
            lock (this) {
                return new RenderPartRequest() {
                    part = this,
                    timestamp = notesTimestamp,
                    trackNo = trackNo,
                    phrases = renderPhrases.ToArray(),
                };
            }
        }

        internal void SetMix(ISignalSource mix) {
            lock (this) {
                this.mix = mix;
            }
        }

        public override UPart Clone() {
            return new UVoicePart() {
                name = name,
                comment = comment,
                trackNo = trackNo,
                position = position,
                notes = new SortedSet<UNote>(notes.Select(note => note.Clone())),
                curves = curves.Select(c => c.Clone()).ToList(),
                Duration = Duration,
            };
        }
    }

    public class UWavePart : UPart {
        string _filePath;

        [YamlIgnore]
        public string FilePath {
            set {
                _filePath = value;
                name = Path.GetFileName(value);
            }
            get { return _filePath; }
        }

        [YamlMember(Order = 100)] public string relativePath;
        [YamlMember(Order = 101)] public double fileDurationMs;
        [YamlMember(Order = 102)] public int skip;
        [YamlMember(Order = 103)] public int trim;
        [YamlMember(Order = 104)] public int fadein;
        [YamlMember(Order = 105)] public int fadeout;

        [YamlIgnore]
        public override string DisplayName => Missing ? $"[Missing] {name}" : name;
        [YamlIgnore]
        public override int Duration {
            get => duration;
            set { }
        }
        [YamlIgnore] bool Missing { get; set; }
        [YamlIgnore] public float[] Samples { get; private set; }
        [YamlIgnore] public Task<DiscreteSignal[]> Peaks { get; set; }

        [YamlIgnore] public int channels;
        [YamlIgnore] public int sampleRate;
        [YamlIgnore] public int peaksSampleRate;

        private int duration;

        public double GetSkipMs(UProject project) {
            return project.timeAxis.MsBetweenTickPos(position - skip, position);
        }

        public double GetTrimMs(UProject project) {
            return project.timeAxis.MsBetweenTickPos(End, End + trim);
        }

        public override int GetMinDurTick(UProject project) {
            return project.resolution;
        }

        public override int GetMaxPosiTick(UProject project) {
            return End - project.resolution;
        }

        public override UPart Clone() {
            var part = new UWavePart() {
                _filePath = _filePath,
                relativePath = relativePath,
                skip = skip,
                trim = trim,
            };
            part.Load(DocManager.Inst.Project);
            return part;
        }

        private readonly object loadLockObj = new object();
        public void Load(UProject project) {
            try {
                using (var waveStream = Format.Wave.OpenFile(FilePath)) {
                    fileDurationMs = waveStream.TotalTime.TotalMilliseconds;
                    channels = waveStream.WaveFormat.Channels;
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load wave part {FilePath}");
                Missing = true;
                if (fileDurationMs == 0) {
                    fileDurationMs = 10000;
                }
            }
            lock (loadLockObj) {
                if (Samples != null || Missing) {
                    Peaks = Task.FromResult<DiscreteSignal[]>(null);
                    return;
                }
            }
            UpdateDuration(project);
            Peaks = Task.Run(() => {
                var stopwatch = Stopwatch.StartNew();
                using (var waveStream = Format.Wave.OpenFile(FilePath)) {
                    var samples = Format.Wave.GetStereoSamples(waveStream);
                    lock (loadLockObj) {
                        sampleRate = 44100; // GetStereoSamples resamples waveStream.
                        Samples = samples;
                    }
                }
                stopwatch.Stop();
                Log.Information($"Loaded {FilePath} {stopwatch.Elapsed}");

                stopwatch.Restart();
                float[][] channelSamples = new float[channels][];
                int length = Samples.Length / channels;
                for (int i = 0; i < channels; ++i) {
                    channelSamples[i] = new float[length];
                }
                int pos = 0;
                for (int i = 0; i < length; ++i) {
                    for (int j = 0; j < channels; ++j) {
                        channelSamples[j][i] = Samples[pos++];
                    }
                }
                DiscreteSignal[] peaks = new DiscreteSignal[channels];
                var resampler = new Resampler();
                for (int i = 0; i < channels; ++i) {
                    peaks[i] = new DiscreteSignal(sampleRate, channelSamples[i], false);
                    peaks[i] = resampler.Decimate(peaks[i], 10);
                    for (int j = 0; j < peaks[i].Samples.Length; ++j) {
                        peaks[i].Samples[j] = Math.Clamp(peaks[i].Samples[j], -1, 1);
                    }
                }
                peaksSampleRate = sampleRate / 10;
                stopwatch.Stop();
                Log.Information($"Built peaks {FilePath} {stopwatch.Elapsed}");
                return peaks;
            });
        }

        public override void Validate(ValidateOptions options, UProject project, UTrack track) {
            UpdateDuration(project);
        }

        private void UpdateDuration(UProject project) {
            double fileStartMs = project.timeAxis.TickPosToMsPos(position - skip);
            int fileEnd = project.timeAxis.MsPosToTickPos(fileStartMs + fileDurationMs);
            duration = fileEnd - trim - position;
        }

        public override void BeforeSave(UProject project, UTrack track) {
            relativePath = Path.GetRelativePath(Path.GetDirectoryName(project.FilePath), FilePath);
        }

        public override void AfterLoad(UProject project, UTrack track) {
            try {
                FilePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.FilePath), relativePath ?? ""));
            } catch {
                if (string.IsNullOrWhiteSpace(FilePath)) {
                    throw;
                }
            }
            Load(project);
        }

        public ISignalSource TrimSamples(UProject project) {
            double offsetMs = project.timeAxis.TickPosToMsPos(position);
            double estimatedLengthMs = project.timeAxis.TickPosToMsPos(End) - offsetMs;
            var waveSource = new WaveSource(
                offsetMs,
                estimatedLengthMs,
                0, channels);
            int skipCount = (int)(GetSkipMs(project) * sampleRate / 1000) * channels;
            int trimCount = (int)(GetTrimMs(project) * sampleRate / 1000) * channels;
            int remainingCount = Samples.Length - skipCount - trimCount;
            if (remainingCount <= 0) {
                waveSource.SetSamples(new float[0]);
                return waveSource;
            }
            float[] trimmedSamples = new float[remainingCount];
            Array.Copy(Samples, skipCount, trimmedSamples, 0, remainingCount);

            int fadeinFrames = (int)(project.timeAxis.MsBetweenTickPos(position, position + fadein) * sampleRate / 1000);
            for (int i = 0; i < fadeinFrames && i * channels < remainingCount; i++) {
                float gain = (float)i / fadeinFrames;
                for (int j = 0; j < channels; j++) {
                    trimmedSamples[i * channels + j] *= gain;
                }
            }
            int fadeoutFrames = (int)(project.timeAxis.MsBetweenTickPos(End - fadeout, End) * sampleRate / 1000);
            for (int i = 0; i < fadeoutFrames && i * channels < remainingCount; i++) {
                float gain = (float)i / fadeoutFrames;
                for (int j = 0; j < channels; j++) {
                    int targetIdx = remainingCount - 1 - (i * channels + (channels - 1 - j));
                    if (targetIdx >= 0) {
                        trimmedSamples[targetIdx] *= gain;
                    }
                }
            }
            waveSource.SetSamples(trimmedSamples);
            return waveSource;
        }
    }
}
