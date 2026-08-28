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
        [Reactive] public Dictionary<string, KeyGesture> Hotkeys { get; set; } = new Dictionary<string, KeyGesture>();

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
        [Reactive] public int ToolIndex { get; set; }
        [Reactive] public int PenToolIndex { get; set; }
        [Reactive] public bool PitchOverwrite { get; set; }

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

        public void ReloadShortcuts() {
            var newHotkeys = new Dictionary<string, KeyGesture>();
            foreach (var sc in KeyTranslator.Shortcuts) {
                newHotkeys.TryAdd(sc.ActionId, sc.Gesture);
            }
            Hotkeys = newHotkeys;
        }

        private int IndexToBaseTool(int index) {
            return index switch {
                0 => 0, // UI Mouse -> Backend 0 (Mouse)
                1 => 1, // UI Draw -> Backend 1 (Draw)
                2 => 2, // UI Eraser -> Backend 2 (Eraser)
                3 => 4, // UI Draw Pitch -> Backend 4 (Draw Pitch)
                4 => 5, // UI Line -> Backend 5 (Line)
                5 => 6, // UI S-Curve -> Backend 6 (S-Curve)
                6 => 7, // UI Sine Wave -> Backend 7 (Sine Wave)
                7 => 8, // UI Smoothen -> Backend 8 (Smoothen)
                8 => 3, // UI Knife -> Backend 3 (Knife)
                _ => 0
            };
        }

        private int BaseToolToIndex(int baseTool) {
            return baseTool switch {
                0 => 0, // Backend 0 (Mouse) -> UI Mouse
                1 => 1, // Backend 1 (Draw) -> UI Draw
                2 => 2, // Backend 2 (Eraser) -> UI Eraser
                3 => 8, // Backend 3 (Knife) -> UI Knife
                4 => 3, // Backend 4 (Draw Pitch) -> UI Draw Pitch
                5 => 4, // Backend 5 (Line) -> UI Line
                6 => 5, // Backend 6 (S-Curve) -> UI S-Curve
                7 => 6, // Backend 7 (Sine Wave) -> UI Sine Wave
                8 => 7, // Backend 8 (Smoothen) -> UI Smoothen
                _ => 0
            };
        }

        public PianoRollViewModel() {
            ReloadShortcuts();
            NotesViewModel = new NotesViewModel();
            CurveViewModel = new CurveViewModel();

            // Safely initialize from preferences
            ToolIndex = BaseToolToIndex(EditTool.BaseTool);
            PenToolIndex = EditTool.PenToolVariation;
            PitchOverwrite = EditTool.OverwritePitch;

            this.WhenAnyValue(vm => vm.ToolIndex)
                .Subscribe(index => {
                    EditTool.BaseTool = IndexToBaseTool(index);
                    Preferences.Default.EditTool.BaseTool = EditTool.BaseTool;
                    Preferences.Save();
                });
                
            this.WhenAnyValue(vm => vm.PenToolIndex)
                .Subscribe(index => EditTool.PenToolVariation = index);
                
            this.WhenAnyValue(vm => vm.PitchOverwrite)
                .Subscribe(val => { 
                    EditTool.OverwritePitch = val; 
                    Preferences.Default.EditTool.OverwritePitch = val; 
                    Preferences.Save(); 
                });

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
            MessageBus.Current.Listen<ShortcutsRefreshEvent>()
                .Subscribe(_ => ReloadShortcuts());
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
                InputGesture = KeyTranslator.GetGestureForMenu(plugin.Name),
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