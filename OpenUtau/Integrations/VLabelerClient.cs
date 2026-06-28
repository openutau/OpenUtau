using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.Integrations {
    internal class VLabelerClient : Core.Util.SingletonBase<VLabelerClient> {
        class HeartbeatRequest {
#pragma warning disable CS0414
            public string type = "Heartbeat";
#pragma warning restore CS0414
            public long sentAt = Epoch();
        }

        class GotoEntryByName {
            public string parentFolderName;
            public string entryName;
            public GotoEntryByName(string parentFolderName, string entryName) {
                this.parentFolderName = parentFolderName;
                this.entryName = entryName;
            }
        }

        class GotoEntryByIndex {
            public string parentFolderName;
            public string entryIndex;
            public GotoEntryByIndex(string parentFolderName, string entryIndex) {
                this.parentFolderName = parentFolderName;
                this.entryIndex = entryIndex;
            }
        }

        class TypedValue {
            public string type;
            public object value;
            public TypedValue(string type, object value) {
                this.type = type;
                this.value = value;
            }
        }

        class NewProjectArgs {
#pragma warning disable 0649
#pragma warning disable CS0414
            public string labelerName = "utau-singer.default";
            public string? sampleDirectory;
            public string? cacheDirectory;
            public Dictionary<string, TypedValue>? labelerParams;
            public string? pluginName;
            public Dictionary<string, TypedValue>? pluginParams;
            public string? inputFile;
            public string encoding = Encoding.UTF8.WebName;
            public bool autoExport;
#pragma warning restore 0649
#pragma warning restore CS0414

        }

        class OpenOrCreateRequest {
#pragma warning disable CS0414
            public string type = "OpenOrCreate";
#pragma warning restore CS0414
            public string projectFile = string.Empty;
            public GotoEntryByName? gotoEntryByName;
            public NewProjectArgs newProjectArgs = new NewProjectArgs();
            public long sentAt = Epoch();
        }

        private static long Epoch() {
            return (long)(DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private static string HashHex(string s) {
            return $"{XXH32.DigestOf(Encoding.UTF8.GetBytes(s)):x8}";
        }

        private bool Heartbeat() {
            using (var client = new RequestSocket()) {
                client.Connect("tcp://localhost:32342");
                string reqStr = JsonConvert.SerializeObject(new HeartbeatRequest());
                client.SendFrame(reqStr);
                if (client.TryReceiveFrameString(TimeSpan.FromMilliseconds(1000), out string? respStr)) {
                    return true;
                }
                return false;
            }
        }

        private void OpenOrCreate(Core.Ustx.USinger singer, Core.Ustx.UOto? oto) {
            var existingProjectName = Directory.GetFiles(singer.Location)
                .Where(path => Path.GetExtension(path) == ".lbp")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            var request = new OpenOrCreateRequest() {
                projectFile = Path.Combine(singer.Location, existingProjectName ?? "_vlabeler.lbp"),
                newProjectArgs = new NewProjectArgs {
                    cacheDirectory = Path.Combine(PathManager.Inst.CachePath, $"vlabeler-{HashHex(singer.Id)}"),
                    labelerParams = new Dictionary<string, TypedValue> {
                        { "useRootDirectory", new TypedValue("boolean", true) }
                    },
                    encoding = singer.TextFileEncoding.WebName,
                    autoExport = true,
                },
            };
            if (oto != null) {
                request.gotoEntryByName = new GotoEntryByName(oto.Set.Replace("\\", "/"), oto.Alias);
            }
            using (var client = new RequestSocket()) {
                client.Connect("tcp://localhost:32342");
                string reqStr = JsonConvert.SerializeObject(request);
                client.SendFrame(reqStr);
                if (!client.TryReceiveFrameString(TimeSpan.FromMilliseconds(1000), out string? respStr)) {
                    Log.Warning($"Failed to OpenOrCreate with vLabeler");
                }
            }
        }

        private Task<bool> TryStart() {
            return Task.Run(() => {
                if (Heartbeat()) {
                    return true;
                }
                var path = Core.Util.Preferences.Default.VLabelerPath;
                if (!OS.AppExists(path)) {
                    throw new FileNotFoundException($"Cannot find file {path}.");
                }
                using (var proc = new Process()) {
                    if (OS.IsMacOS()) {
                        OS.OpenFolder(path);
                    } else {
                        proc.StartInfo = new ProcessStartInfo(path) {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        };
                        proc.Start();
                    }
                    Log.Information("Starting vLabeler.");
                }
                for (int i = 0; i < 50; i++) {
                    Task.Delay(100);
                    if (Heartbeat()) {
                        Log.Information("vLabeler started.");
                        return true;
                    }
                }
                Log.Warning("Unable to start vLabeler.");
                return false;
            });
        }

        public void GotoOto(Core.Ustx.USinger singer, Core.Ustx.UOto? oto) {
            TryStart().ContinueWith(task => {
                if (!task.IsFaulted && task.Result) {
                    OpenOrCreate(singer, oto);
                } else if (task.IsFaulted) {
                    if (task.Exception != null) {
                        throw task.Exception;
                    }
                } else {
                    throw new Exception("Failed to start vLabeler");
                }
            }).ContinueWith(task => {
                if (task.IsFaulted) {
                    if (task.Exception != null) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(task.Exception));
                    } else {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("Failed to start vLabeler"));
                    }
                }
            });
        }
    }
}
