using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace OpenUtau.App.Views {
    /// <summary>
    /// A track's Track Polish rack.  It doesn't block the main window, so the
    /// song can be played and edited while the knobs are turned; every edit is
    /// heard live.  One window per track.
    /// </summary>
    public partial class MixFxDialog : Window, ICmdSubscriber {
        static readonly Dictionary<UTrack, MixFxDialog> open = new Dictionary<UTrack, MixFxDialog>();

        readonly MixFxViewModel viewModel;
        readonly UTrack? track;
        // False once the edits should stay: OK, or the track went away.
        bool revertOnClose = true;

        public MixFxDialog() : this(null) { }

        public MixFxDialog(UTrack? track) {
            InitializeComponent();
            this.track = track;
            DataContext = viewModel = new MixFxViewModel(track);
            viewModel.AskForName = PromptForNameAsync;
            // Keep the track header's FX indicator in step with the power switch.
            viewModel.WhenAnyValue(x => x.Enabled).Subscribe(_ => NotifyTrackHeader());
            if (track != null) {
                DocManager.Inst.AddSubscriber(this);
            }
        }

        /// <summary>Opens the rack for <paramref name="track"/>, or brings its open one to the front.</summary>
        public static void Open(Window owner, UTrack track) {
            if (open.TryGetValue(track, out var existing)) {
                existing.Activate();
                return;
            }
            var dialog = new MixFxDialog(track);
            open[track] = dialog;
            dialog.Show(owner);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (track == null) {
                return;
            }
            DocManager.Inst.RemoveSubscriber(this);
            if (open.TryGetValue(track, out var dialog) && dialog == this) {
                open.Remove(track);
            }
            // Edits are previewed live on the track; closing without OK
            // (Cancel, the title bar close button) undoes them.
            if (revertOnClose) {
                viewModel.Revert();
                NotifyTrackHeader();
            }
        }

        /// <summary>
        /// Closes when another project is loaded or the track is removed
        /// (including by undoing its addition), keeping the edits so undoing
        /// the removal brings the track back as it last sounded.  Follows
        /// renames.
        /// </summary>
        public void OnNext(UCommand cmd, bool isUndo) {
            if (track == null || !(cmd is TrackCommand || cmd is LoadProjectNotification)) {
                return;
            }
            Dispatcher.UIThread.Post(() => {
                if (!open.TryGetValue(track, out var dialog) || dialog != this) {
                    return;
                }
                if (cmd is LoadProjectNotification || !DocManager.Inst.Project.tracks.Contains(track)) {
                    revertOnClose = false;
                    Close();
                } else {
                    viewModel.TrackName = track.TrackName;
                }
            });
        }

        void NotifyTrackHeader() {
            if (track != null) {
                MessageBus.Current.SendMessage(new MixFxChangedNotification(track.TrackNo));
            }
        }

        Task<string?> PromptForNameAsync() {
            var tcs = new TaskCompletionSource<string?>();
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("mixfx.library.save");
            dialog.SetText(string.Empty);
            string? captured = null;
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name)) captured = name;
            };
            dialog.Closed += (_, __) => tcs.TrySetResult(captured);
            dialog.ShowDialog(this);
            return tcs.Task;
        }

        void OnOkClicked(object sender, RoutedEventArgs e) {
            revertOnClose = false;
            viewModel.Apply();
            NotifyTrackHeader();
            Close();
        }

        void OnCancelClicked(object sender, RoutedEventArgs e) {
            Close();
        }

        void OnApplyOnExportTapped(object? sender, TappedEventArgs e) {
            // The CheckBox handles its own clicks; this catches the label.
            if ((e.Source as Visual)?.FindAncestorOfType<CheckBox>(includeSelf: true) == null) {
                viewModel.ApplyOnExportMixdown = !viewModel.ApplyOnExportMixdown;
            }
        }
    }
}
