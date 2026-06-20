using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Serilog;

namespace OpenUtau.Core.Util {
    public static class ProcessRunner {
        public static bool DebugSwitch { get; set; }
        public static string Run(string file, string args, ILogger logger, string workDir = null, int timeoutMs = 60000) {
            if (!File.Exists(file)) {
                throw new FileNotFoundException($"Executable {file} not found.");
            }
            var threadId = Thread.CurrentThread.ManagedThreadId;
            var output = "";
            using (var proc = new Process()) {
                proc.StartInfo = new ProcessStartInfo(file, args) {
                    Environment = {{"LANG", "ja_JP.utf8"}},
                    UseShellExecute = false,
                    RedirectStandardOutput = DebugSwitch,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir,
                };
                if (DebugSwitch) {
                    proc.OutputDataReceived += (o, e) => {
                        if (!string.IsNullOrEmpty(e.Data)) {
                            logger.Information($"ProcessRunner >>> [thread-{threadId}] {e.Data}");
                            output += $"{e.Data}\n";
                        }
                    };
                }
                proc.ErrorDataReceived += (o, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) {
                        logger.Error($"ProcessRunner >>> [thread-{threadId}] {e.Data}");
                        output += $"{e.Data}\n";
                    }
                };
                proc.Start();
                if (DebugSwitch) {
                    proc.BeginOutputReadLine();
                }
                proc.BeginErrorReadLine();
                if (timeoutMs <= 0) {
                    proc.WaitForExit();
                } else {
                    if (proc.WaitForExit(timeoutMs)) {
                        output += $"Exit code {proc.ExitCode}";
                        return output;
                    }
                    logger.Warning($"ProcessRunner >>> [thread-{threadId}] Timeout, killing...");
                    try {
                        proc.Kill();
                        logger.Warning($"ProcessRunner >>> [thread-{threadId}] Killed.");
                        output += "Killed due to timeout.";
                    } catch (Exception e) {
                        logger.Error(e, $"ProcessRunner >>> [thread-{threadId}] Failed to kill");
                    }
                }
            }
            return output;
        }
    }
}
