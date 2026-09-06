using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Views {
    public partial class PianoRollDetachedWindow : Window {
        private readonly PianoRoll pianoRoll;
        private bool forceClose;
        private WindowNotificationManager notificationManager;

        public PianoRollDetachedWindow(PianoRoll pianoRoll) {
            InitializeComponent();
            this.pianoRoll = pianoRoll;
            DataContext = pianoRoll.DataContext;

            PianoRollContainer.Content = pianoRoll;

            if (Preferences.Default.PianorollWindowSize.TryGetPosition(out int x, out int y)) {
                Position = new PixelPoint(x, y);
            }
            WindowState = (WindowState)Preferences.Default.PianorollWindowSize.State;

            notificationManager = new WindowNotificationManager(this) {
                Position = NotificationPosition.BottomCenter,
                MaxItems = 3
            };
        }

        public void WindowGotFocus(object sender, FocusChangedEventArgs e) {
            if (e.Source is PianoRollDetachedWindow) {
                pianoRoll.Focus();
            }
        }

        public void WindowClosing(object? sender, WindowClosingEventArgs e) {
            Preferences.Default.PianorollWindowSize.Set(Width, Height, Position.X, Position.Y, (int)WindowState);
            Preferences.Save();
            Hide();
            e.Cancel = !forceClose;
        }

        public void WindowDeactivated(object sender, EventArgs args) {
            pianoRoll.LyricBox?.EndEdit();
        }

        public void ForceClose() {
            PianoRollContainer.Content = null;
            forceClose = true;
            Close();
        }

        public bool Toast(ToastNotification toast) {
            if (this.IsActive) {
                notificationManager.Show(ToastControl.GetNotification(toast, this));
                return true;
            }
            return false;
        }
    }
}
