using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Data.Converters;
using DynamicData.Binding;
using OpenUtau.App.Views;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.SourceGenerators;
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
                string message = stringWriter.GetStringBuilder().ToString();
                stringWriter.GetStringBuilder().Clear();
                return message;
            }
            return string.Empty;
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return new Avalonia.Data.BindingNotification(new NotImplementedException(), Avalonia.Data.BindingErrorType.Error);
        }
    }

    public partial class DebugViewModel : ViewModelBase {

        private DebugWindow? window;

        public void SetWindow(DebugWindow w) {
            window = w;
        }

        public DebugViewModel() {
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

        [Reactive] public partial LogEventLevel LogEventLevel { get; set; }
        public ObservableCollection<LogEvent> LogEvents => Sink.Inst.LogEvents;
        public ReactiveCommand<RxVoid, RxVoid> ReverseLogOrderCommand { get; private set; }
        public ReactiveCommand<RxVoid, RxVoid> CopyLogCommand { get; private set; }

        public void Clear() {
            Sink.Inst.LogEvents.Clear();
        }

        public void Attach() {
            Core.Util.ProcessRunner.DebugSwitch = true;
            Sink.Inst.LevelSwitch.MinimumLevel = LogEventLevel.Verbose;
        }

        public void Detach() {
            Core.Util.ProcessRunner.DebugSwitch = false;
            Sink.Inst.LevelSwitch.MinimumLevel = LogEventLevel.Error;
        }
    }
}
