using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class SingerManager : SingletonBase<SingerManager> {
        public Dictionary<string, USinger> Singers { get; private set; } = new Dictionary<string, USinger>();
        public Dictionary<USingerType, List<USinger>> SingerGroups { get; private set; } = new Dictionary<USingerType, List<USinger>>();

        private readonly ConcurrentQueue<USinger> reloadQueue = new ConcurrentQueue<USinger>();
        private CancellationTokenSource reloadCancellation;

        private HashSet<USinger> singersUsed = new HashSet<USinger>();

        // YAML Watcher & timestamp tracking for character.yaml
        private readonly List<YamlWatcher> singerWatchers = new List<YamlWatcher>();
        private readonly ConcurrentDictionary<string, DateTime> charYamlLastWriteTimes = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public void Initialize() {
            SearchAllSingers();
        }

        public void SearchAllSingers() {
            Log.Information("Searching singers.");
            Directory.CreateDirectory(PathManager.Inst.SingersPath);
            var stopWatch = Stopwatch.StartNew();
            var oldSingers = Singers.Values.ToList();
            var singers = ClassicSingerLoader.FindAllSingers()
                .Concat(Vogen.VogenSingerLoader.FindAllSingers())
                .Distinct();
            Singers = singers
                .ToLookup(s => s.Id)
                .ToDictionary(g => g.Key, g => g.First());
            SingerGroups = singers
                .GroupBy(s => s.SingerType)
                .ToDictionary(s => s.Key, s => s.LocalizedOrderBy(singer => singer.LocalizedName).ToList());
            foreach (var old in oldSingers) {
                (old as IDisposable)?.Dispose();
            }
            stopWatch.Stop();
            Log.Information($"Search all singers: {stopWatch.Elapsed}");
            CacheCharacterYamlTimestamps();
            SetupSingerWatchers();
        }

        private void CacheCharacterYamlTimestamps() {
            charYamlLastWriteTimes.Clear();
            var singers = Singers.Values.ToArray();
            foreach (var singer in singers) {
                if (singer == null || string.IsNullOrEmpty(singer.Location)) {
                    continue;
                }
                try {
                    string charYaml = Path.Combine(singer.Location, "character.yaml");
                    if (File.Exists(charYaml)) {
                        charYamlLastWriteTimes[charYaml] = File.GetLastWriteTimeUtc(charYaml);
                    }
                } catch { }
            }
        }

        private void SetupSingerWatchers() {
            foreach (var watcher in singerWatchers) {
                watcher.Dispose();
            }
            singerWatchers.Clear();

            // Attach YamlWatcher to each singers root path
            foreach (var path in PathManager.Inst.SingersPaths) {
                if (Directory.Exists(path)) {
                    try {
                        singerWatchers.Add(new YamlWatcher(path, OnSingerYamlChanged));
                    } catch (Exception ex) {
                        Log.Error(ex, $"[SingerManager] Failed to start YAML watcher for '{path}'");
                    }
                }
            }
        }

        private void OnSingerYamlChanged() {
            // Brief sleep to allow editors/writers to finish flushing and close file handles
            Thread.Sleep(300);
            var singers = Singers.Values.ToArray();

            foreach (var singer in singers) {
                if (singer == null || string.IsNullOrEmpty(singer.Location)) {
                    continue;
                }
                string charYaml;
                try {
                    charYaml = Path.Combine(singer.Location, "character.yaml");
                    if (!File.Exists(charYaml)) {
                        continue;
                    }
                } catch {
                    continue;
                }
                DateTime currentWriteTime;
                try {
                    currentWriteTime = File.GetLastWriteTimeUtc(charYaml);
                } catch (IOException) {
                    // File is temporarily locked by editor; the next event or debounce will catch it
                    continue;
                } catch {
                    continue;
                }
                if (charYamlLastWriteTimes.TryGetValue(charYaml, out var lastWriteTime)) {
                    if (currentWriteTime > lastWriteTime) {
                        charYamlLastWriteTimes[charYaml] = currentWriteTime;
                        string singerName = singer.Id ?? Path.GetFileName(singer.Location);
                        Log.Information($"[SingerManager] character.yaml modified for '{singerName}'. Scheduling reload...");
                        ScheduleReload(singer);
                    }
                } else {
                    // First time detecting this character.yaml
                    charYamlLastWriteTimes[charYaml] = currentWriteTime;
                    string singerName = singer.Id ?? Path.GetFileName(singer.Location);
                    Log.Information($"[SingerManager] character.yaml detected for '{singerName}'. Scheduling reload...");
                    ScheduleReload(singer);
                }
            }
        }

        public USinger GetSinger(string name) {
            Log.Information($"Attach singer to track: {name}");
            name = name.Replace("%VOICE%", "");
            if (Singers.ContainsKey(name)) {
                return Singers[name];
            }
            return null;
        }

        public void ScheduleReload(USinger singer) {
            reloadQueue.Enqueue(singer);
            ScheduleReload();
        }

        private void ScheduleReload() {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref reloadCancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            Task.Run(() => {
                Thread.Sleep(200);
                if (newCancellation.IsCancellationRequested) {
                    return;
                }
                Refresh();
            });
        }

        private void Refresh() {
            var singers = new HashSet<USinger>();
            while (reloadQueue.TryDequeue(out USinger singer)) {
                singers.Add(singer);
            }
            foreach (var singer in singers) {
                Log.Information($"Reloading {singer.Id}");
                new Task(() => {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Reloading {singer.Id}"));
                }).Start(DocManager.Inst.MainScheduler);
                int retries = 5;
                while (retries > 0) {
                    retries--;
                    try {
                        singer.Reload();
                        break;
                    } catch (Exception e) {
                        if (retries == 0) {
                            Log.Error(e, $"Failed to reload {singer.Id}");
                        } else {
                            Log.Error(e, $"Retrying reload {singer.Id}");
                            Thread.Sleep(200);
                        }
                    }
                }
                Log.Information($"Reloaded {singer.Id}");
                new Task(() => {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Reloaded {singer.Id}"));
                    DocManager.Inst.ExecuteCmd(new OtoChangedNotification(external: true));
                    DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(-1, true));
                }).Start(DocManager.Inst.MainScheduler);
            }
        }

        // Check which singers are in use and free memory for those that are not.
        // UI thread only: it mutates the singer map the UI reads.
        public void ReleaseSingersNotInUse(UProject project) {
            Util.ThreadGuard.AssertUi();
            // Check which singers are in use
            var singersInUse = new HashSet<USinger>();
            foreach (var track in project.tracks) {
                var singer = track.Singer;
                if (singer != null && singer.Found && !singersInUse.Contains(singer)) {
                    singersInUse.Add(singer);
                }
            }
            // Release singers that are no longer in use
            foreach (var singer in singersUsed) {
                if (!singersInUse.Contains(singer)) {
                    singer.FreeMemory();
                }
            }
            // Update singers used
            singersUsed = singersInUse;
        }
    }
}