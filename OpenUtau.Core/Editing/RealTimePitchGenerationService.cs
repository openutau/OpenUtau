using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Editing {
    /// <summary>
    /// When enabled, regenerates DiffSinger pitch curves (Ctrl+R) after relevant piano roll edits.
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
        readonly LoadRenderedPitch pitchLoader = new();

        readonly struct RealtimePitchSettings {
            public double PitchSteps { get; init; }
            public int DebounceMs { get; init; }
            public int LyricFallbackMs { get; init; }
            public int AfterPhonemizeMs { get; init; }
            public bool FastRealtime { get; init; }
        }

        static LivePitchMode ActiveMode =>
            (LivePitchMode)Preferences.Default.RealTimePitchMode;

        static bool IsEnabled => ActiveMode != LivePitchMode.Off;

        static RealtimePitchSettings GetSettings() => ActiveMode switch {
            LivePitchMode.Normal => new RealtimePitchSettings {
                PitchSteps = 2,
                DebounceMs = 200,
                LyricFallbackMs = 1200,
                AfterPhonemizeMs = 50,
                FastRealtime = false,
            },
            LivePitchMode.SuperFast => new RealtimePitchSettings {
                PitchSteps = 0.1,
                DebounceMs = 80,
                LyricFallbackMs = 800,
                AfterPhonemizeMs = 30,
                FastRealtime = true,
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
            var settings = GetSettings();
            if (cmd is ChangeNoteLyricCommand lyricCmd) {
                lock (scheduleLock) {
                    lyricPendingParts.Add(lyricCmd.Part);
                    TrackAffectedNotes(lyricCmd.Part, lyricCmd.Notes);
                }
                SchedulePart(lyricCmd.Part, settings.LyricFallbackMs);
                return;
            }
            if (cmd is PhonemizedNotification phonemized) {
                bool schedule;
                lock (scheduleLock) {
                    schedule = lyricPendingParts.Remove(phonemized.part);
                }
                if (schedule) {
                    SchedulePart(phonemized.part, settings.AfterPhonemizeMs);
                }
                return;
            }
            if (cmd is NoteCommand noteCmd && TriggerCommandTypes.Contains(cmd.GetType())) {
                lock (scheduleLock) {
                    TrackAffectedNotes(noteCmd.Part, noteCmd.Notes);
                }
                SchedulePart(noteCmd.Part, settings.DebounceMs);
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
            if (!IsDiffSingerPart(part)) {
                return;
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
                }
                RunForPart(part, token);
            });
        }

        void RunForPart(UVoicePart part, CancellationToken cancellationToken) {
            if (cancellationToken.IsCancellationRequested || !IsEnabled) {
                return;
            }
            var settings = GetSettings();
            var project = DocManager.Inst.Project;
            if (!project.parts.Contains(part) || !IsDiffSingerPart(part)) {
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
                    settings.PitchSteps,
                    settings.FastRealtime);
            } catch (Exception e) {
                Log.Warning(e, "Real-time pitch generation failed.");
            }
        }

        static bool IsDiffSingerPart(UVoicePart part) {
            if (part == null || part.trackNo < 0 || part.trackNo >= DocManager.Inst.Project.tracks.Count) {
                return false;
            }
            var renderer = DocManager.Inst.Project.tracks[part.trackNo].RendererSettings.Renderer;
            return renderer != null
                && renderer.SupportsRenderPitch
                && renderer.SingerType == USingerType.DiffSinger;
        }
    }
}
