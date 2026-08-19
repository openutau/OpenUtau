using System.Diagnostics;
using System.IO;
using OpenUtau.Core.Util;

namespace OpenUtau.Classic {
    public class Plugin : IPlugin {
        public string Name;
        public string Executable;
        public bool AllNotes;
        public bool UseShell;
        private string encoding = "shift_jis";
        public string Shortcut;

        public string Encoding { get => encoding; set => encoding = value; }

        public void Run(string tempFile) {
            if (!File.Exists(Executable)) {
                throw new FileNotFoundException($"Executable {Executable} not found.");
            }
            string winePath = Preferences.Default.WinePath;
            string ext = Path.GetExtension(Executable).ToLowerInvariant();
            string validatedWinePath = string.Empty;
            bool useWine = !OS.IsWindows()
                && !string.IsNullOrEmpty(winePath)
                && (ext == ".exe" || ext == ".bat")
                && Path.IsPathRooted(winePath)
                && File.Exists(winePath);
            if (useWine) {
                validatedWinePath = Path.GetFullPath(winePath);
            }
            var startInfo = new ProcessStartInfo() {
                FileName = useWine ? validatedWinePath : Executable,
                Arguments = useWine ? $"\"{Executable}\" \"{tempFile}\"" : $"\"{tempFile}\"",
                Environment = {{"LANG", "ja_JP.utf8"}},
                WorkingDirectory = Path.GetDirectoryName(Executable),
                UseShellExecute = UseShell,
            };
            using (var process = Process.Start(startInfo)) {
                process.WaitForExit();
            }
        }

        public override string ToString() => Name;
    }
}
