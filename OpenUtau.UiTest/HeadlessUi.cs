using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.Core;
using ReactiveUI.Avalonia;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using SerilogLevel = Serilog.Events.LogEventLevel;

namespace OpenUtau.UiTest {
    public class TestAppBuilder {
        // Skia with real drawing so tests can capture screenshots; ReactiveUI as in Program.
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseReactiveUI(_ => { });
    }

    /// <summary>
    /// Runs UI test code on one headless app shared by the whole test run, like the real app.
    /// Used instead of [AvaloniaFact], which Avalonia.Headless.XUnit 12.1.2 can't run on xunit.v3 4.x.
    /// </summary>
    public static class HeadlessUi {
        // PerAssembly: the app attaches dev tools on init in Debug builds, which Avalonia allows only once.
        static readonly Lazy<HeadlessUnitTestSession> session = new Lazy<HeadlessUnitTestSession>(() => {
            Log.Logger = new LoggerConfiguration().WriteTo.Sink(Errors).CreateLogger();
            Avalonia.Logging.Logger.Sink = Errors;
            return HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder), AvaloniaTestIsolationLevel.PerAssembly);
        });

        /// <summary>Errors reported anywhere in the app while tests run.</summary>
        public static readonly ErrorCollector Errors = new ErrorCollector();

        public static void Run(Action action) {
            session.Value.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>Processes pending UI work and renders a frame.</summary>
        public static void Flush() {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        /// <summary>
        /// Saves what the window currently shows to ui-screenshots next to the test binaries,
        /// where CI picks it up as an artifact.
        /// </summary>
        public static void SaveScreenshot(TopLevel topLevel, string name) {
            using var frame = topLevel.CaptureRenderedFrame();
            if (frame == null) {
                return;
            }
            var dir = Path.Combine(AppContext.BaseDirectory, "ui-screenshots");
            Directory.CreateDirectory(dir);
            frame.Save(Path.Combine(dir, name + ".png"));
        }

        /// <summary>
        /// Number of distinct colors in the rendered frame, sampled on a grid. A blank or
        /// single-color window has one or two; a drawn UI has many. Robust to layout and style changes.
        /// </summary>
        public static int CountRenderedColors(TopLevel topLevel) {
            using var frame = topLevel.CaptureRenderedFrame();
            if (frame == null) {
                return 0;
            }
            using var buffer = frame.Lock();
            var colors = new HashSet<int>();
            int step = 8;
            for (int y = 0; y < buffer.Size.Height; y += step) {
                for (int x = 0; x < buffer.Size.Width; x += step) {
                    colors.Add(Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4));
                }
            }
            return colors.Count;
        }
    }

    /// <summary>
    /// Collects error notifications (the app also turns unhandled UI exceptions into these),
    /// error-level log events and Avalonia binding errors.
    /// </summary>
    public class ErrorCollector : ICmdSubscriber, ILogEventSink, ILogSink {
        readonly List<string> errors = new List<string>();

        public IReadOnlyList<string> Snapshot() {
            lock (errors) {
                return errors.ToArray();
            }
        }

        public void Clear() {
            lock (errors) {
                errors.Clear();
            }
        }

        void Add(string error) {
            lock (errors) {
                errors.Add(error);
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ErrorMessageNotification notification) {
                Add($"Error notification: {notification.message} {notification.e}");
            }
        }

        public void Emit(LogEvent logEvent) {
            if (logEvent.Level >= SerilogLevel.Error) {
                Add($"Log error: {logEvent.RenderMessage()} {logEvent.Exception}");
            }
        }

        // Avalonia reports broken bindings as warnings, so those count too.
        public bool IsEnabled(AvaloniaLevel level, string area) =>
            level >= AvaloniaLevel.Error || (level >= AvaloniaLevel.Warning && area == LogArea.Binding);

        public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate) {
            if (IsEnabled(level, area)) {
                Add($"Avalonia {area} on {source?.GetType().Name}: {messageTemplate}");
            }
        }

        public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) {
            if (IsEnabled(level, area)) {
                Add($"Avalonia {area} on {source?.GetType().Name}: {messageTemplate} [{string.Join(", ", propertyValues)}]");
            }
        }
    }
}
