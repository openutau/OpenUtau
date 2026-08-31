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
using System.Text.RegularExpressions;
using System;

namespace OpenUtau.Classic {
    class ExeWavtool : IWavtool {
        static object tempBatLock = new object();

        readonly StringBuilder sb = new StringBuilder();
        readonly string filePath;
        readonly string name;
        readonly string winePath;
        readonly bool useWine;
        private Encoding osEncoding;

        public ExeWavtool(string filePath, string basePath) {
            this.filePath = filePath;
            name = Path.GetRelativePath(basePath, filePath);
            osEncoding = OS.IsWindows() ? Encoding.GetEncoding(0) : Encoding.UTF8;
            winePath = Preferences.Default.WinePath;
            useWine = !string.IsNullOrEmpty(winePath);
        }

        public float[] Concatenate(List<ResamplerItem> resamplerItems, string tempPath, CancellationTokenSource cancellation) {
            if (cancellation.IsCancellationRequested) {
                return null;
            }
            //The builtin worldline and Linux/MacOS resamplers can't be
            //called from bat script, so we need to call it directly from C#
            foreach(var item in resamplerItems){
                if(item.resampler.CallDirectly && !cancellation.IsCancellationRequested && !File.Exists(item.outputFile)){
                    lock (Renderers.GetCacheLock(item.outputFile)) {
                        item.resampler.DoResamplerReturnsFile(item, Log.Logger);
                    }
                }
            }
            PrepareHelper();
            string batPath = Path.Combine(PathManager.Inst.CachePath, "temp.bat");
            lock (tempBatLock) {
                using (var stream = File.Open(batPath, FileMode.Create)) {
                    UTF8Encoding noBomEncoding = new UTF8Encoding(false);
                    using (var writer = new StreamWriter(stream, OS.IsWindows() ? osEncoding : noBomEncoding)) {
                        WriteSetUp(writer, resamplerItems, tempPath);
                        for (var i = 0; i < resamplerItems.Count; i++) {
                            WriteItem(writer, resamplerItems[i], i, resamplerItems.Count);
                        }
                        WriteTearDown(writer);
                    }
                }

                if (useWine) {
                    ProcessRunner.Run(winePath, batPath, Log.Logger, workDir: PathManager.Inst.CachePath, timeoutMs: 5 * 60 * 1000);
                } else {
                    ProcessRunner.Run(batPath, "", Log.Logger, workDir: PathManager.Inst.CachePath, timeoutMs: 5 * 60 * 1000);
                }

            }
            if (string.IsNullOrEmpty(tempPath) || File.Exists(tempPath)) {
                using (var wavStream = Core.Format.Wave.OpenFile(tempPath)) {
                    return Core.Format.Wave.GetSamples(wavStream.ToSampleProvider().ToMono(1, 0));
                }
            }
            return new float[0];
        }

        void PrepareHelper() {
            string tempHelper = Path.Join(PathManager.Inst.CachePath, "temp_helper.bat");
            lock (Renderers.GetCacheLock(tempHelper)) {
                if (!File.Exists(tempHelper)) {
                    using (var stream = File.Open(tempHelper, FileMode.Create)) {
                        //BOM also causes problems when running .bat files through wine
                        UTF8Encoding noBomEncoding = new UTF8Encoding(false);
                        using (var writer = new StreamWriter(stream, OS.IsWindows() ? osEncoding : noBomEncoding)) {
                            WriteHelper(writer);
                        }
                    }
                }
            }
        }

        void WriteHelper(StreamWriter writer) {
            // writes temp_helper.bat
            writer.WriteLine("@if exist %temp% goto A");
            writer.WriteLine("@\"%resamp%\" %1 %temp% %2 %vel% %flag% %5 %6 %7 %8 %params%");
            writer.WriteLine(":A");
            writer.WriteLine("@\"%tool%\" \"%output%\" %temp% %stp% %3 %env%");
        }

        void WriteSetUp(StreamWriter writer, List<ResamplerItem> resamplerItems, string tempPath) {
            string globalFlags = "";

            writer.WriteLine("@rem project=");
            writer.WriteLine("@set loadmodule=");
            writer.WriteLine($"@set tempo={resamplerItems[0].tempo}");
            writer.WriteLine($"@set samples={44100}");
            writer.WriteLine($"@set oto={ConvertIfNeeded(PathManager.Inst.CachePath)}");
            writer.WriteLine($"@set tool={ConvertIfNeeded(filePath)}");
            string tempFile = Path.GetRelativePath(PathManager.Inst.CachePath, tempPath);
            writer.WriteLine($"@set output={ConvertIfNeeded(tempFile)}");
            writer.WriteLine("@set helper=temp_helper.bat");
            writer.WriteLine($"@set cachedir={ConvertIfNeeded(PathManager.Inst.CachePath)}");
            writer.WriteLine($"@set flag=\"{globalFlags}\"");
            writer.WriteLine("@set env=0 5 35 0 100 100 0");
            writer.WriteLine("@set stp=0");
            writer.WriteLine("");
            writer.WriteLine("@del \"%output%\" 2>nul");
            writer.WriteLine("@mkdir \"%cachedir%\" 2>nul");
            writer.WriteLine("");
        }

