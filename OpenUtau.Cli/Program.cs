using System;
using System.IO;
using System.Text;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.Cli {
    internal static class Program {
        public static int Main(string[] args) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitLogging();
            try {
                return RenderCommand.Run(args);
            } finally {
                if (!OS.IsMacOS()) {
                    NetMQ.NetMQConfig.Cleanup(block: false);
                }
                Log.CloseAndFlush();
            }
        }

        private static void InitLogging() {
            Directory.CreateDirectory(PathManager.Inst.LogsPath);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8)
                .CreateLogger();
        }
    }
}
