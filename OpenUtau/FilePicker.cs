using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OpenUtau.Core;
using OpenUtau.Core.Util;

namespace OpenUtau.App {
    internal class FilePicker {
        public static FilePickerFileType ProjectFiles { get; } = new("Project Files") {
            Patterns = new[] { "*.ustx", "*.vsqx", "*.ust", "*.mid", "*.midi", "*.ufdata", "*.musicxml" },
        };
        public static FilePickerFileType USTX { get; } = new("USTX") {
            Patterns = new[] { "*.ustx" },
        };
        public static FilePickerFileType VSQX { get; } = new("VSQX") {
            Patterns = new[] { "*.vsqx" },
        };
        public static FilePickerFileType UST { get; } = new("UST") {
            Patterns = new[] { "*.ust" },
        };
        public static FilePickerFileType MIDI { get; } = new("MIDI") {
            Patterns = new[] { "*.mid", "*.midi" },
        };
        public static FilePickerFileType UFDATA { get; } = new("UFDATA") {
            Patterns = new[] { "*.ufdata" },
        };
        public static FilePickerFileType MUSICXML { get; } = new("MUSICXML") {
            Patterns = new[] { "*.musicxml" },
        };
        public static FilePickerFileType AudioFiles { get; } = new("Audio Files") {
            Patterns = new[] { "*.wav", "*.mp3", "*.ogg", "*.opus", "*.flac", "*.m4a" },
        };
        public static FilePickerFileType WAV { get; } = new("WAV") {
            Patterns = new[] { "*.wav" },
        };
        public static FilePickerFileType ArchiveFiles { get; } = new("Archive File") {
            Patterns = new[] { "*.zip", "*.rar", "*.uar", "*.vogeon", "*.oudep" },
        };
        public static FilePickerFileType ZIP { get; } = new("ZIP") {
            Patterns = new[] { "*.zip" },
        };
        public static FilePickerFileType EXE { get; } = new("EXE") {
            Patterns = new[] { "*.exe" },
        };
        public static FilePickerFileType APP { get; } = new("APP") {
            Patterns = new[] { "*.app" },
        };
        public static FilePickerFileType PrefixMap { get; } = new("Prefix Map") {
            Patterns = new[] { "*.map" },
        };
        public static FilePickerFileType DS { get; } = new("DS") {
            Patterns = new[] { "*.ds" },
        };
        public static FilePickerFileType OUDEP { get; } = new("OpenUtau dependency") {
            Patterns = new[] { "*.oudep" },
        };
        public static FilePickerFileType UnixExecutable { get; } = new("Executable") {
            MimeTypes = new[] { "application/x-executable" },
            AppleUniformTypeIdentifiers = new[] { "public.unix-executable" },
        };

        public async static Task<string?> OpenFile(
            Window window, string titleKey, params FilePickerFileType[] types) {
            return await OpenFile(window, titleKey, null, types);
        }

        public async static Task<string?> OpenFileAboutSinger(
            Window window, string titleKey, params FilePickerFileType[] types) {
            var path = await OpenFile(window, titleKey, Preferences.Default.RecentOpenSingerDirectory, types);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) {
                Preferences.Default.RecentOpenSingerDirectory = dir;
                Preferences.Save();
            }
            return path;
        }

        public async static Task<string?> OpenFileAboutProject(
            Window window, string titleKey, params FilePickerFileType[] types) {
            var path = await OpenFile(window, titleKey, Preferences.Default.RecentOpenProjectDirectory, types);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) {
                Preferences.Default.RecentOpenProjectDirectory = dir;
                Preferences.Save();
            }
            return path;
        }

        public async static Task<string?> OpenFile(
            Window window, string titleKey, string? startLocation, params FilePickerFileType[] types) {
            var location = startLocation == null
                ? null
                : await window.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var files = await window.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions() {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = false,
                    FileTypeFilter = types,
                    SuggestedStartLocation = location,
                });
            return files
                ?.Select(f => f.TryGetLocalPath())
                ?.OfType<string>()
                ?.FirstOrDefault();
        }

        public async static Task<string[]?> OpenFilesAboutProject(
                Window window, string titleKey, params FilePickerFileType[] types) {
            var result = await OpenFiles(window, titleKey, Preferences.Default.RecentOpenProjectDirectory, types);
            if (result != null) {
                var dir = Path.GetDirectoryName(result.FirstOrDefault());
                if (dir != null) {
                    Preferences.Default.RecentOpenProjectDirectory = dir;
                    Preferences.Save();
                }
            }
            return result;
        }

        public async static Task<string[]?> OpenFiles(
            Window window, string titleKey, string? startLocation, params FilePickerFileType[] types) {
            var location = startLocation == null
                ? null
                : await window.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var files = await window.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions() {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = true,
                    FileTypeFilter = types,
                    SuggestedStartLocation = location
                });
            return files
                ?.Select(f => f.TryGetLocalPath())
                ?.OfType<string>()
                ?.ToArray();
        }

        public async static Task<string?> OpenFolderAboutSinger(Window window, string titleKey) {
            var dir = await OpenFolder(window, titleKey, Preferences.Default.RecentOpenSingerDirectory);
            if (dir != null) {
                Preferences.Default.RecentOpenSingerDirectory = dir;
                Preferences.Save();
            }
            return dir;
        }

        public async static Task<string?> OpenFolder(Window window, string titleKey, string? startLocation) {
            var location = startLocation == null
                ? null
                : await window.StorageProvider.TryGetFolderFromPathAsync(startLocation);
            var dirs = await window.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions {
                    Title = ThemeManager.GetString(titleKey),
                    AllowMultiple = false,
                    SuggestedStartLocation = location
                });
            return dirs
                ?.Select(f => f.TryGetLocalPath())
                ?.OfType<string>()
                ?.FirstOrDefault();
        }

        public async static Task<string?> SaveFile
            (Window window, string titleKey, params FilePickerFileType[] types) {
            return await SaveFile(window, titleKey, null, null, types);
        }

        public async static Task<string?> SaveFileAboutProject
            (Window window, string titleKey, params FilePickerFileType[] types) {
            var path = await SaveFile(window, titleKey, Preferences.Default.RecentOpenProjectDirectory, 
                Path.GetFileName(Path.ChangeExtension(DocManager.Inst.Project.FilePath, null)), types);
            var dir = Path.GetDirectoryName(path);
            if (dir != null) {
                Preferences.Default.RecentOpenProjectDirectory = dir;
                Preferences.Save();
            }
            return path;
        }

        public async static Task<string?> SaveFile
            (Window window, string titleKey, string? startLocation, string? filename, params FilePickerFileType[] types) {
            var location = startLocation == null
                ? null
                : await window.StorageProvider.TryGetFolderFromPathAsync(startLocation);

            var file = await window.StorageProvider.SaveFilePickerAsync(
                 new FilePickerSaveOptions {
                     Title = ThemeManager.GetString(titleKey),
                     FileTypeChoices = types,
                     ShowOverwritePrompt = true,
                     SuggestedStartLocation = location,
                     SuggestedFileName = filename,
                 });
            return file?.TryGetLocalPath();
        }
    }
}
