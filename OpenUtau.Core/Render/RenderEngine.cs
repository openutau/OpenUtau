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
            var notif = new ProgressBarNotification(progress, info);
            var task = new Task(() => DocManager.Inst.ExecuteCmd(notif));
            task.Start(DocManager.Inst.MainScheduler);
        }
    }

    class RenderPartRequest {
        public UVoicePart part;
        public long timestamp;
        public int trackNo;
        public RenderPhrase[] phrases;
        public WaveSource[] sources;
        public WaveMix mix;
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

        // for playback or export
        public Tuple<WaveMix, List<Fader>> RenderMixdown(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation, bool wait = false) {
            return RenderMixdown(uiScheduler, ref cancellation, wait, applyMixFx: true);
        }

        // for playback or export -- explicit MixFx control (export dialog passes false to keep dry stems)
        public Tuple<WaveMix, List<Fader>> RenderMixdown(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation, bool wait, bool applyMixFx) {
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
                .Where(request => request.sources.Length > 0 && request.sources.Max(s => s.EndMs) > startMs && (double.IsPositiveInfinity(endMs) || request.sources.Min(s => s.offsetMs) < endMs))
                .ToArray();
            for (int i = 0; i < project.tracks.Count; ++i) {
                if (trackNo != -1 && trackNo != i) {
                    continue;
                }
                var track = project.tracks[i];
                var trackRequests = requests
                    .Where(req => req.trackNo == i)
                    .ToArray();
                var trackSources = trackRequests.Select(req => req.mix)
                    .OfType<ISignalSource>()
                    .ToList();
                trackSources.AddRange(project.parts
                    .Where(part => part is UWavePart && part.trackNo == i)
                    .Select(part => part as UWavePart)
                    .Where(part => part.Samples != null)
                    .Select(part => part.TrimSamples(project)));
                var trackMix = new WaveMix(trackSources);
                var fader = new Fader(trackMix);
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
                RenderRequests(requests, newCancellation, playing: !wait);
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

        // for playback
        public Tuple<MasterAdapter, List<Fader>> RenderProject(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation) {
            double startMs = project.timeAxis.TickPosToMsPos(startTick);
            double endMs = endTick == -1
                ? double.PositiveInfinity
                : project.timeAxis.TickPosToMsPos(endTick);
            var renderMixdownResult = RenderMixdown(uiScheduler, ref cancellation, wait: false);
            var master = new MasterAdapter(renderMixdownResult.Item1, endMs, PlaybackManager.Inst.metronomeGenerator);
            master.SetPosition((int)(startMs * 44100 / 1000) * 2);
            return Tuple.Create(master, renderMixdownResult.Item2);
        }

        // for export
        public List<WaveMix> RenderTracks(TaskScheduler uiScheduler, ref CancellationTokenSource cancellation) {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref cancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            var trackMixes = new List<WaveMix>();
            var requests = PrepareRequests();
            if (requests.Length == 0) {
                return trackMixes;
            }
            Enumerable.Range(0, requests.Max(req => req.trackNo) + 1)
                .Select(trackNo => requests.Where(req => req.trackNo == trackNo).ToArray())
                .ToList()
                .ForEach(trackRequests => {
                    if (trackRequests.Length == 0) {
                        trackMixes.Add(null);
                    } else {
                        RenderRequests(trackRequests, newCancellation);
                        var mix = new WaveMix(trackRequests.Select(req => req.mix).ToArray());
                        trackMixes.Add(mix);
                    }
                });
            return trackMixes;
        }

        // for pre render
        public void PreRenderProject(ref CancellationTokenSource cancellation) {
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
                    RenderRequests(PrepareRequests(), newCancellation);
                } catch (Exception e) {
                    if (!newCancellation.IsCancellationRequested) {
                        Log.Error(e, "Failed to pre-render.");
                    }
                }
            });
        }

        private RenderPartRequest[] PrepareRequests() {
            RenderPartRequest[] requests;
            SingerManager.Inst.ReleaseSingersNotInUse(project);
            lock (project) {
                requests = project.parts
                    .Where(part => part is UVoicePart && (trackNo == -1 || part.trackNo == trackNo))
                    .Where(part => !Preferences.Default.SkipRenderingMutedTracks || !project.tracks[part.trackNo].Muted)
                    .Select(part => part as UVoicePart)
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
                request.sources = new WaveSource[request.phrases.Length];
                for (var i = 0; i < request.phrases.Length; i++) {
                    var phrase = request.phrases[i];
                    var firstPhone = phrase.phones.First();
                    var lastPhone = phrase.phones.Last();
                    var layout = phrase.renderer.Layout(phrase);
                    double posMs = layout.positionMs - layout.leadingMs;
                    double durMs = layout.estimatedLengthMs;
                    request.sources[i] = new WaveSource(posMs, durMs, 0, 1);
                }
                request.mix = new WaveMix(request.sources);
            }
            return requests;
        }

        private void RenderRequests(
            RenderPartRequest[] requests,
            CancellationTokenSource cancellation,
            bool playing = false) {
            if (requests.Length == 0 || cancellation.IsCancellationRequested) {
                return;
            }
            var tuples = requests
                .SelectMany(req => req.phrases
                    .Zip(req.sources, (phrase, source) => (phrase, source, request: req)))
                .ToArray();
            if (tuples.Length == 0) {
                return;
            }
            if (playing) {
                tuples = OrderForPlayback(tuples);
            } else if (focusPart != null || focusTick >= 0) {
                tuples = OrderForPreRender(tuples);
            }
            var progress = new Progress(tuples.Sum(t => t.Item1.phones.Length));
            // Only full-project passes (pre-render / export) maintain the real-curve coverage
            // invariant. Partial playback passes must not trim curves outside their tick window.
            bool maintainCoverage = startTick == 0 && endTick == -1;
            var coverageRanges = maintainCoverage
                ? new Dictionary<UVoicePart, List<(int start, int end)>>()
                : null;
            foreach (var tuple in tuples) {
                if (cancellation.IsCancellationRequested) {
                    break;
                }
                var phrase = tuple.phrase;
                var source = tuple.source;
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
                    source.SetSamples(task.Result.samples);
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

                        var otoField = typeof(RenderPhone).GetField("oto");
                        var hashField = typeof(RenderPhone).GetField("hash");
                        var phraseHashField = typeof(RenderPhrase).GetField("hash");
                        var originalOtos = phrase.phones.Select(p => p.oto).ToArray();
                        var originalHashes = phrase.phones.Select(p => p.hash).ToArray();
                        ulong originalPhraseHash = phrase.hash;
                        float[] samplesB;
                        try {
                            for (int i = 0; i < phrase.phones.Length; i++) {
                                var phone = phrase.phones[i];
                                if (phone.oto2 != null) {
                                    otoField.SetValue(phone, phone.oto2);
                                    hashField.SetValue(phone, phone.hash ^ 0x5858585858585858);
                                }
                            }
                            phraseHashField.SetValue(phrase, phrase.hash ^ 0x5858585858585858);
                            var taskB = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true);
                            taskB.Wait();
                            samplesB = taskB.Result.samples;
                        } finally {
                            for (int i = 0; i < phrase.phones.Length; i++) {
                                otoField.SetValue(phrase.phones[i], originalOtos[i]);
                                hashField.SetValue(phrase.phones[i], originalHashes[i]);
                            }
                            phraseHashField.SetValue(phrase, originalPhraseHash);
                        }
                        if (cancellation.IsCancellationRequested) {
                            break;
                        }

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
                    source.SetSamples(blended);
                }
                if (publishedUpdates == null) {
                    publishedUpdates = PublishRealCurveUpdates(request.part, phrase);
                }
                if (coverageRanges != null && publishedUpdates != null) {
                    AccumulateCoverage(coverageRanges, request.part, publishedUpdates);
                }
                if (request.sources.All(s => s.HasSamples)) {
                    request.part.SetMix(request.mix);
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

        private (RenderPhrase phrase, WaveSource source, RenderPartRequest request)[] OrderForPlayback(
            (RenderPhrase phrase, WaveSource source, RenderPartRequest request)[] tuples) {
            double playbackStartMs = project.timeAxis.TickPosToMsPos(startTick);
            return tuples
                .Select((tuple, index) => (tuple, index))
                .OrderBy(item => RenderPriority.PlaybackBucket(
                    item.tuple.source.offsetMs, item.tuple.source.EndMs, playbackStartMs))
                .ThenBy(item => RenderPriority.PlaybackDistance(
                    item.tuple.source.offsetMs, item.tuple.source.EndMs, playbackStartMs))
                .ThenBy(item => item.index)
                .Select(item => item.tuple)
                .ToArray();
        }

        private (RenderPhrase phrase, WaveSource source, RenderPartRequest request)[] OrderForPreRender(
            (RenderPhrase phrase, WaveSource source, RenderPartRequest request)[] tuples) {
            return tuples
                .Select((tuple, index) => (tuple, index))
                .OrderBy(item => PreRenderAttentionBucket(item.tuple))
                .ThenBy(item => PreRenderAttentionDistance(item.tuple.phrase))
                .ThenBy(item => item.index)
                .Select(item => item.tuple)
                .ToArray();
        }

        private int PreRenderAttentionBucket(
            (RenderPhrase phrase, WaveSource source, RenderPartRequest request) tuple) {
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
