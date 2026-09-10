using System;
using System.Collections.Generic;
using System.Linq;
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

        // Coalesced dispatch: at most one UI post is in flight; pings that
        // arrive while it is running only update the pending values.
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

        // Under lock.
        private void StartPending() {
            pending = new Task(Dispatch);
            // MainScheduler is null only in test hosts without a UI thread.
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
                // A newer update piled up while dispatching: this task's work is
                // done, hand the slot to a follow-up. The restart decision lives
                // here — inside the task — so no update can be lost in the
                // window between task completion and the next notify.
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
        // Parallel to phrases: the phrase placement in the 44.1 kHz transport domain.
        public double[] phraseOffsetMs;
        public double[] phraseEstimatedLengthMs;
        // Phrases finished in the current render pass. The request is freshly created
        // per pass, so plain per-pass state is safe.
        public int completedPhrases = 0;
    }

    class RenderEngine {
        readonly UProject project;
        readonly int startTick;
        readonly int endTick;
        readonly int trackNo;
        readonly UVoicePart focusPart;
        readonly int focusTick;

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, float[]> XsyBlendCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, float[]>();

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

        /// <summary>
        /// For playback or export. The track tree is built over <paramref name="planner"/>'s
        /// per-track slot sources and the render pass publishes each finished phrase into
        /// the planner. Export passes its own throwaway planner so a long export does not
        /// clobber the live playback session.
        /// </summary>
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
            // Each track is wrapped with its own UMixFx (no global FX bus).
            // Tracks with MixFx == null or Enabled = false pass through unchanged
            // (zero-overhead bypass).  All tracks sum into a single mix.
            var trackOutputs = new List<ISignalSource>();
            var requests = PrepareRequests()
                .Where(request => request.phrases.Length > 0
                    && request.phraseOffsetMs.Zip(request.phraseEstimatedLengthMs, (o, l) => o + l).Max() > startMs
                    && (double.IsPositiveInfinity(endMs) || request.phraseOffsetMs.Min() < endMs))
                .ToArray();
            // Session: specs for every voice phrase plus every loaded wave part.
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
                // Publish this track's wave parts on the setup thread, before
                // playback can read the slots.
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

                ISignalSource trackOut = applyMixFx
                    ? MixFxSource.WrapWith(fader, track.MixFx)
                    : (ISignalSource)fader;
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
            // disabled tracks zero-cost.
            var resultMix = new WaveMix(trackOutputs);
            return Tuple.Create(resultMix, faders);
        }

        // for export
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

        // for pre render
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
            // Wait for each part's latest phrase build, outside the project
            // lock, so the pass renders the newest phrases.
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
            // Only full-project passes (pre-render / export) maintain the real-curve coverage
            // invariant. Partial playback passes must not trim curves outside their tick window.
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
                bool useXsy = phrase.xsy != null && phrase.xsy.Any(x => x > 0);
                if (!useXsy) {
                    var task = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true, renderEvents);
                    task.Wait();
                    if (cancellation.IsCancellationRequested) {
                        break;
                    }
                    planner.RegisterPcm(request.part, phrase.hash, tuple.offsetMs, tuple.estimatedLengthMs, 1, task.Result.samples);
                } else {
                    string xsyKey = $"{phrase.hash:x16}|" +
                        string.Join(",", phrase.phones.Select(p => $"{p.oto2?.Set}:{p.oto2?.Alias}"));
                    if (!XsyBlendCache.TryGetValue(xsyKey, out var blended)) {
                        var taskA = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true, renderEvents);
                        taskA.Wait();
                        if (cancellation.IsCancellationRequested) {
                            break;
                        }
                        float[] samplesA = taskA.Result.samples;
                        // The secondary render runs on a separate phrase with oto2
                        // substituted, so the live phrase is never mutated.
                        var variant = RenderPhrase.BuildXsyVariant(phrase);
                        var taskB = phrase.renderer.Render(variant, progress, request.trackNo, cancellation, true);
                        taskB.Wait();
                        if (cancellation.IsCancellationRequested) {
                            break;
                        }
                        float[] samplesB = taskB.Result.samples;

                        const int fftSize = 2048;
                        const int hopSize = 512;
                        int totalSamples = Math.Max(samplesA.Length, samplesB.Length);
                        int frameCount = Math.Max(1, (totalSamples - fftSize) / hopSize + 1);
                        float[] frameRatios = new float[frameCount];
                        int pitchStart = phrase.position - phrase.leading;
                        for (int f = 0; f < frameCount; f++) {
                            double timeMs = phrase.positionMs - phrase.leadingMs
                                + (double)(f * hopSize) / 44100.0 * 1000.0;
                            double tick = project.timeAxis.MsPosToTickPos(timeMs);
                            int curveIndex = (int)Math.Max(0, (tick - pitchStart) / 5);
                            if (phrase.xsy.Length > 0) {
                                frameRatios[f] = curveIndex < phrase.xsy.Length
                                    ? Math.Clamp(phrase.xsy[curveIndex] / 100f, 0f, 1f)
                                    : Math.Clamp(phrase.xsy.Last() / 100f, 0f, 1f);
                            }
                        }
                        blended = CrossSynthDSP.StftBlend(samplesA, samplesB, frameRatios);
                        if (XsyBlendCache.Count > 1024) {
                            XsyBlendCache.Clear();
                        }
                        XsyBlendCache[xsyKey] = blended;
                    }
                    planner.RegisterPcm(request.part, phrase.hash, tuple.offsetMs, tuple.estimatedLengthMs, 1, blended);
                }
                // Progressive waveform: coalesced to a ~10 Hz repaint rate.
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
            // Immediate final refresh once the pass is done.
            DocManager.Inst.ExecuteCmd(new WaveformReadyNotification());
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
    }
}
