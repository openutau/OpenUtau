using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    static class NeutrinoServerLauncher {
        static readonly object lockObj = new object();
        static readonly Dictionary<string, Process> serverProcesses =
            new Dictionary<string, Process>(StringComparer.OrdinalIgnoreCase);
        static NeutrinoServerLauncher() {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAll();
        }

        public static void EnsureStarted(string serverExe, int? serverPort = 12345, string host = "127.0.0.1") {
            if (string.IsNullOrEmpty(serverExe) || !File.Exists(serverExe)) {
                return;
            }

            serverExe = Path.GetFullPath(serverExe);
            var serverName = Path.GetFileNameWithoutExtension(serverExe);
            if (Process.GetProcessesByName(serverName).Any() || IsServerReady(host, serverPort)) {
                Log.Information($"Background server already running: {serverExe}");
                return;
            }

            lock (lockObj) {
                if (serverProcesses.TryGetValue(serverExe, out var runningProcess) &&
                        !runningProcess.HasExited) {
                    return;
                }
                if (Process.GetProcessesByName(serverName).Any() || IsServerReady(host, serverPort)) {
                    return;
                }

                var startedProcess = ProcessRunner.StartBackground(
                    serverExe,
                    string.Empty,
                    Log.Logger,
                    workDir: Path.GetDirectoryName(serverExe));
                startedProcess.EnableRaisingEvents = true;
                startedProcess.Exited += (_, _) => {
                    lock (lockObj) {
                        if (serverProcesses.TryGetValue(serverExe, out var currentProcess) &&
                                ReferenceEquals(currentProcess, startedProcess)) {
                            serverProcesses.Remove(serverExe);
                        }
                    }
                };
                serverProcesses[serverExe] = startedProcess;
                WaitForServerReady(host, serverPort);
                Log.Information($"Started background server: {serverExe}");
            }
        }

        static void StopAll() {
            Process[] processes;
            lock (lockObj) {
                processes = serverProcesses.Values.ToArray();
                serverProcesses.Clear();
            }

            foreach (var process in processes) {
                try {
                    if (process.HasExited) {
                        process.Dispose();
                        continue;
                    }
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                    process.Dispose();
                } catch (Exception e) {
                    Log.Warning(e, "Failed to stop background server process.");
                }
            }
        }

        static bool IsServerReady(string host, int? serverPort) {
            if (!serverPort.HasValue) {
                return false;
            }
            try {
                using var client = new TcpClient();
                return client.ConnectAsync(host, serverPort.Value).Wait(50);
            } catch {
                return false;
            }
        }

        static void WaitForServerReady(string host, int? serverPort) {
            if (!serverPort.HasValue) {
                return;
            }
            for (int i = 0; i < 50; i++) {
                if (IsServerReady(host, serverPort)) {
                    return;
                }
                Thread.Sleep(100);
            }
        }
    }
}
