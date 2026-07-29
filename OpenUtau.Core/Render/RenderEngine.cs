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

        public RenderEngine(UProject project, int startTick = 0, int endTick = -1, int trackNo = -1) {
            this.project = project;
            this.startTick = startTick;
            this.endTick = endTick;
            this.trackNo = trackNo;
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
            var renderMixdownResult = RenderMixdown(uiScheduler, ref cancellation, wait: false);
            var master = new MasterAdapter(renderMixdownResult.Item1);
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
                    .Zip(req.sources, (phrase, source) => Tuple.Create(phrase, source, req)))
                .ToArray();
            if (playing) {
                var orderedTuples = tuples
                    .Where(tuple => tuple.Item1.end > startTick)
                    .OrderBy(tuple => tuple.Item1.end)
                    .Concat(tuples.Where(tuple => tuple.Item1.end <= startTick))
                    .ToArray();
                tuples = orderedTuples;
            }
            var progress = new Progress(tuples.Sum(t => t.Item1.phones.Length));
            foreach (var tuple in tuples) {
                var phrase = tuple.Item1;
                var source = tuple.Item2;
                var request = tuple.Item3;
                var task = phrase.renderer.Render(phrase, progress, request.trackNo, cancellation, true);
                task.Wait();
                if (cancellation.IsCancellationRequested) {
                    break;
                }
                source.SetSamples(task.Result.samples);
                if (request.sources.All(s => s.HasSamples)) {
                    request.part.SetMix(request.mix);
                    DocManager.Inst.ExecuteCmd(new PartRenderedNotification(request.part));
                }
            }
            progress.Clear();
        }

        public static void ReleaseSourceTemp() {
            VoicebankFiles.Inst.ReleaseSourceTemp();
        }
    }
}
