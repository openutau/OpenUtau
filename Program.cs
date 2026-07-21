using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App {
    public class Program {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) {
            // ลงทะเบียน Provider เพื่อให้รองรับการเข้ารหัสอักขระภาษาต่างๆ (รวมถึงภาษาไทย)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            // เริ่มต้นระบบบันทึกประวัติการทำงาน (Logging) ของโปรแกรม
            InitLogging();
            
            // ตรวจสอบชื่อโปรเซสปัจจุบันเพื่อป้องกันไม่ให้เปิดโปรแกรมซ้ำซ้อนกันหลายหน้าต่าง
            string processName = Process.GetCurrentProcess().ProcessName;
            if (processName != "dotnet") {
                var exists = Process.GetProcessesByName(processName).Count() > 1;
                if (exists) {
                    Log.Information($"Process {processName} already open. Exiting.");
                    return;
                }
            }
            
            // บันทึกข้อมูลเวอร์ชันของระบบปฏิบัติการที่ผู้ใช้กำลังใช้งานลง Log
            Log.Information($"{Environment.OSVersion}");
            Log.Information($"{RuntimeInformation.OSDescription} " +
                $"{RuntimeInformation.OSArchitecture} " +
                $"{RuntimeInformation.ProcessArchitecture}");
            
            // ประกาศเอกลักษณ์ Thai OpenUtau พัฒนาโดย DELTA SYNTH เมื่อโปรแกรมเริ่มทำงาน
            Log.Information($"Thai OpenUtau v{Assembly.GetEntryAssembly()?.GetName().Version} by DELTA SYNTH " +
                $"{RuntimeInformation.RuntimeIdentifier}");
                
            // บันทึกตำแหน่งโฟลเดอร์เก็บข้อมูลและ Cache ของโปรแกรมลง Log
            Log.Information($"Data path = {PathManager.Inst.DataPath}");
            Log.Information($"Cache path = {PathManager.Inst.CachePath}");
            Log.Information($"System encoding = {Encoding.GetEncoding(0)?.WebName ?? "null"}");
            
            try {
                // เรียกใช้ฟังก์ชันหลักเพื่อเริ่มต้นหน้าต่างและการทำงานของโปรแกรม (Avalonia UI)
                Run(args);
                Log.Information($"Exiting.");
            } finally {
                // เคลียร์การเชื่อมต่อภายในระบบเครือข่าย (NetMQ) ยกเว้นบน macOS เพื่อป้องกันการค้าง
                if (!OS.IsMacOS()) {
                    NetMQ.NetMQConfig.Cleanup(/*block=*/false);
                }
            }
            
            // บันทึกสถานะว่าโปรแกรมทำงานเสร็จสิ้นกระบวนการหลักแล้ว
            Log.Information($"Exited.");
            
            // [ส่วนที่ปรับปรุงใหม่ ⚡] เคลียร์ Buffer ทั้งหมดของระบบ Log และปิดการทำงานลงทันที
            Log.CloseAndFlush();
            
            // [ส่วนที่ปรับปรุงใหม่ ⚡] บังคับให้ระบบปฏิบัติการปิดโปรเซสนี้ลงอย่างเด็ดขาดและรวดเร็ว
            // พารามิเตอร์ 0 หมายถึงโปรแกรมจบการทำงานโดยสมบูรณ์และไม่มีข้อผิดพลาด (Exit Code: 0)
            Environment.Exit(0);
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
                fontOptions.DefaultFamilyName = "Hiragino Sans, Segoe UI, San Francisco, Helvetica Neue";
            }
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI()
                .With(fontOptions)
                .With(new X11PlatformOptions {EnableIme = true});
        }

        public static void Run(string[] args)
            => BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(
                    args, ShutdownMode.OnMainWindowClose);

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
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.Warning()
                    .WriteTo.Sink(OpenUtau.App.Views.ToastNotificationSink.Inst))
                .CreateLogger();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((sender, args) => {
                Log.Error((Exception)args.ExceptionObject, "Unhandled exception");
            });
            Log.Information("Logging initialized.");
        }
    }
}