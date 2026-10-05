using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.App.Controls;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;

namespace OpenUtau.App.ViewModels {
    public class PhonemeMouseoverEvent {
        public readonly UPhoneme? mouseoverPhoneme;
        public PhonemeMouseoverEvent(UPhoneme? mouseoverPhoneme) {
            this.mouseoverPhoneme = mouseoverPhoneme;
        }
    }

    public class NotesContextMenuArgs {
        public PianoRollViewModel? ViewModel { get; set; }

        public bool ForNote { get; set; }
        public NoteHitInfo NoteHitInfo { get; set; }

        public bool ForPitchPoint { get; set; }
        public bool PitchPointIsFirst { get; set; }
        public bool PitchPointCanDel { get; set; }
        public bool PitchPointCanAdd { get; set; }
        public PitchPointHitInfo PitchPointHitInfo { get; set; }
    }

    public class PianorollRefreshEvent {
        public readonly string refreshItem;
        public PianorollRefreshEvent(string refreshItem) {
            this.refreshItem = refreshItem;
        }
    }

    public partial class PianoRollViewModel : ViewModelBase, ICmdSubscriber {

        [Reactive] public partial NotesViewModel NotesViewModel { get; set; }
        [Reactive] public partial PlaybackViewModel? PlaybackViewModel { get; set; }
        [Reactive] public partial CurveViewModel CurveViewModel { get; set; }

        public double Width => Preferences.Default.PianorollWindowSize.Width;
        public double Height => Preferences.Default.PianorollWindowSize.Height;

        public bool LockPitchPoints { get => Preferences.Default.LockUnselectedNotesPitch; }
        public bool LockVibrato { get => Preferences.Default.LockUnselectedNotesVibrato; }
        public bool LockExpressions { get => Preferences.Default.LockUnselectedNotesExpressions; }
        public bool ShowPortrait { get => Preferences.Default.ShowPortrait; }
        public bool ShowIcon { get => Preferences.Default.ShowIcon; }
        public bool ShowGhostNotes { get => Preferences.Default.ShowGhostNotes; }
        public bool UseTrackColor { get => Preferences.Default.UseTrackColor; }
        public bool DegreeStyle0 { get => Preferences.Default.DegreeStyle == 0 ? true : false; }
        public bool DegreeStyle1 { get => Preferences.Default.DegreeStyle == 1 ? true : false; }
        public bool DegreeStyle2 { get => Preferences.Default.DegreeStyle == 2 ? true : false; }
        public bool LockStartTime0 { get => Preferences.Default.LockStartTime == 0 ? true : false; }
        public bool LockStartTime1 { get => Preferences.Default.LockStartTime == 1 ? true : false; }
        public bool LockStartTime2 { get => Preferences.Default.LockStartTime == 2 ? true : false; }
        public bool PlaybackAutoScroll0 { get => Preferences.Default.PlaybackAutoScroll == 0 ? true : false; }
        public bool PlaybackAutoScroll1 { get => Preferences.Default.PlaybackAutoScroll == 1 ? true : false; }
        public bool PlaybackAutoScroll2 { get => Preferences.Default.PlaybackAutoScroll == 2 ? true : false; }
        public bool PianoRollDetached { get => Preferences.Default.DetachPianoRoll; }
        public bool HideMenuItemVisible => !Preferences.Default.DetachPianoRoll;
        public bool ShowPhonemizerTags {
            get => Preferences.Default.ShowPhonemizerTags;
            set {
                Preferences.Default.ShowPhonemizerTags = value;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(ShowPhonemizerTags));
            }
        }

        public EditTool EditTool { get; set; } = Preferences.Default.EditTool;
        [Reactive] public partial int ToolIndex { get; set; } = Preferences.Default.EditTool.BaseTool;
        [Reactive] public partial int PenToolIndex { get; set; } = Preferences.Default.EditTool.PenToolVariation;
        [Reactive] public partial bool PitchOverwrite { get; set; } = Preferences.Default.EditTool.OverwritePitch;

