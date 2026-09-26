using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    /// <summary>
    /// The project's or a track's expressions, and the expression graphs, in one window. It doesn't block the
    /// main window, so a graph can be edited while listening and watching the piano roll.
    /// </summary>
    public partial class ExpressionsDialog : Window, ICmdSubscriber {
        static ExpressionsDialog? open;

        readonly UTrack? track;
        bool applying;

        public ExpressionsDialog() : this(null) { }

        public ExpressionsDialog(UTrack? track) {
            InitializeComponent();
            this.track = track;
            DataContext = new ExpressionsViewModel(track);
            DocManager.Inst.AddSubscriber(this);
        }

        /// <summary>
        /// Opens the window for the project, or for a track's expressions, bringing it to the front if it's
        /// already open for the same one.
        /// </summary>
        public static ExpressionsDialog Open(Window owner, UTrack? track = null) {
            if (open != null && open.track == track) {
                open.Activate();
                return open;
            }
            open?.Close();
            var dialog = new ExpressionsDialog(track);
            open = dialog;
            dialog.Show(owner);
            if (dialog.Position.Y < 0) {
                dialog.Position = dialog.Position.WithY(0);
            }
            return dialog;
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            DocManager.Inst.RemoveSubscriber(this);
            if (open == this) {
                open = null;
            }
        }

        /// <summary>
        /// Starts over when the expressions change from elsewhere, e.g. by undo, since unapplied edits were made
        /// against the old ones. A track's window closes when another project is loaded.
        /// </summary>
        public void OnNext(UCommand cmd, bool isUndo) {
            if (applying) {
                return;
            }
            if (cmd is LoadProjectNotification && track != null) {
                Dispatcher.UIThread.Post(Close);
            } else if (cmd is ConfigureExpressionsCommand || cmd is LoadProjectNotification) {
                Dispatcher.UIThread.Post(() => DataContext = new ExpressionsViewModel(track));
            }
        }

        private void ApplyButtonClicked(object sender, RoutedEventArgs _) {
            // Stays open: new expressions are then available to the graphs.
            applying = true;
            try {
                (DataContext as ExpressionsViewModel)?.Apply();
            } catch (Exception e) {
                MessageBox.ShowError(this, e);
            } finally {
                applying = false;
            }
        }

        private void AddButtonClicked(object sender, RoutedEventArgs _) {
            var button = (Button)sender;
            var vm = DataContext as ExpressionsViewModel;
            if (vm != null) {
                vm.Add();
                if (vm.IsTrackOverride) {
                    button.ContextMenu?.Open();
                }
            }
        }
    }
}
