using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Classic {
    class UnixWavtool : IWavtool {
        readonly StringBuilder sb = new StringBuilder();
        readonly string filePath;
        readonly string name;

        public UnixWavtool(string filePath, string basePath) {
            this.filePath = filePath;
            name = Path.GetRelativePath(basePath, filePath);
        }

        public float[] Concatenate(List<ResamplerItem> resamplerItems, string tempPath, CancellationTokenSource cancellation) {
            if (cancellation.IsCancellationRequested) {
                return null;
            }

            File.Delete(tempPath);

            foreach(var item in resamplerItems){
                if(!File.Exists(item.outputFile)){
                    lock (Renderers.GetCacheLock(item.outputFile)) {
                        item.resampler.DoResamplerReturnsFile(item, Log.Logger);
                    }
                }
                
                string parameters = GenerateParameters(item, tempPath);

                ProcessRunner.Run(filePath, parameters, Log.Logger, workDir: PathManager.Inst.CachePath, timeoutMs: 5 * 60 * 1000);
            }

            string whdFile = tempPath + ".whd";
            string datFile = tempPath + ".dat";

            if (File.Exists(whdFile) && File.Exists(datFile)) {
                using (var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    foreach (string file in new[] { whdFile, datFile })
                    {
                        using (var inStream = new FileStream(file, FileMode.Open, FileAccess.Read))
                        {
                            inStream.CopyTo(outStream);
                        }
                    }
                }

                File.Delete(whdFile);
                File.Delete(datFile);
            }

            if (string.IsNullOrEmpty(tempPath) || File.Exists(tempPath)) {
                using (var wavStream = Core.Format.Wave.OpenFile(tempPath)) {
                    return Core.Format.Wave.GetSamples(wavStream.ToSampleProvider().ToMono(1, 0));
                }
            }
            return new float[0];
        }

        string GenerateParameters(ResamplerItem item, string tempPath) {
            string envelope = GetEnvelope(item);
            string dur = $"{item.phone.duration:G999}@{item.phone.adjustedTempo:G999}{(item.durCorrection >= 0 ? "+" : "")}{item.durCorrection}";

            if (item.phone.direct) {
                return $"\"{tempPath}\" \"{item.outputFile}\" {item.offset} {item.phone.durationMs:F1} {envelope}";
            }
            
            return $"\"{tempPath}\" \"{item.outputFile}\" {item.skipOver} {dur} {envelope}";
        }

        string GetEnvelope(ResamplerItem item) {
            var env = item.phone.envelope;
            sb.Clear()
                .Append(env[0].X - env[0].X).Append(' ')
                .Append(env[1].X - env[0].X).Append(' ')
                .Append(env[4].X - env[3].X).Append(' ')
                .Append(env[0].Y).Append(' ')
                .Append(env[1].Y).Append(' ')
                .Append(env[3].Y).Append(' ')
                .Append(env[4].Y).Append(' ')
                .Append(item.overlap).Append(' ')
                .Append(env[4].X - env[4].X).Append(' ')
                .Append(env[2].X - env[1].X).Append(' ')
                .Append(env[2].Y);
            return sb.ToString();
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int chmod(string pathname, int mode);

        public void CheckPermissions() {
            if (OS.IsWindows() || !File.Exists(filePath)) {
                return;
            }
            int mode = (7 << 6) | (5 << 3) | 5;
            chmod(filePath, mode);
        }

        public override string ToString() => name;
    }
}