        private const string ToolTipSeparator = "\n    ";
        public string SelectionToolTip =>  GetToolHintText(EditTools.CursorTool, ToolTipSeparator);
        public string PenToolTip => GetToolHintText(EditTools.PenTool, ToolTipSeparator);
        public string PenPlusToolTip => GetToolHintText(EditTools.PenPlusTool, ToolTipSeparator);
        public string EraserToolTip => GetToolHintText(EditTools.EraserTool, ToolTipSeparator);
        public string KnifeToolTip => GetToolHintText(EditTools.KnifeTool, ToolTipSeparator);
        public string PitchPointToolTip => GetToolHintText(EditTools.PitchPointTool, ToolTipSeparator);
        public string DrawPitchToolTip => GetToolHintText(EditTools.DrawPitchTool, ToolTipSeparator);
        public string PitchLineToolTip => GetToolHintText(EditTools.PitchLineTool, ToolTipSeparator);
        public string PitchSCurveToolTip => GetToolHintText(EditTools.PitchSCurveTool, ToolTipSeparator);
        public string PitchSineWaveToolTip => GetToolHintText(EditTools.PitchSineWaveTool, ToolTipSeparator);
        public string PitchSmoothenToolTip => GetToolHintText(EditTools.PitchSmoothenTool, ToolTipSeparator);
        public string CurveSelectionToolTip => GetToolHintText(CurveTools.CurveSelectTool, ToolTipSeparator);
        public string CurvePenToolTip => GetToolHintText(CurveTools.CurvePenTool, ToolTipSeparator);
        public string CurvePitchLineToolTip => GetToolHintText(CurveTools.CurveLineTool, ToolTipSeparator);
        public string CurveEraserToolTip => GetToolHintText(CurveTools.CurveEraserTool, ToolTipSeparator);
        public string VerticalStretchToolTip => GetToolHintText(CurveTools.CurveVerticalStretchTool, ToolTipSeparator);
        public string HorizontalStretchToolTip => GetToolHintText(CurveTools.CurveHorizontalStretchTool, ToolTipSeparator);
        public string VerticalShiftToolTip => GetToolHintText(CurveTools.CurveVerticalShiftTool, ToolTipSeparator);
        public string HorizontalShiftToolTip => GetToolHintText(CurveTools.CurveHorizontalShiftTool, ToolTipSeparator);

