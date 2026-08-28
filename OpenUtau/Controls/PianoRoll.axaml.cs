using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.ViewModels;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.Controls {
    interface IValueTip {
        void ShowValueTip();
        void HideValueTip();
        void UpdateValueTip(string text);
    }

    public partial class PianoRoll : UserControl, IValueTip, ICmdSubscriber {
        public MainWindow? MainWindow { get; set; }
        public PianoRollViewModel ViewModel;

        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        private KeyboardPlayState? keyboardPlayState;
        private NoteEditState? editState;
        private Point valueTipPointerPosition;
        private bool shouldOpenNotesContextMenu;

        private bool isSelectingRange;
        private Point rangeSelectStartPoint = default;
        private const double RangeSelectThreshold = 5; // pixels

        private ReactiveCommand<Unit, Unit>? lyricsDialogCommand;
        private ReactiveCommand<Unit, Unit>? noteDefaultsCommand;
        private ReactiveCommand<BatchEdit, Unit>? noteBatchEditCommand;

        private Window RootWindow => (Window)TopLevel.GetTopLevel(this)!;

        public PianoRoll(PianoRollViewModel model) {
            InitializeComponent();
            DataContext = ViewModel = model;
            ValueTip.IsVisible = false;
            SetPenToolIcon();
            penTool.AddHandler(PointerPressedEvent, OnToolButtonPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
            this.LayoutUpdated += PianoRollLayoutUpdated;
        }

        private void PianoRollLayoutUpdated(object? sender, EventArgs e) {
            UpdatePortraitPosition();
        }

        private void UpdatePortraitPosition() {
            if (PortraitImage.DesiredSize.Width == 0 || PortraitCanvas.Bounds.Width == 0) return;
            // Position at top-right of row 3, with 100px margin from right
            Canvas.SetTop(PortraitImage, 0);
            Canvas.SetLeft(PortraitImage, PortraitCanvas.Bounds.Width - PortraitImage.DesiredSize.Width - 100);
        }

        public void InitializePianoRollWindowAsync() {
            noteBatchEditCommand = ReactiveCommand.Create<BatchEdit>(async edit => {
                var NotesVm = ViewModel?.NotesViewModel;
                if (NotesVm == null || NotesVm.Part == null) {
                    return;
                }
                try {
                    if (edit.IsAsync) {
                        var mainWindow =
                            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                            ?.MainWindow! as MainWindow;
                        var name = ThemeManager.GetString(edit.Name);
                        await MessageBox.ShowProcessing(RootWindow, $"{name} - ? / ?",
                            ThemeManager.GetString("pianoroll.menu.batch.running"),
                            (messageBox, cancellationToken) => {
                                edit.RunAsync(NotesVm.Project, NotesVm.Part,
                                    NotesVm.Selection.ToList(), DocManager.Inst,
                                    (current, total) => {
                                        messageBox.SetText($"{name}: {current} / {total}");
                                    }, cancellationToken);
                            },
                            (Task t) => {
                                var e = t.Exception;
                                if (t.IsFaulted && e != null) {
                                    if (e != null) {
                                        Log.Error(e, $"Failed to run Editing Macro");
                                        var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                                    }
                                    return;
                                }
                            }
                        );
                    } else {
                        edit.Run(NotesVm.Project, NotesVm.Part, NotesVm.Selection.ToList(),
                            DocManager.Inst);
                    }
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }

            });
            ViewModel.NoteBatchEdits.AddRange(new List<BatchEdit>() {
                new LoadRenderedPitch(),
                new RefreshRealCurves(),
                new AddTailNote("-", "pianoroll.menu.notes.addtaildash"),
                new AddTailNote("R", "pianoroll.menu.notes.addtailrest"),
                new RemoveTailNote("-", "pianoroll.menu.notes.removetaildash"),
                new RemoveTailNote("R", "pianoroll.menu.notes.removetailrest"),
                new Transpose(12, "pianoroll.menu.notes.octaveup"),
                new Transpose(-12, "pianoroll.menu.notes.octavedown"),
                new AutoLegato(),
                new CommonnoteCopy(),
                new CommonnotePaste(),
                new FixOverlap(),
                new BakePitch(),
                new RandomizeTiming(),
                new RandomizePhonemeOffset()
            }.Select(edit => new MenuItemViewModel() {
                Header = ThemeManager.GetString(edit.Name),
                Command = noteBatchEditCommand,
                CommandParameter = edit,
            }));
            ViewModel.LyricBatchEdits.AddRange(new List<BatchEdit>() {
                new RomajiToHiragana(),
                new HiraganaToRomaji(),
                new JapaneseVCVtoCV(),
                new HanziToPinyin(),
                new RemoveToneSuffix(),
                new RemoveLetterSuffix(),
                new MoveSuffixToVoiceColor(),
                new RemovePhoneticHint(),
                new DashToPlus(),
                new DashToPlusTilda(),
                new InsertSlur(),
            }.Select(edit => new MenuItemViewModel() {
                Header = ThemeManager.GetString(edit.Name),
                Command = noteBatchEditCommand,
                CommandParameter = edit,
            }));
            ViewModel.ResetBatchEdits.AddRange(new List<BatchEdit>() {
                new ResetAll(),
                new ResetPitchBends(),
                new ResetAllExpressions(),
                new ClearVibratos(),
                new ResetVibratos(),
                new ClearTimings(),
                new ResetAliases(),
            }.Select(edit => new MenuItemViewModel() {
                Header = ThemeManager.GetString(edit.Name),
                Command = noteBatchEditCommand,
                CommandParameter = edit,
            }));
            try {
                ViewModel.ExternalBatchEdits.AddRange(
                    DocManager.Inst.ExternalBatchEditTypes
                        .Select(type => Activator.CreateInstance(type) as BatchEdit)
                        .Where(edit => edit != null)
                        .Select(edit => new MenuItemViewModel() {
                            Header = ThemeManager.GetString(edit!.Name),
                            Command = noteBatchEditCommand,
                            CommandParameter = edit,
                        })
                );
            } catch (Exception e) {
                Log.Error(e, "Failed to load external batch edits.");
            }

            DocManager.Inst.AddSubscriber(this);

            ViewModel.NoteBatchEdits.Insert(6, new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.addbreath"),
                Command = ReactiveCommand.Create(() => {
                    AddBreathNote();
                })
            });
            ViewModel.NoteBatchEdits.Insert(9, new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.quantize"),
                Command = ReactiveCommand.Create(() => {
                    QuantizeNotes();
                })
            });
            ViewModel.NoteBatchEdits.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.randomizetuning"),
                Command = ReactiveCommand.Create(() => {
                    RandomizeTuning();
                })
            });
            ViewModel.NoteBatchEdits.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("pianoroll.menu.notes.lengthencrossfade"),
                Command = ReactiveCommand.Create(() => {
                    LengthenCrossfade();
                })
            });
            ViewModel.LyricBatchEdits.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("lyricsreplace.replace"),
                Command = ReactiveCommand.Create(() => {
                    ReplaceLyrics();
                })
            });
            lyricsDialogCommand = ReactiveCommand.Create(() => {
                EditLyrics();
            });
            noteDefaultsCommand = ReactiveCommand.Create(() => {
                EditNoteDefaults();
            });

            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            this.WhenAnyValue(x => x.ViewModel!.PlaybackViewModel!.PlayPosTick)
                .Subscribe(tick => {
                    var notesVm = ViewModel?.NotesViewModel;

                    if (notesVm?.Part == null) return;
                    if (tick < notesVm.Part.position || tick >= notesVm.Part.End) {
                        var targetPart = notesVm.Project.parts
                            .OfType<UVoicePart>()
                            .FirstOrDefault(p => p.trackNo == notesVm.Part.trackNo && p.position <= tick && p.End > tick);

                        if (targetPart != null) {
                            DocManager.Inst.ExecuteCmd(new LoadPartNotification(targetPart, notesVm.Project, tick));
                            AttachExpressions();
                        }
                    }
                });

            DocManager.Inst.AddSubscriber(this);
        }

        void OnMenuClosed(object sender, RoutedEventArgs args) {
            Focus(); // Force unfocus menu for key down events.
        }

        void OnMenuPointerLeave(object sender, PointerEventArgs args) {
            Focus(); // Force unfocus menu for key down events.
        }

        // Edit menu
        void OnMenuLockPitchPoints(object sender, RoutedEventArgs args) {
            Preferences.Default.LockUnselectedNotesPitch = !Preferences.Default.LockUnselectedNotesPitch;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.LockPitchPoints));
        }
        void OnMenuLockVibrato(object sender, RoutedEventArgs args) {
            Preferences.Default.LockUnselectedNotesVibrato = !Preferences.Default.LockUnselectedNotesVibrato;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.LockVibrato));
        }
        void OnMenuLockExpressions(object sender, RoutedEventArgs args) {
            Preferences.Default.LockUnselectedNotesExpressions = !Preferences.Default.LockUnselectedNotesExpressions;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.LockExpressions));
        }

        // View menu
        void OnMenuShowPortrait(object sender, RoutedEventArgs args) {
            Preferences.Default.ShowPortrait = !Preferences.Default.ShowPortrait;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.ShowPortrait));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
        }
        void OnMenuShowIcon(object sender, RoutedEventArgs args) {
            Preferences.Default.ShowIcon = !Preferences.Default.ShowIcon;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.ShowIcon));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Portrait"));
        }
        void OnMenuShowGhostNotes(object sender, RoutedEventArgs args) {
            Preferences.Default.ShowGhostNotes = !Preferences.Default.ShowGhostNotes;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.ShowGhostNotes));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));

        }
        void OnMenuUseTrackColor(object sender, RoutedEventArgs args) {
            Preferences.Default.UseTrackColor = !Preferences.Default.UseTrackColor;
            Preferences.Save();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.UseTrackColor));
            MessageBus.Current.SendMessage(new PianorollRefreshEvent("TrackColor"));
        }
        void OnMenuFullScreen(object sender, RoutedEventArgs args) {
            RootWindow.WindowState = RootWindow.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : WindowState.FullScreen;
        }
        void OnMenuDegreeStyle(object sender, RoutedEventArgs args) {
            if (sender is MenuItem menu && int.TryParse(menu.Tag?.ToString(), out int tag)) {
                Preferences.Default.DegreeStyle = tag;
                Preferences.Save();
                ViewModel.RaisePropertyChanged(nameof(ViewModel.DegreeStyle0));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.DegreeStyle1));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.DegreeStyle2));
                MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
            }
        }
        void OnMenuLockStartTime(object sender, RoutedEventArgs args) {
            if (sender is MenuItem menu && int.TryParse(menu.Tag?.ToString(), out int tag)) {
                Preferences.Default.LockStartTime = tag;
                Preferences.Save();
                ViewModel.RaisePropertyChanged(nameof(ViewModel.LockStartTime0));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.LockStartTime1));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.LockStartTime2));
            }
        }
        void OnMenuPlaybackAutoScroll(object sender, RoutedEventArgs args) {
            if (sender is MenuItem menu && int.TryParse(menu.Tag?.ToString(), out int tag)) {
                Preferences.Default.PlaybackAutoScroll = tag;
                Preferences.Save();
                ViewModel.RaisePropertyChanged(nameof(ViewModel.PlaybackAutoScroll0));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.PlaybackAutoScroll1));
                ViewModel.RaisePropertyChanged(nameof(ViewModel.PlaybackAutoScroll2));
            }
        }

        async void OnMenuSingers(object sender, RoutedEventArgs args) {
            if (MainWindow != null) {
                await MainWindow.OpenSingersWindowAsync();
            }
            RootWindow.Activate();
            try {
                USinger? singer = null;
                UOto? oto = null;
                if (ViewModel.NotesViewModel.Part != null) {
                    singer = ViewModel.NotesViewModel.Project.tracks[ViewModel.NotesViewModel.Part.trackNo].Singer;
                    if (!ViewModel.NotesViewModel.Selection.IsEmpty && ViewModel.NotesViewModel.Part.phonemes.Count() > 0) {
                        oto = ViewModel.NotesViewModel.Part.phonemes.First(p => p.Parent == ViewModel.NotesViewModel.Selection.First()).oto;
                    }
                }
                DocManager.Inst.ExecuteCmd(new GotoOtoNotification(singer, oto));
            } catch { }
        }

        void OnMenuSearchNote(object sender, RoutedEventArgs args) {
            SearchNote();
        }

        void OnMenuDetachPianoRoll(object sender, RoutedEventArgs args) {
            MainWindow?.SetPianoRollAttachment();
            ViewModel.RaisePropertyChanged(nameof(ViewModel.PianoRollDetached));
        }

        void OnMenuHidePianoRoll(object sender, RoutedEventArgs args) {
            if (RootWindow.DataContext is MainWindowViewModel mwvm) {
                mwvm.ShowPianoRoll = false;
            } else {
                RootWindow.Hide();
            }
        }

        // Edit Tools
        private CancellationTokenSource? _longPressCts;
        private async void OnToolButtonPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (sender is Control control) {
                _longPressCts = new CancellationTokenSource();
                try {
                    await Task.Delay(500, _longPressCts.Token);
                    if (_longPressCts != null && !_longPressCts.IsCancellationRequested) {
                        FlyoutBase.ShowAttachedFlyout(control);
                    }
                } catch {
                    // don't open the flyout
                }
            }
        }
        private void OnToolButtonPointerReleased(object? sender, PointerReleasedEventArgs args) {
            _longPressCts?.Cancel();
            _longPressCts?.Dispose();
            _longPressCts = null;
        }
        void PenToolListBox_PointerReleased(object? sender, PointerReleasedEventArgs args) {
            FlyoutBase.GetAttachedFlyout(penTool)?.Hide(); ;
            SetPenToolIcon();
        }
        void SetPenToolIcon() {
            penTool.Classes.Remove("penTool");
            penTool.Classes.Remove("penPlusTool");
            penTool.Classes.Add(ViewModel.EditTool.PenToolVariation == 1 ? "penPlusTool" : "penTool");
        }

        void SearchNote() {
            if (ViewModel.NotesViewModel.Part == null || ViewModel.NotesViewModel.Part.notes.Count == 0) {
                return;
            }
            SearchBar.Show(ViewModel.NotesViewModel);
        }

        void ReplaceLyrics() {
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            if (ViewModel.NotesViewModel.Part.notes.Count < 1) {
                _ = MessageBox.Show(
                    RootWindow,
                    ThemeManager.GetString("lyrics.nonote"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }

            var notes = ViewModel.NotesViewModel.Selection.ToArray();
            if (notes.Length == 0) {
                notes = ViewModel.NotesViewModel.Part.notes.ToArray();
            }
            var vm = new LyricsReplaceViewModel(ViewModel.NotesViewModel.Part, notes);
            var dialog = new LyricsReplaceDialog() {
                DataContext = vm,
            };
            dialog.ShowDialog(RootWindow);
        }

        void OnMenuEditLyrics(object? sender, RoutedEventArgs e) {
            EditLyrics();
        }

        void EditLyrics() {
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            if (ViewModel.NotesViewModel.Part.notes.Count < 1) {
                _ = MessageBox.Show(
                    RootWindow,
                    ThemeManager.GetString("lyrics.nonote"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }

            var vm = new LyricsViewModel();
            var (notes, selection) = ViewModel.NotesViewModel.PrepareInsertLyrics();
            vm.Start(ViewModel.NotesViewModel.Part, notes, selection);
            var dialog = new LyricsDialog() {
                DataContext = vm,
            };
            dialog.ShowDialog(RootWindow);
        }

        void OnMenuNoteDefaults(object sender, RoutedEventArgs args) {
            EditNoteDefaults();
        }

        void EditNoteDefaults() {
            var dialog = new NoteDefaultsDialog();
            dialog.ShowDialog(RootWindow);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        void AddBreathNote() {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            if (notesVM.Selection.IsEmpty) {
                _ = MessageBox.Show(
                    RootWindow,
                    ThemeManager.GetString("lyrics.selectnotes"),
                    ThemeManager.GetString("lyrics.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            var dialog = new TypeInDialog() {
                Title = ThemeManager.GetString("pianoroll.menu.notes.addbreath"),
                onFinish = value => {
                    if (!string.IsNullOrWhiteSpace(value)) {
                        var edit = new Core.Editing.AddBreathNote(value);
                        try {
                            edit.Run(notesVM.Project, notesVM.Part, notesVM.Selection.ToList(), DocManager.Inst);
                        } catch (Exception e) {
                            var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                        }
                    }
                }
            };
            dialog.SetText("br");
            dialog.ShowDialog(RootWindow);
        }

        void QuantizeNotes() {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            var edit = new QuantizeNotes(notesVM.Project.resolution * 4 / notesVM.SnapDiv);
            edit.Run(notesVM.Project, notesVM.Part, notesVM.Selection.ToList(), DocManager.Inst);
        }

        void RandomizeTuning() {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            var dialog = new SliderDialog(ThemeManager.GetString("pianoroll.menu.notes.randomizetuning"), 20, 1, 100, 1);
            dialog.onFinish = value => {
                try {
                    var edit = new RandomizeTuning((int)value);
                    edit.Run(notesVM.Project, notesVM.Part, notesVM.Selection.ToList(), DocManager.Inst);
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            };
            dialog.ShowDialog(RootWindow);
        }

        void LengthenCrossfade() {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            var dialog = new SliderDialog(ThemeManager.GetString("pianoroll.menu.notes.lengthencrossfade"), 0.5, 0, 1, 0.1);
            dialog.onFinish = value => {
                var edit = new Core.Editing.LengthenCrossfade(value);
                try {
                    edit.Run(notesVM.Project, notesVM.Part, notesVM.Selection.ToList(), DocManager.Inst);
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to run editing macro", "<translate:errors.failed.runeditingmacro>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            };
            dialog.ShowDialog(RootWindow);
        }

        private void OnPianoRollFocus(object sender, GotFocusEventArgs e) {
            var input = e.Source as InputElement;
            if (input is TextBox or ComboBox or ComboBoxItem) {
                input.Focus();
            }
        }

        private void LyricBoxLostFocus(object sender, RoutedEventArgs e) {
            if (sender is InputElement { IsKeyboardFocusWithin: false }) {
                this.Focus();
            }
        }

        public void OnExpButtonClick(object sender, RoutedEventArgs args) {
            var notesVM = ViewModel.NotesViewModel;
            if (notesVM.Part == null) {
                return;
            }
            var dialog = new ExpressionsDialog() {
                DataContext = new ExpressionsViewModel(notesVM.Project.tracks[notesVM.Part.trackNo]),
            };
            dialog.ShowDialog(RootWindow);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
        }

        public void KeyboardPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            LyricBox?.EndEdit();
            VScrollPointerWheelChanged(VScrollBar, args);
        }

        public void KeyboardPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (keyboardPlayState != null) {
                return;
            }
            var element = (TrackBackground)sender;
            keyboardPlayState = new KeyboardPlayState(element, ViewModel);
            keyboardPlayState.Begin(args.Pointer, args.GetPosition(element));
        }

        public void KeyboardPointerMoved(object sender, PointerEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.Update(args.Pointer, args.GetPosition(element));
            }
        }

        public void KeyboardPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (keyboardPlayState != null) {
                var element = (TrackBackground)sender;
                keyboardPlayState.End(args.Pointer, args.GetPosition(element));
                keyboardPlayState = null;
            }
        }

        public void HScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            LyricBox?.EndEdit();
        }

        public void VScrollPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var scrollbar = (ScrollBar)sender;
            scrollbar.Value = Math.Max(scrollbar.Minimum, Math.Min(scrollbar.Maximum, scrollbar.Value - scrollbar.SmallChange * args.Delta.Y));
            LyricBox?.EndEdit();
        }

        public void TimelinePointerWheelChanged(object sender, PointerWheelEventArgs args) {
            var control = (Control)sender;
            var position = args.GetCurrentPoint((Visual)sender).Position;
            var size = control.Bounds.Size;
            position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
            ViewModel.NotesViewModel.OnXZoomed(position, 0.1 * args.Delta.Y);
            LyricBox?.EndEdit();
        }

        public void ViewScalerPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            ViewModel.NotesViewModel.OnYZoomed(new Point(0, 0.5), 0.1 * args.Delta.Y);
            LyricBox?.EndEdit();
        }

        public void TimelinePointerPressed(object sender, PointerPressedEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (point.Properties.IsLeftButtonPressed) {
                args.Pointer.Capture(control);
                ViewModel.NotesViewModel.PointToLineTick(point.Position, out int left, out int right);
                int tick = left + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
            } else if (point.Properties.IsRightButtonPressed) {
                args.Pointer.Capture(control);
                isSelectingRange = true;
                rangeSelectStartPoint = point.Position;
                LyricBox?.EndEdit();
                return;
            }
            LyricBox?.EndEdit();
        }

        public void TimelinePointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (point.Properties.IsLeftButtonPressed) {
                ViewModel.NotesViewModel.PointToLineTick(point.Position, out int left, out int right);
                int tick = left + ViewModel.NotesViewModel.Part?.position ?? 0;
                ViewModel.PlaybackViewModel?.MovePlayPos(tick);
            } else if (point.Properties.IsRightButtonPressed && isSelectingRange) {
                double dx = Math.Abs(point.Position.X - rangeSelectStartPoint.X);
                if (dx >= RangeSelectThreshold) {
                    UpdateRangeSelection(point.Position);
                }
            }
        }

        public void TimelinePointerReleased(object sender, PointerReleasedEventArgs args) {
            if (isSelectingRange && args.InitialPressMouseButton == MouseButton.Right) {
                isSelectingRange = false;
                var control = (Control)sender;
                var point = args.GetCurrentPoint(control);
                double dx = Math.Abs(point.Position.X - rangeSelectStartPoint.X);
                if (dx < RangeSelectThreshold) {
                    DocManager.Inst.ExecuteCmd(new SetRangeSelectionNotification(0, 0));
                }
                args.Pointer.Capture(null);
                return;
            }
            args.Pointer.Capture(null);
        }

        public void TimelineDoubleTapped(object sender, TappedEventArgs args) {
            DocManager.Inst.ExecuteCmd(new SetRangeSelectionNotification(0, 0));
        }

        private void UpdateRangeSelection(Point currentPoint) {
            var notesVm = ViewModel.NotesViewModel;
            int partPos = notesVm.Part?.position ?? 0;
            notesVm.PointToLineTick(rangeSelectStartPoint, out int startLeft, out int startRight);
            notesVm.PointToLineTick(currentPoint, out int endLeft, out int endRight);
            int left = Math.Min(startLeft, endLeft);
            int right = Math.Max(startRight, endRight);
            DocManager.Inst.ExecuteCmd(new SetRangeSelectionNotification(left + partPos, right + partPos));
        }

        public void NotesCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel.NotesViewModel.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                // Finalize pitch curve in adjusting phase before starting a new edit
                if (editState is PitchCurveState pcs2 && pcs2.IsInAdjustingPhase) {
                    if (point.Properties.IsLeftButtonPressed) {
                        pcs2.Apply();
                        pcs2.End(pointer: args.Pointer, point: point.Position);
                    } else {
                        // Right-click during adjusting: cancel the edit
                        pcs2.Cancel(args.Pointer);
                    }
                    editState = null;
                } else {
                    return;
                }
            }
            if (point.Properties.IsLeftButtonPressed) {
                NotesCanvasLeftPointerPressed(control, point, args);
            } else if (point.Properties.IsRightButtonPressed) {
                NotesCanvasRightPointerPressed(control, point, args);
            } else if (point.Properties.IsMiddleButtonPressed) {
                editState = new NotePanningState(control, ViewModel, this);
                Cursor = ViewConstants.cursorHand;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        private void NotesCanvasLeftPointerPressed(Control control, PointerPoint point, PointerPressedEventArgs args) {
            if (ViewModel.EditTool.IsPitchTool) {
                ViewModel.NotesViewModel.DeselectNotes();
                if (args.KeyModifiers != cmdKey) {
                    bool overwrite = ViewModel.EditTool.OverwritePitch;
                    EditTools tool = ViewModel.EditTool.CurrentTool;
                    if (tool == EditTools.PitchSmoothenTool) {
                        editState = new SmoothenPitchState(control, ViewModel, this, overwrite);
                    } else if (tool == EditTools.DrawPitchTool) {
                        editState = new DrawPitchState(control, ViewModel, this, overwrite);
                    } else if (tool == EditTools.PitchLineTool) {
                        editState = new PitchCurveState(control, ViewModel, this, PitchPreviewLine, PitchCurveState.CurveMode.Line, overwrite);
                    } else if (tool == EditTools.PitchSCurveTool) {
                        editState = new PitchCurveState(control, ViewModel, this, PitchPreviewLine, PitchCurveState.CurveMode.SCurve, overwrite);
                    } else if (tool == EditTools.PitchSineWaveTool) {
                        editState = new PitchCurveState(control, ViewModel, this, PitchPreviewLine, PitchCurveState.CurveMode.Sine, overwrite);
                    }
                    return;
                }
            }
            if (ViewModel.EditTool.CurrentTool == EditTools.EraserTool && args.KeyModifiers != cmdKey) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteEraseEditState(control, ViewModel, this, MouseButton.Left);
                Cursor = ViewConstants.cursorNo;
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null) {
                editState = new PitchPointEditState(control, ViewModel, this,
                    pitHitInfo.Note, pitHitInfo.Index, pitHitInfo.OnPoint, pitHitInfo.X, pitHitInfo.Y);
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit) {
                if (vbrHitInfo.hitToggle) {
                    ViewModel.NotesViewModel.ToggleVibrato(vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitStart) {
                    editState = new VibratoChangeStartState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitIn) {
                    editState = new VibratoChangeInState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitOut) {
                    editState = new VibratoChangeOutState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitDepth) {
                    editState = new VibratoChangeDepthState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitPeriod) {
                    editState = new VibratoChangePeriodState(control, ViewModel, this, vbrHitInfo.note);
                    return;
                }
                if (vbrHitInfo.hitShift) {
                    editState = new VibratoChangeShiftState(
                        control, ViewModel, this, vbrHitInfo.note, vbrHitInfo.point, vbrHitInfo.initialShift);
                    return;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitBody) {
                if (noteHitInfo.hitResizeArea) {
                    editState = new NoteResizeEditState(
                        control, ViewModel, this, noteHitInfo.note,
                        args.KeyModifiers == KeyModifiers.Alt,
                        fromStart: noteHitInfo.hitResizeAreaFromStart);
                    Cursor = ViewConstants.cursorSizeWE;
                } else if (args.KeyModifiers == cmdKey) {
                    ViewModel.NotesViewModel.ToggleSelectNote(noteHitInfo.note);
                } else if (args.KeyModifiers == KeyModifiers.Shift) {
                    ViewModel.NotesViewModel.SelectNotesUntil(noteHitInfo.note);
                } else if (ViewModel.EditTool.CurrentTool == EditTools.KnifeTool) {
                    ViewModel.NotesViewModel.DeselectNotes();
                    editState = new NoteSplitEditState(
                            control, ViewModel, this, noteHitInfo.note);
                } else {
                    editState = new NoteMoveEditState(control, ViewModel, this, noteHitInfo.note);
                    Cursor = ViewConstants.cursorSizeAll;
                }
                return;
            }
            if (ViewModel.EditTool.CurrentTool == EditTools.CursorTool || args.KeyModifiers == cmdKey) {
                if (args.KeyModifiers == KeyModifiers.None) {
                    // New selection.
                    ViewModel.NotesViewModel.DeselectNotes();
                    editState = new NoteSelectionEditState(control, ViewModel, this, SelectionBox);
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                if (args.KeyModifiers == cmdKey) {
                    // Additional selection.
                    editState = new NoteSelectionEditState(control, ViewModel, this, SelectionBox);
                    Cursor = ViewConstants.cursorCross;
                    return;
                }
                ViewModel.NotesViewModel.DeselectNotes();
            } else if (ViewModel.EditTool.IsMatch([EditTools.PenTool, EditTools.PenPlusTool])) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteDrawEditState(control, ViewModel, this, ViewModel.NotesViewModel.PlayTone);
            }
        }

        private void NotesCanvasRightPointerPressed(Control control, PointerPoint point, PointerPressedEventArgs args) {
            if (ViewModel.NotesContextMenuItems.Count > 0) {
                ViewModel.NotesContextMenuItems.Clear();
            }
            var selectedNotes = ViewModel.NotesViewModel.Selection.ToList();
            if (ViewModel.EditTool.IsPitchTool) {
                editState = new ResetPitchState(control, ViewModel, this);
                return;
            }
            if (ViewModel.NotesViewModel.ShowPitch) {
                var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
                if (pitHitInfo.Note != null) {
                    var shapes = new List<MenuItemViewModel>();
                    var currentShape = pitHitInfo.Note.pitch.data[pitHitInfo.Index].shape;
                    shapes.Add(new MenuItemViewModel(currentShape == PitchPointShape.io) {
                        Header = ThemeManager.GetString("context.pitch.easeinout"),
                        Command = ViewModel.PitEaseInOutCommand,
                        CommandParameter = pitHitInfo,
                    });
                    shapes.Add(new MenuItemViewModel(currentShape == PitchPointShape.l) {
                        Header = ThemeManager.GetString("context.pitch.linear"),
                        Command = ViewModel.PitLinearCommand,
                        CommandParameter = pitHitInfo,
                    });
                    shapes.Add(new MenuItemViewModel(currentShape == PitchPointShape.i) {
                        Header = ThemeManager.GetString("context.pitch.easein"),
                        Command = ViewModel.PitEaseInCommand,
                        CommandParameter = pitHitInfo,
                    });
                    shapes.Add(new MenuItemViewModel(currentShape == PitchPointShape.o) {
                        Header = ThemeManager.GetString("context.pitch.easeout"),
                        Command = ViewModel.PitEaseOutCommand,
                        CommandParameter = pitHitInfo,
                    });
                    shapes.Add(new MenuItemViewModel(currentShape == PitchPointShape.sp) {
                        Header = ThemeManager.GetString("context.pitch.smooth"),
                        Command = ViewModel.PitSplineCommand,
                        CommandParameter = pitHitInfo,
                    });
                    ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("context.pitch.shape"),
                        Items = shapes,
                    });
                    if (pitHitInfo.OnPoint && pitHitInfo.Index == 0) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel(pitHitInfo.Note.pitch.snapFirst) {
                            Header = ThemeManager.GetString("context.pitch.pointsnapprev"),
                            Command = ViewModel.PitSnapCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    if (pitHitInfo.OnPoint && pitHitInfo.Index != 0 &&
                        pitHitInfo.Index != pitHitInfo.Note.pitch.data.Count - 1) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointdel"),
                            Command = ViewModel.PitDelCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    if (!pitHitInfo.OnPoint) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.pitch.pointadd"),
                            Command = ViewModel.PitAddCommand,
                            CommandParameter = pitHitInfo,
                        });
                    }
                    shouldOpenNotesContextMenu = true;
                    return;
                }
            }
            if (ViewModel.EditTool.IsMatch([EditTools.CursorTool, EditTools.PenTool, EditTools.KnifeTool])) {
                var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
                var vibHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
                if ((hitInfo.hitBody && hitInfo.note != null) || vibHitInfo.hit) {
                    if (hitInfo.note != null && !selectedNotes.Contains(hitInfo.note)) {
                        ViewModel.NotesViewModel.DeselectNotes();
                        ViewModel.NotesViewModel.SelectNote(hitInfo.note, false);
                    }
                    if (ViewModel.NotesViewModel.Selection.Count > 0) {
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.copy"),
                            Command = ViewModel.NoteCopyCommand,
                            CommandParameter = hitInfo,
                            InputGesture = new KeyGesture(Key.C, KeyModifiers.Control),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.delete"),
                            Command = ViewModel.NoteDeleteCommand,
                            CommandParameter = hitInfo,
                            InputGesture = new KeyGesture(Key.Delete),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.pasteparameters"),
                            Command = ReactiveCommand.Create(() => ViewModel.NotesViewModel.PasteSelectedParams(RootWindow)),
                            InputGesture = new KeyGesture(Key.V, KeyModifiers.Alt),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.notes"),
                            Items = ViewModel.NoteBatchEdits.ToArray(),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.lyrics"),
                            Items = ViewModel.LyricBatchEdits.ToArray(),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.reset"),
                            Items = ViewModel.ResetBatchEdits.ToArray(),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.part.legacypluginexp"),
                            Items = ViewModel.LegacyPlugins.ToArray(),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.external"),
                            Items = ViewModel.ExternalBatchEdits.ToArray(),
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.lyrics.edit"),
                            Command = lyricsDialogCommand,
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("pianoroll.menu.notedefaults"),
                            Command = noteDefaultsCommand,
                        });
                        ViewModel.NotesContextMenuItems.Add(new MenuItemViewModel() {
                            Header = ThemeManager.GetString("context.note.clearcache"),
                            Command = ViewModel.ClearPhraseCacheCommand,
                        });
                        shouldOpenNotesContextMenu = true;
                        return;
                    }
                } else {
                    ViewModel.NotesViewModel.DeselectNotes();
                }
            } else if (ViewModel.EditTool.IsMatch([EditTools.EraserTool, EditTools.PenPlusTool])) {
                ViewModel.NotesViewModel.DeselectNotes();
                editState = new NoteEraseEditState(control, ViewModel, this, MouseButton.Right);
                Cursor = ViewConstants.cursorNo;
            }
        }

        public void NotesCanvasPointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            args.Handled = true;
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.altShiftHeld = args.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift);
                editState.shiftHeld = args.KeyModifiers == KeyModifiers.Shift;
                editState.ctrlHeld = args.KeyModifiers == cmdKey;
                editState.altHeld = args.KeyModifiers == KeyModifiers.Alt;
                editState.Update(point.Pointer, point.Position);
                return;
            }
            if (ViewModel?.NotesViewModel?.HitTest == null) {
                return;
            }
            if (ViewModel.EditTool.IsMatch([EditTools.DrawPitchTool, EditTools.PitchLineTool, EditTools.PitchSCurveTool, EditTools.PitchSineWaveTool, EditTools.PitchSmoothenTool, EditTools.EraserTool]) && args.KeyModifiers != cmdKey) {
                Cursor = null;
                return;
            }
            var pitHitInfo = ViewModel.NotesViewModel.HitTest.HitTestPitchPoint(point.Position);
            if (pitHitInfo.Note != null) {
                Cursor = ViewConstants.cursorHand;
                return;
            }
            var vbrHitInfo = ViewModel.NotesViewModel.HitTest.HitTestVibrato(point.Position);
            if (vbrHitInfo.hit) {
                if (vbrHitInfo.hitDepth) {
                    Cursor = ViewConstants.cursorSizeNS;
                } else if (vbrHitInfo.hitPeriod) {
                    Cursor = ViewConstants.cursorSizeWE;
                } else {
                    Cursor = ViewConstants.cursorHand;
                }
                return;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point.Position);
            if (noteHitInfo.hitResizeArea) {
                Cursor = ViewConstants.cursorSizeWE;
                return;
            }
            if (!noteHitInfo.hitBody && (ViewModel.EditTool.CurrentTool == EditTools.CursorTool || args.KeyModifiers == cmdKey)) {
                Cursor = ViewConstants.cursorCross;
                return;
            }
            Cursor = null;
        }

        public void NotesCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.shiftHeld = args.KeyModifiers == KeyModifiers.Shift;
            editState.ctrlHeld = args.KeyModifiers == cmdKey;
            editState.altHeld = args.KeyModifiers == KeyModifiers.Alt;
            editState.Update(point.Pointer, point.Position);

            // Two-phase handling for S-curve and Sine wave tools:
            // On first mouse up, transition to adjusting phase instead of ending.
            if (editState is PitchCurveState pcs) {
                if (pcs.Mode == PitchCurveState.CurveMode.Line) {
                    // Line tool: apply immediately on mouse up
                    pcs.Apply();
                    pcs.End(pointer: args.Pointer, point: point.Position);
                } else {
                    // S-curve / Sine: transition to adjusting phase, keep pointer captured
                    if (!pcs.TransitionToAdjusting(point.Position)) {
                        // TransitionToAdjusting returned false (click without drag) — already cancelled
                        editState = null;
                        return;
                    }
                    return;
                }
            } else {
                editState.End(point.Pointer, point.Position);
            }
            editState = null;
            Cursor = null;
        }

        public void NotesCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (!(sender is Control control)) {
                return;
            }
            var point = args.GetPosition(control);
            if (editState != null) {
                editState.End(args.Pointer, point);
                editState = null;
                Cursor = null;
            }
            var noteHitInfo = ViewModel.NotesViewModel.HitTest.HitTestNote(point);
            if (noteHitInfo.hitBody && ViewModel?.NotesViewModel?.Part != null) {
                var note = noteHitInfo.note;
                LyricBox?.Show(ViewModel.NotesViewModel.Part, new LyricBoxNote(note), note.lyric);
            }
        }

        public void NotesCanvasPointerWheelChanged(object sender, PointerWheelEventArgs args) {
            LyricBox?.EndEdit();
            var control = (Control)sender;
            var position = args.GetCurrentPoint(control).Position;
            var size = control.Bounds.Size;
            var delta = args.Delta;
            if (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift) {
                if (args.KeyModifiers == KeyModifiers.Shift) {
                    delta = new Vector(delta.Y, delta.X);
                }
                if (delta.X != 0) {
                    HScrollBar.Value = Math.Max(HScrollBar.Minimum,
                        Math.Min(HScrollBar.Maximum, HScrollBar.Value - HScrollBar.SmallChange * delta.X));
                }
                if (delta.Y != 0) {
                    VScrollBar.Value = Math.Max(VScrollBar.Minimum,
                        Math.Min(VScrollBar.Maximum, VScrollBar.Value - VScrollBar.SmallChange * delta.Y));
                }
            } else if (args.KeyModifiers == KeyModifiers.Alt) {
                position = position.WithX(position.X / size.Width).WithY(position.Y / size.Height);
                ViewModel.NotesViewModel.OnYZoomed(position, 0.1 * args.Delta.Y);
            } else if (args.KeyModifiers == cmdKey) {
                TimelinePointerWheelChanged(TimelineCanvas, args);
            }
            if (editState != null) {
                var point = args.GetCurrentPoint(editState.control);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void NotesContextMenuOpening(object sender, CancelEventArgs args) {
            if (shouldOpenNotesContextMenu) {
                shouldOpenNotesContextMenu = false;
            } else {
                args.Cancel = true;
            }
        }

        public void ExpCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            var notesVm = ViewModel.NotesViewModel;
            if (notesVm.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            var track = notesVm.Project.tracks[notesVm.Part.trackNo];
            if (!track.TryGetExpDescriptor(notesVm.Project, notesVm.PrimaryKey, out var descriptor)) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                if (descriptor.type == UExpressionType.Curve) {
                    switch (ViewModel.CurveViewModel.CurveTool) {
                        case CurveTools.CurveSelectTool:
                            editState = new CurveSelectionState(control, ViewModel, this, descriptor);
                            break;
                        case CurveTools.CurvePenTool:
                            ViewModel.CurveViewModel.ClearSelect();
                            editState = new ExpSetValueState(control, ViewModel, this, descriptor);
                            break;
                        case CurveTools.CurveEraserTool:
                            ViewModel.CurveViewModel.ClearSelect();
                            editState = new ExpResetValueState(control, ViewModel, this, descriptor, MouseButton.Left);
                            break;
                        default:
                            ViewModel.CurveViewModel.ClearSelect();
                            break;
                    }
                } else {
                    editState = new ExpSetValueState(control, ViewModel, this, descriptor);
                }
                Cursor = null;
            } else if (point.Properties.IsRightButtonPressed) {
                if (descriptor.type == UExpressionType.Curve && ViewModel.CurveViewModel.CurveTool == CurveTools.CurveSelectTool) {
                    ViewModel.CurveViewModel.ClearSelect();
                } else {
                    ViewModel.CurveViewModel.ClearSelect();
                    editState = new ExpResetValueState(control, ViewModel, this, descriptor);
                }
                Cursor = ViewConstants.cursorNo;
            }
            if (editState != null) {
                editState.ctrlShiftHeld = args.KeyModifiers == (cmdKey | KeyModifiers.Shift);
                editState.shiftHeld = args.KeyModifiers == KeyModifiers.Shift;
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void ExpCanvasPointerMoved(object sender, PointerEventArgs args) {
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            args.Handled = true;
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            if (editState != null) {
                editState.ctrlShiftHeld = args.KeyModifiers == (cmdKey | KeyModifiers.Shift);
                editState.shiftHeld = args.KeyModifiers == KeyModifiers.Shift;
                editState.Update(point.Pointer, point.Position);
            } else {
                Cursor = null;
            }
        }

        public void ExpCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.ctrlShiftHeld = args.KeyModifiers == (cmdKey | KeyModifiers.Shift);
            editState.shiftHeld = args.KeyModifiers == KeyModifiers.Shift;
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void PhonemeCanvasDoubleTapped(object sender, TappedEventArgs args) {
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            if (sender is not Control control) {
                return;
            }
            var point = args.GetPosition(control);
            if (editState != null) {
                editState.End(args.Pointer, point);
                editState = null;
                Cursor = null;
            }
            var hitInfoAlias = ViewModel.NotesViewModel.HitTest.HitTestAlias(point);
            var phoneme = hitInfoAlias.phoneme;
            Log.Debug($"PhonemeCanvasDoubleTapped, hit = {hitInfoAlias.hit}, point = {{{hitInfoAlias.point}}}, phoneme = {phoneme?.phoneme}");
            if (hitInfoAlias.hit) {
                LyricBox?.Show(ViewModel.NotesViewModel.Part, new LyricBoxPhoneme(phoneme!), phoneme!.phoneme);
                return;
            }
        }

        public async void PhonemeCanvasPointerPressed(object sender, PointerPressedEventArgs args) {
            LyricBox?.EndEdit();
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                return;
            }
            if (point.Properties.IsLeftButtonPressed) {
                if (args.KeyModifiers == cmdKey) {
                    var hitAliasInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(args.GetPosition(control));
                    if (hitAliasInfo.hit) {
                        var singer = ViewModel.NotesViewModel.Project.tracks[ViewModel.NotesViewModel.Part.trackNo].Singer;
                        if (Preferences.Default.OtoEditor == 1 && !string.IsNullOrEmpty(Preferences.Default.VLabelerPath)) {
                            Integrations.VLabelerClient.Inst.GotoOto(singer, hitAliasInfo.phoneme.oto);
                        } else {
                            if (MainWindow != null) {
                                await MainWindow.OpenSingersWindowAsync();
                            }
                            RootWindow.Activate();
                            DocManager.Inst.ExecuteCmd(new GotoOtoNotification(singer, hitAliasInfo.phoneme.oto));
                        }
                        return;
                    }
                }
                // Plain click on errored phoneme alias shows error details
                var clickAliasInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(args.GetPosition(control));
                if (clickAliasInfo.hit && clickAliasInfo.phoneme.Error && clickAliasInfo.phoneme.ErrorException != null) {
                    _ = MessageBox.ShowError(RootWindow, clickAliasInfo.phoneme.ErrorException);
                    return;
                }
                var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
                if (hitInfo.hit) {
                    var phoneme = hitInfo.phoneme;
                    var note = phoneme.Parent;
                    var index = phoneme.index;
                    if (hitInfo.hitPosition) {
                        editState = new PhonemeMoveState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitPreutter) {
                        editState = new PhonemeChangePreutterState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitOverlap) {
                        if (phoneme.Next == null || !phoneme.Next.adjacent) {
                            return;
                        }
                        phoneme = hitInfo.phoneme.Next;
                        note = phoneme.Parent;
                        index = phoneme.index;
                        editState = new PhonemeChangeOverlapState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitAttackTime) {
                        editState = new PhonemeChangeAttackTimeState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    } else if (hitInfo.hitReleaseTime) {
                        editState = new PhonemeChangeReleaseTimeState(
                            control, ViewModel, this, note.Extends ?? note, phoneme, index);
                    }
                }
            } else if (point.Properties.IsRightButtonPressed) {
                editState = new PhonemeResetState(control, ViewModel, this);
                Cursor = ViewConstants.cursorNo;
            }
            if (editState != null) {
                editState.Begin(point.Pointer, point.Position);
                editState.Update(point.Pointer, point.Position);
            }
        }

        public void PhonemeCanvasPointerMoved(object sender, PointerEventArgs args) {
            args.Handled = true;
            if (ViewModel?.NotesViewModel?.Part == null) {
                return;
            }
            if (ValueTipCanvas != null) {
                valueTipPointerPosition = args.GetCurrentPoint(ValueTipCanvas!).Position;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            if (editState != null) {
                editState.Update(point.Pointer, point.Position);
                return;
            }
            var aliasHitInfo = ViewModel.NotesViewModel.HitTest.HitTestAlias(point.Position);
            if (aliasHitInfo.hit) {
                ViewModel.MouseoverPhoneme(aliasHitInfo.phoneme);
                Cursor = null;
                return;
            }
            var hitInfo = ViewModel.NotesViewModel.HitTest.HitTestPhoneme(point.Position);
            if (hitInfo.hitPosition || hitInfo.hitPreutter || hitInfo.hitOverlap || hitInfo.hitAttackTime || hitInfo.hitReleaseTime) {
                Cursor = ViewConstants.cursorSizeWE;
                ViewModel.MouseoverPhoneme(null);
                return;
            }
            ViewModel.MouseoverPhoneme(null);
            Cursor = null;
        }

        public void PhonemeCanvasPointerReleased(object sender, PointerReleasedEventArgs args) {
            if (editState == null) {
                return;
            }
            if (editState.MouseButton != args.InitialPressMouseButton) {
                return;
            }
            var control = (Control)sender;
            var point = args.GetCurrentPoint(control);
            editState.Update(point.Pointer, point.Position);
            editState.End(point.Pointer, point.Position);
            editState = null;
            Cursor = null;
        }

        public void BackgroundPointerMoved(object sender, PointerEventArgs args) {
            Cursor = null;
            args.Handled = true;
        }

        public void OnSnapDivMenuButton(object sender, RoutedEventArgs args) {
            SnapDivMenu.PlacementTarget = sender as Button;
            SnapDivMenu.Open();
        }

        void OnSnapDivKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) {
                if (sender is ContextMenu menu && menu.SelectedItem is MenuItemViewModel item) {
                    item.Command?.Execute(item.CommandParameter);
                }
            }
        }

        public void OnKeyMenuButton(object sender, RoutedEventArgs args) {
            KeyMenu.PlacementTarget = sender as Button;
            KeyMenu.Open();
        }

        bool MoveToNextPart(bool next) {
            var notesVm = ViewModel.NotesViewModel;
            var playVm = ViewModel.PlaybackViewModel;
            if (notesVm?.Part == null || playVm == null) {
                return false;
            }
            // tick is the center of NotesCanvas
            var tick = (int)(notesVm.TickOffset + notesVm.Bounds.Width / notesVm.TickWidth / 2 + notesVm.Part.position);
            var parts = notesVm.Project.parts
                .Where(part => part is UVoicePart && part.position <= tick && tick <= part.End)
                .OfType<UVoicePart>()
                .OrderBy(part => part.trackNo)
                .ThenBy(part => part.position)
                .ToArray();
            if (parts.Length == 0) {
                return false;
            }
            var index = Array.IndexOf(parts, notesVm.Part);
            index = next ? index + 1 : index - 1;
            if (parts.Length <= index) {
                index = 0;
            } else if (index < 0) {
                index = parts.Length - 1;
            }
            DocManager.Inst.ExecuteCmd(new LoadPartNotification(parts[index], notesVm.Project, tick));
            AttachExpressions();
            return true;
        }

        void OnKeyKeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) {
                if (sender is ContextMenu menu && menu.SelectedItem is MenuItemViewModel item) {
                    item.Command?.Execute(item.CommandParameter);
                }
            }
        }

        #region value tip

        void IValueTip.ShowValueTip() {
            if (ValueTip != null) {
                ValueTip.IsVisible = true;
            }
        }

        void IValueTip.HideValueTip() {
            if (ValueTip != null) {
                ValueTip.IsVisible = false;
            }
            if (ValueTipText != null) {
                ValueTipText.Text = string.Empty;
            }
        }

        void IValueTip.UpdateValueTip(string text) {
            if (ValueTip == null || ValueTipText == null || ValueTipCanvas == null) {
                return;
            }
            ValueTipText.Text = text;
            Canvas.SetLeft(ValueTip, valueTipPointerPosition.X);
            double tipY = valueTipPointerPosition.Y + 21;
            if (tipY + 21 > ValueTipCanvas!.Bounds.Height) {
                tipY = tipY - 42;
            }
            Canvas.SetTop(ValueTip, tipY);
        }

        #endregion

        void OnKeyDown(object? sender, KeyEventArgs args) {
            var notesVm = ViewModel.NotesViewModel;
            if (notesVm.Part == null) {
                args.Handled = false;
                return;
            }

            if (RootWindow.FocusManager != null) {
                if (RootWindow.FocusManager.GetFocusedElement() is TextBox focusedTextBox) {
                    if (focusedTextBox.IsEnabled && focusedTextBox.IsEffectivelyVisible && focusedTextBox.IsFocused) {
                        args.Handled = false;
                        return;
                    }
                } else if (RootWindow.FocusManager.GetFocusedElement() is ComboBox or ComboBoxItem) {
                    args.Handled = false;
                    return;
                }
            }
            if (LyricBox.IsVisible) {
                args.Handled = false;
                return;
            }

            if (args.Key == Key.R && args.KeyModifiers == KeyModifiers.Control) {
                var project = DocManager.Inst.Project;
                var part = notesVm.Part;
                var selectedNotes = notesVm.Selection.ToList();

                if (part != null && selectedNotes.Count > 0) {
                    noteBatchEditCommand?.Execute(new LoadRenderedPitch()).Subscribe();
                }

                args.Handled = true;
                return;
            }

            // returns true if handled
            args.Handled = OnKeyExtendedHandler(args);
        }

        bool OnKeyExtendedHandler(KeyEventArgs args) {
            var notesVm = ViewModel.NotesViewModel;
            var playVm = ViewModel.PlaybackViewModel;
            var curveVm = ViewModel.CurveViewModel;
            if (notesVm?.Part == null || playVm == null || curveVm == null) {
                return false;
            }
            var project = DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / notesVm.SnapDiv;
            int deltaTicks = notesVm.IsSnapOn ? snapUnit : 15;

            bool isNone = args.KeyModifiers == KeyModifiers.None;
            bool isAlt = args.KeyModifiers == KeyModifiers.Alt;
            bool isCtrl = args.KeyModifiers == cmdKey;
            bool isShift = args.KeyModifiers == KeyModifiers.Shift;
            bool isBoth = args.KeyModifiers == (cmdKey | KeyModifiers.Shift);

            if (PluginMenu.IsSubMenuOpen && isNone) {
                if (ViewModel.LegacyPluginShortcuts.ContainsKey(args.Key)) {
                    var plugin = ViewModel.LegacyPluginShortcuts[args.Key];
                    if (plugin != null && plugin.Command != null) {
                        plugin.Command.Execute(plugin.CommandParameter);
                    }
                }
                return true;
            }

            string? action = KeyTranslator.GetActionIdFromKey(args.Key, args.KeyModifiers);
            if (action == null) return false;

            switch (action) {
                // Playback & Selection
                case "PlayOrPause": playVm.PlayOrPause(); return true;
                case "PlaySelection":
                    if (!notesVm.Selection.IsEmpty) {
                        playVm.PlayOrPause(
                            tick: notesVm.Part.position + notesVm.Selection.FirstOrDefault()!.position,
                            endTick: notesVm.Part.position + notesVm.Selection.LastOrDefault()!.RightBound
                        );
                    }
                    return true;
                case "ClearSelection":
                    var numSelected = notesVm.Selection.Count;
                    if (numSelected == 1 || numSelected == notesVm.Part.notes.Count) notesVm.DeselectNotes();
                    else if (numSelected > 1) notesVm.SelectNote(notesVm.Selection.Head!);
                    return true;
                case "SelectAll": notesVm.SelectAllNotes(); return true;
                case "DeselectAll": notesVm.DeselectNotes(); return true;

                // UI & Windows
                case "CloseWindow": if (RootWindow is PianoRollDetachedWindow) RootWindow.Hide(); return true;
                case "menu.tools.fullscreen": OnMenuFullScreen(this, new RoutedEventArgs()); return true;
                case "OpenPluginMenu": if (PluginMenu.Parent is MenuItem batch) { batch.Open(); PluginMenu.Open(); } return true;

                // Lyrics
                case "EditLyrics":
                    if (LyricBox != null && LyricBox.IsVisible) {
                        return false; 
                    }

                    if (notesVm.Selection.Count == 1) {
                        var note = notesVm.Selection.First();
                        LyricBox?.Show(ViewModel.NotesViewModel.Part!, new LyricBoxNote(note), note.lyric);
                    } else if (notesVm.Selection.Count > 1) {
                        EditLyrics();
                    }
                    return true;

                // Tools
                case "ToolSelect1": ViewModel.ToolIndex = 0; return true;
                case "ToolSelect2Main": ViewModel.ToolIndex = 1; ViewModel.PenToolIndex = 0; return true;
                case "ToolSelect2Alt": ViewModel.ToolIndex = 1; ViewModel.PenToolIndex = 1; return true;
                case "ToolSelect3": ViewModel.ToolIndex = 2; return true;
                case "ToolSelect4Main": ViewModel.ToolIndex = 3; ViewModel.PitchOverwrite = false; return true;
                case "ToolSelect4Overwrite": ViewModel.ToolIndex = 3; ViewModel.PitchOverwrite = true; return true;
                case "ToolSelect4Line": ViewModel.ToolIndex = 4; ViewModel.PitchOverwrite = false; return true;
                case "ToolSelect4LineOverwrite": ViewModel.ToolIndex = 4; ViewModel.PitchOverwrite = true; return true;
                case "ToolSelectSCurve": ViewModel.ToolIndex = 5; ViewModel.PitchOverwrite = false; return true;
                case "ToolSelectSine": ViewModel.ToolIndex = 6; ViewModel.PitchOverwrite = false; return true;
                case "ToolSelectSmoothen": ViewModel.ToolIndex = 7; ViewModel.PitchOverwrite = false; return true;
                case "ToolSelect5": ViewModel.ToolIndex = 8; return true;

                // Expressions
                case "ExpSelect1": expSelector1?.SelectExp(); return true;
                case "ExpSelect2": expSelector2?.SelectExp(); return true;
                case "ExpSelect3": expSelector3?.SelectExp(); return true;
                case "ExpSelect4": expSelector4?.SelectExp(); return true;
                case "ExpSelect5": expSelector5?.SelectExp(); return true;
                case "ExpSelect6": expSelector6?.SelectExp(); return true;
                case "ExpSelect7": expSelector7?.SelectExp(); return true;
                case "ExpSelect8": expSelector8?.SelectExp(); return true;
                case "ExpSelect9": expSelector9?.SelectExp(); return true;
                case "ExpSelect10": expSelector10?.SelectExp(); return true;

                // Toggles
                case "ToggleFinalPitch": notesVm.ShowFinalPitch = !notesVm.ShowFinalPitch; return true;
                case "ToggleTips": notesVm.ShowTips = !notesVm.ShowTips; return true;
                case "ToggleVibrato": notesVm.ShowVibrato = !notesVm.ShowVibrato; return true;
                case "TogglePitch": notesVm.ShowPitch = !notesVm.ShowPitch; return true;
                case "TogglePhoneme": notesVm.ShowPhoneme = !notesVm.ShowPhoneme; return true;
                case "ToggleExpressions": notesVm.ShowExpressions = !notesVm.ShowExpressions; return true;
                case "ToggleSnap": notesVm.IsSnapOn = !notesVm.IsSnapOn; return true;
                case "OpenSnapMenu": SnapDivMenu.Open(); return true;
                case "ToggleNoteParams": notesVm.ShowNoteParams = !notesVm.ShowNoteParams; return true;
                case "TogglePlayTone": notesVm.PlayTone = !notesVm.PlayTone; return true;
                case "ToggleWaveform": notesVm.ShowWaveform = !notesVm.ShowWaveform; return true;

                // Transposition
                case "TransposeUp": notesVm.TransposeSelection(1); return true;
                case "pianoroll.menu.notes.octaveup": notesVm.TransposeSelection(12); return true;
                case "TransposeDown": notesVm.TransposeSelection(-1); return true;
                case "pianoroll.menu.notes.octavedown": notesVm.TransposeSelection(-12); return true;

                // Note Movement & Sizing
                case "MoveCursorLeft": notesVm.MoveCursor(-1); return true;
                case "ResizeNotesLeft": notesVm.ResizeSelectedNotes(-1 * deltaTicks); return true;
                case "MoveNotesLeft": notesVm.MoveSelectedNotes(-1 * deltaTicks); return true;
                case "ExtendSelectionLeft": notesVm.ExtendSelection(-1); return true;
                case "MoveCursorRight": notesVm.MoveCursor(1); return true;
                case "ResizeNotesRight": notesVm.ResizeSelectedNotes(deltaTicks); return true;
                case "MoveNotesRight": notesVm.MoveSelectedNotes(deltaTicks); return true;
                case "ExtendSelectionRight": notesVm.ExtendSelection(1); return true;

                // Edit Operations
                case "menu.edit.undo": ViewModel.Undo(); return true;
                case "menu.edit.redo": ViewModel.Redo(); return true;
                case "menu.edit.copy": ViewModel.Copy(); return true;
                case "menu.edit.cut": ViewModel.Cut(); return true;
                case "menu.edit.paste": ViewModel.Paste(); return true;
                case "PastePlain": notesVm.PastePlainNotes(); return true;
                case "PasteParameters": notesVm.PasteSelectedParams(RootWindow); return true;
                case "InsertNote": notesVm.InsertNote(); return true;
                case "menu.edit.delete": notesVm.DeleteSelectedNotes(); return true;
                case "MergeNotes": notesVm.MergeSelectedNotes(); return true;

                // Playhead & Timeline Navigation
                case "PlayheadHome": playVm.MovePlayPos(notesVm.Part.position); return true;
                case "SelectToStart": if (notesVm.Part.notes.FirstOrDefault() is UNote first) notesVm.ExtendSelection(first); return true;
                case "PlayheadEnd": playVm.MovePlayPos(notesVm.Part.End); HScrollBar.Value = HScrollBar.Maximum; return true;
                case "SelectToEnd": if (notesVm.Part.notes.LastOrDefault() is UNote last) notesVm.ExtendSelection(last); return true;
                
                case "PlayheadLeft": playVm.MovePlayPos(playVm.PlayPosTick - snapUnit); return true;
                case "PlayheadToSelectionStart": if (!notesVm.Selection.IsEmpty) playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.FirstOrDefault()!.position); return true;
                case "PlayheadToViewStart": playVm.MovePlayPos(notesVm.Part.position + (int)notesVm.TickOffset); return true;
                
                case "PlayheadRight": playVm.MovePlayPos(playVm.PlayPosTick + snapUnit); return true;
                case "PlayheadToSelectionEnd": if (!notesVm.Selection.IsEmpty) playVm.MovePlayPos(notesVm.Part.position + notesVm.Selection.LastOrDefault()!.RightBound); return true;
                case "PlayheadToViewEnd": playVm.MovePlayPos(notesVm.Part.position + (int)(notesVm.TickOffset + notesVm.Bounds.Width / notesVm.TickWidth)); return true;

                // Scrolling & Zooming
                case "ScrollLeft": notesVm.TickOffset = Math.Max(0, notesVm.TickOffset - snapUnit); return true;
                case "ScrollRight": notesVm.TickOffset = Math.Min(notesVm.TickOffset + snapUnit, notesVm.HScrollBarMax); return true;
                case "ScrollUp": notesVm.TrackOffset = Math.Max(notesVm.TrackOffset - 2, 0); return true;
                case "ScrollDown": notesVm.TrackOffset = Math.Min(notesVm.TrackOffset + 2, notesVm.VScrollBarMax); return true;
                case "ZoomIn":
                case "ZoomOut":
                    double x = 0, y = 0;
                    if (!notesVm.Selection.IsEmpty) {
                        x = (notesVm.Selection.Head!.position - notesVm.TickOffset) / notesVm.ViewportTicks;
                        y = (ViewConstants.MaxTone - 1 - notesVm.Selection.Head.tone - notesVm.TrackOffset) / notesVm.ViewportTracks;
                    } else if (notesVm.TickOffset != 0) { x = 0.5; y = 0.5; }
                    notesVm.OnXZoomed(new Point(x, y), action == "ZoomIn" ? 0.1 : -0.1);
                    return true;

                // Track & Project Operations
                case "SaveProject": _ = MainWindow?.Save(); return true;
                case "SoloTrack": MessageBus.Current.SendMessage(new TracksSoloEvent(notesVm.Part.trackNo, !project.tracks[notesVm.Part.trackNo].Solo, false)); return true;
                case "MuteTrack": MessageBus.Current.SendMessage(new TracksMuteEvent(notesVm.Part.trackNo, false)); return true;
                case "FocusSelection": 
                    if (notesVm.Selection.FirstOrDefault() is UNote focusNote) DocManager.Inst.ExecuteCmd(new FocusNoteNotification(notesVm.Part, focusNote)); 
                    return true;
                case "SearchNote": SearchNote(); return true;
                case "MoveToNextPart": MoveToNextPart(true); return true;
                case "MoveToPrevPart": MoveToNextPart(false); return true;

                // others
                case "Quantize Notes": QuantizeNotes(); return true;
                case "lyricsreplace.replace": ReplaceLyrics(); return true;
                case "Randomize Tuning": RandomizeTuning(); return true;
                case "Lengthen Crossfade": LengthenCrossfade(); return true;
                case "Add Breath": AddBreathNote(); return true; 
                case "Edit Note Defaults": EditNoteDefaults(); return true;
                case "Open Singers Window": OnMenuSingers(this, new RoutedEventArgs()); return true;
                case "Open Expressions": OnExpButtonClick(this, new RoutedEventArgs()); return true;
                case "Lock Pitch Points": OnMenuLockPitchPoints(this, new RoutedEventArgs()); return true;
                case "Lock Vibrato": OnMenuLockVibrato(this, new RoutedEventArgs()); return true;
                case "Lock Expressions": OnMenuLockExpressions(this, new RoutedEventArgs()); return true;
                case "Show Portrait": OnMenuShowPortrait(this, new RoutedEventArgs()); return true;
                case "Show Icon": OnMenuShowIcon(this, new RoutedEventArgs()); return true;
                case "Show Ghost Notes": OnMenuShowGhostNotes(this, new RoutedEventArgs()); return true;
                case "Use Track Color": OnMenuUseTrackColor(this, new RoutedEventArgs()); return true;
                case "Detach Piano Roll": OnMenuDetachPianoRoll(this, new RoutedEventArgs()); return true;
                case "Hide Piano Roll": OnMenuHidePianoRoll(this, new RoutedEventArgs()); return true;
            }

            // External and batch note edits
            var allDynamicMenus = ViewModel.NoteBatchEdits
                .Concat(ViewModel.LyricBatchEdits)
                .Concat(ViewModel.ResetBatchEdits)
                .Concat(ViewModel.ExternalBatchEdits);
            foreach (var menu in allDynamicMenus) {
                if (menu.CommandParameter is BatchEdit edit && edit.Name == action) {
                    menu.Command?.Execute(edit);
                    return true;
                }
            }
            // Legacy plugins
            foreach (var menu in ViewModel.LegacyPlugins) {
                if (menu.Header?.ToString() == action) {
                    menu.Command?.Execute(menu.CommandParameter);
                    return true;
                }
            }
            return false;
        }

        public void AttachExpressions() {
            if (expSelector1 == null) {
                return;
            }
            var exps = new ExpSelector[] { expSelector1, expSelector2, expSelector3, expSelector4, expSelector5, expSelector6, expSelector7, expSelector8, expSelector9, expSelector10 };
            exps[DocManager.Inst.Project.expSecondary].SelectExp();
            exps[DocManager.Inst.Project.expPrimary].SelectExp();
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadingNotification loadingNotif && loadingNotif.window == typeof(PianoRoll)) {
                if (loadingNotif.startLoading) {
                    LoadingWindow.BeginLoadingImmediate(RootWindow);
                } else {
                    LoadingWindow.EndLoading();
                }
            }
        }
    }
}