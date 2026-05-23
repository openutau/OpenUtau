using System.IO;
using System.Threading.Tasks;

namespace OpenUtau.Core.Vogen {
    public class VogenSingerInstaller {
        public const string FileExt = ".vogeon";
        public static void Install(string filePath) {
            string fileName = Path.GetFileName(filePath);
            string destName = Path.Combine(PathManager.Inst.SingersInstallPath, fileName);
            if (File.Exists(destName)) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification($"{destName} already exist!"));
                return;
            }
            File.Copy(filePath, destName);
            new Task(() => {
                DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Installed {fileName}"));
            }).Start(DocManager.Inst.MainScheduler);
        }
    }
}
