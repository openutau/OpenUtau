using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Classic {
    public class VoicebankFiles : Core.Util.SingletonBase<VoicebankFiles> {
        public string GetSourceTempPath(string singerId, UOto oto, string ext = null) {
            if (string.IsNullOrEmpty(ext)) {
                ext = Path.GetExtension(oto.File);
            }
            return Path.Combine(PathManager.Inst.CachePath,
                $"src-{HashHex(singerId)}-{HashHex(oto.Set)}-{HashHex(oto.File)}{ext}");
        }

        private string HashHex(string s) {
            return $"{XXH32.DigestOf(Encoding.UTF8.GetBytes(s)):x8}";
        }

        public void CopySourceTemp(string source, string temp) {
            lock (Renderers.GetCacheLock(temp)) {
                DecodeOrStamp(source, temp);
                var metaFiles = GetMetaFiles(source, temp);
                metaFiles.ForEach(t => CopyOrStamp(t.Item1, t.Item2, false));
            }
        }

        public void CopyBackMetaFiles(string source, string temp) {
            lock (Renderers.GetCacheLock(temp)) {
                var metaFiles = GetMetaFiles(source, temp);
                metaFiles.ForEach(t => CopyOrStamp(t.Item2, t.Item1, false));
            }
        }

        private List<Tuple<string, string>> GetMetaFiles(string source, string sourceTemp) {
            string ext = Path.GetExtension(source);
            string noExt = source.Substring(0, source.Length - ext.Length);
            string frqExt = ext.Replace('.', '_') + ".frq";
            string tempExt = Path.GetExtension(sourceTemp);
            string tempNoExt = sourceTemp.Substring(0, sourceTemp.Length - ext.Length);
            string tempFrqExt = tempExt.Replace('.', '_') + ".frq";
            return new List<Tuple<string, string>>() {
                Tuple.Create(noExt + frqExt, tempNoExt + tempFrqExt),
                Tuple.Create(source + ".llsm", sourceTemp + ".llsm"),
                Tuple.Create(source + ".uspec", sourceTemp + ".uspec"),
                Tuple.Create(source + ".dio", sourceTemp + ".dio"),
                Tuple.Create(source + ".star", sourceTemp + ".star"),
                Tuple.Create(source + ".platinum", sourceTemp + ".platinum"),
                Tuple.Create(source + ".frc", sourceTemp + ".frc"),
                Tuple.Create(source + ".pmk", sourceTemp + ".pmk"),
                Tuple.Create(source + ".vs4ufrq", sourceTemp + ".vs4ufrq"),
                Tuple.Create(noExt + ".rudb", tempNoExt + ".rudb"),
                Tuple.Create(noExt + ".sc.npz", tempNoExt + ".sc.npz"),
                Tuple.Create(noExt + ".sc", tempNoExt + ".sc"),
                Tuple.Create(noExt + ".hifi.npz", tempNoExt + ".hifi.npz"),
                //Tuple.Create(noExt + ".lessaudio", tempNoExt + ".lessaudio"),
            };
        }

        private void CopyOrStamp(string source, string dest, bool required) {
            if (!File.Exists(source)) {
                if (required) {
                    Log.Error($"Source file {source} not found");
                    throw new FileNotFoundException($"Source file {source} not found");
                }
            } else if (!File.Exists(dest)) {
                Log.Verbose($"Copy {source} to {dest}");
                File.Copy(source, dest);
            }
        }

        private void DecodeOrStamp(string source, string dest) {
            if (!File.Exists(source)) {
                Log.Error($"Source file {source} not found");
                throw new FileNotFoundException($"Source file {source} not found");
            }
            if (File.Exists(dest)) {
                return;
            }
            if (Path.GetExtension(source) == ".wav") {
                Log.Verbose($"Copy {source} to {dest}");
                File.Copy(source, dest);
                return;
            }
            Log.Verbose($"Decode {source} to {dest}");
            using (var outputStream = new FileStream(dest, FileMode.Create)) {
                using (var waveStream = Core.Format.Wave.OpenFile(source)) {
                    WaveFileWriter.WriteWavFileToStream(outputStream, waveStream);
                }
            }
        }

        public void ReleaseSourceTemp() {
            var expire = DateTime.Now - TimeSpan.FromDays(7);
            string path = PathManager.Inst.CachePath;
            Log.Information($"ReleaseSourceTemp {path}");
            Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(file =>
                    !File.GetAttributes(file).HasFlag(FileAttributes.Directory)
                        && File.GetCreationTime(file) < expire)
                .ToList()
                .ForEach(file => File.Delete(file));
        }

        public static string GetFrqFile(string source) {
            string ext = Path.GetExtension(source);
            string noExt = source.Substring(0, source.Length - ext.Length);
            string frqExt = ext.Replace('.', '_') + ".frq";
            return noExt + frqExt;
        }
        public static string GetMrqFile(string source) {
            return Path.Combine(Path.GetDirectoryName(source), "desc.mrq");
        }
    }
}