        public ObservableCollectionExtended<MenuItemViewModel> LegacyPlugins { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> NoteBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> LyricBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> ResetBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> ExternalBatchEdits { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public ObservableCollectionExtended<MenuItemViewModel> NotesContextMenuItems { get; private set; }
            = new ObservableCollectionExtended<MenuItemViewModel>();
        public Dictionary<Key, MenuItemViewModel> LegacyPluginShortcuts { get; private set; }
            = new Dictionary<Key, MenuItemViewModel>();

        [Reactive] public partial string OperationHintText { get; set; } = string.Empty;
        [Reactive] public partial double Progress { get; set; }
        [Reactive] public partial bool CanUndo { get; set; } = false;
        [Reactive] public partial bool CanRedo { get; set; } = false;
        [Reactive] public partial string UndoText { get; set; } = ThemeManager.GetString("menu.edit.undo");
        [Reactive] public partial string RedoText { get; set; } = ThemeManager.GetString("menu.edit.redo");

        public ReactiveCommand<NoteHitInfo, RxVoid> NoteDeleteCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, RxVoid> NoteCopyCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, RxVoid> ClearPhraseCacheCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitEaseInOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitLinearCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitEaseInCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitEaseOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitSplineCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitSnapCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitDelCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, RxVoid> PitAddCommand { get; set; }

        private ReactiveCommand<Classic.Plugin, RxVoid> legacyPluginCommand;

        public PianoRollViewModel() {
            NotesViewModel = new NotesViewModel();
            CurveViewModel = new CurveViewModel();

            this.WhenAnyValue(vm => vm.ToolIndex)
                .Subscribe(index => EditTool.BaseTool = index);
            this.WhenAnyValue(vm => vm.PenToolIndex)
                .Subscribe(index => EditTool.PenToolVariation = index);
            this.WhenAnyValue(vm => vm.PitchOverwrite)
                .Subscribe(val => { EditTool.OverwritePitch = val; Preferences.Default.EditTool.OverwritePitch = val; Preferences.Save(); });

            NoteDeleteCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.DeleteSelectedNotes();
            });
            NoteCopyCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.CopyNotes();
            });
            ClearPhraseCacheCommand = ReactiveCommand.Create<NoteHitInfo>(info => {
                NotesViewModel.ClearPhraseCache();
            });
            PitEaseInOutCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.io));
                DocManager.Inst.EndUndoGroup();
            });
            PitLinearCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.l));
                DocManager.Inst.EndUndoGroup();
            });
            PitEaseInCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.i));
                DocManager.Inst.EndUndoGroup();
            });
            PitEaseOutCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.o));
                DocManager.Inst.EndUndoGroup();
            });
            PitSplineCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new ChangePitchPointShapeCommand(NotesViewModel.Part, info.Note.pitch.data[info.Index], PitchPointShape.sp));
                DocManager.Inst.EndUndoGroup();
            });
            PitSnapCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.editpoint");
                DocManager.Inst.ExecuteCmd(new SnapPitchPointCommand(NotesViewModel.Part, info.Note));
                DocManager.Inst.EndUndoGroup();
            });
            PitDelCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.delete");
                DocManager.Inst.ExecuteCmd(new DeletePitchPointCommand(NotesViewModel.Part, info.Note, info.Index));
                DocManager.Inst.EndUndoGroup();
            });
            PitAddCommand = ReactiveCommand.Create<PitchPointHitInfo>(info => {
                if (NotesViewModel.Part == null) { return; }
                DocManager.Inst.StartUndoGroup("command.pitch.add");
                DocManager.Inst.ExecuteCmd(new AddPitchPointCommand(NotesViewModel.Part, info.Note, new PitchPoint(info.X, info.Y, NotePresets.Default.DefaultPitchShape), info.Index + 1));
                DocManager.Inst.EndUndoGroup();
            });

            legacyPluginCommand = ReactiveCommand.Create<Classic.Plugin>(async plugin => {
                if (NotesViewModel.Part == null || NotesViewModel.Part.notes.Count == 0) {
                    return;
                }
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PianoRoll), true, "legacy plugin"));
                
                try {
                    var part = NotesViewModel.Part;
                    UNote? first;
                    UNote? last;
                    if (NotesViewModel.Selection.IsEmpty) {
                        first = part.notes.First();
                        last = part.notes.Last();
                    } else {
                        first = NotesViewModel.Selection.FirstOrDefault();
                        last = NotesViewModel.Selection.LastOrDefault();
                    }
                    var runner = PluginRunner.from(PathManager.Inst, DocManager.Inst);
                    await runner.Execute(NotesViewModel.Project, part, first, last, plugin);

                } catch (Exception e) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                } finally {
                    DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PianoRoll), false, "legacy plugin"));
                }
            });
            LoadLegacyPlugins();
            DocManager.Inst.AddSubscriber(this);
        }

        public void SetOperationHintText(string pointer) {
            string separator = "\n";
            switch (pointer) {
                case "Keyboard":
                    OperationHintText = GetOperationHintText(["operation.clickplaysound"], separator); // Todo: Shift + click to select notes
                    break;
                case "Timeline":
                    OperationHintText = GetOperationHintText(["operation.clickplayhead", "operation.scroolzoom", "operation.looprange"], separator);
                    break;
                case "NotesCanvas":
                    OperationHintText = GetToolHintText(EditTool.CurrentTool, separator);
                    break;
                case "PhonemeCanvas":
                    OperationHintText = GetOperationHintText(["operation.doubleeditphoneme", "operation.timingenvelope"], separator);
                    break;
                case "ExpCanvas":
                    var vm = NotesViewModel;
                    if (vm.Project == null
                        || vm.Part == null
                        || vm.Project.tracks.Count <= vm.Part.trackNo
                        || !vm.Project.tracks[vm.Part.trackNo].TryGetExpDescriptor(vm.Project, vm.PrimaryKey, out var exp)) {
                        OperationHintText = string.Empty;
                        break;
                    }
                    if (exp.type == UExpressionType.Curve) {
                        OperationHintText = GetToolHintText(CurveViewModel.CurveTool, separator);
                    } else {
                        OperationHintText = GetOperationHintText(["tools.tips.leftexp", "tools.tips.rightreset", "tools.tips.shiftsameexp"], separator);
                    }
                    break;
                case "Background":
                default:
                    OperationHintText = string.Empty;
                    break;
            }
        }
        private string GetOperationHintText(string[] keys, string separator) {
            var strings = new List<string>();
            foreach (string key in keys) {
                strings.Add(ThemeManager.GetString(key));
            }
            return string.Join(separator, strings);
        }
        private string GetToolHintText(object tool, string separator) {
            switch (tool) {
                case EditTools.CursorTool:
                    return GetOperationHintText(["tools.selection", "tools.tips.leftdragselect", "tools.tips.rightdeselect"], separator);
                case EditTools.PenTool:
                    return GetOperationHintText(["tools.pen", "tools.tips.leftdragcreate", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], separator);
                case EditTools.PenPlusTool:
                    return GetOperationHintText(["tools.penplus", "tools.tips.leftdragcreate", "tools.tips.rightdelete", "tools.tips.ctrlselect"], separator);
                case EditTools.EraserTool:
                    return GetOperationHintText(["tools.eraser", "tools.tips.leftdelete", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], separator);
                case EditTools.KnifeTool:
                    return GetOperationHintText(["tools.knife", "tools.tips.leftsplit", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], separator);
                case EditTools.PitchPointTool:
                    return GetOperationHintText(["tools.pitchpoint", "tools.tips.leftaddpoint", "tools.tips.leftdragmovepoint","tools.tips.ctrlselect"], separator);
                case EditTools.DrawPitchTool:
                    return GetOperationHintText(["tools.drawpitch", "tools.tips.leftdragdraw", "tools.tips.rightdragreset", "tools.tips.ctrlselect"], separator);
                case EditTools.PitchLineTool:
                    return GetOperationHintText(["tools.pitchline", "tools.tips.leftdragdrawline", "tools.tips.rightdragreset", "tools.tips.ctrlselect"], separator);
                case EditTools.PitchSCurveTool:
                    return GetOperationHintText(["tools.pitchscurve", "tools.tips.leftdragscurve", "tools.tips.leftdragscurve2", "tools.tips.rightdragreset", "tools.tips.ctrlselect"], separator);
                case EditTools.PitchSineWaveTool:
                    return GetOperationHintText(["tools.pitchsinewave", "tools.tips.leftdragsinewave", "tools.tips.leftdragsinewave2", "tools.tips.rightdragreset", "tools.tips.ctrlselect"], separator);
                case EditTools.PitchSmoothenTool:
                    return GetOperationHintText(["tools.pitchsmoothen", "tools.tips.leftdragsmoothen", "tools.tips.rightdragreset", "tools.tips.ctrlselect"], separator);
                case CurveTools.CurveSelectTool:
                    return GetOperationHintText(["tools.selection", "tools.tips.leftdragselect", "tools.tips.rightdeselect"], separator);
                case CurveTools.CurvePenTool:
                    return GetOperationHintText(["tools.pen", "tools.tips.leftdragdraw", "tools.tips.rightdragreset"], separator);
                case CurveTools.CurveLineTool:
                    return GetOperationHintText(["tools.line", "tools.tips.leftdragdrawline", "tools.tips.rightdragreset"], separator);
                case CurveTools.CurveEraserTool:
                    return GetOperationHintText(["tools.eraser", "tools.tips.leftdragreset", "tools.tips.rightdeselect"], separator);
                case CurveTools.CurveVerticalStretchTool:
                    return GetOperationHintText(["tools.verticalstretch", "tools.tips.leftdragstretch", "tools.tips.rightdragreset"], separator);
                case CurveTools.CurveHorizontalStretchTool:
                    return GetOperationHintText(["tools.horizontalstretch", "tools.tips.leftdragstretch", "tools.tips.rightdragreset"], separator);
                case CurveTools.CurveVerticalShiftTool:
                    return GetOperationHintText(["tools.verticalshift", "tools.tips.leftdragshift", "tools.tips.rightdragreset"], separator);
                case CurveTools.CurveHorizontalShiftTool:
                    return GetOperationHintText(["tools.horizontalshift", "tools.tips.leftdragshift", "tools.tips.rightdragreset"], separator);
                default:
                    return string.Empty;
            }
        }

        public void HideTips() {
            NotesViewModel.ShowTips = false;
        }

        private void SetUndoState() {
            CanUndo = DocManager.Inst.GetUndoState(out string? undoNameKey);
            if (!string.IsNullOrWhiteSpace(undoNameKey)) {
                UndoText = $"{ThemeManager.GetString("menu.edit.undo")}: {ThemeManager.GetString(undoNameKey)}";
            } else {
                UndoText = ThemeManager.GetString("menu.edit.undo");
            }
            CanRedo = DocManager.Inst.GetRedoState(out string? redoNameKey);
            if (!string.IsNullOrWhiteSpace(redoNameKey)) {
                RedoText = $"{ThemeManager.GetString("menu.edit.redo")}:  {ThemeManager.GetString(redoNameKey)}";
            } else {
                RedoText = ThemeManager.GetString("menu.edit.redo");
            }
        }

        private void LoadLegacyPlugins() {
            LegacyPlugins.Clear();
            LegacyPlugins.AddRange(DocManager.Inst.Plugins.Select(plugin => new MenuItemViewModel() {
                Header = plugin.Name,
                Command = legacyPluginCommand,
                CommandParameter = plugin,
            }));

            LegacyPluginShortcuts.Clear();
            foreach (MenuItemViewModel menu in LegacyPlugins) {
                if (menu.CommandParameter is Classic.Plugin plugin) {
                    if (Enum.TryParse(plugin.Shortcut, out Key key) && !LegacyPluginShortcuts.ContainsKey(key)) {
                        LegacyPluginShortcuts.Add(key, menu);
                    }
                }
            }
            LegacyPlugins.Add(new MenuItemViewModel() { // Separator
                Header = "-",
                Height = 1
            });
            LegacyPlugins.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.plugin.openfolder"),
                Command = ReactiveCommand.Create(() => {
                    try {
                        OS.OpenFolder(PathManager.Inst.PluginsPath);
                    } catch (Exception e) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    }
                })
            });
            LegacyPlugins.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.plugin.reload"),
                Command = ReactiveCommand.Create(() => {
                    DocManager.Inst.SearchAllLegacyPlugins();
                    LoadLegacyPlugins();
                })
            });
        }

        public void Undo() => DocManager.Inst.Undo();
        public void Redo() => DocManager.Inst.Redo();
        public void Cut() {
            if (CurveViewModel.IsSelected(NotesViewModel.PrimaryKey)) {
                CurveViewModel.Cut(NotesViewModel.Part!);
            } else {
                NotesViewModel.CutNotes();
            }
        }
        public void Copy() {
            if (CurveViewModel.IsSelected(NotesViewModel.PrimaryKey)) {
                CurveViewModel.Copy(NotesViewModel.Part!);
            } else {
                NotesViewModel.CopyNotes();
            }
        }
        public void Paste() {
            if (DocManager.Inst.NotesClipboard != null && DocManager.Inst.NotesClipboard.Count > 0) {
                NotesViewModel.PasteNotes();
            } else if (DocManager.Inst.CurvesClipboard != null && NotesViewModel.Part != null) {
                var track = NotesViewModel.Project.tracks[NotesViewModel.Part.trackNo];
                if (track.TryGetExpDescriptor(NotesViewModel.Project, NotesViewModel.PrimaryKey, out var descriptor)) {
                    CurveViewModel.Paste(NotesViewModel.Part, descriptor);
                }
            }
        }
        public void PastePlain() => NotesViewModel.PastePlainNotes();
        public void Delete() => NotesViewModel.DeleteSelectedNotes();
        public void SelectAll() => NotesViewModel.SelectAllNotes();

        public void MouseoverPhoneme(UPhoneme? phoneme) {
            MessageBus.Current.SendMessage(new PhonemeMouseoverEvent(phoneme));
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ProgressBarNotification progressBarNotification) {
                if (PianoRollDetached) {
                    Dispatcher.UIThread.InvokeAsync(() => {
                        Progress = progressBarNotification.Progress;
                    }, DispatcherPriority.Background);
                }
            }
            SetUndoState();
        }

        #endregion
    }
}
