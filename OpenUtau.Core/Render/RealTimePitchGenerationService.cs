using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Render {
    /// <summary>
    /// When enabled, regenerates pitch curves (Ctrl+R) after relevant piano roll edits,
    /// for renderers whose <see cref="IRenderer.LivePitchCost"/> is not Unsupported.
    /// </summary>
    public sealed class RealTimePitchGenerationService : ICmdSubscriber {
        public static RealTimePitchGenerationService Inst { get; } = new();

        internal static bool SuppressCallbacks;

        static readonly HashSet<Type> TriggerCommandTypes = new HashSet<Type> {
            typeof(AddNoteCommand),
            typeof(RemoveNoteCommand),
            typeof(ResizeNoteCommand),
            typeof(MoveNoteCommand),
            typeof(PhonemeOffsetCommand),
            typeof(PhonemePreutterCommand),
            typeof(PhonemeOverlapCommand),
            typeof(ClearPhonemeTimingCommand),
            typeof(ChangePhonemeAliasCommand),
        };

        readonly object scheduleLock = new();
        readonly Dictionary<UVoicePart, CancellationTokenSource> debounceTokens = new();
        readonly Dictionary<UVoicePart, HashSet<UNote>> pendingNotesByPart = new();
        readonly HashSet<UVoicePart> lyricPendingParts = new();
        // Heavy renderers: parts currently generating, and parts edited meanwhile that need one more run.
        readonly HashSet<UVoicePart> runningParts = new();
        readonly HashSet<UVoicePart> rerunParts = new();
        readonly LoadRenderedPitch pitchLoader = new();

        /// <summary>Delay multiplier for <see cref="LivePitchCost.Heavy"/> renderers.</summary>
        const int HeavyDelayScale = 4;

        readonly struct RealtimePitchSettings {
            public PitchGenerationOptions Options { get; init; }
            public int DebounceMs { get; init; }
            public int LyricFallbackMs { get; init; }
            public int AfterPhonemizeMs { get; init; }
        }

        static LivePitchMode ActiveMode =>
            (LivePitchMode)Preferences.Default.RealTimePitchMode;

        static bool IsEnabled => ActiveMode != LivePitchMode.Off;

        /// <summary>
        /// Settings for the part's renderer. Fast mode runs as Normal when the renderer
        /// does not support it.
        /// </summary>
        static RealtimePitchSettings GetSettings(UVoicePart part) {
            var mode = ActiveMode;
            if (mode == LivePitchMode.Fast && GetRenderer(part)?.SupportsFastLivePitch != true) {
                mode = LivePitchMode.Normal;
            }
            return GetSettings(mode);
        }

        static RealtimePitchSettings GetSettings(LivePitchMode mode) => mode switch {
            LivePitchMode.Normal => new RealtimePitchSettings {
                Options = new PitchGenerationOptions(Steps: 2, FastRealtime: false),
                DebounceMs = 200,
                LyricFallbackMs = 1200,
                AfterPhonemizeMs = 50,
            },
            LivePitchMode.Fast => new RealtimePitchSettings {
                Options = new PitchGenerationOptions(Steps: 0.1, FastRealtime: true),
                DebounceMs = 80,
                LyricFallbackMs = 800,
                AfterPhonemizeMs = 30,
            },
            _ => default,
        };

        RealTimePitchGenerationService() { }

        public void Initialize() {
            DocManager.Inst.AddSubscriber(this);
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (isUndo || SuppressCallbacks || !IsEnabled) {
                return;
            }
            if (cmd is ChangeNoteLyricCommand lyricCmd) {
                lock (scheduleLock) {
                    lyricPendingParts.Add(lyricCmd.Part);
                    TrackAffectedNotes(lyricCmd.Part, lyricCmd.Notes);
                }
                SchedulePart(lyricCmd.Part, GetSettings(lyricCmd.Part).LyricFallbackMs);
                return;
            }
            if (cmd is PhonemizedNotification phonemized) {
                bool schedule;
                lock (scheduleLock) {
                    schedule = lyricPendingParts.Remove(phonemized.part);
                }
                if (schedule) {
                    SchedulePart(phonemized.part, GetSettings(phonemized.part).AfterPhonemizeMs);
                }
                return;
            }
            if (cmd is NoteCommand noteCmd && TriggerCommandTypes.Contains(cmd.GetType())) {
                lock (scheduleLock) {
                    TrackAffectedNotes(noteCmd.Part, noteCmd.Notes);
                }
                SchedulePart(noteCmd.Part, GetSettings(noteCmd.Part).DebounceMs);
            }
        }

        void TrackAffectedNotes(UVoicePart part, UNote[] notes) {
            if (notes == null || notes.Length == 0) {
                return;
            }
            if (!pendingNotesByPart.TryGetValue(part, out var set)) {
                set = new HashSet<UNote>();
                pendingNotesByPart[part] = set;
            }
            foreach (var note in notes) {
                set.Add(note);
            }
        }

        void SchedulePart(UVoicePart part, int delayMs) {
            var cost = GetLivePitchCost(part);
            if (cost == LivePitchCost.Unsupported) {
                return;
            }
            if (cost == LivePitchCost.Heavy) {
                delayMs *= HeavyDelayScale;
            }
            CancellationTokenSource cts;
            lock (scheduleLock) {
                if (debounceTokens.TryGetValue(part, out var existing)) {
                    existing.Cancel();
                    existing.Dispose();
                }
                cts = new CancellationTokenSource();
                debounceTokens[part] = cts;
            }
            var token = cts.Token;
            _ = Task.Run(async () => {
                try {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                } catch (TaskCanceledException) {
                    return;
                }
                if (token.IsCancellationRequested) {
                    return;
                }
                lock (scheduleLock) {
                    if (debounceTokens.TryGetValue(part, out var current) && current == cts) {
                        debounceTokens.Remove(part);
                    }
                    if (cost == LivePitchCost.Heavy && !runningParts.Add(part)) {
                        // Still generating: keep the pending notes and run once more when it finishes.
                        rerunParts.Add(part);
                        return;
                    }
                }
                try {
                    RunForPart(part, token);
                } finally {
                    if (cost == LivePitchCost.Heavy) {
                        bool rerun;
                        lock (scheduleLock) {
                            runningParts.Remove(part);
                            rerun = rerunParts.Remove(part);
                        }
                        if (rerun) {
                            SchedulePart(part, 0);
                        }
                    }
                }
            });
        }

        void RunForPart(UVoicePart part, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested || !IsEnabled) {
                return;
            }
            var settings = GetSettings(part);
            var project = DocManager.Inst.Project;
            if (!project.parts.Contains(part) || GetLivePitchCost(part) == LivePitchCost.Unsupported) {
                return;
            }
            List<UNote> affectedNotes;
            lock (scheduleLock) {
                if (!pendingNotesByPart.TryGetValue(part, out var set) || set.Count == 0) {
                    return;
                }
                affectedNotes = set.ToList();
                pendingNotesByPart.Remove(part);
            }
            affectedNotes = affectedNotes
                .Where(n => n != null)
                .Distinct()
                .ToList();
            if (affectedNotes.Count == 0) {
                return;
            }
            try {
                pitchLoader.RunLive(
                    project, part, affectedNotes,
                    DocManager.Inst,
                    cancellationToken,
                    settings.Options);
            } catch (Exception e) {
                Log.Warning(e, "Real-time pitch generation failed.");
            }
        }

        static IRenderer? GetRenderer(UVoicePart part) {
            if (part == null || part.trackNo < 0 || part.trackNo >= DocManager.Inst.Project.tracks.Count) {
                return null;
            }
            return DocManager.Inst.Project.tracks[part.trackNo].RendererSettings.Renderer;
        }

        static LivePitchCost GetLivePitchCost(UVoicePart part) {
            var renderer = GetRenderer(part);
            if (renderer == null || !renderer.SupportsRenderPitch) {
                return LivePitchCost.Unsupported;
            }
            return renderer.LivePitchCost;
        }
    }
}
