using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    public partial class TrackHeader : UserControl, IDisposable {
        public static readonly DirectProperty<TrackHeader, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackHeader, Point> OffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, Point>(
                nameof(Offset),
                o => o.Offset,
                (o, v) => o.Offset = v);
        public static readonly DirectProperty<TrackHeader, int> TrackNoProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, int>(
                nameof(TrackNo),
                o => o.TrackNo,
                (o, v) => o.TrackNo = v);

        public double TrackHeight {
            get => trackHeight;
            set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public Point Offset {
            get => offset;
            set => SetAndRaise(OffsetProperty, ref offset, value);
        }
        public int TrackNo {
            get => trackNo;
            set => SetAndRaise(TrackNoProperty, ref trackNo, value);
        }

        private double trackHeight;
        private Point offset;
        private int trackNo;
        private readonly KeyModifiers cmdKey =
            OS.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        public TrackHeaderViewModel? ViewModel;

        private List<IDisposable> unbinds = new List<IDisposable>();

        private UTrack? track;
        private TrackHeaderCanvas? canvas;

        public TrackHeader() {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == OffsetProperty ||
                change.Property == TrackNoProperty ||
                change.Property == TrackHeightProperty) {
                SetPosition();
            }
        }

        internal void Bind(UTrack track, TrackHeaderCanvas canvas) {
            this.track = track;
            this.canvas = canvas;
            unbinds.Add(this.Bind(TrackHeightProperty, canvas.GetObservable(TrackHeaderCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(HeightProperty, canvas.GetObservable(TrackHeaderCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(OffsetProperty, canvas.WhenAnyValue(x => x.TrackOffset, trackOffset => new Point(0, -trackOffset * TrackHeight))));
            SetPosition();
        }

        private void SetPosition() {
            Canvas.SetLeft(this, 0);
            Canvas.SetTop(this, Offset.Y + (track?.TrackNo ?? 0) * trackHeight);
            if (ViewModel != null) {
                ViewModel.IsSingerVisible = trackHeight >= ViewConstants.TrackHeightDelta * 3;
                ViewModel.IsPhonemizerVisible = trackHeight >= ViewConstants.TrackHeightDelta * 4;
                ViewModel.IsRendererVisible = trackHeight >= ViewConstants.TrackHeightDelta * 5;
            }
        }

        void HeaderPointerPressed(object? sender, PointerPressedEventArgs args) {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
                track == null ||
                canvas?.DataContext is not TracksViewModel tracksViewModel) {
                return;
            }
            if (args.KeyModifiers == KeyModifiers.Shift) {
                tracksViewModel.SelectTracksUntil(track);
            } else if (args.KeyModifiers == cmdKey) {
                tracksViewModel.ToggleSelectTrack(track);
            } else {
                tracksViewModel.SelectTrack(track);
            }
        }

        void TrackNameButtonClicked(object sender, RoutedEventArgs args) {
            ViewModel?.Rename();
            args.Handled = true;
        }

        async void SingerButtonClicked(object sender, RoutedEventArgs args) {
            args.Handled = true;
            if (SingerManager.Inst.Singers.Count > 0) {
                if (ViewModel != null) {
                    await ViewModel.RefreshSingersAsync();
                }
                SingersMenu.Open();
            } else {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("There is no singer."));
            }
        }

        void SingerButtonContextRequested(object sender, ContextRequestedEventArgs args) {
            args.Handled = true;
        }

        void PhonemizerButtonClicked(object sender, RoutedEventArgs args) {
            ViewModel?.RefreshPhonemizers();
            PhonemizersMenu.Open((Control)sender);
            args.Handled = true;
        }

        void PhonemizerButtonContextRequested(object sender, ContextRequestedEventArgs args) {
            args.Handled = true;
        }

        void RendererButtonClicked(object sender, RoutedEventArgs args) {
            ViewModel?.RefreshRenderers();
            if (ViewModel?.RenderersMenuItems?.Count > 0) {
                RenderersMenu.Open((Control)sender);
            }
            args.Handled = true;
        }

        void RendererButtonContextRequested(object sender, ContextRequestedEventArgs args) {
            args.Handled = true;
        }

        void VolumeFaderPointerPressed(object sender, PointerPressedEventArgs args) {
            if (args.GetCurrentPoint((Visual?)sender).Properties.IsRightButtonPressed && ViewModel != null) {
                ViewModel.Volume = 0;
                args.Handled = true;
            }
        }

        void PanFaderPointerPressed(object sender, PointerPressedEventArgs args) {
            if (args.GetCurrentPoint((Visual?)sender).Properties.IsRightButtonPressed && ViewModel != null) {
                ViewModel.Pan = 0;
                args.Handled = true;
            }
        }

        void VolumeFaderContextRequested(object sender, ContextRequestedEventArgs args) {
            if (ViewModel != null) {
                ViewModel.Volume = 0;
            }
            args.Handled = true;
        }

        void PanFaderContextRequested(object sender, ContextRequestedEventArgs args) {
            if (ViewModel != null) {
                ViewModel.Pan = 0;
            }
            args.Handled = true;
        }

        async void TrackSettingsButtonClicked(object sender, RoutedEventArgs args) {
            if (track?.Singer != null && track.Singer.Found) {
                if (TopLevel.GetTopLevel(this) is Window window) {
                    var dialog = new Views.TrackSettingsDialog(track);
                    await dialog.ShowDialog(window);
                }
            }
        }

        void VolumePointerPressed(object sender, PointerPressedEventArgs args) {
            if (args.ClickCount == 2 && ViewModel != null) {
                ActivateVolumeOrPanTextBox(
                    VolumeTextBox, string.Format("{0:0.0}", ViewModel.Volume));
                args.Handled = true;
                args.Pointer.Capture(sender as IInputElement);
            }
        }
        void PanPointerPressed(object sender, PointerPressedEventArgs args) {
            if (args.ClickCount == 2 && ViewModel != null) {
                ActivateVolumeOrPanTextBox(
                    PanTextBox, string.Format("{0:0}", ViewModel.Pan));
                args.Handled = true;
            }
        }
        void VolumeOrPanTextBoxKeyDown(object sender, KeyEventArgs args) {
            if (args.Key == Key.Enter) {
                FinishVolumeOrPanInput(sender, true);
                args.Handled = true;
            } else if (args.Key == Key.Escape) {
                FinishVolumeOrPanInput(sender, false);
                args.Handled = true;
            }
        }
        void VolumeOrPanTextBoxLostFocus(object sender, FocusChangedEventArgs args) {
            FinishVolumeOrPanInput(sender, true);
            args.Handled = true;
        }
        void VolumeOrPanSliderValueChanged(object sender, RangeBaseValueChangedEventArgs args) {
            VolumeTextBox.IsVisible = false;
            VolumeTextBox.IsEnabled = false;
            PanTextBox.IsVisible = false;
            PanTextBox.IsEnabled = false;
        }

        void ActivateVolumeOrPanTextBox(TextBox textBox, string text) {
            textBox.Text = text;
            textBox.IsEnabled = true;
            textBox.IsVisible = true;
            textBox.Focus();
        }

        private void MoveHandleScrolled(object? sender, PointerWheelEventArgs e) {
            Dispatcher.UIThread.Post(() => MoveHandleMoved(sender, e));
        }

        private void MoveHandlePressed(object? sender, PointerPressedEventArgs e) {
            var control = sender as Control;
            var pointer = e.GetCurrentPoint(control);
            if (control != null && pointer.Properties.IsLeftButtonPressed) {
                e.Pointer.Capture(control);
            }
        }

        private void MoveHandleReleased(object? sender, PointerReleasedEventArgs e) {
            if (e.InitialPressMouseButton == MouseButton.Left && canvas != null) {
                Cursor = null;
                Point point = e.GetPosition(canvas);
                int tracksMaxIdx = DocManager.Inst.Project.tracks.Count - 1;
                double sizeHidden = Math.Abs(Offset.Y);
                if (track != null) {
                    bool isAboveCenter = Canvas.GetTop(this) + TrackHeight / 2 > point.Y;
                    int position = (int) Math.Round((sizeHidden + point.Y - (isAboveCenter ? 0 : TrackHeight)) / TrackHeight);
                    int idx = Math.Clamp(position, 0, tracksMaxIdx);

                    if (track.TrackNo != idx) {
                        DocManager.Inst.StartUndoGroup("command.track.order");
                        DocManager.Inst.ExecuteCmd(new SetTrackNoCommand(DocManager.Inst.Project, track, idx));
                        DocManager.Inst.EndUndoGroup();
                    }

                }
                if (canvas.TrackMover != null) {
                    canvas.TrackMover.IsVisible = false;
                }
            }
            e.Pointer.Capture(null);
        }

        private void MoveHandleMoved(object? sender, PointerEventArgs e) {
            var control = sender as Control;
            var pointer = e.GetCurrentPoint(control);
            if (pointer.Properties.IsLeftButtonPressed && e.Pointer.Captured == control && canvas?.TrackMover != null) {
                canvas.TrackMover.IsVisible = true;
                Cursor = ViewConstants.cursorSizeNS;
                double maxHeight = DocManager.Inst.Project.tracks.Count * TrackHeight;
                Point point = e.GetPosition(canvas);
                double offset = Math.Abs(Offset.Y);
                double leftOver = offset % TrackHeight;
                if (track != null) {
                    double position = Math.Round((point.Y + leftOver) / TrackHeight) * TrackHeight;
                    double finalPos = Math.Clamp(position - leftOver, -leftOver, maxHeight - offset);
                    Canvas.SetTop(canvas.TrackMover, finalPos);
                }
            }
        }

        private void FinishVolumeOrPanInput(object sender, bool commit) {
            if (sender == VolumeTextBox) {
                if (!VolumeTextBox.IsVisible) {
                    return; // Avoid double commit.
                }
                VolumeTextBox.IsVisible = false;
                VolumeTextBox.IsEnabled = false;
                if (commit && double.TryParse(VolumeTextBox.Text, out var volume) && ViewModel != null) {
                    ViewModel.Volume = Math.Clamp(volume, VolumeSlider.Minimum, VolumeSlider.Maximum);
                }
            } else {
                if (!PanTextBox.IsVisible) {
                    return; // Avoid double commit.
                }
                PanTextBox.IsVisible = false;
                PanTextBox.IsEnabled = false;
                if (commit && double.TryParse(PanTextBox.Text, out var pan) && ViewModel != null) {
                    ViewModel.Pan = Math.Clamp(pan, PanSlider.Minimum, PanSlider.Maximum);
                }
            }
        }

        public void Dispose() {
            unbinds.ForEach(u => u.Dispose());
            unbinds.Clear();
        }
    }
}
