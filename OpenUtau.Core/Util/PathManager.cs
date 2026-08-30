using System;
using System.Collections.Generic;
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
                _dataPath = Path.Combine(userHome, "Library", "OpenUtau");
                CachePath = Path.Combine(userHome, "Library", "Caches", "OpenUtau");
                HomePathIsAscii = true;
                try {
                    // Deletes old cache.
                    string oldCache = Path.Combine(_dataPath, "Cache");
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
                _dataPath = Path.Combine(dataHome, "OpenUtau");
                string cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(cacheHome)) {
                    cacheHome = Path.Combine(userHome, ".cache");
                }
                CachePath = Path.Combine(cacheHome, "OpenUtau");
                HomePathIsAscii = true;
            } else {
                string exePath = Path.GetDirectoryName(Environment.ProcessPath);
                IsInstalled = File.Exists(Path.Combine(exePath, "installed.txt"));
                if (!IsInstalled) {
                    _dataPath = exePath;
                } else {
                    string dataHome = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                    _dataPath = Path.Combine(dataHome, "OpenUtau");
                }
                CachePath = Path.Combine(_dataPath, "Cache");
                HomePathIsAscii = true;
                var etor = StringInfo.GetTextElementEnumerator(_dataPath);
                while (etor.MoveNext()) {
                    string s = etor.GetTextElement();
                    if (s.Length != 1 || s[0] >= 128) {
                        HomePathIsAscii = false;
                        break;
                    }
                }
            }
        }

        private string _dataPath;
        public string RootPath { get; }
        public string CachePath { get; }
        public bool HomePathIsAscii { get; }
        public bool IsInstalled { get; }
        public string CustomDataPath => Preferences.Default.CustomDataPath;
        public string DataPath => string.IsNullOrEmpty(CustomDataPath) ? _dataPath : CustomDataPath;
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
        public string ThemesPath => Path.Combine(DataPath, "Themes");
        public string LogsPath => Path.Combine(_dataPath, "Logs");
        public string LogFilePath => Path.Combine(_dataPath, "Logs", "log.txt");
        public string PrefsFilePath => Path.Combine(_dataPath, "prefs.json");
        public string NotePresetsFilePath => Path.Combine(_dataPath, "notepresets.json");
        public string BackupsPath => Path.Combine(_dataPath, "Backups");

        public void ValidatePaths() {
            // Default data path authorization check
            var tempPath = Path.Combine(_dataPath, "temp.txt");
            try {
                File.WriteAllText(tempPath, "default data directory");
            } catch (Exception e) {
                Preferences.LoadingErrors.Add(new MessageCustomizableException($"Cannot access default data directory: {_dataPath}", $"<translate:startuperror.defaultdatapath.unauthorized>: {_dataPath}", e));
            } finally {
                File.Delete(tempPath);
            }

            // Cloud folder check
            try {
                List<string> paths = new List<string> { _dataPath, Path.GetDirectoryName(Environment.ProcessPath)! };
                if (!string.IsNullOrEmpty(CustomDataPath)) {
                    paths.Add(CustomDataPath);
                }
                paths = paths.Distinct().ToList();
                foreach (var path in paths) {
                    string fullPath = Path.GetFullPath(path);
                    string lowerPath = fullPath.ToLowerInvariant();
                    if (lowerPath.Contains("onedrive") ||
                        lowerPath.Contains("googledrive") || lowerPath.Contains("google drive") ||
                        lowerPath.Contains("dropbox") ||
                        lowerPath.Contains("mobile documents")) { // iCloud
                        Preferences.LoadingErrors.Add(new MessageCustomizableException($"This folder may be located on a cloud storage: {fullPath}", $"<translate:startuperror.cloudfolder>: {fullPath}", new ArgumentException()));
                    } else {
                        DriveInfo drive = new DriveInfo(Path.GetPathRoot(path)!);
                        if (drive.VolumeLabel.Contains("Google Drive")) {
                            Preferences.LoadingErrors.Add(new MessageCustomizableException($"This folder may be located on a cloud storage: {fullPath}", $"<translate:startuperror.cloudfolder>: {fullPath}", new ArgumentException()));
                        }
                    }
                }
            } catch { }
        }

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

        public void SetCustomDataPath(string path) {
            if (string.IsNullOrWhiteSpace(path) || path == _dataPath) {
                Preferences.Default.CustomDataPath = string.Empty;
                Preferences.Save();
                return;
            }
            try {
                File.WriteAllText(Path.Combine(path, "temp.txt"), "custom data directory");
                Preferences.Default.CustomDataPath = path;
                Preferences.Save();
            } finally {
                File.Delete(Path.Combine(path, "temp.txt"));
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