        void WriteItem(StreamWriter writer, ResamplerItem item, int index, int total) {
            writer.WriteLine($"@set resamp={ConvertIfNeeded(item.resampler.FilePath)}");
            writer.WriteLine($"@set params={item.volume} {item.modulation} !{item.tempo:G999} {Base64.Base64EncodeInt12(item.pitches)}");
            // fixed the commandline vulnerabilities that also exists in og utau
            writer.WriteLine($"@set flag=\"{EscapeFlags(item.GetFlagsString())}\"");
            writer.WriteLine($"@set env={GetEnvelope(item)}");
            writer.WriteLine($"@set stp={item.skipOver}");
            writer.WriteLine($"@set vel={item.velocity}");
            string relOutputFile = Path.GetRelativePath(PathManager.Inst.CachePath, item.outputFile);
            writer.WriteLine($"@set temp=\"%cachedir%\\{ConvertIfNeeded(relOutputFile)}\"");
            string toneName = MusicMath.GetToneName(item.tone);
            string dur = $"{item.phone.duration:G999}@{item.phone.adjustedTempo:G999}{(item.durCorrection >= 0 ? "+" : "")}{item.durCorrection}";
            string relInputTemp = Path.GetRelativePath(PathManager.Inst.CachePath, item.inputTemp);
            writer.WriteLine($"@echo {MakeProgressBar(index + 1, total)}");
            if(item.phone.direct){
                writer.WriteLine($"@\"%tool%\" \"%output%\" \"%oto%\\{ConvertIfNeeded(relInputTemp)}\" {item.offset} {item.phone.durationMs:F1} %env%");
            } else {
                writer.WriteLine($"@call %helper% \"%oto%\\{ConvertIfNeeded(relInputTemp)}\" {toneName} {dur} {item.preutter} {item.offset} {item.durRequired} {item.consonant} {item.cutoff} {index}");
            }
        }

        string MakeProgressBar(int index, int total) {
            const int kWidth = 40;
            int fill = index * kWidth / total;
            return $"{new string('#', fill)}{new string('-', kWidth - fill)}({index}/{total})";
        }

        private static string EscapeFlags(string flag) {
            // Remove special characters
            flag = Regex.Replace(flag, @"[&/\\|<>\""'!:#\n\t].", "");

            // Remove specific file extensions (case-insensitive)
            string[] extensions = {
                ".exe", ".py", ".app", ".bat", ".cmd", ".sh", ".vbs", ".js", ".wsf", ".msi", ".com", ".pif",
                ".scr", ".hta", ".cpl", ".jar", ".ps1", ".psm1", ".msh", ".msh1", ".msh2", ".mshxml", ".msh1xml", ".msh2xml"
            };
            foreach (var ext in extensions) {
                if (flag.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) {
                    flag = flag.Substring(0, flag.Length - ext.Length);
                }
            }
            // Remove "https://" case-insensitively
            if (flag.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
                flag = flag.Substring(8);
            }
            else if (flag.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) {
                flag = flag.Substring(7);
            }
            return flag;
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

        void WriteTearDown(StreamWriter writer) {
            writer.WriteLine("@if not exist \"%output%.whd\" goto E");
            writer.WriteLine("@if not exist \"%output%.dat\" goto E");
            writer.WriteLine("copy /Y \"%output%.whd\" /B + \"%output%.dat\" /B \"%output%\"");
            writer.WriteLine("del \"%output%.whd\"");
            writer.WriteLine("del \"%output%.dat\"");
            writer.WriteLine(":E");
        }

        string ConvertIfNeeded(string path) {
            if (!OS.IsWindows()) return ConvertToWindowsPath(path);
            else return path;
        } 

        string ConvertToWindowsPath (string linuxPath) {
            List<char> path = new List<char>(linuxPath.ToCharArray());
            bool absolutePath = false;
            if (path[0] == '/') absolutePath = true;
            for (int i = path.Count - 1; i > 0; i--) {
                if (path[i] == ' ' && path[i-1] == '\\') {
                    path.RemoveAt(i-1);
                    i--;
                }
            }
            for (int i = 0; i < path.Count; i++) {
                if (path[i] == '/') path[i] = '\\';
            }
            string windowsPath = new string(path.ToArray());
            if (absolutePath) windowsPath = "Z:" + windowsPath;
            return windowsPath;
        }

        public void CheckPermissions() { }

        public override string ToString() => name;
    }
}
