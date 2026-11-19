using System.IO;
using System.Text;
using System.Threading.Tasks;
using OpenUtau;
using OpenUtau.Core;

namespace Classic {
    public enum ExeType { resampler, wavtool }
    public class ExeInstaller {
        public static void Install(string filePath, ExeType exeType) {
            string fileName = Path.GetFileName(filePath);

            string destPath = exeType == ExeType.wavtool
                ? PathManager.Inst.WavtoolsPath
                : PathManager.Inst.ResamplersInstallPath;
            string destName = Path.Combine(destPath, fileName);
            File.Copy(filePath, destName, true);

            new Task(() => {
                DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Installed {fileName}"));
            }).Start(DocManager.Inst.MainScheduler);
        }
    }
}
