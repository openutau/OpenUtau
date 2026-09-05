using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.DiffSinger {
    internal static class DiffSingerRealCurveScheduler {
        // Coalesce drag-generated curve commands without adding pointer-level preview logic.
        const int DebounceMs = 200;
        static readonly object lockObj = new object();
        static readonly Dictionary<UVoicePart, CancellationTokenSource> pending = new Dictionary<UVoicePart, CancellationTokenSource>();

        public static void TrySchedule(UProject project, UVoicePart part, UCommand command) {
            if (command is not ExpCommand expCommand ||
                !IsCurveEditCommand(command) ||
                string.IsNullOrEmpty(expCommand.Key) ||
                expCommand.Part != part ||
                !CanRefresh(project, part, expCommand.Key)) {
                return;
            }
            Schedule(project, part);
        }

        static bool IsCurveEditCommand(UCommand command) {
            return command is SetCurveCommand ||
                command is MergedSetCurveCommand ||
                command is PasteCurveCommand ||
                command is ClearCurveCommand;
        }

        static bool CanRefresh(UProject project, UVoicePart part, string abbr) {
            if (!DiffSingerRenderer.ShouldRefreshRealCurvesOnCurveEdit(abbr) ||
                !project.parts.Contains(part) ||
                part.trackNo < 0 ||
                part.trackNo >= project.tracks.Count) {
                return false;
            }
            return project.tracks[part.trackNo].RendererSettings.Renderer is DiffSingerRenderer;
        }

        static void Schedule(UProject project, UVoicePart part) {
            var cancellation = new CancellationTokenSource();
            lock (lockObj) {
                if (pending.TryGetValue(part, out var previous)) {
                    previous.Cancel();
                }
                pending[part] = cancellation;
            }
            _ = Task.Run(() => RefreshAsync(project, part, cancellation));
        }

        static async Task RefreshAsync(UProject project, UVoicePart part, CancellationTokenSource cancellation) {
            try {
                await Task.Delay(DebounceMs, cancellation.Token);
                var updates = LoadPartUpdates(project, part, cancellation.Token);
                if (updates.Count > 0 && !cancellation.IsCancellationRequested) {
                    DocManager.Inst.ExecuteCmd(new RealCurvesUpdatedNotification(part, updates));
                }
            } catch (OperationCanceledException) {
            } catch (Exception e) {
                Log.Debug(e, "Failed to refresh DiffSinger real curves after curve edit.");
            } finally {
                lock (lockObj) {
                    if (pending.TryGetValue(part, out var current) && current == cancellation) {
                        pending.Remove(part);
                    }
                }
                cancellation.Dispose();
            }
        }

        static IReadOnlyList<RealCurveUpdate> LoadPartUpdates(
            UProject project,
            UVoicePart part,
            CancellationToken cancellationToken) {
            RenderPhrase[] phrases;
            lock (project) {
                if (!project.parts.Contains(part) ||
                    part.trackNo < 0 ||
                    part.trackNo >= project.tracks.Count ||
                    project.tracks[part.trackNo].RendererSettings.Renderer is not DiffSingerRenderer) {
                    return Array.Empty<RealCurveUpdate>();
                }
                phrases = part.renderPhrases.ToArray();
            }
            if (phrases.Length == 0) {
                return Array.Empty<RealCurveUpdate>();
            }
            var updates = new List<RealCurveUpdate>();
            foreach (var phrase in phrases) {
                if (cancellationToken.IsCancellationRequested) {
                    return Array.Empty<RealCurveUpdate>();
                }
                updates.AddRange(RealCurveUpdater.LoadPhraseUpdates(part, phrase));
            }
            return updates;
        }
    }
}
