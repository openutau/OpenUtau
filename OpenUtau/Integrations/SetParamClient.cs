using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Integrations {
    internal static class SetParamClient {
        public static bool IsAvailable {
            get {
                string path = Preferences.Default.SetParamPath;
                return !string.IsNullOrEmpty(path) && File.Exists(path);
            }
        }

        /// <summary>
        /// Opens the given oto in setParam. Writing the temp file and starting the process
        /// happen synchronously, so failures throw to the caller. The returned task completes
        /// after setParam exits and a reload of the singer has been scheduled.
        /// </summary>
        public static async Task OpenAsync(UProject project, USinger singer, UOto oto) {
            string path = Preferences.Default.SetParamPath;
            var tempFile = Path.Combine(PathManager.Inst.CachePath, "temp.tmp");
            Ust.WriteForSetParam(project, tempFile, new List<UOto> { oto });

            var startInfo = new ProcessStartInfo() {
                FileName = path,
                Arguments = $"\"{tempFile}\"",
                WorkingDirectory = Path.GetDirectoryName(path)
            };
            using var process = Process.Start(startInfo);
            if (process == null) {
                return;
            }
            await process.WaitForExitAsync();
            // setParam may have rewritten oto.ini.
            SingerManager.Inst.ScheduleReload(singer);
        }
    }
}
