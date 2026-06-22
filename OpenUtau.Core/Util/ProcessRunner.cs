using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
            var output = new StringBuilder();
            var outputLock = new object();

            // Signals used to ensure the async stdout/stderr readers have flushed all data before we read `output`.
            using var stdoutDone = new ManualResetEventSlim(false);
            using var stderrDone = new ManualResetEventSlim(false);
            using (var proc = new Process()) {
                proc.StartInfo = new ProcessStartInfo(file, args) {
                    Environment = { { "LANG", "ja_JP.utf8" } },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir,
                };

                proc.OutputDataReceived += (o, e) => {
                    if (e.Data == null) {
                        stdoutDone.Set();
                        return;
                    }
                    if (DebugSwitch) {
                        logger.Information($"ProcessRunner >>> [thread-{threadId}] {e.Data}");
                    }
                    lock (outputLock) {
                        output.AppendLine(e.Data);
                    }
                };
                proc.ErrorDataReceived += (o, e) => {
                    if (e.Data == null) {
                        stderrDone.Set();
                        return;
                    }
                    logger.Error($"ProcessRunner >>> [thread-{threadId}] {e.Data}");
                    lock (outputLock) {
                        output.AppendLine(e.Data);
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                bool exited;
                if (timeoutMs <= 0) {
                    proc.WaitForExit();
                    exited = true;
                } else {
                    exited = proc.WaitForExit(timeoutMs);
                }

                if (!exited) {
                    logger.Warning($"ProcessRunner >>> [thread-{threadId}] Timeout, killing...");
                    try {
                        proc.Kill(entireProcessTree: true);
                        logger.Warning($"ProcessRunner >>> [thread-{threadId}] Killed.");
                    } catch (Exception e) {
                        logger.Error(e, $"ProcessRunner >>> [thread-{threadId}] Failed to kill");
                    }
                }

                try {
                    proc.WaitForExit();
                } catch { /* process already disposed/exited */ }

                stdoutDone.Wait(TimeSpan.FromSeconds(5));
                stderrDone.Wait(TimeSpan.FromSeconds(5));

                lock (outputLock) {
                    if (!exited) {
                        output.AppendLine("Killed due to timeout.");
                    } else {
                        output.Append("Exit code ").Append(proc.ExitCode);
                    }
                    return output.ToString();
                }
            }
        }
    }
}
