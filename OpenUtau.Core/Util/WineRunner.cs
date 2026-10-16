using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Serilog;

namespace OpenUtau.Core.Util {
    public static class WineRunner {
        private static Process? daemon;
        private static string daemonWinePath = string.Empty;
        private static readonly object lockObj = new();

        static WineRunner() {
            AppDomain.CurrentDomain.ProcessExit += (_ ,_) => daemon?.Kill();
        }

        public static void EnsureWineServerRunning(string winePath) {
            lock (lockObj) {
                if (daemonWinePath == winePath && daemon is { HasExited: false }) {
                    return;
                }

                daemon?.Dispose();
                daemon = null;

                if (!File.Exists(winePath)) {
                    return;
                }

                var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = winePath,
                        ArgumentList = {
                            "cmd.exe",
                            "/c",
                            "timeout /t 120 /nobreak > nul & :: Keep wineserver alive for OpenUtau" },
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                    EnableRaisingEvents = true
                };

                process.Exited += (_, _) => {
                    lock (lockObj) {
                        if (ReferenceEquals(daemon, process)) {
                            daemon = null;
                        }
                    }

                    process.Dispose();

                    daemonWinePath = string.Empty;
                };

                try {
                    if (process.Start()) {
                        daemon = process;
                    } else {
                        process.Dispose();
                    }
                } catch {
                    process.Dispose();
                    throw;
                }

                daemonWinePath = winePath;

                Thread.Sleep(100);
            }
        }

        public static string Run(string winePath, string file, string args, ILogger logger, string workDir = null, int timeoutMs = 60000) {
            EnsureWineServerRunning(winePath);
            return ProcessRunner.Run(winePath, $"{file} {args}", logger, workDir, timeoutMs);
        }
    }
}