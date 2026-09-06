using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI.Avalonia;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Headless;
using Serilog;

namespace OpenUtau.App {
    public class Program {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) {
            var isHeadlessCommand = HeadlessRenderCommand.IsCommand(args);
            if (isHeadlessCommand) {
                AttachToParentConsole();
            }
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitLogging();
            if (isHeadlessCommand) {
                try {
                    Environment.ExitCode = RunHeadlessCommand(args);
                } finally {
                    CleanupNetMq();
                    Log.CloseAndFlush();
                }
                return;
            }
            string processName = Process.GetCurrentProcess().ProcessName;
            if (processName != "dotnet") {
                var exists = Process.GetProcessesByName(processName).Count() > 1;
                if (exists) {
                    Log.Information($"Process {processName} already open. Exiting.");
                    return;
                }
            }
            Log.Information($"{Environment.OSVersion}");
            Log.Information($"{RuntimeInformation.OSDescription} " +
                $"{RuntimeInformation.OSArchitecture} " +
                $"{RuntimeInformation.ProcessArchitecture}");
            Log.Information($"OpenUtau v{Assembly.GetEntryAssembly()?.GetName().Version} " +
                $"{RuntimeInformation.RuntimeIdentifier}");
            Log.Information($"Data path = {PathManager.Inst.DataPath}");
            Log.Information($"Cache path = {PathManager.Inst.CachePath}");
            Log.Information($"System encoding = {Encoding.GetEncoding(0)?.WebName ?? "null"}");
            try {
                Run(args);
                Log.Information($"Exiting.");
            } finally {
                CleanupNetMq();
            }
            Log.Information($"Exited.");
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp() {
            FontManagerOptions fontOptions = new();
            if (OS.IsLinux()) {
                using Process process = Process.Start(new ProcessStartInfo("fc-match")
                {
                    ArgumentList = { "-f", "%{family}" },
                    RedirectStandardOutput = true
                })!;
                process.WaitForExit();

                string fontFamily = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrEmpty(fontFamily)) {
                    string [] fontFamilies = fontFamily.Split(',');
                    fontOptions.DefaultFamilyName = fontFamilies[0];
                }
            } else if (OS.IsMacOS()) {
                //To avoid text display corruption, specify Hiragino Sans font first.
                //Due to the specification of AvaloniaUI, this only affects when the language is set to Japanese.
                fontOptions.DefaultFamilyName = "Hiragino Sans";
                fontOptions.FontFallbacks = [
                    new FontFallback { FontFamily = new FontFamily("Helvetica Neue") },
                    new FontFallback { FontFamily = new FontFamily("Arial") },
                ];
            }

            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI(_ => { })
                .With(fontOptions);
            
            if (OS.IsLinux() && Core.Util.Preferences.Default.UseWayland) {
                builder.UseWayland();
            }
            
            return builder.With(new X11PlatformOptions {
                EnableIme = true
            });
        }

        public static void Run(string[] args)
            => BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(
                    args, ShutdownMode.OnMainWindowClose);

        private static int RunHeadlessCommand(string[] args) {
            var exitCode = 1;
            Exception? exception = null;
            var thread = new Thread(() => {
                try {
                    exitCode = HeadlessRenderCommand.Run(args);
                } catch (Exception e) {
                    exception = e;
                }
            });
            try {
                thread.SetApartmentState(ApartmentState.MTA);
            } catch {
            }
            thread.Start();
            thread.Join();
            if (exception != null) {
                Console.Error.WriteLine(exception.Message);
                Log.Error(exception, "Headless command failed unexpectedly.");
                return 1;
            }
            return exitCode;
        }

        private static void CleanupNetMq() {
            if (!OS.IsMacOS()) {
                NetMQ.NetMQConfig.Cleanup(/*block=*/false);
                // Cleanup() hangs on macOS https://github.com/zeromq/netmq/issues/1018
            }
        }

        public static void InitLogging() {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Debug()
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.Information()
                    .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8))
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.ControlledBy(DebugViewModel.Sink.Inst.LevelSwitch)
                    .WriteTo.Sink(DebugViewModel.Sink.Inst))
                .CreateLogger();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((sender, args) => {
                Log.Error((Exception)args.ExceptionObject, "Unhandled exception");
            });
            Log.Information("Logging initialized.");
        }

        private static void AttachToParentConsole() {
            if (!OS.IsWindows()) {
                return;
            }
            AttachConsole(AttachParentProcess);
            ResetConsoleStreams();
        }

        private static void ResetConsoleStreams() {
            try {
                var output = Console.OpenStandardOutput();
                Console.SetOut(new StreamWriter(output) { AutoFlush = true });
            } catch {
            }
            try {
                var error = Console.OpenStandardError();
                Console.SetError(new StreamWriter(error) { AutoFlush = true });
            } catch {
            }
        }

        private const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);
    }
}
