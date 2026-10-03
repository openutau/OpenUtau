using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Classic;
using Serilog;

namespace OpenUtau.Core.Render {
    public class Progress {
        readonly int total;
        int completed = 0;

        Task pending = null;
        double pendingProgress;
        string pendingInfo = string.Empty;

        internal bool DispatchInFlight => pending != null && !pending.IsCompleted;

        public Progress(int total) {
            this.total = total;
        }

        public void Complete(int n, string info) {
            Interlocked.Add(ref completed, n);
            Notify(completed * 100.0 / total, info);
        }

        public void Clear() {
            Notify(0, string.Empty);
        }

        private void Notify(double progress, string info) {
            lock (this) {
                pendingProgress = progress;
                pendingInfo = info;
                if (pending == null || pending.IsCompleted) {
                    StartPending();
                }
            }
        }

        private void StartPending() {
            pending = new Task(Dispatch);
            pending.Start(DocManager.Inst.MainScheduler ?? TaskScheduler.Default);
        }

        private void Dispatch() {
            double progress;
            string info;
            lock (this) {
                progress = pendingProgress;
                info = pendingInfo;
            }
            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(progress, info));
            lock (this) {
                if (progress != pendingProgress || info != pendingInfo) {
                    StartPending();
                }
            }
        }
    }

    class RenderPartRequest {
        public UVoicePart part;
        public long timestamp;
        public int trackNo;
        public RenderPhrase[] phrases;
        public double[] phraseOffsetMs;
        public double[] phraseEstimatedLengthMs;
        public int completedPhrases = 0;
    }

    class RenderEngine {
        readonly UProject project;
        readonly int startTick;
        readonly int endTick;
        readonly int trackNo;
        readonly UVoicePart focusPart;
        readonly int focusTick;

        static readonly ConcurrentDictionary<string, float[]> MorphBlendCache =
            new ConcurrentDictionary<string, float[]>();

        public RenderEngine(
            UProject project,
            int startTick = 0,
            int endTick = -1,
            int trackNo = -1,
            UVoicePart focusPart = null,
            int focusTick = -1) {
            this.project = project;
            this.startTick = startTick;
            this.endTick = endTick;
            this.trackNo = trackNo;
            this.focusPart = focusPart;
            this.focusTick = focusTick;
        }

        public Tuple<WaveMix, List<Fader>> RenderMixdown(
                TaskScheduler uiScheduler, ref CancellationTokenSource cancellation, bool wait, bool applyMixFx, MixPlanner planner) {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            double startMs = project.timeAxis.TickPosToMsPos(startTick);
            double endMs = endTick == -1 ? double.PositiveInfinity : project.timeAxis.TickPosToMsPos(endTick);
            var faders = new List<Fader>();
            var trackOutputs = new List<ISignalSource>();
            var requests = PrepareRequests()
                .Where(request => request.phrases.Length > 0
                    && request.phraseOffsetMs.Zip(request.phraseEstimatedLengthMs, (o, l) => o + l).Max() > startMs
                    && (double.IsPositiveInfinity(endMs) || request.phraseOffsetMs.Min() < endMs))
                .ToArray();

            var specs = new List<MixPlanner.SlotSpec>();
            foreach (var request in requests) {
                for (int i = 0; i < request.phrases.Length; ++i) {
                    specs.Add(new MixPlanner.SlotSpec(
                        request.part, request.trackNo, request.phrases[i].hash,
                        request.phraseOffsetMs[i], request.phraseEstimatedLengthMs[i], 1));
                }
            }
            Dictionary<UWavePart, (double offsetMs, double estimatedLengthMs, int channels, float[] pcm)> waveTrims = null;
            foreach (var part in project.parts.OfType<UWavePart>()) {
                if (trackNo != -1 && part.trackNo != trackNo) {
                    continue;
                }
                if (part.Samples == null) {
                    continue;
                }
                var trim = part.GetTrimmedSamples(project);
                if (waveTrims == null) {
                    waveTrims = new Dictionary<UWavePart, (double offsetMs, double estimatedLengthMs, int channels, float[] pcm)>();
                }
                waveTrims[part] = trim;
                specs.Add(new MixPlanner.SlotSpec(part, part.trackNo, 0, trim.offsetMs, trim.estimatedLengthMs, trim.channels));
            }
            planner.BeginSession(specs);
            for (int i = 0; i < project.tracks.Count; ++i) {
                if (trackNo != -1 && trackNo != i) {
                    continue;
                }
                var track = project.tracks[i];
                if (waveTrims != null) {
                    foreach (var wave in waveTrims.Keys) {
                        if (wave.trackNo != i) {
                            continue;
                        }
                        var trim = waveTrims[wave];
                        planner.RegisterWavePcm(wave, trim.offsetMs, trim.estimatedLengthMs, trim.channels, trim.pcm);
                    }
                }
                var fader = new Fader(planner.GetTrackSource(i));
                fader.Scale = PlaybackManager.DecibelToVolume(track.Muted ? -24 : track.Volume);
                fader.Pan = (float)track.Pan;
                fader.SetScaleToTarget();
                faders.Add(fader);

                // Playback follows the track's MixFx live so Track Polish
                // edits are heard while playing; export uses a fixed snapshot.
                ISignalSource trackOut = !applyMixFx ? fader
                    : wait ? MixFxSource.WrapWith(fader, track.MixFx)
                    : MixFxSource.WrapLive(fader, track);
                trackOutputs.Add(trackOut);
            }
            var task = Task.Run(() => {
                RenderRequests(requests, newCancellation, playing: !wait, planner);
            });
            task.ContinueWith(task => {
                if (task.IsFaulted && !wait) {
                    Log.Error(task.Exception.Flatten(), "Failed to render.");
                    PlaybackManager.Inst.StopPlayback();
                    var flatEx = task.Exception.Flatten();
                    var innerEx = flatEx.InnerExceptions.ToList();
                    if (innerEx.Count == 1 && innerEx[0] is MessageCustomizableException mce) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(mce));
                    } else if (innerEx.Any(e => e is DllNotFoundException)) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(
                            new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>: <translate:errors.install.cpp>", flatEx)));
                    } else if (innerEx.Any(e => e is ResamplerFailedException)) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(
                            new MessageCustomizableException("Failed to render.", "<translate:errors.resampler.failed.message>", flatEx)));
                    } else {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(
                            new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", flatEx)));
                    }
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, uiScheduler);
            if (wait) {
                task.Wait();
            }
            // Build the final mix.  All tracks (FX-wrapped or dry) sum into
            // a single WaveMix.  Bypass-as-pointer-identity in WrapWith keeps
            // disabled tracks zero-cost on export.
            var resultMix = new WaveMix(trackOutputs);
            return Tuple.Create(resultMix, faders);
        }

        public List<SlotMixSource> RenderTracks(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation, MixPlanner planner) {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            var trackMixes = new List<SlotMixSource>();
            var requests = PrepareRequests();
            if (requests.Length == 0) {
                return trackMixes;
            }
            var specs = new List<MixPlanner.SlotSpec>();
            foreach (var request in requests) {
                for (int i = 0; i < request.phrases.Length; ++i) {
                    specs.Add(new MixPlanner.SlotSpec(
                        request.part, request.trackNo, request.phrases[i].hash,
                        request.phraseOffsetMs[i], request.phraseEstimatedLengthMs[i], 1));
                }
            }
            planner.BeginSession(specs);
            Enumerable.Range(0, requests.Max(req => req.trackNo) + 1)
                .Select(trackNo => requests.Where(req => req.trackNo == trackNo).ToArray())
                .ToList()
                .ForEach(trackRequests => {
                    if (trackRequests.Length == 0) {
                        trackMixes.Add(null);
                    } else {
                        RenderRequests(trackRequests, newCancellation, false, planner);
                        trackMixes.Add(planner.GetTrackSource(trackRequests[0].trackNo));
                    }
                });
            return trackMixes;
        }

        public void PreRenderProject(ref CancellationTokenSource cancellation, MixPlanner planner) {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            Task.Run(() => {
                try {
                    Thread.Sleep(200);
                    if (newCancellation.Token.IsCancellationRequested) {
                        return;
                    }
                    RenderRequests(PrepareRequests(), newCancellation, false, planner);
                } catch (Exception e) {
                    if (!newCancellation.IsCancellationRequested) {
                        Log.Error(e, "Failed to pre-render.");
                        DocManager.Inst.ExecuteCmd(new ToastNotification("Pianoroll", "Failed to pre-render.", "errors.failed.prerender", e));
                    }
                }
            });
        }

        private RenderPartRequest[] PrepareRequests() {
            UVoicePart[] parts;
            lock (project) {
                parts = project.parts
                    .Where(part => part is UVoicePart && (trackNo == -1 || part.trackNo == trackNo))
                    .Where(part => !Preferences.Default.SkipRenderingMutedTracks || !project.tracks[part.trackNo].Muted)
                    .Select(part => part as UVoicePart)
                    .ToArray();
            }
            foreach (var part in parts) {
                part.WaitPhraseSource(TimeSpan.FromSeconds(10));
            }
            RenderPartRequest[] requests;
            lock (project) {
                requests = parts
                    .Select(part => part.GetRenderRequest())
                    .Where(request => request != null)
                    .ToArray();
            }
            foreach (var request in requests) {
                if (endTick != -1) {
                    request.phrases = request.phrases
                        .Where(phrase => phrase.end > startTick && (endTick == -1 || phrase.position < endTick))
                        .ToArray();
                }
                request.phraseOffsetMs = new double[request.phrases.Length];
                request.phraseEstimatedLengthMs = new double[request.phrases.Length];
                for (var i = 0; i < request.phrases.Length; i++) {
                    var layout = request.phrases[i].renderer.Layout(request.phrases[i]);
                    request.phraseOffsetMs[i] = layout.positionMs - layout.leadingMs;
                    request.phraseEstimatedLengthMs[i] = layout.estimatedLengthMs;
                }
            }
            return requests;
        }

        private void RenderRequests(
            RenderPartRequest[] requests,
            CancellationTokenSource cancellation,
            bool playing,
            MixPlanner planner) {
            if (requests.Length == 0 || cancellation.IsCancellationRequested) {
                return;
            }
            var tuples = new List<(RenderPhrase phrase, double offsetMs, double estimatedLengthMs, RenderPartRequest request)>();
            foreach (var req in requests) {
                for (int i = 0; i < req.phrases.Length; ++i) {
                    tuples.Add((req.phrases[i], req.phraseOffsetMs[i], req.phraseEstimatedLengthMs[i], req));
                }
            }
            var tupleArray = tuples.ToArray();
            if (tupleArray.Length == 0) {
                return;
            }
            if (playing) {
                tupleArray = OrderForPlayback(tupleArray);
            } else if (focusPart != null || focusTick >= 0) {
                tupleArray = OrderForPreRender(tupleArray);
            }
            var progress = new Progress(tupleArray.Sum(t => t.phrase.phones.Length));
            bool maintainCoverage = startTick == 0 && endTick == -1;
            var coverageRanges = maintainCoverage
                ? new Dictionary<UVoicePart, List<(int start, int end)>>()
                : null;

            foreach (var tuple in tupleArray) {
                if (cancellation.IsCancellationRequested) {
                    break;
                }
                var phrase = tuple.phrase;
                var request = tuple.request;
                RealCurveUpdate[]? publishedUpdates = null;
                var renderEvents = phrase.renderer.SupportsRealCurve
                    ? new RenderPhraseEvents(realCurves => {
                        publishedUpdates = PublishRealCurveUpdates(request.part, phrase, realCurves);
                    })
                    : null;

                var morphTracks = GetActiveMorphTracks(phrase, request.part, request.trackNo);

                if (morphTracks.Count == 0) {
                    phrase.renderSalt = 0;
                    var task = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true, renderEvents);
                    task.Wait();
                    if (cancellation.IsCancellationRequested) {
                        break;
                    }
                    planner.RegisterPcm(request.part, phrase.hash, tuple.offsetMs, tuple.estimatedLengthMs, 1, task.Result.samples);
                } else {
                    string morphKey = $"{phrase.hash:x16}|" +
                        string.Join(",", morphTracks.Select(t => $"{t.TargetColor}:{t.Flag}:{t.Abbr}"));

                    if (!MorphBlendCache.TryGetValue(morphKey, out var blended)) {
                        phrase.renderSalt = 0;
                        var taskA = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true, renderEvents);
                        taskA.Wait();
                        if (cancellation.IsCancellationRequested) {
                            break;
                        }
                        float[] samplesA = taskA.Result.samples;
                        if (samplesA == null || samplesA.Length == 0) {
                            continue;
                        }

                        var otoField = typeof(RenderPhone).GetField("oto", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var hashField = typeof(RenderPhone).GetField("hash", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var flagsField = typeof(RenderPhone).GetField("flags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var phraseHashField = typeof(RenderPhrase).GetField("hash", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        var originalOtos = phrase.phones.Select(p => p.oto).ToArray();
                        var originalHashes = phrase.phones.Select(p => p.hash).ToArray();
                        var originalFlags = phrase.phones.Select(p => p.flags).ToArray();
                        ulong originalPhraseHash = phrase.hash;

                        var singer = DocManager.Inst.Project.tracks[request.trackNo].Singer;
                        var colorAudios = new List<float[]>();
                        var tempAuxCacheFiles = new List<string>();
                        int pitchStart = phrase.position - phrase.leading;

                        try {
                            for (int t = 0; t < morphTracks.Count; t++) {
                                var track = morphTracks[t];
                                ulong salt = (ulong)(t + 1) * 0x5858585858585858UL;
                                float[] samplesB;

                                try {
                                    phrase.renderSalt = salt;

                                    for (int i = 0; i < phrase.phones.Length; i++) {
                                        var phone = phrase.phones[i];

                                        int pStartTick = phrase.position + phone.position - phone.leading;
                                        int pEndTick = phrase.position + phone.position + phone.duration;
                                        int idxStart = Math.Max(0, (pStartTick - pitchStart) / 5);
                                        int idxEnd = Math.Min(track.RawCurve.Length - 1, (pEndTick - pitchStart) / 5);

                                        bool isPhoneActive = false;
                                        for (int k = idxStart; k <= idxEnd; k++) {
                                            if (track.WeightFunc(track.RawCurve[k]) > 0.001f) {
                                                isPhoneActive = true;
                                                break;
                                            }
                                        }

                                        if (!isPhoneActive) continue;

                                        if (!string.IsNullOrEmpty(track.TargetColor)) {
                                            if (TryHijackOto(singer, phone, track.TargetColor, out var secondaryOto)) {
                                                otoField?.SetValue(phone, secondaryOto);
                                            }
                                        }

                                        if (!string.IsNullOrEmpty(track.Flag)) {
                                            var currentFlags = originalFlags[i]?.ToList() ?? new List<Tuple<string, int?, string>>();
                                            currentFlags.RemoveAll(f =>
                                                (!string.IsNullOrEmpty(track.Abbr) && f.Item3 == track.Abbr) ||
                                                (!string.IsNullOrEmpty(track.FlagBase) && (f.Item1 == track.FlagBase || f.Item1.StartsWith(track.FlagBase))));
                                            currentFlags.Add(Tuple.Create<string, int?, string>(track.Flag, null, track.Abbr));
                                            flagsField?.SetValue(phone, currentFlags.ToArray());
                                        }

                                        hashField?.SetValue(phone, phone.hash ^ salt);
                                    }
                                    phraseHashField?.SetValue(phrase, phrase.hash ^ salt);

                                    ulong saltedHash = phrase.hash;
                                    tempAuxCacheFiles.Add(Path.Join(PathManager.Inst.CachePath, $"wdl-v1-{saltedHash:x16}.wav"));
                                    tempAuxCacheFiles.Add(Path.Join(PathManager.Inst.CachePath, $"wdl-v2-{saltedHash:x16}.wav"));
                                    tempAuxCacheFiles.Add(Path.Join(PathManager.Inst.CachePath, $"cat-{saltedHash:x16}.wav"));

                                    foreach (var phone in phrase.phones) {
                                        var item = new ResamplerItem(phrase, phone);
                                        tempAuxCacheFiles.Add(item.outputFile);
                                    }

                                    var taskB = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true);
                                    taskB.Wait();
                                    samplesB = taskB.Result.samples;
                                } finally {
                                    phrase.renderSalt = 0;
                                    for (int i = 0; i < phrase.phones.Length; i++) {
                                        otoField?.SetValue(phrase.phones[i], originalOtos[i]);
                                        hashField?.SetValue(phrase.phones[i], originalHashes[i]);
                                        flagsField?.SetValue(phrase.phones[i], originalFlags[i]);
                                    }
                                    phraseHashField?.SetValue(phrase, originalPhraseHash);
                                }

                                if (cancellation.IsCancellationRequested) break;

                                float[] alignedB = new float[samplesA.Length];
                                if (samplesB != null) {
                                    int copyLen = Math.Min(samplesA.Length, samplesB.Length);
                                    Array.Copy(samplesB, alignedB, copyLen);
                                } else {
                                    Array.Copy(samplesA, alignedB, samplesA.Length);
                                }
                                colorAudios.Add(alignedB);
                            }

                            if (cancellation.IsCancellationRequested) break;

                            const int fftSize = 2048;
                            const int hopSize = 512;
                            int targetLength = samplesA.Length;
                            int frameCount = Math.Max(1, (targetLength - fftSize) / hopSize + 1);

                            var colorCurves = new List<float[]>();
                            for (int t = 0; t < morphTracks.Count; t++) {
                                colorCurves.Add(new float[frameCount]);
                            }

                            for (int f = 0; f < frameCount; f++) {
                                double timeMs = phrase.positionMs - phrase.leadingMs
                                    + (double)(f * hopSize + fftSize / 2) / 44100.0 * 1000.0;
                                double tick = project.timeAxis.MsPosToTickPos(timeMs);
                                int curveIndex = (int)Math.Max(0, (tick - pitchStart) / 5.0);

                                float totalWeight = 0f;
                                for (int t = 0; t < morphTracks.Count; t++) {
                                    var track = morphTracks[t];
                                    float weight = 0f;
                                    if (track.RawCurve.Length > 0 && curveIndex < track.RawCurve.Length) {
                                        weight = track.WeightFunc(track.RawCurve[curveIndex]);
                                    }
                                    colorCurves[t][f] = weight;
                                    totalWeight += weight;
                                }

                                if (totalWeight > 100f) {
                                    float scale = 100f / totalWeight;
                                    for (int t = 0; t < morphTracks.Count; t++) {
                                        colorCurves[t][f] *= scale;
                                    }
                                }
                            }

                            blended = CrossSynthDSP.MorphN(samplesA, colorAudios, colorCurves);

                            if (blended.Length != targetLength) {
                                Array.Resize(ref blended, targetLength);
                            }

                            if (MorphBlendCache.Count > 1024) {
                                MorphBlendCache.Clear();
                            }
                            MorphBlendCache[morphKey] = blended;
                        } finally {
                            if (Preferences.Default.AutoDeleteMorphCache) {
                                Task.Run(() => {
                                    foreach (var auxFile in tempAuxCacheFiles) {
                                        CleanUpIntermediateAudio(auxFile);
                                    }
                                });
                            }
                        }
                    }
                    planner.RegisterPcm(request.part, phrase.hash, tuple.offsetMs, tuple.estimatedLengthMs, 1, blended);
                }

                WaveformRefresh.Request();
                if (publishedUpdates == null) {
                    publishedUpdates = PublishRealCurveUpdates(request.part, phrase);
                }
                if (coverageRanges != null && publishedUpdates != null) {
                    AccumulateCoverage(coverageRanges, request.part, publishedUpdates);
                }
                if (++request.completedPhrases == request.phrases.Length) {
                    planner.MarkPartComplete(request.part, request.phrases.Select(p => p.hash));
                    if (coverageRanges != null &&
                        phrase.renderer.SupportsRealCurve &&
                        coverageRanges.TryGetValue(request.part, out var ranges) &&
                        ranges.Count > 0) {
                        DocManager.Inst.ExecuteCmd(new RealCurveCoverageNotification(request.part, ranges));
                    }
                    DocManager.Inst.ExecuteCmd(new PartRenderedNotification(request.part));
                }
            }
            progress.Clear();
            DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
        }

        private List<ActiveMorphTrack> GetActiveMorphTracks(RenderPhrase phrase, UVoicePart part, int trackNo) {
            var list = new List<ActiveMorphTrack>();
            if (phrase.curves == null || part == null) return list;

            var singer = DocManager.Inst.Project.tracks[trackNo].Singer;
            var uniqueColors = singer?.Subbanks?.Select(s => s.Color).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList() ?? new List<string>();

            int pitchStart = phrase.position - phrase.leading;
            int pitchEnd = phrase.end;

            foreach (var tuple in phrase.curves) {
                string abbr = tuple.Item1;
                float[] curveSamples = tuple.Item2;
                if (curveSamples == null || curveSamples.Length == 0) continue;

                project.expressions.TryGetValue(abbr, out var exp);
                string matchedColor = null;

                if (abbr.StartsWith("cl", StringComparison.OrdinalIgnoreCase) && int.TryParse(abbr.Substring(2), out int idx)) {
                    if (idx > 0 && idx <= uniqueColors.Count) {
                        matchedColor = uniqueColors[idx - 1];
                    }
                } else if (exp != null && exp.type == UExpressionType.MorphingCurve && !string.IsNullOrEmpty(exp.name)) {
                    matchedColor = uniqueColors.FirstOrDefault(c => exp.name.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (matchedColor != null) {
                    if (!curveSamples.Any(v => v > 0.001f)) continue;
                    list.Add(new ActiveMorphTrack {
                        Abbr = abbr,
                        TargetColor = matchedColor,
                        FlagBase = null,
                        Flag = "",
                        RawCurve = curveSamples,
                        WeightFunc = val => Math.Clamp(val, 0f, 100f)
                    });
                    continue;
                }

                if (exp != null && exp.type == UExpressionType.MorphingCurve && (exp.isFlag || !string.IsNullOrEmpty(exp.flag))) {
                    string flagBase = string.IsNullOrEmpty(exp.flag) ? exp.abbr : exp.flag;
                    float defVal = exp.defaultValue;
                    float cMax = curveSamples.Max();
                    float cMin = curveSamples.Min();

                    if (cMax > defVal + 0.5f) {
                        int posFlagVal = (int)Math.Round(cMax);
                        string posFlag = $"{flagBase}{posFlagVal}";
                        float rangePos = cMax - defVal;

                        list.Add(new ActiveMorphTrack {
                            Abbr = abbr,
                            FlagBase = flagBase,
                            TargetColor = null,
                            Flag = posFlag,
                            RawCurve = curveSamples,
                            WeightFunc = val => val > defVal ? Math.Clamp((val - defVal) / rangePos * 100f, 0f, 100f) : 0f
                        });
                    }

                    if (cMin < defVal - 0.5f) {
                        int negFlagVal = (int)Math.Round(cMin);
                        string negFlag = $"{flagBase}{negFlagVal}";
                        float rangeNeg = defVal - cMin;

                        list.Add(new ActiveMorphTrack {
                            Abbr = abbr,
                            FlagBase = flagBase,
                            TargetColor = null,
                            Flag = negFlag,
                            RawCurve = curveSamples,
                            WeightFunc = val => val < defVal ? Math.Clamp((defVal - val) / rangeNeg * 100f, 0f, 100f) : 0f
                        });
                    }
                }
            }

            return list;
        }

        private static bool TryHijackOto(USinger singer, RenderPhone phone, string targetColor, out UOto targetOto) {
            targetOto = null;
            if (singer == null || singer.Subbanks == null || phone.oto == null) return false;

            string basePhoneme = phone.oto.Phonetic ?? phone.phoneme;

            if (singer.TryGetMappedOto(basePhoneme, phone.tone, targetColor, out targetOto)) {
                return true;
            }

            var targetSubbanks = singer.Subbanks
                .Where(b => string.Equals(b.Color, targetColor, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (targetSubbanks.Count == 0) return false;

            foreach (var sub in targetSubbanks) {
                string candidate = (sub.Prefix ?? "") + phone.phoneme + (sub.Suffix ?? "");
                if (singer.TryGetOto(candidate, out targetOto)) return true;
            }

            string rawAlias = phone.oto.Alias;
            var baseSub = singer.Subbanks.FirstOrDefault(b =>
                (!string.IsNullOrEmpty(b.Prefix) && rawAlias.StartsWith(b.Prefix)) ||
                (!string.IsNullOrEmpty(b.Suffix) && rawAlias.EndsWith(b.Suffix)));

            string root = rawAlias;
            if (baseSub != null) {
                if (!string.IsNullOrEmpty(baseSub.Prefix) && root.StartsWith(baseSub.Prefix)) root = root.Substring(baseSub.Prefix.Length);
                if (!string.IsNullOrEmpty(baseSub.Suffix) && root.EndsWith(baseSub.Suffix)) root = root.Substring(0, root.Length - baseSub.Suffix.Length);
            }

            foreach (var sub in targetSubbanks) {
                string candidate = (sub.Prefix ?? "") + root + (sub.Suffix ?? "");
                if (singer.TryGetOto(candidate, out targetOto)) return true;
            }

            return false;
        }

        private static void CleanUpIntermediateAudio(string wavPath) {
            if (string.IsNullOrEmpty(wavPath)) return;
            try {
                if (File.Exists(wavPath)) {
                    File.Delete(wavPath);
                }
            } catch { }
            CleanUpMetaFiles(wavPath);
        }

        private static void CleanUpMetaFiles(string filePath) {
            if (string.IsNullOrEmpty(filePath)) return;
            try {
                string ext = Path.GetExtension(filePath);
                string noExt = filePath.Substring(0, filePath.Length - ext.Length);
                string frqExt = ext.Replace('.', '_') + ".frq";

                string[] sidecarFiles = new string[] {
                    noExt + frqExt,
                    filePath + ".llsm",
                    filePath + ".uspec",
                    filePath + ".dio",
                    filePath + ".star",
                    filePath + ".platinum",
                    filePath + ".frc",
                    filePath + ".pmk",
                    filePath + ".vs4ufrq",
                    noExt + ".rudb",
                    noExt + ".sc.npz",
                    noExt + ".sc",
                    noExt + ".hifi.npz"
                };

                foreach (string p in sidecarFiles) {
                    try {
                        if (File.Exists(p)) {
                            File.Delete(p);
                        }
                    } catch { }
                }
            } catch { }
        }

        private RealCurveUpdate[]? PublishRealCurveUpdates(UVoicePart part, RenderPhrase phrase) {
            if (!phrase.renderer.SupportsRealCurve) {
                return null;
            }
            try {
                var updates = RealCurveUpdater.LoadPhraseUpdates(part, phrase);
                if (updates.Length > 0) {
                    DocManager.Inst.ExecuteCmd(new RealCurvesUpdatedNotification(part, updates));
                    return updates;
                }
            } catch (Exception e) {
                Log.Debug(e, "Failed to refresh rendered real curves.");
            }
            return null;
        }

        private RealCurveUpdate[]? PublishRealCurveUpdates(
            UVoicePart part,
            RenderPhrase phrase,
            IReadOnlyList<RenderRealCurveResult> realCurves) {
            if (realCurves.Count == 0) {
                return null;
            }
            try {
                var updates = RealCurveUpdater.BuildUpdates(part, phrase, realCurves);
                if (updates.Length > 0) {
                    DocManager.Inst.ExecuteCmd(new RealCurvesUpdatedNotification(part, updates));
                    return updates;
                }
            } catch (Exception e) {
                Log.Debug(e, "Failed to publish rendered real curves.");
            }
            return null;
        }

        private static void AccumulateCoverage(
            Dictionary<UVoicePart, List<(int start, int end)>> coverage,
            UVoicePart part,
            RealCurveUpdate[] updates) {
            if (!coverage.TryGetValue(part, out var ranges)) {
                ranges = new List<(int start, int end)>();
                coverage[part] = ranges;
            }
            foreach (var update in updates) {
                if (update.IsValid) {
                    ranges.Add((update.startTick, update.endTick));
                }
            }
        }

        private (RenderPhrase phrase, double offsetMs, double estimatedLengthMs, RenderPartRequest request)[] OrderForPlayback(
            (RenderPhrase phrase, double offsetMs, double estimatedLengthMs, RenderPartRequest request)[] tuples) {
            double playbackStartMs = project.timeAxis.TickPosToMsPos(startTick);
            return tuples
                .Select((tuple, index) => (tuple, index))
                .OrderBy(item => RenderPriority.PlaybackBucket(
                    item.tuple.offsetMs, item.tuple.offsetMs + item.tuple.estimatedLengthMs, playbackStartMs))
                .ThenBy(item => RenderPriority.PlaybackDistance(
                    item.tuple.offsetMs, item.tuple.offsetMs + item.tuple.estimatedLengthMs, playbackStartMs))
                .ThenBy(item => item.index)
                .Select(item => item.tuple)
                .ToArray();
        }

        private (RenderPhrase phrase, double offsetMs, double estimatedLengthMs, RenderPartRequest request)[] OrderForPreRender(
            (RenderPhrase phrase, double offsetMs, double estimatedLengthMs, RenderPartRequest request)[] tuples) {
            return tuples
                .Select((tuple, index) => (tuple, index))
                .OrderBy(item => PreRenderAttentionBucket(item.tuple))
                .ThenBy(item => PreRenderAttentionDistance(item.tuple.phrase))
                .ThenBy(item => item.index)
                .Select(item => item.tuple)
                .ToArray();
        }

        private int PreRenderAttentionBucket(
            (RenderPhrase phrase, double offsetMs, double estimatedLengthMs, RenderPartRequest request) tuple) {
            bool isPriorityPart = focusPart != null && ReferenceEquals(tuple.request.part, focusPart);
            bool overlapsPriority = focusTick >= 0 &&
                tuple.phrase.position <= focusTick &&
                tuple.phrase.end > focusTick;
            bool isAfterPriorityStart = focusTick < 0 || tuple.phrase.end > focusTick;
            return RenderPriority.PreRenderBucket(
                isPriorityPart,
                overlapsPriority,
                isAfterPriorityStart);
        }

        private int PreRenderAttentionDistance(RenderPhrase phrase) {
            return focusTick >= 0
                ? RenderPriority.PreRenderDistance(phrase.position, phrase.end, focusTick)
                : 0;
        }

        public static void ReleaseSourceTemp() {
            VoicebankFiles.Inst.ReleaseSourceTemp();
        }

        class ActiveMorphTrack {
            public string Abbr;
            public string FlagBase;
            public string TargetColor;
            public string Flag;
            public float[] RawCurve;
            public Func<float, float> WeightFunc;
        }
    }
}