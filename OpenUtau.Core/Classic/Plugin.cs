using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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

        public async Task Run(string tempFile) {
            if (!File.Exists(Executable)) {
                throw new FileNotFoundException($"Executable {Executable} not found.");
            }
            string winePath = Preferences.Default.WinePath;
            string ext = Path.GetExtension(Executable).ToLower();
            bool useWine = !OS.IsWindows() && !string.IsNullOrEmpty(winePath) && ( ext == ".exe" || ext == ".bat");
            var startInfo = new ProcessStartInfo() {
                WorkingDirectory = Path.GetDirectoryName(Executable),
            };
            if (useWine) {
                startInfo.FileName = winePath;
                startInfo.Arguments = $"\"{Executable}\" \"{tempFile}\"";
                startInfo.UseShellExecute = false;
                startInfo.Environment.Add("LANG", "ja_JP.utf8");
            } else {
                startInfo.FileName = Executable;
                startInfo.Arguments = $"\"{tempFile}\"";
                startInfo.UseShellExecute = UseShell;
            }
            using (var process = Process.Start(startInfo)) {
                await process.WaitForExitAsync();
            }
        }

        public override string ToString() => Name;
    }
}
