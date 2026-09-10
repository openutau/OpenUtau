using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using OpenUtau.App.Views;
using OpenUtau.Core;

namespace OpenUtau.App.Controls {
    public static class ToastControl {
        public static Notification GetNotification(ToastNotification notif, Window window) {
            var message = ThemeManager.GetString(notif.translationKey);
            var type = NotificationType.Warning;
            switch (notif.type) {
                case "Information":
                    type = NotificationType.Information;
                    break;
                case "Error":
                    type = NotificationType.Error;
                    break;
                case "Success":
                    type = NotificationType.Success;
                    break;
                default:
                    break;
            };
            Action? action = null;
            if (notif.e != null) {
                message += $"\n{ThemeManager.GetString("errors.toast.details")}";
                action = new Action(() => {
                    MessageBox.ShowError(window, notif.e);
                });
            }
            return new Notification(
                        notif.title,
                        message,
                        type,
                        notif.durationSec == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(notif.durationSec),
                        action);
        }
    }
}
