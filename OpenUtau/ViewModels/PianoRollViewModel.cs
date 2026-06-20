using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
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
using ReactiveUI.Fody.Helpers;

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

    public class PianoRollViewModel : ViewModelBase, ICmdSubscriber {

        [Reactive] public NotesViewModel NotesViewModel { get; set; }
        [Reactive] public PlaybackViewModel? PlaybackViewModel { get; set; }
        [Reactive] public CurveViewModel CurveViewModel { get; set; }

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
        public bool ShowPhonemizerTags {
            get => Preferences.Default.ShowPhonemizerTags;
            set {
                Preferences.Default.ShowPhonemizerTags = value;
                Preferences.Save();
                this.RaisePropertyChanged(nameof(ShowPhonemizerTags));
            }
        }

        public EditTool EditTool { get; set; } = Preferences.Default.EditTool;
        [Reactive] public int ToolIndex { get; set; } = Preferences.Default.EditTool.BaseTool;
        [Reactive] public int PenToolIndex { get; set; } = Preferences.Default.EditTool.PenToolVariation;
        [Reactive] public int DrawPitchToolIndex { get; set; } = Preferences.Default.EditTool.DrawPitchToolVariation;
        [Reactive] public int DrawLinePitchToolIndex { get; set; } = Preferences.Default.EditTool.DrawLinePitchToolVariation;
        public string SelectionToolTip => GetOperationHint(["tools.selection", "tools.tips.leftdragselect", "tools.tips.rightdeselect"], "\n    ");
        public string PenToolTip => GetOperationHint(["tools.pen", "tools.tips.leftdragcreate", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], "\n    ");
        public string PenPlusToolTip => GetOperationHint(["tools.penplus", "tools.tips.leftdragcreate", "tools.tips.rightdelete", "tools.tips.ctrlselect"], "\n    ");
        public string EraserToolTip => GetOperationHint(["tools.eraser", "tools.tips.leftdelete", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], "\n    ");
        public string DrawPitchToolTip => GetOperationHint(["tools.drawpitch", "tools.tips.leftdragdraw", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], "\n    ");
        public string OverwritePitchToolTip => GetOperationHint(["tools.overwritepitch", "tools.tips.leftdragdrawoverwrite", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], "\n    ");
        public string DrawLinePitchToolTip => GetOperationHint(["tools.drawlinepitch", "tools.tips.leftdragdrawline", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], "\n    ");
        public string OverwriteLinePitchToolTip => GetOperationHint(["tools.overwritelinepitch", "tools.tips.leftdragdrawlineoverwrite", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], "\n    ");
        public string KnifeToolTip => GetOperationHint(["tools.knife", "tools.tips.leftsplit", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], "\n    ");
        public string CurveSelectionToolTip => GetOperationHint(["tools.selection", "tools.tips.leftdragselect", "tools.tips.rightdeselect"], "\n    ");
        public string CurvePenToolTip => GetOperationHint(["tools.pen", "tools.tips.leftdragdraw", "tools.tips.rightdragreset", "tools.tips.shifthorizontal", "tools.tips.shiftctrlline"], "\n    ");
        public string CurveEraserToolTip => GetOperationHint(["tools.eraser", "tools.tips.leftdragreset", "tools.tips.rightdeselect"], "\n    ");

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

        [Reactive] public string StatusBarText { get; set; } = string.Empty;
        [Reactive] public double Progress { get; set; }
        [Reactive] public bool CanUndo { get; set; } = false;
        [Reactive] public bool CanRedo { get; set; } = false;
        [Reactive] public string UndoText { get; set; } = ThemeManager.GetString("menu.edit.undo");
        [Reactive] public string RedoText { get; set; } = ThemeManager.GetString("menu.edit.redo");

        public ReactiveCommand<NoteHitInfo, Unit> NoteDeleteCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, Unit> NoteCopyCommand { get; set; }
        public ReactiveCommand<NoteHitInfo, Unit> ClearPhraseCacheCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitEaseInOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitLinearCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitEaseInCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitEaseOutCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitSplineCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitSnapCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitDelCommand { get; set; }
        public ReactiveCommand<PitchPointHitInfo, Unit> PitAddCommand { get; set; }

        private ReactiveCommand<Classic.Plugin, Unit> legacyPluginCommand;

        public PianoRollViewModel() {
            NotesViewModel = new NotesViewModel();
            CurveViewModel = new CurveViewModel();

            this.WhenAnyValue(vm => vm.ToolIndex)
                .Subscribe(index => EditTool.BaseTool = index);
            this.WhenAnyValue(vm => vm.PenToolIndex)
                .Subscribe(index => EditTool.PenToolVariation = index);
            this.WhenAnyValue(vm => vm.DrawPitchToolIndex)
                .Subscribe(index => EditTool.DrawPitchToolVariation = index);
            this.WhenAnyValue(vm => vm.DrawLinePitchToolIndex)
                .Subscribe(index => EditTool.DrawLinePitchToolVariation = index);

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

        public void SetStatusBarText(string pointer) {
            string separator = ThemeManager.GetString("operation.separator");
            switch (pointer) {
                case "Keyboard":
                    StatusBarText = GetOperationHint(["operation.clickplaysound"], separator);
                    break;
                case "Timeline":
                    StatusBarText = GetOperationHint(["operation.clickplayhead", "operation.scroolzoom"], separator);
                    break;
                case "NotesCanvas":
                    switch (EditTool.CurrentTool) {
                        case EditTools.CursorTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdragselect", "tools.tips.rightdeselect"], separator);
                            break;
                        case EditTools.PenTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdragcreate", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], separator);
                            break;
                        case EditTools.PenPlusTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdragcreate", "tools.tips.rightdelete", "tools.tips.ctrlselect"], separator);
                            break;
                        case EditTools.EraserTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdelete", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], separator);
                            break;
                        case EditTools.DrawPitchTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdragdraw", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], separator);
                            break;
                        case EditTools.OverwritePitchTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdragdrawoverwrite", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], separator);
                            break;
                        case EditTools.DrawLinePitchTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdragdrawline", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], separator);
                            break;
                        case EditTools.OverwriteLinePitchTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftdragdrawlineoverwrite", "tools.tips.rightdragreset", "tools.tips.altsmoothen", "tools.tips.ctrlselect"], separator);
                            break;
                        case EditTools.KnifeTool:
                            StatusBarText = GetOperationHint(["tools.tips.leftsplit", "tools.tips.rightdeselect", "tools.tips.ctrlselect"], separator);
                            break;
                        default:
                            break;
                    }
                    break;
                case "PhonemeCanvas":
                    StatusBarText = GetOperationHint(["operation.doubleeditphoneme", "operation.timingenvelope"], separator);
                    break;
                case "ExpCanvas":
                    var vm = NotesViewModel;
                    if (vm.Project == null
                        || vm.Part == null
                        || vm.Project.tracks.Count <= vm.Part.trackNo
                        || !vm.Project.tracks[vm.Part.trackNo].TryGetExpDescriptor(vm.Project, vm.PrimaryKey, out var exp)) {
                        StatusBarText = string.Empty;
                        break;
                    }
                    if (exp.type == UExpressionType.Curve) {
                        switch (CurveViewModel.CurveTool) {
                            case CurveTools.CurveSelectTool:
                                StatusBarText = GetOperationHint(["tools.tips.leftdragselect", "tools.tips.rightdeselect"], separator);
                                break;
                            case CurveTools.CurvePenTool:
                                StatusBarText = GetOperationHint(["tools.tips.leftdragdraw", "tools.tips.rightdragreset", "tools.tips.shifthorizontal", "tools.tips.shiftctrlline"], separator);
                                break;
                            case CurveTools.CurveEraserTool:
                                StatusBarText = GetOperationHint(["tools.tips.leftdragreset", "tools.tips.rightdeselect"], separator);
                                break;
                            default:
                                break;
                        }
                    } else {
                        StatusBarText = GetOperationHint(["tools.tips.leftexp", "tools.tips.rightreset", "tools.tips.shiftsameexp"], separator);
                    }
                    break;
                case "Background":
                default:
                    StatusBarText = string.Empty;
                    break;
            }
        }
        private string GetOperationHint(string[] keys, string separator) {
            var strings = new List<string>();
            foreach (string key in keys) {
                strings.Add(ThemeManager.GetString(key));
            }
            return string.Join(separator, strings);
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
