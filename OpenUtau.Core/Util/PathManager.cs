using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {

    public class PathManager : SingletonBase<PathManager> {
        public PathManager() {
            RootPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (OS.IsMacOS()) {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                DataPath = Path.Combine(userHome, "Library", "OpenUtau");
                CachePath = Path.Combine(userHome, "Library", "Caches", "OpenUtau");
                HomePathIsAscii = true;
                try {
                    // Deletes old cache.
                    string oldCache = Path.Combine(DataPath, "Cache");
                    if (Directory.Exists(oldCache)) {
                        Directory.Delete(oldCache, true);
                    }
                } catch { }
            } else if (OS.IsLinux()) {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (string.IsNullOrEmpty(dataHome)) {
                    dataHome = Path.Combine(userHome, ".local", "share");
                }
                DataPath = Path.Combine(dataHome, "OpenUtau");
                string cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cacheHome)) {
                    cacheHome = Path.Combine(userHome, ".cache");
                }
                CachePath = Path.Combine(cacheHome, "OpenUtau");
                HomePathIsAscii = true;
            } else {
                string exePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                IsInstalled = File.Exists(Path.Combine(exePath, "installed.txt"));
                if (!IsInstalled) {
                    DataPath = exePath;
                } else {
                    string dataHome = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                    DataPath = Path.Combine(dataHome, "OpenUtau");
                }
                CachePath = Path.Combine(DataPath, "Cache");
                HomePathIsAscii = true;
                var etor = StringInfo.GetTextElementEnumerator(DataPath);
                while (etor.MoveNext()) {
                    string s = etor.GetTextElement();
                    if (s.Length != 1 || s[0] >= 128) {
                        HomePathIsAscii = false;
                        break;
                    }
                }
            }
        }

        public string RootPath { get; private set; }
        public string DataPath { get; private set; }
        public string CachePath { get; private set; }
        public bool HomePathIsAscii { get; private set; }
        public bool IsInstalled { get; private set; }
        public string SingersPathOld => Path.Combine(DataPath, "Content", "Singers");
        public string SingersPath => Path.Combine(DataPath, "Singers");
        public string AdditionalSingersPath => Preferences.Default.AdditionalSingerPath;
        public string SingersInstallPath => Preferences.Default.InstallToAdditionalSingersPath
            && !string.IsNullOrEmpty(Preferences.Default.AdditionalSingerPath)
                ? AdditionalSingersPath
                : SingersPath;
        public string ResamplersPath => Path.Combine(DataPath, "Resamplers");
        public string WavtoolsPath => Path.Combine(DataPath, "Wavtools");
        public string DependencyPath => Path.Combine(DataPath, "Dependencies");
        public string PluginsPath => Path.Combine(DataPath, "Plugins");
        public string DictionariesPath => Path.Combine(DataPath, "Dictionaries");
        public string TemplatesPath => Path.Combine(DataPath, "Templates");
        public string LogsPath => Path.Combine(DataPath, "Logs");
        public string LogFilePath => Path.Combine(DataPath, "Logs", "log.txt");
        public string PrefsFilePath => Path.Combine(DataPath, "prefs.json");
        public string ThemesPath => Path.Combine(DataPath, "Themes");
        public string NotePresetsFilePath => Path.Combine(DataPath, "notepresets.json");
        public string BackupsPath => Path.Combine(DataPath, "Backups");

        public List<string> SingersPaths {
            get {
                var list = new List<string> { SingersPath };
                if (Directory.Exists(SingersPathOld)) {
                    list.Add(SingersPathOld);
                }
                if (Directory.Exists(AdditionalSingersPath)) {
                    list.Add(AdditionalSingersPath);
                }
                return list.Distinct().ToList();
            }
        }

        Regex invalid = new Regex("[\\x00-\\x1f<>:\"/\\\\|?*]|^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9]|CLOCK\\$)(\\.|$)|[\\.]$", RegexOptions.IgnoreCase);

        public string GetPartSavePath(string exportPath, string partName, int partNo) {
            var dir = Path.GetDirectoryName(exportPath);
            Directory.CreateDirectory(dir);
            var filename = Path.GetFileNameWithoutExtension(exportPath);
            var name = invalid.Replace(partName, "_");
            if (DocManager.Inst.Project.parts.FindAll(p => p is UVoicePart).Count(p => p.DisplayName == partName) > 1) {
                name += $"_{partNo:D2}";
            }
            return Path.Combine(dir, $"{filename}_{name}.ust");
        }

        public string GetExportPath(string exportPath, UTrack track) {
            var dir = Path.GetDirectoryName(exportPath);
            Directory.CreateDirectory(dir);
            var filename = Path.GetFileNameWithoutExtension(exportPath);
            var trackName = invalid.Replace(track.TrackName, "_");
            if (DocManager.Inst.Project.tracks.Count(t => t.TrackName == track.TrackName) > 1) {
                trackName += $"_{track.TrackNo:D2}";
            }
            return Path.Combine(dir, $"{filename}_{trackName}.wav");
        }

        public void ClearCache() {
            var files = Directory.GetFiles(CachePath);
            foreach (var file in files) {
                try {
                    File.Delete(file);
                } catch (Exception e) {
                    Log.Error(e, $"Failed to delete file {file}");
                }
            }
            var dirs = Directory.GetDirectories(CachePath);
            foreach (var dir in dirs) {
                try {
                    Directory.Delete(dir, true);
                } catch (Exception e) {
                    Log.Error(e, $"Failed to delete dir {dir}");
                }
            }
        }

        readonly static string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
        public string GetCacheSize() {
            if (!Directory.Exists(CachePath)) {
                return "0B";
            }
            var dir = new DirectoryInfo(CachePath);
            double size = dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            int order = 0;
            while (size >= 1024 && order < sizes.Length - 1) {
                order++;
                size = size / 1024;
            }
            return $"{size:0.##}{sizes[order]}";
        }
    }
}
