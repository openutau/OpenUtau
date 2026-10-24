using System;
using System.Collections.Generic;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core {
    public class UNotification : UCommand {
        public UProject project;
        public UPart part;
        public override void Execute() { }
        public override void Unexecute() { }
        public override string ToString() => "Notification";
    }

    public class ErrorMessageNotification : UNotification {
        public readonly string message = string.Empty;
        public readonly Exception e;
        public ErrorMessageNotification(Exception e) {
            this.e = e;
        }
        public ErrorMessageNotification(string message) {
            this.message = message;
        }
        public ErrorMessageNotification(string message, Exception e) {
            this.message = message;
            this.e = e;
        }
        public override string ToString() {
            if (e is MessageCustomizableException mce) {
                if (string.IsNullOrWhiteSpace(mce.Message)) {
                    return $"Error message: {mce.SubstanceException.Message} {mce.SubstanceException}";
                } else {
                    return $"Error message: {mce.Message} {mce.SubstanceException}";
                }
            } else {
                return $"Error message: {message} {e}";
            }
        }
    }
    public class ToastNotification : UNotification {
        public readonly string windowType;
        public readonly string message;
        public readonly string translationKey;

        /// <summary>
        /// Not currently in use
        /// </summary>
        public string? title;
        /// <summary>
        /// Not currently in use (The color of the toast might change in the future).
        /// </summary>
        public string type = "Warning";
        /// <summary>
        /// Toast display time. If 0 is specified, it will not close automatically.
        /// </summary>
        public long durationSec = 4;
        public Exception? e;

        /// <summary>
        /// Displays a toast message in the window that does not interfere with the user's operations.
        /// </summary>
        /// <param name="windowType">"MainWindow" or "Pianoroll" (If the specified window is not on top, a toast will always appear in the main window).</param>
        /// <param name="message">This is not displayed in the UI. The actual text is specified by translationKey.</param>
        /// <param name="translationKey">The key for the text that actually appears on the toast.</param>
        /// <param name="e">If there are any associated exceptions, clicking the toast notification will display a standard error dialog.</param>
        public ToastNotification(string windowType, string message, string translationKey, Exception? e = null) {
            this.windowType = windowType;
            this.message = message;
            this.translationKey = translationKey;
            this.e = e;
        }
        public override string ToString() => $"Toast notification: {message}";
    }

    public class LoadingNotification : UNotification {
        public readonly Type window;
        public readonly bool startLoading;
        public readonly string loadObject;
        public LoadingNotification(Type window, bool startLoading, string loadObject) {
            this.window = window;
            this.startLoading = startLoading;
            this.loadObject = loadObject;
        }
        public override string ToString() {
            if (startLoading) {
                return $"Start loading {loadObject}";
            } else {
                return $"Finish loading {loadObject}";
            }
        }
    }

    public class LoadPartNotification : UNotification {
        public readonly int tick;
        public LoadPartNotification(UPart part, UProject project, int tick) {
            this.part = part;
            this.project = project;
            this.tick = tick;
        }
        public override string ToString() => "Load part";
    }

    public class LoadProjectNotification : UNotification {
        public LoadProjectNotification(UProject project) {
            this.project = project;
        }
        public override string ToString() => "Load project";
    }

    public class SaveProjectNotification : UNotification {
        public string Path;
        public SaveProjectNotification(string path) {
            Path = path;
        }
        public override string ToString() => "Save project";
    }

    public class ValidateProjectNotification : UNotification {
        public override string ToString() => "Validate Project";
    }

    public class PhonemizedNotification : UNotification {
        public readonly UVoicePart part;
        public PhonemizedNotification(UVoicePart part) {
            this.part = part;
        }
        public override string ToString() => "Phonemized";
    }

    public class SelectExpressionNotification : UNotification {
        public string ExpKey;
        public bool UpdateShadow;
        public int SelectorIndex;
        public SelectExpressionNotification(string expKey, int index, bool updateShadow) {
            ExpKey = expKey;
            SelectorIndex = index;
            UpdateShadow = updateShadow;
        }
        public override string ToString() => $"Select expression {ExpKey}";
    }

    // Notification for UI to move PlayPosMarker
    public class SetPlayPosTickNotification : UNotification {
        public readonly int playPosTick;
        public readonly bool waitingRendering;
        public readonly bool pause;
        public override bool Silent => true;
        public SetPlayPosTickNotification(int tick, bool waitingRendering = false, bool pause = false) {
            playPosTick = tick;
            this.waitingRendering = waitingRendering;
            this.pause = pause;
        }
        public override string ToString() => $"Set play position to tick {playPosTick}";
    }

    /// <summary>
    /// Notification for both views to sync time range selection.
    /// </summary>
    public class SetRangeSelectionNotification : UNotification {
        public readonly int startTick;
        public readonly int endTick;
        public override bool Silent => true;
        public SetRangeSelectionNotification(int startTick, int endTick) {
            this.startTick = startTick;
            this.endTick = endTick;
        }
        public override string ToString() => $"Set range selection {startTick}-{endTick}";
    }

    // Notification for playback manager to change play position
    public class SeekPlayPosTickNotification : UNotification {
        public int playPosTick;
        public readonly bool pause;
        public override bool Silent => true;
        public SeekPlayPosTickNotification(int tick, bool pause = false) {
            playPosTick = tick;
            this.pause = pause;
        }
        public override string ToString() => $"Seek play position to tick {playPosTick}";
    }

    public class ProgressBarNotification : UNotification {
        public double Progress;
        public string Info;
        public override bool Silent => true;
        public ProgressBarNotification(double progress, string info) {
            Progress = progress;
            Info = info;
        }
        public override string ToString() => $"Set progress {Progress} {Info}";
    }

    // Notification to update the phrase blocks in PartControl, optional RenderPhrase for exact update.
    public class PhraseRenderStateNotification : UNotification {
        public UVoicePart Part { get; }
        public RenderPhrase? Phrase { get; }
        public int StartTick { get; }
        public int EndTick { get; }
        public bool Rendered { get; }
        public override bool Silent => true;

        public PhraseRenderStateNotification(UVoicePart part, int startTick, int endTick, bool rendered) {
            Part = part;
            StartTick = startTick;
            EndTick = endTick;
            Rendered = rendered;
        }

        public PhraseRenderStateNotification(UVoicePart part, RenderPhrase phrase, bool rendered) {
            Part = part;
            Phrase = phrase;
            StartTick = phrase.position;
            EndTick = phrase.end;
            Rendered = rendered;
        }

        public override string ToString() => Rendered
            ? $"Phrase Status | {Part.DisplayName} {StartTick}-{EndTick} Rendered"
            : $"Phrase Status | {Part.DisplayName} {StartTick}-{EndTick} Unrendered ";
    }

    public class RebuildPhraseBlocksNotification : UNotification {
        public UVoicePart Part { get; }

        public RebuildPhraseBlocksNotification(UVoicePart part) {
            Part = part;
        }

        public override string ToString() => $"Rebuild phrase blocks | {Part.DisplayName}";
    }

    /// <summary>
    /// A warning that needs the user's attention. DocManager keeps it until a
    /// <see cref="ClearWarningsNotification"/>; the main window shows a warning sign
    /// next to the main menu while any are kept. A warning replaces an earlier one
    /// with the same key, so a check that runs repeatedly doesn't pile up copies.
    /// Title and detail are string resource keys, formatted with <see cref="Args"/>
    /// ({0}, {1}, ...) in the UI language.
    /// </summary>
    public class WarningNotification : UNotification {
        public string Key { get; }
        public string TitleKey { get; }
        public string? DetailKey { get; }
        public object[] Args { get; }
        public WarningNotification(string key, string titleKey, string? detailKey = null, params object[] args) {
            Key = key;
            TitleKey = titleKey;
            DetailKey = detailKey;
            Args = args;
        }
        public override string ToString() => $"Warning: {TitleKey} {string.Join(", ", Args)}";
    }

    /// <summary>Clears the warning with <paramref name="key"/>, or all warnings when null.</summary>
    public class ClearWarningsNotification : UNotification {
        public readonly string? key;
        public ClearWarningsNotification(string? key = null) {
            this.key = key;
        }
        public override string ToString() => key == null ? "Clear warnings" : $"Clear warning {key}";
    }

    public class VolumeChangeNotification : UNotification {
        public double Volume;
        public int TrackNo;
        public override bool Silent => true;
        public VolumeChangeNotification(int trackNo, double volume) {
            TrackNo = trackNo;
            Volume = volume;
        }
        public override string ToString() => $"Set track {TrackNo} volume to {Volume}";
    }

    public class PanChangeNotification : UNotification {
        public double Pan;
        public int TrackNo;
        public override bool Silent => true;
        public PanChangeNotification(int trackNo, double pan) {
            TrackNo = trackNo;
            Pan = pan;
        }
        public override string ToString() => $"Set track {TrackNo} panning to {Pan}";
    }

    public class SoloTrackNotification : UNotification {
        public readonly int trackNo;
        public readonly bool solo;
        public SoloTrackNotification(int trackNo, bool solo) {
            this.trackNo = trackNo;
            this.solo = solo;
        }
        public override string ToString() => $"Solo track {solo}";
    }

    public class SingersChangedNotification : UNotification {
        public SingersChangedNotification() { }
        public override string ToString() => "Singers changed.";
    }

    /// <summary>
    /// Packages in the Dependencies folder were installed, removed or edited.
    /// </summary>
    public class PackagesChangedNotification : UNotification {
        public override string ToString() => "Packages changed.";
    }

    public class SingersRefreshedNotification : UNotification {
        public readonly USinger? singer;
        public SingersRefreshedNotification() { }
        public SingersRefreshedNotification(USinger singer) {
            this.singer = singer;
        }
        public override string ToString() => "Singers refreshed.";
    }

    public class VoiceColorRemappingNotification : UNotification {
        public int TrackNo;
        public bool Validate;
        /// <summary>
        /// Remap when the singer's voice color changes. Or use when the user intentionally wants to remap.
        /// </summary>
        /// <param name="trackNo">Track number for remapping the singer. When -1, checks whether remapping is required for all tracks.</param>
        /// <param name="validate">When verifying if the color lineup has changed, set to true; when forcing remapping even if no changes occur, set to false.</param>
        public VoiceColorRemappingNotification(int trackNo, bool validate) {
            TrackNo = trackNo;
            Validate = validate;
        }
        public override string ToString() => "Voice color remapping.";
    }

    public class OtoChangedNotification : UNotification {
        public readonly bool external;
        public OtoChangedNotification(bool external = false) {
            this.external = external;
        }
        public override string ToString() => "Oto changed.";
    }

    public class WillRemoveTrackNotification : UNotification {
        public int TrackNo;
        public WillRemoveTrackNotification(int trackNo) {
            TrackNo = trackNo;
        }
        public override string ToString() => $"Will remove track {TrackNo}.";
    }

    public class FocusNoteNotification : UNotification {
        public readonly UNote note;
        public FocusNoteNotification(UPart part, UNote note) {
            this.part = part;
            this.note = note;
        }
        public override string ToString() => $"Focus note {note.lyric} at {note.position}.";
    }

    public class PreRenderNotification : UNotification {
        public readonly int focusTick;

        public PreRenderNotification(UPart part = null, int focusTick = -1) {
            this.part = part;
            this.focusTick = focusTick;
        }

        public override string ToString() => "Pre-render notification.";
    }

    public class PartRenderedNotification : UNotification {
        public PartRenderedNotification(UVoicePart part) {
            this.part = part;
        }
        public override string ToString() => "Part rendered.";
    }

    public class RealCurvesUpdatedNotification : UNotification {
        public readonly IReadOnlyList<RealCurveUpdate> updates;
        public override bool Silent => true;
        public RealCurvesUpdatedNotification(UVoicePart part, IReadOnlyList<RealCurveUpdate> updates) {
            this.part = part;
            this.updates = updates;
        }
        public override string ToString() => "Real curves updated.";
    }

    public class RealCurveCoverageNotification : UNotification {
        public readonly IReadOnlyList<(int start, int end)> ranges;
        public override bool Silent => true;
        public RealCurveCoverageNotification(UVoicePart part, IReadOnlyList<(int start, int end)> ranges) {
            this.part = part;
            this.ranges = ranges;
        }
        public override string ToString() => "Real curve coverage.";
    }

    public class GotoOtoNotification : UNotification {
        public readonly USinger? singer;
        public readonly UOto? oto;
        public GotoOtoNotification(USinger? singer, UOto? oto) {
            this.singer = singer;
            this.oto = oto;
        }
        public override string ToString() => "Goto oto.";
    }

    public class NotePresetChangedNotification : UNotification {
        public NotePresetChangedNotification() {

        }
        public override string ToString() => "Note preset changed.";
    }
}
