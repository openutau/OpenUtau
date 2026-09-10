using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Render {
    /// <summary>
    /// The UI-thread read seam for render-derived data: one immutable
    /// <see cref="RenderProjection"/> per part, built from the document's
    /// current phrases and the planner's content-keyed pcm store. Projections
    /// are cached until invalidated, and observers are delivered coalesced to a
    /// repaint rate instead of one notification per phrase.
    ///
    /// UI thread only: projections read the document. Render-pass and playback
    /// invalidations are marshalled here through the command path.
    /// </summary>
    public sealed class RenderView : SingletonBase<RenderView> {
        const int MinIntervalMs = 16; // at most 60 Hz delivery

        private readonly object lockObj = new object();
        private readonly Dictionary<UPart, RenderProjection> projections =
            new Dictionary<UPart, RenderProjection>();
        private readonly List<Action<RenderProjection>> observers =
            new List<Action<RenderProjection>>();
        private MixPlanner plannerForTest;
        private Task pump = null;
        private bool dirty;
        // -MinIntervalMs so the first delivery is immediate, not delayed.
        private int lastDeliver = -MinIntervalMs;
        private long revision;

        private MixPlanner Planner => plannerForTest ?? PlaybackManager.Inst.MixPlanner;

        internal void SetPlannerForTest(MixPlanner planner) {
            plannerForTest = planner;
        }

        /// <summary>
        /// The part's current projection, cached until the next invalidation.
        /// </summary>
        public RenderProjection Current(UPart part) {
            ThreadGuard.AssertUi();
            if (!projections.TryGetValue(part, out var projection)) {
                projection = Build(part);
                projections[part] = projection;
            }
            return projection;
        }

        /// <summary>
        /// Delivers each part's fresh projection after invalidations, coalesced
        /// to at most one delivery per interval, on the UI thread.
        /// </summary>
        public IDisposable Observe(Action<RenderProjection> onChange) {
            ThreadGuard.AssertUi();
            lock (lockObj) {
                observers.Add(onChange);
            }
            return new Unsubscriber(this, onChange);
        }

        /// <summary>
        /// Drops every cached projection. Project load replaces the part objects
        /// wholesale, so the old projections would leak.
        /// </summary>
        public void ForgetAll() {
            ThreadGuard.AssertUi();
            projections.Clear();
        }

        /// <summary>
        /// Marks every cached projection stale and schedules a coalesced
        /// delivery.
        /// </summary>
        public void InvalidateAll() {
            ThreadGuard.AssertUi();
            Task start = null;
            lock (lockObj) {
                dirty = true;
                if (pump == null || pump.IsCompleted) {
                    pump = new Task(Pump);
                    start = pump;
                }
            }
            start?.Start(TaskScheduler.Default);
        }

        // ==================== internals ====================

        private RenderProjection Build(UPart part) {
            if (part is UWavePart) {
                // Wave parts carry no phrases; the whole file is one placement
                // under hash 0 whose geometry comes from the store.
                if (Planner.TryGetPhrasePcm(part, 0, out var wave)) {
                    var view = new PhraseView(0,
                        new PhraseLayout(wave.posMs, wave.posMs + wave.durMs, 0, wave.durMs), true);
                    return new RenderProjection(part, ++revision, new[] { view }, true);
                }
                return new RenderProjection(part, ++revision, Array.Empty<PhraseView>(), false);
            }
            var phrases = ((UVoicePart)part).renderPhrases;
            var views = new PhraseView[phrases.Count];
            bool ready = phrases.Count > 0;
            for (int i = 0; i < phrases.Count; ++i) {
                var phrase = phrases[i];
                bool rendered = Planner.TryGetPhrasePcm(part, phrase.hash, out _);
                if (!rendered) {
                    ready = false;
                }
                views[i] = new PhraseView(phrase.hash, phrase.Layout, rendered);
            }
            return new RenderProjection(part, ++revision, views, ready);
        }

        private void Pump() {
            while (true) {
                lock (lockObj) {
                    var deadline = Math.Max(lastDeliver + MinIntervalMs, Environment.TickCount);
                    while (Environment.TickCount < deadline) {
                        Monitor.Wait(lockObj, 10);
                    }
                    if (!dirty) {
                        // Nothing was invalidated in time: the pump retires.
                        pump = null;
                        return;
                    }
                    dirty = false;
                    lastDeliver = Environment.TickCount;
                }
                Deliver();
            }
        }

        // Deliver reads the document: it must run on the UI thread. Test hosts
        // without a scheduler run it inline.
        private void Deliver() {
            var scheduler = DocManager.Inst.MainScheduler;
            if (scheduler != null && TaskScheduler.Current != scheduler) {
                Task.Factory.StartNew(
                    _ => DoDeliver(), null, CancellationToken.None, TaskCreationOptions.None, scheduler);
            } else {
                DoDeliver();
            }
        }

        private void DoDeliver() {
            var changed = new List<RenderProjection>();
            foreach (var part in projections.Keys.ToList()) {
                var projection = Build(part);
                projections[part] = projection;
                changed.Add(projection);
            }
            List<Action<RenderProjection>> snapshot;
            lock (lockObj) {
                snapshot = observers.ToList();
            }
            for (int p = 0; p < changed.Count; ++p) {
                for (int o = 0; o < snapshot.Count; ++o) {
                    snapshot[o](changed[p]);
                }
            }
            // An invalidation that landed while delivering just sets dirty under
            // the lock; the pump loop's next iteration picks it up.
        }

        private sealed class Unsubscriber : IDisposable {
            private readonly RenderView view;
            private readonly Action<RenderProjection> onChange;

            public Unsubscriber(RenderView view, Action<RenderProjection> onChange) {
                this.view = view;
                this.onChange = onChange;
            }

            public void Dispose() {
                lock (view.lockObj) {
                    view.observers.Remove(onChange);
                }
            }
        }
    }
}
