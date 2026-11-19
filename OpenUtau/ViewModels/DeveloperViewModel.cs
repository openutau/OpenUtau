using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace OpenUtau.App.ViewModels {
    public class LogEventConverter : IValueConverter {
        private ITextFormatter formater;
        private StringWriter stringWriter;
        public LogEventConverter() {
            const string template = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";
            formater = new MessageTemplateTextFormatter(template);
            stringWriter = new StringWriter();
        }
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is LogEvent logEvent) {
                formater.Format(logEvent, stringWriter);
                // The template ends with a new line before the exception, which most events don't have.
                string message = stringWriter.GetStringBuilder().ToString().TrimEnd();
                stringWriter.GetStringBuilder().Clear();
                return message;
            }
            return string.Empty;
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return new Avalonia.Data.BindingNotification(new NotImplementedException(), Avalonia.Data.BindingErrorType.Error);
        }
    }

    public partial class DeveloperViewModel : ViewModelBase {

        private DeveloperWindow? window;

        public void SetWindow(DeveloperWindow w) {
            window = w;
        }

        public DeveloperViewModel() {
            ReverseLogOrderCommand = ReactiveCommand.Create(() => { Sink.Inst.ReverseOrder(); });
            CopyLogCommand = ReactiveCommand.Create(() => {
                window?.CopyLogText();
            });
        }

        public class Sink : ILogEventSink {
            static Sink sink = new Sink();
            public static Sink Inst => sink;

            public LoggingLevelSwitch LevelSwitch =
                new LoggingLevelSwitch(LogEventLevel.Error);
            public ObservableCollectionExtended<LogEvent> LogEvents =
                new ObservableCollectionExtended<LogEvent>();

            private bool reverseLogOrder = Preferences.Default.ReverseLogOrder;

            public void ReverseOrder() {
                if (!reverseLogOrder) {
                    reverseLogOrder = true;
                    reverseOrder();
                } else {
                    reverseLogOrder = false;
                    reverseOrder();
                }
            }

            private void reverseOrder() {
                var t = LogEvents;
                var x = t.AsEnumerable().Reverse().ToArray();
                LogEvents.Clear();
                foreach (var i in x) {
                    LogEvents.Add(i);
                }
            }

            /// <summary>
            /// Allows you to set the reversal of the LogEvents to a specified value
            /// </summary>
            /// <param name="reversed">true = logevents are reversed, false = logevents are not reversed</param>
            public void ReverseOrder(bool reversed) {
                if (reversed) {
                    if (!reverseLogOrder) {
                        ReverseOrder();
                    }
                } else {
                    if (reverseLogOrder) {
                        ReverseOrder();
                    }
                }
            }

            public void Emit(LogEvent logEvent) {
                // Events come from any thread, and the developer window shows this collection.
                if (Avalonia.Application.Current != null && !Dispatcher.UIThread.CheckAccess()) {
                    Dispatcher.UIThread.Post(() => Emit(logEvent));
                    return;
                }
                if (reverseLogOrder) {
                    LogEvents.Insert(0, logEvent);
                } else {
                    LogEvents.Add(logEvent);
                }
            }

            /// <summary>
            /// make the debug string for copying to the clipboard
            /// </summary>
            public override string ToString() {
                var sb = new StringBuilder();
                foreach (var l in LogEvents) {
                    sb.AppendLine($"{l.Timestamp} : {l.Level} : {l.MessageTemplate.Text}");
                }

                return sb.ToString();
            }
        }

        /// <summary>The lowest level shown, as an index of <see cref="LogEventLevel"/>.</summary>
        [Reactive] public partial int LogEventLevelIndex { get; set; }
        /// <summary>Shows only the events whose message or error contains this, ignoring case.</summary>
        [Reactive] public partial string Filter { get; set; } = string.Empty;
        /// <summary>
        /// Reloads the packages in the Dependencies folder when a file in it changes, while the window is open.
        /// </summary>
        [Reactive] public partial bool LiveReload { get; set; }
        /// <summary>The events of <see cref="Sink"/> that pass the level and text filters.</summary>
        public ObservableCollectionExtended<LogEvent> LogEvents { get; } = new ObservableCollectionExtended<LogEvent>();
        public ReactiveCommand<RxVoid, RxVoid> ReverseLogOrderCommand { get; private set; }
        public ReactiveCommand<RxVoid, RxVoid> CopyLogCommand { get; private set; }

        public void ToggleLiveReload() {
            LiveReload = !LiveReload;
        }

        public void Clear() {
            Sink.Inst.LogEvents.Clear();
        }

        public void Attach() {
            Core.Util.ProcessRunner.DebugSwitch = true;
            Sink.Inst.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
            liveReloadSubscription = this.WhenAnyValue(x => x.LiveReload).Subscribe(SetWatching);
            filterSubscription = this.WhenAnyValue(x => x.Filter, x => x.LogEventLevelIndex).Subscribe(_ => Refilter());
            Sink.Inst.LogEvents.CollectionChanged += OnSinkChanged;
        }

        public void Detach() {
            Core.Util.ProcessRunner.DebugSwitch = false;
            Sink.Inst.LevelSwitch.MinimumLevel = LogEventLevel.Error;
            Sink.Inst.LogEvents.CollectionChanged -= OnSinkChanged;
            filterSubscription?.Dispose();
            liveReloadSubscription?.Dispose();
            SetWatching(false);
        }

        private IDisposable? filterSubscription;

        bool Matches(LogEvent logEvent) {
            if ((int)logEvent.Level < LogEventLevelIndex) {
                return false;
            }
            if (string.IsNullOrEmpty(Filter)) {
                return true;
            }
            return logEvent.RenderMessage().Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || (logEvent.Exception?.Message.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        void Refilter() {
            LogEvents.Load(Sink.Inst.LogEvents.Where(Matches));
        }

        void OnSinkChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count == 1) {
                // One new event, at the top or the bottom depending on the log order.
                var logEvent = (LogEvent)e.NewItems[0]!;
                if (Matches(logEvent)) {
                    if (e.NewStartingIndex == 0) {
                        LogEvents.Insert(0, logEvent);
                    } else {
                        LogEvents.Add(logEvent);
                    }
                }
                return;
            }
            Refilter();
        }

        private IDisposable? liveReloadSubscription;
        private FileSystemWatcher? watcher;
        private Timer? reloadTimer;

        void SetWatching(bool watch) {
            watcher?.Dispose();
            watcher = null;
            reloadTimer?.Dispose();
            reloadTimer = null;
            if (!watch) {
                return;
            }
            var path = PathManager.Inst.DependencyPath;
            FileSystemWatcher w;
            try {
                Directory.CreateDirectory(path);
                watcher = w = new FileSystemWatcher(path) { IncludeSubdirectories = true };
            } catch (Exception e) {
                Log.Error(e, "Failed to watch {Path}", path);
                return;
            }
            string? changedPath = null;
            var timer = reloadTimer = new Timer(_ => Dispatcher.UIThread.Post(() => {
                if (watcher != w) {
                    return;
                }
                Log.Information("Reloading packages after a change to {Path}", changedPath);
                DocManager.Inst.ExecuteCmd(new PackagesChangedNotification());
            }));
            // Editors often save in several steps, e.g. a temp file then a rename, so reload once they settle.
            void OnChanged(object sender, FileSystemEventArgs e) {
                changedPath = e.FullPath;
                timer.Change(300, Timeout.Infinite);
            }
            w.Changed += OnChanged;
            w.Created += OnChanged;
            w.Deleted += OnChanged;
            w.Renamed += OnChanged;
            w.EnableRaisingEvents = true;
            Log.Information("Live reload: watching {Path}", path);
        }
    }
}
